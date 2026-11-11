using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.ReadingSlots;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles;

/// <summary>F-17: hồ sơ được đọc khi số hồ sơ Active vượt quyền lợi gói (Luồng 1, 1e).</summary>
public sealed class ReadingSlotServiceTests
{
    private const int OwnerId = 7;
    // Thứ Tư 11/11/2026, 10:00 giờ Việt Nam.
    private static readonly DateTime Now = new(2026, 11, 11, 3, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly Mock<IPlanBenefitService> _benefits = new();
    private readonly Mock<IChildSessionRevoker> _revoker = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly ReadingSlotService _sut;
    private int _nextSessionId = 1;

    public ReadingSlotServiceTests()
    {
        _uow.Seed(new UserAccount { Id = OwnerId, Role = UserRole.User, Status = AccountStatus.Active });
        Slots(1);
        _sut = new ReadingSlotService(_uow, _benefits.Object, _revoker.Object, _audit.Object, _clock);
    }

    private void Slots(int max) => _benefits
        .Setup(service => service.GetForUserAsync(OwnerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlanBenefitsDto { MaxChildProfiles = max });

    private ChildProfile Child(int id, int daysOld = 30, ChildProfileStatus status = ChildProfileStatus.Active) =>
        _uow.Seed(new ChildProfile
        {
            Id = id, OwnerUserId = OwnerId, Nickname = $"Bé {id}", Status = status, CreatedAt = Now.AddDays(-daysOld)
        });

    private void ReadAt(int childId, DateTime startedAt) => _uow.Seed(new ReadingSession
    {
        Id = _nextSessionId++, ChildProfileId = childId, StoryId = 1, StartedAt = startedAt
    });

    private async Task<string> ReadableAsync() => string.Join(",", (await _sut.GetReadableProfileIdsAsync(OwnerId)).Order());

    [Fact]
    public async Task Readable_WhenActiveCountWithinLimit_ReturnsAll()
    {
        Slots(4);
        Child(1); Child(2);

        Assert.Equal("1,2", await ReadableAsync());
        Assert.False((await _sut.GetAsync(OwnerId)).IsOverLimit);
    }

    [Fact]
    public async Task Readable_FreeWithTwoActive_NoSelection_PicksMostRecentReadingSession()
    {
        Child(1, daysOld: 60); Child(2, daysOld: 10);
        ReadAt(1, Now.AddDays(-5));
        ReadAt(2, Now.AddDays(-1));

        Assert.Equal("2", await ReadableAsync());
    }

    [Fact]
    public async Task Readable_NoSessionsNoSelection_PicksEarliestCreated()
    {
        Child(1, daysOld: 10); Child(2, daysOld: 60);

        Assert.Equal("2", await ReadableAsync());
    }

    [Fact]
    public async Task Readable_PrefersSelectedProfiles()
    {
        Child(1).ReadingSlotSelectedAt = Now.AddDays(-20);
        Child(2);
        ReadAt(2, Now.AddHours(-1));

        Assert.Equal("1", await ReadableAsync());
    }

    [Fact]
    public async Task Readable_IgnoresNonActiveProfiles()
    {
        Child(1, status: ChildProfileStatus.Suspended);
        Child(2);

        Assert.Equal("2", await ReadableAsync());
    }

    [Fact]
    public async Task Get_OverLimit_ListsViewOnlyProfiles()
    {
        Child(1, daysOld: 60); Child(2, daysOld: 10);

        var slots = await _sut.GetAsync(OwnerId);

        Assert.Equal((1, true, (DateTime?)null), (slots.Slots, slots.IsOverLimit, slots.NextChangeAllowedAt));
        Assert.Equal(["1:True:False", "2:False:False"],
            slots.Profiles.Select(profile => $"{profile.ChildProfileId}:{profile.CanRead}:{profile.IsSelected}"));
    }

    [Fact]
    public async Task Select_ChoosesProfile_RecordsChangeAndAudits()
    {
        Child(1, daysOld: 60); Child(2, daysOld: 10);

        var slots = await _sut.SelectAsync(OwnerId, [2]);

        Assert.Equal("2", await ReadableAsync());
        Assert.True(slots.Profiles.Single(profile => profile.ChildProfileId == 2).IsSelected);
        Assert.Equal(Now, _uow.Items<ChildProfile>().Single(profile => profile.Id == 2).ReadingSlotSelectedAt);
        Assert.Null(_uow.Items<ChildProfile>().Single(profile => profile.Id == 1).ReadingSlotSelectedAt);
        _audit.Verify(writer => writer.LogAsync(OwnerId, "ReadingSlotsChanged", nameof(UserAccount), OwnerId,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Select_MoreThanSlots_ThrowsBadRequest()
    {
        Child(1); Child(2);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SelectAsync(OwnerId, [1, 2]));
    }

    [Fact]
    public async Task Select_Empty_ThrowsBadRequest()
    {
        Child(1); Child(2);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SelectAsync(OwnerId, []));
    }

    [Fact]
    public async Task Select_ProfileNotOwnedOrNotActive_ThrowsBadRequest()
    {
        Child(1); Child(2, status: ChildProfileStatus.Archived);
        _uow.Seed(new ChildProfile { Id = 3, OwnerUserId = 99, Status = ChildProfileStatus.Active });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SelectAsync(OwnerId, [2]));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SelectAsync(OwnerId, [3]));
    }

    [Fact]
    public async Task Select_SecondTimeSameVietnamWeek_ThrowsConflictWithNextMonday()
    {
        Child(1); Child(2);
        await _sut.SelectAsync(OwnerId, [1]);

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.SelectAsync(OwnerId, [2]));

        Assert.Contains("16/11/2026", error.Message);
        Assert.Equal(new DateTime(2026, 11, 15, 17, 0, 0, DateTimeKind.Utc), (await _sut.GetAsync(OwnerId)).NextChangeAllowedAt);
        Assert.Equal("1", await ReadableAsync());
    }

    [Fact]
    public async Task Select_NextWeek_Allowed()
    {
        Child(1); Child(2);
        await _sut.SelectAsync(OwnerId, [1]);
        _clock.Advance(TimeSpan.FromHours(110));   // 17:00Z 15/11 = 00:00 thứ Hai 16/11 giờ Việt Nam

        await _sut.SelectAsync(OwnerId, [2]);

        Assert.Equal("2", await ReadableAsync());
    }

    [Fact]
    public async Task Select_RevokesChildSessionsOfProfilesLosingAccess()
    {
        Child(1, daysOld: 60); Child(2, daysOld: 10);

        await _sut.SelectAsync(OwnerId, [2]);

        _revoker.Verify(revoker => revoker.RevokeByChildProfileAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _revoker.Verify(revoker => revoker.RevokeByChildProfileAsync(2, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureCanRead_ViewOnlyProfile_ThrowsForbidden()
    {
        Child(1, daysOld: 60); Child(2, daysOld: 10);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureCanReadAsync(2));

        Assert.Contains("chỉ xem", error.Message);
        await _sut.EnsureCanReadAsync(1);
    }
}
