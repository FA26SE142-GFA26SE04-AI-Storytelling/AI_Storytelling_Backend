using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Application.Common.Time;
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
    private const int MaxValidReadsPerChildPerDay = 10;
    // "Mỗi ngày" của trẻ tính theo giờ Việt Nam.

    private readonly IUnitOfWork _unitOfWork;
    private readonly IRevenueStatisticsService _revenueStatisticsService;
    private readonly IPlanSettingsProvider? _planSettings;
    private readonly ITokenQuotaService? _tokenQuota;
    private readonly TimeProvider _clock;

    // Ngưỡng Creator đối tác (Luồng 5, 5g, cập nhật 15/11/2026) và cửa sổ tính.
    public const int PartnerWindowDays = 90;
    public const int PartnerMinPublishedStories = 10;
    public const decimal PartnerMinCompletionRate = 0.6m;
    public const int PartnerMinValidReads = 600;

    /// <param name="planSettings">Thời gian đọc tối thiểu do Admin cấu hình; null thì dùng mặc định 60 giây.</param>
    /// <param name="tokenQuota">Chỉ cần cho <see cref="GetContentAsync"/> (lượt AI / lượt gửi duyệt còn lại).</param>
    public CreatorStatisticsService(
        IUnitOfWork unitOfWork, IRevenueStatisticsService revenueStatisticsService, IPlanSettingsProvider? planSettings = null,
        ITokenQuotaService? tokenQuota = null, TimeProvider? clock = null)
    {
        _unitOfWork = unitOfWork;
        _revenueStatisticsService = revenueStatisticsService;
        _planSettings = planSettings;
        _tokenQuota = tokenQuota;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<CreatorContentStatisticsDto> GetContentAsync(
        int creatorUserId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default)
    {
        var (from, toExclusive) = ReportPeriod.Resolve(periodStart, periodEnd);
        var creatorStories = await GetCreatorStoriesAsync(cancellationToken);
        var mine = creatorStories.Where(story => story.AuthorUserId == creatorUserId).ToList();

        var reviews = (await _unitOfWork.Repository<ModerationItem>().FindAsync(
                item => item.ItemType == ModerationItemType.PublicStoryReview && item.CreatedByUserId == creatorUserId,
                cancellationToken: cancellationToken))
            .ToList();
        var openReviewStoryIds = reviews
            .Where(item => item.StoryId != null && item.Status is ModerationItemStatus.Pending
                or ModerationItemStatus.Claimed or ModerationItemStatus.Escalated)
            .Select(item => item.StoryId!.Value)
            .ToHashSet();

        // Mỗi truyện chưa archive chỉ vào một nhóm: Published > chờ duyệt > Ready > Nháp.
        var live = mine.Where(story => story.ArchivedAt == null).ToList();
        var published = live.Count(IsCurrentlyPublished);
        var unpublished = live.Where(story => !IsCurrentlyPublished(story)).ToList();
        var pending = unpublished.Count(story => openReviewStoryIds.Contains(story.Id));
        var ready = unpublished.Count(story => !openReviewStoryIds.Contains(story.Id) && story.Status == StoryStatus.Ready);

        bool DecidedInPeriod(ModerationItem item, ModerationItemStatus status) =>
            item.Status == status && item.DecidedAt >= from && item.DecidedAt < toExclusive;

        var quota = await RequireTokenQuota().GetCreatorQuotaAsync(creatorUserId, cancellationToken);
        var now = _clock.GetUtcNow().UtcDateTime;

        return new CreatorContentStatisticsDto
        {
            PeriodStart = from,
            PeriodEnd = toExclusive.AddDays(-1),
            Published = published,
            PendingReview = pending,
            Ready = ready,
            Draft = unpublished.Count - pending - ready,
            SubmissionsApproved = reviews.Count(item => DecidedInPeriod(item, ModerationItemStatus.Approved)),
            SubmissionsRejected = reviews.Count(item => DecidedInPeriod(item, ModerationItemStatus.Rejected)),
            SubmissionsChangesRequested = reviews.Count(item => DecidedInPeriod(item, ModerationItemStatus.ChangesRequested)),
            SubmissionsWithdrawn = reviews.Count(
                item => item.Status == ModerationItemStatus.Withdrawn && item.WithdrawnAt >= from && item.WithdrawnAt < toExclusive),
            RemainingAiCredits = quota.AiCredits.TotalRemaining,
            RemainingSubmitCredits = quota.SubmitCredits.TotalRemaining,
            Tier = (await _unitOfWork.Repository<UserAccount>().GetByIdAsync(creatorUserId, cancellationToken))?.CreatorTier
                   == CreatorTier.Partner
                ? PlanBenefitsDto.PartnerCreatorTier
                : PlanBenefitsDto.CreatorTier,
            PartnerProgress = (await GetPartnerProgressAsync(creatorStories, [creatorUserId], now, cancellationToken))[creatorUserId]
        };
    }

    public async Task<IReadOnlyDictionary<int, PartnerProgressDto>> GetPartnerProgressAsync(
        IReadOnlyCollection<int> creatorUserIds, DateTime now, CancellationToken cancellationToken = default) =>
        await GetPartnerProgressAsync(await GetCreatorStoriesAsync(cancellationToken), creatorUserIds, now, cancellationToken);

    private ITokenQuotaService RequireTokenQuota() =>
        _tokenQuota ?? throw new InvalidOperationException("ITokenQuotaService chưa được cấu hình cho thống kê nội dung.");

    /// <summary>
    /// Tiến độ Creator đối tác trên cửa sổ trượt 90 ngày [hôm nay − 90 ngày, hôm nay] theo UTC: số truyện Published,
    /// tỷ lệ đọc hoàn thành, lượt đọc hợp lệ và số truyện bị gỡ vì vi phạm. Chỉ tính, không nâng hạng (CreatorTierService
    /// dùng kết quả này để xét cấp). Phiên đọc và lượt đọc hợp lệ được đọc một lần cho mọi Creator cần tính.
    /// </summary>
    private async Task<Dictionary<int, PartnerProgressDto>> GetPartnerProgressAsync(
        IReadOnlyList<Story> creatorStories, IReadOnlyCollection<int> creatorUserIds, DateTime now,
        CancellationToken cancellationToken)
    {
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
        var windowStart = today.AddDays(-PartnerWindowDays);
        var windowEnd = today.AddDays(1);

        var opened = await _unitOfWork.Repository<ReadingSession>().FindAsync(
            session => session.StartedAt >= windowStart && session.StartedAt < windowEnd,
            cancellationToken: cancellationToken);
        var validReads = await GetValidReadsAsync(creatorStories, windowStart, windowEnd, cancellationToken);

        var result = new Dictionary<int, PartnerProgressDto>();
        foreach (var creatorUserId in creatorUserIds.Distinct())
        {
            var mine = creatorStories.Where(story => story.AuthorUserId == creatorUserId).ToList();
            var ownIds = mine.Select(story => story.Id).ToHashSet();
            var publishedIds = mine.Where(story => story.ArchivedAt == null && IsCurrentlyPublished(story))
                .Select(story => story.Id).ToHashSet();
            var myOpened = opened.Where(session => publishedIds.Contains(session.StoryId)).ToList();
            var completionRate = Rate(myOpened.Count(session => session.Status == SessionStatus.Completed), myOpened.Count);
            var myValidReads = validReads.Count(read => ownIds.Contains(read.StoryId));
            var violationRemovals = mine.Count(
                story => story.ArchivedAt >= windowStart && story.ArchivedAt < windowEnd
                         && story.ArchivedReason is not null and not ArchivedReason.SupervisorPreference);

            var criteria = new List<PartnerCriterionDto>
            {
                new() { Key = "published_stories", Current = publishedIds.Count, Target = PartnerMinPublishedStories, Met = publishedIds.Count >= PartnerMinPublishedStories },
                new() { Key = "completion_rate", Current = completionRate, Target = PartnerMinCompletionRate, Met = completionRate >= PartnerMinCompletionRate },
                new() { Key = "valid_reads", Current = myValidReads, Target = PartnerMinValidReads, Met = myValidReads >= PartnerMinValidReads },
                new() { Key = "no_violation_removals", Current = violationRemovals, Target = 0, Met = violationRemovals == 0 }
            };
            result[creatorUserId] = new PartnerProgressDto
            {
                WindowStart = windowStart,
                WindowEnd = today,
                IsEligible = criteria.All(criterion => criterion.Met),
                Criteria = criteria
            };
        }

        return result;
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
            MinimumReadSeconds = await MinimumReadSecondsAsync(cancellationToken),
            TotalValidReadsAllCreators = allValidReads,
            MyValidReads = myValidReads,
            EstimatedEarningsVnd = Share(myValidReads, fund, allValidReads),
            Stories = rows
        };
    }

    public async Task<CreatorPeriodEarningsDto> GetPeriodEarningsAsync(
        DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default)
    {
        var (from, toExclusive) = ReportPeriod.Resolve(periodStart, periodEnd);
        var creatorStories = await GetCreatorStoriesAsync(cancellationToken);
        var authorByStory = creatorStories.ToDictionary(story => story.Id, story => story.AuthorUserId);
        var validReads = await GetValidReadsAsync(creatorStories, from, toExclusive, cancellationToken);
        var revenue = await _revenueStatisticsService.GetAsync(from, toExclusive.AddDays(-1), cancellationToken);
        var fund = revenue.CreatorFundVnd;

        return new CreatorPeriodEarningsDto
        {
            PeriodStart = from,
            PeriodEnd = toExclusive.AddDays(-1),
            CreatorFundVnd = fund,
            TotalValidReads = validReads.Count,
            Creators = validReads
                .GroupBy(read => authorByStory[read.StoryId])
                .OrderBy(group => group.Key)
                .Select(group => new CreatorPeriodEarningDto
                {
                    CreatorUserId = group.Key,
                    ValidReads = group.Count(),
                    EarningsVnd = Share(group.Count(), fund, validReads.Count)
                })
                .ToList()
        };
    }

    private async Task<int> MinimumReadSecondsAsync(CancellationToken cancellationToken) =>
        _planSettings == null ? new PlanSettings().MinimumReadSeconds
            : (await _planSettings.GetAsync(cancellationToken)).MinimumReadSeconds;

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

        var minimumReadSeconds = await MinimumReadSecondsAsync(cancellationToken);
        var eligible = new List<ValidRead>();
        foreach (var session in completed)
        {
            var completedAt = session.CompletedAt!.Value;
            if (session.TimeSpentSeconds < minimumReadSeconds
                || !storyById.TryGetValue(session.StoryId, out var story)
                || !WasPublishedAt(story, completedAt, from, toExclusive)
                || !ownerByChild.TryGetValue(session.ChildProfileId, out var ownerUserId)
                || ownerUserId == story.AuthorUserId
                || !plansByUser[ownerUserId].Any(plan => plan.StartAt <= completedAt && completedAt < plan.EndAt))
            {
                continue;
            }

            eligible.Add(new ValidRead(
                session.StoryId, session.ChildProfileId, completedAt, VietnamTime.LocalDate(completedAt)));
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
