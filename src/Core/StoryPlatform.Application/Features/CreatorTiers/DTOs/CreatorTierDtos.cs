using System.ComponentModel.DataAnnotations;
using StoryPlatform.Application.Features.CreatorStatistics.DTOs;

namespace StoryPlatform.Application.Features.CreatorTiers.DTOs;

/// <summary>Cấp hiện tại, tiến độ 4 tiêu chí và lịch sử xét của một Creator, cho Admin (Luồng 5, 5g).</summary>
public class CreatorTierOverviewDto
{
    public int CreatorUserId { get; set; }

    /// <summary>Standard hoặc Partner.</summary>
    public string Tier { get; set; } = string.Empty;

    public DateTime? PartnerSince { get; set; }

    /// <summary>Tiến độ tính lúc gọi (cửa sổ 90 ngày kết thúc hôm nay).</summary>
    public PartnerProgressDto Progress { get; set; } = new();

    /// <summary>Các lần xét hằng tháng, mới nhất trước (tối đa 12).</summary>
    public List<CreatorTierEvaluationDto> Evaluations { get; set; } = [];
}

public class CreatorTierEvaluationDto
{
    public DateTime PeriodMonth { get; set; }
    public int PublishedStories { get; set; }
    public decimal CompletionRate { get; set; }
    public int ValidReads { get; set; }
    public int ViolationRemovals { get; set; }
    public bool Eligible { get; set; }
    public string TierBefore { get; set; } = string.Empty;
    public string TierAfter { get; set; } = string.Empty;
    public DateTime EvaluatedAt { get; set; }
}

public class RevokePartnerTierRequestDto
{
    [Required(ErrorMessage = "Cần ghi lý do thu hồi.")]
    [StringLength(500, ErrorMessage = "Lý do thu hồi tối đa 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}
