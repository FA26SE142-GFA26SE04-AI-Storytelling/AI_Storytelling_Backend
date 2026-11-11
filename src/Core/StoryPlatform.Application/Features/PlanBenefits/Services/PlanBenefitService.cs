using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.PlanBenefits.Services;

/// <summary>
/// Quyền lợi đọc trực tiếp từ dòng gói của chu kỳ đang hiệu lực, nên Admin sửa quyền lợi thì người đang dùng gói
/// nhận ngay (khác giá và lượt, vốn chốt theo giao dịch). Không có chu kỳ hiệu lực → mức Miễn phí trong cấu hình.
/// </summary>
public class PlanBenefitService(IUnitOfWork unitOfWork, IPlanSettingsProvider settingsProvider, TimeProvider? clock = null)
    : IPlanBenefitService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<PlanBenefitsDto> GetForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetAsync(cancellationToken);
        var user = await unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user?.Role == UserRole.ContentCreator)
        {
            // Creator không mua gói phụ huynh: phần đọc giữ mức Miễn phí, chỉ trợ lý viết theo hạn mức Creator.
            var creator = FreeBenefits(settings);
            creator.Tier = PlanBenefitsDto.CreatorTier;
            creator.WritingAssistPerDay = settings.CreatorWritingAssistPerDay;
            return creator;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var cycles = await unitOfWork.Repository<UserSubscription>().FindAsync(
            item => item.UserId == userId && item.Status == SubscriptionStatus.Active
                    && item.StartAt <= now && item.EndAt > now,
            includeProperties: "Plan", cancellationToken: cancellationToken);
        var plan = cycles
            .Select(item => item.Plan)
            .FirstOrDefault(item => item is { PlanType: PlanType.Reading or PlanType.Creative });

        return plan == null
            ? FreeBenefits(settings)
            : new PlanBenefitsDto
            {
                Tier = plan.Name,
                PlanId = plan.Id,
                MaxChildProfiles = plan.MaxChildProfiles,
                WeeklyNewLibraryStories = plan.WeeklyNewLibraryStories,
                CanCopyRoadmapTemplates = plan.CanCopyRoadmapTemplates,
                ReadAloudPerMonth = plan.ReadAloudPerMonth,
                WritingAssistPerDay = plan.WritingAssistPerDay
            };
    }

    private static PlanBenefitsDto FreeBenefits(PlanSettings settings) => new()
    {
        Tier = PlanBenefitsDto.FreeTier,
        MaxChildProfiles = settings.FreeMaxChildProfiles,
        WeeklyNewLibraryStories = settings.FreeWeeklyNewLibraryStories,
        CanCopyRoadmapTemplates = settings.FreeCanCopyRoadmapTemplates,
        ReadAloudPerMonth = settings.FreeReadAloudPerMonth,
        WritingAssistPerDay = settings.FreeWritingAssistPerDay
    };
}
