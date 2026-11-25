namespace StoryPlatform.Application.Features.AccessControl;

/// <summary>
/// Mã permission dùng trên controller qua <c>[HasPermission(...)]</c>. Mô tả, nhóm và việc có gán cho nhân viên
/// được không nằm ở <see cref="PermissionCatalog"/>; bản gán mặc định nằm ở <see cref="DefaultRolePermissions"/>.
/// </summary>
public static class Permissions
{
    public static class Parent
    {
        public const string AccountSelfManage = "account.self.manage";
        public const string ChildProfileManage = "child.profile.manage";
        public const string StoryAuthor = "parent.story.author";
        public const string FeedbackManage = "parent.feedback.manage";
        public const string ContentReportSubmit = "content.report.submit";
        public const string StoryRatingWrite = "story.rating.write";
        public const string QuotaView = "quota.parent.view";
        public const string EventParticipate = "event.parent.participate";
    }

    public static class Shared
    {
        public const string MediaGenerate = "media.generate";
        public const string StoryRatingReport = "story.rating.report";
        public const string StoryReviewApprove = "story.review.approve";
        public const string StoryPackageConfirm = "story.package.confirm";
        public const string CommunityView = "community.view";
        public const string EventParticipate = "event.participate";
    }

    public static class Creator
    {
        public const string ApplicationSubmit = "creator.application.submit";
        public const string ApplicationViewOwn = "creator.application.view_own";
        public const string Studio = "creator.studio";
        public const string RatingViewOwn = "creator.rating.view_own";
        public const string CommunityManage = "creator.community.manage";
        public const string PayoutViewOwn = "creator.payout.view_own";
        public const string QuotaView = "quota.creator.view";
    }

    public static class Moderation
    {
        public const string QueueProcess = "moderation.queue.process";
        public const string StatisticsView = "moderation.statistics.view";
        public const string PublishedView = "moderation.published.view";
        public const string PublishedReview = "moderation.published.review";
        public const string ReportReview = "moderation.report.review";
        public const string RatingModerate = "moderation.rating.moderate";
        public const string CategoryManage = "category.manage";
        public const string EventManage = "event.manage";
        public const string EventOperate = "event.operate";
    }

    public static class Review
    {
        public const string PublicStory = "review.public_story";
        public const string GrayZone = "review.gray_zone";
        public const string Roadmap = "review.roadmap";
        public const string CreatorPublication = "review.creator_publication";
        public const string RoadmapTemplateView = "template.roadmap.view";
        public const string RoadmapTemplateAuthor = "template.roadmap.author";
        public const string RubricManage = "rubric.manage";
        public const string GuideManage = "guide.manage";
        public const string WishCardManage = "wishcard.manage";
    }

    public static class Admin
    {
        public const string StaffAccountManage = "staff.account.manage";
        public const string CreatorAccountManage = "creator.account.manage";
        public const string CreatorApplicationReview = "creator.application.review";
        public const string CreatorTierManage = "creator.tier.manage";
        public const string CreatorPayoutManage = "creator.payout.manage";
        public const string QuotaManage = "quota.manage";
        public const string PaymentManage = "payment.manage";
        public const string BusinessReportView = "report.business.view";
        public const string AuditView = "audit.view";
        public const string PermissionManage = "permission.manage";
        public const string EventApprove = "event.approve";
    }
}
