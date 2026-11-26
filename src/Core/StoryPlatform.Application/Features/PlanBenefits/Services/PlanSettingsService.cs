using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.Interfaces;
using StoryPlatform.Application.Features.AccessControl.Services;

namespace StoryPlatform.Application.Features.PlanBenefits.Services;

/// <summary>
/// Admin xem và sửa cấu hình chung (Luồng 5, 5a). Các dịch vụ khác chỉ đọc qua <see cref="IPlanSettingsProvider"/>.
/// </summary>
public class PlanSettingsService(IUnitOfWork unitOfWork, IPlanSettingsProvider provider, IAuditLogWriter auditLogWriter,
    IAccessPolicy? accessPolicy = null)
    : IPlanSettingsService
{
    public async Task<PlanSettingsDto> GetAsync(CancellationToken cancellationToken = default) =>
        Map(await provider.GetAsync(cancellationToken));

    public async Task<PlanSettingsDto> UpdateAsync(
        int adminUserId, UpdatePlanSettingsRequestDto request, CancellationToken cancellationToken = default)
    {
        await (accessPolicy ?? AccessPolicy.Default(unitOfWork)).EnsureAsync(
            adminUserId, Permissions.Admin.PaymentManage, deniedMessage: "Chỉ quản trị viên đang hoạt động mới thực hiện được thao tác này.", cancellationToken: cancellationToken);
        Validate(request);

        var repository = unitOfWork.Repository<PlanSettings>();
        var settings = await repository.FirstOrDefaultAsync(
            item => item.Id == PlanSettings.SingletonId, cancellationToken: cancellationToken);
        var isNew = settings == null;
        settings ??= new PlanSettings { Id = PlanSettings.SingletonId };
        var before = Map(settings);

        settings.ReadingFundPercent = request.ReadingFundPercent;
        settings.CreativeFundPercent = request.CreativeFundPercent;
        settings.MinimumPayoutVnd = request.MinimumPayoutVnd ?? settings.MinimumPayoutVnd;
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
        settings.OutlineRegenerationLimit = request.OutlineRegenerationLimit;
        settings.ContentRegenerationLimit = request.ContentRegenerationLimit;
        settings.ImageRegenerationLimit = request.ImageRegenerationLimit;
        settings.AudioRegenerationLimit = request.AudioRegenerationLimit;
        settings.UpdatedAt = DateTime.UtcNow;

        if (isNew) await repository.AddAsync(settings, cancellationToken);
        else repository.Update(settings);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var after = Map(settings);
        await auditLogWriter.LogAsync(
            adminUserId, "PlanSettingsUpdated", nameof(PlanSettings), settings.Id, before, after, cancellationToken);
        return after;
    }

    /// <summary>Trần của mức chi tối thiểu cho Creator (VND).</summary>
    public const long MaxMinimumPayoutVnd = 1_000_000_000;

    /// <summary>Trần của giới hạn sinh lại: mỗi lần sinh lại tốn chi phí AI mà không trừ lượt.</summary>
    public const int MaxRegenerationLimit = 20;

    private static void Validate(UpdatePlanSettingsRequestDto request)
    {
        if (request.ReadingFundPercent is < 0 or > 100 || request.CreativeFundPercent is < 0 or > 100)
        {
            throw new BadRequestException("Tỷ lệ quỹ nhà sáng tạo phải từ 0 đến 100%.");
        }

        if (request.MinimumPayoutVnd is < 0 or > MaxMinimumPayoutVnd)
        {
            throw new BadRequestException($"Mức chi tối thiểu cho nhà sáng tạo phải từ 0 đến {MaxMinimumPayoutVnd:N0} đ.");
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

        int[] regenerationLimits =
        [
            request.OutlineRegenerationLimit, request.ContentRegenerationLimit, request.ImageRegenerationLimit,
            request.AudioRegenerationLimit
        ];
        if (regenerationLimits.Any(value => value is < 0 or > MaxRegenerationLimit))
        {
            throw new BadRequestException($"Giới hạn sinh lại phải từ 0 đến {MaxRegenerationLimit} lần.");
        }
    }

    private static PlanSettingsDto Map(PlanSettings settings) => new()
    {
        ReadingFundPercent = settings.ReadingFundPercent,
        CreativeFundPercent = settings.CreativeFundPercent,
        MinimumPayoutVnd = settings.MinimumPayoutVnd,
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
        OutlineRegenerationLimit = settings.OutlineRegenerationLimit,
        ContentRegenerationLimit = settings.ContentRegenerationLimit,
        ImageRegenerationLimit = settings.ImageRegenerationLimit,
        AudioRegenerationLimit = settings.AudioRegenerationLimit,
        UpdatedAt = settings.UpdatedAt
    };
}
