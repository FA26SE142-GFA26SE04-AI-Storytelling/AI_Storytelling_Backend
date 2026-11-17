using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Common;

/// <summary>
/// Giới hạn sinh lại miễn phí trong một truyện (Luồng 2 và 3; Admin cấu hình ở Luồng 5, 5a): dàn ý, ảnh và audio.
/// Lần sinh lại bị lỗi kỹ thuật không tính; lần đang chạy có tính để hai yêu cầu cùng lúc không vượt giới hạn.
/// Giới hạn sinh lại nội dung chỉ được lưu: hệ thống chưa có chức năng sinh lại toàn bộ nội dung.
/// </summary>
public static class StoryRegenerationLimits
{
    public const string LimitReachedCode = "REGENERATION_LIMIT_REACHED";

    public static async Task<StoryRegenerationUsage> GetUsageAsync(
        IUnitOfWork unitOfWork, IPlanSettingsProvider? planSettings, int storyId, CancellationToken cancellationToken)
    {
        var settings = planSettings == null ? new PlanSettings() : await planSettings.GetAsync(cancellationToken);
        var outline = await unitOfWork.Repository<StoryGenerationJob>().CountAsync(
            job => job.StoryId == storyId && job.Operation == GenerationJobOperation.RegenerateOutline
                   && job.Status != GenerationJobStatus.Failed && job.Status != GenerationJobStatus.Cancelled,
            cancellationToken);
        var versionIds = (await unitOfWork.Repository<StoryVersion>().FindAsync(
                version => version.StoryId == storyId, cancellationToken: cancellationToken))
            .Select(version => version.Id)
            .ToList();
        var replacements = await unitOfWork.Repository<MediaAsset>().FindAsync(
            asset => versionIds.Contains(asset.StoryVersionId) && asset.ReplacesMediaAssetId != null
                     && asset.Status != MediaStatus.Failed,
            cancellationToken: cancellationToken);

        return new StoryRegenerationUsage(
            new RegenerationCounter(outline, settings.OutlineRegenerationLimit),
            new RegenerationCounter(0, settings.ContentRegenerationLimit),
            new RegenerationCounter(replacements.Count(asset => asset.Type == MediaType.Illustration), settings.ImageRegenerationLimit),
            new RegenerationCounter(replacements.Count(asset => asset.Type == MediaType.TtsAudio), settings.AudioRegenerationLimit));
    }

    /// <summary>Ném 409 <see cref="LimitReachedCode"/> khi đã dùng hết số lần sinh lại của loại này.</summary>
    public static void EnsureAvailable(RegenerationCounter counter, string what)
    {
        if (counter.Remaining <= 0)
        {
            throw new ConflictException(
                $"{LimitReachedCode}: đã dùng hết {counter.Limit} lần sinh lại {what} miễn phí của truyện này.");
        }
    }
}

/// <summary>Số lần đã sinh lại và giới hạn của từng loại trong một truyện.</summary>
public sealed record StoryRegenerationUsage(
    RegenerationCounter Outline, RegenerationCounter Content, RegenerationCounter Images, RegenerationCounter Audio);

public sealed record RegenerationCounter(int Used, int Limit)
{
    public int Remaining => Math.Max(0, Limit - Used);
}
