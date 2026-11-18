using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.Administration.DTOs;

/// <summary>Một tài khoản Content Creator trong màn quản lý của Admin (Luồng 5, M5).</summary>
public class CreatorAccountDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Registered (chưa xác thực email), AwaitingApproval (chờ duyệt đơn), Active hoặc Suspended.</summary>
    public string Status { get; set; } = string.Empty;

    public int PublishedStoryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class DeactivateCreatorRequestDto
{
    [Required(ErrorMessage = "Cần ghi lý do vô hiệu hoá.")]
    [StringLength(500, ErrorMessage = "Lý do vô hiệu hoá tối đa 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Kết quả vô hiệu hoá: tài khoản sau thay đổi và số yêu cầu gửi duyệt đã rút / đóng.</summary>
public class DeactivateCreatorResultDto
{
    public CreatorAccountDto Creator { get; set; } = new();

    /// <summary>Yêu cầu đang chờ chuyển Withdrawn, đã hoàn lượt gửi duyệt.</summary>
    public int WithdrawnSubmissions { get; set; }

    /// <summary>Yêu cầu Moderator đã nhận chuyển Closed; Moderator đó nhận thông báo.</summary>
    public int ClosedSubmissions { get; set; }
}
