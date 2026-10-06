using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Services;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Administration;

public class AdminAccountServiceTests
{
    private readonly Mock<IGenericRepository<UserAccount>> _userRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly AdminAccountService _sut;

    public AdminAccountServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<UserAccount>()).Returns(_userRepository.Object);
        _notificationService
            .Setup(service => service.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationDto());

        _sut = new AdminAccountService(_unitOfWork.Object, _auditLogWriter.Object, _notificationService.Object);
    }

    private static UserAccount MakeUser(int id, UserRole role, AccountStatus status = AccountStatus.LoggedIn,
        UserRole? roleBeforeAdmin = null) => new()
    {
        Id = id,
        Email = $"user{id}@example.com",
        FullName = $"User {id}",
        Role = role,
        Status = status,
        RoleBeforeAdmin = roleBeforeAdmin
    };

    // ---------- Grant ----------

    [Fact]
    public async Task GrantAsync_EmailNotFound_ThrowsNotFound()
    {
        _userRepository.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GrantAsync(
            1, new GrantAdministratorRequestDto { Email = "missing@example.com" }));
    }

    [Fact]
    public async Task GrantAsync_AlreadyAdministrator_ThrowsConflict()
    {
        var target = MakeUser(2, UserRole.Administrator);
        _userRepository.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.GrantAsync(
            1, new GrantAdministratorRequestDto { Email = target.Email }));
    }

    [Fact]
    public async Task GrantAsync_ValidTarget_SavesRoleBeforeAdminPromotesWritesAuditAndNotifies()
    {
        var target = MakeUser(2, UserRole.Parent);
        _userRepository.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        var result = await _sut.GrantAsync(
            10, new GrantAdministratorRequestDto { Email = target.Email });

        Assert.Equal(UserRole.Parent, target.RoleBeforeAdmin);
        Assert.Equal(UserRole.Administrator, target.Role);
        Assert.Equal("Administrator", result.Role);
        Assert.Equal("Parent", result.RoleBeforeAdmin);
        _userRepository.Verify(repo => repo.Update(target), Times.Once);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            10, "AdminGranted", nameof(UserAccount), target.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(service => service.CreateAsync(
            target.Id, NotificationType.AdminRoleChanged, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------- Revoke ----------

    [Fact]
    public async Task RevokeAsync_TargetNotFound_ThrowsNotFound()
    {
        _userRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RevokeAsync(1, 2));
    }

    [Fact]
    public async Task RevokeAsync_TargetNotAdministrator_ThrowsBadRequest()
    {
        var target = MakeUser(2, UserRole.Parent);
        _userRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RevokeAsync(1, 2));
    }

    [Fact]
    public async Task RevokeAsync_LastActiveAdministrator_ThrowsConflict()
    {
        var target = MakeUser(2, UserRole.Administrator, roleBeforeAdmin: UserRole.Parent);
        _userRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        _userRepository.Setup(repo => repo.CountAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.RevokeAsync(1, 2));

        _userRepository.Verify(repo => repo.Update(It.IsAny<UserAccount>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_RoleBeforeAdminSet_RestoresThatRoleAndClearsColumn()
    {
        var target = MakeUser(2, UserRole.Administrator, roleBeforeAdmin: UserRole.Parent);
        _userRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        _userRepository.Setup(repo => repo.CountAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await _sut.RevokeAsync(1, 2);

        Assert.Equal(UserRole.Parent, target.Role);
        Assert.Null(target.RoleBeforeAdmin);
        Assert.Equal("Parent", result.Role);
    }

    [Fact]
    public async Task RevokeAsync_NullRoleBeforeAdmin_DefaultsToParent()
    {
        var target = MakeUser(2, UserRole.Administrator, roleBeforeAdmin: null);
        _userRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        _userRepository.Setup(repo => repo.CountAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await _sut.RevokeAsync(1, 2);

        Assert.Equal(UserRole.Parent, target.Role);
        Assert.Equal("Parent", result.Role);
    }

    [Fact]
    public async Task RevokeAsync_Success_WritesAuditLogAndNotifies()
    {
        var target = MakeUser(2, UserRole.Administrator, roleBeforeAdmin: UserRole.Parent);
        _userRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        _userRepository.Setup(repo => repo.CountAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        await _sut.RevokeAsync(10, 2);

        _userRepository.Verify(repo => repo.Update(target), Times.Once);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            10, "AdminRevoked", nameof(UserAccount), target.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(service => service.CreateAsync(
            target.Id, NotificationType.AdminRoleChanged, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
