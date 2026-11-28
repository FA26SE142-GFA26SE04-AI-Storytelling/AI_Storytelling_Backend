using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Services;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Administration;

/// <summary>Admin xem, vô hiệu hoá và kích hoạt lại Content Creator (Luồng 5, M5).</summary>
public class CreatorAccountServiceTests
{
    private const int AdminId = 1;
    private const int CreatorId = 2;
    private const int ModeratorId = 3;

    private readonly InMemoryUnitOfWork _store = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ITokenQuotaService> _quota = new();
    private readonly CreatorAccountService _sut;

    public CreatorAccountServiceTests()
    {
        _store.Seed(new UserAccount { Id = AdminId, Username = "admin", Role = UserRole.Administrator, Status = AccountStatus.Active });
        _store.Seed(new UserAccount { Id = ModeratorId, Username = "mod", Role = UserRole.Moderator, Status = AccountStatus.Active });
        _sut = new CreatorAccountService(_store, _audit.Object, _notifications.Object, _quota.Object);
    }

    private UserAccount SeedCreator(int id = CreatorId, AccountStatus status = AccountStatus.Active, string name = "creator_demo") =>
        _store.Seed(new UserAccount
        {
            Id = id, Username = name, FullName = "Lê Thị Mai", Email = $"{name}@example.test", Role = UserRole.ContentCreator,
            Status = status, TokenVersion = 1, CreatedAt = DateTime.UtcNow.AddDays(-id)
        });

    private ModerationItem SeedSubmission(ModerationItemStatus status, int? versionId = null) =>
        _store.Seed(new ModerationItem
        {
            ItemType = ModerationItemType.PublicStoryReview, CreatedByUserId = CreatorId, StoryId = 50,
            StoryVersionId = versionId, Status = status,
            ClaimedByUserId = status == ModerationItemStatus.Claimed ? ModeratorId : null
        });

    private static DeactivateCreatorRequestDto Reason(string reason = "Vi phạm quy định nội dung") => new() { Reason = reason };

    [Fact]
    public async Task List_OnlyCreators_FilteredBySearchAndStatus_WithPublishedCounts()
    {
        SeedCreator(CreatorId, AccountStatus.Active, "mai_writer");
        SeedCreator(4, AccountStatus.Suspended, "lan_writer");
        SeedCreator(5, AccountStatus.AwaitingApproval, "hoa_writer");
        _store.Seed(new Story { Id = 50, AuthorUserId = CreatorId, Scope = StoryScope.Creator, IsPublished = true });
        _store.Seed(new Story { Id = 51, AuthorUserId = CreatorId, Scope = StoryScope.Creator, IsPublished = false });

        var all = await _sut.ListAsync(null, null, 1, 20);
        var search = await _sut.ListAsync(null, "MAI_", 1, 20);
        var suspended = await _sut.ListAsync(AccountStatus.Suspended, null, 1, 20);

        Assert.Equal(3, all.TotalCount);
        Assert.DoesNotContain(all.Items, item => item.Id is AdminId or ModeratorId);
        Assert.Equal(("mai_writer", 1), (Assert.Single(search.Items).Username, search.Items[0].PublishedStoryCount));
        Assert.Equal("lan_writer", Assert.Single(suspended.Items).Username);
    }

    [Fact]
    public async Task List_And_Get_ExposeLatestApplicationStatus()
    {
        SeedCreator(CreatorId, AccountStatus.AwaitingApproval, "rejected_writer");
        SeedCreator(4, AccountStatus.AwaitingApproval, "resubmitted_writer");
        SeedCreator(5, AccountStatus.Active, "seeded_writer");
        var older = DateTime.UtcNow.AddDays(-2);
        _store.Seed(new CreatorApplication { ApplicantUserId = CreatorId, Status = CreatorApplicationStatus.Pending, CreatedAt = older });
        _store.Seed(new CreatorApplication { ApplicantUserId = CreatorId, Status = CreatorApplicationStatus.Rejected, CreatedAt = DateTime.UtcNow.AddDays(-1) });
        _store.Seed(new CreatorApplication { ApplicantUserId = 4, Status = CreatorApplicationStatus.Rejected, CreatedAt = older });
        _store.Seed(new CreatorApplication { ApplicantUserId = 4, Status = CreatorApplicationStatus.Pending, CreatedAt = DateTime.UtcNow });

        var list = await _sut.ListAsync(null, null, 1, 20);
        var single = await _sut.GetAsync(CreatorId);

        var byName = list.Items.ToDictionary(item => item.Username, item => item.LatestApplicationStatus);
        Assert.Equal("Rejected", byName["rejected_writer"]);
        Assert.Equal("Pending", byName["resubmitted_writer"]);
        // Creator tạo trước khi có đơn đăng ký (seed, Admin cấp) không có đơn nào.
        Assert.Null(byName["seeded_writer"]);
        Assert.Equal("Rejected", single.LatestApplicationStatus);
    }

