using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.BusinessReports.Interfaces;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Application.Features.CreatorStatistics.DTOs;
using StoryPlatform.Application.Features.CreatorStatistics.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CreatorStatistics.Services;

/// <summary>
/// Thống kê của Content Creator (F-20, F-21). Mọi số liệu là số tổng hợp theo truyện, không có dữ liệu của trẻ,
/// và chỉ tính trên truyện của chính Creator đang gọi (mã Creator lấy từ token ở controller).
/// </summary>
public class CreatorStatisticsService : ICreatorStatisticsService
{
    // Thời gian đọc tối thiểu của một lượt đọc hợp lệ; Admin cấu hình được ở F-17 (chưa làm).
    public const int MinimumReadSeconds = 60;
    private const int MaxValidReadsPerChildPerDay = 10;
    // "Mỗi ngày" của trẻ tính theo giờ Việt Nam.
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IRevenueStatisticsService _revenueStatisticsService;

    public CreatorStatisticsService(IUnitOfWork unitOfWork, IRevenueStatisticsService revenueStatisticsService)
    {
        _unitOfWork = unitOfWork;
        _revenueStatisticsService = revenueStatisticsService;
    }

    public async Task<CreatorReadingStatisticsDto> GetReadingAsync(
        int creatorUserId, DateTime periodStart, DateTime periodEnd, int? storyId,
        CancellationToken cancellationToken = default)
    {
        var (from, toExclusive) = ReportPeriod.Resolve(periodStart, periodEnd);
        var creatorStories = await GetCreatorStoriesAsync(cancellationToken);
        var stories = SelectOwnStories(creatorStories, creatorUserId, storyId);
        var storyIds = stories.Select(story => story.Id).ToHashSet();

        // Lượt xem = phiên đọc được mở trong kỳ; lượt đọc hoàn thành = trong số đó, phiên đã Completed.
        var opened = (await _unitOfWork.Repository<ReadingSession>().FindAsync(
                session => session.StartedAt >= from && session.StartedAt < toExclusive,
                cancellationToken: cancellationToken))
            .Where(session => storyIds.Contains(session.StoryId))
            .ToList();
        var favorites = (await _unitOfWork.Repository<ReadingProgress>().FindAsync(
                progress => progress.IsFavorited, cancellationToken: cancellationToken))
            .Where(progress => storyIds.Contains(progress.StoryId))
            .ToList();
        var validReads = await GetValidReadsAsync(creatorStories, from, toExclusive, cancellationToken);

        var rows = stories
            .Select(story =>
            {
                var views = opened.Count(session => session.StoryId == story.Id);
                var completed = opened.Count(
                    session => session.StoryId == story.Id && session.Status == SessionStatus.Completed);
                return new CreatorStoryReadingDto
                {
                    StoryId = story.Id,
                    Title = story.Title ?? string.Empty,
                    Views = views,
                    Favorites = favorites.Count(progress => progress.StoryId == story.Id),
                    CompletedReadings = completed,
                    ValidReads = validReads.Count(read => read.StoryId == story.Id),
                    CompletionRate = Rate(completed, views)
                };
            })
            .ToList();

        return new CreatorReadingStatisticsDto
        {
            PeriodStart = from,
            PeriodEnd = toExclusive.AddDays(-1),
            Views = rows.Sum(row => row.Views),
            Favorites = rows.Sum(row => row.Favorites),
            CompletedReadings = rows.Sum(row => row.CompletedReadings),
            ValidReads = rows.Sum(row => row.ValidReads),
            CompletionRate = Rate(rows.Sum(row => row.CompletedReadings), rows.Sum(row => row.Views)),
            Stories = rows
        };
    }

