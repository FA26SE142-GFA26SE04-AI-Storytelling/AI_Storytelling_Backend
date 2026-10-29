using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Api.Controllers;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class ModerationQueueServiceTests
{
    private readonly Mock<IGenericRepository<ModerationItem>> _itemRepoMock = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriterMock = new();
    private readonly ModerationQueueService _sut;

    private readonly UserAccount _admin = new()
    {
        Id = 1, Username = "admin_demo", Email = "admin@example.com", FullName = "Admin Demo",
        Role = UserRole.Administrator, Status = AccountStatus.Active
    };

    private readonly UserAccount _moderatorA = new()
    {
        Id = 11, Username = "moderator_demo1", Email = "moderator1@example.com", FullName = "Nguyen Thi Lan",
        Role = UserRole.Moderator, Status = AccountStatus.Active
    };

    private readonly UserAccount _moderatorB = new()
    {
        Id = 12, Username = "moderator_demo2", Email = "moderator2@example.com", FullName = "Tran Van Minh",
        Role = UserRole.Moderator, Status = AccountStatus.Active
    };

    private readonly UserAccount _parent = new()
    {
        Id = 2, Username = "parent_demo1", Email = "parent1@example.com", FullName = "Tran Thi Hoa",
        Role = UserRole.User, Status = AccountStatus.Active
    };

    public ModerationQueueServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.Repository<ModerationItem>()).Returns(_itemRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>()).Returns(_userRepoMock.Object);
        foreach (var user in new[] { _admin, _moderatorA, _moderatorB, _parent })
        {
            var captured = user;
            _userRepoMock.Setup(r => r.GetByIdAsync(captured.Id, It.IsAny<CancellationToken>())).ReturnsAsync(captured);
        }

        _itemRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ModerationItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModerationItem item, CancellationToken _) => item);
        _sut = new ModerationQueueService(_unitOfWorkMock.Object, _auditLogWriterMock.Object);
    }

    [Theory]
    [InlineData(ModerationItemType.GrayZoneStory, false, ModerationPriority.High, 24)]
    [InlineData(ModerationItemType.ContentReport, true, ModerationPriority.High, 48)]
    [InlineData(ModerationItemType.ContentReport, false, ModerationPriority.Medium, 48)]
    [InlineData(ModerationItemType.PublicStoryReview, false, ModerationPriority.Medium, 72)]
    [InlineData(ModerationItemType.RoadmapTemplateReview, false, ModerationPriority.Low, 72)]
    [InlineData(ModerationItemType.CommunitySubmission, false, ModerationPriority.Low, 72)]
    public void Policy_ReturnsPriorityAndSlaPerItemType(
        ModerationItemType type, bool safetyConcern, ModerationPriority priority, int slaHours)
    {
        Assert.Equal(priority, ModerationQueuePolicy.GetPriority(type, safetyConcern));
        Assert.Equal(TimeSpan.FromHours(slaHours), ModerationQueuePolicy.GetSla(type));
    }

    [Fact]
    public void Policy_ClaimAndReviewViewTimeoutsAreTwoHours()
    {
        Assert.Equal(TimeSpan.FromHours(2), ModerationQueuePolicy.ClaimTimeout);
        Assert.Equal(TimeSpan.FromHours(2), ModerationQueuePolicy.ReviewViewLifetime);
    }

    [Fact]
    public async Task EnqueueAsync_GrayZoneStory_CreatesPendingHighPriorityItemDueIn24Hours()
    {
        var before = DateTime.UtcNow;

        var item = await _sut.EnqueueAsync(new EnqueueModerationItemCommand
        {
            ItemType = ModerationItemType.GrayZoneStory, StoryId = 4, StoryVersionId = 4, CreatedByUserId = 8
        });

        Assert.Equal(ModerationItemStatus.Pending, item.Status);
        Assert.Equal(ModerationPriority.High, item.Priority);
        Assert.Equal(4, item.StoryId);
        Assert.Equal(8, item.CreatedByUserId);
        Assert.InRange(item.SlaDueAt!.Value, before.AddHours(24), DateTime.UtcNow.AddHours(24));
        _itemRepoMock.Verify(r => r.AddAsync(item, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_SafetyConcernReport_IsHighPriority()
    {
        var item = await _sut.EnqueueAsync(new EnqueueModerationItemCommand
        {
            ItemType = ModerationItemType.ContentReport, ContentReportId = 3, StoryId = 2, IsSafetyConcern = true
        });

        Assert.Equal(ModerationPriority.High, item.Priority);
    }

    [Fact]
    public async Task EnqueueAsync_Resubmission_KeepsLinkToPreviousItem()
    {
        var item = await _sut.EnqueueAsync(new EnqueueModerationItemCommand
        {
            ItemType = ModerationItemType.PublicStoryReview, StoryId = 3, StoryVersionId = 9,
            IsResubmission = true, PreviousItemId = 5
        });

        Assert.True(item.IsResubmission);
        Assert.Equal(5, item.PreviousItemId);
    }

    [Fact]
    public async Task ClaimAsync_ConcurrentClaimConflict_DoesNotWriteSuccessAudit()
    {
        ReturnItem(PendingItem());
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Conflict"));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ClaimAsync(11, 3));

        _auditLogWriterMock.Verify(a => a.LogAsync(
            It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuspendedModerator_CannotListClaimOrOpen()
    {
        _moderatorA.Status = AccountStatus.Suspended;
        ReturnItem(PendingItem());

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(11, new ModerationQueueQueryDto()));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ClaimAsync(11, 3));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.OpenAsync(11, 3));
    }

    [Fact]
    public async Task ClaimAsync_AdministratorCannotClaimOwnItemEvenWithoutModerators()
    {
        ReturnItem(PendingItem(createdBy: _admin.Id));
        SetEligibleModeratorCount(0);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ClaimAsync(_admin.Id, 3));
    }

    [Fact]
    public async Task ClaimAsync_AdminFallback_ExcludesCreatorAndInactiveModerators()
    {
        var item = PendingItem(createdBy: _moderatorA.Id);
        ReturnItem(item);
        _moderatorB.Status = AccountStatus.Suspended;
        var users = new[] { _admin, _moderatorA, _moderatorB, _parent };
        _userRepoMock.Setup(r => r.CountAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserAccount, bool>> predicate, CancellationToken _) =>
                users.Count(predicate.Compile()));

        var result = await _sut.ClaimAsync(_admin.Id, item.Id);

        Assert.Equal(_admin.Id, result.ClaimedByUserId);
    }

    [Fact]
    public async Task ListAsync_ClampsPaginationAndMapsSummaryWithoutDetailFields()
    {
        var item = PendingItem();
        item.Status = ModerationItemStatus.Claimed;
        item.ClaimedAt = DateTime.UtcNow.AddMinutes(-10);
        item.Flags = "private review details";
        _itemRepoMock.Setup(r => r.GetPagedAsync(
                1, 100, It.IsAny<Expression<Func<ModerationItem, bool>>>(),
                It.IsAny<Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>>(),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ModerationItem>)new[] { item }, 120));

        var result = await _sut.ListAsync(11, new ModerationQueueQueryDto
        {
            PageIndex = -1, PageSize = 1000, Status = ModerationItemStatus.Claimed
        });

        Assert.Equal(1, result.PageIndex);
        Assert.Equal(100, result.PageSize);
        Assert.Equal(120, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        var dto = Assert.Single(result.Items);
        Assert.IsType<ModerationItemSummaryDto>(dto);
        Assert.Equal(item.ClaimedAt.Value.AddHours(2), dto.ClaimExpiresAt);
    }

    [Fact]
    public async Task ListAsync_AdministratorCanSeeOwnItemsAndQueryTranslatesToPostgresCaseOrdering()
    {
        Expression<Func<ModerationItem, bool>>? filter = null;
        Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>? order = null;
        _itemRepoMock.Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<ModerationItem, bool>>>(),
                It.IsAny<Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>>(),
                null, It.IsAny<CancellationToken>()))
            .Callback<int, int, Expression<Func<ModerationItem, bool>>?,
                Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?, string?, CancellationToken>(
                (_, _, f, o, _, _) => { filter = f; order = o; })
            .ReturnsAsync(((IReadOnlyList<ModerationItem>)Array.Empty<ModerationItem>(), 0));

        await _sut.ListAsync(_admin.Id, new ModerationQueueQueryDto());

        Assert.True(filter!.Compile()(PendingItem(createdBy: _admin.Id)));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options;
        await using var context = new ApplicationDbContext(options);
        // SQL generation only: no connection or database changes.
        var sql = order!(context.Set<ModerationItem>().Where(filter)).ToQueryString();
        Assert.Contains("ORDER BY CASE", sql);
        Assert.Contains("'High'", sql);
        Assert.Contains("'Medium'", sql);
    }

    private static ModerationItem PendingItem(int id = 3, int? createdBy = 8) => new()
    {
        Id = id, ItemType = ModerationItemType.ContentReport, Status = ModerationItemStatus.Pending,
        Priority = ModerationPriority.Medium, StoryId = 2, CreatedByUserId = createdBy,
        SlaDueAt = DateTime.UtcNow.AddHours(20), RowVersion = 0
    };

    private void ReturnItem(ModerationItem item) =>
        _itemRepoMock.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

    private void SetEligibleModeratorCount(int count) =>
        _userRepoMock
            .Setup(r => r.CountAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);

    [Fact]
    public async Task ClaimAsync_PendingItem_MarksClaimedAndBumpsRowVersion()
    {
        var item = PendingItem();
        ReturnItem(item);

        var result = await _sut.ClaimAsync(_moderatorA.Id, item.Id);

        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
        Assert.Equal(_moderatorA.Id, item.ClaimedByUserId);
        Assert.NotNull(item.ClaimedAt);
        Assert.Equal(1, item.RowVersion);
        Assert.Equal("Claimed", result.Status);
        Assert.Equal(_moderatorA.Id, result.ClaimedByUserId);
        _itemRepoMock.Verify(r => r.Update(item), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _moderatorA.Id, "ModerationItemClaimed", nameof(ModerationItem), item.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClaimAsync_ItemCreatedByActor_ThrowsForbiddenAndDoesNotChangeItem()
    {
        var item = PendingItem(createdBy: _moderatorA.Id);
        ReturnItem(item);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ClaimAsync(_moderatorA.Id, item.Id));

        Assert.Equal(ModerationItemStatus.Pending, item.Status);
        Assert.Null(item.ClaimedByUserId);
    }

    [Fact]
    public async Task ClaimAsync_AlreadyClaimed_ThrowsConflict()
    {
        var item = PendingItem();
        item.Status = ModerationItemStatus.Claimed;
        item.ClaimedByUserId = _moderatorB.Id;
        ReturnItem(item);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ClaimAsync(_moderatorA.Id, item.Id));
    }

    [Fact]
    public async Task ClaimAsync_UnknownItem_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ClaimAsync(_moderatorA.Id, 999));
    }

    [Fact]
    public async Task ClaimAsync_ActorIsNotStaff_ThrowsForbidden()
    {
        var item = PendingItem();
        ReturnItem(item);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ClaimAsync(_parent.Id, item.Id));
    }

    [Fact]
    public async Task ClaimAsync_AdministratorWhileAnotherModeratorIsAvailable_ThrowsForbidden()
    {
        var item = PendingItem();
        ReturnItem(item);
        SetEligibleModeratorCount(1);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ClaimAsync(_admin.Id, item.Id));

        Assert.Equal(ModerationItemStatus.Pending, item.Status);
    }

    [Fact]
    public async Task ClaimAsync_AdministratorWhenNoEligibleModerator_Succeeds()
    {
        var item = PendingItem(createdBy: _moderatorA.Id);
        ReturnItem(item);
        SetEligibleModeratorCount(0);

        var result = await _sut.ClaimAsync(_admin.Id, item.Id);

        Assert.Equal(_admin.Id, result.ClaimedByUserId);
        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
    }

    [Fact]
    public async Task OpenAsync_ClaimedByActor_ReturnsDetailAndWritesAudit()
    {
        var item = PendingItem();
        item.Status = ModerationItemStatus.Claimed;
        item.ClaimedByUserId = _moderatorA.Id;
        item.ClaimedAt = DateTime.UtcNow.AddMinutes(-30);
        item.Flags = "{\"sections\":[\"development\"]}";
        ReturnItem(item);

        var result = await _sut.OpenAsync(_moderatorA.Id, item.Id);

        Assert.Equal(item.Id, result.Id);
        Assert.Equal("{\"sections\":[\"development\"]}", result.Flags);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _moderatorA.Id, "ModerationItemOpened", nameof(ModerationItem), item.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenAsync_ClaimedBySomeoneElse_ThrowsForbiddenAndWritesNoAudit()
    {
        var item = PendingItem();
        item.Status = ModerationItemStatus.Claimed;
        item.ClaimedByUserId = _moderatorB.Id;
        item.ClaimedAt = DateTime.UtcNow.AddMinutes(-30);
        ReturnItem(item);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.OpenAsync(_moderatorA.Id, item.Id));

        _auditLogWriterMock.Verify(a => a.LogAsync(
            It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenAsync_PendingItem_ThrowsForbidden()
    {
        var item = PendingItem();
        ReturnItem(item);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.OpenAsync(_moderatorA.Id, item.Id));
    }

    [Fact]
    public async Task OpenAsync_ClaimOlderThanTwoHours_ThrowsConflict()
    {
        var item = PendingItem();
        item.Status = ModerationItemStatus.Claimed;
        item.ClaimedByUserId = _moderatorA.Id;
        item.ClaimedAt = DateTime.UtcNow.AddHours(-3);
        ReturnItem(item);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.OpenAsync(_moderatorA.Id, item.Id));
    }

    [Fact]
    public async Task ListAsync_Moderator_HidesOwnItemsAndSortsHighPriorityThenSoonestSla()
    {
        var own = PendingItem(id: 1, createdBy: _moderatorA.Id);
        var lowSoon = PendingItem(id: 2); lowSoon.Priority = ModerationPriority.Low; lowSoon.SlaDueAt = DateTime.UtcNow.AddHours(1);
        var mediumLate = PendingItem(id: 3); mediumLate.Priority = ModerationPriority.Medium; mediumLate.SlaDueAt = DateTime.UtcNow.AddHours(30);
        var mediumSoon = PendingItem(id: 4); mediumSoon.Priority = ModerationPriority.Medium; mediumSoon.SlaDueAt = DateTime.UtcNow.AddHours(5);
        var high = PendingItem(id: 5); high.Priority = ModerationPriority.High; high.SlaDueAt = DateTime.UtcNow.AddHours(40);
        var claimed = PendingItem(id: 6); claimed.Status = ModerationItemStatus.Claimed;
        var all = new List<ModerationItem> { own, lowSoon, mediumLate, mediumSoon, high, claimed };

        Expression<Func<ModerationItem, bool>>? capturedFilter = null;
        Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>? capturedOrder = null;
        _itemRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<ModerationItem, bool>>?>(),
                It.IsAny<Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, Expression<Func<ModerationItem, bool>>?,
                Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?, string?, CancellationToken>(
                (_, _, filter, order, _, _) =>
                {
                    capturedFilter = filter;
                    capturedOrder = order;
                })
            .ReturnsAsync((Items: (IReadOnlyList<ModerationItem>)new List<ModerationItem>(), TotalCount: 0));

        await _sut.ListAsync(_moderatorA.Id, new ModerationQueueQueryDto());

        Assert.NotNull(capturedFilter);
        Assert.NotNull(capturedOrder);
        var visible = all.AsQueryable().Where(capturedFilter!);
        var ordered = capturedOrder!(visible).Select(item => item.Id).ToList();
        Assert.Equal(new[] { 5, 4, 3, 2 }, ordered);
    }

    [Fact]
    public async Task ListAsync_FiltersByItemTypeAndOnlyOverdue()
    {
        var overdueReport = PendingItem(id: 1); overdueReport.SlaDueAt = DateTime.UtcNow.AddHours(-1);
        var freshReport = PendingItem(id: 2); freshReport.SlaDueAt = DateTime.UtcNow.AddHours(10);
        var overdueCommunity = PendingItem(id: 3); overdueCommunity.ItemType = ModerationItemType.CommunitySubmission;
        overdueCommunity.SlaDueAt = DateTime.UtcNow.AddHours(-1);
        var all = new List<ModerationItem> { overdueReport, freshReport, overdueCommunity };

        Expression<Func<ModerationItem, bool>>? capturedFilter = null;
        _itemRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<ModerationItem, bool>>?>(),
                It.IsAny<Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, Expression<Func<ModerationItem, bool>>?,
                Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?, string?, CancellationToken>(
                (_, _, filter, _, _, _) => capturedFilter = filter)
            .ReturnsAsync((Items: (IReadOnlyList<ModerationItem>)new List<ModerationItem>(), TotalCount: 0));

        await _sut.ListAsync(_moderatorA.Id, new ModerationQueueQueryDto
        {
            ItemType = ModerationItemType.ContentReport, OnlyOverdue = true
        });

        var ids = all.AsQueryable().Where(capturedFilter!).Select(item => item.Id).ToList();
        Assert.Equal(new[] { 1 }, ids);
    }

    [Fact]
    public async Task ListAsync_ActorIsNotStaff_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(_parent.Id, new ModerationQueueQueryDto()));
    }

    [Fact]
    public async Task ListAsync_RecentResubmission_IsBoostedOnlyForPreviousModeratorFor24Hours()
    {
        Assert.Equal(TimeSpan.FromHours(24), ModerationQueuePolicy.ResubmissionPriorityWindow);
        Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>? capturedOrder = null;
        _itemRepoMock.Setup(repository => repository.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<ModerationItem, bool>>?>(),
                It.IsAny<Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, Expression<Func<ModerationItem, bool>>?,
                Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?, string?, CancellationToken>(
                (_, _, _, order, _, _) => capturedOrder = order)
            .ReturnsAsync((Items: (IReadOnlyList<ModerationItem>)Array.Empty<ModerationItem>(), TotalCount: 0));

        var previous = new ModerationItem { Id = 90, DecidedByUserId = _moderatorA.Id };
        var boosted = PendingItem(1);
        boosted.Priority = ModerationPriority.Low;
        boosted.IsResubmission = true;
        boosted.PreviousItemId = previous.Id;
        boosted.PreviousItem = previous;
        boosted.CreatedAt = DateTime.UtcNow.AddHours(-1);
        var high = PendingItem(2);
        high.Priority = ModerationPriority.High;
        var items = new[] { high, boosted }.AsQueryable();

        await _sut.ListAsync(_moderatorA.Id, new ModerationQueueQueryDto());
        Assert.Equal(new[] { 1, 2 }, capturedOrder!(items).Select(item => item.Id));

        await _sut.ListAsync(_moderatorB.Id, new ModerationQueueQueryDto());
        Assert.Equal(new[] { 2, 1 }, capturedOrder!(items).Select(item => item.Id));

        boosted.CreatedAt = DateTime.UtcNow.AddHours(-25);
        await _sut.ListAsync(_moderatorA.Id, new ModerationQueueQueryDto());
        Assert.Equal(new[] { 2, 1 }, capturedOrder!(items).Select(item => item.Id));
    }

    [Fact]
    public void ModerationQueueController_IsRestrictedToModeratorAndAdministrator()
    {
        var authorize = typeof(ModerationQueueController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Moderator,Administrator", authorize!.Roles);
    }
}
