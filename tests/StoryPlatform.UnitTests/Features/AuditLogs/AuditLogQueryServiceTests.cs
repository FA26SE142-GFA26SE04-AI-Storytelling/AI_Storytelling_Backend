using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Services;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AuditLogs;

public class AuditLogQueryServiceTests
{
    private readonly Mock<IGenericRepository<AuditLog>> _auditLogRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IChildProfileAccessGuard> _accessGuard = new();
    private readonly AuditLogQueryService _sut;

    public AuditLogQueryServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<AuditLog>()).Returns(_auditLogRepo.Object);
        _sut = new AuditLogQueryService(_unitOfWork.Object, _accessGuard.Object);
    }

    [Fact]
    public async Task GetMyAuditLogAsync_ReturnsPagedResultMappedFromRepository()
    {
        var logs = new List<AuditLog>
        {
            new()
            {
                Id = 1, ActorUserId = 7, Action = "CHANGE_PASSWORD",
                EntityType = nameof(UserAccount), EntityId = 7, OccurredAt = DateTime.UtcNow
            }
        };
        _auditLogRepo.Setup(repo => repo.GetPagedAsync(
                1, 10, It.IsAny<Expression<Func<AuditLog, bool>>>(),
                It.IsAny<Func<IQueryable<AuditLog>, IOrderedQueryable<AuditLog>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((logs, 1));

        var result = await _sut.GetMyAuditLogAsync(7, new PageRequest());

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("CHANGE_PASSWORD", result.Items.Single().Action);
        Assert.Equal(7, result.Items.Single().ActorUserId);
    }

    [Fact]
    public async Task GetChildProfileAuditLogAsync_CallerNotSupervisor_PropagatesGuardException()
    {
        _accessGuard.Setup(guard => guard.EnsureOwnerAsync(
                42, 999, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Bạn không có quyền giám sát hồ sơ trẻ này."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetChildProfileAuditLogAsync(42, 999, new PageRequest()));
        _auditLogRepo.Verify(repo => repo.GetPagedAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<AuditLog, bool>>>(),
            It.IsAny<Func<IQueryable<AuditLog>, IOrderedQueryable<AuditLog>>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetChildProfileAuditLogAsync_ActiveSupervisor_ReturnsOnlyRequestedProfileLogs()
    {
        Expression<Func<AuditLog, bool>>? capturedFilter = null;
        _accessGuard.Setup(guard => guard.EnsureOwnerAsync(
                42, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 42, OwnerUserId = 7 });
        _auditLogRepo.Setup(repo => repo.GetPagedAsync(
                2, 5, It.IsAny<Expression<Func<AuditLog, bool>>>(),
                It.IsAny<Func<IQueryable<AuditLog>, IOrderedQueryable<AuditLog>>>(), null,
                It.IsAny<CancellationToken>()))
            .Callback<int, int, Expression<Func<AuditLog, bool>>?,
                Func<IQueryable<AuditLog>, IOrderedQueryable<AuditLog>>?, string?, CancellationToken>(
                (_, _, filter, _, _, _) => capturedFilter = filter)
            .ReturnsAsync((new List<AuditLog>
            {
                new()
                {
                    Id = 3, ActorUserId = 7, Action = "UPDATE_CHILD_PROFILE",
                    EntityType = nameof(ChildProfile), EntityId = 42, OccurredAt = DateTime.UtcNow
                }
            }, 1));

        var result = await _sut.GetChildProfileAuditLogAsync(
            42, 7, new PageRequest { PageIndex = 2, PageSize = 5 });

        Assert.Single(result.Items);
        Assert.Equal(2, result.PageIndex);
        Assert.NotNull(capturedFilter);
        var filter = capturedFilter!.Compile();
        Assert.True(filter(new AuditLog { EntityType = nameof(ChildProfile), EntityId = 42 }));
        Assert.False(filter(new AuditLog { EntityType = nameof(ChildProfile), EntityId = 99 }));
        Assert.False(filter(new AuditLog { EntityType = nameof(UserAccount), EntityId = 42 }));
    }
}

/// <summary>Màn audit log của Administrator (Luồng 5) với kho dữ liệu trong bộ nhớ: lọc, sắp xếp, phân trang thật.</summary>
public class AdminAuditLogQueryTests
{
    private static readonly DateTime Base = new(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);

    private readonly StoryPlatform.UnitTests.Features.TokenQuota.InMemoryUnitOfWork _store = new();
    private readonly AuditLogQueryService _sut;
    private readonly UserAccount _admin = new()
    {
        Id = 1, Username = "admin_demo", FullName = "Quản Trị", Role = StoryPlatform.Domain.Enums.UserRole.Administrator
    };

    public AdminAuditLogQueryTests()
    {
        _store.Seed(_admin);
        Log(1, 1, "PlanSettingsUpdated", "PlanSettings", 1, Base, "{\"before\":1}", "{\"after\":2}");
        Log(2, null, "CreatorPartnerGranted", nameof(UserAccount), 13, Base.AddHours(1));
        Log(3, 1, "CreatorPayoutMarkedPaid", "CreatorBalanceEntry", 5, Base.AddDays(1));
        Log(4, 7, "CHANGE_PASSWORD", nameof(UserAccount), 7, Base.AddDays(2));
        _store.Seed(new AuditLog
        {
            Id = 5, ActorUserId = 1, Action = "Hidden", EntityType = "X", EntityId = 1, OccurredAt = Base, IsDeleted = true
        });
        _sut = new AuditLogQueryService(_store, new Mock<IChildProfileAccessGuard>().Object);
    }

    private void Log(int id, int? actor, string action, string entityType, int entityId, DateTime at,
        string? before = null, string? after = null) =>
        _store.Seed(new AuditLog
        {
            Id = id, ActorUserId = actor, ActorUser = actor == 1 ? _admin : null, Action = action, EntityType = entityType,
            EntityId = entityId, OccurredAt = at, BeforeState = before, AfterState = after
        });

    [Fact]
    public async Task Search_NoFilter_ReturnsEveryVisibleLogNewestFirst_WithActorDetails()
    {
        var result = await _sut.SearchAsync(new StoryPlatform.Application.Features.AuditLogs.DTOs.AdminAuditLogQuery());

        Assert.Equal(4, result.TotalCount);
        Assert.Equal([4, 3, 2, 1], result.Items.Select(item => item.Id));
        var oldest = result.Items.Last();
        Assert.Equal(("admin_demo", "Quản Trị", "Administrator"), (oldest.ActorUsername, oldest.ActorFullName, oldest.ActorRole));
        Assert.Equal(("{\"before\":1}", "{\"after\":2}"), (oldest.BeforeState, oldest.AfterState));
        Assert.Null(result.Items.Single(item => item.Id == 2).ActorUsername);
    }

    [Fact]
    public async Task Search_CombinesFilters()
    {
        var byActor = await _sut.SearchAsync(new() { ActorUserId = 1 });
        Assert.Equal([3, 1], byActor.Items.Select(item => item.Id));

        var system = await _sut.SearchAsync(new() { SystemOnly = true });
        Assert.Equal(2, Assert.Single(system.Items).Id);

        var byEntity = await _sut.SearchAsync(new() { EntityType = " UserAccount ", EntityId = 13 });
        Assert.Equal(2, Assert.Single(byEntity.Items).Id);

        var byAction = await _sut.SearchAsync(new() { Action = "CHANGE_PASSWORD" });
        Assert.Equal(4, Assert.Single(byAction.Items).Id);

        var inRange = await _sut.SearchAsync(new() { From = Base.AddHours(1), To = Base.AddDays(1) });
        Assert.Equal([3, 2], inRange.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Search_PagesResults()
    {
        var second = await _sut.SearchAsync(new() { PageIndex = 2, PageSize = 3 });

        Assert.Equal((4, 2, 3), (second.TotalCount, second.PageIndex, second.PageSize));
        Assert.Equal(1, Assert.Single(second.Items).Id);
    }

    [Fact]
    public async Task Search_InvalidFilters_AreRejected()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SearchAsync(new() { From = Base, To = Base.AddSeconds(-1) }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SearchAsync(new() { ActorUserId = 1, SystemOnly = true }));
    }

    [Fact]
    public async Task GetById_ReturnsDetail_OrNotFound()
    {
        Assert.Equal("CreatorPayoutMarkedPaid", (await _sut.GetByIdAsync(3)).Action);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(5));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(99));
    }

    [Fact]
    public async Task GetFilters_ListsDistinctActionsAndEntityTypesSorted()
    {
        var filters = await _sut.GetFiltersAsync();

        Assert.Equal(["CHANGE_PASSWORD", "CreatorPartnerGranted", "CreatorPayoutMarkedPaid", "PlanSettingsUpdated"],
            filters.Actions.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(["CreatorBalanceEntry", "PlanSettings", "UserAccount"], filters.EntityTypes.OrderBy(name => name, StringComparer.Ordinal));
    }
}
