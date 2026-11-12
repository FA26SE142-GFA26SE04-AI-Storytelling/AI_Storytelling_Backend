using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Profiles;

/// <summary>F-17: số hồ sơ trẻ tối đa theo gói (Luồng 2, 2h).</summary>
public sealed class ChildProfilePlanLimitTests
{
    private const int OwnerId = 2;
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly Mock<IPlanBenefitService> _benefits = new();
    private readonly ChildProfileService _sut;
    private int _nextId = 100;

    public ChildProfilePlanLimitTests()
    {
        _uow.Seed(new UserAccount { Id = OwnerId, Role = UserRole.User, Status = AccountStatus.Active });
        Limit(1);
        _sut = new ChildProfileService(
            _uow, new Mock<IChildProfileAccessGuard>().Object, new Mock<IChildSessionRevoker>().Object, _benefits.Object);
    }

    private void Limit(int max) => _benefits
        .Setup(service => service.GetForUserAsync(OwnerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlanBenefitsDto { Tier = max == 1 ? PlanBenefitsDto.FreeTier : "Gói Đọc", MaxChildProfiles = max });

    private ChildProfile Profile(ChildProfileStatus status, bool deleted = false) =>
        _uow.Seed(new ChildProfile { Id = _nextId++, OwnerUserId = OwnerId, Status = status, IsDeleted = deleted });

    private Task<ChildProfileDto> CreateAsync() =>
        _sut.CreateChildProfileAsync(OwnerId, new CreateChildProfileRequestDto { Nickname = "Bé Mới", AgeBand = AgeBand.Age_6_8 });

    [Fact]
    public async Task Create_FreeAccountWithOneProfile_ThrowsConflict()
    {
        Profile(ChildProfileStatus.Active);

        var error = await Assert.ThrowsAsync<ConflictException>(CreateAsync);

        Assert.Contains("tối đa 1 hồ sơ trẻ", error.Message);
        Assert.Single(_uow.Items<ChildProfile>());
    }

    [Fact]
    public async Task Create_FreeAccountWithNoProfile_Allowed()
    {
        await CreateAsync();

        Assert.Single(_uow.Items<ChildProfile>());
    }

    [Fact]
    public async Task Create_IgnoresArchivedProfiles_CountsDraftAndSuspended()
    {
        Limit(3);
        Profile(ChildProfileStatus.Archived);
        Profile(ChildProfileStatus.Deleted);
        Profile(ChildProfileStatus.Active, deleted: true);
        Profile(ChildProfileStatus.Draft);
        Profile(ChildProfileStatus.Suspended);

        await CreateAsync();                                   // 2 hồ sơ chiếm chỗ < 3
        await Assert.ThrowsAsync<ConflictException>(CreateAsync);   // giờ đã đủ 3
    }

    [Fact]
    public async Task Create_ReadingPlanWithThreeProfiles_Allowed()
    {
        Limit(4);
        for (var i = 0; i < 3; i++) Profile(ChildProfileStatus.Active);

        await CreateAsync();

        Assert.Equal(4, _uow.Items<ChildProfile>().Count);
    }

    [Fact]
    public async Task Restore_WhenAtLimit_ThrowsConflict()
    {
        Profile(ChildProfileStatus.Active);
        var archived = Profile(ChildProfileStatus.Archived);
        _uow.Seed(new SafetyPolicy { ChildProfileId = archived.Id, ConsentRecorded = true, ConsentPolicyVersion = ConsentPolicy.CurrentVersion });

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.RestoreChildProfileAsync(archived.Id, OwnerId, new ResumeChildProfileRequestDto()));

        Assert.Equal(ChildProfileStatus.Archived, archived.Status);
    }

    [Fact]
    public async Task Restore_UnderLimit_Allowed()
    {
        Limit(2);
        Profile(ChildProfileStatus.Active);
        var archived = Profile(ChildProfileStatus.Archived);
        _uow.Seed(new SafetyPolicy { ChildProfileId = archived.Id, ConsentRecorded = true, ConsentPolicyVersion = ConsentPolicy.CurrentVersion });

        await _sut.RestoreChildProfileAsync(archived.Id, OwnerId, new ResumeChildProfileRequestDto());

        Assert.Equal(ChildProfileStatus.Active, archived.Status);
    }
}
