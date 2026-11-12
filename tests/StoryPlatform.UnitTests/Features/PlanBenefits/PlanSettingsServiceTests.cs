using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.PlanBenefits;

public sealed class PlanSettingsServiceTests
{
    private const int AdminId = 1;
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly PlanSettingsService _sut;

    public PlanSettingsServiceTests()
    {
        _uow.Seed(new PlanSettings { Id = PlanSettings.SingletonId });
        _sut = new PlanSettingsService(_uow, new PlanSettingsProvider(_uow), _audit.Object);
    }

    private static UpdatePlanSettingsRequestDto Defaults() => new()
    {
        ReadingFundPercent = 30, CreativeFundPercent = 10, CreatorMonthlyAiCredits = 2, CreatorMonthlySubmitCredits = 5,
        PartnerMonthlyAiCredits = 6, PartnerMonthlySubmitCredits = 10, MinimumReadSeconds = 60, TrialAiCredits = 1,
        FreeMaxChildProfiles = 1, FreeWeeklyNewLibraryStories = 3, FreeCanCopyRoadmapTemplates = false,
        FreeReadAloudPerMonth = 5, FreeWritingAssistPerDay = 5, CreatorWritingAssistPerDay = 20, PartnerWritingAssistPerDay = 40
    };

    [Fact]
    public async Task Get_ReturnsDefaults()
    {
        var settings = await _sut.GetAsync();

        Assert.Equal((30, 10, 2, 5, 60, 1, 1, (int?)3), (settings.ReadingFundPercent, settings.CreativeFundPercent,
            settings.CreatorMonthlyAiCredits, settings.CreatorMonthlySubmitCredits, settings.MinimumReadSeconds,
            settings.TrialAiCredits, settings.FreeMaxChildProfiles, settings.FreeWeeklyNewLibraryStories));
    }

    [Fact]
    public async Task Get_NoRow_ReturnsDefaults()
    {
        var empty = new InMemoryUnitOfWork();
        var settings = await new PlanSettingsService(empty, new PlanSettingsProvider(empty), _audit.Object).GetAsync();

        Assert.Equal(30, settings.ReadingFundPercent);
    }

    [Fact]
    public async Task Update_ValidRequest_SavesAndAuditsBeforeAfter()
    {
        object? before = null, after = null;
        _audit.Setup(writer => writer.LogAsync(AdminId, "PlanSettingsUpdated", nameof(PlanSettings), PlanSettings.SingletonId,
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<int?, string, string, int, object?, object?, CancellationToken>((_, _, _, _, b, a, _) => (before, after) = (b, a))
            .Returns(Task.CompletedTask);
        var request = Defaults();
        request.ReadingFundPercent = 25;
        request.FreeWeeklyNewLibraryStories = null;

        var result = await _sut.UpdateAsync(AdminId, request);

        var stored = Assert.Single(_uow.Items<PlanSettings>());
        Assert.Equal((25, (int?)null, 25), (stored.ReadingFundPercent, stored.FreeWeeklyNewLibraryStories, result.ReadingFundPercent));
        Assert.NotNull(stored.UpdatedAt);
        Assert.Equal(30, ((PlanSettingsDto)before!).ReadingFundPercent);
        Assert.Equal(25, ((PlanSettingsDto)after!).ReadingFundPercent);
    }

    [Fact]
    public async Task Update_NoRowYet_CreatesTheSingletonRow()
    {
        var empty = new InMemoryUnitOfWork();
        await new PlanSettingsService(empty, new PlanSettingsProvider(empty), _audit.Object).UpdateAsync(AdminId, Defaults());

        Assert.Equal(PlanSettings.SingletonId, Assert.Single(empty.Items<PlanSettings>()).Id);
    }

    public static TheoryData<Action<UpdatePlanSettingsRequestDto>> InvalidRequests => new()
    {
        r => r.ReadingFundPercent = 101,
        r => r.CreativeFundPercent = -1,
        r => r.MinimumReadSeconds = 0,
        r => r.MinimumReadSeconds = 3601,
        r => r.FreeMaxChildProfiles = 0,
        r => r.CreatorMonthlyAiCredits = -1,
        r => r.CreatorMonthlySubmitCredits = -1,
        r => r.PartnerMonthlyAiCredits = -1,
        r => r.PartnerMonthlySubmitCredits = -1,
        r => r.TrialAiCredits = -1,
        r => r.FreeWeeklyNewLibraryStories = -1,
        r => r.FreeReadAloudPerMonth = -1,
        r => r.FreeWritingAssistPerDay = -1,
        r => r.CreatorWritingAssistPerDay = -1,
        r => r.PartnerWritingAssistPerDay = -1
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Update_Rejects(Action<UpdatePlanSettingsRequestDto> change)
    {
        var request = Defaults();
        change(request);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpdateAsync(AdminId, request));
        Assert.Equal(30, Assert.Single(_uow.Items<PlanSettings>()).ReadingFundPercent);
    }
}
