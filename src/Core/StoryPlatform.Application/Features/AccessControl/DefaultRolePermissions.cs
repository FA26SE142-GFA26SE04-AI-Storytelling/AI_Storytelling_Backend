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
        Permissions.Moderation.EventOperate,
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
                Permissions.Shared.CommunityView,
                Permissions.Shared.EventParticipate,
                Permissions.Parent.EventParticipate),
            [RoleCodes.ContentCreator] = Set(
                Permissions.Shared.MediaGenerate,
                Permissions.Shared.StoryRatingReport,
                Permissions.Shared.StoryPackageConfirm,
                Permissions.Shared.CommunityView,
                Permissions.Shared.EventParticipate,
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
                Permissions.Moderation.EventManage,
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
                Permissions.Admin.PermissionManage,
                Permissions.Admin.EventApprove,
                // Luồng 5: soạn bộ truyện mẫu khi không còn kiểm duyệt viên (điều kiện NoActiveModerator).
                Permissions.Review.RoadmapTemplateAuthor]),
        };

    private static readonly string[] ClaimablePermissions =
    [
        Permissions.Moderation.QueueProcess,
        Permissions.Review.PublicStory,
        Permissions.Review.GrayZone,
        Permissions.Review.Roadmap,
        Permissions.Review.CreatorPublication,
    ];

    private static readonly string[] ModerationConfigPermissions =
    [
        Permissions.Review.RubricManage,
        Permissions.Review.GuideManage,
        Permissions.Review.WishCardManage,
        Permissions.Review.RoadmapTemplateAuthor,
    ];

    /// <summary>
    /// Điều kiện gắn với từng lần cấp quyền (role, permission), theo luật Luồng 5 – Phần I. <see cref="AccessConditions.ActiveAccount"/>
    /// luôn áp nên không khai báo ở đây.
    /// </summary>
    public static readonly IReadOnlyDictionary<(string Role, string Permission), IReadOnlyList<string>> GrantConditions =
        BuildGrantConditions();

    public static IReadOnlyList<string> ConditionsFor(string roleCode, string permission) =>
        GrantConditions.TryGetValue((roleCode, permission), out var conditions) ? conditions : [];

    private static Dictionary<(string, string), IReadOnlyList<string>> BuildGrantConditions()
    {
        var map = new Dictionary<(string, string), IReadOnlyList<string>>();
        foreach (var permission in ClaimablePermissions)
        {
            map[(RoleCodes.Moderator, permission)] = [AccessConditions.NotOwnContent];
            map[(RoleCodes.Administrator, permission)] = [AccessConditions.NotOwnContent, AccessConditions.AdminFallback];
        }

        map[(RoleCodes.Moderator, Permissions.Moderation.ReportReview)] = [AccessConditions.NotOwnContent];
        map[(RoleCodes.Moderator, Permissions.Moderation.RatingModerate)] = [AccessConditions.NotOwnContent];
        foreach (var permission in ModerationConfigPermissions)
            map[(RoleCodes.Administrator, permission)] = [AccessConditions.NoActiveModerator];

        // Luồng 1, 3, 4: chỉ mô tả, kiểm ở code sẵn có (xem AccessConditions).
        map[(RoleCodes.User, Permissions.Parent.ChildProfileManage)] =
            [AccessConditions.OwnChildProfile, AccessConditions.PlanLimits, AccessConditions.ParentalGate];
        map[(RoleCodes.User, Permissions.Parent.StoryAuthor)] = [AccessConditions.OwnChildProfile, AccessConditions.PlanLimits];
        map[(RoleCodes.User, Permissions.Parent.FeedbackManage)] = [AccessConditions.OwnChildProfile];
        map[(RoleCodes.User, Permissions.Shared.MediaGenerate)] = [AccessConditions.OwnChildProfile];
        map[(RoleCodes.User, Permissions.Shared.StoryReviewApprove)] = [AccessConditions.DelegatedApproval];
        map[(RoleCodes.User, Permissions.Shared.StoryPackageConfirm)] = [AccessConditions.OwnChildProfile];
        map[(RoleCodes.ContentCreator, Permissions.Creator.Studio)] = [AccessConditions.OwnCreatorStory, AccessConditions.PlanLimits];
        map[(RoleCodes.ContentCreator, Permissions.Shared.MediaGenerate)] = [AccessConditions.OwnCreatorStory];
        map[(RoleCodes.ContentCreator, Permissions.Shared.StoryPackageConfirm)] = [AccessConditions.OwnCreatorStory];
        return map;
    }

    private static IReadOnlySet<string> Set(params string[] codes) => new HashSet<string>(codes, StringComparer.Ordinal);
}
