using System.Linq.Expressions;
using System.Text.Json;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Services;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Safety;

public class SafetyPolicyServiceTests
{
    private readonly Mock<IGenericRepository<SafetyPolicy>> _policyRepo = new();
    private readonly Mock<IGenericRepository<SafetyPolicyCategory>> _categoryRepo = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IGenericRepository<ContentCategory>> _contentCategoryRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly List<(int? Actor, string Action, string EntityType, int EntityId, string? Before, string? After)> _audits = new();
    private readonly SafetyPolicyService _sut;

    public SafetyPolicyServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<SafetyPolicy>()).Returns(_policyRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SafetyPolicyCategory>()).Returns(_categoryRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ContentCategory>()).Returns(_contentCategoryRepo.Object);
        _auditLogWriter.Setup(w => w.LogAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<int?, string, string, int, object?, object?, CancellationToken>(
                (actor, action, entityType, entityId, before, after, _) => _audits.Add((
                    actor, action, entityType, entityId,
                    before == null ? null : JsonSerializer.Serialize(before),
                    after == null ? null : JsonSerializer.Serialize(after))))
            .Returns(Task.CompletedTask);
        _sut = new SafetyPolicyService(_unitOfWork.Object, _guard.Object, _auditLogWriter.Object);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_WithoutPermission_StopsBeforeWrite()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(
                1, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SetSafetyPolicyAsync(1, 3, ValidRequest()));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_WithoutConsent_ThrowsBadRequest()
    {
        AllowPermission();
        var request = ValidRequest();
        request.ConsentRecorded = false;

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetSafetyPolicyAsync(1, 2, request));
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_NewPolicy_CreatesCategoriesAndConsentTimestamp()
    {
        AllowPermission();
        _policyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync((SafetyPolicy?)null);
        SafetyPolicy? added = null;
        _policyRepo.Setup(r => r.AddAsync(It.IsAny<SafetyPolicy>(), It.IsAny<CancellationToken>()))
            .Callback<SafetyPolicy, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((SafetyPolicy value, CancellationToken _) => value);

        var result = await _sut.SetSafetyPolicyAsync(1, 2, ValidRequest());

        Assert.Equal(1500, added!.MaxStoryLength);
        Assert.Equal(60m, added.ReadabilityScoreThreshold);
        Assert.Equal(60m, result.ReadabilityScoreThreshold);
        Assert.True(added.ConsentRecorded);
        Assert.NotNull(added.ConsentRecordedAt);
        Assert.Equal(ConsentPolicy.CurrentVersion, added.ConsentPolicyVersion);
        Assert.Equal(2, added.ConsentedByUserId);
        Assert.Equal(2, result.ConsentedByUserId);
        Assert.Single(result.Categories);
        _categoryRepo.Verify(r => r.AddAsync(
            It.Is<SafetyPolicyCategory>(c => c.SafetyPolicy == added
                                              && c.ContentCategoryId == 1
                                              && c.Rule == PolicyRule.Blocked),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_audits, audit => audit.Action == "SET_SAFETY_POLICY");
        Assert.Single(_audits, audit => audit.Action == "RECORD_SAFETY_CONSENT");
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_ExistingPolicy_ReplacesCategories()
    {
        AllowPermission();
        var policy = new SafetyPolicy { Id = 5, ChildProfileId = 1 };
        var oldCategories = new List<SafetyPolicyCategory>
        {
            new() { Id = 8, SafetyPolicyId = 5, ContentCategoryId = 2 }
        };
        _policyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        _categoryRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SafetyPolicyCategory, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(oldCategories);

        await _sut.SetSafetyPolicyAsync(1, 2, ValidRequest());

        _policyRepo.Verify(r => r.Update(policy), Times.Once);
        _categoryRepo.Verify(r => r.DeleteRange(oldCategories), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_DuplicateCategory_ThrowsBadRequest()
    {
        AllowPermission();
        var request = ValidRequest();
        request.Categories.Add(new SetSafetyPolicyCategoryRequestDto
        {
            ContentCategoryId = 1,
            Rule = PolicyRule.Allowed
        });

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetSafetyPolicyAsync(1, 2, request));
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_ReadabilityThresholdOutsideRange_ThrowsBadRequest()
    {
        AllowPermission();
        var request = ValidRequest();
        request.ReadabilityScoreThreshold = 101m;

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetSafetyPolicyAsync(1, 2, request));
    }

    [Fact]
    public async Task GetSafetyPolicyAsync_NotYetSet_ThrowsNotFound()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2 });
        _policyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync((SafetyPolicy?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetSafetyPolicyAsync(1, 2));
    }

    [Fact]
    public async Task GetSafetyPolicyAsync_Exists_ReturnsDtoWithCategories()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2 });
        var policy = new SafetyPolicy
        {
            Id = 5, ChildProfileId = 1, MaxStoryLength = 1500, ConsentRecorded = true,
            ReadabilityScoreThreshold = 62m
        };
        _policyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        _categoryRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SafetyPolicyCategory, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SafetyPolicyCategory> { new() { SafetyPolicyId = 5, ContentCategoryId = 1, Rule = PolicyRule.Blocked } });

        var result = await _sut.GetSafetyPolicyAsync(1, 2);

        Assert.Equal(1500, result.MaxStoryLength);
        Assert.Equal(62m, result.ReadabilityScoreThreshold);
        Assert.Single(result.Categories);
    }

    [Fact]
    public async Task DeleteSafetyPolicyAsync_WithoutPermission_ThrowsForbidden()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(1, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.DeleteSafetyPolicyAsync(1, 3));
    }

    [Fact]
    public async Task DeleteSafetyPolicyAsync_Exists_DeletesPolicyAndCascadesCategories()
    {
        AllowPermission();
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, Status = ChildProfileStatus.Draft });
        var policy = new SafetyPolicy { Id = 5, ChildProfileId = 1 };
        _policyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        var categories = new List<SafetyPolicyCategory> { new() { Id = 8, SafetyPolicyId = 5, ContentCategoryId = 1 } };
        _categoryRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SafetyPolicyCategory, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(categories);

        await _sut.DeleteSafetyPolicyAsync(1, 2);

        _categoryRepo.Verify(r => r.DeleteRange(categories), Times.Once);
        _policyRepo.Verify(r => r.Delete(policy), Times.Once);
        Assert.Single(_audits, audit => audit.Action == "DELETE_SAFETY_POLICY");
    }

    [Fact]
    public async Task DeleteSafetyPolicyAsync_ActiveProfile_ThrowsBadRequest()
    {
        AllowPermission();
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, Status = ChildProfileStatus.Active });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.DeleteSafetyPolicyAsync(1, 2));

        _policyRepo.Verify(r => r.Delete(It.IsAny<SafetyPolicy>()), Times.Never);
        Assert.Empty(_audits);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_NewPolicy_StoresInteractionFlagsAndDefaults()
    {
        AllowPermission();
        SetupNoPolicy();
        SafetyPolicy? added = null;
        _policyRepo.Setup(repository => repository.AddAsync(
                It.IsAny<SafetyPolicy>(), It.IsAny<CancellationToken>()))
            .Callback<SafetyPolicy, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((SafetyPolicy value, CancellationToken _) => value);
        var request = ValidRequest();
        request.StoryWishesEnabled = false;
        request.ChildCreationMode = ChildCreationMode.ListenNow;

        var result = await _sut.SetSafetyPolicyAsync(1, 2, request);

        Assert.False(added!.StoryWishesEnabled);
        Assert.True(added.AllowInteractiveStories);
        Assert.True(added.ChildCreationEnabled);
        Assert.Equal(ChildCreationMode.ListenNow, added.ChildCreationMode);
        Assert.False(added.VoiceAnswersEnabled);
        Assert.Equal("ListenNow", result.ChildCreationMode);
        Assert.False(result.VoiceConsentRequired);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_OwnerEnablesVoiceFirstTimeWithoutAcceptance_ThrowsBadRequest()
    {
        AllowPermission();
        SetupExistingPolicy(new SafetyPolicy
        {
            Id = 5, ChildProfileId = 1, ConsentRecorded = true,
            ConsentPolicyVersion = 1, ConsentedByUserId = 2
        });
        var request = ValidRequest();
        request.VoiceAnswersEnabled = true;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetSafetyPolicyAsync(1, 2, request));
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_OwnerEnablesVoiceWithAcceptance_RecordsVoiceConsent()
    {
        AllowPermission();
        var policy = new SafetyPolicy
        {
            Id = 5, ChildProfileId = 1, ConsentRecorded = true,
            ConsentPolicyVersion = 1, ConsentedByUserId = 2
        };
        SetupExistingPolicy(policy);
        var request = ValidRequest();
        request.VoiceAnswersEnabled = true;
        request.VoiceConsentAccepted = true;

        var result = await _sut.SetSafetyPolicyAsync(1, 2, request);

        Assert.True(policy.VoiceAnswersEnabled);
        Assert.NotNull(policy.VoiceConsentAt);
        Assert.Null(policy.VoiceEnableRequestedAt);
        Assert.False(result.VoiceConsentRequired);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_DisablingVoice_ClearsFlagAndPendingRequest()
    {
        AllowPermission(userId: 3);
        var policy = new SafetyPolicy
        {
            Id = 5, ChildProfileId = 1, ConsentRecorded = true,
            ConsentPolicyVersion = 1, ConsentedByUserId = 2,
            VoiceAnswersEnabled = true, VoiceConsentAt = DateTime.UtcNow,
            VoiceEnableRequestedAt = DateTime.UtcNow
        };
        SetupExistingPolicy(policy);
        var request = ValidRequest();
        request.VoiceAnswersEnabled = false;

        await _sut.SetSafetyPolicyAsync(1, 3, request);

        Assert.False(policy.VoiceAnswersEnabled);
        Assert.Null(policy.VoiceEnableRequestedAt);
        Assert.NotNull(policy.VoiceConsentAt);
    }

    [Fact]
    public void ConsentPolicy_IsOutdated_ComparesWithCurrentVersion()
    {
        Assert.True(ConsentPolicy.IsOutdated(ConsentPolicy.CurrentVersion - 1));
        Assert.False(ConsentPolicy.IsOutdated(ConsentPolicy.CurrentVersion));
    }

    [Fact]
    public async Task ReconsentAsync_CallerNotOwner_ThrowsForbidden()
    {
        _guard.Setup(guard => guard.EnsureOwnerAsync(1, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không phải Owner."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ReconsentAsync(1, 3));

        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconsentAsync_OutdatedVersion_RecordsCurrentVersionAndAudits()
    {
        var policy = new SafetyPolicy
        {
            Id = 5, ChildProfileId = 1, ConsentRecorded = true,
            ConsentPolicyVersion = 0, ConsentedByUserId = 9
        };
        SetupExistingPolicy(policy);

        var result = await _sut.ReconsentAsync(1, 2);

        Assert.Equal(ConsentPolicy.CurrentVersion, policy.ConsentPolicyVersion);
        Assert.Equal(2, policy.ConsentedByUserId);
        Assert.NotNull(policy.ConsentRecordedAt);
        Assert.False(result.ConsentOutdated);
        Assert.Single(_audits, audit => audit.Action == "RECONSENT_SAFETY_POLICY");
    }

    [Fact]
    public async Task ReconsentAsync_AlreadyCurrent_DoesNothing()
    {
        var policy = new SafetyPolicy
        {
            Id = 5, ChildProfileId = 1, ConsentRecorded = true,
            ConsentPolicyVersion = ConsentPolicy.CurrentVersion
        };
        SetupExistingPolicy(policy);

        await _sut.ReconsentAsync(1, 2);

        _policyRepo.Verify(repository => repository.Update(It.IsAny<SafetyPolicy>()), Times.Never);
        Assert.Empty(_audits);
    }

    [Fact]
    public async Task ReconsentAsync_NoPolicyOrNoInitialConsent_Throws()
    {
        SetupNoPolicy();
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ReconsentAsync(1, 2));

        SetupExistingPolicy(new SafetyPolicy { Id = 5, ChildProfileId = 1, ConsentRecorded = false });
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ReconsentAsync(1, 2));
    }

    [Fact]
    public async Task GetSafetyPolicyAsync_OutdatedConsent_ReportsOutdatedAndTurnsVoiceOffTemporarily()
    {
        AllowPermission();
        SetupExistingPolicy(new SafetyPolicy
        {
            Id = 5, ChildProfileId = 1, ConsentRecorded = true, ConsentPolicyVersion = 0,
            VoiceAnswersEnabled = true, VoiceConsentAt = DateTime.UtcNow
        });

        var result = await _sut.GetSafetyPolicyAsync(1, 2);

        Assert.True(result.ConsentOutdated);
        Assert.True(result.VoiceAnswersEnabled);
        Assert.False(result.EffectiveVoiceAnswersEnabled);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_NewPolicyWithoutCategories_FillsBlockedPresetForAgeBand()
    {
        AllowPermission();
        SetupNoPolicy();
        SetupChildAndCategories(AgeBand.Age_6_8);
        var request = ValidRequest();
        request.Categories.Clear();

        var result = await _sut.SetSafetyPolicyAsync(1, 2, request);

        var category = Assert.Single(result.Categories);
        Assert.Equal(7, category.ContentCategoryId);
        Assert.Equal("Blocked", category.Rule);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_NewPolicyOptOutOfPreset_StoresNoCategories()
    {
        AllowPermission();
        SetupNoPolicy();
        SetupChildAndCategories(AgeBand.Age_6_8);
        var request = ValidRequest();
        request.Categories.Clear();
        request.ApplyAgePreset = false;

        var result = await _sut.SetSafetyPolicyAsync(1, 2, request);

        Assert.Empty(result.Categories);
        _profileRepo.Verify(repository => repository.GetByIdAsync(
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_PresetDoesNotOverrideCategoryParentAlreadyDeclared()
    {
        AllowPermission();
        SetupNoPolicy();
        SetupChildAndCategories(AgeBand.Age_6_8);
        var request = ValidRequest();
        request.ApplyAgePreset = true;
        request.Categories = new List<SetSafetyPolicyCategoryRequestDto>
        {
            new() { ContentCategoryId = 7, Rule = PolicyRule.Allowed }
        };

        var result = await _sut.SetSafetyPolicyAsync(1, 2, request);

        var category = Assert.Single(result.Categories);
        Assert.Equal("Allowed", category.Rule);
    }

    [Fact]
    public async Task GetAgePresetAsync_ReturnsBlockedCategoryIdsOfChildAgeBand()
    {
        _guard.Setup(guard => guard.EnsureOwnerAsync(
                1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2 });
        SetupChildAndCategories(AgeBand.Age_9_10);

        var result = await _sut.GetAgePresetAsync(1, 2);

        Assert.Equal("Age_9_10", result.AgeBand);
        Assert.Equal(new[] { 8 }, result.BlockedCategoryIds);
    }

    [Fact]
    public async Task ApplyAgePresetAsync_ReplacesBlockedKeepsUnrelatedAllowedAndBlockedWins()
    {
        AllowPermission();
        var policy = new SafetyPolicy
        {
            Id = 5, ChildProfileId = 1, ConsentRecorded = true, ConsentPolicyVersion = 1
        };
        _policyRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
        var existing = new List<SafetyPolicyCategory>
        {
            new() { Id = 1, SafetyPolicyId = 5, ContentCategoryId = 1, Rule = PolicyRule.Allowed },
            new() { Id = 2, SafetyPolicyId = 5, ContentCategoryId = 2, Rule = PolicyRule.Blocked },
            new() { Id = 3, SafetyPolicyId = 5, ContentCategoryId = 7, Rule = PolicyRule.Allowed }
        };
        _categoryRepo.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<SafetyPolicyCategory, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupChildAndCategories(AgeBand.Age_6_8);

        var result = await _sut.ApplyAgePresetAsync(1, 2);

        _categoryRepo.Verify(repository => repository.DeleteRange(
            It.Is<IEnumerable<SafetyPolicyCategory>>(removed =>
                removed.Select(category => category.Id).OrderBy(id => id).SequenceEqual(new[] { 2, 3 }))),
            Times.Once);
        _categoryRepo.Verify(repository => repository.AddAsync(
            It.Is<SafetyPolicyCategory>(category =>
                category.ContentCategoryId == 7 && category.Rule == PolicyRule.Blocked),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(result.Categories,
            category => category.ContentCategoryId == 1 && category.Rule == "Allowed");
        Assert.Contains(result.Categories,
            category => category.ContentCategoryId == 7 && category.Rule == "Blocked");
        Assert.DoesNotContain(result.Categories, category => category.ContentCategoryId == 2);
        Assert.Single(_audits, audit => audit.Action == "APPLY_AGE_PRESET");
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_RestrictedRule_ThrowsBadRequest()
    {
        AllowPermission();
        var request = ValidRequest();
        request.Categories = new List<SetSafetyPolicyCategoryRequestDto>
        {
            new() { ContentCategoryId = 1, Rule = PolicyRule.Restricted }
        };

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetSafetyPolicyAsync(1, 2, request));
    }

    private void SetupExistingPolicy(SafetyPolicy policy)
    {
        _policyRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
        _categoryRepo.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<SafetyPolicyCategory, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SafetyPolicyCategory>());
    }

    private void SetupNoPolicy() => _policyRepo.Setup(repository => repository.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((SafetyPolicy?)null);

    private void SetupChildAndCategories(AgeBand ageBand)
    {
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, AgeBand = ageBand });
        var category7 = new ContentCategory { Id = 7, Code = "HORROR", IsActive = true };
        category7.DefaultBlockedAgeBands.Add("Age_6_8");
        var category8 = new ContentCategory { Id = 8, Code = "VIOLENCE", IsActive = true };
        category8.DefaultBlockedAgeBands.Add("Age_9_10");
        var category9 = new ContentCategory { Id = 9, Code = "ANIMALS", IsActive = true };
        _contentCategoryRepo.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<ContentCategory, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContentCategory> { category7, category8, category9 });
    }

    private void AllowPermission(int userId = 2)
    {
        _guard.Setup(g => g.EnsureOwnerAsync(
                1, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = userId });
        _guard.Setup(g => g.EnsureOwnerAsync(1, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = userId });
    }

    private static SetSafetyPolicyRequestDto ValidRequest() => new()
    {
        MaxStoryLength = 1500,
        ConsentRecorded = true,
        ReadabilityScoreThreshold = 60m,
        Categories = new List<SetSafetyPolicyCategoryRequestDto>
        {
            new() { ContentCategoryId = 1, Rule = PolicyRule.Blocked }
        }
    };
}
