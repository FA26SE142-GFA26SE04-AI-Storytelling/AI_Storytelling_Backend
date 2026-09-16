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

    public async Task<TokenQuotaConfigDto> SetConfigAsync(
        int adminUserId, SetTokenQuotaConfigRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<TokenQuotaScope>(request.Scope, ignoreCase: true, out var scope))
        {
            throw new BadRequestException($"Scope '{request.Scope}' không hợp lệ.");
        }

        if (request.PeriodEnd < request.PeriodStart)
        {
            throw new BadRequestException("PeriodEnd phải >= PeriodStart.");
        }

        // Validate scope-specific required fields
        if (scope == TokenQuotaScope.Organization && !request.OrganizationId.HasValue)
        {
            throw new BadRequestException("OrganizationId là bắt buộc cho Organization scope.");
        }
        if (scope == TokenQuotaScope.Child && !request.ChildProfileId.HasValue)
        {
            throw new BadRequestException("ChildProfileId là bắt buộc cho Child scope.");
        }
        if (scope == TokenQuotaScope.Personal && !request.UserId.HasValue)
        {
            throw new BadRequestException("UserId là bắt buộc cho Personal scope.");
        }

        var repo = _unitOfWork.Repository<TokenQuotaConfig>();

        // Try to find existing config
        TokenQuotaConfig? existing = null;
        if (scope == TokenQuotaScope.System)
        {
            existing = await repo.FirstOrDefaultAsync(c => c.Scope == TokenQuotaScope.System, cancellationToken: cancellationToken);
        }
        else if (scope == TokenQuotaScope.Organization)
        {
            existing = await repo.FirstOrDefaultAsync(
                c => c.Scope == TokenQuotaScope.Organization && c.OrganizationId == request.OrganizationId,
                cancellationToken: cancellationToken);
        }
        else if (scope == TokenQuotaScope.Child)
        {
            existing = await repo.FirstOrDefaultAsync(
                c => c.Scope == TokenQuotaScope.Child && c.ChildProfileId == request.ChildProfileId,
                cancellationToken: cancellationToken);
        }
        else if (scope == TokenQuotaScope.Personal)
        {
            existing = await repo.FirstOrDefaultAsync(
                c => c.Scope == TokenQuotaScope.Personal && c.UserId == request.UserId,
                cancellationToken: cancellationToken);
        }

        TokenQuotaConfig config;
        if (existing == null)
        {
            config = new TokenQuotaConfig
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
            await repo.AddAsync(config, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                adminUserId, "TokenQuotaConfigSet", nameof(TokenQuotaConfig), config.Id,
                null, cancellationToken);
        }
        else
        {
            var beforeState = new { existing.QuotaLimit, PeriodStart = existing.PeriodStart.ToString(), PeriodEnd = existing.PeriodEnd.ToString() };

            existing.QuotaLimit = request.QuotaLimit;
            existing.PeriodStart = request.PeriodStart;
            existing.PeriodEnd = request.PeriodEnd;
            repo.Update(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                adminUserId, "TokenQuotaConfigSet", nameof(TokenQuotaConfig), existing.Id,
                beforeState, cancellationToken);

            config = existing;
        }

        return MapToDto(config);
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
