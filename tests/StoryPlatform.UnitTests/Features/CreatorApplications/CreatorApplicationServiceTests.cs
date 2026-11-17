using Moq;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.CreatorApplications.DTOs;
using StoryPlatform.Application.Features.CreatorApplications.Interfaces;
using StoryPlatform.Application.Features.CreatorApplications.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.CreatorApplications;

/// <summary>Creator tự đăng ký và Admin duyệt đơn (Luồng 5, M5).</summary>
public class CreatorApplicationServiceTests
{
    private const int AdminId = 1;
    private const int CreatorId = 2;

    private readonly InMemoryUnitOfWork _store = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly CreatorApplicationService _sut;

    public CreatorApplicationServiceTests()
    {
        _store.Seed(new UserAccount
        {
            Id = AdminId, Username = "admin", FullName = "Quản trị", Role = UserRole.Administrator,
            Status = AccountStatus.Active
        });
        _sut = new CreatorApplicationService(_store, _audit.Object, _notifications.Object, _email.Object);
    }

    private UserAccount SeedCreator(AccountStatus status = AccountStatus.AwaitingApproval) =>
        _store.Seed(new UserAccount
        {
            Id = CreatorId, Username = "creator_new", Email = "creator@example.test", FullName = "Tác giả",
            Role = UserRole.ContentCreator, Status = status
        });

    private CreatorApplication SeedApplication(
        CreatorApplicationStatus status = CreatorApplicationStatus.Pending, DateTime? createdAt = null) =>
        _store.Seed(new CreatorApplication
        {
            ApplicantUserId = CreatorId, Introduction = "Tôi viết truyện thiếu nhi đã 5 năm.", Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow
        });

    private AuthService NewAuthService() => new(
        _store, Mock.Of<IPasswordHasher>(hasher => hasher.HashPassword(It.IsAny<string>()) == "hashed"),
        Mock.Of<IJwtTokenGenerator>(), _email.Object, Mock.Of<IChildSessionRevoker>(), _notifications.Object);

    // ---------- Đăng ký, xác thực email, role trong token ----------

