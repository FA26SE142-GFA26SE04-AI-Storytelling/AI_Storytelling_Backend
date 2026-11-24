using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Payments;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.BusinessReports.DTOs;
using StoryPlatform.Application.Features.BusinessReports.Interfaces;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Application.Features.Payments.Services;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.Services;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AccessControl;

/// <summary>Luồng 5: M5, Phần II và đối soát Luồng 2 chỉ Administrator đang hoạt động, kiểm ngay trong service.</summary>
public sealed class AdminActionAccessTests
{
    private const int AdminId = 1, ModeratorId = 11;
    private readonly List<UserAccount> _users =
    [
        new() { Id = AdminId, Role = UserRole.Administrator, Status = AccountStatus.Active },
        new() { Id = ModeratorId, Role = UserRole.Moderator, Status = AccountStatus.Active },
    ];
    private readonly Mock<IUnitOfWork> _uow = new() { DefaultValue = DefaultValue.Mock };
    private readonly IAuditLogWriter _audit = Mock.Of<IAuditLogWriter>();

    public AdminActionAccessTests()
    {
        var users = new Mock<IGenericRepository<UserAccount>>();
        users.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => _users.SingleOrDefault(u => u.Id == (int)id));
        users.Setup(r => r.CountAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserAccount, bool>> p, CancellationToken _) => _users.AsQueryable().Count(p));
        _uow.Setup(u => u.Repository<UserAccount>()).Returns(users.Object);
    }

    private void LockAdmin() => _users.Single(u => u.Id == AdminId).LockedUntil = DateTime.UtcNow.AddMinutes(10);

    public static TheoryData<string> Actions => new()
    {
        "mark-paid", "reconciliation", "plan-settings", "adjust-credits", "business-report",
    };

    private Func<int, Task> Action(string name) => name switch
    {
        "mark-paid" => actor => new PaymentService(_uow.Object, _audit, Mock.Of<INotificationService>(),
            Mock.Of<ISePayQrUrlBuilder>(), Mock.Of<ISePayWebhookAuthenticator>(), Mock.Of<ISubscriptionService>())
            .MarkPaidManuallyAsync(actor, 99),
        "reconciliation" => actor => new PaymentReconciliationService(_uow.Object, Mock.Of<ISubscriptionService>(), _audit)
            .ResolveAsync(actor, 99),
        "plan-settings" => actor => new PlanSettingsService(_uow.Object, Mock.Of<IPlanSettingsProvider>(), _audit)
            .UpdateAsync(actor, new UpdatePlanSettingsRequestDto()),
        "adjust-credits" => actor => new TokenQuotaService(_uow.Object, _audit)
            .AdjustAsync(actor, new AdjustCreditsRequestDto { UserId = 5, Amount = 1, Reason = "Bù lỗi" }),
        "business-report" => actor => new BusinessReportService(_uow.Object, _audit, Mock.Of<IRevenueStatisticsService>(),
                Mock.Of<IUserAccountStatisticsService>())
            .GenerateAsync(actor, new GenerateBusinessReportRequestDto()),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task LockedAdministrator_IsForbidden(string action)
    {
        LockAdmin();

        await Assert.ThrowsAsync<ForbiddenException>(() => Action(action)(AdminId));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Moderator_IsForbidden(string action)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => Action(action)(ModeratorId));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task ActiveAdministrator_PassesTheAccessGate(string action)
    {
        var error = await Record.ExceptionAsync(() => Action(action)(AdminId));

        Assert.False(error is ForbiddenException, error?.Message);
    }
}
