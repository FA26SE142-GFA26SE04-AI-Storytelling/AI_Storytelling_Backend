using Microsoft.Extensions.DependencyInjection;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.CreatorStatistics.DTOs;
using StoryPlatform.Application.Features.CreatorStatistics.Interfaces;
using StoryPlatform.Application.Features.CreatorTiers.DTOs;
using StoryPlatform.Application.Features.CreatorTiers.Interfaces;
using StoryPlatform.Application.Features.CreatorTiers.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.Services;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.CreatorTiers;

/// <summary>Xét cấp Creator đối tác (Luồng 5, 5g).</summary>
public sealed class CreatorTierServiceTests : IDisposable
{
    private const int AdminId = 1;
    private const int CreatorId = 2;
    private static readonly DateTime Now = new(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc); // 10:00 ngày 1/12 giờ Việt Nam

    private readonly InMemoryUnitOfWork _store = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ICreatorStatisticsService> _statistics = new();
    private readonly HashSet<int> _eligible = [];
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly CreatorTierService _sut;

    public CreatorTierServiceTests()
    {
        _store.Seed(new UserAccount { Id = AdminId, Role = UserRole.Administrator, Status = AccountStatus.Active });
        _statistics
            .Setup(s => s.GetPartnerProgressAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids, DateTime _, CancellationToken _) =>
                ids.ToDictionary(id => id, id => Progress(_eligible.Contains(id))));

        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(_store);
        services.AddSingleton(_audit.Object);
        services.AddSingleton(_notifications.Object);
        services.AddSingleton(_statistics.Object);
        services.AddScoped<CreatorTierService>();
        services.AddScoped<ICreatorTierService>(provider => provider.GetRequiredService<CreatorTierService>());
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _sut = _scope.ServiceProvider.GetRequiredService<CreatorTierService>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private static PartnerProgressDto Progress(bool eligible) => new()
    {
        WindowStart = Now.Date.AddDays(-90),
        WindowEnd = Now.Date,
        IsEligible = eligible,
        Criteria =
        [
            new() { Key = "published_stories", Current = eligible ? 12 : 4, Met = eligible },
            new() { Key = "completion_rate", Current = 0.7m, Met = true },
            new() { Key = "valid_reads", Current = eligible ? 650 : 120, Met = eligible },
            new() { Key = "no_violation_removals", Current = 0, Met = true }
        ]
    };

    private UserAccount SeedCreator(
        int id = CreatorId, CreatorTier tier = CreatorTier.Standard, AccountStatus status = AccountStatus.Active) =>
        _store.Seed(new UserAccount
        {
            Id = id, Username = $"creator_{id}", Role = UserRole.ContentCreator, Status = status, CreatorTier = tier,
            PartnerSince = tier == CreatorTier.Partner ? Now.AddMonths(-3) : null
        });

    private void SeedEvaluation(DateTime month, bool eligible, CreatorTier after) =>
        _store.Seed(new CreatorTierEvaluation
        {
            CreatorUserId = CreatorId, PeriodMonth = month, Eligible = eligible, TierBefore = after, TierAfter = after
        });

    private static DateTime ThisMonth => VietnamTime.StartOfMonthUtc(Now);
    private static DateTime LastMonth => VietnamTime.StartOfMonthUtc(ThisMonth.AddDays(-1));

    // ---------- Quy tắc lên / xuống cấp ----------

    [Theory]
    [InlineData(CreatorTier.Standard, true, null, CreatorTier.Partner)]
    [InlineData(CreatorTier.Standard, false, null, CreatorTier.Standard)]
    [InlineData(CreatorTier.Partner, true, false, CreatorTier.Partner)]
    [InlineData(CreatorTier.Partner, false, null, CreatorTier.Partner)]      // trượt tháng đầu: giữ cấp
    [InlineData(CreatorTier.Partner, false, true, CreatorTier.Partner)]      // tháng trước đạt: mới trượt 1 tháng
    [InlineData(CreatorTier.Partner, false, false, CreatorTier.Standard)]    // trượt 2 tháng liên tiếp
    public void Decide_FollowsTheTwoConsecutiveMonthsRule(
        CreatorTier before, bool eligible, bool? previousEligible, CreatorTier expected)
    {
        var previous = previousEligible is { } wasEligible
            ? new CreatorTierEvaluation { Eligible = wasEligible, TierAfter = CreatorTier.Partner }
            : null;

        Assert.Equal(expected, CreatorTierService.Decide(before, eligible, previous));
    }

    [Fact]
    public void Decide_PartnerRestoredAfterRevocation_DoesNotCountAnOldMissAsConsecutive()
    {
        // Tháng trước đã là Creator thường (bị thu hồi) nên lần trượt đó không tính cho đối tác mới.
        var previous = new CreatorTierEvaluation { Eligible = false, TierAfter = CreatorTier.Standard };

        Assert.Equal(CreatorTier.Partner, CreatorTierService.Decide(CreatorTier.Partner, false, previous));
    }

    // ---------- Xét hằng tháng ----------

