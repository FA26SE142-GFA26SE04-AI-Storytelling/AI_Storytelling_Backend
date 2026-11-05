using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    private readonly IMediaUploadValidator? _uploadValidator;

    private async Task<bool> HasUnselectedCreatorMediaAsync(StoryGenerationJob? root, CancellationToken ct) =>
        root is not null && CreatorMediaSelection.Mode(root) == "choose" && await _unitOfWork.Repository<MediaAsset>().ExistsAsync(x =>
            x.ParentStoryPackageId == null && x.StoryVersionId == root.StoryVersionId && x.IsCurrent && !x.IsDeleted && x.Source == MediaAssetSource.Unspecified, ct);

    private async Task<Story> CreatorManualAsync(int actor, int storyId, CancellationToken ct)
    {
        var story = await FreshAsync<Story>(storyId, ct) ?? throw new NotFoundException("Story", storyId);
        if (story.Scope != StoryScope.Creator || story.Source != StorySource.Manual || story.ChildProfileId != null)
            throw new NotFoundException("Story", storyId);
        await MediaStoryAccess.AuthorizeCreatorAsync(_unitOfWork, story, actor, ct);
        await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, storyId, null, ct);
        return story;
    }

    public async Task<CreatorMediaCommandResult> SelectCreatorMediaAsync(int userId, int storyId, int targetId, bool image,
        CreatorMediaCommand command, CreatorMediaUploadCommand? upload, GeneratedMedia? file, CancellationToken ct = default)
    {
        await CreatorManualAsync(userId, storyId, ct);
        if (string.IsNullOrWhiteSpace(command.OperationId) || command.OperationId.Length > 100 || command.ExpectedConfirmed is null)
            throw new BadRequestException("MEDIA_OPERATION_ID_REQUIRED");
        if ((upload is null) != (file is null)) throw new BadRequestException("MEDIA_FILE_REQUIRED");
        if (upload is not null)
        {
            if (string.IsNullOrWhiteSpace(upload.SourceDeclaration) || string.IsNullOrWhiteSpace(upload.RightsDeclaration) ||
                upload.SourceDeclaration.Length > 2000 || upload.RightsDeclaration.Length > 2000)
                throw new BadRequestException("MEDIA_RIGHTS_DECLARATION_REQUIRED");
            (_uploadValidator ?? throw new InvalidOperationException("MEDIA_UPLOAD_VALIDATOR_NOT_CONFIGURED")).Validate(file!, image);
        }
        var hash = CommandHash(new { command, image, targetId, upload, fileHash = file is null ? null : Convert.ToHexString(SHA256.HashData(file.Content)), mime = file?.MimeType });
        var replay = await CreatorUploadReplayAsync(userId, storyId, command.OperationId, hash, ct);
        if (replay is not null) return replay;
        string? newPath = null;
        var retained = false;
        try
        {
            if (file is not null)
            {
                // A unique private object is uploaded outside the story transaction; authorization is rechecked below.
                var path = $"{storyId}/versions/{command.StoryVersionId}/uploads/{Guid.NewGuid():N}{file.SuggestedExtension}";
                await using var stream = file.OpenReadStream();
                newPath = await _mediaStorage.UploadAsync(path, stream, file.MimeType, ct);
            }
            await _unitOfWork.BeginTransactionAsync(ct);
            CreatorMediaCommandResult result;
            try
            {
                await _unitOfWork.AcquireTransactionLockAsync(storyId, ct);
                var story = await CreatorManualAsync(userId, storyId, ct);
                replay = await CreatorUploadReplayAsync(userId, storyId, command.OperationId, hash, ct);
                if (replay is not null) { await _unitOfWork.CommitTransactionAsync(ct); return replay; }
                var version = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent && !x.IsDeleted, cancellationToken: ct)
                    ?? throw new ConflictException("CURRENT_STORY_VERSION_REQUIRED");
                if (version.Id != command.StoryVersionId || version.MediaState.MediaPackageRevision != command.PackageRevision ||
                    MediaPackageConfirmation.IsConfirmed(version) != command.ExpectedConfirmed)
                    throw new ConflictException("STALE_MEDIA_PACKAGE");
                var root = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.StoryVersionId == version.Id &&
                    x.ParentStoryPackageId == null && x.Operation == GenerationJobOperation.GenerateMediaPackage, cancellationToken: ct)
                    ?? throw new ConflictException("MEDIA_PREPARATION_REQUIRED");
                if (root.MediaContextId is null || story.Status is not (StoryStatus.MediaProcessing or StoryStatus.MediaReview or StoryStatus.Ready))
                    throw new ConflictException("MEDIA_PREPARATION_REQUIRED");
                if (CreatorMediaSelection.Mode(root) != "choose" && root.Status != GenerationJobStatus.Completed)
                    throw new ConflictException("MEDIA_JOB_ACTIVE");
                var scene = image
                    ? await SceneForBeatAsync(version.Id, targetId, ct)
                    : await SceneForSegmentAsync(version.Id, targetId, ct);
                var type = image ? MediaType.Illustration : MediaType.TtsAudio;
                var current = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(x => x.ParentStoryPackageId == null && x.StoryVersionId == version.Id &&
                    x.IsCurrent && !x.IsDeleted && x.Type == type && (image ? x.IllustrationBeatId == targetId : x.StorySegmentId == targetId), cancellationToken: ct);
                if (current is not null && current.Status != MediaStatus.Ready && current.Source != MediaAssetSource.Unspecified &&
                    (file is null || current.Status is not (MediaStatus.Failed or MediaStatus.ManualReview) ||
                     await _unitOfWork.Repository<MediaAssetGenerationTask>().ExistsAsync(x => x.MediaAssetId == current.Id &&
                         (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), ct)))
                    throw new ConflictException("MEDIA_TARGET_ALREADY_SELECTED");
                if (current?.Status == MediaStatus.Ready && file is null) throw new ConflictException("MEDIA_ALREADY_EXISTS_USE_REGENERATE");
                await EnsureNoPendingReplacementAsync(version.Id, ct);
                if (current?.Status == MediaStatus.Ready)
                {
                    if (await _unitOfWork.Repository<MediaAssetGenerationTask>().ExistsAsync(x => x.StoryVersionId == version.Id &&
                        (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), ct))
                        throw new ConflictException("MEDIA_JOB_ACTIVE");
                    MediaPackageConfirmation.Invalidate(version);
                    _unitOfWork.Repository<StoryVersion>().Update(version);
                }
                if (current?.Status is MediaStatus.Failed or MediaStatus.ManualReview)
                {
                    var supersededTask = await _unitOfWork.Repository<MediaAssetGenerationTask>().FirstOrDefaultAsync(
                        x => x.MediaAssetId == current.Id, cancellationToken: ct);
                    if (supersededTask is not null)
                    {
                        // Attach with the database token before rotating it so EF retains the original value.
                        _unitOfWork.Repository<MediaAssetGenerationTask>().Update(supersededTask);
                        supersededTask.Status = GenerationJobStatus.Cancelled;
                        supersededTask.LeaseExpiresAt = null;
                        supersededTask.ConcurrencyToken = Guid.NewGuid().ToString("N");
                        _unitOfWork.Repository<MediaAssetGenerationTask>().Update(supersededTask);
                    }
                    current.IsCurrent = false; _unitOfWork.Repository<MediaAsset>().Update(current);
                    await _unitOfWork.SaveChangesAsync(ct);
                    current = null; // Keep the failed upload row/file in history; the new attempt gets a new asset identity.
                }
                var asset = current?.Status == MediaStatus.Ready ? new MediaAsset
                {
                    StoryVersionId = version.Id, StorySceneId = scene.Id, SceneIndex = scene.SceneIndex, Type = type,
                    IllustrationBeatId = image ? targetId : null, StorySegmentId = image ? null : targetId,
                    IsCurrent = false, ReplacesMediaAssetId = current.Id
                } : current ?? new MediaAsset
                {
                    StoryVersionId = version.Id, StorySceneId = scene.Id, SceneIndex = scene.SceneIndex, Type = type,
                    IllustrationBeatId = image ? targetId : null, StorySegmentId = image ? null : targetId
                };
                asset.Source = file is null ? MediaAssetSource.AiGenerated : MediaAssetSource.UserUpload;
                asset.Provider = file is null ? null : "CreatorUpload";
                asset.Status = MediaStatus.Queued; asset.ValidationStatus = ValidationStatus.Pending;
                asset.RequestedPackageRevision = version.MediaState.MediaPackageRevision;
                asset.Url = newPath; asset.MimeType = file?.MimeType;
                asset.ValidationResultJson = upload is null ? null : JsonSerializer.Serialize(new { uploadedByUserId = userId,
                    fileHash = Convert.ToHexString(SHA256.HashData(file!.Content)), upload.SourceDeclaration, upload.RightsDeclaration,
                    requiresModeratorAudioReview = !image });
                if (asset.Id == 0) await _unitOfWork.Repository<MediaAsset>().AddAsync(asset, ct);
                else _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(ct);
                root.GenerationMetadataJson = "{\"mediaMode\":\"choose\",\"choicePrepared\":true}";
                root.Status = GenerationJobStatus.Processing; root.Stage = JobStage.MediaAwaitingSelection;
                root.MediaPackageRevision = version.MediaState.MediaPackageRevision; root.LeaseExpiresAt = null;
                root.CompletedAt = null; root.ErrorCode = null; root.NextAttemptAt = null;
                _unitOfWork.Repository<StoryGenerationJob>().Update(root);
                await QueueCreatorTaskAsync(root, asset, ct);
                story.Status = StoryStatus.MediaProcessing; _unitOfWork.Repository<Story>().Update(story);
                result = new(asset.Id, version.Id, version.MediaState.MediaPackageRevision, "Queued",
                    $"/api/v1/stories/{storyId}/media/progress", $"/api/v1/stories/{storyId}/media/package");
                await _unitOfWork.Repository<StoryAuthoringOperation>().AddAsync(new()
                {
                    StoryId = storyId, StoryVersionId = version.Id, ActorUserId = userId, Kind = "creator-media-select",
                    OperationId = command.OperationId, RequestHash = hash, OperationResponseJson = JsonSerializer.Serialize(result)
                }, ct);
                await _unitOfWork.CommitTransactionAsync(ct); retained = true;
            }
            catch { await RollbackMediaAsync(CancellationToken.None); throw; }
            return result;
        }
        finally
        {
            if (!retained && newPath is not null)
                try { await _mediaStorage.DeleteAsync(newPath, CancellationToken.None); }
                catch { _logger.LogWarning("Unreferenced creator upload cleanup failed Story={StoryId}", storyId); }
        }
    }

    private async Task<CreatorMediaCommandResult?> CreatorUploadReplayAsync(int actor, int story, string operation, string hash, CancellationToken ct)
    {
        var receipt = await _unitOfWork.Repository<StoryAuthoringOperation>().FirstOrDefaultAsync(x => x.StoryId == story && x.OperationId == operation, cancellationToken: ct);
        if (receipt is null) return null;
        if (receipt.ActorUserId != actor || receipt.Kind != "creator-media-select" || receipt.RequestHash != hash)
            throw new ConflictException("MEDIA_OPERATION_ID_CONFLICT");
        return JsonSerializer.Deserialize<CreatorMediaCommandResult>(receipt.OperationResponseJson)!;
    }
    private async Task<StoryScene> SceneForBeatAsync(int version, int beatId, CancellationToken ct)
    {
        var beat = await FreshAsync<IllustrationBeat>(beatId, ct) ?? throw new NotFoundException("IllustrationBeat", beatId);
        var scene = await FreshAsync<StoryScene>(beat.StorySceneId, ct);
        return scene?.StoryVersionId == version ? scene : throw new NotFoundException("IllustrationBeat", beatId);
    }
    private async Task<StoryScene> SceneForSegmentAsync(int version, int segmentId, CancellationToken ct)
    {
        var segment = await FreshAsync<StorySegment>(segmentId, ct) ?? throw new NotFoundException("StorySegment", segmentId);
        var scene = await FreshAsync<StoryScene>(segment.StorySceneId, ct);
        return scene?.StoryVersionId == version ? scene : throw new NotFoundException("StorySegment", segmentId);
    }

    private async Task QueueCreatorTaskAsync(StoryGenerationJob root, MediaAsset asset, CancellationToken ct)
    {
        var existing = await _unitOfWork.Repository<MediaAssetGenerationTask>().FirstOrDefaultAsync(x => x.MediaAssetId == asset.Id, cancellationToken: ct);
        var task = existing ?? new MediaAssetGenerationTask { StoryId = root.StoryId, StoryVersionId = root.StoryVersionId!.Value, MediaAssetId = asset.Id };
        if (existing is not null) _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
        task.BranchJobId = root.Id; task.MediaContextId = root.MediaContextId!.Value; task.PackageRevision = root.MediaPackageRevision!.Value;
        task.IllustrationBeatId = asset.IllustrationBeatId;
        task.Kind = (asset.Source, asset.Type) switch
        {
            (MediaAssetSource.UserUpload, MediaType.Illustration) => MediaTaskKind.ValidateUploadedImage,
            (MediaAssetSource.UserUpload, _) => MediaTaskKind.ValidateUploadedAudio,
            (_, MediaType.Illustration) => MediaTaskKind.GenerateChosenImage,
            _ => MediaTaskKind.GenerateChosenAudio
        };
        task.Status = GenerationJobStatus.Pending; task.AttemptNo = 0; task.ErrorCode = null;
        task.NextAttemptAt = task.LeaseExpiresAt = task.CompletedAt = null; task.QueuedAt = _clock.GetUtcNow().UtcDateTime;
        task.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (existing is null) await _unitOfWork.Repository<MediaAssetGenerationTask>().AddAsync(task, ct);
        else _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
    }

    private async Task PrepareCreatorChoiceAsync(MediaState state, MediaContext context, string token, IReadOnlyList<StoryScene> scenes, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(state.Story.Id, ct);
            await AssertFreshAsync(state.Job.Id, token, state.Version.Id, context.Id, ct);
            var root = await RequireClaimedJobAsync(state.Job.Id, token, ct);
            var prepared = root.GenerationMetadataJson?.Contains("\"choicePrepared\":true", StringComparison.Ordinal) == true;
            root.MediaContextId = context.Id; root.MediaPackageRevision = state.Version.MediaState.MediaPackageRevision;
            foreach (var scene in scenes)
            {
                var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(x => x.StorySceneId == scene.Id && !x.IsDeleted, cancellationToken: ct);
                var assets = new List<MediaAsset>();
                foreach (var beat in beats) assets.Add(await ReplacementOrCurrentAsync(await GetOrCreateIllustrationAssetAsync(state.Version.Id, scene, beat, ct), ct));
                var segments = await _unitOfWork.Repository<StorySegment>().FindAsync(x => x.StorySceneId == scene.Id && !x.IsDeleted, cancellationToken: ct);
                foreach (var segment in segments)
                {
                    var asset = await ReplacementOrCurrentAsync(await GetOrCreateSegmentAudioAssetAsync(state.Version.Id, segment, ct), ct);
                    asset.StorySceneId = scene.Id; assets.Add(asset);
                }
                foreach (var asset in assets)
                {
                    if (asset.Status == MediaStatus.Ready)
                    {
                        // The narration task also owns vocabulary/listening media. Retry that work
                        // even when narration itself succeeded, without regenerating the Ready asset.
                        if (asset.Type == MediaType.TtsAudio && await _unitOfWork.Repository<MediaAssetGenerationTask>().ExistsAsync(
                            x => x.MediaAssetId == asset.Id && x.BranchJobId == root.Id &&
                                 x.PackageRevision == root.MediaPackageRevision && x.Status == GenerationJobStatus.Failed, ct))
                            await QueueCreatorTaskAsync(root, asset, ct);
                        continue;
                    }
                    if (!prepared) asset.Source = MediaAssetSource.Unspecified;
                    _unitOfWork.Repository<MediaAsset>().Update(asset);
                    if (asset.Source != MediaAssetSource.Unspecified) await QueueCreatorTaskAsync(root, asset, ct);
                }
            }
            root.GenerationMetadataJson = "{\"mediaMode\":\"choose\",\"choicePrepared\":true}";
            root.Stage = JobStage.MediaAwaitingSelection; root.LeaseExpiresAt = null;
            _unitOfWork.Repository<StoryGenerationJob>().Update(root);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    private async Task<bool> TryFinalizeCreatorChoiceAsync(CancellationToken ct)
    {
        var roots = await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(x => x.Stage == JobStage.MediaAwaitingSelection &&
            x.Status == GenerationJobStatus.Processing && x.ParentStoryPackageId == null && x.Operation == GenerationJobOperation.GenerateMediaPackage, cancellationToken: ct);
        foreach (var root in roots)
        {
            try
            {
            var tasks = await _unitOfWork.Repository<MediaAssetGenerationTask>().FindAsync(x => x.BranchJobId == root.Id && x.PackageRevision == root.MediaPackageRevision, cancellationToken: ct);
            if (tasks.Any(x => x.Status is GenerationJobStatus.Pending or GenerationJobStatus.Processing)) continue;
            if (tasks.Any(x => x.Status == GenerationJobStatus.Failed))
            { await _failureFinalizer.MarkFailedAsync(root.Id, root.ConcurrencyToken, tasks.First(x => x.Status == GenerationJobStatus.Failed).ErrorCode ?? "MEDIA_UPLOAD_VALIDATION_FAILED", ct); return true; }
            if (!(await _readinessService.CheckAsync(root.StoryVersionId!.Value, null, ct)).IsReady) continue;
            await FinalizeAsync(root.Id, root.ConcurrencyToken, root.StoryVersionId.Value, root.MediaContextId!.Value,
                await _unitOfWork.Repository<StoryScene>().CountAsync(x => x.StoryVersionId == root.StoryVersionId, ct), ct);
            return true;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Creator media finalization failed Job={JobId} Code={Code}", root.Id, PublicErrorCode(ex));
                await _failureFinalizer.MarkFailedAsync(root.Id, root.ConcurrencyToken, PublicErrorCode(ex), CancellationToken.None);
                return true;
            }
        }
        return false;
    }
}
