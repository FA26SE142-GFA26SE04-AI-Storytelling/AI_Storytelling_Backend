using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using Xunit;

namespace StoryPlatform.IntegrationTests;

public sealed class Luong1SchemaAlignmentModelTests
{
    [Fact]
    public void Child_profile_status_matches_v41_lifecycle_values()
    {
        Assert.Equal(
            new[] { "Draft", "Active", "Suspended", "Archived", "Deleted" },
            Enum.GetNames<ChildProfileStatus>());
    }

    [Fact]
    public void Child_profile_model_does_not_store_exact_date_of_birth()
    {
        using var context = CreateModelOnlyContext();
        var childProfile = context.Model.FindEntityType(typeof(ChildProfile))!;

        Assert.Null(childProfile.FindProperty("DateOfBirth"));
    }

    [Theory]
    [InlineData("ModerationSlaWarning", 15)]
    [InlineData("ContentReportResolved", 16)]
    public void NotificationType_defines_content_report_values(string name, int expectedValue)
    {
        Assert.True(Enum.TryParse<NotificationType>(name, out var value), name);
        Assert.Equal(expectedValue, (int)value);
    }

    [Fact]
    public void Notification_type_column_fits_every_enum_name()
    {
        using var context = CreateModelOnlyContext();
        var maxLength = context.Model.FindEntityType(typeof(Notification))!
            .FindProperty(nameof(Notification.Type))!.GetMaxLength();

        Assert.NotNull(maxLength);
        Assert.All(Enum.GetNames<NotificationType>(), name => Assert.True(name.Length <= maxLength, name));
    }

    [Theory]
    [InlineData("SupervisionInvitation")]
    [InlineData("SupervisionRelationship")]
    [InlineData("SupervisionPermission")]
    [InlineData("SupervisionPermissionRequest")]
    [InlineData("SupervisionPermissionRequestItem")]
    [InlineData("OwnershipTransferRequest")]
    public void Model_does_not_contain_removed_supervision_entities(string entityName)
    {
        using var context = CreateModelOnlyContext();
        Assert.Null(context.Model.FindEntityType($"StoryPlatform.Domain.Entities.{entityName}"));
    }

