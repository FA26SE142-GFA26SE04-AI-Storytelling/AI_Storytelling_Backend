using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.PlanBenefits.Interfaces;

/// <summary>Đọc cấu hình chung của gói (dòng Id = 1); chưa có dòng thì trả cấu hình mặc định.</summary>
public interface IPlanSettingsProvider
{
    Task<PlanSettings> GetAsync(CancellationToken cancellationToken = default);
}
