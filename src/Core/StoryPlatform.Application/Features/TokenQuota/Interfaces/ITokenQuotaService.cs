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
