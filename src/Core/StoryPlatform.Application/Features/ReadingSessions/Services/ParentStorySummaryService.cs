using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>Chủ hồ sơ nghe / xem / xóa tóm tắt cuối truyện con gửi. File lưu tới khi chủ hồ sơ xóa.</summary>
public class ParentStorySummaryService : IParentStorySummaryService
{
    private const int MaxPageSize = 50;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _guard;
    private readonly IMediaStorage _storage;

    public ParentStorySummaryService(IUnitOfWork unitOfWork, IChildProfileAccessGuard guard, IMediaStorage storage)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
        _storage = storage;
    }

    public async Task<PagedResult<ParentStorySummaryItemDto>> ListAsync(
        int parentUserId, int childProfileId, ParentStorySummaryQueryDto query, CancellationToken ct = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var (summaries, total) = await _unitOfWork.Repository<StorySummary>().GetPagedAsync(
            pageIndex,
            pageSize,
            summary => summary.ChildProfileId == childProfileId && !summary.IsDeleted,
            orderBy: rows => rows.OrderByDescending(summary => summary.CreatedAt).ThenByDescending(summary => summary.Id),
            cancellationToken: ct);

        var summaryIds = summaries.Select(summary => summary.Id).ToList();
        var flags = await _unitOfWork.Repository<StorySummaryFlag>().FindAsync(
            flag => summaryIds.Contains(flag.StorySummaryId) && !flag.IsDeleted, cancellationToken: ct);
        var sceneIds = flags.Select(flag => flag.StorySceneId).Distinct().ToList();
        var scenes = (await _unitOfWork.Repository<StoryScene>().FindAsync(
            scene => sceneIds.Contains(scene.Id), cancellationToken: ct)).ToDictionary(scene => scene.Id);
        var illustrations = (await _unitOfWork.Repository<MediaAsset>().FindAsync(
                asset => asset.StorySceneId.HasValue && sceneIds.Contains(asset.StorySceneId.Value)
                         && asset.Type == MediaType.Illustration && asset.Status == MediaStatus.Ready && !asset.IsDeleted,
                cancellationToken: ct))
            .OrderByDescending(asset => asset.CompletedAt ?? asset.CreatedAt).ThenByDescending(asset => asset.Id)
            .GroupBy(asset => asset.StorySceneId!.Value)
            .ToDictionary(group => group.Key, group => group.First().Url);
        var versionIds = summaries.Where(summary => summary.StoryVersionId.HasValue)
            .Select(summary => summary.StoryVersionId!.Value).Distinct().ToList();
        var titles = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
                version => versionIds.Contains(version.Id), cancellationToken: ct))
            .ToDictionary(version => version.Id, version => version.Title);

        var items = new List<ParentStorySummaryItemDto>();
        foreach (var summary in summaries)
        {
            var url = summary.AudioStoragePath == null
                ? null
                : await _storage.GetSignedUrlAsync(summary.AudioStoragePath, StorySummaryPolicy.ListenUrlLifetime, ct);
            items.Add(new ParentStorySummaryItemDto
            {
                SummaryId = summary.Id,
                SessionId = summary.ReadingSessionId,
                StoryId = summary.StoryId,
                Title = summary.StoryVersionId.HasValue && titles.TryGetValue(summary.StoryVersionId.Value, out var title)
                    ? title
                    : string.Empty,
                CreatedAt = summary.CreatedAt,
                RecordingUrl = url,
                RecordingSentAt = summary.RecordingSentAt,
                FlagsSentAt = summary.FlagsSentAt,
                Flags = flags.Where(flag => flag.StorySummaryId == summary.Id && scenes.ContainsKey(flag.StorySceneId))
                    .Select(flag => scenes[flag.StorySceneId])
                    .OrderBy(scene => scene.SceneIndex)
                    .Select(scene => new ParentStorySummaryFlagDto
                    {
                        SceneId = scene.Id,
                        SceneIndex = scene.SceneIndex,
                        SceneText = scene.SceneText,
                        IllustrationUrl = illustrations.TryGetValue(scene.Id, out var image) ? image : null
                    }).ToList()
            });
        }

        return new PagedResult<ParentStorySummaryItemDto>(items, total, pageIndex, pageSize);
    }

    public async Task DeleteAsync(int parentUserId, int childProfileId, int summaryId, CancellationToken ct = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        var summary = await _unitOfWork.Repository<StorySummary>().GetByIdAsync(summaryId, ct);
        if (summary == null || summary.IsDeleted || summary.ChildProfileId != childProfileId)
            throw new NotFoundException("Bản tóm tắt", summaryId);

        await RemoveAsync(summary, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task PurgeChildAsync(int childProfileId, CancellationToken ct = default)
    {
        var summaries = await _unitOfWork.Repository<StorySummary>().FindAsync(
            summary => summary.ChildProfileId == childProfileId && !summary.IsDeleted, cancellationToken: ct);
        foreach (var summary in summaries)
        {
            await RemoveAsync(summary, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task RemoveAsync(StorySummary summary, CancellationToken ct)
    {
        if (summary.AudioStoragePath != null)
        {
            await _storage.DeleteAsync(summary.AudioStoragePath, ct);
        }

        var now = DateTime.UtcNow;
        var flagRepo = _unitOfWork.Repository<StorySummaryFlag>();
        var flags = await flagRepo.FindAsync(flag => flag.StorySummaryId == summary.Id && !flag.IsDeleted, cancellationToken: ct);
        foreach (var flag in flags)
        {
            flag.IsDeleted = true;
            flag.UpdatedAt = now;
            flagRepo.Update(flag);
        }

        summary.AudioStoragePath = null;
        summary.IsDeleted = true;
        summary.UpdatedAt = now;
        _unitOfWork.Repository<StorySummary>().Update(summary);
    }
}
