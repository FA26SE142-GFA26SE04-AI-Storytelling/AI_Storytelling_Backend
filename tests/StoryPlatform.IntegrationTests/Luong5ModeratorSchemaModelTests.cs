using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using Xunit;

namespace StoryPlatform.IntegrationTests;

public sealed class Luong5ModeratorSchemaModelTests
{
    [Fact]
    public void UserRole_has_moderator_and_content_creator()
    {
        Assert.True(Enum.IsDefined(UserRole.Moderator));
        Assert.True(Enum.IsDefined(UserRole.ContentCreator));
        Assert.Equal(1, (int)UserRole.Parent);
        Assert.Equal(3, (int)UserRole.Administrator);
    }

    [Fact]
    public void Moderation_enums_match_workflow_documents()
    {
        Assert.Equal(
            new[] { "PublicStoryReview", "GrayZoneStory", "ContentReport", "RoadmapTemplateReview", "CommunitySubmission", "PublicStoryRemoval" },
            Enum.GetNames<ModerationItemType>());
        Assert.Equal(
            new[] { "Pending", "Claimed", "Approved", "ChangesRequested", "Rejected", "Cleared", "Blocked",
                    "Dismissed", "Archived", "Escalated", "Withdrawn", "Removed", "Closed" },
            Enum.GetNames<ModerationItemStatus>());
        Assert.Equal(new[] { "High", "Medium", "Low" }, Enum.GetNames<ModerationPriority>());
        Assert.Equal(new[] { "Draft", "PendingReview", "Published", "Archived" }, Enum.GetNames<RoadmapTemplateStatus>());
        Assert.Equal(new[] { "ReplacedByClone", "Manual" }, Enum.GetNames<RoadmapArchiveReason>());
        Assert.Equal(new[] { "Ai", "Moderator", "Parent" }, Enum.GetNames<TagSource>());
        Assert.Equal(new[] { "Safe", "GrayZone", "HardBlock" }, Enum.GetNames<ModerationZone>());
        Assert.Equal(
            new[] { "None", "Pending", "Approved", "ChangesRequested", "Rejected" },
            Enum.GetNames<VersionReviewStatus>());
        Assert.Equal(new[] { "AiGenerated", "OwnWork", "FamilyUse" }, Enum.GetNames<CopyrightDeclaration>());
    }

    [Theory]
    [InlineData("ContentReportEscalated", 18)]
    [InlineData("ContentModerationResult", 19)]
    [InlineData("StoryModerationResult", 20)]
    [InlineData("StoryArchivedByModeration", 21)]
    [InlineData("CreatorContentFlagged", 22)]
    [InlineData("CommunityLibraryReviewed", 23)]
    [InlineData("TotpReset", 24)]
    [InlineData("StaffInvite", 25)]
    public void NotificationType_defines_moderator_values(string name, int expected)
    {
        Assert.True(Enum.TryParse<NotificationType>(name, out var value), name);
        Assert.Equal(expected, (int)value);
    }

    [Fact]
    public void Report_and_archive_enums_cover_moderator_decisions()
    {
        Assert.True(Enum.IsDefined(ContentReportReason.Copyright));
        Assert.True(Enum.IsDefined(ContentReportReason.Cultural));
        Assert.True(Enum.IsDefined(ContentReportStatus.Escalated));
        Assert.True(Enum.IsDefined(ContentReportStatus.Closed));
        Assert.True(Enum.IsDefined(ArchivedReason.Copyright));
        Assert.True(Enum.IsDefined(ArchivedReason.Inappropriate));
    }

