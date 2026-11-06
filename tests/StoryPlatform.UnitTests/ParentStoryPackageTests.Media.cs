using System.Text.Json;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class ParentStoryPackageTests
{
    // Seed the durable handoff to the media worker; these tests exercise the consumer, not learning providers.
    private static async Task<(ParentStoryPackage Package, StoryGenerationJob Job)> QueueMedia(
        Fixture f, ParentPackageComponents components, int child = 3)
    {
        var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("media-child-" + child, child) with
        { Recipients = [new ParentPackageRecipient(child, components)] }, default);
        Assert.True(await f.Worker.ProcessNextAsync(default));
        var p = f.Store.Items<ParentStoryPackage>().Single(x => x.Id == created[0].PackageId);
        p.Status = "Generating";
        await f.Store.Uow.Object.Repository<StoryGenerationJob>().AddAsync(new()
        {
            StoryId = f.StoryId, StoryVersionId = p.StoryVersionId, ParentStoryPackageId = p.Id,
            Operation = GenerationJobOperation.BuildParentPackage, Status = GenerationJobStatus.Completed
        });
        var job = await f.Store.Uow.Object.Repository<StoryGenerationJob>().AddAsync(new()
        {
            StoryId = f.StoryId, StoryVersionId = p.StoryVersionId, ParentStoryPackageId = p.Id,
            RequestedByUserId = 1, Operation = GenerationJobOperation.GenerateMediaPackage,
            Status = GenerationJobStatus.Pending, MediaPackageRevision = p.Revision, MaxAttempts = 3
        });
        return (p, job);
    }

    private static async Task<ListeningInteraction> AddPicture(Fixture f, ParentStoryPackage p)
    {
        var scene = f.Store.Items<StoryScene>().OrderBy(x => x.SceneIndex).First();
        var interaction = await f.Store.Uow.Object.Repository<ListeningInteraction>().AddAsync(new()
        {
            StoryVersionId = p.StoryVersionId, ParentStoryPackageId = p.Id, Type = ListeningInteractionType.PicturePrediction,
            AnchorOffset = scene.TextRangeEnd, RevealOffset = Body.Length, Prompt = "Who comes next?", WaitFeedback = "Keep listening."
        });
        await f.Store.Uow.Object.Repository<ListeningInteractionOption>().AddAsync(new()
        { ListeningInteractionId = interaction.Id, SortOrder = 1, Text = "The dog", Feedback = "The dog comes.", ImageDescription = "A small dog", IsMatch = true });
        return interaction;
    }

    private static MediaGenerationService MediaWorker(Fixture f, ITtsProvider voice, IImageGenerationProvider? images = null,
        BinaryStorage? storage = null) => MediaGenerationServiceTests.Create(f.Store.Uow.Object, image: images ?? new CountingImage(), tts: voice,
            storage: storage ?? new BinaryStorage(), evaluator: new CountEvaluation(), supervision: new ChildProfileAccessGuard(f.Store.Uow.Object));

    [Fact]
    public async Task Listening_manifest_ignores_history_and_reuses_newest_valid_assets_in_its_own_scope()
    {
        var f = new Fixture(); await f.Initialize();
        var (p, _) = await QueueMedia(f, new(Listening: true));
        var interaction = await AddPicture(f, p);
        var options = f.Store.Items<ListeningInteractionOption>().ToArray();
        var originals = new List<MediaAsset>();
        foreach (var part in ListeningAudioParts.For(interaction, options))
            originals.Add(new() { Type = MediaType.ListeningAudio, ListeningInteractionId = interaction.Id,
                ListeningInteractionOptionId = part.OptionId, AudioPartKey = part.PartKey });
        originals.Add(new() { Type = MediaType.ListeningImage, ListeningInteractionId = interaction.Id, ListeningInteractionOptionId = options[0].Id });
        var expected = new List<int>();
        foreach (var template in originals)
        {
            foreach (var state in new[] { MediaStatus.Queued, MediaStatus.Failed, MediaStatus.ManualReview, MediaStatus.Ready, MediaStatus.Ready })
            {
                var asset = JsonSerializer.Deserialize<MediaAsset>(JsonSerializer.Serialize(template))!;
                asset.StoryVersionId = p.StoryVersionId; asset.ParentStoryPackageId = p.Id; asset.Status = state;
                asset.ValidationStatus = state == MediaStatus.Ready ? ValidationStatus.Passed : ValidationStatus.Pending;
                asset.Url = state == MediaStatus.Ready ? "existing-media" : null;
                asset.CompletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                await f.Store.Uow.Object.Repository<MediaAsset>().AddAsync(asset);
                if (state == MediaStatus.Ready)
                {
                    if (expected.Count > 0 && expected[^1] == asset.Id - 1) expected.RemoveAt(expected.Count - 1);
                    expected.Add(asset.Id);
                }
            }
            // Newer rows in another package, another version, or the legacy scope must not win.
            foreach (var (packageId, versionId) in new (int?, int)[] { (999, p.StoryVersionId), (null, p.StoryVersionId), (p.Id, p.StoryVersionId + 1) })
            {
                var foreign = JsonSerializer.Deserialize<MediaAsset>(JsonSerializer.Serialize(template))!;
                foreign.ParentStoryPackageId = packageId; foreign.StoryVersionId = versionId;
                foreign.Status = MediaStatus.Ready; foreign.ValidationStatus = ValidationStatus.Passed;
                foreign.Url = "foreign-media"; foreign.CompletedAt = DateTime.UtcNow;
                await f.Store.Uow.Object.Repository<MediaAsset>().AddAsync(foreign);
            }
        }
        var snapshot = JsonSerializer.Serialize(f.Store.Items<MediaAsset>());
        var voice = new ConfigurableVoice(); var images = new CountingImage();
        var result = await MediaWorker(f, voice, images).ProcessNextAsync(default);
        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(0, voice.Calls); Assert.Equal(0, images.Calls);
        Assert.Equal(snapshot, JsonSerializer.Serialize(f.Store.Items<MediaAsset>()));
        var manifest = JsonSerializer.Deserialize<ParentPackageManifest>(p.ManifestJson, ParentPackageContext.Json)!;
        Assert.Equal(expected.Order(), manifest.MediaAssetIds.Order());
        var current = await f.Packages.GetAsync(1, f.StoryId, p.Id, default);
        var confirmed = await f.Packages.ConfirmAsync(1, f.StoryId, p.Id, new() { OperationId = "confirm-listening", PackageToken = current.PackageToken }, default);
        var grant = Assert.Single(await f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver-listening", confirmed), default));
        Assert.Equal(p.Id, grant.ParentStoryPackageId);
        var frozen = p.ManifestJson;
        var (second, _) = await QueueMedia(f, new(Listening: true), 4);
        await AddPicture(f, second);
        Assert.True((await MediaWorker(f, voice, images).ProcessNextAsync(default)).Success);
        Assert.True(voice.Calls > 0); Assert.True(images.Calls > 0);
        Assert.Equal(frozen, p.ManifestJson);
        foreach (var asset in f.Store.Items<MediaAsset>().Where(x => expected.Contains(x.Id))) Assert.Equal("existing-media", asset.Url);
        var secondIds = JsonSerializer.Deserialize<ParentPackageManifest>(second.ManifestJson, ParentPackageContext.Json)!.MediaAssetIds;
        Assert.All(secondIds, id => Assert.Equal(second.Id, f.Store.Items<MediaAsset>().Single(x => x.Id == id).ParentStoryPackageId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Listening_retry_keeps_partial_audio_and_images_without_duplicate_rows(bool failImage)
    {
        var f = new Fixture(); await f.Initialize();
        var (p, job) = await QueueMedia(f, new(Listening: true));
        var interaction = await AddPicture(f, p);
        var parts = ListeningAudioParts.For(interaction, f.Store.Items<ListeningInteractionOption>());
        var voice = new ScriptedVoice { FailCall = failImage ? -1 : 2, Failure = new TransientMediaGenerationException("GEMINI_TTS_HTTP_429", retryAfter: TimeSpan.FromSeconds(180)) };
        var images = new ScriptedImage { FailOnce = failImage };
        var media = MediaWorker(f, voice, images);
        var first = await media.ProcessNextAsync(default);
        Assert.False(first.Success); Assert.Equal(GenerationJobStatus.Pending, job.Status);
        var completed = f.Store.Items<MediaAsset>().Where(x => x.Status == MediaStatus.Ready).ToDictionary(x => x.Id, x => x.Url);
        job.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
        var retried = await media.ProcessNextAsync(default);
        Assert.True(retried.Success, retried.ErrorCode);
        Assert.Equal(parts.Count + 1, f.Store.Items<MediaAsset>().Count);
        Assert.Equal(parts.Count + (failImage ? 0 : 1), voice.Calls);
        Assert.Equal(failImage ? 2 : 1, images.Calls);
        foreach (var asset in f.Store.Items<MediaAsset>().Where(x => completed.ContainsKey(x.Id))) Assert.Equal(completed[asset.Id], asset.Url);
        Assert.Single(f.Store.Items<StoryVersion>());
    }

    [Theory]
    [InlineData("quota", true, "GEMINI_TTS_HTTP_429")]
    [InlineData("timeout", true, "MEDIA_PROVIDER_TIMEOUT")]
    [InlineData("cancelled-request", true, "MEDIA_PROVIDER_TIMEOUT")]
    [InlineData("permanent", false, "GEMINI_TTS_CONFIGURATION_REJECTED")]
    [InlineData("legacy-incomplete", false, "MEDIA_PACKAGE_INCOMPLETE")]
    public async Task Required_vocabulary_audio_preserves_error_retry_delay_and_completed_work(string failure, bool retryable, string expectedCode)
    {
        var f = new Fixture(); await f.Initialize();
        var (p, job) = await QueueMedia(f, new(Vocabulary: true, Quiz: true, Narration: true));
        await f.Store.Uow.Object.Repository<StoryVocabulary>().AddAsync(new() { ParentStoryPackageId = p.Id, StoryVersionId = p.StoryVersionId, Term = "Tom", Definition = "A boy" });
        await f.Store.Uow.Object.Repository<QuizItem>().AddAsync(new() { ParentStoryPackageId = p.Id, StoryVersionId = p.StoryVersionId, Question = "Who has a ball?" });
        Exception error = failure switch
        {
            "quota" => new TransientMediaGenerationException(expectedCode, retryAfter: TimeSpan.FromSeconds(180)),
            "timeout" => new TimeoutException(),
            "cancelled-request" => new OperationCanceledException(),
            "permanent" => new PermanentMediaGenerationException(expectedCode),
            _ => new InvalidOperationException("PACKAGE_MEDIA_INCOMPLETE")
        };
        var voice = new ScriptedVoice { FailText = "Tom", Failure = error };
        var media = MediaWorker(f, voice);
        var before = DateTime.UtcNow;
        var result = await media.ProcessNextAsync(default);
        Assert.False(result.Success); Assert.Equal(expectedCode, result.ErrorCode); Assert.Equal(expectedCode, job.ErrorCode);
        Assert.Equal(retryable ? GenerationJobStatus.Pending : GenerationJobStatus.Failed, job.Status);
        Assert.Equal(retryable ? "Generating" : "Failed", p.Status);
        if (!retryable) Assert.Null(job.NextAttemptAt);
        else Assert.True(job.NextAttemptAt >= before.AddSeconds(failure == "quota" ? 179 : 59));
        var paths = f.Store.Items<MediaAsset>().Where(x => x.Status == MediaStatus.Ready).ToDictionary(x => x.Id, x => x.Url);
        Assert.NotEmpty(paths);
        var assetCount = f.Store.Items<MediaAsset>().Count;
        var calls = voice.Calls;
        if (retryable)
        {
            voice.FailText = null; job.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
            var success = await media.ProcessNextAsync(default); Assert.True(success.Success, success.ErrorCode);
            Assert.Equal(calls + 1, voice.Calls); Assert.Null(job.ErrorCode); Assert.Null(p.LastErrorCode);
        }
        Assert.Equal(assetCount, f.Store.Items<MediaAsset>().Count);
        foreach (var asset in f.Store.Items<MediaAsset>().Where(x => paths.ContainsKey(x.Id))) Assert.Equal(paths[asset.Id], asset.Url);
        Assert.Single(f.Store.Items<StoryVocabulary>()); Assert.Single(f.Store.Items<QuizItem>()); Assert.Single(f.Store.Items<StoryVersion>());
        f.Batches.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Required_vocabulary_audio_stops_automatic_retry_at_job_limit()
    {
        var f = new Fixture(); await f.Initialize();
        var (p, job) = await QueueMedia(f, new(Vocabulary: true, Narration: true));
        await f.Store.Uow.Object.Repository<StoryVocabulary>().AddAsync(new() { ParentStoryPackageId = p.Id, StoryVersionId = p.StoryVersionId, Term = "Tom" });
        var voice = new ScriptedVoice { FailText = "Tom", Failure = new TransientMediaGenerationException("GEMINI_TTS_HTTP_429", retryAfter: TimeSpan.FromSeconds(180)) };
        var media = MediaWorker(f, voice);
        for (var attempt = 1; attempt <= job.MaxAttempts; attempt++)
        {
            Assert.False((await media.ProcessNextAsync(default)).Success);
            Assert.Equal(attempt, job.AttemptNo);
            Assert.Equal(attempt < job.MaxAttempts ? GenerationJobStatus.Pending : GenerationJobStatus.Failed, job.Status);
            if (attempt < job.MaxAttempts) job.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
        }
        Assert.Null(job.NextAttemptAt); Assert.Equal("GEMINI_TTS_HTTP_429", p.LastErrorCode);
        var calls = voice.Calls;
        Assert.False((await media.ProcessNextAsync(default)).JobFound); Assert.Equal(calls, voice.Calls);
    }

    [Fact]
    public async Task Media_with_no_usable_url_cannot_complete_or_be_confirmed()
    {
        var f = new Fixture(); await f.Initialize();
        var (p, job) = await QueueMedia(f, new(Narration: true));
        var storage = new Mock<IMediaStorage>();
        storage.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("");
        var media = MediaGenerationServiceTests.Create(f.Store.Uow.Object, storage: storage.Object,
            supervision: new ChildProfileAccessGuard(f.Store.Uow.Object));
        var result = await media.ProcessNextAsync(default);
        Assert.False(result.Success); Assert.Equal("MEDIA_PACKAGE_INCOMPLETE", result.ErrorCode);
        var failed = await f.Packages.GetAsync(1, f.StoryId, p.Id, default);
        Assert.Equal("Failed", failed.Status); Assert.Equal("MEDIA_PACKAGE_INCOMPLETE", failed.LastErrorCode);
        Assert.Equal(GenerationJobStatus.Failed, f.Store.Items<StoryGenerationJob>().Single(x => x.Id == job.Id).Status);
        var error = await Assert.ThrowsAsync<ConflictException>(() => f.Packages.ConfirmAsync(1, f.StoryId, p.Id,
            new() { OperationId = "cannot-confirm-missing", PackageToken = failed.PackageToken }, default));
        Assert.Equal("PACKAGE_NOT_READY", error.Message);
        Assert.DoesNotContain(f.Store.Items<ParentStoryOperation>(), x => x.Kind == "FamilyConfirmation");
    }

    private sealed class ScriptedVoice : ITtsProvider
    {
        public string ConfigurationFingerprint => "test-voice-v1";
        public int Calls;
        public int FailCall = -1;
        public string? FailText;
        public Exception Failure = new TimeoutException();
        public Task<GeneratedMedia> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == FailCall || text == FailText) throw Failure;
            return Task.FromResult(new GeneratedMedia([4, 5, 6], "audio/mpeg"));
        }
    }

    private sealed class ScriptedImage : IImageGenerationProvider
    {
        public int Calls;
        public bool FailOnce;
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailOnce && Calls == 1) throw new TransientMediaGenerationException("GEMINI_IMAGE_HTTP_429", retryAfter: TimeSpan.FromSeconds(180));
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
    }
}
