using System.Linq.Expressions;
using System.Text.Json;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Profiles;

public class ChildProfileServiceTests
{
    private readonly Mock<IGenericRepository<UserAccount>> _userRepo = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IGenericRepository<SupervisionRelationship>> _supervisionRepo = new();
    private readonly Mock<IGenericRepository<Organization>> _organizationRepo = new();
    private readonly Mock<IGenericRepository<ClassGroup>> _classGroupRepo = new();
    private readonly Mock<IGenericRepository<ClassGroupMember>> _memberRepo = new();
    private readonly Mock<IGenericRepository<LearningProfile>> _learningProfileRepo = new();
    private readonly Mock<IGenericRepository<SafetyPolicy>> _safetyPolicyRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISupervisionAccessGuard> _accessGuard = new();
    private readonly ChildProfileService _sut;

    public ChildProfileServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<UserAccount>()).Returns(_userRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionRelationship>()).Returns(_supervisionRepo.Object);
        _unitOfWork.Setup(u => u.Repository<Organization>()).Returns(_organizationRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ClassGroup>()).Returns(_classGroupRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ClassGroupMember>()).Returns(_memberRepo.Object);
        _unitOfWork.Setup(u => u.Repository<LearningProfile>()).Returns(_learningProfileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SafetyPolicy>()).Returns(_safetyPolicyRepo.Object);
        _sut = new ChildProfileService(_unitOfWork.Object, _accessGuard.Object);
    }

    [Fact]
    public async Task CreateChildProfileAsync_PersonalScope_CreatesDraftProfileAndOwnerSupervisionInOneSave()
    {
        SetupOwner();
        ChildProfile? profile = null;
        SupervisionRelationship? supervision = null;
        _profileRepo.Setup(r => r.AddAsync(It.IsAny<ChildProfile>(), It.IsAny<CancellationToken>()))
            .Callback<ChildProfile, CancellationToken>((value, _) => profile = value)
            .ReturnsAsync((ChildProfile value, CancellationToken _) => value);
        _supervisionRepo.Setup(r => r.AddAsync(It.IsAny<SupervisionRelationship>(), It.IsAny<CancellationToken>()))
            .Callback<SupervisionRelationship, CancellationToken>((value, _) => supervision = value)
            .ReturnsAsync((SupervisionRelationship value, CancellationToken _) => value);

        var result = await _sut.CreateChildProfileAsync(2, new CreateChildProfileRequestDto
        {
            Nickname = "  Bé An  ",
            AgeBand = AgeBand.Age_6_8
        });

        Assert.NotNull(profile);
        Assert.Equal(2, profile!.OwnerUserId);
        Assert.Equal("Bé An", profile.Nickname);
        Assert.Equal(AgeBand.Age_6_8, profile.AgeBand);
        Assert.Equal("vi", profile.Language);
        Assert.Equal(ChildProfileStatus.Draft, profile.Status);
        Assert.Equal(ProfileScope.Personal, profile.Scope);
        Assert.Null(profile.OrganizationId);
        Assert.NotNull(supervision);
        Assert.Same(profile, supervision!.ChildProfile);
        Assert.Equal(2, supervision.SupervisorUserId);
        Assert.Equal(SupervisorRole.Owner, supervision.SupervisorRole);
        Assert.Null(supervision.RevokedAt);
        Assert.Equal("Draft", result.Status);
        Assert.Equal("Personal", result.Scope);
        _memberRepo.Verify(r => r.AddAsync(It.IsAny<ClassGroupMember>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateChildProfileAsync_BlankLanguage_DefaultsToVietnamese()
    {
        SetupOwner();
        ChildProfile? profile = null;
        _profileRepo.Setup(r => r.AddAsync(It.IsAny<ChildProfile>(), It.IsAny<CancellationToken>()))
            .Callback<ChildProfile, CancellationToken>((value, _) => profile = value)
            .ReturnsAsync((ChildProfile value, CancellationToken _) => value);

        var request = PersonalRequest();
        request.Language = "  ";

        await _sut.CreateChildProfileAsync(2, request);

        Assert.Equal("vi", profile!.Language);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(null, 5)]
    [InlineData(1, 5)]
    public async Task CreateChildProfileAsync_PersonalScopeWithOrganizationData_ThrowsBadRequest(
        int? organizationId, int? classGroupId)
    {
        SetupOwner();
        var request = PersonalRequest();
        request.OrganizationId = organizationId;
        request.ClassGroupId = classGroupId;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateChildProfileAsync(2, request));

        VerifyNothingWasSaved();
    }

    [Fact]
    public async Task CreateChildProfileAsync_UnknownOwner_ThrowsNotFound()
    {
        _userRepo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateChildProfileAsync(999, PersonalRequest()));

        VerifyNothingWasSaved();
    }

    [Fact]
    public async Task CreateChildProfileAsync_OrganizationScope_MissingOrganizationOrClassGroup_ThrowsBadRequest()
    {
        SetupOwner();
        var request = OrganizationRequest();
        request.ClassGroupId = null;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateChildProfileAsync(2, request));

        VerifyNothingWasSaved();
    }

    [Fact]
    public async Task CreateChildProfileAsync_OrganizationScope_OrganizationNotFound_ThrowsNotFound()
    {
        SetupOwner();
        _organizationRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateChildProfileAsync(2, OrganizationRequest()));

        VerifyNothingWasSaved();
    }

    [Theory]
    [InlineData(OrgVerification.PendingVerification)]
    [InlineData(OrgVerification.Suspended)]
    [InlineData(OrgVerification.PendingReverification)]
    [InlineData(OrgVerification.Rejected)]
    public async Task CreateChildProfileAsync_OrganizationScope_OrganizationNotActive_ThrowsBadRequest(OrgVerification status)
    {
        SetupOwner();
        _organizationRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization
            {
                Id = 1,
                Name = "Trường Demo",
                VerificationStatus = status,
                CreatedByUserId = 1
            });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateChildProfileAsync(2, OrganizationRequest()));

        VerifyNothingWasSaved();
    }

    [Fact]
    public async Task CreateChildProfileAsync_OrganizationScope_ClassGroupNotFound_ThrowsNotFound()
    {
        SetupOrganizationBranch();
        _classGroupRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync((ClassGroup?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateChildProfileAsync(2, OrganizationRequest()));

        VerifyNothingWasSaved();
    }

    [Fact]
    public async Task CreateChildProfileAsync_OrganizationScope_ClassGroupFromOtherOrganization_ThrowsBadRequest()
    {
        var classGroup = ActiveClassGroup();
        classGroup.OrganizationId = 99;
        SetupOrganizationBranch(classGroup);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateChildProfileAsync(2, OrganizationRequest()));

        VerifyNothingWasSaved();
    }

    [Fact]
    public async Task CreateChildProfileAsync_OrganizationScope_ArchivedClassGroup_ThrowsBadRequest()
    {
        var classGroup = ActiveClassGroup();
        classGroup.Status = ClassGroupStatus.Archived;
        SetupOrganizationBranch(classGroup);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateChildProfileAsync(2, OrganizationRequest()));

        VerifyNothingWasSaved();
    }

    [Fact]
    public async Task CreateChildProfileAsync_OrganizationScope_CreatesAllEntitiesInOneSave()
    {
        var classGroup = ActiveClassGroup();
        SetupOrganizationBranch(classGroup);
        ChildProfile? profile = null;
        SupervisionRelationship? supervision = null;
        ClassGroupMember? member = null;
        _profileRepo.Setup(r => r.AddAsync(It.IsAny<ChildProfile>(), It.IsAny<CancellationToken>()))
            .Callback<ChildProfile, CancellationToken>((value, _) => profile = value)
            .ReturnsAsync((ChildProfile value, CancellationToken _) => value);
        _supervisionRepo.Setup(r => r.AddAsync(It.IsAny<SupervisionRelationship>(), It.IsAny<CancellationToken>()))
            .Callback<SupervisionRelationship, CancellationToken>((value, _) => supervision = value)
            .ReturnsAsync((SupervisionRelationship value, CancellationToken _) => value);
        _memberRepo.Setup(r => r.AddAsync(It.IsAny<ClassGroupMember>(), It.IsAny<CancellationToken>()))
            .Callback<ClassGroupMember, CancellationToken>((value, _) => member = value)
            .ReturnsAsync((ClassGroupMember value, CancellationToken _) => value);

        var result = await _sut.CreateChildProfileAsync(2, OrganizationRequest());

        Assert.Equal(1, profile!.OrganizationId);
        Assert.Equal(ProfileScope.Organization, profile.Scope);
        Assert.Same(profile, supervision!.ChildProfile);
        Assert.Same(profile, member!.ChildProfile);
        Assert.Same(classGroup, member.ClassGroup);
        Assert.True(member.JoinedAt <= DateTime.UtcNow);
        Assert.Equal("Organization", result.Scope);
        Assert.Equal(1, result.OrganizationId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData((ProfileScope)999, AgeBand.Age_6_8)]
    [InlineData(ProfileScope.Personal, (AgeBand)999)]
    public async Task CreateChildProfileAsync_InvalidEnum_ThrowsBadRequest(ProfileScope scope, AgeBand ageBand)
    {
        SetupOwner();
        var request = PersonalRequest();
        request.Scope = scope;
        request.AgeBand = ageBand;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateChildProfileAsync(2, request));

        VerifyNothingWasSaved();
    }

    [Fact]
    public void CreateChildProfileRequestDto_AcceptsDocumentedStringEnums()
    {
        var request = JsonSerializer.Deserialize<CreateChildProfileRequestDto>(
            """{"Nickname":"Bé Test","AgeBand":"Age_6_8","Scope":"Personal"}""");

        Assert.NotNull(request);
        Assert.Equal(AgeBand.Age_6_8, request!.AgeBand);
        Assert.Equal(ProfileScope.Personal, request.Scope);
    }

    [Fact]
    public async Task ActivateChildProfileAsync_WithoutSupervision_ThrowsForbidden()
    {
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2 });
        _accessGuard.Setup(g => g.EnsureActiveSupervisionAsync(
                1, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.ActivateChildProfileAsync(1, 99));
    }

    [Theory]
    [InlineData(false, true, "Learning Profile")]
    [InlineData(true, false, "Safety Policy")]
    public async Task ActivateChildProfileAsync_MissingRequiredSetup_ThrowsBadRequest(
        bool hasLearning, bool hasSafety, string expectedMessage)
    {
        SetupActivation(hasLearning, hasSafety, hasParentSupervisor: true);

        var exception = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ActivateChildProfileAsync(1, 2));

        Assert.Contains(expectedMessage, exception.Message);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, ChildProfileStatus.Active)]
    [InlineData(false, ChildProfileStatus.PendingParentConsent)]
    public async Task ActivateChildProfileAsync_AppliesBr19(
        bool hasParentSupervisor, ChildProfileStatus expectedStatus)
    {
        var profile = SetupActivation(true, true, hasParentSupervisor);

        var result = await _sut.ActivateChildProfileAsync(1, 2);

        Assert.Equal(expectedStatus, profile.Status);
        Assert.Equal(expectedStatus.ToString(), result.Status);
        _profileRepo.Verify(r => r.Update(profile), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActivateChildProfileAsync_ArchivedProfile_ThrowsBadRequest()
    {
        var profile = new ChildProfile
        {
            Id = 1,
            OwnerUserId = 2,
            Status = ChildProfileStatus.Archived
        };
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        AllowProfileAccess();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ActivateChildProfileAsync(1, 2));
    }

    [Fact]
    public async Task ListMyChildProfilesAsync_ReturnsRepositoryResult()
    {
        var profiles = new List<ChildProfile>
        {
            new() { Id = 1, OwnerUserId = 2, Nickname = "Bé An" },
            new() { Id = 2, OwnerUserId = 2, Nickname = "Bé Bình" }
        };
        _profileRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<ChildProfile, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(profiles);

        var result = await _sut.ListMyChildProfilesAsync(2);

        Assert.Equal(2, result.Count);
        Assert.All(result, value => Assert.Equal(2, value.OwnerUserId));
    }

    [Fact]
    public async Task GetChildProfileByIdAsync_NoActiveSupervision_ThrowsForbidden()
    {
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2, Nickname = "Bé An" });
        _accessGuard.Setup(g => g.EnsureActiveSupervisionAsync(1, 9, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetChildProfileByIdAsync(1, 9));
    }

    [Fact]
    public async Task GetChildProfileByIdAsync_Valid_ReturnsDto()
    {
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2, Nickname = "Bé An", Status = ChildProfileStatus.Active });
        AllowProfileAccess();

        var result = await _sut.GetChildProfileByIdAsync(1, 2);

        Assert.Equal("Bé An", result.Nickname);
    }

    [Fact]
    public async Task UpdateChildProfileAsync_Valid_UpdatesFields()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Nickname = "Cũ", AgeBand = AgeBand.Age_6_8, Status = ChildProfileStatus.Active };
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        AllowProfileAccess();

        var result = await _sut.UpdateChildProfileAsync(1, 2, new UpdateChildProfileRequestDto
        {
            Nickname = "Bé Mới",
            AgeBand = AgeBand.Age_9_12,
            Language = "vi"
        });

        Assert.Equal("Bé Mới", profile.Nickname);
        Assert.Equal(AgeBand.Age_9_12, profile.AgeBand);
        Assert.Equal("Bé Mới", result.Nickname);
        _profileRepo.Verify(r => r.Update(profile), Times.Once);
    }

    [Fact]
    public async Task ArchiveChildProfileAsync_CallerNotOwner_ThrowsForbidden()
    {
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active });
        _accessGuard.Setup(g => g.EnsureOwnerAsync(1, 5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không phải Owner."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ArchiveChildProfileAsync(1, 5));
    }

    [Fact]
    public async Task ArchiveChildProfileAsync_Owner_SetsStatusArchived()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active };
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        _accessGuard.Setup(g => g.EnsureOwnerAsync(1, 2, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ArchiveChildProfileAsync(1, 2);

        Assert.Equal(ChildProfileStatus.Archived, profile.Status);
    }

    [Fact]
    public async Task ArchiveChildProfileAsync_AlreadyArchived_IsIdempotent()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Archived };
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        _accessGuard.Setup(g => g.EnsureOwnerAsync(1, 2, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ArchiveChildProfileAsync(1, 2);

        _profileRepo.Verify(r => r.Update(profile), Times.Never);
    }

    private ChildProfile SetupActivation(
        bool hasLearning, bool hasSafety, bool hasParentSupervisor)
    {
        var profile = new ChildProfile
        {
            Id = 1,
            OwnerUserId = 2,
            Status = ChildProfileStatus.Draft
        };
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _learningProfileRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(hasLearning);
        _safetyPolicyRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(hasSafety);
        _supervisionRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(hasParentSupervisor);
        AllowProfileAccess();
        return profile;
    }

    private void AllowProfileAccess() => _accessGuard
        .Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SupervisionRelationship());

    private void SetupOwner() => _userRepo
        .Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new UserAccount { Id = 2, Role = UserRole.Parent, Status = AccountStatus.EmailVerified });

    private void SetupOrganizationBranch(ClassGroup? classGroup = null)
    {
        SetupOwner();
        _organizationRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveOrganization());
        _classGroupRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(classGroup ?? ActiveClassGroup());
    }

    private void VerifyNothingWasSaved()
    {
        _profileRepo.Verify(r => r.AddAsync(It.IsAny<ChildProfile>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateChildProfileRequestDto PersonalRequest() => new()
    {
        Nickname = "Bé Test",
        AgeBand = AgeBand.Age_6_8,
        Scope = ProfileScope.Personal
    };

    private static CreateChildProfileRequestDto OrganizationRequest() => new()
    {
        Nickname = "Bé Test",
        AgeBand = AgeBand.Age_9_12,
        Scope = ProfileScope.Organization,
        OrganizationId = 1,
        ClassGroupId = 5
    };

    private static Organization ActiveOrganization() => new()
    {
        Id = 1,
        Name = "Trường Demo",
        VerificationStatus = OrgVerification.Active,
        CreatedByUserId = 1
    };

    private static ClassGroup ActiveClassGroup() => new()
    {
        Id = 5,
        OrganizationId = 1,
        TeacherUserId = 7,
        Name = "Lớp 1A",
        Status = ClassGroupStatus.Active
    };
}
