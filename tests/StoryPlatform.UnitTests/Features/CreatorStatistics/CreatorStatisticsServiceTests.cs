using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Application.Features.CreatorStatistics.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.CreatorStatistics;

public class CreatorStatisticsServiceTests
{
    private const int CreatorId = 50;
    private const int OtherCreatorId = 51;
    private const int PaidParentId = 1;
    private const int FreeParentId = 2;
    private const int PaidChildId = 10;
    private const int SecondPaidChildId = 11;
    private const int FreeChildId = 20;
    private const int CreatorsOwnChildId = 30;
    private const int StoryId = 100;
    private const int SecondStoryId = 101;
    private const int OtherCreatorStoryId = 200;

    // Kỳ tính trọn ngày theo UTC, gồm cả ngày kết thúc.
    private static readonly DateTime PeriodStart = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BeforePeriod = PeriodStart.AddDays(-40);
    // 02:00 UTC = 09:00 giờ Việt Nam.
    private static readonly DateTime Morning = new(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _store = new();
    private readonly CreatorStatisticsService _sut;
    private int _nextSessionId = 1;
    private int _nextPaymentId = 1;

    public CreatorStatisticsServiceTests()
    {
        _store.Seed(new UserAccount { Id = CreatorId, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        _store.Seed(new UserAccount { Id = OtherCreatorId, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        _store.Seed(new UserAccount { Id = PaidParentId, Role = UserRole.User, Status = AccountStatus.Active });
        _store.Seed(new UserAccount { Id = FreeParentId, Role = UserRole.User, Status = AccountStatus.Active });
        _store.Seed(new SubscriptionPlan { Id = 2, Name = "Gói Đọc", PlanType = PlanType.Reading, PriceVnd = 49000 });
        _store.Seed(new SubscriptionPlan { Id = 3, Name = "Gói Sáng tạo", PlanType = PlanType.Creative, PriceVnd = 99000, AiCredits = 4 });

        Child(PaidChildId, PaidParentId);
        Child(SecondPaidChildId, PaidParentId);
        Child(FreeChildId, FreeParentId);
        Child(CreatorsOwnChildId, CreatorId);
        // Phụ huynh 1 có gói trả phí phủ hết kỳ; cả Creator cũng có gói (để loại trừ "con của Creator" không do thiếu gói).
        Subscription(PaidParentId, BeforePeriod, PeriodEnd.AddDays(30));
        Subscription(CreatorId, BeforePeriod, PeriodEnd.AddDays(30));

        PublishedStory(StoryId, CreatorId, "Chú ếch nhỏ");
        PublishedStory(SecondStoryId, CreatorId, "Đầm sen");
        PublishedStory(OtherCreatorStoryId, OtherCreatorId, "Truyện của người khác");

        _sut = new CreatorStatisticsService(_store, new RevenueStatisticsService(_store));
    }

    private void Child(int id, int ownerUserId) =>
        _store.Seed(new ChildProfile { Id = id, OwnerUserId = ownerUserId, Status = ChildProfileStatus.Active, CreatedAt = BeforePeriod });

    private void Subscription(int userId, DateTime startAt, DateTime endAt) => _store.Seed(new UserSubscription
    {
        UserId = userId, PlanId = 2, Status = SubscriptionStatus.Active, StartAt = startAt, EndAt = endAt, PaymentId = 9000 + _nextPaymentId++
    });

    private Story PublishedStory(int id, int authorUserId, string title) => _store.Seed(new Story
    {
        Id = id, AuthorUserId = authorUserId, Title = title, Scope = StoryScope.Creator, Status = StoryStatus.Ready,
        IsPublished = true, PublishedAt = BeforePeriod, CreatedAt = BeforePeriod
    });

    private Story StoryOf(int id) => _store.Items<Story>().Single(story => story.Id == id);

    /// <summary>Phiên đọc Completed, đủ thời gian đọc tối thiểu, trừ khi chỉ định khác.</summary>
    private ReadingSession Completed(int storyId, int childId, DateTime completedAt, int seconds = 180) => _store.Seed(new ReadingSession
    {
        Id = _nextSessionId++, StoryId = storyId, ChildProfileId = childId, Status = SessionStatus.Completed,
        StartedAt = completedAt.AddSeconds(-seconds), CompletedAt = completedAt, TimeSpentSeconds = seconds
    });

    private ReadingSession Opened(int storyId, int childId, DateTime startedAt, SessionStatus status = SessionStatus.Abandoned) =>
        _store.Seed(new ReadingSession
        {
            Id = _nextSessionId++, StoryId = storyId, ChildProfileId = childId, Status = status, StartedAt = startedAt
        });

    private void PaidRevenue(int planId, int amount, DateTime paidAt) => _store.Seed(new PaymentTransaction
    {
        Id = _nextPaymentId++, PlanId = planId, PayerUserId = PaidParentId, TransactionCode = $"SEPAYCRS{_nextPaymentId:D7}",
        Amount = amount, Status = PaymentStatus.Paid, PaidAt = paidAt, CreatedAt = paidAt, ExpiresAt = paidAt.AddMinutes(15)
    });

    private async Task<int> ValidReadsAsync(int storyId = StoryId) =>
        (await _sut.GetReadingAsync(CreatorId, PeriodStart, PeriodEnd, storyId)).Stories.Single().ValidReads;

    // ---------- Lượt đọc hợp lệ ----------

    [Fact]
    public async Task ValidRead_CompletedReadOfPublishedCreatorStoryByPaidAccountChild_Counts()
    {
        Completed(StoryId, PaidChildId, Morning);

        Assert.Equal(1, await ValidReadsAsync());
    }

    [Fact]
    public async Task ValidRead_RequiresCompletedSession()
    {
        Opened(StoryId, PaidChildId, Morning, SessionStatus.Reading);
        Opened(StoryId, PaidChildId, Morning.AddDays(1), SessionStatus.Abandoned);

        Assert.Equal(0, await ValidReadsAsync());
    }

    [Fact]
    public async Task ValidRead_RequiresAnActivePaidPlanAtTheTimeOfReading()
    {
        Completed(StoryId, FreeChildId, Morning);                         // tài khoản Miễn phí
        Subscription(FreeParentId, Morning.AddDays(5), Morning.AddDays(35));
        Completed(StoryId, FreeChildId, Morning.AddDays(4));              // trước khi gói bắt đầu
        Completed(StoryId, FreeChildId, Morning.AddDays(6));              // gói đang hiệu lực

        Assert.Equal(1, await ValidReadsAsync());
    }

    [Fact]
    public async Task ValidRead_PlanExpiredBeforeTheRead_DoesNotCount()
    {
        Subscription(FreeParentId, BeforePeriod, Morning.AddDays(-1));
        Completed(StoryId, FreeChildId, Morning);

        Assert.Equal(0, await ValidReadsAsync());
    }

    [Theory]
    [InlineData(59, 0)]
    [InlineData(60, 1)]
    [InlineData(600, 1)]
    public async Task ValidRead_RequiresMinimumReadingTime(int seconds, int expected)
    {
        Completed(StoryId, PaidChildId, Morning, seconds);

        Assert.Equal(expected, await ValidReadsAsync());
    }

    [Fact]
    public async Task ValidRead_SameChildSameStorySameDay_CountsOnce_ButAgainTheNextDay()
    {
        Completed(StoryId, PaidChildId, Morning);
        Completed(StoryId, PaidChildId, Morning.AddHours(3));
        Completed(StoryId, PaidChildId, Morning.AddDays(1));
        Completed(StoryId, SecondPaidChildId, Morning);                   // trẻ khác: tính riêng

        Assert.Equal(3, await ValidReadsAsync());
    }

    [Fact]
    public async Task ValidRead_DayIsTheVietnamDay_NotTheUtcDay()
    {
        // 23:30 UTC ngày 14 = 06:30 ngày 15 giờ Việt Nam; 12:00 UTC ngày 15 = 19:00 ngày 15: cùng một ngày với trẻ.
        Completed(StoryId, PaidChildId, new DateTime(2026, 11, 14, 23, 30, 0, DateTimeKind.Utc));
        Completed(StoryId, PaidChildId, new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, await ValidReadsAsync());
    }

    [Fact]
    public async Task ValidRead_AtMostTenPerChildPerDay_AcrossStories()
    {
        for (var index = 0; index < 12; index++)
        {
            PublishedStory(300 + index, OtherCreatorId, $"Truyện {index}");
            Completed(300 + index, PaidChildId, Morning.AddMinutes(index * 10));
        }
        Completed(StoryId, PaidChildId, Morning.AddHours(5));             // lượt thứ 13 trong ngày của trẻ này
        Completed(StoryId, SecondPaidChildId, Morning.AddHours(5));       // trẻ khác chưa chạm trần

        var mine = await ValidReadsAsync();
        var revenue = await _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal(1, mine);
        Assert.Equal(11, revenue.TotalValidReadsAllCreators);
    }

    [Fact]
    public async Task ValidRead_ExcludesTheCreatorsOwnChildren()
    {
        Completed(StoryId, CreatorsOwnChildId, Morning);

        Assert.Equal(0, await ValidReadsAsync());
    }

    [Fact]
    public async Task ValidRead_StoryRemovedForViolationInThePeriod_LosesAllItsValidReadsOfThePeriod()
    {
        Completed(StoryId, PaidChildId, Morning);
        Completed(SecondStoryId, PaidChildId, Morning);
        var removed = StoryOf(StoryId);
        removed.IsPublished = false;
        removed.ArchivedAt = Morning.AddDays(5);
        removed.ArchivedReason = ArchivedReason.Inappropriate;

        var revenue = await _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal((1, 1), (revenue.MyValidReads, revenue.TotalValidReadsAllCreators));
        Assert.DoesNotContain(revenue.Stories, story => story.StoryId == StoryId && story.ValidReads > 0);
    }

    [Fact]
    public async Task ValidRead_StoryRemovedAfterThePeriod_KeepsItsValidReads()
    {
        Completed(StoryId, PaidChildId, Morning);
        var removed = StoryOf(StoryId);
        removed.IsPublished = false;
        removed.ArchivedAt = PeriodEnd.AddDays(10);
        removed.ArchivedReason = ArchivedReason.Inappropriate;

        var revenue = await _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal(1, revenue.MyValidReads);
    }

    [Fact]
    public async Task ValidRead_UnpublishedOrParentStories_DoNotCount()
    {
        _store.Seed(new Story { Id = 400, AuthorUserId = CreatorId, Title = "Bản nháp", Scope = StoryScope.Creator, Status = StoryStatus.Ready, IsPublished = false });
        _store.Seed(new Story { Id = 401, AuthorUserId = PaidParentId, Title = "Truyện gia đình", Scope = StoryScope.Parent, Status = StoryStatus.Ready, IsPublished = true, PublishedAt = BeforePeriod });
        Completed(400, PaidChildId, Morning);
        Completed(401, PaidChildId, Morning);
        Completed(StoryId, PaidChildId, PeriodStart.AddDays(-45));        // trước khi truyện được xuất bản

        var revenue = await _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal(0, revenue.TotalValidReadsAllCreators);
    }

    [Fact]
    public async Task ValidRead_OnlyReadsCompletedInsideThePeriod_Count()
    {
        Completed(StoryId, PaidChildId, PeriodStart.AddMinutes(-1));
        Completed(StoryId, PaidChildId, PeriodStart.AddHours(1));
        Completed(StoryId, SecondPaidChildId, PeriodEnd.AddHours(23));    // cuối ngày cuối kỳ
        Completed(StoryId, SecondPaidChildId, PeriodEnd.AddDays(1));

        Assert.Equal(2, await ValidReadsAsync());
    }

    // ---------- F-21: thống kê lượt đọc ----------

    [Fact]
    public async Task GetReadingAsync_ReportsViewsFavoritesCompletedValidReadsAndCompletionRatePerStory()
    {
        Completed(StoryId, PaidChildId, Morning);
        Completed(StoryId, FreeChildId, Morning);                         // hoàn thành nhưng không hợp lệ (Miễn phí)
        Opened(StoryId, SecondPaidChildId, Morning);
        Opened(StoryId, SecondPaidChildId, Morning.AddDays(1), SessionStatus.Reading);
        Opened(StoryId, PaidChildId, PeriodStart.AddDays(-3));            // mở trước kỳ: không tính
        Completed(SecondStoryId, PaidChildId, Morning);
        _store.Seed(new ReadingProgress { ChildProfileId = PaidChildId, StoryId = StoryId, IsFavorited = true });
        _store.Seed(new ReadingProgress { ChildProfileId = FreeChildId, StoryId = StoryId, IsFavorited = true });
        _store.Seed(new ReadingProgress { ChildProfileId = SecondPaidChildId, StoryId = StoryId, IsFavorited = false });

        var result = await _sut.GetReadingAsync(CreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal(
            ["100:Chú ếch nhỏ:4:2:2:1:0.5", "101:Đầm sen:1:0:1:1:1"],
            result.Stories.Select(story =>
                $"{story.StoryId}:{story.Title}:{story.Views}:{story.Favorites}:{story.CompletedReadings}:{story.ValidReads}:{story.CompletionRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        Assert.Equal((5, 2, 3, 2, 0.6m), (result.Views, result.Favorites, result.CompletedReadings, result.ValidReads, result.CompletionRate));
        Assert.Equal((PeriodStart, PeriodEnd), (result.PeriodStart, result.PeriodEnd));
    }

    [Fact]
    public async Task GetReadingAsync_ListsOnlyTheCreatorsOwnPublishedStories()
    {
        _store.Seed(new Story { Id = 400, AuthorUserId = CreatorId, Title = "Bản nháp", Scope = StoryScope.Creator, Status = StoryStatus.Ready, IsPublished = false });
        Completed(OtherCreatorStoryId, PaidChildId, Morning);

        var result = await _sut.GetReadingAsync(CreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal([StoryId, SecondStoryId], result.Stories.Select(story => story.StoryId));
        Assert.Equal((0, 0), (result.Views, result.ValidReads));
    }

    [Fact]
    public async Task GetReadingAsync_NoViews_CompletionRateIsZero()
    {
        var result = await _sut.GetReadingAsync(CreatorId, PeriodStart, PeriodEnd, StoryId);

        Assert.Equal(0m, result.Stories.Single().CompletionRate);
        Assert.Equal(0m, result.CompletionRate);
    }

    [Fact]
    public async Task GetReadingAsync_AnotherCreatorsStory_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.GetReadingAsync(CreatorId, PeriodStart, PeriodEnd, OtherCreatorStoryId));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, OtherCreatorStoryId));
    }

    [Fact]
    public async Task GetReadingAsync_UnknownStory_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetReadingAsync(CreatorId, PeriodStart, PeriodEnd, 99999));
    }

    [Fact]
    public async Task GetReadingAsync_PeriodEndBeforeStart_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GetReadingAsync(CreatorId, PeriodEnd, PeriodStart, null));
    }

    // ---------- F-20: thu nhập dự kiến ----------

    [Fact]
    public async Task GetRevenueAsync_SharesTheCreatorFundByValidReads_RoundedDown()
    {
        // Quỹ = 30% × 49.000 + 10% × 99.000 = 24.600 đ. Add-on không vào quỹ.
        PaidRevenue(2, 49000, Morning);
        PaidRevenue(3, 99000, Morning);
        // Creator này: 2 lượt hợp lệ (truyện 100) + 1 lượt (truyện 101); Creator khác: 4 lượt. Tổng 7.
        Completed(StoryId, PaidChildId, Morning);
        Completed(StoryId, SecondPaidChildId, Morning);
        Completed(SecondStoryId, PaidChildId, Morning);
        for (var day = 1; day <= 4; day++) Completed(OtherCreatorStoryId, PaidChildId, Morning.AddDays(day));

        var result = await _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal((24600L, 7, 3), (result.CreatorFundVnd, result.TotalValidReadsAllCreators, result.MyValidReads));
        // 3 × 24.600 ÷ 7 = 10.542,86 → 10.542 đ.
        Assert.Equal(10542L, result.EstimatedEarningsVnd);
        // Theo truyện: 2 × 24.600 ÷ 7 = 7.028,57 → 7.028; 1 × 24.600 ÷ 7 = 3.514,29 → 3.514.
        Assert.Equal(
            ["100:2:7028", "101:1:3514"],
            result.Stories.Select(story => $"{story.StoryId}:{story.ValidReads}:{story.EstimatedEarningsVnd}"));
        Assert.Equal((0.30m, 0.10m, 60), (result.ReadingFundRate, result.CreativeFundRate, result.MinimumReadSeconds));
    }

    [Fact]
    public async Task GetRevenueAsync_NoValidReadsAnywhere_EarningsAreZero()
    {
        PaidRevenue(2, 49000, Morning);

        var result = await _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal((14700L, 0, 0, 0L), (result.CreatorFundVnd, result.TotalValidReadsAllCreators, result.MyValidReads, result.EstimatedEarningsVnd));
    }

    [Fact]
    public async Task GetRevenueAsync_EachCreatorSeesOnlyTheirOwnFigures()
    {
        PaidRevenue(3, 100000, Morning);                                  // quỹ 10.000 đ
        Completed(StoryId, PaidChildId, Morning);
        Completed(OtherCreatorStoryId, PaidChildId, Morning);
        Completed(OtherCreatorStoryId, SecondPaidChildId, Morning);
        Completed(OtherCreatorStoryId, PaidChildId, Morning.AddDays(1));

        var mine = await _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, null);
        var theirs = await _sut.GetRevenueAsync(OtherCreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal((1, 2500L), (mine.MyValidReads, mine.EstimatedEarningsVnd));
        Assert.Equal((3, 7500L), (theirs.MyValidReads, theirs.EstimatedEarningsVnd));
        Assert.All(mine.Stories, story => Assert.NotEqual(OtherCreatorStoryId, story.StoryId));
        Assert.Equal([OtherCreatorStoryId], theirs.Stories.Select(story => story.StoryId));
    }

    [Fact]
    public async Task GetRevenueAsync_FundOnlyCountsPaymentsOfThePeriod()
    {
        PaidRevenue(3, 100000, PeriodStart.AddDays(-2));
        PaidRevenue(3, 100000, PeriodEnd.AddDays(2));
        Completed(StoryId, PaidChildId, Morning);

        var result = await _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, null);

        Assert.Equal((0L, 0L), (result.CreatorFundVnd, result.EstimatedEarningsVnd));
    }

    [Fact]
    public async Task GetRevenueAsync_SingleStoryFilter_ReturnsThatStoryOnly()
    {
        PaidRevenue(3, 100000, Morning);
        Completed(StoryId, PaidChildId, Morning);
        Completed(SecondStoryId, PaidChildId, Morning);

        var result = await _sut.GetRevenueAsync(CreatorId, PeriodStart, PeriodEnd, SecondStoryId);

        Assert.Equal([SecondStoryId], result.Stories.Select(story => story.StoryId));
        Assert.Equal((1, 2, 5000L), (result.MyValidReads, result.TotalValidReadsAllCreators, result.EstimatedEarningsVnd));
    }
}
