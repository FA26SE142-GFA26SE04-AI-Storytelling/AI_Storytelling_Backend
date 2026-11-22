namespace StoryPlatform.Application.Features.AccessControl;

/// <summary>
/// Bản gán role → permission mặc định, khớp đúng quyền của hệ thống trước khi có bảng phân quyền
/// (test ma trận quyền khóa điều này). Seeder ghi bản này cho role chưa có quyền nào trong DB.
/// </summary>
public static class DefaultRolePermissions
{
    private static readonly string[] ModeratorAndAdmin =
    [
        Permissions.Moderation.QueueProcess,
        Permissions.Moderation.PublishedView,
        Permissions.Review.PublicStory,
        Permissions.Review.GrayZone,
        Permissions.Review.Roadmap,
        Permissions.Review.CreatorPublication,
        Permissions.Review.RoadmapTemplateView,
        Permissions.Review.RubricManage,
        Permissions.Review.GuideManage,
        Permissions.Review.WishCardManage,
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [RoleCodes.User] = Set(
                Permissions.Parent.AccountSelfManage,
                Permissions.Parent.ChildProfileManage,
                Permissions.Parent.StoryAuthor,
                Permissions.Parent.FeedbackManage,
                Permissions.Parent.ContentReportSubmit,
                Permissions.Parent.StoryRatingWrite,
                Permissions.Parent.QuotaView,
                Permissions.Shared.MediaGenerate,
                Permissions.Shared.StoryRatingReport,
                Permissions.Shared.StoryReviewApprove,
                Permissions.Shared.StoryPackageConfirm,
                Permissions.Shared.CommunityView),
            [RoleCodes.ContentCreator] = Set(
                Permissions.Shared.MediaGenerate,
                Permissions.Shared.StoryRatingReport,
                Permissions.Shared.StoryPackageConfirm,
                Permissions.Shared.CommunityView,
                Permissions.Creator.ApplicationViewOwn,
                Permissions.Creator.Studio,
                Permissions.Creator.RatingViewOwn,
                Permissions.Creator.CommunityManage,
                Permissions.Creator.PayoutViewOwn,
                Permissions.Creator.QuotaView),
            [RoleCodes.CreatorApplicant] = Set(
                Permissions.Creator.ApplicationSubmit,
                Permissions.Creator.ApplicationViewOwn),
            [RoleCodes.Moderator] = Set([
                .. ModeratorAndAdmin,
                Permissions.Shared.CommunityView,
                Permissions.Moderation.StatisticsView,
                Permissions.Moderation.PublishedReview,
                Permissions.Moderation.ReportReview,
                Permissions.Moderation.RatingModerate,
                Permissions.Moderation.CategoryManage,
                Permissions.Review.RoadmapTemplateAuthor]),
            [RoleCodes.Administrator] = Set([
                .. ModeratorAndAdmin,
                Permissions.Shared.StoryReviewApprove,
                Permissions.Shared.StoryPackageConfirm,
                Permissions.Shared.CommunityView,
                Permissions.Admin.StaffAccountManage,
                Permissions.Admin.CreatorAccountManage,
                Permissions.Admin.CreatorApplicationReview,
                Permissions.Admin.CreatorTierManage,
                Permissions.Admin.CreatorPayoutManage,
                Permissions.Admin.QuotaManage,
                Permissions.Admin.PaymentManage,
                Permissions.Admin.BusinessReportView,
                Permissions.Admin.AuditView,
                Permissions.Admin.PermissionManage]),
        };

    private static IReadOnlySet<string> Set(params string[] codes) => new HashSet<string>(codes, StringComparer.Ordinal);
}
