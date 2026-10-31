using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public class ParentReadingFeedbackService : IParentReadingFeedbackService
{
    private const int MaxPageSize = 50;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _guard;

    public ParentReadingFeedbackService(IUnitOfWork unitOfWork, IChildProfileAccessGuard guard)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
    }

    public async Task<PagedResult<ParentReadingFeedbackItemDto>> ListAsync(
        int parentUserId, int childProfileId, ParentReadingFeedbackQueryDto query, CancellationToken ct = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);

        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var (sessions, total) = await _unitOfWork.Repository<ReadingSession>().GetPagedAsync(
            pageIndex,
            pageSize,
            session => session.ChildProfileId == childProfileId
                       && session.Status == SessionStatus.Completed
                       && !session.IsDeleted,
            orderBy: rows => rows.OrderByDescending(session => session.CompletedAt)
                .ThenByDescending(session => session.Id),
            cancellationToken: ct);

        var versionIds = sessions.Where(session => session.StoryVersionId.HasValue)
            .Select(session => session.StoryVersionId!.Value).Distinct().ToList();
        var versions = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
            version => versionIds.Contains(version.Id), cancellationToken: ct)).ToDictionary(version => version.Id);
        var items = (await _unitOfWork.Repository<QuizItem>().FindAsync(
                item => versionIds.Contains(item.StoryVersionId) && !item.IsDeleted, cancellationToken: ct))
            .GroupBy(item => item.StoryVersionId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToList());

        var storyIds = sessions.Select(session => session.StoryId).Distinct().ToList();
        var childSessionIds = (await _unitOfWork.Repository<ReadingSession>().FindAsync(
                session => session.ChildProfileId == childProfileId
                           && storyIds.Contains(session.StoryId)
                           && !session.IsDeleted,
                cancellationToken: ct))
            .Select(session => session.Id).ToList();
        var allItemIds = items.Values.SelectMany(ids => ids).ToList();
        var attempts = (await _unitOfWork.Repository<QuizAttempt>().FindAsync(
                attempt => childSessionIds.Contains(attempt.ReadingSessionId)
                           && allItemIds.Contains(attempt.QuizItemId)
                           && !attempt.IsDeleted,
                cancellationToken: ct))
            .OrderBy(attempt => attempt.Id).ToList();

        var rows = sessions.Select(session =>
        {
            var versionId = session.StoryVersionId;
            var itemIds = versionId.HasValue && items.TryGetValue(versionId.Value, out var ids)
                ? ids
                : new List<int>();
            var runs = QuizRunCalculator.Split(attempts.Where(attempt => itemIds.Contains(attempt.QuizItemId)));
            return new ParentReadingFeedbackItemDto
            {
                SessionId = session.Id,
                StoryId = session.StoryId,
                Title = versionId.HasValue && versions.TryGetValue(versionId.Value, out var version)
                    ? version.Title
                    : string.Empty,
                CompletedAt = session.CompletedAt,
                Feeling = session.Feeling?.ToString(),
                FeelingLabel = session.Feeling.HasValue ? StoryFeelingLabels.Label(session.Feeling.Value) : null,
                QuizTotalQuestions = itemIds.Count,
                QuizBestPercent = itemIds.Count == 0 ? null : QuizRunCalculator.BestPercent(runs, itemIds.Count)
            };
        }).ToList();

        return new PagedResult<ParentReadingFeedbackItemDto>(rows, total, pageIndex, pageSize);
    }
}
