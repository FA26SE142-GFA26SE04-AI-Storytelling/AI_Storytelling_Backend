# Token Quota Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Bước 5.1c (Token Quota) end-to-end — Admin config, hierarchical enforcement in the AI story-generation entry point, and quota crediting from SePay payments — so the existing `token_quota_configs` table and the SePay payment flow actually do something.

**Architecture:** A new `Features/TokenQuota` slice in `StoryPlatform.Application` (DTOs/Interfaces/Services, same shape as every other feature folder in this repo) owns all quota logic behind `ITokenQuotaService`. Two existing services get one extra constructor dependency each and call into it: `AIStoryInputService.SubmitAsync` (check-then-increment) and `PaymentService` (credit-on-paid). A new `TokenQuotaController` exposes Admin config CRUD + a Supervisor "my usage" read.

**Tech Stack:** .NET 10 / EF Core (Npgsql), xUnit + Moq (and a hand-rolled `FakeUnitOfWork` in one existing test file), ASP.NET Core `[Authorize]`.

**Spec:** [docs/superpowers/specs/2026-09-16-token-quota-design.md](../specs/2026-09-16-token-quota-design.md) — read it first; this plan implements it task-by-task and does not repeat the "why".

## Global Constraints

- Fail-open: a `ChildProfile` with no applicable `TokenQuotaConfig` anywhere in the resolve hierarchy is **unlimited** — never throw in that case.
- One "lượt" = one brand-new `StoryGenerationRequest` created by `AIStoryInputService.SubmitAsync`. Idempotent resubmits and `RetryAsync` must never consume a second lượt.
- Every repository read (`FindAsync`/`FirstOrDefaultAsync`/`GetAllAsync`) is `.AsNoTracking()` in the real EF repository — every mutation path must call `.Update(entity)` explicitly before `SaveChangesAsync`.
- `GetByIdAsync` calls raw `DbSet.FindAsync` — it does **not** support `includeProperties` and does not populate navigation properties. Never read `transaction.Plan` — always fetch `SubscriptionPlan` by `transaction.PlanId` explicitly.
- Money/scope mapping: `SubscriptionPlan.ApplicableScope` is `ProfileScope` (`Personal`/`Organization`); `TokenQuotaConfig.Scope` is the separate `TokenQuotaScope` enum (`System`/`Organization`/`Child`/`Personal`, this last one new). `CreditAsync` is the single place that translates between them.
- No optimistic concurrency on `QuotaUsed` — plain read-mutate-save, consistent with the rest of the codebase.
- Reuse `ConflictException` (409) for "quota exceeded" — do not add a new exception type.

---

### Task 1: Domain schema — `TokenQuotaScope.Personal` + `TokenQuotaConfig.UserId`

**Files:**
- Modify: `src/Core/StoryPlatform.Domain/Enums/TokenQuotaScope.cs`
- Modify: `src/Core/StoryPlatform.Domain/Entities/TokenQuotaConfig.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/TokenQuotaConfigConfiguration.cs`
- Create: new EF Core migration (via `dotnet ef migrations add`)

**Interfaces:**
- Produces: `TokenQuotaScope.Personal = 4`; `TokenQuotaConfig.UserId` (`int?`) and `TokenQuotaConfig.User` (`UserAccount?`) — every later task's queries/writes on `TokenQuotaConfig` use this field.

- [ ] **Step 1: Add the enum value**

Edit `src/Core/StoryPlatform.Domain/Enums/TokenQuotaScope.cs`:

```csharp
namespace StoryPlatform.Domain.Enums;

public enum TokenQuotaScope
{
    System = 1,
    Organization = 2,
    Child = 3,
    Personal = 4
}
```

- [ ] **Step 2: Add the `UserId` column to the entity**

Edit `src/Core/StoryPlatform.Domain/Entities/TokenQuotaConfig.cs`:

```csharp
using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class TokenQuotaConfig : BaseEntity
{
    public TokenQuotaScope Scope { get; set; }

    public int? OrganizationId { get; set; }
    public virtual Organization? Organization { get; set; }

    public int? ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int? UserId { get; set; }
    public virtual UserAccount? User { get; set; }

    public int QuotaLimit { get; set; }
    public int QuotaUsed { get; set; } = 0;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
}
```

- [ ] **Step 3: Map the new FK in EF configuration**

Edit `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/TokenQuotaConfigConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class TokenQuotaConfigConfiguration : IEntityTypeConfiguration<TokenQuotaConfig>
{
    public void Configure(EntityTypeBuilder<TokenQuotaConfig> builder)
    {
        builder.ToTable("token_quota_configs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Scope)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
```

- [ ] **Step 4: Generate the migration**

```bash
dotnet ef migrations add AddPersonalScopeToTokenQuota --project src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj --startup-project src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj
```

Expected: a new `Migrations/<timestamp>_AddPersonalScopeToTokenQuota.cs` that only adds a nullable `UserId` column + FK to `token_quota_configs` (no other table changes). Open the generated file and confirm that's all it contains before moving on — if EF also picked up unrelated pending model changes, stop and investigate rather than committing them silently.

- [ ] **Step 5: Build**