    [Fact]
    public async Task Get_NonCreator_Is404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(ModeratorId));
    }

    [Fact]
    public async Task Deactivate_LocksTheAccountAndClosesOpenSubmissions()
    {
        var creator = SeedCreator();
        var session = _store.Seed(new RefreshToken { UserAccountId = CreatorId, TokenHash = "h", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        var version = _store.Seed(new StoryVersion { Id = 70, StoryId = 50 });
        version.Moderation.ReviewStatus = VersionReviewStatus.Pending;
        version.Moderation.ReviewSubmittedAt = DateTime.UtcNow;
        var pending = SeedSubmission(ModerationItemStatus.Pending, versionId: 70);
        var claimed = SeedSubmission(ModerationItemStatus.Claimed);
        var approved = SeedSubmission(ModerationItemStatus.Approved);
        _store.Seed(new Story { Id = 50, AuthorUserId = CreatorId, Scope = StoryScope.Creator, IsPublished = true });

        var result = await _sut.DeactivateAsync(AdminId, CreatorId, Reason());

        Assert.Equal((AccountStatus.Suspended, 2), (creator.Status, creator.TokenVersion));
        Assert.NotNull(session.RevokedAt);
        Assert.Equal((ModerationItemStatus.Withdrawn, ModerationItemStatus.Closed, ModerationItemStatus.Approved),
            (pending.Status, claimed.Status, approved.Status));
        Assert.Equal(CreatorAccountService.ClosedReason, claimed.ClosedReason);
        Assert.Equal(VersionReviewStatus.None, version.Moderation.ReviewStatus);
        Assert.Equal((1, 1, "Suspended", 1),
            (result.WithdrawnSubmissions, result.ClosedSubmissions, result.Creator.Status, result.Creator.PublishedStoryCount));
        // Chỉ yêu cầu đang chờ được hoàn lượt gửi duyệt; mục đã có Moderator nhận thì không.
        _quota.Verify(q => q.RefundSubmissionAsync(pending.Id, It.IsAny<CancellationToken>()), Times.Once);
        _quota.Verify(q => q.RefundSubmissionAsync(claimed.Id, It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.CreateAsync(ModeratorId, NotificationType.ContentModerationResult,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(CreatorId, NotificationType.AdminRoleChanged,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(AdminId, "ContentCreatorDeactivated", nameof(UserAccount), CreatorId,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deactivate_WithoutReason_Is400()
    {
        var creator = SeedCreator();

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.DeactivateAsync(AdminId, CreatorId, Reason("  ")));

        Assert.Equal(AccountStatus.Active, creator.Status);
    }

    [Theory]
    [InlineData(AccountStatus.Suspended)]
    [InlineData(AccountStatus.AwaitingApproval)]
    [InlineData(AccountStatus.Registered)]
    public async Task Deactivate_NotActive_Is409(AccountStatus status)
    {
        SeedCreator(status: status);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.DeactivateAsync(AdminId, CreatorId, Reason()));
    }

    [Fact]
    public async Task Deactivate_ByNonAdministrator_Is403()
    {
        var creator = SeedCreator();

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.DeactivateAsync(ModeratorId, CreatorId, Reason()));

        Assert.Equal(AccountStatus.Active, creator.Status);
    }

    [Fact]
    public async Task Reactivate_SuspendedCreator_BecomesActive()
    {
        var creator = SeedCreator(status: AccountStatus.Suspended);
        creator.FailedLoginAttempts = 5;
        creator.LockedUntil = DateTime.UtcNow.AddMinutes(10);

        var result = await _sut.ReactivateAsync(AdminId, CreatorId);

        Assert.Equal(("Active", AccountStatus.Active, 0, (DateTime?)null),
            (result.Status, creator.Status, creator.FailedLoginAttempts, creator.LockedUntil));
        _audit.Verify(a => a.LogAsync(AdminId, "ContentCreatorReactivated", nameof(UserAccount), CreatorId,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(AccountStatus.Active)]
    [InlineData(AccountStatus.AwaitingApproval)]
    public async Task Reactivate_NotSuspended_Is409(AccountStatus status)
    {
        SeedCreator(status: status);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ReactivateAsync(AdminId, CreatorId));
    }
}
