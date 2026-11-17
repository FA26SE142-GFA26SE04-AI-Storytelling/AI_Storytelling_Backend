namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Trạng thái tài khoản — S3 Mục 11. Đăng nhập/đăng xuất và khóa 15 phút do sai mật khẩu không phải trạng thái.
/// PasswordResetPending dùng cho tài khoản vận hành mới được mời, chờ đặt mật khẩu.
/// AwaitingApproval dùng cho Content Creator tự đăng ký đã xác thực email, chờ Admin duyệt đơn (Luồng 5, M5).
/// </summary>
public enum AccountStatus
{
    Registered = 1,
    Active = 2,
    PasswordResetPending = 4,
    Suspended = 6,
    AwaitingApproval = 7
}
