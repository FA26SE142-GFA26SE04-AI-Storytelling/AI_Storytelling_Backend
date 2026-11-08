using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Features.Stories.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class CreatorMediaUploadTests
{
    private static readonly GeneratedMedia Png = new([137,80,78,71,13,10,26,10,0,0,0,0], "image/png");
    private static readonly GeneratedMedia Wav = new([82,73,70,70,0,0,0,0,87,65,86,69], "audio/wav");
    private sealed class Providers : IImageGenerationProvider, ITtsProvider
    {
        public int Images, Audio;
        public bool FailAudioOnce;
        public int DeferredImages;
        public string? FailAudioText;
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification spec, CancellationToken ct = default)
        {
            Images++;
            if (DeferredImages>0) { DeferredImages--; throw new TransientMediaGenerationException("MEDIA_PROVIDER_COOLDOWN",retryAfter:TimeSpan.FromSeconds(120)); }
            return Task.FromResult(Png);
        }
        public Task<GeneratedMedia> GenerateAsync(string text, CancellationToken ct = default)
        {
            Audio++;
            if (text == FailAudioText) throw new PermanentMediaGenerationException("TEST_LEARNING_AUDIO_FAILED");
            if (FailAudioOnce && Audio == 1) throw new TransientMediaGenerationException("GEMINI_TTS_HTTP_429", retryAfter: TimeSpan.FromSeconds(120));
            return Task.FromResult(Wav);
        }
    }
    private sealed class Storage : IMediaStorage
    {
        public readonly Dictionary<string, GeneratedMedia> Files = [];
        public int Writes;
        public bool FailReadOnce;
        public Action? AfterUpload;
        public async Task<string> UploadAsync(string path, Stream content, string mime, CancellationToken ct = default)
        { using var buffer = new MemoryStream(); await content.CopyToAsync(buffer, ct); Files.Add(path, new(buffer.ToArray(), mime)); Writes++; AfterUpload?.Invoke(); return path; }
        public Task<GeneratedMedia> ReadAsync(string path, CancellationToken ct = default)
        {
            if (FailReadOnce) { FailReadOnce=false; throw new TimeoutException(); }
            return Task.FromResult(Files[path]);
        }
        public Task<string> GetSignedUrlAsync(string path, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult("https://media.test/" + path);
        public Task DeleteAsync(string path, CancellationToken ct = default) { Files.Remove(path); return Task.CompletedTask; }
    }
    private sealed class Fixture
    {
        public StoryReviewServiceTests.FakeUnitOfWork Store = null!;
        public MediaGenerationService Media = null!;
        public Providers Providers = new();
        public Storage Storage = new();
        public StoryMediaPackage Package = null!;
        public async Task Initialize(IMediaAlignmentEvaluator? evaluator = null)
        {
            var setup = CreatorManualMediaTests.Setup(); Store = setup.Store;
            await setup.Service.ConfirmAsync(1, 1, new(10, "rev1", "choose"));
            Media = MediaGenerationServiceTests.Create(Store, image: Providers, tts: Providers, storage: Storage, evaluator: evaluator);
            Assert.True((await Media.ProcessNextAsync()).Success);
            Package = await Media.GetPackageAsync(1, 1);
        }
        public CreatorMediaCommand Command(string key) => new(key, 10, Store.Items<StoryVersion>().Single().MediaState.MediaPackageRevision,
            Store.Items<Story>().Single().Status == StoryStatus.Ready);
        public Task<CreatorMediaCommandResult> Upload(int id, bool image, string key) => Media.SelectCreatorMediaAsync(1, 1, id, image,
            Command(key), new(Command(key), "Own work", "I have the rights"), image ? Png : Wav);
        public async Task Fill(bool uploadImages = true, bool uploadAudio = true)
        {
            foreach (var scene in Package.Scenes)
            {
                foreach (var beat in scene.Illustrations)
                    if (uploadImages) await Upload(beat.BeatId, true, "image-" + beat.BeatId);
                    else await Media.SelectCreatorMediaAsync(1,1,beat.BeatId,true,Command("generate-image-" + beat.BeatId),null,null);
                foreach (var segment in scene.Audio)
                    if (uploadAudio) await Upload(segment.SegmentId,false,"audio-" + segment.SegmentId);
                    else await Media.SelectCreatorMediaAsync(1,1,segment.SegmentId,false,Command("generate-audio-" + segment.SegmentId),null,null);
            }
            for (var i=0;i<50;i++)
            {
                await Media.ProcessBranchNextAsync(MediaType.Illustration); await Media.ProcessBranchNextAsync(MediaType.TtsAudio);
                await Media.ProcessNextAsync();
                if (Store.Items<Story>().Single().Status == StoryStatus.MediaReview) return;
            }
            throw new InvalidOperationException("Choice package did not complete");
        }
    }

    [Fact]
    public async Task Choose_prepares_text_targets_without_generating_and_waits_for_selection()
    {
        var f=new Fixture(); await f.Initialize();
        Assert.NotEmpty(f.Package.Scenes); Assert.All(f.Package.Scenes,x => { Assert.NotEmpty(x.Text); Assert.NotEmpty(x.Illustrations); Assert.NotEmpty(x.Audio); });
        Assert.Equal("choose",f.Package.MediaMode); Assert.True(f.Package.IsAwaitingSelection);
        Assert.Equal(0,f.Providers.Images); Assert.Equal(0,f.Providers.Audio);
        for(var i=0;i<3;i++) Assert.False((await f.Media.ProcessNextAsync()).JobFound);
        Assert.Equal(StoryStatus.MediaProcessing,f.Store.Items<Story>().Single().Status);
        Assert.All(f.Store.Items<MediaAsset>(),x => Assert.Equal(MediaAssetSource.Unspecified,x.Source));
        await Assert.ThrowsAsync<ConflictException>(()=>f.Media.ConfirmAsync(1,1,new(10,1)));
    }

    [Fact]
    public async Task SetCover_StoresSceneReference_ValidatesAssetAndCrop_AndBlocksPublishedStory()
    {
        var f=new Fixture(); await f.Initialize(); await f.Fill();
        var package=await f.Media.GetPackageAsync(1,1);
        var assetId=package.Scenes.SelectMany(x=>x.Illustrations).First(x=>x.AssetId.HasValue && x.Status=="Ready").AssetId!.Value;
        var story=f.Store.Items<Story>().Single();
        var cover=new StoryCoverService(f.Store);

        await cover.SetCoverAsync(1,1,new(assetId,0.1,0.2,0.5,0.5));
        Assert.Equal($"scene:{assetId}#crop=0.1,0.2,0.5,0.5",story.CoverImageUrl);
        await cover.SetCoverAsync(1,1,new(assetId));
        Assert.Equal($"scene:{assetId}",story.CoverImageUrl);

        await Assert.ThrowsAsync<BadRequestException>(() => cover.SetCoverAsync(1,1,new(assetId,0.8,0.2,0.5,0.5)));
        await Assert.ThrowsAsync<BadRequestException>(() => cover.SetCoverAsync(1,1,new(assetId,0.1,null,null,null)));
        await Assert.ThrowsAsync<BadRequestException>(() => cover.SetCoverAsync(1,1,new(999999)));

        await cover.SetCoverAsync(1,1,new(null));
        Assert.Null(story.CoverImageUrl);

        story.IsPublished=true;
        await Assert.ThrowsAsync<ConflictException>(() => cover.SetCoverAsync(1,1,new(assetId)));
        story.IsPublished=false; story.ArchivedAt=DateTime.UtcNow;
        await Assert.ThrowsAsync<ConflictException>(() => cover.SetCoverAsync(1,1,new(assetId)));
        story.ArchivedAt=null; story.Status=StoryStatus.Rejected;
        await Assert.ThrowsAsync<ConflictException>(() => cover.SetCoverAsync(1,1,new(assetId)));
    }

    [Fact]
    public async Task SetCover_IsAllowedOnARemovedStoryWithoutANewRevision()
    {
        var f=new Fixture(); await f.Initialize(); await f.Fill();
        var package=await f.Media.GetPackageAsync(1,1);
        var assetId=package.Scenes.SelectMany(x=>x.Illustrations).First(x=>x.AssetId.HasValue && x.Status=="Ready").AssetId!.Value;
        var cover=new StoryCoverService(f.Store);
        var story=f.Store.Items<Story>().Single();
        var version=f.Store.Items<StoryVersion>().Single(x=>x.IsCurrent);
        // Đã từng nộp duyệt, được duyệt, rồi Moderator đồng ý gỡ: không công khai, không có mục nào đang mở.
        version.Moderation.ReviewStatus=VersionReviewStatus.Approved;
        f.Store.Seed(new ModerationItem { Id=1, StoryId=1, StoryVersionId=version.Id, ItemType=ModerationItemType.PublicStoryReview, Status=ModerationItemStatus.Approved });
        f.Store.Seed(new ModerationItem { Id=2, StoryId=1, StoryVersionId=version.Id, ItemType=ModerationItemType.PublicStoryRemoval, Status=ModerationItemStatus.Approved, RemovedAt=DateTime.UtcNow });
        story.IsPublished=false;

        await cover.SetCoverAsync(1,1,new(assetId));
        Assert.Equal($"scene:{assetId}",story.CoverImageUrl);

        story.IsPublished=true;
        await Assert.ThrowsAsync<ConflictException>(() => cover.SetCoverAsync(1,1,new(assetId)));
    }

    [Fact]
    public async Task SetCover_IsBlockedWhileACreatorReviewIsOpen()
    {
        var f=new Fixture(); await f.Initialize(); await f.Fill();
        var package=await f.Media.GetPackageAsync(1,1);
        var assetId=package.Scenes.SelectMany(x=>x.Illustrations).First(x=>x.AssetId.HasValue && x.Status=="Ready").AssetId!.Value;
        var cover=new StoryCoverService(f.Store);
        f.Store.Seed(new ModerationItem { Id=1, StoryId=1, ItemType=ModerationItemType.PublicStoryReview, Status=ModerationItemStatus.Pending });
        await Assert.ThrowsAsync<ConflictException>(() => cover.SetCoverAsync(1,1,new(assetId)));
    }

    [Fact]
    public async Task CoverReference_ParsesAndRejectsMalformedValues()
    {
        Assert.True(StoryCover.TryParse("scene:12#crop=0.1,0.2,0.5,0.5", out var id, out var tail));
        Assert.Equal(12,id); Assert.Equal("#crop=0.1,0.2,0.5,0.5",tail);
        Assert.True(StoryCover.TryParse("scene:7", out id, out tail)); Assert.Equal(7,id); Assert.Equal("",tail);
        Assert.False(StoryCover.TryParse("scene:abc", out _, out _));
        Assert.False(StoryCover.TryParse("scene:0", out _, out _));
        Assert.False(StoryCover.TryParse("https://x/y.png", out _, out _));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task PublishedCreatorPackage_IsReadableOnlyAfterPublish_AndHidesWorkflowFields()
    {
        var f=new Fixture(); await f.Initialize();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Media.GetPublishedCreatorPackageAsync(1));
        var story=f.Store.Items<Story>().Single(); story.IsPublished=true; story.Status=StoryStatus.Ready;
        var package=await f.Media.GetPublishedCreatorPackageAsync(1);
        Assert.Equal(f.Package.Scenes.Count, package.Scenes.Count);
        Assert.Empty(package.Replacements); Assert.Null(package.ConfirmedByUserId);
        story.ArchivedAt=DateTime.UtcNow;
        await Assert.ThrowsAsync<NotFoundException>(() => f.Media.GetPublishedCreatorPackageAsync(1));
    }

    [Theory]
    [InlineData(true,true)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(false,false)]
    public async Task Upload_and_AI_can_mix_and_require_whole_package_confirmation(bool images,bool audio)
    {
        var f=new Fixture(); await f.Initialize(); await f.Fill(images,audio);
        Assert.Equal(images?0:f.Package.Scenes.Sum(x=>x.Illustrations.Count), f.Providers.Images);
        Assert.Equal(audio?0:f.Package.Scenes.Sum(x=>x.Audio.Count), f.Providers.Audio);
        var p=await f.Media.GetPackageAsync(1,1); Assert.True(p.IsAwaitingConfirmation); Assert.False(p.IsReady);
        Assert.All(f.Store.Items<MediaAsset>().Where(x=>x.Source==MediaAssetSource.UserUpload),x=>
        { Assert.Equal(ValidationStatus.Passed,x.ValidationStatus); Assert.Contains("I have the rights",x.ValidationResultJson); if(x.Type==MediaType.TtsAudio) Assert.Null(x.WordTimings); });
        var confirmed=await f.Media.ConfirmAsync(1,1,new(10,p.PackageRevision)); Assert.True(confirmed.IsReady);
        Assert.Single(f.Store.Items<StoryVersion>());
    }

    [Fact]
    public async Task Upload_replay_does_not_store_twice_and_changed_file_conflicts()
    {
        var f=new Fixture(); await f.Initialize(); var id=f.Package.Scenes[0].Illustrations[0].BeatId;
        var first=await f.Upload(id,true,"replay"); var second=await f.Upload(id,true,"replay");
        Assert.Equal(first,second); Assert.Equal(1,f.Storage.Writes); Assert.Single(f.Store.Items<MediaAssetGenerationTask>());
        var changed=Png with {Content=Png.Content.Concat(new byte[]{1}).ToArray()};
        await Assert.ThrowsAsync<ConflictException>(()=>f.Media.SelectCreatorMediaAsync(1,1,id,true,f.Command("replay"),
            new(f.Command("replay"),"Own work","I have the rights"),changed));
        Assert.Equal(1,f.Storage.Writes);
    }

    [Fact]
    public async Task Wrong_actor_scope_target_revision_and_invalid_file_are_blocked()
    {
        var f=new Fixture(); await f.Initialize(); var id=f.Package.Scenes[0].Illustrations[0].BeatId;
        await Assert.ThrowsAsync<BadRequestException>(()=>f.Media.SelectCreatorMediaAsync(1,1,id,true,f.Command("bad-mime"),
            new(f.Command("bad-mime"),"Own work","rights"),Png with {MimeType="image/jpeg"}));
        await Assert.ThrowsAsync<BadRequestException>(()=>f.Media.SelectCreatorMediaAsync(1,1,id,true,f.Command("no-rights"),
            new(f.Command("no-rights"),"Own work",""),Png));
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Media.SelectCreatorMediaAsync(1,1,999,true,f.Command("target"),null,null));
        await Assert.ThrowsAsync<ConflictException>(()=>f.Media.SelectCreatorMediaAsync(1,1,id,true,f.Command("revision") with{PackageRevision=2},null,null));
        f.Store.Items<Story>().Single().Source=StorySource.Ai;
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Media.SelectCreatorMediaAsync(1,1,id,true,f.Command("source"),null,null));
        Assert.Equal(0,f.Storage.Writes);
    }

    [Fact]
    public async Task Replacing_uploaded_image_keeps_old_file_and_requires_confirmation_again()
    {
        var f=new Fixture(); await f.Initialize(); await f.Fill(); await f.Media.ConfirmAsync(1,1,new(10,1));
        var old=f.Store.Items<MediaAsset>().First(x=>x.Type==MediaType.Illustration);
        var path=old.Url;
        var replacement=await f.Upload(old.IllustrationBeatId!.Value,true,"replace");
        Assert.Equal(2,replacement.PackageRevision); Assert.True(old.IsCurrent); Assert.Equal(path,old.Url);
        var generated=await f.Media.ProcessBranchNextAsync(MediaType.Illustration); Assert.True(generated.Success,generated.ErrorCode);
        await f.Media.ProcessNextAsync();
        Assert.False(old.IsCurrent); Assert.True(f.Storage.Files.ContainsKey(path!));
        Assert.True((await f.Media.GetPackageAsync(1,1)).IsAwaitingConfirmation); Assert.Single(f.Store.Items<StoryVersion>());
    }

    [Theory]
    [InlineData(VersionReviewStatus.Pending)]
    [InlineData(VersionReviewStatus.Approved)]
    [InlineData(VersionReviewStatus.ChangesRequested)]
    public async Task Submitted_versions_reject_upload_before_storage(VersionReviewStatus state)
    {
        var f=new Fixture(); await f.Initialize();
        f.Store.Items<StoryVersion>().Single().Moderation.ReviewStatus=state;
        await Assert.ThrowsAsync<ConflictException>(()=>f.Upload(f.Package.Scenes[0].Illustrations[0].BeatId,true,"locked"));
        Assert.Equal(0,f.Storage.Writes);
    }

    [Fact]
    public async Task Another_author_and_suspended_creator_cannot_upload()
    {
        var f=new Fixture(); await f.Initialize(); var id=f.Package.Scenes[0].Illustrations[0].BeatId;
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Media.SelectCreatorMediaAsync(2,1,id,true,f.Command("other"),null,null));
        f.Store.Items<UserAccount>().Single().Status=AccountStatus.Suspended;
        await Assert.ThrowsAsync<ForbiddenException>(()=>f.Upload(id,true,"inactive"));
        Assert.Equal(0,f.Storage.Writes);
    }

    [Fact]
    public async Task Revoked_creator_access_before_worker_claim_does_not_read_or_generate()
    {
        var f=new Fixture(); await f.Initialize();
        await f.Upload(f.Package.Scenes[0].Illustrations[0].BeatId,true,"before-suspension");
        f.Store.Items<UserAccount>().Single().Status=AccountStatus.Suspended;
        var result=await f.Media.ProcessBranchNextAsync(MediaType.Illustration);
        Assert.False(result.Success); Assert.Equal(0,f.Providers.Images);
        Assert.All(f.Store.Items<MediaAsset>(),x=>Assert.NotEqual(MediaStatus.Ready,x.Status));
    }

    [Fact]
    public async Task Uploaded_narration_still_generates_required_learning_media()
    {
        var f=new Fixture(); await f.Initialize();
        f.Store.Seed(new StoryVocabulary { Id=90, StoryVersionId=10, Term="Na" });
        f.Store.Seed(new ListeningInteraction { Id=91, StoryVersionId=10, StorySceneId=f.Package.Scenes[0].SceneId,
            Type=ListeningInteractionType.Checkpoint, AnchorOffset=f.Store.Items<StoryVersion>().Single().Content!.Length, Prompt="Na giúp ai?" });
        await f.Fill();
        Assert.Contains(f.Store.Items<MediaAsset>(),x=>x.Type==MediaType.VocabularyAudio && x.Status==MediaStatus.Ready);
        Assert.Contains(f.Store.Items<MediaAsset>(),x=>x.Type==MediaType.ListeningAudio && x.Status==MediaStatus.Ready);
        Assert.True((await f.Media.GetPackageAsync(1,1)).IsAwaitingConfirmation);
    }

    [Fact]
    public async Task Upload_timeout_retries_original_file_and_hash_failure_allows_corrected_upload()
    {
        var f=new Fixture(); await f.Initialize(); var id=f.Package.Scenes[0].Illustrations[0].BeatId;
        await f.Upload(id,true,"timeout-upload"); f.Storage.FailReadOnce=true;
        Assert.False((await f.Media.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        var task=Assert.Single(f.Store.Items<MediaAssetGenerationTask>());
        Assert.Equal(GenerationJobStatus.Pending,task.Status);
        Assert.Equal(MediaTaskKind.ValidateUploadedImage,task.Kind);
        task.NextAttemptAt=DateTime.UtcNow.AddMinutes(-1);
        var asset=f.Store.Items<MediaAsset>().Single(x=>x.Id==task.MediaAssetId);
        f.Storage.Files[asset.Url!]=Png with { Content=Png.Content.Concat(new byte[]{1}).ToArray() };
        var invalid=await f.Media.ProcessBranchNextAsync(MediaType.Illustration);
        Assert.Equal("MEDIA_UPLOAD_HASH_MISMATCH",invalid.ErrorCode);
        var corrected=await f.Upload(id,true,"corrected-upload");
        Assert.NotEqual(asset.Id,corrected.AssetId); Assert.False(asset.IsCurrent);
        Assert.True((await f.Media.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        Assert.True(f.Storage.Files.ContainsKey(asset.Url!)); Assert.Equal(0,f.Providers.Images);
        Assert.Equal(GenerationJobStatus.Cancelled,task.Status);
        foreach(var scene in f.Package.Scenes)
        {
            foreach(var beat in scene.Illustrations.Where(x=>x.BeatId!=id)) await f.Upload(beat.BeatId,true,"remaining-image-"+beat.BeatId);
            foreach(var segment in scene.Audio) await f.Upload(segment.SegmentId,false,"remaining-audio-"+segment.SegmentId);
        }
        for(var i=0;i<20;i++)
        {
            await f.Media.ProcessBranchNextAsync(MediaType.Illustration);
            await f.Media.ProcessBranchNextAsync(MediaType.TtsAudio);
            await f.Media.ProcessNextAsync();
            if(f.Store.Items<Story>().Single().Status==StoryStatus.MediaReview) break;
        }
        Assert.Equal(StoryStatus.MediaReview,f.Store.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task Failed_storage_transaction_cleans_only_the_new_unreferenced_file()
    {
        var f=new Fixture(); await f.Initialize(); var id=f.Package.Scenes[0].Illustrations[0].BeatId;
        f.Storage.AfterUpload=()=>f.Store.Items<StoryVersion>().Single().MediaState.MediaPackageRevision++;
        await Assert.ThrowsAsync<ConflictException>(()=>f.Upload(id,true,"source-changed"));
        Assert.Empty(f.Storage.Files); Assert.Empty(f.Store.Items<MediaAssetGenerationTask>());
    }

    [Fact]
    public void Upload_validator_rejects_oversize_and_mismatched_headers()
    {
        var validator=new CreatorMediaUploadValidator();
        Assert.Throws<BadRequestException>(()=>validator.Validate(new(new byte[10*1024*1024+1],"image/png"),true));
        Assert.Throws<BadRequestException>(()=>validator.Validate(Png,false));
        validator.Validate(Png,true); validator.Validate(Wav,false);
    }

    [Fact]
    public async Task Selected_AI_audio_honors_retry_after_without_replacing_uploaded_images()
    {
        var f=new Fixture(); await f.Initialize();
        foreach(var scene in f.Package.Scenes)
        {
            foreach(var beat in scene.Illustrations) await f.Upload(beat.BeatId,true,"quota-image-"+beat.BeatId);
            foreach(var segment in scene.Audio) await f.Media.SelectCreatorMediaAsync(1,1,segment.SegmentId,false,f.Command("quota-audio-"+segment.SegmentId),null,null);
        }
        f.Providers.FailAudioOnce=true;
        var failed=await f.Media.ProcessBranchNextAsync(MediaType.TtsAudio);
        Assert.False(failed.Success); Assert.Equal("GEMINI_TTS_HTTP_429",failed.ErrorCode);
        var task=f.Store.Items<MediaAssetGenerationTask>().Single(x=>x.Kind==MediaTaskKind.GenerateChosenAudio);
        Assert.Equal(GenerationJobStatus.Pending,task.Status); Assert.True(task.NextAttemptAt>DateTime.UtcNow.AddSeconds(119));
        var progress=await f.Media.GetProgressAsync(1,1); Assert.Equal("GEMINI_TTS_HTTP_429",progress.Audio.ErrorCode); Assert.NotNull(progress.Audio.NextRetryAt);
        task.NextAttemptAt=DateTime.UtcNow.AddMinutes(-1);
        Assert.True((await f.Media.ProcessBranchNextAsync(MediaType.TtsAudio)).Success);
        for(var i=0;i<f.Package.Scenes.Sum(x=>x.Illustrations.Count);i++) Assert.True((await f.Media.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        await f.Media.ProcessNextAsync();
        Assert.Equal(2,f.Providers.Audio); Assert.Equal(0,f.Providers.Images);
        Assert.True((await f.Media.GetPackageAsync(1,1)).IsAwaitingConfirmation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_package_retry_preserves_upload_provenance_and_revalidates_same_file(bool image)
    {
        var f=new Fixture(); await f.Initialize();
        var target=image ? f.Package.Scenes[0].Illustrations[0].BeatId : f.Package.Scenes[0].Audio[0].SegmentId;
        var selected=await f.Upload(target,image,"root-retry-upload");
        f.Storage.FailReadOnce=true;
        Assert.False((await f.Media.ProcessBranchNextAsync(image ? MediaType.Illustration : MediaType.TtsAudio)).Success);
        var asset=f.Store.Items<MediaAsset>().Single(x=>x.Id==selected.AssetId);
        var originalMetadata=asset.ValidationResultJson;
        var path=asset.Url;
        // Represent a terminal root failure after automatic retries are exhausted.
        var root=f.Store.Items<StoryGenerationJob>().Single(); root.Status=GenerationJobStatus.Failed;
        var task=Assert.Single(f.Store.Items<MediaAssetGenerationTask>()); task.Status=GenerationJobStatus.Failed;
        await f.Media.RetryAsync(1,1);
        Assert.Equal(originalMetadata,asset.ValidationResultJson);
        Assert.True((await f.Media.ProcessNextAsync()).Success);
        var result=await f.Media.ProcessBranchNextAsync(image ? MediaType.Illustration : MediaType.TtsAudio);
        Assert.True(result.Success,result.ErrorCode);
        Assert.Equal(MediaStatus.Ready,asset.Status); Assert.Equal(path,asset.Url);
        Assert.Contains("I have the rights",asset.ValidationResultJson);
        Assert.Contains("Own work",asset.ValidationResultJson);
        Assert.Equal(1,f.Storage.Writes); Assert.Equal(0,f.Providers.Images); Assert.Equal(0,f.Providers.Audio);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Package_retry_preserves_unresolved_uploaded_replacement(bool image)
    {
        var f=new Fixture(); await f.Initialize(); await f.Fill();
        await f.Media.ConfirmAsync(1,1,new(10,1));
        var old=f.Store.Items<MediaAsset>().First(x=>x.Type==(image?MediaType.Illustration:MediaType.TtsAudio));
        var target=image?old.IllustrationBeatId!.Value:old.StorySegmentId!.Value;
        var selected=await f.Upload(target,image,"retry-replacement");
        var candidate=f.Store.Items<MediaAsset>().Single(x=>x.Id==selected.AssetId);
        var metadata=candidate.ValidationResultJson;
        var writes=f.Storage.Writes;
        f.Storage.FailReadOnce=true;
        Assert.False((await f.Media.ProcessBranchNextAsync(image?MediaType.Illustration:MediaType.TtsAudio)).Success);
        var root=f.Store.Items<StoryGenerationJob>().Single(); root.Status=GenerationJobStatus.Failed;
        f.Store.Items<MediaAssetGenerationTask>().Single(x=>x.MediaAssetId==candidate.Id).Status=GenerationJobStatus.Failed;
        await f.Media.RetryAsync(1,1);
        Assert.Equal(metadata,candidate.ValidationResultJson);
        Assert.True(old.IsCurrent);
        Assert.True((await f.Media.ProcessNextAsync()).Success);
        Assert.True((await f.Media.ProcessBranchNextAsync(image?MediaType.Illustration:MediaType.TtsAudio)).Success);
        Assert.Equal(MediaStatus.Ready,candidate.Status);
        Assert.Contains("I have the rights",candidate.ValidationResultJson);
        Assert.Equal(writes,f.Storage.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Package_retry_resumes_learning_after_narration_is_ready_without_regenerating_it(bool uploadedNarration)
    {
        var f=new Fixture(); await f.Initialize();
        f.Store.Seed(new StoryVocabulary {Id=90,StoryVersionId=10,Term="learning-retry"});
        f.Providers.FailAudioText="learning-retry";
        foreach(var scene in f.Package.Scenes)
        {
            foreach(var beat in scene.Illustrations) await f.Upload(beat.BeatId,true,"learning-image-"+beat.BeatId);
            foreach(var segment in scene.Audio)
                if(uploadedNarration) await f.Upload(segment.SegmentId,false,"learning-audio-"+segment.SegmentId);
                else await f.Media.SelectCreatorMediaAsync(1,1,segment.SegmentId,false,f.Command("learning-ai-"+segment.SegmentId),null,null);
        }
        foreach(var _ in f.Package.Scenes.SelectMany(x=>x.Illustrations))
            Assert.True((await f.Media.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        Assert.False((await f.Media.ProcessBranchNextAsync(MediaType.TtsAudio)).Success);
        var narration=f.Store.Items<MediaAsset>().Single(x=>x.Type==MediaType.TtsAudio);
        Assert.Equal(MediaStatus.Ready,narration.Status);
        var narrationUrl=narration.Url;
        var writes=f.Storage.Writes;
        await f.Media.ProcessNextAsync();
        Assert.Equal(GenerationJobStatus.Failed,f.Store.Items<StoryGenerationJob>().Single().Status);
        f.Providers.FailAudioText=null;
        await f.Media.RetryAsync(1,1);
        Assert.True((await f.Media.ProcessNextAsync()).Success);
        var task=f.Store.Items<MediaAssetGenerationTask>().Single(x=>x.MediaAssetId==narration.Id);
        Assert.Equal(GenerationJobStatus.Pending,task.Status);
        Assert.True((await f.Media.ProcessBranchNextAsync(MediaType.TtsAudio)).Success);
        await f.Media.ProcessNextAsync();
        Assert.Equal(narrationUrl,narration.Url);
        Assert.Equal(writes+1,f.Storage.Writes); // Only the missing vocabulary audio is written.
        Assert.True((await f.Media.GetPackageAsync(1,1)).IsAwaitingConfirmation);
    }

    private sealed class QualityEvaluator(bool safetyFailure, int failures) : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
    {
        private int _alignmentCalls, _safetyCalls;
        Task<MediaEvaluationResult> IMediaAlignmentEvaluator.EvaluateAsync(SceneSpecification spec, GeneratedMedia media, CancellationToken ct)
            => Task.FromResult(new MediaEvaluationResult(!safetyFailure && ++_alignmentCalls<=failures ? MediaEvaluationDecision.Fail : MediaEvaluationDecision.Pass,"test alignment"));
        Task<MediaEvaluationResult> IMediaSafetyEvaluator.EvaluateAsync(SceneSpecification spec, GeneratedMedia media, CancellationToken ct)
            => Task.FromResult(new MediaEvaluationResult(safetyFailure && ++_safetyCalls<=failures ? MediaEvaluationDecision.Fail : MediaEvaluationDecision.Pass,"test safety"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chosen_AI_image_quality_failure_is_retried_until_a_checked_image_passes(bool safetyFailure)
    {
        var f=new Fixture(); await f.Initialize(new QualityEvaluator(safetyFailure,1));
        var beat=f.Package.Scenes[0].Illustrations[0].BeatId;
        await f.Media.SelectCreatorMediaAsync(1,1,beat,true,f.Command("quality-retry"),null,null);
        var first=await f.Media.ProcessBranchNextAsync(MediaType.Illustration);
        Assert.False(first.Success);
        var task=Assert.Single(f.Store.Items<MediaAssetGenerationTask>());
        Assert.Equal(GenerationJobStatus.Pending,task.Status); Assert.NotNull(task.NextAttemptAt);
        Assert.Equal(0,f.Storage.Writes); // A rejected image must never become a stored Ready image.
        task.NextAttemptAt=DateTime.UtcNow.AddMinutes(-1);
        Assert.True((await f.Media.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        Assert.Equal(2,f.Providers.Images); Assert.Equal(1,f.Storage.Writes);
        Assert.Equal(MediaStatus.Ready,f.Store.Items<MediaAsset>().Single(x=>x.Id==task.MediaAssetId).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chosen_AI_quality_retries_are_bounded_but_rejected_upload_is_not_regenerated(bool upload)
    {
        var f=new Fixture(); await f.Initialize(new QualityEvaluator(false,int.MaxValue));
        var beat=f.Package.Scenes[0].Illustrations[0].BeatId;
        if(upload) await f.Upload(beat,true,"bad-upload");
        else await f.Media.SelectCreatorMediaAsync(1,1,beat,true,f.Command("always-fails"),null,null);
        var count=upload?1:3;
        for(var i=0;i<count;i++)
        {
            Assert.False((await f.Media.ProcessBranchNextAsync(MediaType.Illustration)).Success);
            var current=Assert.Single(f.Store.Items<MediaAssetGenerationTask>());
            if(i<count-1) Assert.Equal(GenerationJobStatus.Pending,current.Status);
            current.NextAttemptAt=DateTime.UtcNow.AddMinutes(-1);
        }
        Assert.Equal(GenerationJobStatus.Failed,Assert.Single(f.Store.Items<MediaAssetGenerationTask>()).Status);
        Assert.Equal(upload?0:3,f.Providers.Images);
    }

    [Fact]
    public async Task Chosen_AI_provider_cooldown_preserves_attempt_budget_and_retry_after()
    {
        var f=new Fixture(); await f.Initialize(); f.Providers.DeferredImages=4;
        await f.Media.SelectCreatorMediaAsync(1,1,f.Package.Scenes[0].Illustrations[0].BeatId,true,f.Command("cooldown"),null,null);
        for(var i=0;i<4;i++)
        {
            Assert.False((await f.Media.ProcessBranchNextAsync(MediaType.Illustration)).Success);
            var task=Assert.Single(f.Store.Items<MediaAssetGenerationTask>());
            Assert.Equal(GenerationJobStatus.Pending,task.Status); Assert.Equal(0,task.AttemptNo);
            Assert.Equal(0,f.Store.Items<MediaAsset>().Single(x=>x.Id==task.MediaAssetId).AttemptCount);
            Assert.True(task.NextAttemptAt>DateTime.UtcNow.AddSeconds(119));
            task.NextAttemptAt=DateTime.UtcNow.AddMinutes(-1);
        }
        Assert.True((await f.Media.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        Assert.Equal(1,Assert.Single(f.Store.Items<MediaAssetGenerationTask>()).AttemptNo);
        Assert.Equal(1,f.Storage.Writes);
    }
}