    public async Task<CreatorRevenueStatisticsDto> GetRevenueAsync(
        int creatorUserId, DateTime periodStart, DateTime periodEnd, int? storyId,
        CancellationToken cancellationToken = default)
    {
        var (from, toExclusive) = ReportPeriod.Resolve(periodStart, periodEnd);
        var lastDay = toExclusive.AddDays(-1);
        var creatorStories = await GetCreatorStoriesAsync(cancellationToken);
        var ownStories = SelectOwnStories(creatorStories, creatorUserId, storyId);
        var validReads = await GetValidReadsAsync(creatorStories, from, toExclusive, cancellationToken);

        // Quỹ của kỳ lấy đúng số của thống kê doanh thu (F-15) để hai nơi luôn khớp nhau.
        var revenue = await _revenueStatisticsService.GetAsync(from, lastDay, cancellationToken);
        var fund = revenue.CreatorFundVnd;
        var allValidReads = validReads.Count;

        // Liệt kê truyện đang xuất bản và cả truyện đã gỡ sau kỳ nhưng vẫn có lượt đọc hợp lệ trong kỳ.
        var ownById = creatorStories.Where(story => story.AuthorUserId == creatorUserId).ToDictionary(story => story.Id);
        var listedIds = ownStories.Select(story => story.Id)
            .Union(validReads
                .Where(read => ownById.ContainsKey(read.StoryId) && (storyId == null || read.StoryId == storyId))
                .Select(read => read.StoryId))
            .OrderBy(id => id);

        var rows = listedIds
            .Select(id =>
            {
                var reads = validReads.Count(read => read.StoryId == id);
                return new CreatorStoryRevenueDto
                {
                    StoryId = id,
                    Title = ownById[id].Title ?? string.Empty,
                    ValidReads = reads,
                    EstimatedEarningsVnd = Share(reads, fund, allValidReads)
                };
            })
            .ToList();
        var myValidReads = rows.Sum(row => row.ValidReads);

        return new CreatorRevenueStatisticsDto
        {
            PeriodStart = from,
            PeriodEnd = lastDay,
            CreatorFundVnd = fund,
            ReadingFundRate = revenue.ReadingFundRate,
            CreativeFundRate = revenue.CreativeFundRate,
            MinimumReadSeconds = MinimumReadSeconds,
            TotalValidReadsAllCreators = allValidReads,
            MyValidReads = myValidReads,
            EstimatedEarningsVnd = Share(myValidReads, fund, allValidReads),
            Stories = rows
        };
    }

