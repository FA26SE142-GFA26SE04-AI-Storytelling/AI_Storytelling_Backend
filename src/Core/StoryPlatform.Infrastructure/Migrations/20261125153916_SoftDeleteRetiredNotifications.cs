using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteRetiredNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Thong bao co Type da bi xoa khoi NotificationType khien API thong bao tra 500. Xoa mem de bo loc IsDeleted loai chung ra.
            migrationBuilder.Sql(
                """
                UPDATE notifications SET "IsDeleted" = TRUE, "UpdatedAt" = NOW()
                WHERE "IsDeleted" = FALSE AND "Type" NOT IN (
                    'SupervisionInvite', 'SupervisionInviteResult', 'SupervisionRevoked', 'PermissionRequest',
                    'PermissionRequestResult', 'OwnershipTransferRequest', 'OwnershipTransferResult', 'AccountSecurityChanged',
                    'AdminRoleChanged', 'ContentReported', 'ModerationSlaWarning', 'ContentReportResolved',
                    'PaymentConfirmed', 'ContentReportEscalated', 'ContentModerationResult', 'StoryModerationResult',
                    'StoryArchivedByModeration', 'CreatorContentFlagged', 'CommunityLibraryReviewed', 'StaffInvite',
                    'RoadmapStoryAdded', 'RoadmapStoryRemoved', 'RoadmapCompleted', 'StoryWishSubmitted',
                    'StoryWishExpiring', 'StorySummarySent', 'SubscriptionExpiring', 'StoryRatingPublished',
                    'StoryRatingModerated', 'CreatorApplicationApproved', 'CreatorApplicationRejected', 'CreatorTierChanged',
                    'CreatorPayoutPaid', 'CreatorPayoutCancelled', 'StoryCreditReleased', 'EventAwardApproved');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Khong khoi phuc: cac dong nay khong doc duoc voi enum hien tai.
        }
    }
}
