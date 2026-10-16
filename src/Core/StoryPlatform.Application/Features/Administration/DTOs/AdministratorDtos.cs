using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.Administration.DTOs;

public class GrantAdministratorRequestDto
{
    public string Email { get; set; } = string.Empty;
}

public class AdministratorAccountDto
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? RoleBeforeAdmin { get; set; }
}

public class InviteModeratorRequestDto
{
    [Required(ErrorMessage = "Họ tên không được để trống.")]
    [StringLength(100, ErrorMessage = "Họ tên tối đa 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(254, ErrorMessage = "Email tối đa 254 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Chuyên môn không được để trống.")]
    [StringLength(200, ErrorMessage = "Chuyên môn tối đa 200 ký tự.")]
    public string Expertise { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Note { get; set; }
}

public class ReasonRequestDto
{
    [Required(ErrorMessage = "Lý do không được để trống.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Lý do từ 3 đến 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

public class ModeratorAccountDto
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Expertise { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True khi người được mời đã kích hoạt xong.</summary>
    public bool IsActivated { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Hạn lời mời đang chờ; null khi tài khoản không còn chờ kích hoạt.</summary>
    public DateTime? InviteExpiresAt { get; set; }
}
