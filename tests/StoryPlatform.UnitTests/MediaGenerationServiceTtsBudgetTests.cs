using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class MediaGenerationServiceTests
{
    private sealed class BudgetTts(Func<string,CancellationToken,Task<GeneratedMedia>> call) : ITtsProvider
    {
        public Task<GeneratedMedia> GenerateAsync(string text,CancellationToken cancellationToken=default)=>call(text,cancellationToken);
    }

    [Fact]
    public async Task Application_audio_budget_schedules_bounded_retry_without_marking_package_ready()
    {
        var store=Seed();
        var tts=new BudgetTts(async (_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return new GeneratedMedia([1],"audio/wav");});
        var service=Create(store,tts:tts,options:new(){AssetMaxAttempts=3,Audio=new(){ProviderTimeoutSeconds=1}});
        Assert.True((await service.ProcessNextAsync()).Success);
        var result=await service.ProcessBranchNextAsync(MediaType.TtsAudio);
        Assert.False(result.Success);Assert.Equal("MEDIA_PROVIDER_TIMEOUT",result.ErrorCode);
        var progress=await service.GetProgressAsync(1,1);
        Assert.Equal("Pending",progress.Audio.Status);Assert.NotNull(progress.Audio.NextRetryAt);
        Assert.False(progress.IsGenerated);Assert.False(progress.IsAwaitingConfirmation);
        Assert.Equal(1,store.Items<MediaAsset>().Single(x=>x.Type==MediaType.TtsAudio&&x.AttemptCount>0).AttemptCount);
    }

    [Fact]
    public async Task Worker_shutdown_propagates_without_recording_provider_failure()
    {
        var store=Seed();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tts=new BudgetTts(async (_,ct)=>{entered.SetResult();await Task.Delay(Timeout.Infinite,ct);return new GeneratedMedia([1],"audio/wav");});
        var service=Create(store,tts:tts);
        Assert.True((await service.ProcessNextAsync()).Success);
        using var stop=new CancellationTokenSource();
        var running=service.ProcessBranchNextAsync(MediaType.TtsAudio,stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>running);
        var job=store.Items<StoryGenerationJob>().Single(x=>x.Operation==GenerationJobOperation.GenerateTts);
        Assert.Equal(GenerationJobStatus.Processing,job.Status);Assert.Null(job.ErrorCode);
        Assert.DoesNotContain(store.Items<MediaAsset>(),x=>x.Type==MediaType.TtsAudio&&x.Status is MediaStatus.Failed or MediaStatus.ManualReview);
    }

    [Fact]
    public async Task Audio_retry_keeps_ready_narration_and_illustrations_and_only_generates_missing_segment()
    {
        var store=Seed();var inputs=new List<string>();var fail=true;
        var tts=new BudgetTts((text,_)=>
        {
            inputs.Add(text);
            if(fail&&inputs.Count==2)throw new TimeoutException();
            return Task.FromResult(new GeneratedMedia([82,73,70,70,0,0,0,0,87,65,86,69],"audio/wav"));
        });
        var service=Create(store,tts:tts,options:new(){AssetMaxAttempts=1});
        Assert.False((await service.RunPackageAsync()).Success);
        var first=store.Items<MediaAsset>().Single(x=>x.Type==MediaType.TtsAudio&&x.Status==MediaStatus.Ready);
        var firstUrl=first.Url;
        var images=store.Items<MediaAsset>().Where(x=>x.Type==MediaType.Illustration).Select(x=>(x.Id,x.Url)).ToArray();
        fail=false;await service.RetryAsync(1,1);
        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(firstUrl,first.Url);Assert.Equal(1,inputs.Count(x=>x==inputs[0]));
        Assert.Equal(images,store.Items<MediaAsset>().Where(x=>x.Type==MediaType.Illustration).Select(x=>(x.Id,x.Url)).ToArray());
        var progress=await service.GetProgressAsync(1,1);
        Assert.True(progress.IsAwaitingConfirmation);Assert.False(progress.IsReady);
    }
}
