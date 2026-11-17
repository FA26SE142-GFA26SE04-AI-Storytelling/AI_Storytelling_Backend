namespace StoryPlatform.Domain.Enums;

/// <summary>Trạng thái một lần nộp đơn đăng ký Content Creator (Luồng 5, M5).</summary>
public enum CreatorApplicationStatus
{
    /// <summary>Chờ Admin duyệt.</summary>
    Pending = 1,
    /// <summary>Admin đã duyệt; tài khoản Creator chuyển sang Active.</summary>
    Approved = 2,
    /// <summary>Admin từ chối kèm lý do; Creator có thể nộp lại đơn mới.</summary>
    Rejected = 3
}
