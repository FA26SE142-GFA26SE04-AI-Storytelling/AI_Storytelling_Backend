using System.Diagnostics;
using StoryPlatform.Application.Features.MediaStorage.Models;
using Microsoft.Extensions.Logging;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    private async Task<string> UploadGeneratedMediaAsync(
        string stage, int jobId, string claimedToken, int contextId, MediaAsset asset, string storagePath,
        GeneratedMedia media, CancellationToken ct)
    {
        Task<string> UploadAsync() => MeasureMediaStageAsync(stage, jobId, asset, async () =>
        {
            await using var content = media.OpenReadStream();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                return await _mediaStorage.UploadAsync(storagePath, content, media.MimeType, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TransientMediaGenerationException("MEDIA_STORAGE_UPLOAD_TIMEOUT");
            }
            catch (ArgumentException)
            {
                throw new PermanentMediaGenerationException("MEDIA_STORAGE_UPLOAD_INVALID");
            }
        });

        try
        {
            return await UploadAsync();
        }
        catch (TransientMediaGenerationException ex) when (!ct.IsCancellationRequested &&
            ex.ErrorCode.StartsWith("MEDIA_STORAGE_UPLOAD_", StringComparison.Ordinal) &&
            ex.ErrorCode != "MEDIA_STORAGE_UPLOAD_RATE_LIMITED" &&
            (ex.RetryAfter is null || ex.RetryAfter <= TimeSpan.FromSeconds(2)))
        {
            // Reopen the same bytes and reuse the object key, including when the first write timed out.
            await Task.Delay(ex.RetryAfter is { } after && after > TimeSpan.FromSeconds(1)
                ? after : TimeSpan.FromSeconds(1), _clock, ct);
            await AssertFreshAsync(jobId, claimedToken, asset.StoryVersionId, contextId, ct);
            return await UploadAsync();
        }
    }

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