```bash
dotnet build StoryPlatform.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Apply the migration to the local dev database**

```bash
dotnet ef database update --project src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj --startup-project src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj
```

Expected: `Applying migration '..._AddPersonalScopeToTokenQuota'. Done.`

- [ ] **Step 7: Commit**

```bash
git add src/Core/StoryPlatform.Domain/Enums/TokenQuotaScope.cs src/Core/StoryPlatform.Domain/Entities/TokenQuotaConfig.cs src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/TokenQuotaConfigConfiguration.cs src/Core/StoryPlatform.Infrastructure/Migrations/
git commit -m "feat(token-quota): add Personal scope and UserId column to token_quota_configs"
```

---

### Task 2: `TokenQuotaService` core — hierarchy resolve, lazy rollover, enforce, increment

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/TokenQuota/DTOs/TokenQuotaDtos.cs`
- Create: `src/Core/StoryPlatform.Application/Features/TokenQuota/Interfaces/ITokenQuotaService.cs`
- Create: `src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork.Repository<T>()` (`ChildProfile`, `TokenQuotaConfig`), `IAuditLogWriter.LogAsync(int? actorUserId, string action, string entityType, int entityId, object? beforeState, object? afterState, CancellationToken)`.
- Produces (used by Task 3, 5, 6, 7, 8):
  - `Task EnsureWithinQuotaAsync(int childProfileId, CancellationToken ct = default)`
  - `Task IncrementUsageAsync(int childProfileId, CancellationToken ct = default)`
  - DTOs: `TokenQuotaConfigDto`, `TokenQuotaStatusDto`, `SetTokenQuotaConfigRequestDto` (full shape below)

- [ ] **Step 1: Write the DTOs**

Create `src/Core/StoryPlatform.Application/Features/TokenQuota/DTOs/TokenQuotaDtos.cs`:

```csharp
namespace StoryPlatform.Application.Features.TokenQuota.DTOs;

public class SetTokenQuotaConfigRequestDto
{
    public string Scope { get; set; } = string.Empty;
    public int? OrganizationId { get; set; }
    public int? ChildProfileId { get; set; }
    public int? UserId { get; set; }
    public int QuotaLimit { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
}

public class TokenQuotaConfigDto
{
    public int Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public int? OrganizationId { get; set; }
    public int? ChildProfileId { get; set; }
    public int? UserId { get; set; }
    public int QuotaLimit { get; set; }
    public int QuotaUsed { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
}

public class TokenQuotaStatusDto
{
    public bool IsUnlimited { get; set; }
    public string? Scope { get; set; }
    public int? QuotaLimit { get; set; }
    public int? QuotaUsed { get; set; }
    public int? Remaining { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
}
```

- [ ] **Step 2: Write the interface**

Create `src/Core/StoryPlatform.Application/Features/TokenQuota/Interfaces/ITokenQuotaService.cs`:

```csharp
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.TokenQuota.Interfaces;

public interface ITokenQuotaService
{
    Task<TokenQuotaConfigDto> SetConfigAsync(
        int adminUserId, SetTokenQuotaConfigRequestDto request, CancellationToken cancellationToken = default);

    Task<List<TokenQuotaConfigDto>> ListConfigsAsync(CancellationToken cancellationToken = default);

    Task<TokenQuotaStatusDto> GetStatusForChildAsync(
        int requestingUserId, int childProfileId, CancellationToken cancellationToken = default);

    Task EnsureWithinQuotaAsync(int childProfileId, CancellationToken cancellationToken = default);

    Task IncrementUsageAsync(int childProfileId, CancellationToken cancellationToken = default);

    Task CreditAsync(
        ProfileScope planScope, int payerUserId, int? organizationId, int quotaAmount,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing tests for hierarchy resolve + enforce**

Create `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs`:

```csharp
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
}
```

- [ ] **Step 4: Run the tests to confirm they fail (service does not exist yet)**

```bash
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~TokenQuotaServiceTests"
```

Expected: build error — `TokenQuotaService` does not exist. That confirms the tests are wired to real code, not vacuously passing.

- [ ] **Step 5: Implement `TokenQuotaService` (resolve/rollover/enforce/increment only — Credit/SetConfig/List/GetStatus land in later tasks as `NotImplementedException` stubs)**

Create `src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs`:

```csharp
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.TokenQuota.Services;