    [Fact]
    public async Task EvaluateDue_PromotesEligibleCreatorsOncePerMonth()
    {
        var creator = SeedCreator();
        SeedCreator(3);
        SeedCreator(4, status: AccountStatus.Suspended);
        _eligible.Add(CreatorId);

        Assert.Equal(2, await _sut.EvaluateDueAsync(Now));
        Assert.Equal(0, await _sut.EvaluateDueAsync(Now.AddHours(5)));

        Assert.Equal((CreatorTier.Partner, (DateTime?)Now), (creator.CreatorTier, creator.PartnerSince));
        var evaluation = Assert.Single(_store.Items<CreatorTierEvaluation>(), item => item.CreatorUserId == CreatorId);
        Assert.Equal((ThisMonth, true, CreatorTier.Standard, CreatorTier.Partner, 12, 650),
            (evaluation.PeriodMonth, evaluation.Eligible, evaluation.TierBefore, evaluation.TierAfter,
                evaluation.PublishedStories, evaluation.ValidReads));
        Assert.DoesNotContain(_store.Items<CreatorTierEvaluation>(), item => item.CreatorUserId == 4);
        Assert.Contains($"lock:{CreatorId}", _store.Calls);
        _audit.Verify(a => a.LogAsync(null, "CreatorPartnerGranted", nameof(UserAccount), CreatorId,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(CreatorId, NotificationType.CreatorTierChanged,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(3, It.IsAny<NotificationType>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateDue_PartnerMissingTwoMonthsInARow_LosesTheTier()
    {
        var creator = SeedCreator(tier: CreatorTier.Partner);
        SeedEvaluation(LastMonth, eligible: false, after: CreatorTier.Partner);

        await _sut.EvaluateDueAsync(Now);

        Assert.Equal((CreatorTier.Standard, (DateTime?)null), (creator.CreatorTier, creator.PartnerSince));
        _audit.Verify(a => a.LogAsync(null, "CreatorPartnerLost", nameof(UserAccount), CreatorId,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureEvaluated_RunsOnceThenReusesTheMonthResult()
    {
        SeedCreator();
        _eligible.Add(CreatorId);

        Assert.Equal(CreatorTier.Partner, await _sut.EnsureEvaluatedAsync(CreatorId, Now));
        Assert.Equal(CreatorTier.Partner, await _sut.EnsureEvaluatedAsync(CreatorId, Now));

        Assert.Single(_store.Items<CreatorTierEvaluation>());
        _statistics.Verify(s => s.GetPartnerProgressAsync(
            It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- Hệ quả của cấp đối tác ----------

    [Fact]
    public async Task MonthlyAllowance_IsEvaluatedFirst_SoANewPartnerGetsSixAndTen()
    {
        SeedCreator();
        _eligible.Add(CreatorId);
        var quota = new TokenQuotaService(_store, creatorTiers: _sut);

        var result = await quota.GetCreatorQuotaAsync(CreatorId);

        Assert.Equal((6, 6, 10, 10),
            (result.AiCredits.MonthlyAllowance, result.AiCredits.MonthlyRemaining,
                result.SubmitCredits.MonthlyAllowance, result.SubmitCredits.MonthlyRemaining));
        Assert.Single(_store.Items<CreatorTierEvaluation>());
    }

    [Fact]
    public async Task Partner_GetsFortyWritingAssistsAndHighQueuePriority()
    {
        SeedCreator(tier: CreatorTier.Partner);
        var settings = new PlanSettingsProvider(_store);

        var benefits = await new PlanBenefitService(_store, settings).GetForUserAsync(CreatorId);

        Assert.Equal(("PartnerCreator", 40), (benefits.Tier, benefits.WritingAssistPerDay));
        Assert.Equal(ModerationPriority.High,
            ModerationQueuePolicy.GetPriority(ModerationItemType.PublicStoryReview, false, isPartnerCreator: true));
        Assert.Equal(ModerationPriority.Medium,
            ModerationQueuePolicy.GetPriority(ModerationItemType.PublicStoryReview, false));
    }

    // ---------- Mất cấp ngay ----------

    [Theory]
    [InlineData(ArchivedReason.SafetyConcern, StoryScope.Creator, CreatorTier.Standard)]
    [InlineData(ArchivedReason.Copyright, StoryScope.Creator, CreatorTier.Standard)]
    [InlineData(ArchivedReason.SupervisorPreference, StoryScope.Creator, CreatorTier.Partner)]
    [InlineData(ArchivedReason.Inappropriate, StoryScope.Parent, CreatorTier.Partner)]
    public async Task StoryRemovedForViolation_DemotesThePartnerImmediately(
        ArchivedReason reason, StoryScope scope, CreatorTier expected)
    {
        var creator = SeedCreator(tier: CreatorTier.Partner);
        var story = new Story { Id = 50, AuthorUserId = CreatorId, Scope = scope };

        await _sut.HandleAsync(new StoryArchiveContext(story, reason, ActorUserId: 9, Now));

        Assert.Equal(expected, creator.CreatorTier);
    }

    [Fact]
    public async Task Revoke_ByAdministrator_DemotesWithReason()
    {
        var creator = SeedCreator(tier: CreatorTier.Partner);

        var result = await _sut.RevokeAsync(AdminId, CreatorId, new RevokePartnerTierRequestDto { Reason = "Vi phạm cam kết" });

        Assert.Equal(("Standard", CreatorTier.Standard), (result.Tier, creator.CreatorTier));
        _audit.Verify(a => a.LogAsync(AdminId, "CreatorPartnerRevoked", nameof(UserAccount), CreatorId,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_Invalid_IsRejected()
    {
        SeedCreator();
        _store.Seed(new UserAccount { Id = 5, Role = UserRole.Moderator, Status = AccountStatus.Active });

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RevokeAsync(AdminId, CreatorId, new RevokePartnerTierRequestDto { Reason = " " }));
        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.RevokeAsync(AdminId, CreatorId, new RevokePartnerTierRequestDto { Reason = "x" }));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.RevokeAsync(5, CreatorId, new RevokePartnerTierRequestDto { Reason = "x" }));
    }
}
