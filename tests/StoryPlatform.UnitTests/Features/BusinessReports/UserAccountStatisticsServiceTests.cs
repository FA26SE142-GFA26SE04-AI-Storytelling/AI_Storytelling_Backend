using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.BusinessReports;

public class UserAccountStatisticsServiceTests
{
    // Kỳ tính trọn ngày, gồm cả ngày kết thúc: 01/11 00:00 → hết ngày 30/11 (UTC).
    private static readonly DateTime PeriodStart = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BeforePeriod = PeriodStart.AddDays(-40);
    private static readonly DateTime MidPeriod = new(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterPeriod = PeriodEnd.AddDays(1);

    private readonly InMemoryUnitOfWork _store = new();
    private readonly UserAccountStatisticsService _sut;

    public UserAccountStatisticsServiceTests()
    {
        _sut = new UserAccountStatisticsService(_store);
    }

    private UserAccount Account(
        int id, DateTime createdAt, UserRole role = UserRole.User, AccountStatus status = AccountStatus.Active,
        DateTime? lastLoginAt = null, bool deleted = false) =>
        _store.Seed(new UserAccount
        {
            Id = id, Role = role, Status = status, CreatedAt = createdAt, LastLoginAt = lastLoginAt, IsDeleted = deleted,
            Email = $"u{id}@x.com", Username = $"u{id}"
        });

    private void SignedIn(int userId, DateTime at) =>
        _store.Seed(new RefreshToken { UserAccountId = userId, TokenHash = $"h{userId}{at.Ticks}", IssuedAt = at, ExpiresAt = at.AddDays(7) });

    private ChildProfile Child(
        int id, int ownerUserId, DateTime createdAt, ChildProfileStatus status = ChildProfileStatus.Active, bool deleted = false) =>
        _store.Seed(new ChildProfile { Id = id, OwnerUserId = ownerUserId, CreatedAt = createdAt, Status = status, IsDeleted = deleted });

    private void Read(int childProfileId, DateTime startedAt) =>
        _store.Seed(new ReadingSession { ChildProfileId = childProfileId, StoryId = 1, StartedAt = startedAt, Status = SessionStatus.Started });

    [Fact]
    public async Task GetAsync_CountsParentAccountsCreatedByTheEndOfThePeriod()
    {
        Account(1, BeforePeriod);
        Account(2, MidPeriod);
        Account(3, PeriodEnd.AddHours(23));                        // cuối ngày cuối kỳ: vẫn thuộc kỳ
        Account(4, AfterPeriod);                                   // đăng ký sau kỳ
        Account(5, BeforePeriod, UserRole.Administrator);          // không phải phụ huynh
        Account(6, BeforePeriod, UserRole.ContentCreator);
        Account(7, BeforePeriod, UserRole.Moderator);
        Account(8, BeforePeriod, deleted: true);                   // tài khoản đã xoá

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal((3, 2), (result.TotalParentAccounts, result.NewAccounts));
        Assert.Equal((PeriodStart, PeriodEnd), (result.PeriodStart, result.PeriodEnd));
    }

    [Fact]
    public async Task GetAsync_NewAccounts_AreThoseRegisteredInsideThePeriod()
    {
        Account(1, PeriodStart.AddSeconds(-1));
        Account(2, PeriodStart);
        Account(3, PeriodEnd.AddHours(23).AddMinutes(59));
        Account(4, AfterPeriod);
        Account(5, MidPeriod, UserRole.ContentCreator);

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(2, result.NewAccounts);
    }

    [Fact]
    public async Task GetAsync_ActiveAccounts_SignedInOrHadAChildReadingSessionInThePeriod()
    {
        Account(1, BeforePeriod); SignedIn(1, MidPeriod);                        // đăng nhập trong kỳ
        Account(2, BeforePeriod); Child(20, 2, BeforePeriod); Read(20, MidPeriod); // con đọc truyện trong kỳ
        Account(3, BeforePeriod, lastLoginAt: MidPeriod);                        // lần đăng nhập gần nhất trong kỳ
        Account(4, BeforePeriod); SignedIn(4, MidPeriod); SignedIn(4, MidPeriod.AddDays(1));
        Child(40, 4, BeforePeriod); Read(40, MidPeriod);                         // vừa đăng nhập vừa có phiên đọc: đếm một lần
        Account(5, BeforePeriod); SignedIn(5, BeforePeriod);                     // chỉ hoạt động trước kỳ
        Child(50, 5, BeforePeriod); Read(50, AfterPeriod);                       // và sau kỳ
        Account(6, BeforePeriod);                                                // không hoạt động
        Account(7, BeforePeriod, UserRole.Administrator); SignedIn(7, MidPeriod); // quản trị viên không tính
        Account(8, AfterPeriod); SignedIn(8, MidPeriod);                         // dữ liệu lệch: tài khoản tạo sau kỳ không tính

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(4, result.ActiveAccounts);
    }

    [Fact]
    public async Task GetAsync_ActivityOnTheLastDayOfThePeriod_Counts()
    {
        Account(1, BeforePeriod); SignedIn(1, PeriodEnd.AddHours(22));
        Account(2, BeforePeriod); Child(20, 2, BeforePeriod); Read(20, PeriodEnd.AddHours(23));
        Account(3, BeforePeriod); SignedIn(3, AfterPeriod);

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(2, result.ActiveAccounts);
    }

    [Fact]
    public async Task GetAsync_LockedOrDisabledAccounts_AreSuspendedParentAccounts()
    {
        Account(1, BeforePeriod, status: AccountStatus.Suspended);
        Account(2, MidPeriod, status: AccountStatus.Suspended);
        Account(3, BeforePeriod, status: AccountStatus.Active);
        Account(4, BeforePeriod, status: AccountStatus.Registered);              // chưa xác thực email: không phải bị khoá
        Account(5, AfterPeriod, status: AccountStatus.Suspended);
        Account(6, BeforePeriod, UserRole.ContentCreator, AccountStatus.Suspended);

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal((4, 2), (result.TotalParentAccounts, result.LockedOrDisabledAccounts));
    }

    [Fact]
    public async Task GetAsync_ChildProfiles_AreThoseCreatedByTheEndOfThePeriodAndNotDeleted()
    {
        Account(1, BeforePeriod);
        Child(10, 1, BeforePeriod);
        Child(11, 1, MidPeriod, ChildProfileStatus.Draft);
        Child(12, 1, BeforePeriod, ChildProfileStatus.Archived);
        Child(13, 1, AfterPeriod);
        Child(14, 1, BeforePeriod, ChildProfileStatus.Deleted);
        Child(15, 1, BeforePeriod, deleted: true);

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(3, result.ChildProfiles);
    }

    [Fact]
    public async Task GetAsync_NoData_ReturnsZeroes()
    {
        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(
            (0, 0, 0, 0, 0),
            (result.TotalParentAccounts, result.NewAccounts, result.ActiveAccounts, result.LockedOrDisabledAccounts, result.ChildProfiles));
    }

    [Fact]
    public async Task GetAsync_PeriodEndBeforeStart_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GetAsync(PeriodEnd, PeriodStart));
    }
}