    [Fact]
    public void ReadingSession_has_exactly_one_entry_source_check_constraint()
    {
        using var context = CreateModelOnlyContext();
        var readingSession = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ReadingSession))!;

        var constraint = Assert.Single(
            readingSession.GetCheckConstraints(),
            value => value.Name == ReadingSessionConfiguration.ExactlyOneEntrySourceConstraintName);
        Assert.Equal(ReadingSessionConfiguration.ExactlyOneEntrySourceSql, constraint.Sql);
        Assert.Contains("\"ChildAccessCredentialId\"", constraint.Sql);
        Assert.Contains("\"SupervisorSessionId\"", constraint.Sql);
    }

    [Fact]
    public void ChildSession_has_exactly_one_entry_source_check_constraint()
    {
        using var context = CreateModelOnlyContext();
        var childSession = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ChildSession))!;

        var constraint = Assert.Single(
            childSession.GetCheckConstraints(),
            value => value.Name == ChildSessionConfiguration.ExactlyOneEntrySourceConstraintName);
        Assert.Equal(ChildSessionConfiguration.ExactlyOneEntrySourceSql, constraint.Sql);
        Assert.NotNull(childSession.FindProperty(nameof(ChildSession.ChildAccessCredentialId)));
        Assert.NotNull(childSession.FindProperty(nameof(ChildSession.SupervisorSessionId)));
    }

    [Fact]
    public void ChildAccessCredential_unique_profile_index_ignores_revoked_and_soft_deleted_rows()
    {
        using var context = CreateModelOnlyContext();
        var credential = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ChildAccessCredential))!;

        var index = Assert.Single(credential.GetIndexes(), value =>
            value.IsUnique
            && value.Properties.Single().Name == nameof(ChildAccessCredential.ChildProfileId));
        Assert.Equal("\"RevokedAt\" IS NULL AND \"IsDeleted\" = false", index.GetFilter());
    }

    [Fact]
    public void Safety_learning_profile_v41_columns_are_mapped()
    {
        using var context = CreateModelOnlyContext();

        var childProfile = context.Model.FindEntityType(typeof(ChildProfile))!;
        Assert.Equal(
            ChildProfile.DefaultAvatarId,
            childProfile.FindProperty(nameof(ChildProfile.AvatarId))!.GetDefaultValue());

        var learningProfile = context.Model.FindEntityType(typeof(LearningProfile))!;
        Assert.Equal(
            ReadingMode.ListenFirst,
            learningProfile.FindProperty(nameof(LearningProfile.ReadingMode))!.GetDefaultValue());

        var contentCategory = context.Model.FindEntityType(typeof(ContentCategory))!;
        Assert.Equal(
            "text[]",
            contentCategory.FindProperty(nameof(ContentCategory.DefaultBlockedAgeBands))!.GetColumnType());

        var safetyPolicy = context.Model.FindEntityType(typeof(SafetyPolicy))!;
        Assert.NotNull(safetyPolicy.FindProperty(nameof(SafetyPolicy.StoryWishesEnabled)));
        Assert.NotNull(safetyPolicy.FindProperty(nameof(SafetyPolicy.AllowInteractiveStories)));
        Assert.NotNull(safetyPolicy.FindProperty(nameof(SafetyPolicy.ChildCreationEnabled)));
        Assert.NotNull(safetyPolicy.FindProperty(nameof(SafetyPolicy.ChildCreationMode)));
        Assert.NotNull(safetyPolicy.FindProperty(nameof(SafetyPolicy.VoiceAnswersEnabled)));
        Assert.NotNull(safetyPolicy.FindProperty(nameof(SafetyPolicy.VoiceConsentAt)));
        Assert.NotNull(safetyPolicy.FindProperty(nameof(SafetyPolicy.VoiceEnableRequestedAt)));

        var versionHistory = context.Model.FindEntityType(typeof(ChildProfileVersionHistory))!;
        Assert.Equal(
            VersionHistorySource.Recommendation,
            versionHistory.FindProperty(nameof(ChildProfileVersionHistory.Source))!.GetDefaultValue());
    }

    [Fact]
    public void Account_v41_columns_and_statuses_are_mapped()
    {
        using var context = CreateModelOnlyContext();

        var userAccount = context.Model.FindEntityType(typeof(UserAccount))!;
        Assert.NotNull(userAccount.FindProperty(nameof(UserAccount.EmailVerifiedAt)));
        Assert.NotNull(userAccount.FindProperty(nameof(UserAccount.EmailVerificationFailedAttempts)));
        Assert.NotNull(userAccount.FindProperty(nameof(UserAccount.PasswordChangedAt)));
        Assert.Equal(150, userAccount.FindProperty(nameof(UserAccount.PendingEmail))!.GetMaxLength());
        Assert.Equal(64, userAccount.FindProperty(nameof(UserAccount.PendingEmailOtpHash))!.GetMaxLength());
        Assert.NotNull(userAccount.FindProperty(nameof(UserAccount.PendingEmailOtpExpiresAt)));
        Assert.NotNull(userAccount.FindProperty(nameof(UserAccount.PendingEmailOtpFailedAttempts)));

        Assert.Contains(nameof(DataRequestStatus.Cancelled), Enum.GetNames<DataRequestStatus>());
        Assert.Equal(8, (int)NotificationType.AccountSecurityChanged);
    }

    [Fact]
    public void Notifications_sessions_and_data_requests_v41_columns_are_mapped()
    {
        using var context = CreateModelOnlyContext();

        var notification = context.Model.FindEntityType(typeof(Notification))!;
        Assert.NotNull(notification.FindProperty(nameof(Notification.ChildProfileId)));

        var childSession = context.Model.FindEntityType(typeof(ChildSession))!;
        Assert.Equal(30, childSession.FindProperty(nameof(ChildSession.EntryMethod))!.GetMaxLength());
        Assert.Equal(
            ChildEntryMethod.ProfileSwitcher,
            childSession.FindProperty(nameof(ChildSession.EntryMethod))!.GetDefaultValue());

        var credential = context.Model.FindEntityType(typeof(ChildAccessCredential))!;
        Assert.NotNull(credential.FindProperty(nameof(ChildAccessCredential.RevokedAt)));

        var dataRequest = context.Model.FindEntityType(typeof(DataRequest))!;
        Assert.Equal(500, dataRequest.FindProperty(nameof(DataRequest.RejectionReason))!.GetMaxLength());
        Assert.Equal(
            new[] { "Pending", "Resolved", "Cancelled", "Rejected" },
            Enum.GetNames<DataRequestStatus>());
    }

    private static ApplicationDbContext CreateModelOnlyContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options);
}
