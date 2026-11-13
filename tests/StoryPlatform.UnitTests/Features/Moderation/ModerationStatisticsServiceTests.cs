using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

/// <summary>F-19: thống kê hàng đợi và SLA cho Moderator (TC-WF05-F19-01..03).</summary>
public class ModerationStatisticsServiceTests
{
    private const int ModeratorA = 11;
    private const int ModeratorB = 12;
    private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodStart = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _store = new();
    private readonly ModerationStatisticsService _sut;
    private int _nextId = 1;

    public ModerationStatisticsServiceTests()
    {
        _sut = new ModerationStatisticsService(_store, new FixedClock(Now));
    }

    private ModerationItem Item(
        ModerationItemType type, ModerationItemStatus status, DateTime? slaDueAt = null, bool resubmission = false,
        int? decidedBy = null, DateTime? decidedAt = null, DateTime? createdAt = null) => _store.Seed(new ModerationItem
    {
        Id = _nextId++, ItemType = type, Status = status, SlaDueAt = slaDueAt, IsResubmission = resubmission,
        DecidedByUserId = decidedBy, DecidedAt = decidedAt, CreatedAt = createdAt ?? Now.AddDays(-2)
    });

    [Fact]
    public async Task Queue_CountsPendingClaimedAndOverdueAcrossWholeQueue_ByType()
    {
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Pending, Now.AddHours(5));
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Pending, Now.AddHours(-1));
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Claimed, Now.AddHours(-3));
        Item(ModerationItemType.ContentReport, ModerationItemStatus.Pending, Now.AddMinutes(1));
        // Đã quyết định / đã rút: không thuộc hàng đợi mở dù quá hạn.
        Item(ModerationItemType.ContentReport, ModerationItemStatus.Approved, Now.AddDays(-5));
        Item(ModerationItemType.ContentReport, ModerationItemStatus.Withdrawn, Now.AddDays(-5));

        var result = await _sut.GetAsync(ModeratorA, PeriodStart, PeriodEnd);

        Assert.Equal((3, 1, 2), (result.Pending, result.Claimed, result.Overdue));
        Assert.Equal(Now, result.AsOfUtc);
        var review = result.QueueByType.Single(row => row.ItemType == "PublicStoryReview");
        Assert.Equal((2, 1, 2), (review.Pending, review.Claimed, review.Overdue));
        var report = result.QueueByType.Single(row => row.ItemType == "ContentReport");
        Assert.Equal((1, 0, 0), (report.Pending, report.Claimed, report.Overdue));
    }

    [Fact]
    public async Task Decisions_OnlyCountTheCallingModeratorWithinThePeriod()
    {
        var inPeriod = new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Approved, decidedBy: ModeratorA, decidedAt: inPeriod);
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Approved, decidedBy: ModeratorA, decidedAt: inPeriod);
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Rejected, decidedBy: ModeratorA, decidedAt: inPeriod);
        Item(ModerationItemType.GrayZoneStory, ModerationItemStatus.ChangesRequested, decidedBy: ModeratorA, decidedAt: PeriodEnd.AddHours(20));
        // Của Moderator khác, ngoài kỳ (trước / sau): không tính.
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Approved, decidedBy: ModeratorB, decidedAt: inPeriod);
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Approved, decidedBy: ModeratorA, decidedAt: PeriodStart.AddSeconds(-1));
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Approved, decidedBy: ModeratorA, decidedAt: PeriodEnd.AddDays(1));

        var a = await _sut.GetAsync(ModeratorA, PeriodStart, PeriodEnd);
        var b = await _sut.GetAsync(ModeratorB, PeriodStart, PeriodEnd);

        Assert.Equal((2, 1, 1), (a.MyApproved, a.MyRejected, a.MyChangesRequested));
        var review = a.MyDecisionsByType.Single(row => row.ItemType == "PublicStoryReview");
        Assert.Equal((2, 1, 0), (review.Approved, review.Rejected, review.ChangesRequested));
        var grayZone = a.MyDecisionsByType.Single(row => row.ItemType == "GrayZoneStory");
        Assert.Equal((0, 0, 1), (grayZone.Approved, grayZone.Rejected, grayZone.ChangesRequested));
        Assert.Equal((1, 0, 0), (b.MyApproved, b.MyRejected, b.MyChangesRequested));
    }

    [Fact]
    public async Task ResubmissionAndGrayZone_CountItemsCreatedInThePeriod()
    {
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Pending, resubmission: true);
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Approved, resubmission: true);
        Item(ModerationItemType.GrayZoneStory, ModerationItemStatus.Pending);
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Pending);
        // Tạo ngoài kỳ: không tính.
        Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Pending, resubmission: true, createdAt: PeriodStart.AddDays(-3));
        Item(ModerationItemType.GrayZoneStory, ModerationItemStatus.Pending, createdAt: PeriodEnd.AddDays(2));

        var result = await _sut.GetAsync(ModeratorA, PeriodStart, PeriodEnd);

        Assert.Equal((2, 1), (result.ResubmissionCount, result.GrayZoneCount));
    }

    [Fact]
    public async Task EmptyQueue_ReturnsZeros_AndPeriodEndIsInclusiveLastDay()
    {
        var result = await _sut.GetAsync(ModeratorA, PeriodStart, PeriodEnd);

        Assert.Equal((0, 0, 0, 0, 0), (result.Pending, result.Claimed, result.Overdue, result.ResubmissionCount, result.GrayZoneCount));
        Assert.Empty(result.QueueByType);
        Assert.Equal(PeriodEnd, result.PeriodEnd);
    }

    [Fact]
    public async Task InvalidPeriod_Throws()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GetAsync(ModeratorA, PeriodEnd, PeriodStart));
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
