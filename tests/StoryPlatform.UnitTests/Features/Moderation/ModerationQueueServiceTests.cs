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
using StoryPlatform.Application.Features.Moderation.Interfaces;
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
    private const string QueueIncludes =
        $"{nameof(ModerationItem.Story)},{nameof(ModerationItem.StoryVersion)},{nameof(ModerationItem.CreatedByUser)},{nameof(ModerationItem.ClaimedByUser)},{nameof(ModerationItem.ContentReport)}";

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
        item.ItemType = ModerationItemType.RoadmapTemplateReview;
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
                QueueIncludes, It.IsAny<CancellationToken>()))
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
                QueueIncludes, It.IsAny<CancellationToken>()))
            .Callback<int, int, Expression<Func<ModerationItem, bool>>?,
                Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?, string?, CancellationToken>(
                (_, _, f, o, _, _) => { filter = f; order = o; })
            .ReturnsAsync(((IReadOnlyList<ModerationItem>)Array.Empty<ModerationItem>(), 0));

        await _sut.ListAsync(_admin.Id, new ModerationQueueQueryDto());

        var own = PendingItem(createdBy: _admin.Id);
        own.ItemType = ModerationItemType.GrayZoneStory;
        Assert.True(filter!.Compile()(own));
        // Loại mục Administrator không được nhận thì không hiện trong danh sách.
        Assert.False(filter.Compile()(PendingItem()));
        own.ItemType = ModerationItemType.StoryRating;
        Assert.False(filter.Compile()(own));
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
    public async Task ClaimAsync_AdministratorClaimsEscalatedItem_WhileModeratorIsAvailable()
    {
        var item = PendingItem();
        item.ItemType = ModerationItemType.GrayZoneStory;
        item.EscalatedAt = DateTime.UtcNow.AddMinutes(-5);
        ReturnItem(item);
        SetEligibleModeratorCount(1);

        var result = await _sut.ClaimAsync(_admin.Id, item.Id);

        Assert.Equal(_admin.Id, result.ClaimedByUserId);
    }

    [Fact]
    public void ClaimPermission_MapsEachItemTypeToItsReviewPermission()
    {
        Assert.Equal("moderation.report.review", ModerationQueuePolicy.ClaimPermission(ModerationItemType.ContentReport));
        Assert.Equal("moderation.rating.moderate", ModerationQueuePolicy.ClaimPermission(ModerationItemType.StoryRating));
        Assert.Equal("review.gray_zone", ModerationQueuePolicy.ClaimPermission(ModerationItemType.GrayZoneStory));
        Assert.Equal("review.public_story", ModerationQueuePolicy.ClaimPermission(ModerationItemType.PublicStoryReview));
        Assert.Equal("review.roadmap", ModerationQueuePolicy.ClaimPermission(ModerationItemType.RoadmapTemplateReview));
        Assert.Equal("review.creator_publication", ModerationQueuePolicy.ClaimPermission(ModerationItemType.PublicStoryRemoval));
        Assert.Equal("moderation.queue.process", ModerationQueuePolicy.ClaimPermission(ModerationItemType.CommunitySubmission));
    }

    [Fact]
    public async Task ClaimAsync_AdministratorWhenNoEligibleModerator_Succeeds()
    {
        var item = PendingItem(createdBy: _moderatorA.Id);
        item.ItemType = ModerationItemType.RoadmapTemplateReview;
        ReturnItem(item);
        SetEligibleModeratorCount(0);

        var result = await _sut.ClaimAsync(_admin.Id, item.Id);

        Assert.Equal(_admin.Id, result.ClaimedByUserId);
        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
    }

    [Fact]
    public async Task ClaimAsync_AdministratorCannotClaimContentReport_EvenWithoutModerators()
    {
        var item = PendingItem(createdBy: _moderatorA.Id);
        ReturnItem(item);
        SetEligibleModeratorCount(0);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ClaimAsync(_admin.Id, item.Id));

        Assert.Equal(ModerationItemStatus.Pending, item.Status);
        Assert.Null(item.ClaimedByUserId);
        _itemRepoMock.Verify(r => r.Update(It.IsAny<ModerationItem>()), Times.Never);
    }

    [Fact]
    public async Task ClaimAsync_AdministratorCannotClaimStoryRating_EvenWithoutModerators()
    {
        var item = PendingItem(createdBy: _parent.Id);
        item.ItemType = ModerationItemType.StoryRating;
        ReturnItem(item);
        SetEligibleModeratorCount(0);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ClaimAsync(_admin.Id, item.Id));

        Assert.Equal(ModerationItemStatus.Pending, item.Status);
        Assert.Null(item.ClaimedByUserId);
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
    public async Task ListAsync_SecondPage_IncludesSubmittedStoryIdentityAndPagination()
    {
        var item = PendingItem(18, createdBy: 13);
        item.ItemType = ModerationItemType.PublicStoryReview;
        item.StoryId = 48;
        item.StoryVersionId = 97;
        item.Story = new Story { Id = 48, Title = "Ngọn Đèn Dũng Cảm Của Titi" };
        item.ClaimedByUserId = 21;
        item.ClaimedByUser = new UserAccount { Id = 21, Username = "mod21", FullName = "Nguyễn Văn Kiểm" };
        _itemRepoMock.Setup(r => r.GetPagedAsync(2, 6,
                It.IsAny<Expression<Func<ModerationItem, bool>>?>(),
                It.IsAny<Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?>(),
                QueueIncludes, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Items: (IReadOnlyList<ModerationItem>)new[] { item }, TotalCount: 7));

        var result = await _sut.ListAsync(_moderatorB.Id, new ModerationQueueQueryDto { PageIndex = 2, PageSize = 6 });

        Assert.Equal(7, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        var summary = Assert.Single(result.Items);
        Assert.Equal(18, summary.Id);
        Assert.Equal(48, summary.StoryId);
        Assert.Equal(97, summary.StoryVersionId);
        Assert.Equal("Ngọn Đèn Dũng Cảm Của Titi", summary.StoryTitle);
        Assert.Equal(21, summary.ClaimedByUserId);
        Assert.Equal("Nguyễn Văn Kiểm", summary.ClaimedByName);
        Assert.Equal("Pending", summary.Status);
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
        Assert.Equal("Moderator,Administrator", authorize!.GrantedRoles());
    }

    // ---- storyImpact ----

    private ModerationQueueService WithImpact(Mock<IStoryUsageImpactReader> reader) =>
        new(_unitOfWorkMock.Object, _auditLogWriterMock.Object, reader.Object);

    [Theory]
    [InlineData(ModerationItemType.PublicStoryRemoval, false)]
    [InlineData(ModerationItemType.PublicStoryReview, true)]
    public async Task ClaimAsync_StoryItem_ReturnsStoryImpact(ModerationItemType type, bool checkRepublish)
    {
        var item = PendingItem(); item.ItemType = type;
        ReturnItem(item);
        var impact = new StoryImpactDto { PendingWishCount = 2 };
        var reader = new Mock<IStoryUsageImpactReader>();
        reader.Setup(r => r.ReadAsync(2, checkRepublish, It.IsAny<CancellationToken>())).ReturnsAsync(impact);

        var result = await WithImpact(reader).ClaimAsync(_moderatorA.Id, item.Id);

        Assert.Same(impact, result.StoryImpact);
    }

    [Fact]
    public async Task OpenAsync_RemovalItem_ReturnsStoryImpact()
    {
        var item = PendingItem(); item.ItemType = ModerationItemType.PublicStoryRemoval;
        item.Status = ModerationItemStatus.Claimed; item.ClaimedByUserId = _moderatorA.Id; item.ClaimedAt = DateTime.UtcNow;
        ReturnItem(item);
        var impact = new StoryImpactDto { ActiveRoadmapItemCount = 1 };
        var reader = new Mock<IStoryUsageImpactReader>();
        reader.Setup(r => r.ReadAsync(2, false, It.IsAny<CancellationToken>())).ReturnsAsync(impact);

        var result = await WithImpact(reader).OpenAsync(_moderatorA.Id, item.Id);

        Assert.Same(impact, result.StoryImpact);
    }

    [Fact]
    public async Task ClaimAsync_OtherItemTypes_HaveNoStoryImpact_AndDoNotQueryTheReader()
    {
        var item = PendingItem();   // ContentReport
        ReturnItem(item);
        var reader = new Mock<IStoryUsageImpactReader>();

        var result = await WithImpact(reader).ClaimAsync(_moderatorA.Id, item.Id);

        Assert.Null(result.StoryImpact);
        reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClaimAsync_WithoutReader_StillWorks_WithNullImpact()
    {
        var item = PendingItem(); item.ItemType = ModerationItemType.PublicStoryRemoval;
        ReturnItem(item);

        var result = await _sut.ClaimAsync(_moderatorA.Id, item.Id);

        Assert.Null(result.StoryImpact);
    }

    // ---- người gửi (createdBy) ----

    private readonly UserAccount _creator = new()
    {
        Id = 20, Username = "creator_demo", Email = "creator@example.com", FullName = "Le Van Tac Gia",
        Role = UserRole.ContentCreator, Status = AccountStatus.Active
    };

    private void ReturnReport(ContentReport report)
    {
        var reportRepo = new Mock<IGenericRepository<ContentReport>>();
        reportRepo.Setup(r => r.GetByIdAsync(report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(report);
        _unitOfWorkMock.Setup(u => u.Repository<ContentReport>()).Returns(reportRepo.Object);
    }

    [Fact]
    public async Task ListAsync_ShowsSubmitterForCreatorRemovalAndModeratorReviewRequest_ButNotParentReport()
    {
        var removal = PendingItem(30, createdBy: _creator.Id);
        removal.ItemType = ModerationItemType.PublicStoryRemoval;
        removal.CreatedByUser = _creator;
        var reviewRequest = PendingItem(31, createdBy: _moderatorA.Id);
        reviewRequest.ContentReportId = 5;
        reviewRequest.ContentReport = new ContentReport { Id = 5, IsReviewItem = true };
        reviewRequest.CreatedByUser = _moderatorA;
        var parentReport = PendingItem(32, createdBy: _parent.Id);
        parentReport.ContentReportId = 6;
        parentReport.ContentReport = new ContentReport { Id = 6, IsReviewItem = false };
        parentReport.CreatedByUser = _parent;
        _itemRepoMock.Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<ModerationItem, bool>>?>(),
                It.IsAny<Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?>(),
                QueueIncludes, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ModerationItem>)new[] { removal, reviewRequest, parentReport }, 3));

        var result = await _sut.ListAsync(_moderatorB.Id, new ModerationQueueQueryDto());

        var removalDto = result.Items.Single(x => x.Id == 30);
        Assert.Equal(_creator.Id, removalDto.CreatedByUserId);
        Assert.Equal("Le Van Tac Gia", removalDto.CreatedByName);
        var reviewDto = result.Items.Single(x => x.Id == 31);
        Assert.Equal(_moderatorA.Id, reviewDto.CreatedByUserId);
        Assert.Equal("Nguyen Thi Lan", reviewDto.CreatedByName);
        var parentDto = result.Items.Single(x => x.Id == 32);
        Assert.Null(parentDto.CreatedByUserId);
        Assert.Null(parentDto.CreatedByName);
    }

    [Fact]
    public async Task ClaimAsync_ModeratorReviewRequest_ReturnsRequestingModerator()
    {
        var item = PendingItem(createdBy: _moderatorB.Id);
        item.ContentReportId = 5;
        ReturnItem(item);
        ReturnReport(new ContentReport { Id = 5, IsReviewItem = true });

        var result = await _sut.ClaimAsync(_moderatorA.Id, item.Id);

        Assert.Equal(_moderatorB.Id, result.CreatedByUserId);
        Assert.Equal("Tran Van Minh", result.CreatedByName);
    }

    [Fact]
    public async Task ClaimAsync_ParentReport_KeepsReporterHidden()
    {
        var item = PendingItem(createdBy: _parent.Id);
        item.ContentReportId = 6;
        ReturnItem(item);
        ReturnReport(new ContentReport { Id = 6, IsReviewItem = false });

        var result = await _sut.ClaimAsync(_moderatorA.Id, item.Id);

        Assert.Null(result.CreatedByUserId);
        Assert.Null(result.CreatedByName);
    }

    [Fact]
    public async Task OpenAsync_CreatorRemovalRequest_ReturnsRequestingCreator()
    {
        var item = PendingItem(createdBy: _creator.Id);
        item.ItemType = ModerationItemType.PublicStoryRemoval;
        item.Status = ModerationItemStatus.Claimed; item.ClaimedByUserId = _moderatorA.Id; item.ClaimedAt = DateTime.UtcNow;
        ReturnItem(item);
        _userRepoMock.Setup(r => r.GetByIdAsync(_creator.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_creator);

        var result = await _sut.OpenAsync(_moderatorA.Id, item.Id);

        Assert.Equal(_creator.Id, result.CreatedByUserId);
        Assert.Equal("Le Van Tac Gia", result.CreatedByName);
    }

    private (ModerationQueueService Service, CountingModerationRepo Repo) CreateEventModeQueueHarness(DateTime now)
    {
        var store = new StoryPlatform.UnitTests.Features.TokenQuota.InMemoryUnitOfWork();
        store.Seed(_moderatorA);
        store.Seed(new Event { Id = 1, Title = "Sự kiện 1", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = _moderatorA.Id });
        store.Seed(new Event { Id = 2, Title = "Sự kiện 2", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = _moderatorA.Id });
        store.Seed(new Event { Id = 3, Title = "Sự kiện 3", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = _moderatorA.Id });
        store.Seed(new EventActivity { Id = 1, EventId = 1, ActivityType = EventActivityType.Submission, ReviewCutoffAt = now.AddHours(3) });
        store.Seed(new EventActivity { Id = 2, EventId = 2, ActivityType = EventActivityType.Submission, ReviewCutoffAt = now.AddHours(1) });
        store.Seed(new EventActivity { Id = 3, EventId = 3, ActivityType = EventActivityType.Submission, ReviewCutoffAt = now.AddHours(2) });

        for (var i = 101; i <= 115; i++)
        {
            store.Seed(new Story { Id = i, Title = $"Truyện {i}" });
            store.Seed(new StoryVersion { Id = i, StoryId = i, VersionNo = 1, IsCurrent = true });
        }

        store.Seed(new EventSubmission { Id = 1, EventId = 1, StoryId = 101, CurrentStoryVersionId = 101, Status = EventSubmissionStatus.PendingReview });
        store.Seed(new EventSubmission { Id = 2, EventId = 2, StoryId = 102, CurrentStoryVersionId = 102, Status = EventSubmissionStatus.PendingReview });
        store.Seed(new EventSubmission { Id = 3, EventId = 3, StoryId = 103, CurrentStoryVersionId = 103, Status = EventSubmissionStatus.PendingReview });

        var prev = new ModerationItem { Id = 90, DecidedByUserId = _moderatorA.Id, Status = ModerationItemStatus.Rejected };
        store.Seed(prev);

        var items = new[]
        {
            new ModerationItem { Id = 101, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 101, StoryVersionId = 101, Priority = ModerationPriority.Low, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(20) },
            new ModerationItem { Id = 102, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 102, StoryVersionId = 102, Priority = ModerationPriority.Low, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(20) },
            new ModerationItem { Id = 103, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 103, StoryVersionId = 103, Priority = ModerationPriority.Low, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(20) },
            new ModerationItem { Id = 104, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 104, StoryVersionId = 104, Priority = ModerationPriority.Low, IsResubmission = true, PreviousItemId = 90, PreviousItem = prev, CreatedAt = now.AddMinutes(-10), SlaDueAt = now.AddHours(20) },
            new ModerationItem { Id = 105, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 105, StoryVersionId = 105, Priority = ModerationPriority.High, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(10) },
            new ModerationItem { Id = 106, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 106, StoryVersionId = 106, Priority = ModerationPriority.High, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(5) },
            new ModerationItem { Id = 107, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 107, StoryVersionId = 107, Priority = ModerationPriority.High, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(20) },
            new ModerationItem { Id = 108, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 108, StoryVersionId = 108, Priority = ModerationPriority.Medium, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(8) },
            new ModerationItem { Id = 109, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 109, StoryVersionId = 109, Priority = ModerationPriority.Medium, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(4) },
            new ModerationItem { Id = 110, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 110, StoryVersionId = 110, Priority = ModerationPriority.Medium, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(12) },
            new ModerationItem { Id = 111, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 111, StoryVersionId = 111, Priority = ModerationPriority.Medium, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(1) },
            new ModerationItem { Id = 112, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 112, StoryVersionId = 112, Priority = ModerationPriority.Low, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(30) },
            new ModerationItem { Id = 113, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 113, StoryVersionId = 113, Priority = ModerationPriority.Low, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(30) },
            new ModerationItem { Id = 114, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 114, StoryVersionId = 114, Priority = ModerationPriority.Low, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(30) },
            new ModerationItem { Id = 115, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending, StoryId = 115, StoryVersionId = 115, Priority = ModerationPriority.Low, CreatedAt = now.AddHours(-2), SlaDueAt = now.AddHours(30) }
        };
        foreach (var it in items) store.Seed(it);

        var countingRepo = new CountingModerationRepo(store.Repository<ModerationItem>());
        var uowProxy = new Mock<IUnitOfWork>();
        uowProxy.Setup(u => u.Repository<ModerationItem>()).Returns(countingRepo);
        uowProxy.Setup(u => u.Repository<UserAccount>()).Returns(store.Repository<UserAccount>());
        uowProxy.Setup(u => u.Repository<Event>()).Returns(store.Repository<Event>());
        uowProxy.Setup(u => u.Repository<EventActivity>()).Returns(store.Repository<EventActivity>());
        uowProxy.Setup(u => u.Repository<EventTrack>()).Returns(store.Repository<EventTrack>());
        uowProxy.Setup(u => u.Repository<EventSubmission>()).Returns(store.Repository<EventSubmission>());
        uowProxy.Setup(u => u.Repository<EventModeratorAssignment>()).Returns(store.Repository<EventModeratorAssignment>());

        return (new ModerationQueueService(uowProxy.Object, _auditLogWriterMock.Object), countingRepo);
    }

    [Fact]
    public async Task Queue_EventMode_PagesWithSameOrderAndTotal()
    {
        var (service, _) = CreateEventModeQueueHarness(DateTime.UtcNow);

        var p1 = await service.ListAsync(_moderatorA.Id, new ModerationQueueQueryDto { PageIndex = 1, PageSize = 6 });
        var p2 = await service.ListAsync(_moderatorA.Id, new ModerationQueueQueryDto { PageIndex = 2, PageSize = 6 });
        var p3 = await service.ListAsync(_moderatorA.Id, new ModerationQueueQueryDto { PageIndex = 3, PageSize = 6 });

        Assert.Equal(15, p1.TotalCount);
        Assert.Equal(15, p2.TotalCount);
        Assert.Equal(15, p3.TotalCount);
        Assert.Equal(new[] { 102, 103, 101, 104, 106, 105 }, p1.Items.Select(x => x.Id));
        Assert.Equal(new[] { 107, 111, 109, 108, 110, 112 }, p2.Items.Select(x => x.Id));
        Assert.Equal(new[] { 113, 114, 115 }, p3.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Queue_EventMode_LoadsIncludesOnlyForPageItems()
    {
        var (service, repo) = CreateEventModeQueueHarness(DateTime.UtcNow);

        await service.ListAsync(_moderatorA.Id, new ModerationQueueQueryDto { PageIndex = 1, PageSize = 6 });

        Assert.True(repo.LoadedWithIncludesCount <= 6, $"Expected <= 6 entities loaded with includes, got {repo.LoadedWithIncludesCount}.");
    }

    [Fact]
    public async Task HistoryQueue_ModeratorAssignedToOneOfTwoEvents_HidesItem()
    {
        var store = new StoryPlatform.UnitTests.Features.TokenQuota.InMemoryUnitOfWork();
        store.Seed(_moderatorA);
        store.Seed(_moderatorB);
        store.Seed(new Event { Id = 1, Title = "Sự kiện 1", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = _moderatorA.Id });
        store.Seed(new Event { Id = 2, Title = "Sự kiện 2", Status = EventStatus.Published, ConfigVersion = EventConfigVersion.ActivityBased, AssignedModeratorId = _moderatorB.Id });
        store.Seed(new Story { Id = 101, Title = "Truyện 101" });
        store.Seed(new StoryVersion { Id = 101, StoryId = 101, VersionNo = 1, IsCurrent = true });
        store.Seed(new EventSubmission { Id = 1, EventId = 1, StoryId = 101, CurrentStoryVersionId = 101, Status = EventSubmissionStatus.Approved });
        store.Seed(new EventSubmission { Id = 2, EventId = 2, StoryId = 101, CurrentStoryVersionId = 101, Status = EventSubmissionStatus.Approved });
        store.Seed(new ModerationItem
        {
            Id = 101,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Approved,
            StoryId = 101,
            StoryVersionId = 101
        });

        var service = new ModerationQueueService(store, _auditLogWriterMock.Object);
        var result = await service.ListAsync(_moderatorA.Id, new ModerationQueueQueryDto
        {
            Status = ModerationItemStatus.Approved,
            OnlyMyEventSubmissions = true
        });

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    private sealed class CountingModerationRepo(IGenericRepository<ModerationItem> inner) : IGenericRepository<ModerationItem>
    {
        public int LoadedWithIncludesCount { get; private set; }
        public Task<ModerationItem?> GetByIdAsync(object id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);
        public Task<IReadOnlyList<ModerationItem>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);
        public async Task<IReadOnlyList<ModerationItem>> FindAsync(Expression<Func<ModerationItem, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default)
        {
            var res = await inner.FindAsync(predicate, includeProperties, cancellationToken);
            if (!string.IsNullOrWhiteSpace(includeProperties)) LoadedWithIncludesCount += res.Count;
            return res;
        }
        public Task<ModerationItem?> FirstOrDefaultAsync(Expression<Func<ModerationItem, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(predicate, includeProperties, cancellationToken);
        public Task<(IReadOnlyList<ModerationItem> Items, int TotalCount)> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<ModerationItem, bool>>? filter = null, Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>? orderBy = null, string? includeProperties = null, CancellationToken cancellationToken = default) => inner.GetPagedAsync(pageIndex, pageSize, filter, orderBy, includeProperties, cancellationToken);
        public Task<ModerationItem> AddAsync(ModerationItem entity, CancellationToken cancellationToken = default) => inner.AddAsync(entity, cancellationToken);
        public Task AddRangeAsync(IEnumerable<ModerationItem> entities, CancellationToken cancellationToken = default) => inner.AddRangeAsync(entities, cancellationToken);
        public void Update(ModerationItem entity) => inner.Update(entity);
        public void Delete(ModerationItem entity) => inner.Delete(entity);
        public void DeleteRange(IEnumerable<ModerationItem> entities) => inner.DeleteRange(entities);
        public Task<int> CountAsync(Expression<Func<ModerationItem, bool>>? predicate = null, CancellationToken cancellationToken = default) => inner.CountAsync(predicate, cancellationToken);
        public Task<bool> ExistsAsync(Expression<Func<ModerationItem, bool>> predicate, CancellationToken cancellationToken = default) => inner.ExistsAsync(predicate, cancellationToken);
        public IQueryable<ModerationItem> Query() => inner.Query();
    }
}
