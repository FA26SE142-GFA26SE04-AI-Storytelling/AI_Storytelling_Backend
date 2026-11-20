using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>Chủ hồ sơ xem cảm nhận sau khi đọc, nghe/xóa lời con. File lưu tới khi chủ hồ sơ xóa.</summary>
public class ParentStoryReflectionService : IParentStoryReflectionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _guard;
    private readonly IMediaStorage _storage;
    private readonly IChildMediaUrlSigner? _mediaUrls;

    public ParentStoryReflectionService(IUnitOfWork unitOfWork, IChildProfileAccessGuard guard, IMediaStorage storage,
        IChildMediaUrlSigner? mediaUrls = null)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
        _storage = storage;
        _mediaUrls = mediaUrls;
    }

    public async Task<ParentStoryReflectionDto> GetAsync(int parentUserId, int childProfileId, int sessionId, CancellationToken ct = default)
    {
        await RequireSessionAsync(parentUserId, childProfileId, sessionId, ct);
        var reflection = await FindAsync(sessionId, ct);
        var result = new ParentStoryReflectionDto { SessionId = sessionId };
        if (reflection == null) return result;

        result.Emotions = ChildStoryReflectionService.ToEmotionDtos(reflection.Emotions);
        result.NoteText = reflection.NoteText;
        result.NoteSentAt = reflection.NoteSentAt;
        result.NoteAudioUrl = reflection.NoteAudioStoragePath == null ? null
            : await _storage.GetSignedUrlAsync(reflection.NoteAudioStoragePath, StoryReflectionPolicy.ListenUrlLifetime, ct);

        var scene = reflection.FavoriteSceneId is int sceneId
            ? await _unitOfWork.Repository<StoryScene>().GetByIdAsync(sceneId, ct) : null;
        if (scene is { IsDeleted: false } && scene.StoryVersionId == reflection.StoryVersionId)
        {
            var picture = reflection.FavoriteMediaAssetId is int assetId
                ? await _unitOfWork.Repository<MediaAsset>().GetByIdAsync(assetId, ct) : null;
            var path = picture is { IsDeleted: false } && picture.StorySceneId == scene.Id ? picture.Url : null;
            result.FavoriteScene = new ParentFavoriteSceneDto
            {
                SceneId = scene.Id,
                SceneIndex = scene.SceneIndex,
                SceneText = scene.SceneText,
                IllustrationUrl = await _mediaUrls.SignAsync(path, ct)
            };
        }

        return result;
    }

    public async Task DeleteNoteAsync(int parentUserId, int childProfileId, int sessionId, CancellationToken ct = default)
    {
        await RequireSessionAsync(parentUserId, childProfileId, sessionId, ct);
        var reflection = await FindAsync(sessionId, ct);
        if (reflection == null || (reflection.NoteText == null && reflection.NoteAudioStoragePath == null))
            throw new NotFoundException("Lời con", sessionId);

        await ClearNoteAsync(reflection, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task PurgeChildAsync(int childProfileId, CancellationToken ct = default)
    {
        var rows = await _unitOfWork.Repository<StoryReflection>().FindAsync(
            row => row.ChildProfileId == childProfileId && !row.IsDeleted, cancellationToken: ct);
        foreach (var row in rows)
        {
            await ClearNoteAsync(row, ct);
            row.IsDeleted = true;
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task RequireSessionAsync(int parentUserId, int childProfileId, int sessionId, CancellationToken ct)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
            throw new NotFoundException("Phiên đọc", sessionId);
    }

    private Task<StoryReflection?> FindAsync(int sessionId, CancellationToken ct) =>
        _unitOfWork.Repository<StoryReflection>().FirstOrDefaultAsync(
            row => row.ReadingSessionId == sessionId && !row.IsDeleted, cancellationToken: ct);

    private async Task ClearNoteAsync(StoryReflection reflection, CancellationToken ct)
    {
        if (reflection.NoteAudioStoragePath != null)
            await _storage.DeleteAsync(reflection.NoteAudioStoragePath, ct);
        reflection.NoteText = null;
        reflection.NoteAudioStoragePath = null;
        reflection.NoteAudioContentType = null;
        reflection.NoteAudioBytes = null;
        reflection.NoteSentAt = null;
        reflection.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<StoryReflection>().Update(reflection);
    }
}