public class TokenQuotaService : ITokenQuotaService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;

    public TokenQuotaService(IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
    }

    public async Task EnsureWithinQuotaAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var config = await ResolveApplicableConfigAsync(child, cancellationToken);

        if (config != null && config.QuotaUsed >= config.QuotaLimit)
        {
            throw new ConflictException(
                "Đã hết lượt sinh truyện AI trong chu kỳ hiện tại. Vui lòng mua thêm quota hoặc chờ tới kỳ reset tiếp theo.");
        }
    }

    public async Task IncrementUsageAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var config = await ResolveApplicableConfigAsync(child, cancellationToken);
        if (config == null)
        {
            return;
        }

        config.QuotaUsed += 1;
        _unitOfWork.Repository<TokenQuotaConfig>().Update(config);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<TokenQuotaConfigDto> SetConfigAsync(
        int adminUserId, SetTokenQuotaConfigRequestDto request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Implemented in Task 4.");

    public Task<List<TokenQuotaConfigDto>> ListConfigsAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Implemented in Task 4.");

    public Task<TokenQuotaStatusDto> GetStatusForChildAsync(
        int requestingUserId, int childProfileId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Implemented in Task 4.");

    public Task CreditAsync(
        ProfileScope planScope, int payerUserId, int? organizationId, int quotaAmount,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Implemented in Task 3.");

    private async Task<TokenQuotaConfig?> ResolveApplicableConfigAsync(ChildProfile child, CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<TokenQuotaConfig>();

        var childConfig = await repo.FirstOrDefaultAsync(
            c => c.Scope == TokenQuotaScope.Child && c.ChildProfileId == child.Id,
            cancellationToken: cancellationToken);
        if (childConfig != null)
        {
            return await RolloverIfExpiredAsync(childConfig, cancellationToken);
        }

        if (child.Scope == ProfileScope.Organization && child.OrganizationId.HasValue)
        {
            var orgConfig = await repo.FirstOrDefaultAsync(
                c => c.Scope == TokenQuotaScope.Organization && c.OrganizationId == child.OrganizationId,
                cancellationToken: cancellationToken);
            if (orgConfig != null)
            {
                return await RolloverIfExpiredAsync(orgConfig, cancellationToken);
            }
        }
        else
        {
            var personalConfig = await repo.FirstOrDefaultAsync(
                c => c.Scope == TokenQuotaScope.Personal && c.UserId == child.OwnerUserId,
                cancellationToken: cancellationToken);
            if (personalConfig != null)
            {
                return await RolloverIfExpiredAsync(personalConfig, cancellationToken);
            }
        }

        var systemConfig = await repo.FirstOrDefaultAsync(
            c => c.Scope == TokenQuotaScope.System, cancellationToken: cancellationToken);
        return systemConfig != null ? await RolloverIfExpiredAsync(systemConfig, cancellationToken) : null;
    }

    private async Task<TokenQuotaConfig> RolloverIfExpiredAsync(TokenQuotaConfig config, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (config.PeriodEnd >= today)
        {
            return config;
        }

        while (config.PeriodEnd < today)
        {
            var spanDays = Math.Max(config.PeriodEnd.DayNumber - config.PeriodStart.DayNumber, 1);
            config.PeriodStart = config.PeriodEnd.AddDays(1);
            config.PeriodEnd = config.PeriodStart.AddDays(spanDays);
        }

        config.QuotaUsed = 0;
        _unitOfWork.Repository<TokenQuotaConfig>().Update(config);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return config;
    }
}
```

- [ ] **Step 6: Run the tests to confirm they pass**

```bash
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~TokenQuotaServiceTests"
```

Expected: all tests in this file PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Core/StoryPlatform.Application/Features/TokenQuota tests/StoryPlatform.UnitTests/Features/TokenQuota
git commit -m "feat(token-quota): add hierarchical quota resolve, lazy rollover, enforce and increment"
```

---

### Task 3: `TokenQuotaService.CreditAsync`

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs`
- Modify (append tests): `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs`

**Interfaces:**
- Consumes: `ProfileScope` (`StoryPlatform.Domain.Enums`).
- Produces: `Task CreditAsync(ProfileScope planScope, int payerUserId, int? organizationId, int quotaAmount, CancellationToken ct = default)` — called by Task 8 (`PaymentService`).

- [ ] **Step 1: Write the failing tests**

Append to `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs`, right before the final closing `}`:

```csharp

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
```

- [ ] **Step 2: Run to confirm failure**

```bash
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~CreditAsync"
```

Expected: FAIL — `CreditAsync` still throws `NotImplementedException`.

- [ ] **Step 3: Implement `CreditAsync`**

In `src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs`, replace the `CreditAsync` stub:

```csharp
    public async Task CreditAsync(
        ProfileScope planScope, int payerUserId, int? organizationId, int quotaAmount,
        CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<TokenQuotaConfig>();
        var scope = planScope == ProfileScope.Personal ? TokenQuotaScope.Personal : TokenQuotaScope.Organization;

        TokenQuotaConfig? config;
        if (scope == TokenQuotaScope.Personal)
        {
            config = await repo.FirstOrDefaultAsync(
                c => c.Scope == TokenQuotaScope.Personal && c.UserId == payerUserId,
                cancellationToken: cancellationToken);
        }
        else
        {
            if (!organizationId.HasValue)
            {
                throw new BadRequestException("OrganizationId là bắt buộc để cộng quota cho gói Organization.");
            }

            config = await repo.FirstOrDefaultAsync(
                c => c.Scope == TokenQuotaScope.Organization && c.OrganizationId == organizationId,
                cancellationToken: cancellationToken);
        }

        if (config == null)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            config = new TokenQuotaConfig
            {
                Scope = scope,
                UserId = scope == TokenQuotaScope.Personal ? payerUserId : null,
                OrganizationId = scope == TokenQuotaScope.Organization ? organizationId : null,
                QuotaLimit = quotaAmount,
                QuotaUsed = 0,
                PeriodStart = today,
                PeriodEnd = today.AddMonths(1)
            };
            await repo.AddAsync(config, cancellationToken);
        }
        else
        {
            config = await RolloverIfExpiredAsync(config, cancellationToken);
            config.QuotaLimit += quotaAmount;
            repo.Update(config);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
```

- [ ] **Step 4: Run to confirm pass**

```bash
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~TokenQuotaServiceTests"
```

Expected: all tests in the file PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs
git commit -m "feat(token-quota): implement CreditAsync top-up/create logic"
```

---

### Task 4: `TokenQuotaService` admin CRUD + supervisor status read

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs`
- Modify (append tests): `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs`

**Interfaces:**
- Produces: `SetConfigAsync`, `ListConfigsAsync`, `GetStatusForChildAsync` — called by `TokenQuotaController` in Task 5.

- [ ] **Step 1: Write the failing tests**

Append to `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs`:

```csharp

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
```

- [ ] **Step 2: Run to confirm failure**

```bash
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~TokenQuotaServiceTests"
```

Expected: the new tests FAIL with `NotImplementedException` (the earlier ones still PASS).

- [ ] **Step 3: Implement the three methods**

In `src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs`, add `using System.Linq.Expressions;` to the usings, then replace the three stub methods:

```csharp
    public async Task<TokenQuotaConfigDto> SetConfigAsync(
        int adminUserId, SetTokenQuotaConfigRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<TokenQuotaScope>(request.Scope, ignoreCase: true, out var scope))
        {
            throw new BadRequestException("Scope phải là 'System', 'Organization', 'Child' hoặc 'Personal'.");
        }

        if (request.PeriodEnd <= request.PeriodStart)
        {
            throw new BadRequestException("PeriodEnd phải sau PeriodStart.");
        }

        if (request.QuotaLimit < 0)
        {
            throw new BadRequestException("QuotaLimit không được âm.");
        }

        if (scope == TokenQuotaScope.Organization && !request.OrganizationId.HasValue)
        {
            throw new BadRequestException("OrganizationId là bắt buộc cho scope Organization.");
        }

        if (scope == TokenQuotaScope.Child && !request.ChildProfileId.HasValue)
        {
            throw new BadRequestException("ChildProfileId là bắt buộc cho scope Child.");
        }

        if (scope == TokenQuotaScope.Personal && !request.UserId.HasValue)
        {
            throw new BadRequestException("UserId là bắt buộc cho scope Personal.");
        }

        Expression<Func<TokenQuotaConfig, bool>> matchPredicate = scope switch
        {
            TokenQuotaScope.System => c => c.Scope == TokenQuotaScope.System,
            TokenQuotaScope.Organization => c => c.Scope == TokenQuotaScope.Organization && c.OrganizationId == request.OrganizationId,
            TokenQuotaScope.Child => c => c.Scope == TokenQuotaScope.Child && c.ChildProfileId == request.ChildProfileId,
            TokenQuotaScope.Personal => c => c.Scope == TokenQuotaScope.Personal && c.UserId == request.UserId,
            _ => throw new BadRequestException("Scope không hợp lệ.")
        };

        var repo = _unitOfWork.Repository<TokenQuotaConfig>();
        var existing = await repo.FirstOrDefaultAsync(matchPredicate, cancellationToken: cancellationToken);

        object beforeState = existing == null
            ? new { existed = false }
            : new { existing.QuotaLimit, PeriodStart = existing.PeriodStart.ToString(), PeriodEnd = existing.PeriodEnd.ToString() };

        if (existing == null)
        {
            existing = new TokenQuotaConfig
            {
                Scope = scope,
                OrganizationId = request.OrganizationId,
                ChildProfileId = request.ChildProfileId,
                UserId = request.UserId,
                QuotaLimit = request.QuotaLimit,
                QuotaUsed = 0,
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd
            };
            await repo.AddAsync(existing, cancellationToken);
        }
        else
        {
            existing.QuotaLimit = request.QuotaLimit;
            existing.PeriodStart = request.PeriodStart;
            existing.PeriodEnd = request.PeriodEnd;
            repo.Update(existing);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "TokenQuotaConfigSet", nameof(TokenQuotaConfig), existing.Id,
            beforeState,
            new { existing.QuotaLimit, PeriodStart = existing.PeriodStart.ToString(), PeriodEnd = existing.PeriodEnd.ToString() },
            cancellationToken);

        return MapToDto(existing);
    }

    public async Task<List<TokenQuotaConfigDto>> ListConfigsAsync(CancellationToken cancellationToken = default)
    {
        var configs = await _unitOfWork.Repository<TokenQuotaConfig>().FindAsync(
            c => true, cancellationToken: cancellationToken);
        return configs.Select(MapToDto).ToList();
    }

    public async Task<TokenQuotaStatusDto> GetStatusForChildAsync(
        int requestingUserId, int childProfileId, CancellationToken cancellationToken = default)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);

        var requester = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(requestingUserId, cancellationToken);
        var isAdministrator = requester?.Role == UserRole.Administrator;
        if (!isAdministrator)
        {
            var hasActiveSupervision = await _unitOfWork.Repository<SupervisionRelationship>().ExistsAsync(
                r => r.ChildProfileId == childProfileId && r.SupervisorUserId == requestingUserId && r.RevokedAt == null,
                cancellationToken);
            if (!hasActiveSupervision)
            {
                throw new ForbiddenException("Bạn không có quyền xem quota của hồ sơ trẻ này.");
            }
        }

        var config = await ResolveApplicableConfigAsync(child, cancellationToken);
        if (config == null)
        {
            return new TokenQuotaStatusDto { IsUnlimited = true };
        }

        return new TokenQuotaStatusDto
        {
            IsUnlimited = false,
            Scope = config.Scope.ToString(),
            QuotaLimit = config.QuotaLimit,
            QuotaUsed = config.QuotaUsed,
            Remaining = Math.Max(config.QuotaLimit - config.QuotaUsed, 0),
            PeriodStart = config.PeriodStart,
            PeriodEnd = config.PeriodEnd
        };
    }

    private static TokenQuotaConfigDto MapToDto(TokenQuotaConfig config) => new()
    {
        Id = config.Id,
        Scope = config.Scope.ToString(),
        OrganizationId = config.OrganizationId,
        ChildProfileId = config.ChildProfileId,
        UserId = config.UserId,
        QuotaLimit = config.QuotaLimit,
        QuotaUsed = config.QuotaUsed,
        PeriodStart = config.PeriodStart,
        PeriodEnd = config.PeriodEnd
    };
