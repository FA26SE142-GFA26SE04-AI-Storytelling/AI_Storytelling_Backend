using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AccessControl.Services;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.AuthoringGuideManagement.DTOs;
using StoryPlatform.Application.Features.AuthoringGuideManagement.Services;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Moderation.Rubrics;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates.Services;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AccessControl;

/// <summary>Luồng 5, cấu hình M4: Administrator xem được, chỉ sửa khi không còn Moderator hoạt động; kích hoạt rubric cả hai.</summary>
public sealed class ModerationConfigAccessTests
{
    private const int AdminId = 1, ModeratorId = 11;
    private readonly List<UserAccount> _users =
    [
        new() { Id = AdminId, Role = UserRole.Administrator, Status = AccountStatus.Active },
        new() { Id = ModeratorId, Role = UserRole.Moderator, Status = AccountStatus.Active },
    ];
    private readonly Mock<IUnitOfWork> _uow = new() { DefaultValue = DefaultValue.Mock };
    private readonly IAuditLogWriter _audit = Mock.Of<IAuditLogWriter>();

    public ModerationConfigAccessTests()
    {
        var users = new Mock<IGenericRepository<UserAccount>>();
        users.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => _users.SingleOrDefault(u => u.Id == (int)id));
        users.Setup(r => r.CountAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserAccount, bool>> p, CancellationToken _) => _users.AsQueryable().Count(p));
        _uow.Setup(u => u.Repository<UserAccount>()).Returns(users.Object);
    }

    private void NoActiveModerator() => _users.Single(u => u.Id == ModeratorId).Status = AccountStatus.Suspended;

    /// <summary>Lỗi khác 403 nghĩa là đã qua cổng quyền và dừng ở bước nghiệp vụ sau (dữ liệu giả rỗng).</summary>
    private static async Task AssertPassesAccessGate(Func<Task> action)
    {
        var error = await Record.ExceptionAsync(action);
        Assert.False(error is ForbiddenException, error?.Message);
    }

    private static async Task AssertBlockedForAdmin(Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<ForbiddenException>(action);
        Assert.Equal(AccessPolicy.NoActiveModeratorMessage, error.Message);
    }

    [Fact]
    public async Task Guides_AdminEditsOnlyWithoutActiveModerator_ButAlwaysViews()
    {
        var sut = new AuthoringGuideManagementService(_uow.Object, _audit);
        var request = new CreateGuideDraftRequest(null);

        await AssertBlockedForAdmin(() => sut.CreateDraftAsync(AdminId, request));
        await AssertPassesAccessGate(() => sut.GetOverviewAsync(AdminId));
        await AssertPassesAccessGate(() => sut.CreateDraftAsync(ModeratorId, request));

        NoActiveModerator();
        await AssertPassesAccessGate(() => sut.CreateDraftAsync(AdminId, request));
    }

    [Fact]
    public async Task WishCards_AdminEditsOnlyWithoutActiveModerator()
    {
        var cards = new WishCardManagementService(_uow.Object, _audit);
        var matches = new WishCardMatchService(_uow.Object, _audit);
        var request = new CreateWishCardRequestDto();

        await AssertBlockedForAdmin(() => cards.CreateAsync(AdminId, request));
        await AssertBlockedForAdmin(() => cards.SetActiveAsync(AdminId, 5, false));
        await AssertBlockedForAdmin(() => matches.AddAsync(AdminId, 7, 5));

        NoActiveModerator();
        await AssertPassesAccessGate(() => cards.CreateAsync(AdminId, request));
    }

    [Fact]
    public async Task Rubric_AdminEditsOnlyWithoutActiveModerator_ButActivatesAnyTime()
    {
        var sut = new ModerationRubricService(_uow.Object, Array.Empty<IModelModerationProvider>());
        var request = new RubricWriteRequest("v1", "Rubric", null!);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => sut.UpdateAsync(AdminId, 3, request, CancellationToken.None));
        Assert.Equal(AccessPolicy.NoActiveModeratorMessage, error.Message);
        await AssertPassesAccessGate(() => sut.ActivateAsync(AdminId, 3, 1, CancellationToken.None));
        await AssertPassesAccessGate(() => sut.GetAsync(AdminId, 3, CancellationToken.None));

        NoActiveModerator();
        await AssertPassesAccessGate(() => sut.UpdateAsync(AdminId, 3, request, CancellationToken.None));
    }

    [Fact]
    public async Task RoadmapTemplates_AdminAuthorsOnlyWithoutActiveModerator()
    {
        var sut = new RoadmapTemplateService(_uow.Object, Mock.Of<IRoadmapSubmissionChecker>(),
            Mock.Of<IModerationQueueService>(), _audit);
        var request = new SaveRoadmapRequestDto();

        await AssertBlockedForAdmin(() => sut.CreateAsync(AdminId, request));

        NoActiveModerator();
        await AssertPassesAccessGate(() => sut.CreateAsync(AdminId, request));
    }
}
