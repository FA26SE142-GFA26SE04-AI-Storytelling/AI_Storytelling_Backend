using StoryPlatform.Application.Features.PlanBenefits.DTOs;

namespace StoryPlatform.Application.Features.PlanBenefits.Interfaces;

/// <summary>Quyền lợi hiệu lực của một tài khoản (Luồng 2, 2h).</summary>
public interface IPlanBenefitService
{
    Task<PlanBenefitsDto> GetForUserAsync(int userId, CancellationToken cancellationToken = default);
}