```

Also add the two missing usings needed by these methods (`ForbiddenException`, `NotFoundException`, `UserRole`, `UserAccount`, `SupervisionRelationship` are already reachable via existing `using StoryPlatform.Domain.Entities;`/`using StoryPlatform.Domain.Enums;`/`using StoryPlatform.Application.Common.Exceptions;`) — only `System.Linq.Expressions` and `System.Linq` (for `.Select().ToList()`) need adding if not already implicit via `ImplicitUsings`. This project has `<ImplicitUsings>enable</ImplicitUsings>` (see `StoryPlatform.Api.csproj`; Application project uses the same SDK defaults), so only add:

```csharp
using System.Linq.Expressions;
```
at the top of the file.

- [ ] **Step 4: Run to confirm pass**

```bash
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~TokenQuotaServiceTests"
```

Expected: all tests PASS (this file should now have ~19 passing tests).

- [ ] **Step 5: Commit**

```bash
git add src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs
git commit -m "feat(token-quota): implement admin config CRUD and supervisor status read"
```

---

### Task 5: DI registration + `TokenQuotaController`

**Files:**
- Modify: `src/Core/StoryPlatform.Application/DependencyInjection.cs:69` (register right after `IPaymentService`)
- Create: `src/Core/StoryPlatform.Api/Controllers/TokenQuotaController.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaControllerAuthorizationTests.cs`

**Interfaces:**
- Consumes: `ITokenQuotaService` (Task 2-4), `BaseApiController.HandleResult<T>`/`GetCurrentUserId()`.

- [ ] **Step 1: Register the service**

In `src/Core/StoryPlatform.Application/DependencyInjection.cs`, find:

```csharp
        services.AddScoped<IPaymentService, PaymentService>();
