namespace StoryPlatform.Application.Features.AccessControl;

public sealed record PermissionDefinition(string Code, string Group, string Description, bool IsStaffGrantable);

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

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        P(Permissions.Parent.AccountSelfManage, ParentGroup, "Đổi email, xem trước và xóa tài khoản của mình"),
        P(Permissions.Parent.ChildProfileManage, ParentGroup, "Quản lý hồ sơ trẻ, hồ sơ học tập, lộ trình và đăng nhập của trẻ"),
        P(Permissions.Parent.StoryAuthor, ParentGroup, "Tạo, sửa và duyệt truyện cho con"),
        P(Permissions.Parent.FeedbackManage, ParentGroup, "Xem phản hồi đọc, tóm tắt và điều ước của trẻ"),
        P(Permissions.Parent.ContentReportSubmit, ParentGroup, "Gửi báo cáo vi phạm nội dung"),
        P(Permissions.Parent.StoryRatingWrite, ParentGroup, "Chấm sao và nhận xét truyện cộng đồng"),
        P(Permissions.Parent.QuotaView, ParentGroup, "Xem gói và lượt của mình"),

        P(Permissions.Shared.MediaGenerate, SharedGroup, "Sinh tranh, âm thanh và ảnh bìa cho truyện"),
        P(Permissions.Shared.StoryRatingReport, SharedGroup, "Báo cáo một nhận xét truyện"),
        P(Permissions.Shared.StoryReviewApprove, SharedGroup, "Duyệt hoặc từ chối nội dung truyện AI trước khi đến trẻ"),
        P(Permissions.Shared.StoryPackageConfirm, SharedGroup, "Xác nhận gói truyện"),
        P(Permissions.Shared.CommunityView, SharedGroup, "Xem thư viện cộng đồng"),

        P(Permissions.Creator.ApplicationSubmit, CreatorGroup, "Gửi lại đơn đăng ký nhà sáng tạo"),
        P(Permissions.Creator.ApplicationViewOwn, CreatorGroup, "Xem đơn đăng ký nhà sáng tạo của mình"),
        P(Permissions.Creator.Studio, CreatorGroup, "Viết, kiểm tra, nộp và theo dõi truyện của nhà sáng tạo"),
        P(Permissions.Creator.RatingViewOwn, CreatorGroup, "Xem đánh giá truyện của mình"),
        P(Permissions.Creator.CommunityManage, CreatorGroup, "Xem truyện công khai của mình"),
        P(Permissions.Creator.PayoutViewOwn, CreatorGroup, "Xem thu nhập và cập nhật tài khoản nhận tiền"),
        P(Permissions.Creator.QuotaView, CreatorGroup, "Xem lượt của nhà sáng tạo"),

        P(Permissions.Moderation.QueueProcess, ModerationGroup, "Nhận và xử lý hàng đợi kiểm duyệt"),
        P(Permissions.Moderation.StatisticsView, ModerationGroup, "Xem thống kê kiểm duyệt của mình"),
        P(Permissions.Moderation.PublishedView, ModerationGroup, "Xem truyện và lộ trình đang công khai"),
        P(Permissions.Moderation.PublishedReview, ModerationGroup, "Mở hồ sơ rà soát nội dung đang công khai"),
        P(Permissions.Moderation.ReportReview, ModerationGroup, "Xử lý báo cáo vi phạm nội dung"),
        P(Permissions.Moderation.RatingModerate, ModerationGroup, "Duyệt hoặc ẩn nhận xét truyện"),
        P(Permissions.Moderation.CategoryManage, ModerationGroup, "Thêm, sửa, xóa danh mục nội dung"),
        P(Permissions.Review.PublicStory, ModerationGroup, "Duyệt truyện nhà sáng tạo nộp công khai"),
        P(Permissions.Review.GrayZone, ModerationGroup, "Duyệt nội dung vùng xám"),
        P(Permissions.Review.Roadmap, ModerationGroup, "Duyệt lộ trình mẫu"),
        P(Permissions.Review.CreatorPublication, ModerationGroup, "Quyết định yêu cầu gỡ truyện của nhà sáng tạo"),
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
