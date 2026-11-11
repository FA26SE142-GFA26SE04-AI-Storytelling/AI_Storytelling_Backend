using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.PlanBenefits;

public sealed class PlanBenefitServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 3, 0, 0, DateTimeKind.Utc);
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly PlanBenefitService _service;

    public PlanBenefitServiceTests()
    {
        _service = new PlanBenefitService(_uow, new PlanSettingsProvider(_uow), new ManualTimeProvider(Now));
        _uow.Seed(new UserAccount { Id = 10, Role = UserRole.User });
        _uow.Seed(new UserAccount { Id = 20, Role = UserRole.ContentCreator });
    }

    internal static SubscriptionPlan CreativePlan() => new()
    {
        Id = 3, Name = "Gói Sáng tạo", PlanType = PlanType.Creative, PriceVnd = 99000, AiCredits = 4,
        MaxChildProfiles = 4, CanCopyRoadmapTemplates = true, ReadAloudPerMonth = 60, WritingAssistPerDay = 20
    };

    private SubscriptionPlan GivenCycle(SubscriptionStatus status, DateTime startAt, DateTime endAt)
    {
        var plan = _uow.Items<SubscriptionPlan>().FirstOrDefault() ?? _uow.Seed(CreativePlan());
        _uow.Seed(new UserSubscription { Id = _uow.Items<UserSubscription>().Count + 1, UserId = 10, PlanId = plan.Id, Plan = plan, Status = status, StartAt = startAt, EndAt = endAt });
        return plan;
    }

    [Fact]
    public async Task GetForUser_ActiveCreativeCycle_ReturnsPlanBenefits()
    {
        GivenCycle(SubscriptionStatus.Active, Now.AddDays(-3), Now.AddDays(27));

        var benefits = await _service.GetForUserAsync(10);

        Assert.Equal(("Gói Sáng tạo", (int?)3, 4, (int?)null, true, (int?)60, 20),
            (benefits.Tier, benefits.PlanId, benefits.MaxChildProfiles, benefits.WeeklyNewLibraryStories,
                benefits.CanCopyRoadmapTemplates, benefits.ReadAloudPerMonth, benefits.WritingAssistPerDay));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Expired)]
    [InlineData(SubscriptionStatus.Superseded)]
    [InlineData(SubscriptionStatus.Scheduled)]
    public async Task GetForUser_NoActiveCycle_ReturnsFreeSettings(SubscriptionStatus status)
    {
        GivenCycle(status, Now.AddDays(-3), Now.AddDays(27));

        var benefits = await _service.GetForUserAsync(10);

        Assert.Equal((PlanBenefitsDto.FreeTier, (int?)null, 1, (int?)3, false, (int?)5, 5),
            (benefits.Tier, benefits.PlanId, benefits.MaxChildProfiles, benefits.WeeklyNewLibraryStories,
                benefits.CanCopyRoadmapTemplates, benefits.ReadAloudPerMonth, benefits.WritingAssistPerDay));
    }

    [Fact]
    public async Task GetForUser_ActiveCycleAlreadyEnded_ReturnsFree()
    {
        GivenCycle(SubscriptionStatus.Active, Now.AddDays(-31), Now.AddMinutes(-1));

        Assert.Equal(PlanBenefitsDto.FreeTier, (await _service.GetForUserAsync(10)).Tier);
    }

    [Fact]
    public async Task GetForUser_Creator_ReturnsCreatorWritingLimit()
    {
        var benefits = await _service.GetForUserAsync(20);

        Assert.Equal((PlanBenefitsDto.CreatorTier, 20), (benefits.Tier, benefits.WritingAssistPerDay));
    }

    [Fact]
    public async Task GetForUser_Creator_KeepsFreeReadingBenefits_SoOwnChildrenAreNotViewOnly()
    {
        var benefits = await _service.GetForUserAsync(20);

        Assert.Equal((1, (int?)3, false), (benefits.MaxChildProfiles, benefits.WeeklyNewLibraryStories, benefits.CanCopyRoadmapTemplates));
    }

    [Fact]
    public async Task GetForUser_UsesCurrentPlanRow_AfterAdminEdit()
    {
        var plan = GivenCycle(SubscriptionStatus.Active, Now.AddDays(-3), Now.AddDays(27));
        plan.MaxChildProfiles = 2;

        Assert.Equal(2, (await _service.GetForUserAsync(10)).MaxChildProfiles);
    }

    [Fact]
    public async Task GetForUser_FreeUsesConfiguredSettings()
    {
        _uow.Seed(new PlanSettings { Id = PlanSettings.SingletonId, FreeMaxChildProfiles = 2, FreeWeeklyNewLibraryStories = null });

        var benefits = await _service.GetForUserAsync(10);

        Assert.Equal((2, (int?)null), (benefits.MaxChildProfiles, benefits.WeeklyNewLibraryStories));
    }
}