```

and add immediately after it:

```csharp
        services.AddScoped<ITokenQuotaService, TokenQuotaService>();
```

Add the two required usings at the top of the file (next to the other `Features.*` usings already there):

```csharp
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;
```

- [ ] **Step 2: Write the controller**

Create `src/Core/StoryPlatform.Api/Controllers/TokenQuotaController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Cấu hình và tra cứu Token Quota (Bước 5.1c).
/// </summary>
public class TokenQuotaController : BaseApiController
{
    private readonly ITokenQuotaService _tokenQuotaService;

    public TokenQuotaController(ITokenQuotaService tokenQuotaService)
    {
        _tokenQuotaService = tokenQuotaService;
    }

    /// <summary>
    /// Tạo mới hoặc cập nhật cấu hình Token Quota cho 1 scope cụ thể.
    /// </summary>
    [HttpPost("config")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<TokenQuotaConfigDto>>> SetConfig(
        [FromBody] SetTokenQuotaConfigRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.SetConfigAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Cấu hình Token Quota thành công.");
    }

    /// <summary>
    /// Danh sách toàn bộ cấu hình Token Quota hiện có.
    /// </summary>
    [HttpGet("config")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<List<TokenQuotaConfigDto>>>> ListConfigs(CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.ListConfigsAsync(cancellationToken);
        return HandleResult(result, "Lấy danh sách Token Quota Config thành công.");
    }

    /// <summary>
    /// Xem mức dùng Token Quota hiện tại của 1 trẻ (Owner/Supervisor/Administrator).
    /// </summary>
    [HttpGet("child/{childProfileId:int}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<TokenQuotaStatusDto>>> GetChildStatus(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.GetStatusForChildAsync(GetCurrentUserId(), childProfileId, cancellationToken);
        return HandleResult(result, "Lấy thông tin Token Quota thành công.");
    }
}
```

- [ ] **Step 3: Write the authorization test**

Create `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaControllerAuthorizationTests.cs` (pattern copied from `tests/StoryPlatform.UnitTests/Features/Administration/AdminAccountControllerAuthorizationTests.cs`):

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.TokenQuota;

public class TokenQuotaControllerAuthorizationTests
{
    [Theory]
    [InlineData(UserRole.Administrator, true)]
    [InlineData(UserRole.Teacher, false)]
    [InlineData(UserRole.Parent, false)]
    public async Task ConfigActions_RequireAdministratorOnly(UserRole role, bool expected)
    {
        foreach (var actionName in new[]
                 {
                     nameof(TokenQuotaController.SetConfig),
                     nameof(TokenQuotaController.ListConfigs)
                 })
        {
            Assert.Equal(expected, await IsAuthorizedAsync(actionName, role));
        }
    }

    [Theory]
    [InlineData(UserRole.Administrator, true)]
    [InlineData(UserRole.Teacher, true)]
    [InlineData(UserRole.Parent, true)]
    public async Task GetChildStatus_AllowsAnyAuthenticatedRole(UserRole role, bool expected)
    {
        Assert.Equal(expected, await IsAuthorizedAsync(nameof(TokenQuotaController.GetChildStatus), role));
    }

    private static async Task<bool> IsAuthorizedAsync(string actionName, UserRole role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, role.ToString()) }, "Bearer"));
        var method = typeof(TokenQuotaController).GetMethod(actionName)!;
        var attributes = typeof(TokenQuotaController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<IAuthorizeData>()
            .Concat(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<IAuthorizeData>());
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, attributes);

        Assert.NotNull(policy);
        return (await authorization.AuthorizeAsync(user, null, policy!)).Succeeded;
    }
}
```

- [ ] **Step 4: Build and run the new tests**

```bash
dotnet build StoryPlatform.sln
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~TokenQuotaController"
```

Expected: build succeeds, all authorization tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/StoryPlatform.Application/DependencyInjection.cs src/Core/StoryPlatform.Api/Controllers/TokenQuotaController.cs tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaControllerAuthorizationTests.cs
git commit -m "feat(token-quota): register service and add admin config / status API"
```

---

### Task 6: Wire enforcement into `AIStoryInputService.SubmitAsync`

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/AIStoryInput/Services/AIStoryInputService.cs`
- Modify: `tests/StoryPlatform.UnitTests/AIStoryInputServiceTests.cs`

**Interfaces:**
- Consumes: `ITokenQuotaService.EnsureWithinQuotaAsync(int, CancellationToken)`, `.IncrementUsageAsync(int, CancellationToken)`.

- [ ] **Step 1: Add the constructor dependency**

In `src/Core/StoryPlatform.Application/Features/AIStoryInput/Services/AIStoryInputService.cs`, add the using and change the constructor:

```csharp
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
```

```csharp
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInputGuardrail _inputGuardrail;
    private readonly ITokenQuotaService _tokenQuotaService;

    public AIStoryInputService(IUnitOfWork unitOfWork, IInputGuardrail inputGuardrail, ITokenQuotaService tokenQuotaService)
    {
        _unitOfWork = unitOfWork;
        _inputGuardrail = inputGuardrail;
        _tokenQuotaService = tokenQuotaService;
    }
```

- [ ] **Step 2: Call `EnsureWithinQuotaAsync` before creating a new request, and `IncrementUsageAsync` inside the transaction**

In the same file, `SubmitAsync` currently reads (existing code, do not change these lines yet — this step only shows where the two new calls are inserted):

```csharp
        if (existing is not null)
        {
            ...
            return await RecoverIfStaleAsync(existing, cancellationToken);
        }

        Story story;
        if (request.ExistingStoryId.HasValue)
        {
```

Insert `EnsureWithinQuotaAsync` right after the `if (existing is not null) { ... }` block and before `Story story;`:

```csharp
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(existing.InputFingerprint),
                    Encoding.ASCII.GetBytes(inputFingerprint)))
            {
                throw new ConflictException("Idempotency key đã được sử dụng cho một nội dung khác.");
            }

            return await RecoverIfStaleAsync(existing, cancellationToken);
        }

        await _tokenQuotaService.EnsureWithinQuotaAsync(request.ChildProfileId, cancellationToken);

        Story story;
        if (request.ExistingStoryId.HasValue)
        {
```

Then, inside the transaction block, currently:

```csharp
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!request.ExistingStoryId.HasValue)
            {
                await _unitOfWork.Repository<Story>().AddAsync(story, cancellationToken);
            }

            await requestRepository.AddAsync(generationRequest, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
```

add the increment call right after `requestRepository.AddAsync`:

```csharp
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!request.ExistingStoryId.HasValue)
            {
                await _unitOfWork.Repository<Story>().AddAsync(story, cancellationToken);
            }

            await requestRepository.AddAsync(generationRequest, cancellationToken);
            await _tokenQuotaService.IncrementUsageAsync(request.ChildProfileId, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
```

- [ ] **Step 3: Update all existing test call sites in one shot**

In `tests/StoryPlatform.UnitTests/AIStoryInputServiceTests.cs`, add these usings near the top:

```csharp
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;
```

Add a private nested no-op audit writer and a helper factory right next to the existing `ThrowingGuardrail`/`FakeUnitOfWork` classes (just above `private sealed class ThrowingGuardrail`):

```csharp
    private static ITokenQuotaService CreateTokenQuotaService(FakeUnitOfWork unitOfWork) =>
        new TokenQuotaService(unitOfWork, new NoopAuditLogWriter());

    private sealed class NoopAuditLogWriter : IAuditLogWriter
    {
        public Task LogAsync(
            int? actorUserId, string action, string entityType, int entityId,
            object? beforeState, object? afterState, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
```

Then replace every identical call site — this exact string appears 9 times in the file:

Old string (repeated, use a project-wide "replace all occurrences in this file" edit): `new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail())`
New string: `new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail(), CreateTokenQuotaService(unitOfWork))`

And the one `ThrowingGuardrail` call site:

Old: `new AIStoryInputService(unitOfWork, new ThrowingGuardrail())`
New: `new AIStoryInputService(unitOfWork, new ThrowingGuardrail(), CreateTokenQuotaService(unitOfWork))`

- [ ] **Step 4: Run the full existing file to confirm nothing broke**

```bash
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~AIStoryInputServiceTests"
```

Expected: all pre-existing tests in this file still PASS (they use a real `TokenQuotaService` against the same `FakeUnitOfWork`, and no `TokenQuotaConfig` is ever seeded in `CreateEligibleUnitOfWork`, so every call resolves to "unlimited" — behavior is unchanged).

- [ ] **Step 5: Add the two new quota-specific tests**

Append to the test file, right before the final `private static SubmitAIStoryInputRequestDto ValidRequest(...)` helper (i.e. as new `[Fact]` methods alongside the others):

```csharp
    [Fact]
    public async Task Quota_exceeded_blocks_submission_and_creates_nothing()
    {
        var unitOfWork = CreateEligibleUnitOfWork();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        unitOfWork.Seed(new StoryPlatform.Domain.Entities.TokenQuotaConfig
        {
            Id = 1,
            Scope = StoryPlatform.Domain.Enums.TokenQuotaScope.Child,
            ChildProfileId = 1,
            QuotaLimit = 1,
            QuotaUsed = 1,
            PeriodStart = today,
            PeriodEnd = today.AddMonths(1)
        });
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail(), CreateTokenQuotaService(unitOfWork));

        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(1, ValidRequest()));

        Assert.Empty(unitOfWork.Items<Story>());
        Assert.Empty(unitOfWork.Items<StoryGenerationRequest>());
    }

    [Fact]
    public async Task Successful_submission_increments_quota_once_and_idempotent_retry_does_not_double_count()
    {
        var unitOfWork = CreateEligibleUnitOfWork();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var config = new StoryPlatform.Domain.Entities.TokenQuotaConfig
        {
            Id = 1,
            Scope = StoryPlatform.Domain.Enums.TokenQuotaScope.Child,
            ChildProfileId = 1,
            QuotaLimit = 5,
            QuotaUsed = 0,
            PeriodStart = today,
            PeriodEnd = today.AddMonths(1)
        };
        unitOfWork.Seed(config);
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail(), CreateTokenQuotaService(unitOfWork));
        var input = ValidRequest();

        await service.SubmitAsync(1, input);
        await service.SubmitAsync(1, input); // same idempotency key -> must not consume a second lượt

        Assert.Equal(1, config.QuotaUsed);
    }
```

- [ ] **Step 6: Run to confirm pass**

```bash
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~AIStoryInputServiceTests"
```

Expected: all tests PASS, including the two new ones.

- [ ] **Step 7: Commit**

```bash
git add src/Core/StoryPlatform.Application/Features/AIStoryInput/Services/AIStoryInputService.cs tests/StoryPlatform.UnitTests/AIStoryInputServiceTests.cs
git commit -m "feat(token-quota): enforce quota check and increment in AIStoryInputService.SubmitAsync"
```

---

### Task 7: Wire crediting into `PaymentService`

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Payments/Services/PaymentService.cs`
- Modify: `tests/StoryPlatform.UnitTests/Features/Payments/PaymentServiceTests.cs`

**Interfaces:**
- Consumes: `ITokenQuotaService.CreditAsync(ProfileScope, int, int?, int, CancellationToken)`.

- [ ] **Step 1: Add the constructor dependency**

In `src/Core/StoryPlatform.Application/Features/Payments/Services/PaymentService.cs`, add the using and extend the constructor:

```csharp
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
```

```csharp
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly ISePayQrUrlBuilder _qrUrlBuilder;
    private readonly ISePayWebhookAuthenticator _webhookAuthenticator;
    private readonly ITokenQuotaService _tokenQuotaService;

    public PaymentService(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService,
        ISePayQrUrlBuilder qrUrlBuilder,
        ISePayWebhookAuthenticator webhookAuthenticator,
        ITokenQuotaService tokenQuotaService)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
        _qrUrlBuilder = qrUrlBuilder;
        _webhookAuthenticator = webhookAuthenticator;
        _tokenQuotaService = tokenQuotaService;
    }
```

- [ ] **Step 2: Credit quota in `HandleWebhookAsync`'s paid branch**

Current code (the `if (transaction.Amount == payload.TransferAmount)` branch inside `HandleWebhookAsync`):

```csharp
        if (transaction.Amount == payload.TransferAmount)
        {
            transaction.Status = PaymentStatus.Paid;
            transaction.PaidAt = DateTime.UtcNow;
            transaction.SepayTransactionId = payload.ReferenceCode;
            _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                null, "PaymentPaidViaWebhook", nameof(PaymentTransaction), transaction.Id,
                beforeState, new { status = transaction.Status.ToString() }, cancellationToken);
            await _notificationService.CreateAsync(
                transaction.PayerUserId, NotificationType.PaymentConfirmed,
                JsonSerializer.Serialize(new { transactionId = transaction.Id }), cancellationToken);
        }
```

Replace it with (adds the `SubscriptionPlan` fetch and `CreditAsync` call at the end):

```csharp
        if (transaction.Amount == payload.TransferAmount)
        {
            transaction.Status = PaymentStatus.Paid;
            transaction.PaidAt = DateTime.UtcNow;
            transaction.SepayTransactionId = payload.ReferenceCode;
            _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                null, "PaymentPaidViaWebhook", nameof(PaymentTransaction), transaction.Id,
                beforeState, new { status = transaction.Status.ToString() }, cancellationToken);
            await _notificationService.CreateAsync(
                transaction.PayerUserId, NotificationType.PaymentConfirmed,
                JsonSerializer.Serialize(new { transactionId = transaction.Id }), cancellationToken);

