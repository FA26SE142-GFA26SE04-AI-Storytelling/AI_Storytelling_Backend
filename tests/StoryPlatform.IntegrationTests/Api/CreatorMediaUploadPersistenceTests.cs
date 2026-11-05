using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

// Real HTTP/controller/service/EF concurrency handling over isolated SQLite; only file storage is replaced.
public sealed class CreatorMediaUploadPersistenceTests
{
    private static readonly byte[] Png=[137,80,78,71,13,10,26,10,0,0,0,0];
    private sealed class Storage : IMediaStorage
    {
        public readonly Dictionary<string,GeneratedMedia> Files=new() { ["old-upload.png"]=new(Png,"image/png") };
        public async Task<string> UploadAsync(string path,Stream stream,string mime,CancellationToken ct=default)
        { using var bytes=new MemoryStream(); await stream.CopyToAsync(bytes,ct); Files[path]=new(bytes.ToArray(),mime); return path; }
        public Task<GeneratedMedia> ReadAsync(string path,CancellationToken ct=default)=>Task.FromResult(Files[path]);
        public Task<string> GetSignedUrlAsync(string path,TimeSpan ttl,CancellationToken ct=default)=>Task.FromResult("https://media.test/"+path);
        public Task DeleteAsync(string path,CancellationToken ct=default) { Files.Remove(path); return Task.CompletedTask; }
    }
    private sealed class Factory : ApiFactory
    {
        public readonly Storage Storage=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s=>
            {
                s.RemoveAll(x=>x.ServiceType==typeof(IMediaStorage));
                s.AddSingleton<IMediaStorage>(Storage);
            });
        }
    }

    [Theory]
    [InlineData(MediaStatus.Failed)]
    [InlineData(MediaStatus.ManualReview)]
    public async Task Corrected_upload_persists_and_cancels_old_task_using_its_original_concurrency_token(MediaStatus status)
    {
        using var factory=new Factory();
        var creator=await factory.AddUserAsync(UserRole.ContentCreator);
        var ids=await factory.WithDbAsync(async db=>
        {
            var story=new Story { AuthorUserId=creator.Id,Source=StorySource.Manual,Scope=StoryScope.Creator,Status=StoryStatus.MediaProcessing,Title="Test upload",OutputMode=StoryOutputMode.ReadingMediaOnly };
            db.Add(story); await db.SaveChangesAsync();
            var version=new StoryVersion { StoryId=story.Id,VersionNo=1,IsCurrent=true,Title=story.Title!,Content="An gấp thuyền giấy." };
            version.MediaState.MediaPackageRevision=1;
            db.Add(version); await db.SaveChangesAsync();
            var scene=new StoryScene { StoryVersionId=version.Id,SceneIndex=0,SceneText=version.Content,TextRangeEnd=version.Content.Length };
            var context=new MediaContext { StoryVersionId=version.Id,Revision=1 };
            db.AddRange(scene,context); await db.SaveChangesAsync();
            var beat=new IllustrationBeat { StorySceneId=scene.Id,BeatOrder=1,EndOffset=scene.SceneText.Length,VisualFocus="An gấp thuyền" };
            var root=new StoryGenerationJob { StoryId=story.Id,StoryVersionId=version.Id,BaseStoryVersionId=version.Id,RequestedByUserId=creator.Id,
                Operation=GenerationJobOperation.GenerateMediaPackage,Status=GenerationJobStatus.Failed,Stage=JobStage.MediaFailed,
                MediaContextId=context.Id,MediaPackageRevision=1,GenerationMetadataJson="{\"mediaMode\":\"choose\",\"choicePrepared\":true}" };
            db.AddRange(beat,root); await db.SaveChangesAsync();
            var asset=new MediaAsset { StoryVersionId=version.Id,StorySceneId=scene.Id,SceneIndex=0,IllustrationBeatId=beat.Id,
                Type=MediaType.Illustration,Source=MediaAssetSource.UserUpload,Status=status,ValidationStatus=ValidationStatus.Failed,
                IsCurrent=true,Url="old-upload.png",RequestedPackageRevision=1 };
            db.Add(asset); await db.SaveChangesAsync();
            var task=new MediaAssetGenerationTask { StoryId=story.Id,StoryVersionId=version.Id,BranchJobId=root.Id,MediaAssetId=asset.Id,
                IllustrationBeatId=beat.Id,MediaContextId=context.Id,PackageRevision=1,Kind=MediaTaskKind.ValidateUploadedImage,
                Status=GenerationJobStatus.Failed,ConcurrencyToken="original-token" };
            db.Add(task); await db.SaveChangesAsync();
            return (Story:story.Id,Version:version.Id,Beat:beat.Id,OldAsset:asset.Id,OldTask:task.Id);
        });
        using var client=factory.ClientFor(creator);
        using var form=new MultipartFormDataContent();
        using var file=new ByteArrayContent(Png); file.Headers.ContentType=new MediaTypeHeaderValue("image/png");
        form.Add(file,"file","corrected.png");
        foreach(var field in new Dictionary<string,string> { ["operationId"]="corrected-upload",["storyVersionId"]=ids.Version.ToString(),
            ["packageRevision"]="1",["expectedConfirmed"]="false",["sourceDeclaration"]="Own work",["rightsDeclaration"]="Owned test fixture" })
            form.Add(new StringContent(field.Value),field.Key);
        var response=await client.PostAsync($"/api/v1/creator/stories/{ids.Story}/media/illustration-beats/{ids.Beat}/upload",form);
        Assert.True(response.StatusCode==HttpStatusCode.Accepted,await response.Content.ReadAsStringAsync());
        await factory.WithDbAsync(async db=>
        {
            var old=await db.Set<MediaAssetGenerationTask>().SingleAsync(x=>x.Id==ids.OldTask);
            Assert.Equal(GenerationJobStatus.Cancelled,old.Status);
            Assert.NotEqual("original-token",old.ConcurrencyToken);
            var assets=await db.Set<MediaAsset>().Where(x=>x.StoryVersionId==ids.Version).ToListAsync();
            Assert.Equal(2,assets.Count); Assert.False(assets.Single(x=>x.Id==ids.OldAsset).IsCurrent);
            Assert.Equal(MediaStatus.Queued,Assert.Single(assets,x=>x.IsCurrent).Status);
            Assert.Equal(2,await db.Set<MediaAssetGenerationTask>().CountAsync(x=>x.StoryVersionId==ids.Version));
        });
        Assert.True(factory.Storage.Files.ContainsKey("old-upload.png")); Assert.Equal(2,factory.Storage.Files.Count);
    }
}
