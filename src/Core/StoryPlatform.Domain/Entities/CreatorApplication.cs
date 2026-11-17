using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một lần nộp đơn đăng ký Content Creator (Luồng 5, M5). Creator tự đăng ký, Admin duyệt hoặc từ chối kèm lý do;
/// bị từ chối thì nộp lại thành một đơn mới nên lịch sử các lần nộp được giữ. Mỗi tài khoản có tối đa một đơn Pending.
/// </summary>
public class CreatorApplication : BaseEntity
{
    public int ApplicantUserId { get; set; }
    public virtual UserAccount? Applicant { get; set; }

    /// <summary>Giới thiệu bản thân và kinh nghiệm viết cho trẻ em.</summary>
    public string Introduction { get; set; } = string.Empty;

    /// <summary>Link tới tác phẩm mẫu hoặc trang cá nhân; không bắt buộc.</summary>
    public string? PortfolioUrl { get; set; }

    public CreatorApplicationStatus Status { get; set; } = CreatorApplicationStatus.Pending;

    public int? ReviewedByUserId { get; set; }
    public virtual UserAccount? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Lý do từ chối, bắt buộc khi Rejected; Creator thấy lý do này.</summary>
    public string? RejectionReason { get; set; }
}
