using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Services;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Safety;

public class SafetyPolicyServiceTests
{
    private readonly Mock<IGenericRepository<SafetyPolicy>> _policyRepo = new();
    private readonly Mock<IGenericRepository<SafetyPolicyCategory>> _categoryRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISupervisionAccessGuard> _guard = new();
    private readonly SafetyPolicyService _sut;

    public SafetyPolicyServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<SafetyPolicy>()).Returns(_policyRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SafetyPolicyCategory>()).Returns(_categoryRepo.Object);
        _sut = new SafetyPolicyService(_unitOfWork.Object, _guard.Object);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_WithoutPermission_StopsBeforeWrite()
    {
        _guard.Setup(g => g.EnsurePermissionAsync(
                1, 3, Permission.ManageSafetySettings, It.IsAny<CancellationToken>()))
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
        Assert.Single(result.Categories);
        _categoryRepo.Verify(r => r.AddAsync(
            It.Is<SafetyPolicyCategory>(c => c.SafetyPolicy == added
                                              && c.ContentCategoryId == 1
                                              && c.Rule == PolicyRule.Blocked),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
        _guard.Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupervisionRelationship());
        _policyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync((SafetyPolicy?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetSafetyPolicyAsync(1, 2));
    }

    [Fact]
    public async Task GetSafetyPolicyAsync_Exists_ReturnsDtoWithCategories()
    {
        _guard.Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupervisionRelationship());
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
        _guard.Setup(g => g.EnsurePermissionAsync(1, 3, Permission.ManageSafetySettings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.DeleteSafetyPolicyAsync(1, 3));
    }

    [Fact]
    public async Task DeleteSafetyPolicyAsync_Exists_DeletesPolicyAndCascadesCategories()
    {
        AllowPermission();
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
    }

    private void AllowPermission() => _guard.Setup(g => g.EnsurePermissionAsync(
        1, 2, Permission.ManageSafetySettings, It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

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
