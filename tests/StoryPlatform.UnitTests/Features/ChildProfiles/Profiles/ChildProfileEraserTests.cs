using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Profiles;

public class ChildProfileEraserTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IGenericRepository<ChildAccessCredential>> _credentialRepo = new();
    private readonly Mock<IGenericRepository<AssignmentRecipient>> _recipientRepo = new();
    private readonly Mock<IGenericRepository<Assignment>> _assignmentRepo = new();
    private readonly Mock<IChildSessionRevoker> _revoker = new();
    private readonly ChildProfileEraser _sut;

    public ChildProfileEraserTests()
    {
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildAccessCredential>()).Returns(_credentialRepo.Object);
        _unitOfWork.Setup(u => u.Repository<AssignmentRecipient>()).Returns(_recipientRepo.Object);
        _unitOfWork.Setup(u => u.Repository<Assignment>()).Returns(_assignmentRepo.Object);
        SetupAssignments(new List<AssignmentRecipient>(), new List<Assignment>());
        _sut = new ChildProfileEraser(_unitOfWork.Object, _revoker.Object);
    }

    [Fact]
    public async Task EraseAsync_AnonymizesAndRevokesAccess()
    {
        var credential = new ChildAccessCredential { ChildProfileId = 5, EasyLoginSecretHash = "x" };
        _credentialRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
            It.IsAny<CancellationToken>())).ReturnsAsync(credential);
        var child = Child();

        await _sut.EraseAsync(child, 7, Now);

        Assert.Equal(ChildProfileEraser.AnonymizedNickname, child.Nickname);
        Assert.Equal(ChildProfileStatus.Deleted, child.Status);
        Assert.True(child.IsDeleted);
        Assert.Equal(Now, credential.RevokedAt);
        Assert.Null(credential.EasyLoginSecretHash);
        _revoker.Verify(r => r.RevokeByChildProfileAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        _profileRepo.Verify(r => r.Update(child), Times.Once);
    }

    [Fact]
    public async Task EraseAsync_CancelsOpenAssignmentsAndKeepsCancelledOnes()
    {
        var openRecipient = new AssignmentRecipient { Id = 1, ChildProfileId = 5, Status = AssignmentStatus.Assigned };
        var cancelledRecipient = new AssignmentRecipient
        {
            Id = 2, ChildProfileId = 5, Status = AssignmentStatus.Cancelled, CancelledByUserId = 3
        };
        var openAssignment = new Assignment { Id = 10, ChildProfileId = 5, Status = AssignmentStatus.Assigned };
        SetupAssignments(
            new List<AssignmentRecipient> { openRecipient, cancelledRecipient },
            new List<Assignment> { openAssignment });

        await _sut.EraseAsync(Child(), 7, Now);

        Assert.Equal(AssignmentStatus.Cancelled, openRecipient.Status);
        Assert.Equal(Now, openRecipient.CancelledAt);
        Assert.Equal(7, openRecipient.CancelledByUserId);
        Assert.Equal(3, cancelledRecipient.CancelledByUserId);
        Assert.Equal(AssignmentStatus.Cancelled, openAssignment.Status);
        _recipientRepo.Verify(r => r.Update(cancelledRecipient), Times.Never);
    }

    private static ChildProfile Child() =>
        new() { Id = 5, OwnerUserId = 7, Nickname = "Bin", Status = ChildProfileStatus.Active };

    private void SetupAssignments(List<AssignmentRecipient> recipients, List<Assignment> assignments)
    {
        _recipientRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<AssignmentRecipient, bool>>>(), null,
            It.IsAny<CancellationToken>())).ReturnsAsync(recipients);
        _assignmentRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Assignment, bool>>>(), null,
            It.IsAny<CancellationToken>())).ReturnsAsync(assignments);
    }
}