            var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(transaction.PlanId, cancellationToken);
            if (plan != null)
            {
                await _tokenQuotaService.CreditAsync(
                    plan.ApplicableScope, transaction.PayerUserId, transaction.OrganizationId, plan.QuotaAmount,
                    cancellationToken);
            }
        }
```

- [ ] **Step 3: Credit quota in `MarkPaidManuallyAsync`**

Current code:

```csharp
        var beforeState = new { status = transaction.Status.ToString() };
        transaction.Status = PaymentStatus.Paid;
        transaction.PaidAt = DateTime.UtcNow;
        _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "PaymentMarkedPaidManually", nameof(PaymentTransaction), transaction.Id,
            beforeState, new { status = transaction.Status.ToString() }, cancellationToken);
        await _notificationService.CreateAsync(
            transaction.PayerUserId, NotificationType.PaymentConfirmed,
            JsonSerializer.Serialize(new { transactionId = transaction.Id }), cancellationToken);

        return MapToDto(transaction, transaction.Plan?.Name ?? string.Empty);
```

Replace it with:

```csharp
        var beforeState = new { status = transaction.Status.ToString() };
        transaction.Status = PaymentStatus.Paid;
        transaction.PaidAt = DateTime.UtcNow;
        _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "PaymentMarkedPaidManually", nameof(PaymentTransaction), transaction.Id,
            beforeState, new { status = transaction.Status.ToString() }, cancellationToken);
        await _notificationService.CreateAsync(
            transaction.PayerUserId, NotificationType.PaymentConfirmed,
            JsonSerializer.Serialize(new { transactionId = transaction.Id }), cancellationToken);

        var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(transaction.PlanId, cancellationToken);
        if (plan != null)
        {
            await _tokenQuotaService.CreditAsync(
                plan.ApplicableScope, transaction.PayerUserId, transaction.OrganizationId, plan.QuotaAmount,
                cancellationToken);
        }

        return MapToDto(transaction, plan?.Name ?? string.Empty);
