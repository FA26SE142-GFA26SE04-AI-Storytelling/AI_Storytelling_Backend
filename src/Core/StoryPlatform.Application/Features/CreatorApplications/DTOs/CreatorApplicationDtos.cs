using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.CreatorApplications.DTOs;

/// <summary>Một đơn đăng ký Content Creator.</summary>
public class CreatorApplicationDto
{
    public int Id { get; set; }
    public int ApplicantUserId { get; set; }
    public string ApplicantUsername { get; set; } = string.Empty;
    public string ApplicantFullName { get; set; } = string.Empty;
    public string ApplicantEmail { get; set; } = string.Empty;

    /// <summary>Trạng thái tài khoản: Registered nghĩa là Creator chưa xác thực email nên chưa duyệt được.</summary>
    public string ApplicantStatus { get; set; } = string.Empty;

    public string Introduction { get; set; } = string.Empty;
    public string? PortfolioUrl { get; set; }

    /// <summary>Pending, Approved hoặc Rejected.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Tên Admin đã xử lý đơn; chỉ trả cho Admin.</summary>
    public string? ReviewedByName { get; set; }

    public string? RejectionReason { get; set; }
}

public class ResubmitCreatorApplicationRequestDto
{
    [Required(ErrorMessage = "Giới thiệu bản thân không được để trống.")]
    [StringLength(1000, MinimumLength = 20, ErrorMessage = "Giới thiệu bản thân từ 20 đến 1000 ký tự.")]
    public string Introduction { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Link portfolio tối đa 500 ký tự.")]
    [Url(ErrorMessage = "Link portfolio không đúng định dạng.")]
    public string? PortfolioUrl { get; set; }
}

public class RejectCreatorApplicationRequestDto
{
    [Required(ErrorMessage = "Cần ghi lý do từ chối.")]
    [StringLength(500, ErrorMessage = "Lý do từ chối tối đa 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}
