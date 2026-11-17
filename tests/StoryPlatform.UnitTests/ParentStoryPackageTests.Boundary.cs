using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class ParentStoryPackageTests
{
    [Fact]
    public async Task Parent_package_media_coordinator_strips_nested_audience_and_preserves_internal_tts_pinning()
    {
        var f = new Fixture();
        await f.Initialize();
        var (p, _) = await QueueMedia(f, new(Images: true, Narration: true), child: 3);
        var packageId = p.Id;
        var version = f.Store.Items<StoryVersion>().Single(x => x.Id == p.StoryVersionId);

        var voice = new ConfigurableVoice();
        var capturingImages = new CapturingBoundaryImageProvider();
        var result = await MediaWorker(f, voice, capturingImages).ProcessNextAsync(default);

        Assert.True(result.Success, result.ErrorCode);
        var persistedPackage = f.Store.Items<ParentStoryPackage>().Single(x => x.Id == packageId);
        Assert.Equal("Review", persistedPackage.Status);
        Assert.NotEmpty(capturingImages.Payloads);

        // Internal MediaContext keeps nested acceptedContext.audience and ttsConfigurationFingerprint.
        var persistedContext = f.Store.Items<MediaContext>().Single(x => x.ParentStoryPackageId == packageId);
        using var internalDoc = JsonDocument.Parse(persistedContext.ContextJson);
        var acceptedContext = internalDoc.RootElement.GetProperty("storyFacts").GetProperty("acceptedContext");
        Assert.True(acceptedContext.TryGetProperty("audience", out _));
        Assert.Equal(voice.ConfigurationFingerprint, acceptedContext.GetProperty("ttsConfigurationFingerprint").GetString());

        // Provider payload receives only projected story facts & visual design.
        foreach (var payload in capturingImages.Payloads)
        {
            using var providerDoc = JsonDocument.Parse(payload);
            var storyFacts = providerDoc.RootElement.GetProperty("storyFacts");
            Assert.Equal(version.Title, storyFacts.GetProperty("title").GetString());
            Assert.Equal(version.Content, storyFacts.GetProperty("canonicalContent").GetString());
            Assert.DoesNotContain("acceptedContext", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("acceptedInput", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("audience", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("ttsConfigurationFingerprint", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("childId", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("childProfileId", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("sourceStoryVersionId", payload, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Parent_package_media_coordinator_fails_permanently_on_invalid_persisted_context_without_provider_or_retry()
    {
        var f = new Fixture();
        await f.Initialize();
        var (p, job) = await QueueMedia(f, new(Images: true), child: 3);
        var packageId = p.Id;
        var jobId = job.Id;

        await f.Store.Uow.Object.Repository<MediaContext>().AddAsync(new MediaContext
        {
            StoryVersionId = p.StoryVersionId,
            ParentStoryPackageId = packageId,
            Revision = p.Revision,
            ContextJson = """{"schemaVersion":1,"storyFacts":{"title":"Old","canonicalContent":"Body"}}"""
        });

        var voice = new ConfigurableVoice();
        var images = new CountingImage();
        var result = await MediaWorker(f, voice, images).ProcessNextAsync(default);

        Assert.False(result.Success);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", result.ErrorCode);
        Assert.Equal(0, images.Calls);
        Assert.Equal(0, voice.Calls);

        // Re-read persisted rows from f.Store after rollback/commit.
        var persistedJob = f.Store.Items<StoryGenerationJob>().Single(x => x.Id == jobId);
        var persistedPackage = f.Store.Items<ParentStoryPackage>().Single(x => x.Id == packageId);
        Assert.Equal(GenerationJobStatus.Failed, persistedJob.Status);
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", persistedJob.ErrorCode);
        Assert.Null(persistedJob.NextAttemptAt);
        Assert.Equal("Failed", persistedPackage.Status);
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", persistedPackage.LastErrorCode);
    }

    [Fact]
    public async Task Parent_package_ready_asset_reuse_does_not_call_image_provider_or_modify_delivered_references()
    {
        var f = new Fixture();
        await f.Initialize();
        var (p, _) = await QueueMedia(f, new(Images: true), child: 3);
        var packageId = p.Id;

        var scenes = await ParentStoryPackageLayout.EnsureAsync(
            f.Store.Uow.Object,
            f.Store.Items<StoryVersion>().Single(x => x.Id == p.StoryVersionId),
            new StorySegmentService(),
            default);
        Assert.True(scenes.Count >= 2);

        var expectedUrls = new Dictionary<int, string>();
        foreach (var scene in scenes.OrderBy(x => x.SceneIndex))
        {
            var beat = await f.Store.Uow.Object.Repository<IllustrationBeat>().AddAsync(new IllustrationBeat
            {
                StorySceneId = scene.Id,
                BeatOrder = 1,
                StartOffset = 0,
                EndOffset = scene.SceneText.Length,
                VisualFocus = $"Main visual moment {scene.SceneIndex}"
            });
            var url = $"already-delivered-image-{scene.SceneIndex}.png";
            var readyAsset = await f.Store.Uow.Object.Repository<MediaAsset>().AddAsync(new MediaAsset
            {
                StoryVersionId = p.StoryVersionId,
                ParentStoryPackageId = packageId,
                StorySceneId = scene.Id,
                SceneIndex = scene.SceneIndex,
                IllustrationBeatId = beat.Id,
                Type = MediaType.Illustration,
                Status = MediaStatus.Ready,
                ValidationStatus = ValidationStatus.Passed,
                Url = url,
                IsCurrent = true
            });
            expectedUrls[readyAsset.Id] = url;
        }

        var voice = new ConfigurableVoice();
        var images = new CountingImage();
        var result = await MediaWorker(f, voice, images).ProcessNextAsync(default);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(0, images.Calls);
        Assert.Equal(0, voice.Calls);

        var persistedPackage = f.Store.Items<ParentStoryPackage>().Single(x => x.Id == packageId);
        var manifest = JsonSerializer.Deserialize<ParentPackageManifest>(persistedPackage.ManifestJson, ParentPackageContext.Json)!;
        Assert.Equal(expectedUrls.Keys.OrderBy(x => x), manifest.MediaAssetIds.OrderBy(x => x));
        foreach (var (assetId, expectedUrl) in expectedUrls)
        {
            var asset = f.Store.Items<MediaAsset>().Single(x => x.Id == assetId);
            Assert.Equal(expectedUrl, asset.Url);
        }

        // Confirm and deliver, then verify manifest and asset URLs remain unchanged.
        var current = await f.Packages.GetAsync(1, f.StoryId, packageId, default);
        var confirmed = await f.Packages.ConfirmAsync(1, f.StoryId, packageId,
            new() { OperationId = "confirm-ready-reuse", PackageToken = current.PackageToken }, default);
        var grant = Assert.Single(await f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver-ready-reuse", confirmed), default));
        Assert.Equal(packageId, grant.ParentStoryPackageId);
        var deliveredPackage = f.Store.Items<ParentStoryPackage>().Single(x => x.Id == packageId);
        Assert.Equal(persistedPackage.ManifestJson, deliveredPackage.ManifestJson);
        foreach (var (assetId, expectedUrl) in expectedUrls)
            Assert.Equal(expectedUrl, f.Store.Items<MediaAsset>().Single(x => x.Id == assetId).Url);
    }

    private sealed class CapturingBoundaryImageProvider : IImageGenerationProvider
    {
        public List<string> Payloads { get; } = [];

        public Task<GeneratedMedia> GenerateAsync(
            SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            Payloads.Add(ProviderMediaContextSerializer.Serialize(specification.MediaContext));
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
    }
}
