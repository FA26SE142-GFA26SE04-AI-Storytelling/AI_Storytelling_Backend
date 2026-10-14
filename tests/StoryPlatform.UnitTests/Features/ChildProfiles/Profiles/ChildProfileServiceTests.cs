using System.Linq.Expressions;
using System.Text.Json;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
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
    private readonly Mock<IGenericRepository<LearningProfile>> _learningProfileRepo = new();
    private readonly Mock<IGenericRepository<SafetyPolicy>> _safetyPolicyRepo = new();
    private readonly Mock<IGenericRepository<ChildAccessCredential>> _credentialRepo = new();
    private readonly Mock<IGenericRepository<AuditLog>> _auditLogRepo = new();
    private readonly Mock<IGenericRepository<ChildProfileVersionHistory>> _historyRepo = new();
    private readonly Mock<IGenericRepository<Recommendation>> _recommendationRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISupervisionAccessGuard> _accessGuard = new();
    private readonly Mock<IChildSessionRevoker> _childSessionRevoker = new();
    private readonly ChildProfileService _sut;

    public ChildProfileServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<UserAccount>()).Returns(_userRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionRelationship>()).Returns(_supervisionRepo.Object);
        _unitOfWork.Setup(u => u.Repository<LearningProfile>()).Returns(_learningProfileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SafetyPolicy>()).Returns(_safetyPolicyRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildAccessCredential>()).Returns(_credentialRepo.Object);
        _unitOfWork.Setup(u => u.Repository<AuditLog>()).Returns(_auditLogRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfileVersionHistory>()).Returns(_historyRepo.Object);
        _unitOfWork.Setup(u => u.Repository<Recommendation>()).Returns(_recommendationRepo.Object);
        _recommendationRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<Recommendation, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Recommendation>());
        _sut = new ChildProfileService(
            _unitOfWork.Object, _accessGuard.Object, _childSessionRevoker.Object);
    }

    [Fact]
    public async Task CreateChildProfileAsync_CreatesDraftProfileOwnerSupervisionAndAudit()
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
        Assert.NotNull(supervision);
        Assert.Same(profile, supervision!.ChildProfile);
        Assert.Equal(2, supervision.SupervisorUserId);
        Assert.Equal(SupervisorRole.Owner, supervision.SupervisorRole);
        Assert.Null(supervision.RevokedAt);
        Assert.Equal("Draft", result.Status);
        _auditLogRepo.Verify(repository => repository.AddAsync(
            It.Is<AuditLog>(log => log.ActorUserId == 2
                                   && log.Action == "CREATE_CHILD_PROFILE"
                                   && log.EntityType == nameof(ChildProfile)),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
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

    [Fact]
    public async Task CreateChildProfileAsync_AssignsDefaultAvatar()
    {
        SetupOwner();
        ChildProfile? profile = null;
        _profileRepo.Setup(repository => repository.AddAsync(
                It.IsAny<ChildProfile>(), It.IsAny<CancellationToken>()))
            .Callback<ChildProfile, CancellationToken>((value, _) => profile = value)
            .ReturnsAsync((ChildProfile value, CancellationToken _) => value);

        var result = await _sut.CreateChildProfileAsync(2, PersonalRequest());

        Assert.Equal(ChildProfile.DefaultAvatarId, profile!.AvatarId);
        Assert.Equal(ChildProfile.DefaultAvatarId, result.AvatarId);
    }

    [Fact]
    public async Task CreateChildProfileAsync_NonVietnameseLanguage_ThrowsBadRequest()
    {
        SetupOwner();
        var request = PersonalRequest();
        request.Language = "en";

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
    public async Task CreateChildProfileAsync_InvalidAgeBand_ThrowsBadRequest()
    {
        SetupOwner();
        var request = PersonalRequest();
        request.AgeBand = (AgeBand)999;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateChildProfileAsync(2, request));

        VerifyNothingWasSaved();
    }

    [Fact]
    public void CreateChildProfileRequestDto_AcceptsDocumentedStringEnums()
    {
        var request = JsonSerializer.Deserialize<CreateChildProfileRequestDto>(
            """{"Nickname":"Bé Test","AgeBand":"Age_6_8"}""");

        Assert.NotNull(request);
        Assert.Equal(AgeBand.Age_6_8, request!.AgeBand);
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
    [InlineData(false, true, true, "Learning Profile")]
    [InlineData(true, false, false, "Safety Policy")]
    [InlineData(true, true, false, "consent")]
    public async Task ActivateChildProfileAsync_MissingRequiredSetup_ThrowsBadRequest(
        bool hasLearning, bool hasSafety, bool hasConsent, string expectedMessage)
    {
        SetupActivation(hasLearning, hasSafety, hasConsent);

        var exception = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ActivateChildProfileAsync(1, 2));

        Assert.Contains(expectedMessage, exception.Message);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActivateChildProfileAsync_WithLearningSafetyAndConsent_SetsActive()
    {
        var profile = SetupActivation(hasLearning: true, hasSafety: true, hasConsent: true);

        var result = await _sut.ActivateChildProfileAsync(1, 2);

        Assert.Equal(ChildProfileStatus.Active, profile.Status);
        Assert.Equal("Active", result.Status);
        _profileRepo.Verify(r => r.Update(profile), Times.Once);
        _auditLogRepo.Verify(repository => repository.AddAsync(
            It.Is<AuditLog>(log => log.ActorUserId == 2
                                   && log.Action == "ACTIVATE_CHILD_PROFILE"
                                   && log.EntityId == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActivateChildProfileAsync_OutdatedConsentVersion_ThrowsBadRequest()
    {
        var profile = SetupActivation(hasLearning: true, hasSafety: true, hasConsent: true);
        _safetyPolicyRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SafetyPolicy
            {
                Id = 5,
                ChildProfileId = 1,
                ConsentRecorded = true,
                ConsentPolicyVersion = 0
            });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ActivateChildProfileAsync(1, 2));

        Assert.Equal(ChildProfileStatus.Draft, profile.Status);
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
    public async Task ListMyChildProfilesAsync_ReturnsProfilesForAllActiveSupervisorRoles()
    {
        var relationships = new List<SupervisionRelationship>
        {
            new()
            {
                ChildProfileId = 1,
                SupervisorUserId = 2,
                SupervisorRole = SupervisorRole.Owner,
                ChildProfile = new ChildProfile { Id = 1, OwnerUserId = 2, Nickname = "Bé An" }
            },
            new()
            {
                ChildProfileId = 2,
                SupervisorUserId = 2,
                SupervisorRole = SupervisorRole.AdditionalSupervisor,
                ChildProfile = new ChildProfile { Id = 2, OwnerUserId = 3, Nickname = "Bé Bình" }
            }
        };
        _supervisionRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                nameof(SupervisionRelationship.ChildProfile),
                It.IsAny<CancellationToken>())).ReturnsAsync(relationships);

        var result = await _sut.ListMyChildProfilesAsync(2);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, value => value.Id == 1 && value.OwnerUserId == 2);
        Assert.Contains(result, value => value.Id == 2 && value.OwnerUserId == 3);
    }

    [Fact]
    public async Task ListMyChildProfilesAsync_OnlyIncludesActiveRelationshipsAndNonArchivedProfiles()
    {
        Expression<Func<SupervisionRelationship, bool>>? capturedPredicate = null;
        _supervisionRepo.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                nameof(SupervisionRelationship.ChildProfile),
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<SupervisionRelationship, bool>>, string?, CancellationToken>(
                (predicate, _, _) => capturedPredicate = predicate)
            .ReturnsAsync(new List<SupervisionRelationship>());

        await _sut.ListMyChildProfilesAsync(2);

        Assert.NotNull(capturedPredicate);
        var compiled = capturedPredicate!.Compile();
        Assert.True(compiled(new SupervisionRelationship
        {
            SupervisorUserId = 2,
            SupervisorRole = SupervisorRole.Owner,
            ChildProfile = new ChildProfile { Status = ChildProfileStatus.Active }
        }));
        Assert.True(compiled(new SupervisionRelationship
        {
            SupervisorUserId = 2,
            SupervisorRole = SupervisorRole.AdditionalSupervisor,
            ChildProfile = new ChildProfile { Status = ChildProfileStatus.Active }
        }));
        Assert.False(compiled(new SupervisionRelationship
        {
            SupervisorUserId = 2,
            SupervisorRole = SupervisorRole.AdditionalSupervisor,
            RevokedAt = DateTime.UtcNow,
            ChildProfile = new ChildProfile { Status = ChildProfileStatus.Active }
        }));
        Assert.False(compiled(new SupervisionRelationship
        {
            SupervisorUserId = 2,
            SupervisorRole = SupervisorRole.AdditionalSupervisor,
            ChildProfile = new ChildProfile { Status = ChildProfileStatus.Archived }
        }));
        Assert.False(compiled(new SupervisionRelationship
        {
            SupervisorUserId = 3,
            SupervisorRole = SupervisorRole.AdditionalSupervisor,
            ChildProfile = new ChildProfile { Status = ChildProfileStatus.Active }
        }));
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
            AgeBand = AgeBand.Age_9_10,
            Language = "vi"
        });

        Assert.Equal("Bé Mới", profile.Nickname);
        Assert.Equal(AgeBand.Age_9_10, profile.AgeBand);
        Assert.Equal("Bé Mới", result.Nickname);
        _profileRepo.Verify(r => r.Update(profile), Times.Once);
        _auditLogRepo.Verify(repository => repository.AddAsync(
            It.Is<AuditLog>(log => log.ActorUserId == 2
                                   && log.Action == "UPDATE_CHILD_PROFILE"
                                   && log.EntityId == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateChildProfileAsync_CallerNotOwner_ThrowsForbiddenAndSavesNothing()
    {
        var profile = new ChildProfile
        {
            Id = 1,
            OwnerUserId = 2,
            Nickname = "Cũ",
            AgeBand = AgeBand.Age_6_8,
            Status = ChildProfileStatus.Active
        };
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _accessGuard.Setup(guard => guard.EnsureOwnerAsync(1, 5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không phải Owner."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.UpdateChildProfileAsync(
            1, 5, new UpdateChildProfileRequestDto
            {
                Nickname = "Mới",
                AgeBand = AgeBand.Age_9_10
            }));

        Assert.Equal("Cũ", profile.Nickname);
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateChildProfileAsync_NonVietnameseLanguage_ThrowsBadRequest()
    {
        var profile = new ChildProfile
        {
            Id = 1,
            OwnerUserId = 2,
            Nickname = "Cũ",
            AgeBand = AgeBand.Age_6_8,
            Status = ChildProfileStatus.Active
        };
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        AllowProfileAccess();

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpdateChildProfileAsync(
            1, 2, new UpdateChildProfileRequestDto
            {
                Nickname = "Mới",
                AgeBand = AgeBand.Age_6_8,
                Language = "en"
            }));

        Assert.Equal("Cũ", profile.Nickname);
    }

    [Fact]
    public async Task UpdateChildProfileAsync_ChangeAvatarOnly_NoHistoryAndNoPresetPrompt()
    {
        var profile = new ChildProfile
        {
            Id = 1, OwnerUserId = 2, Nickname = "An",
            AgeBand = AgeBand.Age_6_8, Status = ChildProfileStatus.Active
        };
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var result = await _sut.UpdateChildProfileAsync(1, 2, new UpdateChildProfileRequestDto
        {
            Nickname = "An",
            AgeBand = AgeBand.Age_6_8,
            AvatarId = "avatar-fox"
        });

        Assert.Equal("avatar-fox", profile.AvatarId);
        Assert.False(result.ShouldReapplyBlockedPreset);
        Assert.Null(result.SuggestedReadingMode);
        _historyRepo.Verify(repository => repository.AddAsync(
            It.IsAny<ChildProfileVersionHistory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateChildProfileAsync_AgeBandChanged_WritesManualHistoryAndReturnsPromptFlags()
    {
        var profile = new ChildProfile
        {
            Id = 1, OwnerUserId = 2, Nickname = "An",
            AgeBand = AgeBand.Age_6_8, Status = ChildProfileStatus.Active
        };
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        var pending = new Recommendation
        {
            ChildProfileId = 1,
            Category = RecommendationCategory.ReadingDifficulty,
            Status = RecommendationStatus.AwaitingReview
        };
        _recommendationRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<Recommendation, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Recommendation> { pending });

        var result = await _sut.UpdateChildProfileAsync(1, 2, new UpdateChildProfileRequestDto
        {
            Nickname = "An",
            AgeBand = AgeBand.Age_9_10
        });

        Assert.Equal(RecommendationStatus.Stale, pending.Status);

        Assert.Equal(AgeBand.Age_9_10, profile.AgeBand);
        Assert.True(result.ShouldReapplyBlockedPreset);
        Assert.Equal("ReadFirst", result.SuggestedReadingMode);
        _historyRepo.Verify(repository => repository.AddAsync(
            It.Is<ChildProfileVersionHistory>(history =>
                history.ChildProfileId == 1
                && history.Source == VersionHistorySource.Manual
                && history.RecommendationId == null
                && history.AppliedByUserId == 2
                && history.PreviousConfig.Contains("Age_6_8")
                && history.NewConfig.Contains("Age_9_10")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ChildProfileStatus_MatchesSpecValuesOnly()
    {
        Assert.Equal(
            new[] { "Draft", "Active", "Suspended", "Archived", "Deleted" },
            Enum.GetNames<ChildProfileStatus>());
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
    public async Task ArchiveChildProfileAsync_Owner_SetsStatusArchivedAndRevokesAccess()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active };
        var credential = new ChildAccessCredential
        {
            Id = 8,
            ChildProfileId = 1,
            EasyLoginSecretHash = "hash"
        };
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        _credentialRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);
        _accessGuard.Setup(g => g.EnsureOwnerAsync(1, 2, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ArchiveChildProfileAsync(1, 2);

        Assert.Equal(ChildProfileStatus.Archived, profile.Status);
        Assert.NotNull(credential.RevokedAt);
        Assert.False(credential.IsDeleted);
        Assert.Null(credential.EasyLoginSecretHash);
        _childSessionRevoker.Verify(
            revoker => revoker.RevokeByChildProfileAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _auditLogRepo.Verify(repository => repository.AddAsync(
            It.Is<AuditLog>(log => log.ActorUserId == 2
                                   && log.Action == "ARCHIVE_CHILD_PROFILE"
                                   && log.EntityId == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveChildProfileAsync_DraftProfile_ThrowsBadRequest()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Draft };
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ArchiveChildProfileAsync(1, 2));

        Assert.Equal(ChildProfileStatus.Draft, profile.Status);
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

    [Fact]
    public async Task SuspendChildProfileAsync_ActiveProfile_SuspendsAndRevokesSessionsAndEasyLogin()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active };
        var credential = new ChildAccessCredential
        {
            Id = 8,
            ChildProfileId = 1,
            EasyLoginSecretHash = "hash"
        };
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _credentialRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);

        var result = await _sut.SuspendChildProfileAsync(1, 2);

        Assert.Equal("Suspended", result.Status);
        Assert.NotNull(credential.RevokedAt);
        Assert.False(credential.IsDeleted);
        Assert.Null(credential.EasyLoginSecretHash);
        _accessGuard.Verify(
            guard => guard.EnsureOwnerAsync(1, 2, It.IsAny<CancellationToken>()), Times.Once);
        _childSessionRevoker.Verify(
            revoker => revoker.RevokeByChildProfileAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _auditLogRepo.Verify(repository => repository.AddAsync(
            It.Is<AuditLog>(log => log.Action == "SUSPEND_CHILD_PROFILE"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SuspendChildProfileAsync_DraftProfile_ThrowsBadRequest()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Draft };
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SuspendChildProfileAsync(1, 2));
    }

    [Fact]
    public async Task SuspendChildProfileAsync_CallerNotOwner_ThrowsForbidden()
    {
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active });
        _accessGuard.Setup(guard => guard.EnsureOwnerAsync(1, 5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không phải Owner."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SuspendChildProfileAsync(1, 5));
    }

    [Fact]
    public async Task ResumeChildProfileAsync_ConsentUpToDate_SetsActiveWithoutAskingConsent()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Suspended };
        SetupConsent(profile, ConsentPolicy.CurrentVersion);

        var result = await _sut.ResumeChildProfileAsync(1, 2, new ResumeChildProfileRequestDto());

        Assert.Equal("Active", result.Status);
        _safetyPolicyRepo.Verify(repository => repository.Update(It.IsAny<SafetyPolicy>()), Times.Never);
    }

    [Fact]
    public async Task ResumeChildProfileAsync_ConsentOutdatedAndNotAccepted_ThrowsBadRequest()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Suspended };
        SetupConsent(profile, consentVersion: 0);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ResumeChildProfileAsync(1, 2, new ResumeChildProfileRequestDto()));

        Assert.Equal(ChildProfileStatus.Suspended, profile.Status);
    }

    [Fact]
    public async Task ResumeChildProfileAsync_ConsentOutdatedAndAccepted_RecordsNewConsentAndActivates()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Suspended };
        var policy = SetupConsent(profile, consentVersion: 0);

        var result = await _sut.ResumeChildProfileAsync(
            1, 2, new ResumeChildProfileRequestDto { AcceptCurrentConsent = true });

        Assert.Equal("Active", result.Status);
        Assert.Equal(ConsentPolicy.CurrentVersion, policy.ConsentPolicyVersion);
        Assert.Equal(2, policy.ConsentedByUserId);
    }

    [Fact]
    public async Task ResumeChildProfileAsync_NotSuspended_ThrowsBadRequest()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active };
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ResumeChildProfileAsync(1, 2, new ResumeChildProfileRequestDto()));
    }

    [Fact]
    public async Task RestoreChildProfileAsync_ArchivedProfile_SetsActive()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Archived };
        SetupConsent(profile, ConsentPolicy.CurrentVersion);

        var result = await _sut.RestoreChildProfileAsync(1, 2, new ResumeChildProfileRequestDto());

        Assert.Equal("Active", result.Status);
        _auditLogRepo.Verify(repository => repository.AddAsync(
            It.Is<AuditLog>(log => log.Action == "RESTORE_CHILD_PROFILE"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreChildProfileAsync_NotArchived_ThrowsBadRequest()
    {
        var profile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Suspended };
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RestoreChildProfileAsync(1, 2, new ResumeChildProfileRequestDto()));
    }

    private ChildProfile SetupActivation(
        bool hasLearning, bool hasSafety, bool hasConsent)
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
        _safetyPolicyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasSafety
                ? new SafetyPolicy
                {
                    Id = 5,
                    ChildProfileId = 1,
                    ConsentRecorded = hasConsent,
                    ConsentPolicyVersion = ConsentPolicy.CurrentVersion
                }
                : null);
        AllowProfileAccess();
        return profile;
    }

    private void AllowProfileAccess() => _accessGuard
        .Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SupervisionRelationship());

    private SafetyPolicy SetupConsent(ChildProfile profile, int consentVersion)
    {
        var policy = new SafetyPolicy
        {
            Id = 5,
            ChildProfileId = profile.Id,
            ConsentRecorded = true,
            ConsentPolicyVersion = consentVersion
        };
        _profileRepo.Setup(repository => repository.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _safetyPolicyRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
        return policy;
    }

    private void SetupOwner() => _userRepo
        .Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new UserAccount { Id = 2, Role = UserRole.Parent, Status = AccountStatus.Active });

    private void VerifyNothingWasSaved()
    {
        _profileRepo.Verify(r => r.AddAsync(It.IsAny<ChildProfile>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateChildProfileRequestDto PersonalRequest() => new()
    {
        Nickname = "Bé Test",
        AgeBand = AgeBand.Age_6_8
    };
}
