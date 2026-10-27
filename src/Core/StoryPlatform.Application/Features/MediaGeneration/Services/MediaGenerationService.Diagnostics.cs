using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    private async Task<IReadOnlyList<MediaGenerationIssue>> GetMediaIssuesAsync(StoryGenerationJob parent, int revision, CancellationToken ct)
    {
        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(x => x.StoryVersionId == parent.StoryVersionId && !x.IsDeleted, cancellationToken: ct);
        var sceneIds = scenes.Select(x => x.Id).ToList();
        var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(x => sceneIds.Contains(x.StorySceneId) && !x.IsDeleted, cancellationToken: ct);
        var segments = await _unitOfWork.Repository<StorySegment>().FindAsync(x => sceneIds.Contains(x.StorySceneId) && !x.IsDeleted, cancellationToken: ct);
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(x => x.StoryVersionId == parent.StoryVersionId && !x.IsDeleted &&
            (x.IsCurrent || x.ReplacesMediaAssetId != null && x.ReplacementResolvedAt == null) &&
            (x.RequestedPackageRevision == null || x.RequestedPackageRevision == revision) && x.Status != MediaStatus.Ready, cancellationToken: ct);
        var branches = await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(x => x.ParentJobId == parent.Id &&
            x.MediaPackageRevision == revision && !x.IsDeleted, cancellationToken: ct);
        var branchIds = branches.Select(x => x.Id).ToList();
        var tasks = await _unitOfWork.Repository<MediaAssetGenerationTask>().FindAsync(x => branchIds.Contains(x.BranchJobId) &&
            x.PackageRevision == revision && !x.IsDeleted, cancellationToken: ct);
        var issues = new List<MediaGenerationIssue>();
        foreach (var asset in assets)
        {
            var scene = scenes.FirstOrDefault(x => x.Id == asset.StorySceneId);
            if (scene is null) continue; // Do not guess a scene for a preparation failure.
            var image = asset.Type == MediaType.Illustration;
            if (!image && asset.Type != MediaType.TtsAudio) continue;
            var task = image ? tasks.FirstOrDefault(x => x.MediaAssetId == asset.Id) : null;
            if (task?.Status is GenerationJobStatus.Completed or GenerationJobStatus.Cancelled) continue;
            var branch = branches.FirstOrDefault(x => x.Operation == (image ? GenerationJobOperation.GenerateIllustration : GenerationJobOperation.GenerateTts));
            if (branch?.Status == GenerationJobStatus.Cancelled) continue;
            var hasFailure = asset.Status is MediaStatus.Failed or MediaStatus.ManualReview || task?.ErrorCode != null;
            if (!hasFailure) continue;
            var target = image ? asset.IllustrationBeatId : asset.StorySegmentId;
            if (target is null) continue;
            var orderedIds = image
                ? beats.Where(x => x.StorySceneId == scene.Id).OrderBy(x => x.BeatOrder).Select(x => x.Id).ToList()
                : segments.Where(x => x.StorySceneId == scene.Id).OrderBy(x => x.SegmentOrder).Select(x => x.Id).ToList();
            var index = orderedIds.IndexOf(target.Value);
            if (index < 0) continue;
            var status = task?.Status.ToString() ?? (asset.Status == MediaStatus.ManualReview ? "ManualReview" :
                branch?.Status is GenerationJobStatus.Pending or GenerationJobStatus.Processing ? branch.Status.ToString() : "Failed");
            var code = task?.ErrorCode ?? branch?.ErrorCode ?? (asset.Status == MediaStatus.ManualReview ?
                "MEDIA_MANUAL_REVIEW_REQUIRED" : image ? "IMAGE_GENERATION_FAILED" : "TTS_GENERATION_FAILED");
            // Codes originate from worker PublicErrorCode. Never expose LastValidationReason or exception text.
            if (code.Length > 100 || code.Any(c => !(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
                code = "MEDIA_GENERATION_FAILED";
            issues.Add(new(scene.Id, scene.SceneIndex, asset.Id, asset.Type.ToString(), target.Value, index + 1,
                status, code, asset.AttemptCount, task?.NextAttemptAt ?? (!image ? branch?.NextAttemptAt : null)));
        }
        return issues.OrderBy(x => x.SceneIndex).ThenBy(x => x.Type).ThenBy(x => x.ItemNumber).ToArray();
    }
}
