using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Services;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Sessions;

public class ChildSessionRevokerTests
{
    private readonly Mock<IGenericRepository<ChildSession>> _sessionRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ChildSessionRevoker _sut;
    private Expression<Func<ChildSession, bool>>? _predicate;

    public ChildSessionRevokerTests()
    {
        _unitOfWork.Setup(u => u.Repository<ChildSession>()).Returns(_sessionRepo.Object);
        _sut = new ChildSessionRevoker(_unitOfWork.Object);
    }

    [Fact]
    public async Task RevokeByChildProfileAsync_MarksEverySessionDeletedAndMatchesOnlyThatChild()
    {
        var sessions = new List<ChildSession>
        {
            new() { Id = 1, ChildProfileId = 7, ChildAccessCredentialId = 3 },
            new() { Id = 2, ChildProfileId = 7, SupervisorSessionId = 9 }
        };
        SetupFind(sessions);

        var count = await _sut.RevokeByChildProfileAsync(7);

        Assert.Equal(2, count);
        Assert.All(sessions, session => Assert.True(session.IsDeleted));
        _sessionRepo.Verify(repository => repository.Update(It.IsAny<ChildSession>()), Times.Exactly(2));
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var matches = _predicate!.Compile();
        Assert.True(matches(new ChildSession { ChildProfileId = 7 }));
        Assert.False(matches(new ChildSession { ChildProfileId = 8 }));
    }

    [Fact]
    public async Task RevokeBySupervisorAsync_WithChild_MatchesOnlySupervisorSessionsOfThatChild()
    {
        SetupFind(new List<ChildSession>());

        await _sut.RevokeBySupervisorAsync(5, 7);

        var matches = _predicate!.Compile();
        Assert.True(matches(Session(childId: 7, supervisorUserId: 5)));
        Assert.False(matches(Session(childId: 8, supervisorUserId: 5)));
        Assert.False(matches(Session(childId: 7, supervisorUserId: 6)));
        Assert.False(matches(new ChildSession { ChildProfileId = 7, ChildAccessCredentialId = 3 }));
    }

    [Fact]
    public async Task RevokeBySupervisorAsync_WithoutChild_MatchesAllChildrenButNeverEasyLogin()
    {
        SetupFind(new List<ChildSession>());

        await _sut.RevokeBySupervisorAsync(5, null);

        var matches = _predicate!.Compile();
        Assert.True(matches(Session(childId: 7, supervisorUserId: 5)));
        Assert.True(matches(Session(childId: 8, supervisorUserId: 5)));
        Assert.False(matches(new ChildSession { ChildProfileId = 7, ChildAccessCredentialId = 3 }));
    }

    private static ChildSession Session(int childId, int supervisorUserId) => new()
    {
        ChildProfileId = childId,
        SupervisorSessionId = 99,
        SupervisorSession = new RefreshToken { Id = 99, UserAccountId = supervisorUserId }
    };

    private void SetupFind(List<ChildSession> sessions) => _sessionRepo
        .Setup(repository => repository.FindAsync(
            It.IsAny<Expression<Func<ChildSession, bool>>>(), null, It.IsAny<CancellationToken>()))
        .Callback<Expression<Func<ChildSession, bool>>, string?, CancellationToken>(
            (predicate, _, _) => _predicate = predicate)
        .ReturnsAsync((IReadOnlyList<ChildSession>)sessions);
}
