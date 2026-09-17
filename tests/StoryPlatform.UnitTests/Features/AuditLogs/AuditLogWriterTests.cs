using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Services;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AuditLogs;

public class AuditLogWriterTests
{
    private readonly Mock<IGenericRepository<AuditLog>> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly AuditLogWriter _sut;

    public AuditLogWriterTests()
    {
        _unitOfWork.Setup(work => work.Repository<AuditLog>()).Returns(_repository.Object);
        _sut = new AuditLogWriter(_unitOfWork.Object);
    }

    [Fact]
    public async Task LogAsync_WithBeforeAndAfterState_AddsAuditLogWithSerializedStatesAndSaves()
    {
        AuditLog? added = null;
        _repository.Setup(repository => repository.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => added = log)
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);

        await _sut.LogAsync(
            actorUserId: 9,
            action: "AdminGranted",
            entityType: "UserAccount",
            entityId: 42,
            beforeState: new { role = "Teacher" },
            afterState: new { role = "Administrator" });

        Assert.NotNull(added);
        Assert.Equal(9, added!.ActorUserId);
        Assert.Equal("AdminGranted", added.Action);
        Assert.Equal("UserAccount", added.EntityType);
        Assert.Equal(42, added.EntityId);
        Assert.Contains("\"role\":\"Teacher\"", added.BeforeState);
        Assert.Contains("\"role\":\"Administrator\"", added.AfterState);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WithNullStates_StoresNullWithoutSerializing()
    {
        AuditLog? added = null;
        _repository.Setup(repository => repository.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => added = log)
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);

        await _sut.LogAsync(
            actorUserId: null,
            action: "SystemJob",
            entityType: "TokenQuotaConfig",
            entityId: 1,
            beforeState: null,
            afterState: null);

        Assert.NotNull(added);
        Assert.Null(added!.ActorUserId);
        Assert.Null(added.BeforeState);
        Assert.Null(added.AfterState);
    }
}
