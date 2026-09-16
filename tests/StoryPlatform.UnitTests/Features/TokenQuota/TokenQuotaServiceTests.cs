using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.TokenQuota;

public class TokenQuotaServiceTests
{
    private readonly Mock<IGenericRepository<ChildProfile>> _childRepository = new();
    private readonly Mock<IGenericRepository<StoryPlatform.Domain.Entities.TokenQuotaConfig>> _configRepository = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepository = new();
    private readonly Mock<IGenericRepository<SupervisionRelationship>> _supervisionRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly TokenQuotaService _sut;

    public TokenQuotaServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_childRepository.Object);
        _unitOfWork.Setup(u => u.Repository<StoryPlatform.Domain.Entities.TokenQuotaConfig>()).Returns(_configRepository.Object);
        _unitOfWork.Setup(u => u.Repository<UserAccount>()).Returns(_userRepository.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionRelationship>()).Returns(_supervisionRepository.Object);

        _sut = new TokenQuotaService(_unitOfWork.Object, _auditLogWriter.Object);
    }

    private static ChildProfile MakePersonalChild(int id = 1, int ownerUserId = 1) => new()
    {
        Id = id,
        OwnerUserId = ownerUserId,
        Scope = ProfileScope.Personal
    };

    private static ChildProfile MakeOrgChild(int id = 1, int organizationId = 10) => new()
    {
        Id = id,
        OwnerUserId = 1,
        Scope = ProfileScope.Organization,
        OrganizationId = organizationId
    };

    private static StoryPlatform.Domain.Entities.TokenQuotaConfig MakeConfig(
        TokenQuotaScope scope, int quotaLimit, int quotaUsed,
        int? organizationId = null, int? childProfileId = null, int? userId = null,
        DateOnly? periodStart = null, DateOnly? periodEnd = null)
    {
        var start = periodStart ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var end = periodEnd ?? start.AddMonths(1);
        return new StoryPlatform.Domain.Entities.TokenQuotaConfig
        {
            Id = 1,
            Scope = scope,
            OrganizationId = organizationId,
            ChildProfileId = childProfileId,
            UserId = userId,
            QuotaLimit = quotaLimit,
            QuotaUsed = quotaUsed,
            PeriodStart = start,
            PeriodEnd = end
        };
    }

    private void SetupChild(ChildProfile child) =>
        _childRepository.Setup(r => r.GetByIdAsync(child.Id, It.IsAny<CancellationToken>())).ReturnsAsync(child);

    private void SetupConfigLookup(params StoryPlatform.Domain.Entities.TokenQuotaConfig[] configs)
    {
        _configRepository
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<StoryPlatform.Domain.Entities.TokenQuotaConfig, bool>>>(),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<StoryPlatform.Domain.Entities.TokenQuotaConfig, bool>> predicate, string? _, CancellationToken _) =>
                configs.FirstOrDefault(predicate.Compile()));
    }

    // ---------- EnsureWithinQuotaAsync: hierarchy + fail-open ----------

    [Fact]
    public async Task EnsureWithinQuotaAsync_NoConfigAnywhere_DoesNotThrow()
    {
        SetupChild(MakePersonalChild());
        SetupConfigLookup();

        await _sut.EnsureWithinQuotaAsync(1);
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_ChildConfigAtLimit_ThrowsConflict()
    {
        SetupChild(MakePersonalChild());
        SetupConfigLookup(MakeConfig(TokenQuotaScope.Child, quotaLimit: 5, quotaUsed: 5, childProfileId: 1));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(1));
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_ChildConfigUnderLimit_DoesNotThrow()
    {
        SetupChild(MakePersonalChild());
        SetupConfigLookup(MakeConfig(TokenQuotaScope.Child, quotaLimit: 5, quotaUsed: 4, childProfileId: 1));

        await _sut.EnsureWithinQuotaAsync(1);
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_ChildConfigTakesPriorityOverOrganizationConfig()
    {
        SetupChild(MakeOrgChild(organizationId: 10));
        SetupConfigLookup(
            MakeConfig(TokenQuotaScope.Child, quotaLimit: 5, quotaUsed: 1, childProfileId: 1),
            MakeConfig(TokenQuotaScope.Organization, quotaLimit: 1, quotaUsed: 1, organizationId: 10));

        await _sut.EnsureWithinQuotaAsync(1); // child config (1/5) wins, not the exhausted org config
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_OrganizationChild_FallsBackToOrganizationConfig()
    {
        SetupChild(MakeOrgChild(organizationId: 10));
        SetupConfigLookup(MakeConfig(TokenQuotaScope.Organization, quotaLimit: 1, quotaUsed: 1, organizationId: 10));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(1));
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_PersonalChild_FallsBackToOwnerPersonalConfig()
    {
        SetupChild(MakePersonalChild(ownerUserId: 7));
        SetupConfigLookup(MakeConfig(TokenQuotaScope.Personal, quotaLimit: 1, quotaUsed: 1, userId: 7));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(1));
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_FallsBackToSystemConfig()
    {
        SetupChild(MakePersonalChild(ownerUserId: 7));
        SetupConfigLookup(MakeConfig(TokenQuotaScope.System, quotaLimit: 1, quotaUsed: 1));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(1));
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_ExpiredPeriod_TreatedAsNotExceeded_WithoutPersistingRollover()
    {
        // EnsureWithinQuotaAsync must be a pure read: an expired period can never be "exceeded"
        // (the effective QuotaUsed after an as-if rollover is always 0), and no rollover is
        // attached/saved here — that is IncrementUsageAsync's job, and doing it in both places is
        // what caused the EF identity-map double-attach bug.
        SetupChild(MakePersonalChild());
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var config = MakeConfig(
            TokenQuotaScope.Child, quotaLimit: 5, quotaUsed: 5, childProfileId: 1,
            periodStart: yesterday.AddDays(-30), periodEnd: yesterday);
        SetupConfigLookup(config);

        await _sut.EnsureWithinQuotaAsync(1);

        Assert.Equal(5, config.QuotaUsed);
        Assert.Equal(yesterday, config.PeriodEnd);
        _configRepository.Verify(r => r.Update(It.IsAny<StoryPlatform.Domain.Entities.TokenQuotaConfig>()), Times.Never);
    }

    [Fact]
    public async Task EnsureThenIncrement_ExpiredPeriod_DoesNotThrow_AndEndsWithQuotaUsedOne()
    {
        // Regression test for the EF identity-map "instance already tracked" conflict: with a real
        // DbContext, EnsureWithinQuotaAsync attaching a rolled-over config and IncrementUsageAsync
        // then re-resolving and attaching a second, distinct untracked instance for the same row
        // throws InvalidOperationException. Moq's mocked IGenericRepository has no identity map, so
        // it cannot reproduce that exception directly — instead this pins the corrected behavior and
        // call-count contract: Ensure performs zero attaches, and Increment performs exactly one
        // (rollover + increment collapsed into a single Update), for a total of one Update call
        // across the whole Ensure -> Increment flow.
        SetupChild(MakePersonalChild());
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var config = MakeConfig(
            TokenQuotaScope.Child, quotaLimit: 5, quotaUsed: 5, childProfileId: 1,
            periodStart: yesterday.AddDays(-30), periodEnd: yesterday);
        SetupConfigLookup(config);

        await _sut.EnsureWithinQuotaAsync(1);
        await _sut.IncrementUsageAsync(1);

        Assert.Equal(1, config.QuotaUsed);
        Assert.True(config.PeriodEnd >= DateOnly.FromDateTime(DateTime.UtcNow));
        _configRepository.Verify(r => r.Update(config), Times.Once);
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_MalformedOrganizationChildWithNullOrganizationId_DoesNotFallBackToPersonalConfig()
    {
        // A data-invariant-violating Organization-scope child with a null OrganizationId must not
        // silently resolve against the owner's unrelated Personal config.
        var child = new ChildProfile { Id = 1, OwnerUserId = 7, Scope = ProfileScope.Organization, OrganizationId = null };
        SetupChild(child);
        SetupConfigLookup(MakeConfig(TokenQuotaScope.Personal, quotaLimit: 1, quotaUsed: 1, userId: 7));

        // Falls through to the System-scope lookup instead (none configured here) => no applicable
        // config => does not throw, even though the exhausted Personal config exists.
        await _sut.EnsureWithinQuotaAsync(1);
    }

    // ---------- IncrementUsageAsync ----------

    [Fact]
    public async Task IncrementUsageAsync_NoApplicableConfig_NoOp()
    {
        SetupChild(MakePersonalChild());
        SetupConfigLookup();

        await _sut.IncrementUsageAsync(1);

        _configRepository.Verify(r => r.Update(It.IsAny<StoryPlatform.Domain.Entities.TokenQuotaConfig>()), Times.Never);
    }

    [Fact]
    public async Task IncrementUsageAsync_WithApplicableConfig_IncrementsQuotaUsedByOne()
    {
        SetupChild(MakePersonalChild());
        var config = MakeConfig(TokenQuotaScope.Child, quotaLimit: 5, quotaUsed: 2, childProfileId: 1);
        SetupConfigLookup(config);

        await _sut.IncrementUsageAsync(1);

        Assert.Equal(3, config.QuotaUsed);
        _configRepository.Verify(r => r.Update(config), Times.Once);
    }

    // ---------- CreditAsync ----------

    [Fact]
    public async Task CreditAsync_PersonalScope_NoExistingConfig_CreatesNewConfigWithFullAmount()
    {
        SetupConfigLookup();
        StoryPlatform.Domain.Entities.TokenQuotaConfig? added = null;
        _configRepository
            .Setup(r => r.AddAsync(It.IsAny<StoryPlatform.Domain.Entities.TokenQuotaConfig>(), It.IsAny<CancellationToken>()))
            .Callback<StoryPlatform.Domain.Entities.TokenQuotaConfig, CancellationToken>((entity, _) => added = entity)
            .ReturnsAsync((StoryPlatform.Domain.Entities.TokenQuotaConfig entity, CancellationToken _) => entity);

        await _sut.CreditAsync(ProfileScope.Personal, payerUserId: 7, organizationId: null, quotaAmount: 50);

        Assert.NotNull(added);
        Assert.Equal(TokenQuotaScope.Personal, added!.Scope);
        Assert.Equal(7, added.UserId);
        Assert.Null(added.OrganizationId);
        Assert.Equal(50, added.QuotaLimit);
        Assert.Equal(0, added.QuotaUsed);
    }

    [Fact]
    public async Task CreditAsync_PersonalScope_ExistingConfig_TopsUpWithoutResettingUsage()
    {
        var existing = MakeConfig(TokenQuotaScope.Personal, quotaLimit: 50, quotaUsed: 30, userId: 7);
        SetupConfigLookup(existing);

        await _sut.CreditAsync(ProfileScope.Personal, payerUserId: 7, organizationId: null, quotaAmount: 50);

        Assert.Equal(100, existing.QuotaLimit);
        Assert.Equal(30, existing.QuotaUsed);
        _configRepository.Verify(r => r.Update(existing), Times.Once);
    }

    [Fact]
    public async Task CreditAsync_OrganizationScope_NoExistingConfig_CreatesNewConfigForOrganization()
    {
        SetupConfigLookup();
        StoryPlatform.Domain.Entities.TokenQuotaConfig? added = null;
        _configRepository
            .Setup(r => r.AddAsync(It.IsAny<StoryPlatform.Domain.Entities.TokenQuotaConfig>(), It.IsAny<CancellationToken>()))
            .Callback<StoryPlatform.Domain.Entities.TokenQuotaConfig, CancellationToken>((entity, _) => added = entity)
            .ReturnsAsync((StoryPlatform.Domain.Entities.TokenQuotaConfig entity, CancellationToken _) => entity);

        await _sut.CreditAsync(ProfileScope.Organization, payerUserId: 7, organizationId: 10, quotaAmount: 150);

        Assert.NotNull(added);
        Assert.Equal(TokenQuotaScope.Organization, added!.Scope);
        Assert.Equal(10, added.OrganizationId);
        Assert.Null(added.UserId);
        Assert.Equal(150, added.QuotaLimit);
    }

    [Fact]
    public async Task CreditAsync_OrganizationScope_MissingOrganizationId_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CreditAsync(ProfileScope.Organization, payerUserId: 7, organizationId: null, quotaAmount: 150));
    }

    [Fact]
    public async Task CreditAsync_OrganizationScope_ExistingConfig_TopsUpWithoutResettingUsage()
    {
        var existing = MakeConfig(TokenQuotaScope.Organization, quotaLimit: 150, quotaUsed: 50, organizationId: 10);
        SetupConfigLookup(existing);

        await _sut.CreditAsync(ProfileScope.Organization, payerUserId: 7, organizationId: 10, quotaAmount: 150);

        Assert.Equal(300, existing.QuotaLimit);
        Assert.Equal(50, existing.QuotaUsed);
        _configRepository.Verify(r => r.Update(existing), Times.Once);
    }

    [Fact]
    public async Task CreditAsync_RollsOverExpiredPeriodBeforeTopUp()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var existing = MakeConfig(
            TokenQuotaScope.Personal, quotaLimit: 100, quotaUsed: 50, userId: 7,
            periodStart: yesterday.AddDays(-30), periodEnd: yesterday);
        SetupConfigLookup(existing);

        await _sut.CreditAsync(ProfileScope.Personal, payerUserId: 7, organizationId: null, quotaAmount: 150);

        Assert.Equal(0, existing.QuotaUsed);
        Assert.Equal(250, existing.QuotaLimit);
        Assert.True(existing.PeriodEnd >= DateOnly.FromDateTime(DateTime.UtcNow));
        _configRepository.Verify(r => r.Update(existing), Times.AtLeast(2));
    }

    // ---------- SetConfigAsync ----------

    [Fact]
    public async Task SetConfigAsync_InvalidScopeString_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetConfigAsync(1, new SetTokenQuotaConfigRequestDto
        {
            Scope = "not-a-scope", QuotaLimit = 10,
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1)
        }));
    }

    [Fact]
    public async Task SetConfigAsync_OrganizationScopeMissingOrganizationId_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetConfigAsync(1, new SetTokenQuotaConfigRequestDto
        {
            Scope = "Organization", QuotaLimit = 10,
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1)
        }));
    }

    [Fact]
    public async Task SetConfigAsync_PeriodEndBeforeStart_ThrowsBadRequest()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetConfigAsync(1, new SetTokenQuotaConfigRequestDto
        {
            Scope = "System", QuotaLimit = 10, PeriodStart = today, PeriodEnd = today.AddDays(-1)
        }));
    }

    [Fact]
    public async Task SetConfigAsync_NoExistingConfig_CreatesNewAndWritesAudit()
    {
        SetupConfigLookup();
        StoryPlatform.Domain.Entities.TokenQuotaConfig? added = null;
        _configRepository
            .Setup(r => r.AddAsync(It.IsAny<StoryPlatform.Domain.Entities.TokenQuotaConfig>(), It.IsAny<CancellationToken>()))
            .Callback<StoryPlatform.Domain.Entities.TokenQuotaConfig, CancellationToken>((entity, _) =>
            {
                entity.Id = 42;
                added = entity;
            })
            .ReturnsAsync((StoryPlatform.Domain.Entities.TokenQuotaConfig entity, CancellationToken _) => entity);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _sut.SetConfigAsync(99, new SetTokenQuotaConfigRequestDto
        {
            Scope = "System", QuotaLimit = 1000, PeriodStart = today, PeriodEnd = today.AddMonths(1)
        });

        Assert.NotNull(added);
        Assert.Equal("System", result.Scope);
        Assert.Equal(1000, result.QuotaLimit);
        _auditLogWriter.Verify(w => w.LogAsync(
            99, "TokenQuotaConfigSet", nameof(StoryPlatform.Domain.Entities.TokenQuotaConfig), 42,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetConfigAsync_ExistingConfigForSameTarget_UpdatesInPlace()
    {
        var existing = MakeConfig(TokenQuotaScope.Child, quotaLimit: 5, quotaUsed: 2, childProfileId: 3);
        SetupConfigLookup(existing);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _sut.SetConfigAsync(99, new SetTokenQuotaConfigRequestDto
        {
            Scope = "Child", ChildProfileId = 3, QuotaLimit = 20, PeriodStart = today, PeriodEnd = today.AddMonths(1)
        });

        Assert.Equal(20, existing.QuotaLimit);
        Assert.Equal(20, result.QuotaLimit);
        _configRepository.Verify(r => r.Update(existing), Times.Once);
    }

    // ---------- ListConfigsAsync ----------

    [Fact]
    public async Task ListConfigsAsync_ReturnsAllConfigsMapped()
    {
        var configs = new[]
        {
            MakeConfig(TokenQuotaScope.System, quotaLimit: 1000, quotaUsed: 1),
            MakeConfig(TokenQuotaScope.Child, quotaLimit: 5, quotaUsed: 0, childProfileId: 3)
        };
        _configRepository
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<StoryPlatform.Domain.Entities.TokenQuotaConfig, bool>>>(),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<StoryPlatform.Domain.Entities.TokenQuotaConfig, bool>> predicate, string? _, CancellationToken _) =>
                configs.Where(predicate.Compile()).ToList());

        var result = await _sut.ListConfigsAsync();

        Assert.Equal(2, result.Count);
    }

    // ---------- GetStatusForChildAsync ----------

    [Fact]
    public async Task GetStatusForChildAsync_NonOwnerNonSupervisorNonAdmin_ThrowsForbidden()
    {
        SetupChild(MakePersonalChild(ownerUserId: 1));
        _userRepository.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 2, Role = UserRole.Parent });
        _supervisionRepository
            .Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetStatusForChildAsync(2, 1));
    }

    [Fact]
    public async Task GetStatusForChildAsync_Administrator_AllowedRegardlessOfSupervision()
    {
        SetupChild(MakePersonalChild(ownerUserId: 1));
        _userRepository.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 99, Role = UserRole.Administrator });
        SetupConfigLookup();

        var result = await _sut.GetStatusForChildAsync(99, 1);

        Assert.True(result.IsUnlimited);
    }

    [Fact]
    public async Task GetStatusForChildAsync_ActiveSupervisor_ReturnsStatus()
    {
        SetupChild(MakePersonalChild(ownerUserId: 1));
        _userRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 1, Role = UserRole.Parent });
        _supervisionRepository
            .Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupConfigLookup(MakeConfig(TokenQuotaScope.Child, quotaLimit: 10, quotaUsed: 4, childProfileId: 1));

        var result = await _sut.GetStatusForChildAsync(1, 1);

        Assert.False(result.IsUnlimited);
        Assert.Equal(10, result.QuotaLimit);
        Assert.Equal(4, result.QuotaUsed);
        Assert.Equal(6, result.Remaining);
    }
}
