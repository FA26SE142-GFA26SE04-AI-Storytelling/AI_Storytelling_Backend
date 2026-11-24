using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.Interfaces;
using StoryPlatform.Application.Features.AccessControl.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AccessControl;

public sealed class AccessPolicyTests
{
    private const int AdminId = 1, ModeratorId = 11, CreatorId = 20;
    private readonly List<UserAccount> _users =
    [
        new() { Id = AdminId, Role = UserRole.Administrator, Status = AccountStatus.Active },
        new() { Id = ModeratorId, Role = UserRole.Moderator, Status = AccountStatus.Active },
        new() { Id = CreatorId, Role = UserRole.ContentCreator, Status = AccountStatus.Active },
    ];

    private IAccessPolicy Policy(IPermissionService? permissions = null)
    {
        var repo = new Mock<IGenericRepository<UserAccount>>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => _users.SingleOrDefault(u => u.Id == (int)id));
        repo.Setup(r => r.CountAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserAccount, bool>> p, CancellationToken _) => _users.AsQueryable().Count(p));
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Repository<UserAccount>()).Returns(repo.Object);
        return new AccessPolicy(uow.Object, permissions ?? new StaticPermissionService());
    }

    private static ModerationItem Pending(int? createdBy = CreatorId, DateTime? escalatedAt = null) =>
        new() { Id = 5, ItemType = ModerationItemType.GrayZoneStory, Status = ModerationItemStatus.Pending,
            CreatedByUserId = createdBy, EscalatedAt = escalatedAt };

    private void DeactivateModerator() => _users.Single(u => u.Id == ModeratorId).Status = AccountStatus.Suspended;

    [Theory]
    [InlineData("deleted")]
    [InlineData("suspended")]
    [InlineData("locked")]
    [InlineData("missing")]
    public async Task InvalidActor_IsForbidden_WithDeniedMessage(string state)
    {
        var moderator = _users.Single(u => u.Id == ModeratorId);
        if (state == "deleted") moderator.IsDeleted = true;
        if (state == "suspended") moderator.Status = AccountStatus.Suspended;
        if (state == "locked") moderator.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        var actorId = state == "missing" ? 999 : ModeratorId;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Policy().EnsureAsync(
            actorId, Permissions.Moderation.QueueProcess, mode: AccessMode.Read, deniedMessage: "Chỉ nhân viên."));

        Assert.Equal("Chỉ nhân viên.", error.Message);
    }

    [Fact]
    public async Task RoleWithoutPermission_IsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Policy().EnsureAsync(CreatorId, Permissions.Moderation.QueueProcess, mode: AccessMode.Read));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Policy().EnsureAsync(AdminId, Permissions.Moderation.ReportReview, Pending()));
    }

    [Fact]
    public async Task RevokedPermission_IsForbidden_EvenForModerator()
    {
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(p => p.RoleHasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Policy(permissions.Object).EnsureAsync(ModeratorId, Permissions.Moderation.QueueProcess, mode: AccessMode.Read));
    }

    [Fact]
    public async Task Admin_ClaimPending_ForbiddenWhileEligibleModeratorExists()
    {
        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Policy().EnsureAsync(AdminId, Permissions.Review.GrayZone, Pending()));

        Assert.Equal(AccessPolicy.AdminFallbackMessage, error.Message);
    }

    [Fact]
    public async Task Admin_ClaimPending_AllowedWhenOnlyActiveModeratorIsTheCreator()
    {
        var actor = await Policy().EnsureAsync(AdminId, Permissions.Review.GrayZone, Pending(createdBy: ModeratorId));

        Assert.Equal(AdminId, actor.Id);
    }

    [Fact]
    public async Task Admin_ClaimPending_AllowedWhenNoActiveModerator()
    {
        DeactivateModerator();

        Assert.Equal(AdminId, (await Policy().EnsureAsync(AdminId, Permissions.Review.GrayZone, Pending())).Id);
    }

    [Fact]
    public async Task Admin_NotBlocked_WhenModeratorRoleNoLongerHoldsThePermission()
    {
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(p => p.RoleHasAsync(RoleCodes.Administrator, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        permissions.Setup(p => p.RoleHasAsync(RoleCodes.Moderator, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var policy = Policy(permissions.Object);

        Assert.Equal(AdminId, (await policy.EnsureAsync(AdminId, Permissions.Review.GrayZone, Pending())).Id);
        Assert.Equal(AdminId, (await policy.EnsureAsync(AdminId, Permissions.Review.GuideManage)).Id);
    }

    [Fact]
    public async Task Admin_ClaimEscalatedItem_AllowedWhileModeratorActive()
    {
        var actor = await Policy().EnsureAsync(AdminId, Permissions.Review.GrayZone, Pending(escalatedAt: DateTime.UtcNow));

        Assert.Equal(AdminId, actor.Id);
    }

    [Fact]
    public async Task Admin_ActingOnItemAlreadyClaimed_IsNotBlockedByFallback()
    {
        var item = Pending();
        item.Status = ModerationItemStatus.Claimed;
        item.ClaimedByUserId = AdminId;

        Assert.Equal(AdminId, (await Policy().EnsureAsync(AdminId, Permissions.Review.GrayZone, item)).Id);
    }

    [Theory]
    [InlineData(ModeratorId)]
    [InlineData(AdminId)]
    public async Task Staff_ClaimOwnItem_IsForbidden(int actorId)
    {
        if (actorId == AdminId) DeactivateModerator();
        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Policy().EnsureAsync(actorId, Permissions.Moderation.QueueProcess, Pending(createdBy: actorId)));

        Assert.Equal(AccessPolicy.OwnContentMessage, error.Message);
    }

    [Theory]
    [InlineData(Permissions.Review.GuideManage)]
    [InlineData(Permissions.Review.RubricManage)]
    [InlineData(Permissions.Review.WishCardManage)]
    [InlineData(Permissions.Review.RoadmapTemplateAuthor)]
    public async Task Admin_WriteModerationConfig_OnlyWhenNoActiveModerator(string permission)
    {
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Policy().EnsureAsync(AdminId, permission));
        Assert.Equal(AccessPolicy.NoActiveModeratorMessage, error.Message);

        DeactivateModerator();
        Assert.Equal(AdminId, (await Policy().EnsureAsync(AdminId, permission)).Id);
    }

    [Fact]
    public async Task Admin_ReadModerationConfig_AllowedWhileModeratorActive()
    {
        var actor = await Policy().EnsureAsync(AdminId, Permissions.Review.GuideManage, mode: AccessMode.Read);

        Assert.Equal(AdminId, actor.Id);
    }

    [Fact]
    public async Task Moderator_WriteModerationConfig_HasNoCondition()
    {
        Assert.Equal(ModeratorId, (await Policy().EnsureAsync(ModeratorId, Permissions.Review.RoadmapTemplateAuthor)).Id);
    }

    [Fact]
    public void Catalog_MarksClaimedWorkAsAssignedScope()
    {
        Assert.Equal(AccessScope.Assigned, PermissionCatalog.Find(Permissions.Review.GrayZone)!.Scope);
        Assert.Equal(AccessScope.Any, PermissionCatalog.Find(Permissions.Review.GuideManage)!.Scope);
    }
}
