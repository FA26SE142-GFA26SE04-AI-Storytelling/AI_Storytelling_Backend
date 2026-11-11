using StoryPlatform.Application.Features.PlanBenefits.DTOs;

namespace StoryPlatform.Application.Features.PlanBenefits.Interfaces;

/// <summary>Admin xem và sửa cấu hình chung (Luồng 5, 5a); mọi thay đổi ghi audit trước/sau.</summary>
public interface IPlanSettingsService
{
    Task<PlanSettingsDto> GetAsync(CancellationToken cancellationToken = default);

    Task<PlanSettingsDto> UpdateAsync(int adminUserId, UpdatePlanSettingsRequestDto request, CancellationToken cancellationToken = default);
}