    [Fact]
    public async Task RegisterCreator_CreatesUnverifiedCreatorWithPendingApplication()
    {
        await NewAuthService().RegisterCreatorAsync(new RegisterCreatorRequestDto
        {
            Username = "tac_gia", Email = "Tac.Gia@Example.test", FullName = "Tác Giả", Password = "Demo@123",
            ConfirmPassword = "Demo@123", Introduction = "  Tôi viết truyện thiếu nhi đã 5 năm.  ",
            PortfolioUrl = " "
        });

        var user = Assert.Single(_store.Items<UserAccount>(), item => item.Username == "tac_gia");
        Assert.Equal((UserRole.ContentCreator, AccountStatus.Registered), (user.Role, user.Status));
        var application = Assert.Single(_store.Items<CreatorApplication>());
        Assert.Equal(
            (user.Id, CreatorApplicationStatus.Pending, "Tôi viết truyện thiếu nhi đã 5 năm.", (string?)null),
            (application.ApplicantUserId, application.Status, application.Introduction, application.PortfolioUrl));
        _email.Verify(e => e.SendEmailVerificationEmailAsync(
            "tac.gia@example.test", "Tác Giả", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyEmail_ForSelfRegisteredCreator_WaitsForApprovalInsteadOfActivating()
    {
        var creator = SeedCreator(AccountStatus.Registered);
        creator.EmailVerificationTokenHash = OtpHelper.Hash(OtpHelper.EmailVerifyPurpose, creator.Email, "123456");
        creator.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddMinutes(5);

        await NewAuthService().VerifyEmailAsync(new VerifyEmailRequestDto { Email = creator.Email, Otp = "123456" });

        Assert.Equal(AccountStatus.AwaitingApproval, creator.Status);
        Assert.NotNull(creator.EmailVerifiedAt);
    }

    [Theory]
    [InlineData(AccountStatus.Registered)]
    [InlineData(AccountStatus.AwaitingApproval)]
    public async Task ResetPassword_DoesNotLetAnUnapprovedCreatorSkipApproval(AccountStatus status)
    {
        var creator = SeedCreator(status);
        creator.ResetTokenHash = TokenHasher.Hash("reset-token");
        creator.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(5);

        await NewAuthService().ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = creator.Email, ResetToken = "reset-token", NewPassword = "Moi@1234", ConfirmPassword = "Moi@1234"
        });

        Assert.Equal(AccountStatus.AwaitingApproval, creator.Status);
    }

    [Theory]
    [InlineData(UserRole.ContentCreator, AccountStatus.AwaitingApproval, CreatorApplicantAccess.ApplicantRole)]
    [InlineData(UserRole.ContentCreator, AccountStatus.Active, "ContentCreator")]
    [InlineData(UserRole.User, AccountStatus.Active, "User")]
    public void RoleClaim_IsTheApplicantRoleOnlyWhileACreatorAwaitsApproval(
        UserRole role, AccountStatus status, string expected)
    {
        Assert.Equal(expected, CreatorApplicantAccess.RoleClaimFor(new UserAccount { Role = role, Status = status }));
    }

    // ---------- Admin duyệt / từ chối ----------

    [Fact]
    public async Task Approve_ActivatesTheCreatorAndNotifies()
    {
        var creator = SeedCreator();
        var application = SeedApplication();

        var result = await _sut.ApproveAsync(AdminId, application.Id);

        Assert.Equal(("Approved", "Quản trị"), (result.Status, result.ReviewedByName));
        Assert.Equal((CreatorApplicationStatus.Approved, AdminId), (application.Status, application.ReviewedByUserId));
        Assert.Equal(AccountStatus.Active, creator.Status);
        Assert.Contains($"lock:{CreatorId}", _store.Calls);
        _audit.Verify(a => a.LogAsync(AdminId, "CreatorApplicationApproved", nameof(CreatorApplication), application.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(CreatorId, NotificationType.CreatorApplicationApproved,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendCreatorApplicationResultEmailAsync(
            "creator@example.test", "Tác giả", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_KeepsTheCreatorWaitingAndSendsTheReason()
    {
        var creator = SeedCreator();
        var application = SeedApplication();

        await _sut.RejectAsync(AdminId, application.Id, new RejectCreatorApplicationRequestDto { Reason = "  Thiếu tác phẩm mẫu  " });

        Assert.Equal((CreatorApplicationStatus.Rejected, "Thiếu tác phẩm mẫu"), (application.Status, application.RejectionReason));
        Assert.Equal(AccountStatus.AwaitingApproval, creator.Status);
        _notifications.Verify(n => n.CreateAsync(CreatorId, NotificationType.CreatorApplicationRejected,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendCreatorApplicationResultEmailAsync(
            "creator@example.test", "Tác giả", "Thiếu tác phẩm mẫu", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_WithoutReason_Is400()
    {
        SeedCreator();
        var application = SeedApplication();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RejectAsync(AdminId, application.Id, new RejectCreatorApplicationRequestDto { Reason = "   " }));

        Assert.Equal(CreatorApplicationStatus.Pending, application.Status);
    }

    [Theory]
    [InlineData(CreatorApplicationStatus.Approved)]
    [InlineData(CreatorApplicationStatus.Rejected)]
    public async Task Decide_AlreadyHandledApplication_Is409(CreatorApplicationStatus status)
    {
        SeedCreator();
        var application = SeedApplication(status);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ApproveAsync(AdminId, application.Id));
    }

    [Fact]
    public async Task Approve_BeforeTheCreatorVerifiedEmail_Is409()
    {
        var creator = SeedCreator(AccountStatus.Registered);
        var application = SeedApplication();

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.ApproveAsync(AdminId, application.Id));

        Assert.Contains("xác thực email", error.Message);
        Assert.Equal(AccountStatus.Registered, creator.Status);
        Assert.Equal(CreatorApplicationStatus.Pending, application.Status);
    }

    [Fact]
    public async Task Decide_ByNonAdministrator_Is403()
    {
        SeedCreator();
        var application = SeedApplication();
        _store.Seed(new UserAccount { Id = 3, Role = UserRole.Moderator, Status = AccountStatus.Active });

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ApproveAsync(3, application.Id));
    }

    [Fact]
    public async Task List_PendingOldestFirst()
    {
        SeedCreator();
        var newer = SeedApplication(createdAt: DateTime.UtcNow);
        var older = SeedApplication(createdAt: DateTime.UtcNow.AddDays(-2));
        SeedApplication(CreatorApplicationStatus.Rejected, DateTime.UtcNow.AddDays(-5));

        var page = await _sut.ListAsync(CreatorApplicationStatus.Pending, 1, 20);

        Assert.Equal([older.Id, newer.Id], page.Items.Select(item => item.Id));
        Assert.Equal(2, page.TotalCount);
    }

    // ---------- Creator xem và nộp lại đơn ----------

    [Fact]
    public async Task Resubmit_AfterRejection_CreatesANewPendingApplication()
    {
        SeedCreator();
        SeedApplication(CreatorApplicationStatus.Rejected, DateTime.UtcNow.AddDays(-1));

        var result = await _sut.ResubmitAsync(CreatorId, new ResubmitCreatorApplicationRequestDto
        {
            Introduction = "Đã bổ sung ba truyện mẫu về chủ đề tình bạn.", PortfolioUrl = "https://example.test/mau"
        });

        Assert.Equal("Pending", result.Status);
        Assert.Equal(2, _store.Items<CreatorApplication>().Count);
        var mine = await _sut.GetMineAsync(CreatorId);
        Assert.Equal((result.Id, "Pending", (string?)null), (mine.Id, mine.Status, mine.ReviewedByName));
    }

    [Fact]
    public async Task Resubmit_WhileAnApplicationIsPending_Is409()
    {
        SeedCreator();
        SeedApplication();

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ResubmitAsync(CreatorId,
            new ResubmitCreatorApplicationRequestDto { Introduction = "Giới thiệu đủ dài để hợp lệ nhé." }));

        Assert.Single(_store.Items<CreatorApplication>());
    }

    [Fact]
    public async Task Resubmit_ByApprovedCreator_Is403()
    {
        SeedCreator(AccountStatus.Active);
        SeedApplication(CreatorApplicationStatus.Approved);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ResubmitAsync(CreatorId,
            new ResubmitCreatorApplicationRequestDto { Introduction = "Giới thiệu đủ dài để hợp lệ nhé." }));
    }

    [Fact]
    public async Task GetMine_HidesWhichAdministratorReviewed()
    {
        SeedCreator();
        var application = SeedApplication();
        await _sut.RejectAsync(AdminId, application.Id, new RejectCreatorApplicationRequestDto { Reason = "Thiếu mẫu" });

        var mine = await _sut.GetMineAsync(CreatorId);

        Assert.Equal(("Rejected", "Thiếu mẫu", (string?)null), (mine.Status, mine.RejectionReason, mine.ReviewedByName));
    }
}
