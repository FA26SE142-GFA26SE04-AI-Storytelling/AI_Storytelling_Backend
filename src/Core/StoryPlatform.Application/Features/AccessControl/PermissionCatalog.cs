namespace StoryPlatform.Application.Features.AccessControl;

public sealed record PermissionDefinition(
    string Code, string Group, string Description, bool IsStaffGrantable, AccessScope Scope = AccessScope.Any);

/// <summary>Danh mục permission duy nhất của hệ thống; seeder đồng bộ danh mục này vào bảng <c>permissions</c>.</summary>
public static class PermissionCatalog
{
    public const string ParentGroup = "Phụ huynh";
    public const string SharedGroup = "Dùng chung";
    public const string CreatorGroup = "Creator";
    public const string ModerationGroup = "Kiểm duyệt";
    public const string AdminGroup = "Quản trị";

    public static readonly IReadOnlyList<string> GroupOrder = [ParentGroup, SharedGroup, CreatorGroup, ModerationGroup, AdminGroup];

    private static PermissionDefinition P(string code, string group, string description) =>
        new(code, group, description, group is ModerationGroup or AdminGroup);

    /// <summary>Quyền xử lý mục kiểm duyệt: chỉ trên mục đang do chính mình nhận (<see cref="AccessConditions.ClaimedBySelf"/>).</summary>
    /// <summary>Quyền chỉ xem hoặc sửa dữ liệu của chính tài khoản gọi (id lấy từ token đăng nhập).</summary>
    private static PermissionDefinition Own(string code, string group, string description) =>
        P(code, group, description) with { Scope = AccessScope.Own };

    private static PermissionDefinition Assigned(string code, string description) =>
        P(code, ModerationGroup, description) with { Scope = AccessScope.Assigned };

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        Own(Permissions.Parent.AccountSelfManage, ParentGroup, "Đổi email, xem trước và xóa tài khoản của mình"),
        P(Permissions.Parent.ChildProfileManage, ParentGroup, "Quản lý hồ sơ trẻ, hồ sơ học tập, lộ trình và đăng nhập của trẻ"),
        P(Permissions.Parent.StoryAuthor, ParentGroup, "Tạo, sửa và duyệt truyện cho con"),
        P(Permissions.Parent.FeedbackManage, ParentGroup, "Xem phản hồi đọc, tóm tắt và điều ước của trẻ"),
        P(Permissions.Parent.ContentReportSubmit, ParentGroup, "Gửi báo cáo vi phạm nội dung"),
        P(Permissions.Parent.StoryRatingWrite, ParentGroup, "Chấm sao và nhận xét truyện cộng đồng"),
        Own(Permissions.Parent.QuotaView, ParentGroup, "Xem gói và lượt của mình"),

        P(Permissions.Shared.MediaGenerate, SharedGroup, "Sinh tranh, âm thanh và ảnh bìa cho truyện"),
        P(Permissions.Shared.StoryRatingReport, SharedGroup, "Báo cáo một nhận xét truyện"),
        P(Permissions.Shared.StoryReviewApprove, SharedGroup, "Duyệt hoặc từ chối nội dung truyện AI trước khi đến trẻ"),
        P(Permissions.Shared.StoryPackageConfirm, SharedGroup, "Xác nhận gói truyện"),
        P(Permissions.Shared.CommunityView, SharedGroup, "Đọc thư viện truyện cộng đồng (với nhân viên: chỉ ảnh bìa và tên tác giả ở màn Rà soát thư viện)"),

