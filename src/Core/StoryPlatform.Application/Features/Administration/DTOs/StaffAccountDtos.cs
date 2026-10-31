using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Administration.DTOs;

/// <summary>Mời một tài khoản vận hành mới; role quyết định là Moderator hay Administrator.</summary>
public class InviteStaffRequestDto
{
    /// <summary>"Moderator" hoặc "Administrator".</summary>
    [Required(ErrorMessage = "Vai trò không được để trống.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UserRole? Role { get; set; }

    [Required(ErrorMessage = "Họ tên không được để trống.")]
    [StringLength(100, ErrorMessage = "Họ tên tối đa 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(254, ErrorMessage = "Email tối đa 254 ký tự.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Bắt buộc với Moderator; bỏ qua với Administrator.</summary>
    [StringLength(200, ErrorMessage = "Chuyên môn tối đa 200 ký tự.")]
    public string? Expertise { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Note { get; set; }
}

public class ReasonRequestDto
{
    [Required(ErrorMessage = "Lý do không được để trống.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Lý do từ 3 đến 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

public class StaffAccountDto
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>"Moderator" hoặc "Administrator".</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Chuyên môn (chỉ Moderator có).</summary>
    public string? Expertise { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True khi tài khoản không còn chờ kích hoạt.</summary>
    public bool IsActivated { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Hạn lời mời đang chờ; null khi tài khoản không còn chờ kích hoạt.</summary>
    public DateTime? InviteExpiresAt { get; set; }
}
