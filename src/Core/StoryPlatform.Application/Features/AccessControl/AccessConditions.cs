namespace StoryPlatform.Application.Features.AccessControl;

/// <summary>Phạm vi dữ liệu một permission được áp: mọi bản ghi, bản ghi của chính mình, hay mục đang do mình nhận.</summary>
public enum AccessScope
{
    Any,
    Own,
    Assigned,
}

/// <summary>
/// Đọc chỉ kiểm tài khoản còn hoạt động và role có permission; ghi kiểm thêm các điều kiện gắn với lần cấp quyền
/// (ví dụ Administrator chỉ sửa cấu hình kiểm duyệt khi không còn kiểm duyệt viên).
/// </summary>
public enum AccessMode
{
    Write,
    Read,
}

/// <summary>
/// Điều kiện có tên gắn với một lần cấp quyền cho role (<see cref="DefaultRolePermissions.ConditionsFor"/>).
/// Điều kiện định nghĩa trong code, không lưu DB: Administrator thu/trả quyền nhưng không sửa được điều kiện.
/// </summary>
public static class AccessConditions
{
    /// <summary>Luôn áp: tài khoản không bị xoá, đang hoạt động và không bị tạm khoá đăng nhập.</summary>
    public const string ActiveAccount = "ActiveAccount";

    /// <summary>Không xử lý mục kiểm duyệt do chính mình tạo hoặc nộp.</summary>
    public const string NotOwnContent = "NotOwnContent";

    /// <summary>
    /// Phạm vi <see cref="AccessScope.Assigned"/>: chỉ thao tác trên mục đang do chính mình nhận và còn hạn 2 giờ.
    /// Mỗi service xử lý mục tự kiểm điều này vì mỗi nơi trả mã lỗi riêng mà client đang dùng.
    /// </summary>
    public const string ClaimedBySelf = "ClaimedBySelf";

    /// <summary>Administrator chỉ nhận mục khi mục đã được chuyển lên, hoặc không còn kiểm duyệt viên hợp lệ.</summary>
    public const string AdminFallback = "AdminFallback";

    /// <summary>Administrator chỉ sửa khi không còn kiểm duyệt viên nào đang hoạt động.</summary>
    public const string NoActiveModerator = "NoActiveModerator";

    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ActiveAccount] = "Tài khoản đang hoạt động",
        [NotOwnContent] = "Không xử lý nội dung do chính mình tạo hoặc nộp",
        [ClaimedBySelf] = "Chỉ trên mục do chính mình nhận, còn hạn 2 giờ",
        [AdminFallback] = "Chỉ nhận mục đã được chuyển lên, hoặc khi không còn kiểm duyệt viên hợp lệ",
        [NoActiveModerator] = "Chỉ sửa khi không còn kiểm duyệt viên hoạt động (xem thì luôn được)",
    };

    public static string Describe(string condition) =>
        Descriptions.TryGetValue(condition, out var text) ? text : condition;
}
