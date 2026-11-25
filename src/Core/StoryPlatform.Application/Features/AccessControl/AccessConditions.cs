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

    // ---- Luồng 1, 3, 4: luật được kiểm ở code sẵn có (guard chủ sở hữu, Safety Policy, phiên của trẻ, giới hạn gói);
    // AccessPolicy không đánh giá các điều kiện này, chúng chỉ để trang Phân quyền mô tả đúng luật.

    /// <summary>Chỉ hồ sơ trẻ, truyện gia đình và điều ước của chính phụ huynh (IChildProfileAccessGuard).</summary>
    public const string OwnChildProfile = "OwnChildProfile";

    /// <summary>Giới hạn theo gói: số hồ sơ trẻ, lượt AI, truyện thư viện mỗi tuần.</summary>
    public const string PlanLimits = "PlanLimits";

    /// <summary>Tự duyệt theo ngưỡng chỉ khi phụ huynh đã ủy quyền trong Safety Policy.</summary>
    public const string DelegatedApproval = "DelegatedApproval";

    /// <summary>Từ phiên của trẻ vào khu quản lý phải xác minh lại email và mật khẩu.</summary>
    public const string ParentalGate = "ParentalGate";

    /// <summary>Creator chỉ thao tác trên truyện do chính mình tạo.</summary>
    public const string OwnCreatorStory = "OwnCreatorStory";

    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ActiveAccount] = "Tài khoản đang hoạt động",
        [NotOwnContent] = "Không xử lý nội dung do chính mình tạo hoặc nộp",
        [ClaimedBySelf] = "Chỉ trên mục do chính mình nhận, còn hạn 2 giờ",
        [AdminFallback] = "Chỉ nhận mục đã được chuyển lên, hoặc khi không còn kiểm duyệt viên hợp lệ",
        [NoActiveModerator] = "Chỉ sửa khi không còn kiểm duyệt viên hoạt động (xem thì luôn được)",
        [OwnChildProfile] = "Chỉ hồ sơ trẻ, truyện và điều ước của chính mình",
        [PlanLimits] = "Giới hạn theo gói (số hồ sơ trẻ, lượt AI, truyện thư viện mỗi tuần)",
        [DelegatedApproval] = "Tự duyệt theo ngưỡng chỉ khi phụ huynh đã ủy quyền; không áp cho truyện công khai; truyện từng vào vùng xám luôn qua kiểm duyệt viên",
        [ParentalGate] = "Từ phiên của trẻ vào khu quản lý phải qua Parental Gate (nhập lại email và mật khẩu)",
        [OwnCreatorStory] = "Chỉ truyện do chính mình tạo",
    };

    public static string Describe(string condition) =>
        Descriptions.TryGetValue(condition, out var text) ? text : condition;
}