```

(Note: this also fixes the pre-existing `transaction.Plan?.Name` null-navigation issue by reusing the now-explicitly-fetched `plan`.)

- [ ] **Step 4: Update `PaymentServiceTests.cs` — constructor and the two tests that reach the paid branch**

Add the mock field and wire it into `_sut`:

```csharp
    private readonly Mock<ITokenQuotaService> _tokenQuotaService = new();
```

(add `using StoryPlatform.Application.Features.TokenQuota.Interfaces;` to the usings)

```csharp
        _sut = new PaymentService(
            _unitOfWork.Object, _auditLogWriter.Object, _notificationService.Object,
            _qrUrlBuilder.Object, _webhookAuthenticator.Object, _tokenQuotaService.Object);
```

In `HandleWebhookAsync_AmountMatches_MarksPaidAndNotifies`, add a plan lookup mock before the act (the existing `MakeTransaction` helper already sets `Plan = new SubscriptionPlan { Id = 1, PriceVnd = amount }` on the transaction object itself, but the real code now fetches by id separately, so the repository mock needs it too):

```csharp
    [Fact]
    public async Task HandleWebhookAsync_AmountMatches_MarksPaidAndNotifies()
    {
        var transaction = MakeTransaction(1, "SEPAYABC123", 49000);
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Personal, 49000));

        await _sut.HandleWebhookAsync("valid", new SePayWebhookPayloadDto
        {
            Content = $"chuyen tien {transaction.TransactionCode} thanh toan",
            TransferAmount = 49000,
            ReferenceCode = "FT2600123456"
        });

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        Assert.NotNull(transaction.PaidAt);
        Assert.Equal("FT2600123456", transaction.SepayTransactionId);
        _notificationService.Verify(service => service.CreateAsync(
            transaction.PayerUserId, NotificationType.PaymentConfirmed, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _tokenQuotaService.Verify(q => q.CreditAsync(
            ProfileScope.Personal, transaction.PayerUserId, null, 50, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

In `MarkPaidManuallyAsync_Valid_SetsPaidWritesAuditAndNotifies`, add the same plan lookup and a credit-verification, with an Organization-scoped plan (to also cover the organization branch here):

```csharp
    [Fact]
    public async Task MarkPaidManuallyAsync_Valid_SetsPaidWritesAuditAndNotifies()
    {
        var transaction = MakeTransaction(1, "M3", 49000, PaymentStatus.MismatchAmount);
        transaction.OrganizationId = 5;
        _transactionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Organization, 99000));

        var result = await _sut.MarkPaidManuallyAsync(99, 1);

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        Assert.NotNull(transaction.PaidAt);
        Assert.Equal("Paid", result.Status);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            99, "PaymentMarkedPaidManually", nameof(PaymentTransaction), 1,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(service => service.CreateAsync(
            transaction.PayerUserId, NotificationType.PaymentConfirmed, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _tokenQuotaService.Verify(q => q.CreditAsync(
            ProfileScope.Organization, transaction.PayerUserId, 5, 50, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 5: Add two new tests — mismatch never credits, plan-scoped assertions are independently covered**

Append to the file, right before the final closing `}`:

```csharp

    [Fact]
    public async Task HandleWebhookAsync_AmountMismatch_DoesNotCreditQuota()
    {
        var transaction = MakeTransaction(1, "SEPAYXYZ999", 49000);
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });

        await _sut.HandleWebhookAsync("valid", new SePayWebhookPayloadDto
        {
            Content = $"chuyen tien {transaction.TransactionCode}", TransferAmount = 40000
        });

        _tokenQuotaService.Verify(q => q.CreditAsync(
            It.IsAny<ProfileScope>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 6: Run all payment tests to confirm pass**

```bash
dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~PaymentServiceTests"
```

Expected: all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Core/StoryPlatform.Application/Features/Payments/Services/PaymentService.cs tests/StoryPlatform.UnitTests/Features/Payments/PaymentServiceTests.cs
git commit -m "feat(token-quota): credit quota from SePay webhook and manual payment confirmation"
```

---

### Task 8: Full solution verification

**Files:** none (verification only).

- [ ] **Step 1: Full build**

```bash
dotnet build StoryPlatform.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Full test suite**

```bash
dotnet test StoryPlatform.sln --no-build
```

Expected: all 4 test projects PASS, 0 failed (this repo was at 442/442 before this feature — expect roughly 442 + ~19 (`TokenQuotaServiceTests`) + ~4 (`TokenQuotaControllerAuthorizationTests`) + 2 (`AIStoryInputServiceTests` additions) + 2 (`PaymentServiceTests` additions) ≈ 469, all passing, 0 failed).

- [ ] **Step 3: Re-confirm the migration is applied to the local dev database**

```bash
dotnet ef migrations list --project src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj --startup-project src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj
```

Expected: `AddPersonalScopeToTokenQuota` listed with no "(Pending)" marker.

- [ ] **Step 4: Report**

Summarize for the user: files created/modified, migration applied, test counts before/after, and the two behavior changes now live (quota enforcement in story generation, quota crediting from SePay payments) — this is a scope change worth calling out explicitly since it changes production behavior (users can now be blocked from generating stories once an Administrator configures a limit).

---

## Self-Review Notes (completed during plan authoring)

1. **Spec coverage**: every section of the design spec maps to a task — hierarchy resolve/rollover/enforce/increment (Task 2), CreditAsync (Task 3), admin CRUD + supervisor read (Task 4), API + DI (Task 5), Luồng 2 wiring (Task 6), Payment wiring (Task 7), full verification (Task 8). Nothing in the spec is unaddressed.
2. **Placeholder scan**: no "TBD"/"handle appropriately" strings anywhere in the tasks above; every step has real, complete code.
3. **Type consistency checked**: `ITokenQuotaService` method signatures introduced in Task 2 (`EnsureWithinQuotaAsync`, `IncrementUsageAsync`) and Task 3 (`CreditAsync`) and Task 4 (`SetConfigAsync`, `ListConfigsAsync`, `GetStatusForChildAsync`) are used with identical names/parameter types in Task 5 (controller), Task 6 (`AIStoryInputService`), and Task 7 (`PaymentService`) — verified by re-reading each call site against the interface declared in Task 2, Step 2.
4. **Test-double consistency**: Task 6 reuses the *real* `TokenQuotaService` against the existing hand-rolled `FakeUnitOfWork` (verified generic — works for any entity via `FakeRepository<T>`) rather than inventing a second fake, keeping the existing test file's established style.
