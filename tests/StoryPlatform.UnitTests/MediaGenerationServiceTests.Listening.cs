using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class MediaGenerationServiceTests
{
    private const int FirstBlockEnd = 3;
    private const int StoryEnd = 4;

    private static void SeedCheckpoint(StoryReviewServiceTests.FakeUnitOfWork uow)
    {
        uow.Seed(new ListeningInteraction
        {
            Id = 70, StoryVersionId = 10, Type = ListeningInteractionType.Checkpoint, Kind = CheckpointKind.Emotion,
            AnchorOffset = FirstBlockEnd, Prompt = "Thỏ thấy thế nào?", NoMatchFeedback = "Ý của con cũng hay."
        });
        uow.Seed(new ListeningInteractionOption { Id = 80, ListeningInteractionId = 70, SortOrder = 1,
            Text = "Vui", Feedback = "Thỏ vui lắm.", IsMatch = true });
        uow.Seed(new ListeningInteractionOption { Id = 81, ListeningInteractionId = 70, SortOrder = 2,
            Text = "Buồn", Feedback = "Thỏ không buồn đâu." });
    }

    private static void SeedPicturePrediction(StoryReviewServiceTests.FakeUnitOfWork uow)
    {
        uow.Seed(new ListeningInteraction
        {
            Id = 71, StoryVersionId = 10, Type = ListeningInteractionType.PicturePrediction,
            AnchorOffset = FirstBlockEnd, RevealOffset = StoryEnd,
            Prompt = "Ai đến giúp Thỏ?", WaitFeedback = "Nghe tiếp nhé!"
        });
        uow.Seed(new ListeningInteractionOption { Id = 90, ListeningInteractionId = 71, SortOrder = 1,
            Text = "Bác Gấu", Feedback = "Đúng là bác Gấu.", IsMatch = true,
            ImageDescription = "Bác Gấu cầm đèn lồng" });
        uow.Seed(new ListeningInteractionOption { Id = 91, ListeningInteractionId = 71, SortOrder = 2,
            Text = "Chim Sẻ", Feedback = "Hóa ra là bác Gấu.", ImageDescription = "Chim Sẻ bay tới" });
    }

    [Fact]
    public async Task ProcessNext_BindsListeningInteractionsToAnchorScenes()
    {
        var uow = Seed();
        uow.Seed(new ListeningInteraction { Id = 70, StoryVersionId = 10,
            Type = ListeningInteractionType.Checkpoint, AnchorOffset = FirstBlockEnd });
        uow.Seed(new ListeningInteraction { Id = 72, StoryVersionId = 10,
            Type = ListeningInteractionType.EventOrdering, AnchorOffset = StoryEnd });

        Assert.True((await Create(uow).RunPackageAsync()).Success);

        var scenes = uow.Items<StoryScene>().OrderBy(x => x.SceneIndex).ToArray();
        Assert.Equal(scenes[0].Id, uow.Items<ListeningInteraction>().Single(x => x.Id == 70).StorySceneId);
        Assert.Equal(scenes[^1].Id, uow.Items<ListeningInteraction>().Single(x => x.Id == 72).StorySceneId);
    }

    [Fact]
    public async Task ProcessNext_GeneratesListeningAudioForEveryPart()
    {
        var uow = Seed();
        SeedCheckpoint(uow);
        var tts = new RecordingTtsProvider();
        var storage = new RecordingMediaStorage();

        Assert.True((await Create(uow, tts: tts, storage: storage).RunPackageAsync()).Success);

        var audio = uow.Items<MediaAsset>().Where(x => x.Type == MediaType.ListeningAudio).ToArray();
        Assert.Equal(6, audio.Length);
        Assert.All(audio, x =>
        {
            Assert.Equal(MediaStatus.Ready, x.Status);
            Assert.Equal(70, x.ListeningInteractionId);
            Assert.Null(x.StorySceneId);
        });
        Assert.Equal(new[] { "i:noMatch", "i:prompt", "o80:feedback", "o80:text", "o81:feedback", "o81:text" },
            audio.Select(x => $"{(x.ListeningInteractionOptionId is int id ? $"o{id}" : "i")}:{x.AudioPartKey}")
                .Order(StringComparer.Ordinal));
        Assert.Contains("Thỏ thấy thế nào?", tts.Inputs);
        Assert.Contains(storage.UploadedPaths, x => x.Contains("/listening-70-i-prompt-asset-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessNext_ListeningAudioFailingEveryAttempt_GoesToManualReview()
    {
        var uow = Seed();
        SeedCheckpoint(uow);
        Assert.False((await Create(uow, tts: new FailingTextTtsProvider("Ý của con cũng hay.")).RunPackageAsync()).Success);
        var failed = uow.Items<MediaAsset>().Single(x => x.Type == MediaType.ListeningAudio && x.AudioPartKey == "noMatch");
        Assert.Equal(MediaStatus.ManualReview, failed.Status);
        Assert.Equal(3, failed.AttemptCount);
        Assert.NotEqual(StoryStatus.MediaReview, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task TtsBranch_ListeningAudioAlreadyInManualReview_FailsBranchInsteadOfRequeueing()
    {
        // Prod job 30: asset parked in ManualReview with AttemptCount below the budget kept the
        // branch Pending and the worker re-claimed it every few seconds forever.
        var uow = Seed();
        SeedCheckpoint(uow);
        uow.Seed(new MediaAsset
        {
            Id = 500, StoryVersionId = 10, ListeningInteractionId = 70, AudioPartKey = "noMatch",
            Type = MediaType.ListeningAudio, Status = MediaStatus.ManualReview,
            ValidationStatus = ValidationStatus.Failed, AttemptCount = 1, IsCurrent = true
        });
        var tts = new RecordingTtsProvider();
        var service = Create(uow, tts: tts);

        Assert.True((await service.ProcessNextAsync()).Success);
        var result = await service.ProcessBranchNextAsync(MediaType.TtsAudio);

        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal("LISTENING_AUDIO_RETRY_EXHAUSTED", result.ErrorCode);
        var branch = uow.Items<StoryGenerationJob>().Single(x => x.Operation == GenerationJobOperation.GenerateTts);
        Assert.Equal(GenerationJobStatus.Failed, branch.Status);
        Assert.Equal("LISTENING_AUDIO_RETRY_EXHAUSTED", branch.ErrorCode);
        Assert.Null(branch.NextAttemptAt);
        // The asset stays parked for a human; nothing re-generates it behind the reviewer's back.
        var parked = uow.Items<MediaAsset>().Single(x => x.Id == 500);
        Assert.Equal(MediaStatus.ManualReview, parked.Status);
        Assert.Equal(1, parked.AttemptCount);
        Assert.DoesNotContain("Ý của con cũng hay.", tts.Inputs);
        Assert.False((await service.ProcessBranchNextAsync(MediaType.TtsAudio)).JobFound);
    }

    [Fact]
    public async Task ProcessNext_GeneratesCheckedImageForEachPicturePredictionOption()
    {
        var uow = Seed();
        SeedPicturePrediction(uow);
        var images = new RecordingImageProvider();
        var storage = new RecordingMediaStorage();

        Assert.True((await Create(uow, image: images, storage: storage).RunPackageAsync()).Success);

        var pictures = uow.Items<MediaAsset>().Where(x => x.Type == MediaType.ListeningImage)
            .OrderBy(x => x.ListeningInteractionOptionId).ToArray();
        Assert.Equal(new int?[] { 90, 91 }, pictures.Select(x => x.ListeningInteractionOptionId));
        Assert.All(pictures, x =>
        {
            Assert.Equal(MediaStatus.Ready, x.Status);
            Assert.Equal(ValidationStatus.Passed, x.ValidationStatus);
            Assert.Equal(71, x.ListeningInteractionId);
            Assert.Null(x.StorySceneId);
            Assert.Null(x.IllustrationBeatId);
        });
        var firstScene = uow.Items<StoryScene>().Single(x => x.SceneIndex == 0);
        Assert.Contains(images.Specs, x => x.BeatText == "Bác Gấu cầm đèn lồng" &&
            x.StorySceneId == firstScene.Id && x.MustShow.SequenceEqual(["Bác Gấu cầm đèn lồng"]));
        Assert.Contains(storage.UploadedPaths, x => x.Contains("/listening-71-o90-asset-", StringComparison.Ordinal));
        Assert.Contains(storage.UploadedPaths, x => x.Contains("/scene-0-beat-1-asset-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessNext_PicturePredictionImageFailingEveryAttempt_GoesToManualReview()
    {
        var uow = Seed();
        SeedPicturePrediction(uow);
        Assert.False((await Create(uow, image: new FailingBeatTextImageProvider("Chim Sẻ bay tới")).RunPackageAsync()).Success);
        var failed = uow.Items<MediaAsset>().Single(x => x.Type == MediaType.ListeningImage && x.ListeningInteractionOptionId == 91);
        Assert.Equal(MediaStatus.ManualReview, failed.Status);
        Assert.NotEqual(StoryStatus.MediaReview, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task Readiness_RequiresEveryListeningAudioPart()
    {
        var uow = Seed();
        SeedCheckpoint(uow);
        Assert.True((await Create(uow).RunPackageAsync()).Success);
        var readiness = new MediaReadinessService(uow);
        Assert.True((await readiness.CheckAsync(10, null)).IsReady);

        uow.Items<MediaAsset>().Single(x => x.Type == MediaType.ListeningAudio && x.AudioPartKey == "prompt").Status = MediaStatus.ManualReview;
        uow.Items<MediaAsset>().Single(x => x.Type == MediaType.ListeningAudio &&
            x.ListeningInteractionOptionId == 81 && x.AudioPartKey == "text").IsCurrent = false;

        var result = await readiness.CheckAsync(10, null);
        Assert.False(result.IsReady);
        Assert.Equal(new[] { "70:i:prompt", "70:o81:text" }, result.MissingListeningMedia);
    }

    [Fact]
    public async Task Readiness_RequiresBoundSceneAndPassedPicturePredictionImages()
    {
        var uow = Seed();
        SeedPicturePrediction(uow);
        Assert.True((await Create(uow).RunPackageAsync()).Success);
        var readiness = new MediaReadinessService(uow);
        Assert.True((await readiness.CheckAsync(10, null)).IsReady);
        uow.Items<ListeningInteraction>().Single(x => x.Id == 71).StorySceneId = null;
        uow.Items<MediaAsset>().Single(x => x.Type == MediaType.ListeningImage &&
            x.ListeningInteractionOptionId == 90).ValidationStatus = ValidationStatus.Failed;

        var result = await readiness.CheckAsync(10, null);
        Assert.False(result.IsReady);
        Assert.Equal(new[] { "71:scene", "71:o90:image" }, result.MissingListeningMedia);
    }

    [Fact]
    public async Task Readiness_IgnoresDeletedInteractions()
    {
        var uow = Seed();
        Assert.True((await Create(uow).RunPackageAsync()).Success);
        uow.Seed(new ListeningInteraction { Id = 73, StoryVersionId = 10,
            Type = ListeningInteractionType.Checkpoint, AnchorOffset = FirstBlockEnd,
            Prompt = "Đã xoá", IsDeleted = true });
        var result = await new MediaReadinessService(uow).CheckAsync(10, null);
        Assert.True(result.IsReady);
        Assert.Empty(result.MissingListeningMedia);
    }

    private const string OrderingInstruction = "Em hãy chạm vào các thẻ theo đúng thứ tự chuyện xảy ra trong truyện nhé!";

    // Tương tác có media từ trước khi có câu hướng dẫn: đã có audio thẻ, chưa có audio "instruction".
    private static void SeedOldOrdering(StoryReviewServiceTests.FakeUnitOfWork uow, int id = 74, int? packageId = null,
        ListeningInteractionType type = ListeningInteractionType.EventOrdering, bool mediaDone = true)
    {
        uow.Seed(new ListeningInteraction { Id = id, StoryVersionId = 10, Type = type, AnchorOffset = StoryEnd,
            StorySceneId = 500, ParentStoryPackageId = packageId, CharacterName = type == ListeningInteractionType.RolePlay ? "Thỏ" : null });
        if (mediaDone)
            uow.Seed(new MediaAsset { Id = 900 + id, StoryVersionId = 10, ParentStoryPackageId = packageId, ListeningInteractionId = id,
                AudioPartKey = ListeningAudioParts.Match, Type = MediaType.ListeningAudio, Status = MediaStatus.Ready,
                ValidationStatus = ValidationStatus.Passed, Url = $"old-{id}.mp3" });
    }

    private static MediaAsset[] InstructionAudio(StoryReviewServiceTests.FakeUnitOfWork uow) =>
        uow.Items<MediaAsset>().Where(x => x.AudioPartKey == ListeningAudioParts.Instruction).ToArray();

    [Fact]
    public async Task BackfillInstruction_CreatesMissingAudioForOldInteraction_ThenHasNothingLeft()
    {
        var uow = Seed();
        SeedOldOrdering(uow);
        var tts = new RecordingTtsProvider();
        var storage = new RecordingMediaStorage();
        var service = Create(uow, tts: tts, storage: storage);

        Assert.True(await service.BackfillListeningInstructionAudioAsync());

        var asset = Assert.Single(InstructionAudio(uow));
        Assert.Equal(74, asset.ListeningInteractionId);
        Assert.Null(asset.ListeningInteractionOptionId);
        Assert.Null(asset.ParentStoryPackageId);
        Assert.Equal(MediaStatus.Ready, asset.Status);
        Assert.Equal(ValidationStatus.Passed, asset.ValidationStatus);
        Assert.False(string.IsNullOrWhiteSpace(asset.Url));
        Assert.Equal([OrderingInstruction], tts.Inputs);
        Assert.Contains(storage.UploadedPaths, path => path.StartsWith("1/versions/10/listening-74-i-instruction-", StringComparison.Ordinal));
        Assert.Empty(ListeningMediaRequirements.Missing(
            uow.Items<ListeningInteraction>().Single(), [], uow.Items<MediaAsset>().ToList()));

        Assert.False(await service.BackfillListeningInstructionAudioAsync());
        Assert.Single(tts.Inputs);
    }

    [Fact]
    public async Task BackfillInstruction_SkipsParentPackagesUnfinishedMediaAndTypesWithPrompt()
    {
        var uow = Seed();
        SeedOldOrdering(uow, 74, packageId: 5);
        SeedOldOrdering(uow, 75, mediaDone: false);
        SeedOldOrdering(uow, 76, type: ListeningInteractionType.Checkpoint);
        var tts = new RecordingTtsProvider();

        Assert.False(await Create(uow, tts: tts).BackfillListeningInstructionAudioAsync());

        Assert.Empty(InstructionAudio(uow));
        Assert.Empty(tts.Inputs);
    }

    [Fact]
    public async Task BackfillInstruction_RetriesFailuresUpToTheAssetLimit()
    {
        var uow = Seed();
        SeedOldOrdering(uow);
        var service = Create(uow, tts: new FailingTextTtsProvider(OrderingInstruction));

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(await service.BackfillListeningInstructionAudioAsync());
            var asset = Assert.Single(InstructionAudio(uow));
            Assert.Equal(attempt, asset.AttemptCount);
            Assert.Equal(MediaStatus.Failed, asset.Status);
        }

        Assert.False(await service.BackfillListeningInstructionAudioAsync());
        Assert.Equal(3, Assert.Single(InstructionAudio(uow)).AttemptCount);
    }

    [Fact]
    public void BackfillInstruction_QueryTranslatesToPostgresSql()
    {
        using var db = new StoryPlatform.Infrastructure.Persistence.ApplicationDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<StoryPlatform.Infrastructure.Persistence.ApplicationDbContext>()
                .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

        var sql = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToQueryString(
            MediaGenerationService.InstructionBackfillCandidates(db.Set<ListeningInteraction>(), db.Set<MediaAsset>(), 3, DateTime.UtcNow).Take(1));

        Assert.Contains("EXISTS", sql);
        Assert.Contains("instruction", sql);
    }

    private sealed class FailingTextTtsProvider(string failingText) : ITtsProvider
    {
        public Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default) =>
            exactSceneText == failingText
                ? throw new InvalidOperationException("TTS_PROVIDER_TEST_FAILURE")
                : Task.FromResult(new GeneratedMedia([4, 5, 6], "audio/mpeg"));
    }

    private sealed class RecordingImageProvider : IImageGenerationProvider
    {
        public List<SceneSpecification> Specs { get; } = [];
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            Specs.Add(specification);
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
    }

    private sealed class FailingBeatTextImageProvider(string failingBeatText) : IImageGenerationProvider
    {
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default) =>
            specification.BeatText == failingBeatText
                ? throw new InvalidOperationException("IMAGE_PROVIDER_TEST_FAILURE")
                : Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
    }
}