    private async Task<IReadOnlyList<Story>> GetCreatorStoriesAsync(CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<Story>().FindAsync(
            story => story.Scope == StoryScope.Creator, cancellationToken: cancellationToken);

    /// <summary>
    /// Truyện của chính Creator: mặc định là các truyện đang Published; nếu chỉ định một truyện thì phải là truyện
    /// của mình (truyện của Creator khác → 403).
    /// </summary>
    private static List<Story> SelectOwnStories(IReadOnlyList<Story> creatorStories, int creatorUserId, int? storyId)
    {
        if (storyId == null)
        {
            return creatorStories
                .Where(story => story.AuthorUserId == creatorUserId && IsCurrentlyPublished(story))
                .OrderBy(story => story.Id)
                .ToList();
        }

        var requested = creatorStories.FirstOrDefault(story => story.Id == storyId)
                        ?? throw new NotFoundException("Truyện", storyId.Value);
        if (requested.AuthorUserId != creatorUserId)
        {
            throw new ForbiddenException("Bạn chỉ xem được thống kê truyện của chính mình.");
        }

        return [requested];
    }

    /// <summary>
    /// Lượt đọc hợp lệ của mọi Creator trong kỳ: phiên Completed của truyện Creator đang Published tại lúc đọc, trẻ
    /// thuộc tài khoản đang có gói trả phí, đủ thời gian đọc tối thiểu, không phải con của chính Creator; mỗi trẻ
    /// tối đa 1 lượt mỗi truyện mỗi ngày và 10 lượt mỗi ngày; truyện bị gỡ vì vi phạm trong kỳ mất hết lượt của kỳ.
    /// </summary>
    private async Task<List<ValidRead>> GetValidReadsAsync(
        IReadOnlyList<Story> creatorStories, DateTime from, DateTime toExclusive, CancellationToken cancellationToken)
    {
        var storyById = creatorStories.ToDictionary(story => story.Id);
        var completed = await _unitOfWork.Repository<ReadingSession>().FindAsync(
            session => session.Status == SessionStatus.Completed
                       && session.CompletedAt >= from && session.CompletedAt < toExclusive,
            cancellationToken: cancellationToken);
        var ownerByChild = (await _unitOfWork.Repository<ChildProfile>().GetAllAsync(cancellationToken))
            .ToDictionary(child => child.Id, child => child.OwnerUserId);
        var plansByUser = (await _unitOfWork.Repository<UserSubscription>().GetAllAsync(cancellationToken))
            .ToLookup(subscription => subscription.UserId);

        var eligible = new List<ValidRead>();
        foreach (var session in completed)
        {
            var completedAt = session.CompletedAt!.Value;
            if (session.TimeSpentSeconds < MinimumReadSeconds
                || !storyById.TryGetValue(session.StoryId, out var story)
                || !WasPublishedAt(story, completedAt, from, toExclusive)
                || !ownerByChild.TryGetValue(session.ChildProfileId, out var ownerUserId)
                || ownerUserId == story.AuthorUserId
                || !plansByUser[ownerUserId].Any(plan => plan.StartAt <= completedAt && completedAt < plan.EndAt))
            {
                continue;
            }

            eligible.Add(new ValidRead(
                session.StoryId, session.ChildProfileId, completedAt, (completedAt + VietnamOffset).Date));
        }

        return eligible
            // Mỗi trẻ tối đa 1 lượt mỗi truyện mỗi ngày: giữ lượt sớm nhất.
            .GroupBy(read => (read.ChildProfileId, read.StoryId, read.Day))
            .Select(group => group.OrderBy(read => read.CompletedAt).First())
            // Mỗi trẻ tối đa 10 lượt mỗi ngày, tính trên mọi truyện: giữ 10 lượt sớm nhất.
            .GroupBy(read => (read.ChildProfileId, read.Day))
            .SelectMany(group => group.OrderBy(read => read.CompletedAt).ThenBy(read => read.StoryId)
                .Take(MaxValidReadsPerChildPerDay))
            .ToList();
    }

    private static bool IsCurrentlyPublished(Story story) =>
        story.IsPublished && story.ArchivedAt == null && story.Status != StoryStatus.Rejected;

    /// <summary>
    /// Truyện có đang Published lúc đọc không. Truyện bị gỡ vì vi phạm trong kỳ mất toàn bộ lượt của kỳ;
    /// truyện bị gỡ sau kỳ vẫn giữ lượt của kỳ này.
    /// </summary>
    private static bool WasPublishedAt(Story story, DateTime readAt, DateTime from, DateTime toExclusive)
    {
        if (story.Status == StoryStatus.Rejected || story.PublishedAt == null || readAt < story.PublishedAt)
        {
            return false;
        }

        if (story.ArchivedAt == null)
        {
            return story.IsPublished;
        }

        var removedInPeriod = story.ArchivedAt >= from && story.ArchivedAt < toExclusive;
        var removedForViolation = story.ArchivedReason != ArchivedReason.SupervisorPreference;
        if (removedInPeriod && removedForViolation)
        {
            return false;
        }

        return readAt < story.ArchivedAt;
    }

    private static decimal Rate(int completed, int views) =>
        views == 0 ? 0m : Math.Round((decimal)completed / views, 4);

    /// <summary>Phần chia = quỹ × lượt đọc hợp lệ của mình ÷ tổng lượt đọc hợp lệ của mọi Creator, làm tròn xuống đến đồng.</summary>
    private static long Share(int reads, long fund, int allReads) =>
        allReads == 0 ? 0 : reads * fund / allReads;

    private sealed record ValidRead(int StoryId, int ChildProfileId, DateTime CompletedAt, DateTime Day);
}
