using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Profiles;

public class ChildProfileAccessGuardTests
{
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly ChildProfileAccessGuard _sut;

    public ChildProfileAccessGuardTests()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _sut = new ChildProfileAccessGuard(unitOfWork.Object);
    }

    [Fact]
    public async Task EnsureOwnerAsync_Owner_ReturnsProfile()
    {
        SetupProfile(new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active });
        Assert.Equal(1, (await _sut.EnsureOwnerAsync(1, 2)).Id);
    }

    [Fact]
    public async Task EnsureOwnerAsync_OtherUser_ThrowsForbidden()
    {
        SetupProfile(new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active });
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureOwnerAsync(1, 3));
    }

    [Fact]
    public async Task EnsureOwnerAsync_MissingProfile_ThrowsForbidden()
    {
        SetupProfile(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureOwnerAsync(1, 2));
    }

    [Fact]
    public async Task EnsureOwnerAsync_DeletedProfile_ThrowsForbidden()
    {
        SetupProfile(new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Deleted });
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureOwnerAsync(1, 2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsOwnerAsync_ReturnsRepositoryAnswer(bool exists)
    {
        _profileRepo.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<ChildProfile, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists);
        Assert.Equal(exists, await _sut.IsOwnerAsync(1, 2));
    }

    private void SetupProfile(ChildProfile? profile) => _profileRepo
        .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
}
