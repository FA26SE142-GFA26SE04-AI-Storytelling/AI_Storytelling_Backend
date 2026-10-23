using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class MediaGenerationServiceTests
{
    private sealed class SwitchableImage : IImageGenerationProvider
    {
        public bool Fail { get; set; }
        public bool Permanent { get; set; }
        public int Calls { get; private set; }
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification spec, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail)
            {
                if (Permanent) throw new PermanentMediaGenerationException("IMAGE_CONFIGURATION_INVALID");
                throw new TransientMediaGenerationException("GEMINI_IMAGE_HTTP_429", retryAfter: TimeSpan.FromSeconds(15));
            }
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
    }

    [Fact]
    public async Task ImageBackoffDoesNotBlockAudioAndRetainsProviderRetryAfter()
    {
        var store = Seed();
        var image = new SwitchableImage { Fail = true };
        var service = Create(store, image: image);
        Assert.True((await service.ProcessNextAsync()).Success);
        Assert.Empty(store.Items<MediaAsset>());
        Assert.Equal(3, store.Items<StoryGenerationJob>().Count);
        var before = DateTime.UtcNow;
        Assert.False((await service.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        Assert.True((await service.ProcessBranchNextAsync(MediaType.TtsAudio)).Success);
        var progress = await service.GetProgressAsync(1, 1);
        Assert.Equal("Pending", progress.Images.Status);
        Assert.Equal("GEMINI_IMAGE_HTTP_429", progress.Images.ErrorCode);
        Assert.True(progress.Images.NextRetryAt >= before.AddSeconds(14));
        Assert.Equal("Completed", progress.Audio.Status);
        Assert.Equal(2, progress.ReadyAudio);
        Assert.False(progress.IsReady);
        Assert.False((await service.ProcessBranchNextAsync(MediaType.Illustration)).JobFound);
        image.Fail = false;
        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(2, store.Items<MediaAsset>().Count(x => x.Type == MediaType.TtsAudio));
        Assert.Equal(StoryStatus.MediaReview, store.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task PermanentImageFailureLeavesAudioCompleteAndRetryOnlyGeneratesMissingImages()
    {
        var store = Seed();
        var image = new SwitchableImage { Fail = true, Permanent = true };
        var tts = new RecordingTtsProvider();
        var service = Create(store, image: image, tts: tts);
        Assert.False((await service.RunPackageAsync()).Success);
        var audio = store.Items<MediaAsset>().Where(x => x.Type == MediaType.TtsAudio).Select(x => (x.Id, x.Url)).ToArray();
        Assert.Equal(2, tts.Inputs.Count);
        Assert.Equal("Failed", (await service.GetProgressAsync(1, 1)).Images.Status);
        image.Fail = false;
        await service.RetryAsync(1, 1);
        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(2, tts.Inputs.Count);
        Assert.Equal(audio, store.Items<MediaAsset>().Where(x => x.Type == MediaType.TtsAudio).Select(x => (x.Id, x.Url)).ToArray());
        Assert.Equal(3, store.Items<StoryGenerationJob>().Count);
    }

    [Theory]
    [InlineData(MediaType.Illustration)]
    [InlineData(MediaType.TtsAudio)]
    public async Task RetryWaitsForCoordinatorBeforeBranchWorkersCanRun(MediaType firstWorker)
    {
        var store = Seed();
        var image = new SwitchableImage { Fail = true, Permanent = true };
        var tts = new RecordingTtsProvider();
        var service = Create(store, image: image, tts: tts);
        Assert.False((await service.RunPackageAsync()).Success);
        var audio = store.Items<MediaAsset>().Where(x => x.Type == MediaType.TtsAudio)
            .Select(x => (x.Id, x.Url)).ToArray();
        var imageCalls = image.Calls;
        image.Fail = false;

        await service.RetryAsync(1, 1);
        // Reproduce independent worker scheduling: both branches poll before preparation.
        Assert.False((await service.ProcessBranchNextAsync(firstWorker)).JobFound);
        var otherWorker = firstWorker == MediaType.Illustration ? MediaType.TtsAudio : MediaType.Illustration;
        Assert.False((await service.ProcessBranchNextAsync(otherWorker)).JobFound);
        Assert.DoesNotContain(store.Items<StoryGenerationJob>(), x => x.Status == GenerationJobStatus.Cancelled);
        Assert.Equal(imageCalls, image.Calls);
        Assert.Equal(2, tts.Inputs.Count);

        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(StoryStatus.MediaReview, store.Items<Story>().Single().Status);
        Assert.All(store.Items<StoryGenerationJob>(), x => Assert.Equal(GenerationJobStatus.Completed, x.Status));
        Assert.Equal(2, tts.Inputs.Count);
        Assert.Equal(audio, store.Items<MediaAsset>().Where(x => x.Type == MediaType.TtsAudio)
            .Select(x => (x.Id, x.Url)).ToArray());
    }

    [Theory]
    [InlineData(GenerationJobStatus.Pending)]
    [InlineData(GenerationJobStatus.Processing)]
    public async Task BranchWorkerLeavesQueuedWorkUntouchedWhileParentIsPreparing(GenerationJobStatus parentStatus)
    {
        var store = Seed();
        var image = new SwitchableImage();
        var tts = new RecordingTtsProvider();
        var service = Create(store, image: image, tts: tts);
        await service.ProcessNextAsync();
        var parent = store.Items<StoryGenerationJob>().Single(x => x.ParentJobId == null);
        var contextId = parent.MediaContextId;
        parent.Status = parentStatus;
        parent.MediaContextId = null;
        var branches = store.Items<StoryGenerationJob>().Where(x => x.ParentJobId == parent.Id)
            .Select(x => (x.Id, x.Status, x.ConcurrencyToken, x.AttemptNo)).ToArray();

        Assert.False((await service.ProcessBranchNextAsync(MediaType.Illustration)).JobFound);
        Assert.False((await service.ProcessBranchNextAsync(MediaType.TtsAudio)).JobFound);
        Assert.Equal(branches, store.Items<StoryGenerationJob>().Where(x => x.ParentJobId == parent.Id)
            .Select(x => (x.Id, x.Status, x.ConcurrencyToken, x.AttemptNo)).ToArray());
        Assert.Equal(0, image.Calls);
        Assert.Empty(tts.Inputs);

        parent.Status = GenerationJobStatus.Processing;
        parent.MediaContextId = contextId;
        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(StoryStatus.MediaReview, store.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task BranchWorkerStillCancelsWorkWhenParentIsCancelled()
    {
        var store = Seed();
        var image = new SwitchableImage();
        var service = Create(store, image: image);
        await service.ProcessNextAsync();
        var parent = store.Items<StoryGenerationJob>().Single(x => x.ParentJobId == null);
        parent.Status = GenerationJobStatus.Cancelled;

        await service.ProcessBranchNextAsync(MediaType.Illustration);
        Assert.Equal(GenerationJobStatus.Cancelled, store.Items<StoryGenerationJob>()
            .Single(x => x.Operation == GenerationJobOperation.GenerateIllustration).Status);
        Assert.Equal(0, image.Calls);
    }

    [Fact]
    public async Task FailedReplacementKeepsOldPreviewAndRequiresExplicitKeepAndPackageConfirmation()
    {
        var store = Seed();
        var image = new SwitchableImage();
        var service = Create(store, image: image);
        await service.RunPackageAsync();
        await service.ConfirmAsync(1, 1, new(10, 1));
        var selected = store.Items<MediaAsset>().First(x => x.Type == MediaType.Illustration);
        var path = selected.Url;
        image.Fail = image.Permanent = true;
        await service.RegenerateIllustrationBeatAsync(1, 1, selected.IllustrationBeatId!.Value, new(10, 1, true, "replace-failed"));
        var candidate = store.Items<MediaAsset>().Single(x => x.ReplacesMediaAssetId == selected.Id);
        Assert.True(selected.IsCurrent);
        Assert.Equal(MediaStatus.Ready, selected.Status);
        Assert.False((await service.RunPackageAsync()).Success);
        var package = await service.GetPackageAsync(1, 1);
        Assert.Equal("Failed", Assert.Single(package.Replacements).Status);
        Assert.Contains(path!, package.Scenes.SelectMany(x => x.Illustrations).Single(x => x.AssetId == selected.Id).Url);
        Assert.Equal(selected.Url, path);
        await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmAsync(1, 1, new(10, 2)));
        var keep = new KeepCurrentMediaRequest("keep-old", 10, 2);
        var kept = await service.KeepCurrentAsync(1, 1, candidate.Id, keep);
        Assert.True(kept.IsAwaitingConfirmation);
        Assert.False(kept.IsConfirmed);
        await service.KeepCurrentAsync(1, 1, candidate.Id, keep);
        Assert.Equal(2, store.Items<StoryAuthoringOperation>().Count);
        Assert.Equal("Kept", Assert.Single((await service.GetPackageAsync(1, 1)).Replacements).Status);
        Assert.True((await service.ConfirmAsync(1, 1, new(10, 2))).IsReady);
        Assert.True(selected.IsCurrent);
        Assert.False(candidate.IsCurrent);
        Assert.NotNull(candidate.ReplacementResolvedAt);
    }

    [Fact]
    public async Task RegenerateReplayCreatesOneCandidateAndSuccessPromotesItWithoutChangingOtherMedia()
    {
        var store = Seed();
        var image = new SwitchableImage();
        var tts = new RecordingTtsProvider();
        var service = Create(store, image: image, tts: tts);
        await service.RunPackageAsync();
        var target = store.Items<MediaAsset>().First(x => x.Type == MediaType.Illustration);
        var oldPath = target.Url;
        var other = store.Items<MediaAsset>().Where(x => x.Id != target.Id).Select(x => (x.Id, x.Url)).ToArray();
        var input = new RegenerateMediaRequest(10, 1, false, "replace-once");
        await service.RegenerateIllustrationBeatAsync(1, 1, target.IllustrationBeatId!.Value, input);
        await service.RegenerateIllustrationBeatAsync(1, 1, target.IllustrationBeatId.Value, input);
        Assert.Single(store.Items<MediaAsset>(), x => x.ReplacesMediaAssetId == target.Id);
        await Assert.ThrowsAsync<ConflictException>(() => service.RegenerateIllustrationBeatAsync(1, 1,
            target.IllustrationBeatId.Value, input with { PackageRevision = 2 }));
        await Assert.ThrowsAsync<ConflictException>(() => service.RegenerateIllustrationBeatAsync(1, 1,
            target.IllustrationBeatId.Value, input with { OperationId = "second-while-running", PackageRevision = 2 }));
        Assert.True((await service.RunPackageAsync()).Success);
        var replacement = store.Items<MediaAsset>().Single(x => x.ReplacesMediaAssetId == target.Id);
        Assert.True(replacement.IsCurrent);
        Assert.False(target.IsCurrent);
        Assert.Equal(oldPath, target.Url);
        Assert.NotEqual(oldPath, replacement.Url);
        Assert.Equal(3, image.Calls);
        Assert.Equal(2, tts.Inputs.Count);
        Assert.Equal(other, store.Items<MediaAsset>().Where(x => x.IsCurrent && x.Id != replacement.Id).Select(x => (x.Id, x.Url)).ToArray());
        Assert.Equal("Applied", Assert.Single((await service.GetPackageAsync(1, 1)).Replacements).Status);
        Assert.True((await service.ConfirmAsync(1, 1, new(10, 2))).IsReady);
    }

    [Fact]
    public async Task ReadinessIgnoresUnselectedFailedHistoryButConfirmRejectsUnresolvedReplacement()
    {
        var store = Seed();
        var service = Create(store);
        await service.RunPackageAsync();
        var target = store.Items<MediaAsset>().First(x => x.Type == MediaType.Illustration);
        store.Seed(new MediaAsset { Id = 99, StoryVersionId = 10, StorySceneId = target.StorySceneId,
            IllustrationBeatId = target.IllustrationBeatId, Type = target.Type, Status = MediaStatus.ManualReview,
            IsCurrent = false, ReplacesMediaAssetId = target.Id, RequestedPackageRevision = 1 });
        Assert.True((await service.GetProgressAsync(1, 1)).IsGenerated);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmAsync(1, 1, new(10, 1)));
        Assert.Contains("MEDIA_REPLACEMENT_UNRESOLVED", ex.Message);
    }

    [Fact]
    public async Task ExistingLegacyHttpsImageRemainsPreviewableWhileItsReplacementIsPending()
    {
        var store = Seed();
        var service = Create(store);
        await service.RunPackageAsync();
        var version = store.Items<StoryVersion>().Single();
        version.MediaState.MediaPackageRevision = 0;
        store.Items<Story>().Single().Status = StoryStatus.Ready;
        var target = store.Items<MediaAsset>().First(x => x.Type == MediaType.Illustration);
        target.RequestedPackageRevision = null;
        target.Url = "https://legacy.example/old.png";
        await service.RegenerateIllustrationBeatAsync(1, 1, target.IllustrationBeatId!.Value, new(10, 0, true, "legacy-image"));
        var package = await service.GetPackageAsync(1, 1);
        Assert.Equal(target.Url, package.Scenes.SelectMany(x => x.Illustrations).Single(x => x.AssetId == target.Id).Url);
        Assert.False(package.IsConfirmed);
        Assert.False(package.IsReady);
    }

    [Fact]
    public void SplitJobAndAssetModelHasFilteredSelectionAndOwnershipConstraints()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var asset = model.FindEntityType(typeof(MediaAsset))!;
        Assert.Contains(asset.GetIndexes(), x => x.IsUnique && x.GetFilter()!.Contains("IsCurrent"));
        Assert.Contains(asset.GetIndexes(), x => x.IsUnique && x.GetFilter()!.Contains("ReplacementResolvedAt"));
        var job = model.FindEntityType(typeof(StoryGenerationJob))!;
        Assert.Contains(job.GetForeignKeys(), x => x.Properties.Any(p => p.Name == "ParentJobId"));
        Assert.Contains(job.GetForeignKeys(), x => x.Properties.Any(p => p.Name == "MediaContextId"));
    }
}