    [Fact]
    public void ModerationItem_maps_to_moderation_items_with_queue_indexes()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ModerationItem))!;

        Assert.Equal("moderation_items", entity.GetTableName());
        Assert.Equal(30, entity.FindProperty(nameof(ModerationItem.ItemType))!.GetMaxLength());
        Assert.Equal(30, entity.FindProperty(nameof(ModerationItem.Status))!.GetMaxLength());
        Assert.Equal(ModerationItemStatus.Pending, entity.FindProperty(nameof(ModerationItem.Status))!.GetDefaultValue());
        Assert.Equal(ModerationPriority.Medium, entity.FindProperty(nameof(ModerationItem.Priority))!.GetDefaultValue());
        Assert.True(entity.FindProperty(nameof(ModerationItem.RowVersion))!.IsConcurrencyToken);
        Assert.Equal("jsonb", entity.FindProperty(nameof(ModerationItem.ReviewProgress))!.GetColumnType());
        Assert.Equal("jsonb", entity.FindProperty(nameof(ModerationItem.Checklist))!.GetColumnType());

        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "ItemType", "Status", "Priority" }));
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "ClaimedByUserId", "Status" }));
    }

    [Fact]
    public void ModerationItem_has_partial_unique_indexes_for_open_community_and_roadmap_items()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ModerationItem))!;

        var community = Assert.Single(entity.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "StoryId" }));
        Assert.Contains("CommunitySubmission", community.GetFilter());
        Assert.Contains("Pending", community.GetFilter());
        Assert.Contains("Approved", community.GetFilter());

        var roadmap = Assert.Single(entity.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ReadingRoadmapId" }));
        Assert.Contains("RoadmapTemplateReview", roadmap.GetFilter());
    }

    [Fact]
    public void ModerationItem_forbids_claiming_own_item()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ModerationItem))!;

        var constraint = Assert.Single(entity.GetCheckConstraints(),
            c => c.Name == ModerationItemConfiguration.ClaimerIsNotCreatorConstraintName);
        Assert.Contains("\"ClaimedByUserId\" <> \"CreatedByUserId\"", constraint.Sql);
    }

    [Fact]
    public void ModerationReviewView_maps_temporary_access_grant()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ModerationReviewView))!;

        Assert.Equal("moderation_review_views", entity.GetTableName());
        Assert.False(entity.FindProperty(nameof(ModerationReviewView.ExpiresAt))!.IsNullable);
        Assert.Equal(30, entity.FindProperty(nameof(ModerationReviewView.SourceTab))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "ModeratorUserId", "StoryId", "ExpiresAt" }));
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "ModeratorUserId", "ReadingRoadmapId", "ExpiresAt" }));

        var designModel = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ModerationReviewView))!;
        var check = Assert.Single(designModel.GetCheckConstraints(),
            c => c.Name == ModerationReviewViewConfiguration.HasTargetConstraintName);
        Assert.Contains("\"StoryId\" IS NOT NULL OR \"ReadingRoadmapId\" IS NOT NULL", check.Sql);
    }

    [Fact]
    public void ReadingRoadmap_enforces_single_published_per_age_and_category_and_single_open_clone()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ReadingRoadmap))!;

        Assert.Equal("reading_roadmaps", entity.GetTableName());
        Assert.Equal(RoadmapTemplateStatus.Draft, entity.FindProperty(nameof(ReadingRoadmap.Status))!.GetDefaultValue());

        var published = Assert.Single(entity.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "AgeBand", "MainCategoryId" }));
        Assert.Contains("Published", published.GetFilter());

        var clone = Assert.Single(entity.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ClonedFromId" }));
        Assert.Contains("Draft", clone.GetFilter());
        Assert.Contains("PendingReview", clone.GetFilter());
    }

    [Fact]
    public void ReadingRoadmapItem_has_unique_position_and_story_per_roadmap()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ReadingRoadmapItem))!;

        Assert.Equal("reading_roadmap_items", entity.GetTableName());
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "ReadingRoadmapId", "Position" }));
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "ReadingRoadmapId", "StoryId" }));
    }

    [Fact]
    public void ContentReport_has_moderator_review_columns()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ContentReport))!;

        Assert.NotNull(entity.FindProperty(nameof(ContentReport.StoryVersionId)));
        Assert.NotNull(entity.FindProperty(nameof(ContentReport.ReadingRoadmapId)));
        Assert.NotNull(entity.FindProperty(nameof(ContentReport.ResolvedAt)));
        Assert.Equal(500, entity.FindProperty(nameof(ContentReport.AttachmentUrl))!.GetMaxLength());
        Assert.Equal(false, entity.FindProperty(nameof(ContentReport.IsReviewItem))!.GetDefaultValue());
    }

    [Fact]
    public void StoryCategory_records_who_assigned_the_tag()
    {
        using var context = CreateModelOnlyContext();
        var source = context.Model.FindEntityType(typeof(StoryCategory))!
            .FindProperty(nameof(StoryCategory.Source))!;

        Assert.Equal(TagSource.Ai, source.GetDefaultValue());
        Assert.Equal(30, source.GetMaxLength());
    }

    [Fact]
    public void Story_and_version_track_gray_zone_and_review_state()
    {
        using var context = CreateModelOnlyContext();
        var story = context.Model.FindEntityType(typeof(Story))!;
        Assert.Equal(false, story.FindProperty(nameof(Story.EverGrayZone))!.GetDefaultValue());
        Assert.NotNull(story.FindProperty(nameof(Story.PublishedAt)));
        Assert.NotNull(story.FindProperty(nameof(Story.ArchivedAt)));

        var version = context.Model.FindEntityType(typeof(StoryVersion))!;
        Assert.Equal(VersionReviewStatus.None, version.FindProperty(nameof(StoryVersion.ReviewStatus))!.GetDefaultValue());
        Assert.Equal(false, version.FindProperty(nameof(StoryVersion.GrayZoneCleared))!.GetDefaultValue());
        Assert.Equal("jsonb", version.FindProperty(nameof(StoryVersion.GrayZoneFlags))!.GetColumnType());
        Assert.Equal("jsonb", version.FindProperty(nameof(StoryVersion.ReviewFeedback))!.GetColumnType());
        Assert.NotNull(version.FindProperty(nameof(StoryVersion.ModerationZone)));
        Assert.NotNull(version.FindProperty(nameof(StoryVersion.ReviewedByUserId)));
        Assert.NotNull(version.FindProperty(nameof(StoryVersion.GrayZoneClearedByUserId)));
    }

    [Fact]
    public void UserAccount_stores_moderator_expertise_and_commitment()
    {
        using var context = CreateModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(UserAccount))!;

        Assert.Equal(200, entity.FindProperty(nameof(UserAccount.Expertise))!.GetMaxLength());
        Assert.NotNull(entity.FindProperty(nameof(UserAccount.CommitmentAcceptedAt)));
    }

    private static ApplicationDbContext CreateModelOnlyContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options);
}
