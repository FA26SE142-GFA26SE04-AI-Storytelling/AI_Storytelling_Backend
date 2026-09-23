using System.Linq.Expressions;
using System.Text;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.ClassGroups;

public class ClassGroupServiceTests
{
    private readonly Mock<IGenericRepository<ClassGroup>> _classGroupRepo = new();
    private readonly Mock<IGenericRepository<ClassGroupMember>> _memberRepo = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IGenericRepository<SharedStory>> _sharedStoryRepo = new();
    private readonly Mock<IGenericRepository<SharedStoryRecipient>> _recipientRepo = new();
    private readonly Mock<IGenericRepository<SupervisionRelationship>> _supervisionRelationshipRepo = new();
    private readonly Mock<IGenericRepository<SupervisionInvitation>> _invitationRepo = new();
    private readonly Mock<IJwtTokenGenerator> _tokenGenerator = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ClassGroupService _sut;

    public ClassGroupServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<ClassGroup>()).Returns(_classGroupRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ClassGroupMember>()).Returns(_memberRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SharedStory>()).Returns(_sharedStoryRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SharedStoryRecipient>()).Returns(_recipientRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionRelationship>()).Returns(_supervisionRelationshipRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionInvitation>()).Returns(_invitationRepo.Object);
        _sut = new ClassGroupService(_unitOfWork.Object, _tokenGenerator.Object);
    }

    [Fact]
    public async Task CreateClassGroupAsync_Valid_CreatesActiveClass()
    {
        ClassGroup? added = null;
        _classGroupRepo.Setup(r => r.AddAsync(It.IsAny<ClassGroup>(), It.IsAny<CancellationToken>()))
            .Callback<ClassGroup, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((ClassGroup value, CancellationToken _) => value);

        var result = await _sut.CreateClassGroupAsync(
            7, new CreateClassGroupRequestDto { Name = " Lớp 1A " });

        Assert.Equal(7, added!.TeacherUserId);
        Assert.Equal("Lớp 1A", added.Name);
        Assert.Equal(ClassGroupStatus.Active, added.Status);
        Assert.Equal("Active", result.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddChildToClassGroupAsync_NonOwningTeacher_ThrowsForbidden()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveClass());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.AddChildToClassGroupAsync(1, 3, 99));
    }

    [Theory]
    [InlineData(ChildProfileStatus.Draft)]
    [InlineData(ChildProfileStatus.PendingParentConsent)]
    [InlineData(ChildProfileStatus.Archived)]
    public async Task AddChildToClassGroupAsync_ProfileNotActive_ThrowsBadRequest(
        ChildProfileStatus status)
    {
        SetupGroupAndProfile(status);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AddChildToClassGroupAsync(1, 3, 7));
    }

    [Fact]
    public async Task AddChildToClassGroupAsync_ExistingMembership_ThrowsBadRequest()
    {
        SetupGroupAndProfile(ChildProfileStatus.Active);
        _memberRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<ClassGroupMember, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AddChildToClassGroupAsync(1, 3, 7));
    }

    [Fact]
    public async Task AddChildToClassGroupAsync_Valid_CreatesMembershipAndBackfillsApprovedShares()
    {
        SetupGroupAndProfile(ChildProfileStatus.Active);
        _memberRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<ClassGroupMember, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _sharedStoryRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SharedStory, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SharedStory>
            {
                new() { Id = 50, ClassGroupId = 1, TeacherStatus = TeacherShareStatus.Approved }
            });
        _recipientRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SharedStoryRecipient, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _sut.AddChildToClassGroupAsync(1, 3, 7);

        _memberRepo.Verify(r => r.AddAsync(
            It.Is<ClassGroupMember>(m => m.ClassGroupId == 1 && m.ChildProfileId == 3),
            It.IsAny<CancellationToken>()), Times.Once);
        _recipientRepo.Verify(r => r.AddAsync(
            It.Is<SharedStoryRecipient>(value => value.SharedStoryId == 50
                                                   && value.RecipientUserId == 9
                                                   && value.Status == RecipientStatus.Pending),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddChildToClassGroupAsync_ExistingRecipient_DoesNotDuplicateBackfill()
    {
        SetupGroupAndProfile(ChildProfileStatus.Active);
        _sharedStoryRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SharedStory, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SharedStory> { new() { Id = 50 } });
        _recipientRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SharedStoryRecipient, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _sut.AddChildToClassGroupAsync(1, 3, 7);

        _recipientRepo.Verify(r => r.AddAsync(
            It.IsAny<SharedStoryRecipient>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetClassGroupByIdAsync_NotOwningTeacher_ThrowsForbidden()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveClass());

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetClassGroupByIdAsync(1, 99));
    }

    [Fact]
    public async Task ListMyClassGroupsAsync_ReturnsOnlyGroupsOwnedByCaller()
    {
        _classGroupRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<ClassGroup, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClassGroup> { ActiveClass() });

        var result = await _sut.ListMyClassGroupsAsync(7);

        Assert.Single(result);
    }

    [Fact]
    public async Task UpdateClassGroupAsync_Valid_RenamesGroup()
    {
        var classGroup = ActiveClass();
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(classGroup);

        var result = await _sut.UpdateClassGroupAsync(1, 7, new UpdateClassGroupRequestDto { Name = "Lớp 1A - Mới" });

        Assert.Equal("Lớp 1A - Mới", classGroup.Name);
        Assert.Equal("Lớp 1A - Mới", result.Name);
    }

    [Fact]
    public async Task ArchiveClassGroupAsync_NotOwningTeacher_ThrowsForbidden()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveClass());

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ArchiveClassGroupAsync(1, 99));
    }

    [Fact]
    public async Task ArchiveClassGroupAsync_OwningTeacher_SetsStatusArchived()
    {
        var classGroup = ActiveClass();
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(classGroup);

        await _sut.ArchiveClassGroupAsync(1, 7);

        Assert.Equal(ClassGroupStatus.Archived, classGroup.Status);
    }

    [Fact]
    public async Task RemoveChildFromClassGroupAsync_ExistingMember_DeletesRowHard()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveClass());
        var membership = new ClassGroupMember { Id = 30, ClassGroupId = 1, ChildProfileId = 3 };
        _memberRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ClassGroupMember, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        await _sut.RemoveChildFromClassGroupAsync(1, 3, 7);

        _memberRepo.Verify(r => r.Delete(membership), Times.Once);
    }

    [Fact]
    public async Task ListClassGroupMembersAsync_NotOwningTeacher_ThrowsForbidden()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveClass());

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListClassGroupMembersAsync(1, 99));
    }

    [Fact]
    public async Task ListClassGroupMembersAsync_Valid_ReturnsChildProfilesInGroup()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveClass());
        _memberRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<ClassGroupMember, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClassGroupMember> { new() { Id = 1, ClassGroupId = 1, ChildProfileId = 3 } });
        _profileRepo.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 3, OwnerUserId = 9, Nickname = "Bé Dạt", Status = ChildProfileStatus.Active });

        var result = await _sut.ListClassGroupMembersAsync(1, 7);

        Assert.Single(result);
        Assert.Equal("Bé Dạt", result[0].Nickname);
    }

    [Fact]
    public async Task BulkEnrollAsync_NotClassOwner_ThrowsForbidden()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassGroup { Id = 1, TeacherUserId = 99, Status = ClassGroupStatus.Active });
        var csv = Encoding.UTF8.GetBytes("Nickname,AgeBand,Language,InviteeEmail\nBé An,Age_6_8,vi,");

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.BulkEnrollAsync(1, 2, csv));
    }

    [Fact]
    public async Task BulkEnrollAsync_ArchivedClassGroup_ThrowsBadRequest()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassGroup { Id = 1, TeacherUserId = 2, Status = ClassGroupStatus.Archived });
        var csv = Encoding.UTF8.GetBytes("Nickname,AgeBand,Language,InviteeEmail\nBé An,Age_6_8,vi,");

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.BulkEnrollAsync(1, 2, csv));
    }

    [Fact]
    public async Task BulkEnrollAsync_ValidRow_CreatesProfileRelationshipMembershipAndInvitation()
    {
        var classGroup = new ClassGroup
        {
            Id = 1,
            TeacherUserId = 2,
            OrganizationId = 7,
            Status = ClassGroupStatus.Active
        };
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(classGroup);
        _tokenGenerator.Setup(generator => generator.GenerateRefreshToken()).Returns("generated-code");
        ChildProfile? addedProfile = null;
        _profileRepo.Setup(r => r.AddAsync(It.IsAny<ChildProfile>(), It.IsAny<CancellationToken>()))
            .Callback<ChildProfile, CancellationToken>((profile, _) => addedProfile = profile)
            .ReturnsAsync((ChildProfile profile, CancellationToken _) => profile);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => addedProfile!.Id = 123)
            .ReturnsAsync(1);
        var csv = Encoding.UTF8.GetBytes(
            "Nickname,AgeBand,Language,InviteeEmail\nBé An,Age_6_8,vi,parent@example.com");

        var result = await _sut.BulkEnrollAsync(1, 2, csv);

        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal("generated-code", result.Rows[0].InvitationCode);
        Assert.Equal(123, result.Rows[0].ChildProfileId);
        _profileRepo.Verify(r => r.AddAsync(
            It.Is<ChildProfile>(profile => profile.OwnerUserId == 2
                                            && profile.Nickname == "Bé An"
                                            && profile.AgeBand == AgeBand.Age_6_8
                                            && profile.Scope == ProfileScope.Organization
                                            && profile.OrganizationId == 7),
            It.IsAny<CancellationToken>()), Times.Once);
        _supervisionRelationshipRepo.Verify(r => r.AddAsync(
            It.Is<SupervisionRelationship>(relationship =>
                relationship.SupervisorUserId == 2 && relationship.SupervisorRole == SupervisorRole.Owner),
            It.IsAny<CancellationToken>()), Times.Once);
        _memberRepo.Verify(r => r.AddAsync(
            It.Is<ClassGroupMember>(member => member.ClassGroup == classGroup),
            It.IsAny<CancellationToken>()), Times.Once);
        _invitationRepo.Verify(r => r.AddAsync(
            It.Is<SupervisionInvitation>(invitation =>
                invitation.InvitationCode == "generated-code"
                && invitation.InviteeEmail == "parent@example.com"
                && invitation.Status == InvitationStatus.Pending),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkEnrollAsync_InvalidAgeBandRow_RecordsFailureWithoutCreatingProfile()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassGroup { Id = 1, TeacherUserId = 2, Status = ClassGroupStatus.Active });
        var csv = Encoding.UTF8.GetBytes("Nickname,AgeBand,Language,InviteeEmail\nBé An,invalid,vi,");

        var result = await _sut.BulkEnrollAsync(1, 2, csv);

        Assert.Equal(1, result.FailureCount);
        Assert.False(result.Rows[0].Success);
        Assert.NotNull(result.Rows[0].ErrorMessage);
        _profileRepo.Verify(r => r.AddAsync(
            It.IsAny<ChildProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BulkEnrollAsync_EmptyNicknameRow_RecordsFailureWithoutCreatingProfile()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassGroup { Id = 1, TeacherUserId = 2, Status = ClassGroupStatus.Active });
        var csv = Encoding.UTF8.GetBytes("Nickname,AgeBand,Language,InviteeEmail\n,Age_6_8,vi,");

        var result = await _sut.BulkEnrollAsync(1, 2, csv);

        Assert.Equal(1, result.FailureCount);
        Assert.False(result.Rows[0].Success);
        _profileRepo.Verify(r => r.AddAsync(
            It.IsAny<ChildProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BulkEnrollAsync_MixedValidAndInvalidRows_ContinuesProcessingAfterFailure()
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassGroup { Id = 1, TeacherUserId = 2, Status = ClassGroupStatus.Active });
        _tokenGenerator.Setup(generator => generator.GenerateRefreshToken()).Returns("generated-code");
        var csv = Encoding.UTF8.GetBytes(
            "Nickname,AgeBand,Language,InviteeEmail\n"
            + ",Age_6_8,vi,\n"
            + "Bé Bo,Age_9_12,vi,");

        var result = await _sut.BulkEnrollAsync(1, 2, csv);

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.False(result.Rows[0].Success);
        Assert.True(result.Rows[1].Success);
    }

    private void SetupGroupAndProfile(ChildProfileStatus status)
    {
        _classGroupRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveClass());
        _profileRepo.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 3, OwnerUserId = 9, Status = status });
    }

    private static ClassGroup ActiveClass() => new()
    {
        Id = 1,
        TeacherUserId = 7,
        Status = ClassGroupStatus.Active
    };
}
