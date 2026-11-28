using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.PlanBenefits.Services;

/// <summary>
/// Quyền lợi lấy từ gói đang hiệu lực và hạn mức truyện thư viện mới lấy từ giao dịch của chu kỳ đó.
/// Không có chu kỳ hiệu lực → mức Miễn phí trong cấu hình.
/// </summary>
public class PlanBenefitService(IUnitOfWork unitOfWork, IPlanSettingsProvider settingsProvider, TimeProvider? clock = null)
    : IPlanBenefitService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<PlanBenefitsDto> GetForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetAsync(cancellationToken);
        var user = await unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (user?.Role == UserRole.ContentCreator)
        {
            // Creator không mua gói phụ huynh: phần đọc giữ mức Miễn phí, chỉ trợ lý viết theo hạn mức Creator.
            var creator = FreeBenefits(settings, now);
            // Creator đối tác (Luồng 5, 5g): trợ lý viết 40 lần mỗi ngày thay vì 20.
            var partner = user.CreatorTier == CreatorTier.Partner;
            creator.Tier = partner ? PlanBenefitsDto.PartnerCreatorTier : PlanBenefitsDto.CreatorTier;
            creator.WritingAssistPerDay = partner ? settings.PartnerWritingAssistPerDay : settings.CreatorWritingAssistPerDay;
            return creator;
        }

        var cycles = await unitOfWork.Repository<UserSubscription>().FindAsync(
            item => item.UserId == userId && item.Status == SubscriptionStatus.Active
                    && item.StartAt <= now && item.EndAt > now,
            includeProperties: "Plan", cancellationToken: cancellationToken);
        var cycle = cycles.FirstOrDefault(item => item.Plan is { PlanType: PlanType.Reading or PlanType.Creative });
        PlanBenefitsDto benefits;
        if (cycle?.Plan is { } plan)
        {
            var payment = await unitOfWork.Repository<PaymentTransaction>().GetByIdAsync(cycle.PaymentId, cancellationToken);
            benefits = new PlanBenefitsDto
            {
                Tier = plan.Name,
                PlanId = plan.Id,
                MaxChildProfiles = plan.MaxChildProfiles,
                WeeklyNewLibraryStories = payment?.NewLibraryStoriesLimit,
                NewLibraryStoriesPeriod = PlanBenefitsDto.CyclePeriod,
                NewLibraryStoriesPeriodStart = cycle.StartAt,
                NewLibraryStoriesResetsAt = cycle.EndAt,
                CanCopyRoadmapTemplates = plan.CanCopyRoadmapTemplates,
                ReadAloudPerMonth = plan.ReadAloudPerMonth,
                WritingAssistPerDay = plan.WritingAssistPerDay
            };
        }
        else
        {
            benefits = FreeBenefits(settings, now);
        }

        if (user is { Role: UserRole.User, WeeklyNewLibraryStoriesOverride: >= 0 }
            && benefits.WeeklyNewLibraryStories.HasValue)
        {
            benefits.WeeklyNewLibraryStories = Math.Max(
                benefits.WeeklyNewLibraryStories.Value, user.WeeklyNewLibraryStoriesOverride.Value);
        }

        return benefits;
    }

    private static PlanBenefitsDto FreeBenefits(PlanSettings settings, DateTime now) => new()
    {
        Tier = PlanBenefitsDto.FreeTier,
        MaxChildProfiles = settings.FreeMaxChildProfiles,
        WeeklyNewLibraryStories = settings.FreeWeeklyNewLibraryStories,
        NewLibraryStoriesPeriodStart = VietnamTime.StartOfWeekUtc(now),
        NewLibraryStoriesResetsAt = VietnamTime.StartOfNextWeekUtc(now),
        CanCopyRoadmapTemplates = settings.FreeCanCopyRoadmapTemplates,
        ReadAloudPerMonth = settings.FreeReadAloudPerMonth,
        WritingAssistPerDay = settings.FreeWritingAssistPerDay
    };
}
