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
