using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.StoryRatings.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using static StoryPlatform.UnitTests.Features.StoryRatings.StoryRatingHarness;

namespace StoryPlatform.UnitTests.Features.StoryRatings;

public sealed class StoryRatingModerationServiceTests
{
    private readonly StoryRatingHarness _h = new();

    private ModerationItem PendingRatingClaimedByModerator()
    {
        _h.AddRating(1, ParentId, 2, StoryRatingStatus.PendingModeration, "Kết hơi vội");
        return _h.AddOpenItem(50, 1, ParentId, "Screening", "LOW_RATING_WITH_TEXT", claimedBy: ModeratorId);
    }

    private ModerationItem ReportedRatingClaimedByModerator()
    {
        _h.AddRating(1, ParentId, 5, StoryRatingStatus.Published, "Hay");
        var item = _h.AddOpenItem(50, 1, ParentId, "Report", claimedBy: ModeratorId);
        _h.AddReport(1, 1, OtherParentId, item.Id, StoryRatingReportReason.Offensive, "Công kích");
        return item;
    }

    [Fact]
    public async Task Approve_PendingRating_PublishesAndNotifiesReviewerAndCreator()
    {
        var item = PendingRatingClaimedByModerator();

        var result = await _h.ModerationService().ApproveAsync(ModeratorId, item.Id);

        var rating = _h.Ratings[0];
        Assert.Equal(StoryRatingStatus.Published, rating.Status);
        Assert.Equal(ModeratorId, rating.ModeratedByUserId);
        Assert.NotNull(rating.ModeratedAt);
        Assert.Equal(ModerationItemStatus.Approved, item.Status);
        Assert.Equal(ModeratorId, item.DecidedByUserId);
        Assert.Equal(1, item.RowVersion);
        Assert.Equal(("Approved", "Published"), (result.Decision, result.RatingStatus));
        Assert.Equal(
            new[] { (ParentId, NotificationType.StoryRatingModerated), (CreatorId, NotificationType.StoryRatingPublished) },
            _h.Sent.Select(sent => (sent.Recipient, sent.Type)));
    }

    [Fact]
    public async Task Approve_ReportedPublishedRating_KeepsItAndClosesReports_WithoutNotifications()
    {
        var item = ReportedRatingClaimedByModerator();

        await _h.ModerationService().ApproveAsync(ModeratorId, item.Id);

        Assert.Equal(StoryRatingStatus.Published, _h.Ratings[0].Status);
        var report = Assert.Single(_h.Reports);
        Assert.Equal(StoryRatingReportStatus.Approved, report.Status);
        Assert.NotNull(report.ResolvedAt);
        Assert.Equal(ModerationItemStatus.Approved, item.Status);
        Assert.Empty(_h.Sent);
    }

    [Fact]
    public async Task Hide_HidesRatingRejectsItemAndTellsReviewerWhy()
    {
        var item = ReportedRatingClaimedByModerator();

        var result = await _h.ModerationService().HideAsync(
            ModeratorId, item.Id, new HideStoryRatingRequestDto { Reason = "  Công kích tác giả  " });

        var rating = _h.Ratings[0];
        Assert.Equal(StoryRatingStatus.Hidden, rating.Status);
        Assert.Equal("Công kích tác giả", rating.HiddenReason);
        Assert.Equal(StoryRatingReportStatus.Hidden, Assert.Single(_h.Reports).Status);
        Assert.Equal(ModerationItemStatus.Rejected, item.Status);
        Assert.Equal("Công kích tác giả", item.DecisionReason);
        Assert.Equal(("Hidden", "Hidden"), (result.Decision, result.RatingStatus));
        var sent = Assert.Single(_h.Sent);
        Assert.Equal((ParentId, NotificationType.StoryRatingModerated), (sent.Recipient, sent.Type));
        Assert.Contains("Công kích tác giả", JsonDocument.Parse(sent.Payload!).RootElement.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Hide_WithoutReason_IsBadRequest(string reason)
    {
        var item = ReportedRatingClaimedByModerator();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _h.ModerationService().HideAsync(ModeratorId, item.Id, new HideStoryRatingRequestDto { Reason = reason }));
        Assert.Equal(StoryRatingStatus.Published, _h.Ratings[0].Status);
    }

    [Fact]
    public async Task GetDetail_ShowsRatingAndReportReasons_WithoutReporterIdentity()
    {
        var item = ReportedRatingClaimedByModerator();

        var detail = await _h.ModerationService().GetDetailAsync(ModeratorId, item.Id);

        Assert.Equal((item.Id, 1, PublicStoryId, "Cá heo tốt bụng"), (detail.ItemId, detail.RatingId, detail.StoryId, detail.StoryTitle));
        Assert.Equal("Phụ huynh H***", detail.ReviewerName);
        Assert.Equal(("Report", (string?)null), (detail.HoldSource, detail.ScreeningReasonCode));
        var report = Assert.Single(detail.Reports);
        Assert.Equal(("Offensive", "Công kích"), (report.Reason, report.Note));
        var json = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("parent_demo2", json);
        Assert.DoesNotContain("Lê Văn Minh", json);
        Assert.DoesNotContain("Reporter", json);
        _h.Audit.Verify(audit => audit.LogAsync(
            ModeratorId, "StoryRatingOpened", nameof(ModerationItem), item.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDetail_ScreeningItem_ShowsReasonCode()
    {
        var item = PendingRatingClaimedByModerator();

        var detail = await _h.ModerationService().GetDetailAsync(ModeratorId, item.Id);

        Assert.Equal(("Screening", "LOW_RATING_WITH_TEXT", "PendingModeration"),
            (detail.HoldSource, detail.ScreeningReasonCode, detail.RatingStatus));
    }

    [Fact]
    public async Task Gate_ItemClaimedBySomeoneElse_IsForbidden()
    {
        var item = ReportedRatingClaimedByModerator();
        item.ClaimedByUserId = OtherModeratorId;

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.ModerationService().ApproveAsync(ModeratorId, item.Id));
    }

    [Fact]
    public async Task Gate_Administrator_IsForbidden()
    {
        var item = ReportedRatingClaimedByModerator();
        item.ClaimedByUserId = AdminId;

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.ModerationService().GetDetailAsync(AdminId, item.Id));
    }

    [Fact]
    public async Task Gate_ExpiredClaim_IsConflict()
    {
        var item = ReportedRatingClaimedByModerator();
        item.ClaimedAt = DateTime.UtcNow.AddHours(-3);

        await Assert.ThrowsAsync<ConflictException>(() => _h.ModerationService().ApproveAsync(ModeratorId, item.Id));
    }

    [Fact]
    public async Task Gate_OtherItemType_IsBadRequest()
    {
        var item = ReportedRatingClaimedByModerator();
        item.ItemType = ModerationItemType.ContentReport;

        await Assert.ThrowsAsync<BadRequestException>(() => _h.ModerationService().ApproveAsync(ModeratorId, item.Id));
    }

    [Fact]
    public async Task Gate_RatingDeletedByReviewer_IsConflict()
    {
        var item = ReportedRatingClaimedByModerator();
        _h.Ratings[0].IsDeleted = true;

        await Assert.ThrowsAsync<ConflictException>(() => _h.ModerationService().ApproveAsync(ModeratorId, item.Id));
    }

    [Fact]
    public void Controller_IsModeratorOnly()
    {
        var authorize = typeof(StoryRatingModerationController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal("Moderator", authorize!.Roles);
    }
}
