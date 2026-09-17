using System.Linq.Expressions;
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
        // Pure read: never attach/persist a rollover here. If a rollover is actually due it is
        // performed exactly once, later, by IncrementUsageAsync — attaching the same config row
        // from both this method and IncrementUsageAsync within one SubmitAsync flow is what
        // caused the EF identity-map "instance already tracked" conflict.
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var config = await ResolveApplicableConfigRawAsync(child, cancellationToken);
        if (config == null)
        {
            return;
        }

        var effective = ComputeEffectiveView(config, DateOnly.FromDateTime(DateTime.UtcNow));
        if (effective.QuotaUsed >= effective.QuotaLimit)
        {
            throw new ConflictException(
                "Đã hết lượt sinh truyện AI trong chu kỳ hiện tại. Vui lòng mua thêm quota hoặc chờ tới kỳ reset tiếp theo.");
        }
    }

    public async Task IncrementUsageAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        // The only place in the Ensure->Increment flow that actually persists a rollover. Rollover
        // (if due) and the usage increment are applied in-memory together, then attached/saved in a
        // single Update/SaveChanges call — at most one attach per config row.
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var config = await ResolveApplicableConfigRawAsync(child, cancellationToken);
        if (config == null)
        {
            return;
        }

        ApplyRolloverIfExpired(config, DateOnly.FromDateTime(DateTime.UtcNow));
        config.QuotaUsed += 1;
        _unitOfWork.Repository<TokenQuotaConfig>().Update(config);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

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

        // GET-style read: no persisted rollover side effect. Show what the status would be
        // as-if a rollover happened, without attaching/saving anything.
        var config = await ResolveApplicableConfigRawAsync(child, cancellationToken);
        if (config == null)
        {
            return new TokenQuotaStatusDto { IsUnlimited = true };
        }

        var effective = ComputeEffectiveView(config, DateOnly.FromDateTime(DateTime.UtcNow));
        return new TokenQuotaStatusDto
        {
            IsUnlimited = false,
            Scope = config.Scope.ToString(),
            QuotaLimit = effective.QuotaLimit,
            QuotaUsed = effective.QuotaUsed,
            Remaining = Math.Max(effective.QuotaLimit - effective.QuotaUsed, 0),
            PeriodStart = effective.PeriodStart,
            PeriodEnd = effective.PeriodEnd
        };
    }

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

    /// <summary>
    /// Resolves the applicable config for a child by scope hierarchy (Child -> Organization/Personal
    /// -> System) as a pure read — never rolls over or persists anything. Callers decide whether they
    /// need a real (persisted) rollover (<see cref="IncrementUsageAsync"/>) or just an as-if-rolled-over
    /// view (<see cref="EnsureWithinQuotaAsync"/>, <see cref="GetStatusForChildAsync"/>).
    /// </summary>
    private async Task<TokenQuotaConfig?> ResolveApplicableConfigRawAsync(ChildProfile child, CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<TokenQuotaConfig>();

        var childConfig = await repo.FirstOrDefaultAsync(
            c => c.Scope == TokenQuotaScope.Child && c.ChildProfileId == child.Id,
            cancellationToken: cancellationToken);
        if (childConfig != null)
        {
            return childConfig;
        }

        if (child.Scope == ProfileScope.Organization && child.OrganizationId.HasValue)
        {
            var orgConfig = await repo.FirstOrDefaultAsync(
                c => c.Scope == TokenQuotaScope.Organization && c.OrganizationId == child.OrganizationId,
                cancellationToken: cancellationToken);
            if (orgConfig != null)
            {
                return orgConfig;
            }
        }
        else if (child.Scope == ProfileScope.Personal)
        {
            var personalConfig = await repo.FirstOrDefaultAsync(
                c => c.Scope == TokenQuotaScope.Personal && c.UserId == child.OwnerUserId,
                cancellationToken: cancellationToken);
            if (personalConfig != null)
            {
                return personalConfig;
            }
        }
        // Any other/malformed case (e.g. Scope == Organization with a null OrganizationId, which
        // violates the data invariant) intentionally falls through to the System-scope lookup below
        // instead of silently resolving against the owner's unrelated Personal config.

        return await repo.FirstOrDefaultAsync(
            c => c.Scope == TokenQuotaScope.System, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Computes what a config's period/usage would look like if a rollover happened right now,
    /// without mutating <paramref name="config"/> or persisting anything.
    /// </summary>
    private static (int QuotaLimit, int QuotaUsed, DateOnly PeriodStart, DateOnly PeriodEnd) ComputeEffectiveView(
        TokenQuotaConfig config, DateOnly today)
    {
        if (!TryComputeRolledOverPeriod(config.PeriodStart, config.PeriodEnd, today, out var newStart, out var newEnd))
        {
            return (config.QuotaLimit, config.QuotaUsed, config.PeriodStart, config.PeriodEnd);
        }

        return (config.QuotaLimit, 0, newStart, newEnd);
    }

    /// <summary>
    /// In-memory-only mutation: if the config's period has expired, advances it to the current
    /// period and resets QuotaUsed to 0. Does not attach or save — callers are responsible for
    /// persisting (or not) alongside whatever else they mutate in the same call.
    /// </summary>
    private static bool ApplyRolloverIfExpired(TokenQuotaConfig config, DateOnly today)
    {
        if (!TryComputeRolledOverPeriod(config.PeriodStart, config.PeriodEnd, today, out var newStart, out var newEnd))
        {
            return false;
        }

        config.PeriodStart = newStart;
        config.PeriodEnd = newEnd;
        config.QuotaUsed = 0;
        return true;
    }

    private static bool TryComputeRolledOverPeriod(
        DateOnly periodStart, DateOnly periodEnd, DateOnly today, out DateOnly newPeriodStart, out DateOnly newPeriodEnd)
    {
        newPeriodStart = periodStart;
        newPeriodEnd = periodEnd;
        if (periodEnd >= today)
        {
            return false;
        }

        while (newPeriodEnd < today)
        {
            var spanDays = Math.Max(newPeriodEnd.DayNumber - newPeriodStart.DayNumber, 1);
            newPeriodStart = newPeriodEnd.AddDays(1);
            newPeriodEnd = newPeriodStart.AddDays(spanDays);
        }

        return true;
    }

    /// <summary>
    /// The persisted rollover used only by <see cref="CreditAsync"/>'s top-up-existing-config branch,
    /// which is not paired with any other attach of the same row in that method.
    /// </summary>
    private async Task<TokenQuotaConfig> RolloverIfExpiredAsync(TokenQuotaConfig config, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!ApplyRolloverIfExpired(config, today))
        {
            return config;
        }

        _unitOfWork.Repository<TokenQuotaConfig>().Update(config);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return config;
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
}
