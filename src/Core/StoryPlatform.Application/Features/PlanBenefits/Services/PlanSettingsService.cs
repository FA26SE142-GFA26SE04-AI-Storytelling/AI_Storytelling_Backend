using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.PlanBenefits.Services;

/// <summary>
/// Admin xem và sửa cấu hình chung (Luồng 5, 5a). Các dịch vụ khác chỉ đọc qua <see cref="IPlanSettingsProvider"/>.
/// </summary>
public class PlanSettingsService(IUnitOfWork unitOfWork, IPlanSettingsProvider provider, IAuditLogWriter auditLogWriter)
    : IPlanSettingsService
{
    public async Task<PlanSettingsDto> GetAsync(CancellationToken cancellationToken = default) =>
        Map(await provider.GetAsync(cancellationToken));

    public async Task<PlanSettingsDto> UpdateAsync(
        int adminUserId, UpdatePlanSettingsRequestDto request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        var repository = unitOfWork.Repository<PlanSettings>();
        var settings = await repository.FirstOrDefaultAsync(
            item => item.Id == PlanSettings.SingletonId, cancellationToken: cancellationToken);
        var isNew = settings == null;
        settings ??= new PlanSettings { Id = PlanSettings.SingletonId };
        var before = Map(settings);

        settings.ReadingFundPercent = request.ReadingFundPercent;
        settings.CreativeFundPercent = request.CreativeFundPercent;
        settings.CreatorMonthlyAiCredits = request.CreatorMonthlyAiCredits;
        settings.CreatorMonthlySubmitCredits = request.CreatorMonthlySubmitCredits;
        settings.PartnerMonthlyAiCredits = request.PartnerMonthlyAiCredits;
        settings.PartnerMonthlySubmitCredits = request.PartnerMonthlySubmitCredits;
        settings.MinimumReadSeconds = request.MinimumReadSeconds;
        settings.TrialAiCredits = request.TrialAiCredits;
        settings.FreeMaxChildProfiles = request.FreeMaxChildProfiles;
        settings.FreeWeeklyNewLibraryStories = request.FreeWeeklyNewLibraryStories;
        settings.FreeCanCopyRoadmapTemplates = request.FreeCanCopyRoadmapTemplates;
        settings.FreeReadAloudPerMonth = request.FreeReadAloudPerMonth;
        settings.FreeWritingAssistPerDay = request.FreeWritingAssistPerDay;
        settings.CreatorWritingAssistPerDay = request.CreatorWritingAssistPerDay;
        settings.PartnerWritingAssistPerDay = request.PartnerWritingAssistPerDay;
        settings.UpdatedAt = DateTime.UtcNow;

        if (isNew) await repository.AddAsync(settings, cancellationToken);
        else repository.Update(settings);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var after = Map(settings);
        await auditLogWriter.LogAsync(
            adminUserId, "PlanSettingsUpdated", nameof(PlanSettings), settings.Id, before, after, cancellationToken);
        return after;
    }

    private static void Validate(UpdatePlanSettingsRequestDto request)
    {
        if (request.ReadingFundPercent is < 0 or > 100 || request.CreativeFundPercent is < 0 or > 100)
        {
            throw new BadRequestException("Tỷ lệ quỹ Creator phải từ 0 đến 100%.");
        }

        if (request.MinimumReadSeconds is < 1 or > 3600)
        {
            throw new BadRequestException("Thời gian đọc tối thiểu phải từ 1 đến 3600 giây.");
        }

        if (request.FreeMaxChildProfiles < 1)
        {
            throw new BadRequestException("Mức Miễn phí phải cho phép ít nhất 1 hồ sơ trẻ.");
        }

        int?[] counts =
        [
            request.CreatorMonthlyAiCredits, request.CreatorMonthlySubmitCredits, request.PartnerMonthlyAiCredits,
            request.PartnerMonthlySubmitCredits, request.TrialAiCredits, request.FreeWeeklyNewLibraryStories,
            request.FreeReadAloudPerMonth, request.FreeWritingAssistPerDay, request.CreatorWritingAssistPerDay,
            request.PartnerWritingAssistPerDay
        ];
        if (counts.Any(value => value < 0))
        {
            throw new BadRequestException("Số lượt và số lần không được âm.");
        }
    }

    private static PlanSettingsDto Map(PlanSettings settings) => new()
    {
        ReadingFundPercent = settings.ReadingFundPercent,
        CreativeFundPercent = settings.CreativeFundPercent,
        CreatorMonthlyAiCredits = settings.CreatorMonthlyAiCredits,
        CreatorMonthlySubmitCredits = settings.CreatorMonthlySubmitCredits,
        PartnerMonthlyAiCredits = settings.PartnerMonthlyAiCredits,
        PartnerMonthlySubmitCredits = settings.PartnerMonthlySubmitCredits,
        MinimumReadSeconds = settings.MinimumReadSeconds,
        TrialAiCredits = settings.TrialAiCredits,
        FreeMaxChildProfiles = settings.FreeMaxChildProfiles,
        FreeWeeklyNewLibraryStories = settings.FreeWeeklyNewLibraryStories,
        FreeCanCopyRoadmapTemplates = settings.FreeCanCopyRoadmapTemplates,
        FreeReadAloudPerMonth = settings.FreeReadAloudPerMonth,
        FreeWritingAssistPerDay = settings.FreeWritingAssistPerDay,
        CreatorWritingAssistPerDay = settings.CreatorWritingAssistPerDay,
        PartnerWritingAssistPerDay = settings.PartnerWritingAssistPerDay,
        UpdatedAt = settings.UpdatedAt
    };
}
