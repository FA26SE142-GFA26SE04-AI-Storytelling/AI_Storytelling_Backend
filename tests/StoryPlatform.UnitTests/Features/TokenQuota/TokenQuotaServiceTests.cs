using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
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
    public async Task EnsureWithinQuotaAsync_ExpiredPeriod_RollsOverResetsUsageAndAllows()
    {
        SetupChild(MakePersonalChild());
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var config = MakeConfig(
            TokenQuotaScope.Child, quotaLimit: 5, quotaUsed: 5, childProfileId: 1,
            periodStart: yesterday.AddDays(-30), periodEnd: yesterday);
        SetupConfigLookup(config);

        await _sut.EnsureWithinQuotaAsync(1);

        Assert.Equal(0, config.QuotaUsed);
        Assert.True(config.PeriodEnd >= DateOnly.FromDateTime(DateTime.UtcNow));
        _configRepository.Verify(r => r.Update(config), Times.Once);
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
}
