using StoryPlatform.Application.Features.PlanBenefits.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.PlanBenefits;

public sealed class PlanUsageServiceTests
{
    private const int OwnerId = 7;
    // Thứ Tư 11/11/2026 10:00 giờ Việt Nam; tuần bắt đầu 09/11 00:00 VN = 08/11 17:00Z.
    private static readonly DateTime Now = new(2026, 11, 11, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WeekStart = new(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly PlanUsageService _sut;
    private int _nextId = 1;

    public PlanUsageServiceTests()
    {
        _uow.Seed(new ChildProfile { Id = 1, OwnerUserId = OwnerId, Status = ChildProfileStatus.Active });
        _uow.Seed(new ChildProfile { Id = 2, OwnerUserId = OwnerId, Status = ChildProfileStatus.Active });
        _uow.Seed(new ChildProfile { Id = 3, OwnerUserId = 99, Status = ChildProfileStatus.Active });
        foreach (var id in new[] { 10, 11, 12, 13 })
            _uow.Seed(new Story { Id = id, Scope = StoryScope.Creator, IsPublished = true, Status = StoryStatus.Ready });
        _uow.Seed(new Story { Id = 20, Scope = StoryScope.Parent, Status = StoryStatus.Ready, ChildProfileId = 1 });
        _sut = new PlanUsageService(_uow);
    }

    private void Session(int childId, int storyId, DateTime startedAt, int? familyConfirmationId = null) =>
        _uow.Seed(new ReadingSession
        {
            Id = _nextId++, ChildProfileId = childId, StoryId = storyId, StartedAt = startedAt,
            FamilyConfirmationId = familyConfirmationId
        });

    [Fact]
    public async Task CountNewLibraryStories_CountsDistinctStoriesFirstOpenedThisWeek_AcrossChildren()
    {
        Session(1, 10, WeekStart.AddHours(1));
        Session(2, 10, WeekStart.AddHours(2));    // cùng truyện, trẻ khác: không tính thêm
        Session(2, 11, WeekStart.AddHours(3));
        Session(1, 11, WeekStart.AddHours(4));    // đọc lại

        Assert.Equal(2, await _sut.CountNewLibraryStoriesThisWeekAsync(OwnerId, Now));
    }

    [Fact]
    public async Task CountNewLibraryStories_StoryFirstOpenedLastWeek_NotCounted()
    {
        Session(1, 10, WeekStart.AddMinutes(-1));
        Session(1, 10, WeekStart.AddHours(5));

        Assert.Equal(0, await _sut.CountNewLibraryStoriesThisWeekAsync(OwnerId, Now));
    }

    [Fact]
    public async Task CountNewLibraryStories_IgnoresParentStoriesFamilyConfirmationsAndOtherAccounts()
    {
        Session(1, 20, WeekStart.AddHours(1));
        Session(1, 12, WeekStart.AddHours(1), familyConfirmationId: 5);
        Session(3, 13, WeekStart.AddHours(1));

        Assert.Equal(0, await _sut.CountNewLibraryStoriesThisWeekAsync(OwnerId, Now));
    }

    [Fact]
    public async Task Count_ResetsAtMondayVietnam()
    {
        Session(1, 10, WeekStart.AddHours(1));

        Assert.Equal(1, await _sut.CountNewLibraryStoriesThisWeekAsync(OwnerId, WeekStart.AddDays(7).AddMinutes(-1)));
        Assert.Equal(0, await _sut.CountNewLibraryStoriesThisWeekAsync(OwnerId, WeekStart.AddDays(7)));
    }

    [Fact]
    public async Task HasOpenedLibraryStory_TrueWhenAnotherChildOpenedIt()
    {
        Session(2, 10, WeekStart.AddDays(-30));

        Assert.True(await _sut.HasOpenedLibraryStoryAsync(OwnerId, 10));
        Assert.False(await _sut.HasOpenedLibraryStoryAsync(OwnerId, 11));
    }

    [Fact]
    public async Task CountWritingAssistsToday_CountsTasksSinceVietnamMidnight()
    {
        var todayStart = new DateTime(2026, 11, 10, 17, 0, 0, DateTimeKind.Utc);
        _uow.Seed(new StoryAssistanceTask { Id = 1, OwnerUserId = OwnerId, CreatedAt = todayStart.AddMinutes(-1) });
        _uow.Seed(new StoryAssistanceTask { Id = 2, OwnerUserId = OwnerId, CreatedAt = todayStart });
        _uow.Seed(new StoryAssistanceTask { Id = 3, OwnerUserId = OwnerId, CreatedAt = Now });
        _uow.Seed(new StoryAssistanceTask { Id = 4, OwnerUserId = 99, CreatedAt = Now });

        Assert.Equal(2, await _sut.CountWritingAssistsTodayAsync(OwnerId, Now));
    }
}
