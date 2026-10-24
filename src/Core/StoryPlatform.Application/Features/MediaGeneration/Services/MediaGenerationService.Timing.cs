using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    private async Task<T> MeasureMediaStageAsync<T>(string stage, int jobId, MediaAsset asset, Func<Task<T>> action)
    {
        var timer = Stopwatch.StartNew();
        string? code = null;
        try { return await action(); }
        catch (Exception ex) { code = PublicErrorCode(ex); throw; }
        finally
        {
            _logger.LogInformation("Media stage Stage={Stage} Job={JobId} Task={TaskId} Version={VersionId} Revision={Revision} Beat={BeatId} Asset={AssetId} Attempt={Attempt} DurationMs={DurationMs} ErrorCode={ErrorCode}",
                stage, jobId, _claimedImageTask?.Id, asset.StoryVersionId, asset.RequestedPackageRevision,
                asset.IllustrationBeatId, asset.Id, asset.AttemptCount, timer.Elapsed.TotalMilliseconds, code);
        }
    }
}