        P(Permissions.Creator.ApplicationSubmit, CreatorGroup, "Gửi lại đơn đăng ký nhà sáng tạo"),
        Own(Permissions.Creator.ApplicationViewOwn, CreatorGroup, "Xem đơn đăng ký nhà sáng tạo của mình"),
        P(Permissions.Creator.Studio, CreatorGroup, "Viết, kiểm tra, nộp và theo dõi truyện của nhà sáng tạo"),
        Own(Permissions.Creator.RatingViewOwn, CreatorGroup, "Xem đánh giá truyện của mình"),
        Own(Permissions.Creator.CommunityManage, CreatorGroup, "Xem truyện công khai của mình"),
        Own(Permissions.Creator.PayoutViewOwn, CreatorGroup, "Xem thu nhập và cập nhật tài khoản nhận tiền"),
        Own(Permissions.Creator.QuotaView, CreatorGroup, "Xem lượt của nhà sáng tạo"),

        Assigned(Permissions.Moderation.QueueProcess, "Nhận và xử lý hàng đợi kiểm duyệt"),
        P(Permissions.Moderation.StatisticsView, ModerationGroup, "Xem thống kê kiểm duyệt của mình"),
        P(Permissions.Moderation.PublishedView, ModerationGroup, "Mở màn Rà soát thư viện: xem truyện và lộ trình đang công khai"),
        P(Permissions.Moderation.PublishedReview, ModerationGroup, "Mở hồ sơ rà soát nội dung đang công khai"),
        Assigned(Permissions.Moderation.ReportReview, "Xử lý báo cáo vi phạm nội dung"),
        Assigned(Permissions.Moderation.RatingModerate, "Duyệt hoặc ẩn nhận xét truyện"),
        P(Permissions.Moderation.CategoryManage, ModerationGroup, "Thêm, sửa, xóa danh mục nội dung"),
        Assigned(Permissions.Review.PublicStory, "Duyệt truyện nhà sáng tạo nộp công khai"),
        Assigned(Permissions.Review.GrayZone, "Duyệt nội dung vùng xám"),
        Assigned(Permissions.Review.Roadmap, "Duyệt lộ trình mẫu"),
        Assigned(Permissions.Review.CreatorPublication, "Quyết định yêu cầu gỡ truyện của nhà sáng tạo"),
        P(Permissions.Review.RoadmapTemplateView, ModerationGroup, "Xem lộ trình mẫu"),
        P(Permissions.Review.RoadmapTemplateAuthor, ModerationGroup, "Soạn và gửi duyệt lộ trình mẫu"),
        P(Permissions.Review.RubricManage, ModerationGroup, "Quản lý bộ tiêu chí vùng xám"),
        P(Permissions.Review.GuideManage, ModerationGroup, "Quản lý hướng dẫn sáng tác"),
        P(Permissions.Review.WishCardManage, ModerationGroup, "Quản lý thẻ ước và ghép truyện"),

        P(Permissions.Admin.StaffAccountManage, AdminGroup, "Mời và quản lý tài khoản vận hành"),
        P(Permissions.Admin.CreatorAccountManage, AdminGroup, "Quản lý tài khoản nhà sáng tạo"),
        P(Permissions.Admin.CreatorApplicationReview, AdminGroup, "Duyệt đơn đăng ký nhà sáng tạo"),
        P(Permissions.Admin.CreatorTierManage, AdminGroup, "Quản lý cấp đối tác của nhà sáng tạo"),
        P(Permissions.Admin.CreatorPayoutManage, AdminGroup, "Duyệt và ghi nhận chi trả cho nhà sáng tạo"),
        P(Permissions.Admin.QuotaManage, AdminGroup, "Xem và điều chỉnh lượt của người dùng"),
        P(Permissions.Admin.PaymentManage, AdminGroup, "Quản lý gói, giao dịch và đối soát thanh toán"),
        P(Permissions.Admin.BusinessReportView, AdminGroup, "Tạo và xem báo cáo nghiệp vụ"),
        P(Permissions.Admin.AuditView, AdminGroup, "Xem nhật ký thao tác"),
        P(Permissions.Admin.PermissionManage, AdminGroup, "Phân quyền cho role nhân viên"),
    ];

    public static PermissionDefinition? Find(string code) => All.FirstOrDefault(p => p.Code == code);
}
