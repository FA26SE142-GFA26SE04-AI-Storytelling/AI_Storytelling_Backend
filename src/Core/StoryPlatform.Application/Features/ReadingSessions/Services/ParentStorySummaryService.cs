using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
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
    private readonly IChildMediaUrlSigner? _mediaUrls;

    public ParentStorySummaryService(IUnitOfWork unitOfWork, IChildProfileAccessGuard guard, IMediaStorage storage,
        IChildMediaUrlSigner? mediaUrls = null)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
        _storage = storage;
        _mediaUrls = mediaUrls;
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
        var beatIds = flags.Where(flag => flag.IllustrationBeatId.HasValue)
            .Select(flag => flag.IllustrationBeatId!.Value).Distinct().ToList();
        var beats = beatIds.Count == 0 ? new Dictionary<int, IllustrationBeat>()
            : (await _unitOfWork.Repository<IllustrationBeat>().FindAsync(
                beat => beatIds.Contains(beat.Id), cancellationToken: ct)).ToDictionary(beat => beat.Id);
        var pictures = await LoadPicturesAsync(summaries, flags, ct);
        var illustrationUrls = await _mediaUrls.SignValuesAsync(pictures, ct);
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
            var mine = flags.Where(flag => flag.StorySummaryId == summary.Id && scenes.ContainsKey(flag.StorySceneId)
                                           && scenes[flag.StorySceneId].StoryVersionId == summary.StoryVersionId)
                .OrderBy(flag => scenes[flag.StorySceneId].SceneIndex)
                .ThenBy(flag => flag.IllustrationBeatId.HasValue && beats.TryGetValue(flag.IllustrationBeatId.Value, out var b) ? b.BeatOrder : 0)
                .ThenBy(flag => flag.Id).ToList();
            var questions = new List<ParentStorySummaryFlagDto>();
            foreach (var flag in mine)
            {
                var scene = scenes[flag.StorySceneId];
                var beat = flag.IllustrationBeatId.HasValue ? beats.GetValueOrDefault(flag.IllustrationBeatId.Value) : null;
                if (beat?.StorySceneId != scene.Id) beat = null;
                questions.Add(new ParentStorySummaryFlagDto
                {
                    QuestionId = flag.Id,
                    SceneId = scene.Id,
                    SceneIndex = scene.SceneIndex,
                    SceneText = scene.SceneText,
                    BeatOrder = beat?.BeatOrder,
                    PictureText = beat != null && beat.StartOffset >= 0 && beat.EndOffset <= scene.SceneText.Length && beat.StartOffset < beat.EndOffset
                        ? scene.SceneText[beat.StartOffset..beat.EndOffset].Trim() : null,
                    IllustrationUrl = illustrationUrls.TryGetValue(flag.Id, out var image) ? image : null,
                    QuestionText = flag.QuestionText,
                    QuestionAudioUrl = flag.AudioStoragePath == null ? null
                        : await _storage.GetSignedUrlAsync(flag.AudioStoragePath, StorySummaryPolicy.ListenUrlLifetime, ct),
                    AskedAt = flag.CreatedAt,
                    AnsweredAt = flag.AnsweredAt
                });
            }
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
                Flags = questions,
                UnansweredCount = questions.Count(question => question.AnsweredAt == null)
            });
        }

        return new PagedResult<ParentStorySummaryItemDto>(items, total, pageIndex, pageSize);
    }

    /// <summary>Tranh theo Id câu hỏi: tấm đã lưu, hoặc tranh cảnh theo phạm vi phiên đối với cờ cũ.</summary>
    private async Task<Dictionary<int, string>> LoadPicturesAsync(
        IReadOnlyList<StorySummary> summaries, IReadOnlyList<StorySummaryFlag> flags, CancellationToken ct)
    {
        var result = new Dictionary<int, string>();
        if (flags.Count == 0) return result;
        var sessionIds = summaries.Select(summary => summary.ReadingSessionId).Distinct().ToList();
        var sessions = (await _unitOfWork.Repository<ReadingSession>().FindAsync(
            session => sessionIds.Contains(session.Id), cancellationToken: ct)).ToDictionary(session => session.Id);
        var packageIds = sessions.Values.Where(session => session.ParentStoryPackageId.HasValue)
            .Select(session => session.ParentStoryPackageId!.Value).Distinct().ToList();
        var packages = packageIds.Count == 0 ? new Dictionary<int, ParentStoryPackage>()
            : (await _unitOfWork.Repository<ParentStoryPackage>().FindAsync(
                package => packageIds.Contains(package.Id), cancellationToken: ct)).ToDictionary(package => package.Id);
        var assetIds = flags.Where(flag => flag.MediaAssetId.HasValue).Select(flag => flag.MediaAssetId!.Value).Distinct().ToList();
        var savedAssets = assetIds.Count == 0 ? new Dictionary<int, MediaAsset>()
            : (await _unitOfWork.Repository<MediaAsset>().FindAsync(
                asset => assetIds.Contains(asset.Id) && asset.Type == MediaType.Illustration
                         && asset.Status == MediaStatus.Ready && !asset.IsDeleted && asset.Url != null,
                cancellationToken: ct)).ToDictionary(asset => asset.Id);
        foreach (var group in flags.GroupBy(flag => flag.StorySummaryId))
        {
            var summary = summaries.First(value => value.Id == group.Key);
            if (!sessions.TryGetValue(summary.ReadingSessionId, out var session)
                || session.ChildProfileId != summary.ChildProfileId || session.StoryVersionId != summary.StoryVersionId) continue;
            ParentPackageManifest? manifest = null;
            if (session.ParentStoryPackageId is int packageId)
            {
                if (!packages.TryGetValue(packageId, out var package) || package.ChildProfileId != summary.ChildProfileId
                    || package.StoryVersionId != summary.StoryVersionId || package.StoryId != summary.StoryId) continue;
                try { manifest = JsonSerializer.Deserialize<ParentPackageManifest>(package.ManifestJson, ParentPackageContext.Json); }
                catch (JsonException) { continue; }
                if (manifest?.MediaAssetIds is null || manifest.SceneIds is null) continue;
            }
            foreach (var flag in group.Where(flag => flag.MediaAssetId.HasValue))
                if (savedAssets.TryGetValue(flag.MediaAssetId!.Value, out var asset)
                    && asset.StoryVersionId == summary.StoryVersionId && asset.StorySceneId == flag.StorySceneId
                    && (!flag.IllustrationBeatId.HasValue || asset.IllustrationBeatId == flag.IllustrationBeatId)
                    && (manifest is null ? asset.ParentStoryPackageId == null
                        : manifest.MediaAssetIds.Contains(asset.Id) && manifest.SceneIds.Contains(flag.StorySceneId)))
                    result[flag.Id] = asset.Url!;

            var legacy = group.Where(flag => flag.MediaAssetId == null && flag.IllustrationBeatId == null
                && (manifest == null || manifest.SceneIds.Contains(flag.StorySceneId))).ToList();
            if (legacy.Count == 0) continue;
            var assets = await StorySummaryIllustrations.LoadAsync(_unitOfWork, summary.StoryVersionId,
                session.ParentStoryPackageId, manifest?.MediaAssetIds, legacy.Select(flag => flag.StorySceneId).Distinct().ToList(), ct);
            foreach (var flag in legacy)
                if (StorySummaryIllustrations.ForScene(assets, flag.StorySceneId) is { } asset)
                    result[flag.Id] = asset.Url!;
        }
        return result;
    }

    public async Task<StorySummaryAnsweredDto> SetAnsweredAsync(
        int parentUserId, int childProfileId, int summaryId, int questionId, bool answered, CancellationToken ct = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        var summary = await _unitOfWork.Repository<StorySummary>().GetByIdAsync(summaryId, ct);
        if (summary == null || summary.IsDeleted || summary.ChildProfileId != childProfileId)
            throw new NotFoundException("Bản tóm tắt", summaryId);
        var repo = _unitOfWork.Repository<StorySummaryFlag>();
        var question = await repo.FirstOrDefaultAsync(
            flag => flag.Id == questionId && flag.StorySummaryId == summaryId && !flag.IsDeleted, cancellationToken: ct)
            ?? throw new NotFoundException("Câu hỏi", questionId);
        var now = DateTime.UtcNow;
        var next = answered ? question.AnsweredAt ?? now : (DateTime?)null;
        if (next != question.AnsweredAt)
        {
            question.AnsweredAt = next;
            question.UpdatedAt = now;
            repo.Update(question);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        return new StorySummaryAnsweredDto { QuestionId = question.Id, AnsweredAt = question.AnsweredAt };
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
            if (flag.AudioStoragePath != null)
            {
                await _storage.DeleteAsync(flag.AudioStoragePath, ct);
                flag.AudioStoragePath = null;
                flag.AudioContentType = null;
                flag.AudioBytes = null;
            }
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
