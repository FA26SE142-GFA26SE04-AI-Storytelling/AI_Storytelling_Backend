using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class MediaGenerationServiceTests
{
    [Fact]
    public async Task Progress_identifies_failed_image_and_audio_by_scene_and_item_number()
    {
        var store = Seed();
        var service = Create(store);
        Assert.True((await service.RunPackageAsync()).Success);
        var assets = store.Items<MediaAsset>();
        var image = assets.First(x => x.Type == MediaType.Illustration);
        var audio = assets.Last(x => x.Type == MediaType.TtsAudio);
        image.Status = audio.Status = MediaStatus.Failed;
        var task = store.Items<MediaAssetGenerationTask>().Single(x => x.MediaAssetId == image.Id);
        task.Status = GenerationJobStatus.Failed;
        task.ErrorCode = "MEDIA_PROVIDER_TIMEOUT";
        var audioBranch = store.Items<StoryGenerationJob>().Single(x => x.Operation == GenerationJobOperation.GenerateTts);
        audioBranch.Status = GenerationJobStatus.Failed;
        audioBranch.ErrorCode = "TTS_PROVIDER_TIMEOUT";
        var progress = await service.GetProgressAsync(1, 1);
        Assert.Equal(2, progress.Issues.Count);
        var imageIssue = Assert.Single(progress.Issues, x => x.Type == "Illustration");
        Assert.Equal(image.StorySceneId, imageIssue.SceneId);
        Assert.Equal(image.IllustrationBeatId, imageIssue.TargetId);
        Assert.Equal(1, imageIssue.ItemNumber);
        Assert.Equal("MEDIA_PROVIDER_TIMEOUT", imageIssue.ErrorCode);
        var audioIssue = Assert.Single(progress.Issues, x => x.Type == "TtsAudio");
        Assert.Equal(audio.StorySceneId, audioIssue.SceneId);
        Assert.Equal(audio.StorySegmentId, audioIssue.TargetId);
        Assert.Equal("TTS_PROVIDER_TIMEOUT", audioIssue.ErrorCode);
        Assert.Equal("Failed", audioIssue.Status);
    }

    [Fact]
    public async Task Progress_reports_scheduled_retry_and_omits_ready_obsolete_and_resolved_assets()
    {
        var store = Seed();
        var service = Create(store);
        Assert.True((await service.RunPackageAsync()).Success);
        var asset = store.Items<MediaAsset>().First(x => x.Type == MediaType.Illustration);
        var task = store.Items<MediaAssetGenerationTask>().Single(x => x.MediaAssetId == asset.Id);
        asset.Status = MediaStatus.Failed;
        task.Status = GenerationJobStatus.Pending;
        task.ErrorCode = "GEMINI_IMAGE_HTTP_429";
        task.NextAttemptAt = DateTime.UtcNow.AddMinutes(1);
        store.Seed(new MediaAsset { Id = 10001, StoryVersionId = asset.StoryVersionId, StorySceneId = asset.StorySceneId,
            IllustrationBeatId = asset.IllustrationBeatId, Type = MediaType.Illustration, Status = MediaStatus.Failed,
            RequestedPackageRevision = 999, IsCurrent = false, ReplacesMediaAssetId = asset.Id });
        store.Seed(new MediaAsset { Id = 10002, StoryVersionId = asset.StoryVersionId, StorySceneId = asset.StorySceneId,
            IllustrationBeatId = asset.IllustrationBeatId, Type = MediaType.Illustration, Status = MediaStatus.Failed,
            IsCurrent = false, ReplacesMediaAssetId = asset.Id, ReplacementResolvedAt = DateTime.UtcNow });
        var issue = Assert.Single((await service.GetProgressAsync(1, 1)).Issues);
        Assert.Equal("Pending", issue.Status);
        Assert.Equal(task.NextAttemptAt, issue.NextRetryAt);
        Assert.Equal("GEMINI_IMAGE_HTTP_429", issue.ErrorCode);
        task.Status = GenerationJobStatus.Processing;
        Assert.Equal("Processing", Assert.Single((await service.GetProgressAsync(1, 1)).Issues).Status);
        asset.Status = MediaStatus.Ready;
        Assert.Empty((await service.GetProgressAsync(1, 1)).Issues);
    }

    [Fact]
    public async Task Progress_never_exposes_raw_exception_text_as_an_error_code()
    {
        var store = Seed();
        var service = Create(store);
        Assert.True((await service.RunPackageAsync()).Success);
        var asset = store.Items<MediaAsset>().First(x => x.Type == MediaType.Illustration);
        asset.Status = MediaStatus.Failed;
        asset.LastValidationReason = "private provider output";
        var task = store.Items<MediaAssetGenerationTask>().Single(x => x.MediaAssetId == asset.Id);
        task.Status = GenerationJobStatus.Failed;
        task.ErrorCode = "private error with URL or credentials";
        Assert.Equal("MEDIA_GENERATION_FAILED", Assert.Single((await service.GetProgressAsync(1, 1)).Issues).ErrorCode);
    }
}
