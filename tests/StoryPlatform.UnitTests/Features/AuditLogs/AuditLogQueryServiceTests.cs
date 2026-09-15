using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Services;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AuditLogs;

public class AuditLogQueryServiceTests
{
    private readonly Mock<IGenericRepository<AuditLog>> _auditLogRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISupervisionAccessGuard> _accessGuard = new();
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
        _accessGuard.Setup(guard => guard.EnsureActiveSupervisionAsync(
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
        _accessGuard.Setup(guard => guard.EnsureActiveSupervisionAsync(
                42, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupervisionRelationship());
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
