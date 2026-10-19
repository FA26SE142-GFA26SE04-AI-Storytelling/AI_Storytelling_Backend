using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.RoadmapTemplates;

public class RoadmapReviewServiceTests
{
    private readonly RoadmapHarness _h = new();
    private readonly RoadmapReviewService _sut;

    private const int Submitter = RoadmapHarness.ModeratorId;
    private const int Reviewer = RoadmapHarness.OtherModeratorId;

    public RoadmapReviewServiceTests()
    {
        _sut = new RoadmapReviewService(
            _h.UnitOfWork.Object,
            new RoadmapSubmissionChecker(_h.UnitOfWork.Object),
            _h.Audit.Object,
            _h.Notifications.Object);
    }

    private ModerationItem AddClaimedReview(
        int roadmapId = 1,
        int reviewerId = Reviewer,
        int creatorId = Submitter,
        params int[] storyIds)
    {
        if (storyIds.Length == 0)
        {
            storyIds = [10, 11];
        }

        _h.AddRoadmap(
            roadmapId, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.PendingReview,
            creatorId, storyIds);
        var item = new ModerationItem
        {
            Id = 40,
            ItemType = ModerationItemType.RoadmapTemplateReview,
            Status = ModerationItemStatus.Claimed,
            ReadingRoadmapId = roadmapId,
            CreatedByUserId = creatorId,
            ClaimedByUserId = reviewerId,
            ClaimedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        _h.Items.Add(item);
        return item;
    }

    private static SaveRoadmapChecklistRequestDto PassedChecklist() => new()
    {
        Entries = RoadmapReviewPolicy.CriterionCodes.Select(code => new RoadmapChecklistEntryDto
        {
            Code = code,
            Passed = true
        }).ToList()
    };

    private async Task PassChecklistAsync(int itemId = 40, int reviewerId = Reviewer) =>
        await _sut.SaveChecklistAsync(reviewerId, itemId, PassedChecklist());

    private void CaptureNotifications(List<(int Recipient, NotificationType Type, string? Payload)> sent) =>
        _h.Notifications.Setup(notification => notification.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback((int recipient, NotificationType type, string? payload, CancellationToken _) =>
                sent.Add((recipient, type, payload)))
            .ReturnsAsync(new NotificationDto());

    [Fact]
    public async Task GetPackageAsync_ReturnsRoadmapStoriesChecksChecklistAndClaimExpiry()
    {
        var item = AddClaimedReview();

        var result = await _sut.GetPackageAsync(Reviewer, item.Id);

        Assert.Equal(item.Id, result.ItemId);
        Assert.Equal(1, result.Roadmap.Id);
        Assert.Equal(new[] { 10, 11 }, result.Stories.Select(story => story.StoryId));
        Assert.Equal(6, result.Checks.Count);
        Assert.Equal(RoadmapReviewPolicy.CriterionCodes, result.Checklist.Select(entry => entry.Code));
        Assert.All(result.Checklist, entry => Assert.False(entry.Passed));
        Assert.Contains(result.Blockers,
            blocker => blocker.Code == RoadmapReviewPolicy.BlockerCodes.ChecklistIncomplete);
        Assert.Equal(item.ClaimedAt!.Value.AddHours(2), result.ClaimExpiresAt);
        _h.Audit.Verify(audit => audit.LogAsync(
            Reviewer, "RoadmapReviewPackageOpened", nameof(ModerationItem), item.Id,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPackageAsync_FullyPassedChecklist_AllowsApprove()
    {
        var item = AddClaimedReview();
        await PassChecklistAsync(item.Id);

        var result = await _sut.GetPackageAsync(Reviewer, item.Id);

        Assert.True(result.CanApprove);
        Assert.Empty(result.Blockers);
    }

    [Fact]
    public async Task GetPackageAsync_ShowsStoryComboAndOwnRoadmapBlockers()
    {
        var item = AddClaimedReview(creatorId: Reviewer);
        _h.Stories.Single(story => story.Id == 10).IsPublished = false;
        _h.AddRoadmap(
            9, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Published, Submitter, 11);

        var result = await _sut.GetPackageAsync(Reviewer, item.Id);

        Assert.Contains(result.Blockers,
            blocker => blocker.Code == RoadmapReviewPolicy.BlockerCodes.OwnRoadmap);
        Assert.Contains(result.Blockers,
            blocker => blocker.Code == RoadmapReviewPolicy.BlockerCodes.StoriesNotReady);
        Assert.Contains(result.Blockers,
            blocker => blocker.Code == RoadmapReviewPolicy.BlockerCodes.ComboTaken
                       && blocker.RelatedRoadmapId == 9);
    }

    [Fact]
    public async Task GetPackageAsync_EnforcesClaimActorTypeExpiryAndRoadmapState()
    {
        var item = AddClaimedReview();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetPackageAsync(Submitter, item.Id));

        item.ClaimedAt = DateTime.UtcNow.AddHours(-2).AddMinutes(-1);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetPackageAsync(Reviewer, item.Id));

        item.ClaimedAt = DateTime.UtcNow;
        item.ItemType = ModerationItemType.ContentReport;
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.GetPackageAsync(Reviewer, item.Id));

        item.ItemType = ModerationItemType.RoadmapTemplateReview;
        _h.Roadmaps.Single().Status = RoadmapTemplateStatus.Draft;
        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.GetPackageAsync(Reviewer, item.Id));
    }

    [Fact]
    public async Task SaveChecklistAsync_PartialSavePreservesExistingAnswersAndBumpsVersion()
    {
        var item = AddClaimedReview();
        var first = await _sut.SaveChecklistAsync(Reviewer, item.Id, new SaveRoadmapChecklistRequestDto
        {
            Entries = [new RoadmapChecklistEntryDto { Code = "order", Passed = true, Note = "  Tốt  " }]
        });
        var second = await _sut.SaveChecklistAsync(Reviewer, item.Id, new SaveRoadmapChecklistRequestDto
        {
            Entries = [new RoadmapChecklistEntryDto { Code = "theme", Passed = true }]
        });

        Assert.True(first.Single(entry => entry.Code == "order").Passed);
        Assert.True(second.Single(entry => entry.Code == "order").Passed);
        Assert.True(second.Single(entry => entry.Code == "theme").Passed);
        Assert.Equal("Tốt", second.Single(entry => entry.Code == "order").Note);
        Assert.Equal(2, item.RowVersion);
        Assert.NotNull(item.ReviewProgress);
    }

    [Fact]
    public async Task SaveChecklistAsync_RejectsEmptyUnknownDuplicateAndLongNote()
    {
        var item = AddClaimedReview();
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveChecklistAsync(
            Reviewer, item.Id, new SaveRoadmapChecklistRequestDto()));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveChecklistAsync(
            Reviewer, item.Id, new SaveRoadmapChecklistRequestDto
            {
                Entries = [new RoadmapChecklistEntryDto { Code = "bad" }]
            }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveChecklistAsync(
            Reviewer, item.Id, new SaveRoadmapChecklistRequestDto
            {
                Entries =
                [
                    new RoadmapChecklistEntryDto { Code = "order" },
                    new RoadmapChecklistEntryDto { Code = "order" }
                ]
            }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveChecklistAsync(
            Reviewer, item.Id, new SaveRoadmapChecklistRequestDto
            {
                Entries =
                [
                    new RoadmapChecklistEntryDto
                    {
                        Code = "order", Note = new string('a', RoadmapReviewPolicy.MaxNoteLength + 1)
                    }
                ]
            }));
        Assert.Null(item.ReviewProgress);
    }

    [Fact]
    public async Task ApproveAsync_PublishesRoadmapClosesItemAndNotifiesSubmitter()
    {
        var item = AddClaimedReview();
        await PassChecklistAsync(item.Id);
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        var result = await _sut.ApproveAsync(Reviewer, item.Id);

        var roadmap = _h.Roadmaps.Single();
        Assert.Equal("Approved", result.Decision);
        Assert.Equal(RoadmapTemplateStatus.Published, roadmap.Status);
        Assert.Equal(Reviewer, roadmap.PublishedByUserId);
        Assert.NotNull(roadmap.PublishedAt);
        Assert.Equal(ModerationItemStatus.Approved, item.Status);
        Assert.Equal(Reviewer, item.DecidedByUserId);
        Assert.Equal(2, item.RowVersion);
        Assert.Contains("approved", item.Feedback);
        var notification = Assert.Single(sent);
        Assert.Equal(Submitter, notification.Recipient);
        Assert.Equal(NotificationType.ContentModerationResult, notification.Type);
        using var payload = JsonDocument.Parse(notification.Payload!);
        Assert.Equal("approved", payload.RootElement.GetProperty("decision").GetString());
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_RejectsIncompleteChecklistChangedStoryComboAndOwnRoadmap()
    {
        var item = AddClaimedReview();
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ApproveAsync(Reviewer, item.Id));

        await PassChecklistAsync(item.Id);
        _h.Stories.Single(story => story.Id == 10).IsPublished = false;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ApproveAsync(Reviewer, item.Id));
        _h.Stories.Single(story => story.Id == 10).IsPublished = true;

        _h.AddRoadmap(
            9, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Published, Submitter, 10);
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ApproveAsync(Reviewer, item.Id));
        _h.Roadmaps.RemoveAll(roadmap => roadmap.Id == 9);

        _h.Roadmaps.Single().CreatedByUserId = Reviewer;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ApproveAsync(Reviewer, item.Id));
        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
    }

    [Fact]
    public async Task ApproveAsync_CloneArchivesSourceBeforePublishingClone()
    {
        var source = _h.AddRoadmap(
            9, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Published, Submitter, 10);
        var item = AddClaimedReview();
        var clone = _h.Roadmaps.Single(roadmap => roadmap.Id == 1);
        clone.ClonedFromId = source.Id;
        await PassChecklistAsync(item.Id);

        var result = await _sut.ApproveAsync(Reviewer, item.Id);

        Assert.Equal(source.Id, result.ReplacedRoadmapId);
        Assert.Equal(RoadmapTemplateStatus.Archived, source.Status);
        Assert.Equal(RoadmapArchiveReason.ReplacedByClone, source.ArchivedReason);
        Assert.Equal(Reviewer, source.ArchivedByUserId);
        Assert.Equal(RoadmapTemplateStatus.Published, clone.Status);
        _h.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task ApproveAsync_AuditFailureRollsBack()
    {
        var item = AddClaimedReview();
        await PassChecklistAsync(item.Id);
        _h.Audit.Setup(audit => audit.LogAsync(
                It.IsAny<int?>(), "RoadmapTemplateApproved", It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ApproveAsync(Reviewer, item.Id));

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestChangesAsync_ReturnsDraftStoresStructuredFeedbackAndNotifies()
    {
        var item = AddClaimedReview();
        await _sut.SaveChecklistAsync(Reviewer, item.Id, new SaveRoadmapChecklistRequestDto
        {
            Entries = [new RoadmapChecklistEntryDto { Code = "theme", Passed = false, Note = "Chưa cùng chủ đề" }]
        });
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        var result = await _sut.RequestChangesAsync(Reviewer, item.Id, new RequestRoadmapChangesDto
        {
            GeneralNote = "  Cần điều chỉnh bộ truyện  ",
            Parts =
            [
                new RoadmapChangePartDto
                {
                    PartType = "story", StoryId = 10, Action = "replace", Note = "  Đổi truyện  "
                },
                new RoadmapChangePartDto { PartType = "order", Note = "Sắp xếp lại" }
            ]
        });

        Assert.Equal("ChangesRequested", result.Decision);
        Assert.Equal(RoadmapTemplateStatus.Draft, _h.Roadmaps.Single().Status);
        Assert.Null(_h.Roadmaps.Single().SubmittedAt);
        Assert.Equal(ModerationItemStatus.ChangesRequested, item.Status);
        Assert.Equal("Cần điều chỉnh bộ truyện", item.DecisionReason);
        Assert.Contains("changes_requested", item.Feedback);
        Assert.Equal(Submitter, Assert.Single(sent).Recipient);
        Assert.Equal(2, _h.RoadmapItems.Count);
    }

    [Fact]
    public async Task RequestChangesAsync_ValidatesGeneralNoteAndParts()
    {
        var item = AddClaimedReview();
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RequestChangesAsync(
            Reviewer, item.Id, new RequestRoadmapChangesDto { GeneralNote = " " }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RequestChangesAsync(
            Reviewer, item.Id, new RequestRoadmapChangesDto
            {
                GeneralNote = "Sửa",
                Parts =
                [
                    new RoadmapChangePartDto
                    {
                        PartType = "story", StoryId = 999, Action = "remove", Note = "Bỏ"
                    }
                ]
            }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RequestChangesAsync(
            Reviewer, item.Id, new RequestRoadmapChangesDto
            {
                GeneralNote = "Sửa",
                Parts =
                [
                    new RoadmapChangePartDto { PartType = "order", Note = "A" },
                    new RoadmapChangePartDto { PartType = "order", Note = "B" }
                ]
            }));
        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
    }

    [Fact]
    public async Task RejectAsync_ReturnsDraftStoresReasonAndSkipsInactiveSubmitter()
    {
        var item = AddClaimedReview();
        _h.Users.Single(user => user.Id == Submitter).Status = AccountStatus.Suspended;

        var result = await _sut.RejectAsync(Reviewer, item.Id, new RejectRoadmapRequestDto
        {
            Reason = "  Không phù hợp định hướng  "
        });

        Assert.Equal("Rejected", result.Decision);
        Assert.Equal(RoadmapTemplateStatus.Draft, _h.Roadmaps.Single().Status);
        Assert.Equal("Không phù hợp định hướng", item.DecisionReason);
        Assert.Contains("rejected", item.Feedback);
        _h.Notifications.Verify(notification => notification.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_RequiresBoundedReason()
    {
        var item = AddClaimedReview();
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RejectAsync(
            Reviewer, item.Id, new RejectRoadmapRequestDto { Reason = " " }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RejectAsync(
            Reviewer, item.Id, new RejectRoadmapRequestDto
            {
                Reason = new string('a', RoadmapReviewPolicy.MaxTextLength + 1)
            }));
    }

    [Fact]
    public async Task GetAsync_AfterChangesRequested_CreatorSeesLatestReview()
    {
        var item = AddClaimedReview();
        await _sut.RequestChangesAsync(Reviewer, item.Id, new RequestRoadmapChangesDto
        {
            GeneralNote = "Cần sửa",
            Parts = [new RoadmapChangePartDto { PartType = "order", Note = "Đổi thứ tự" }]
        });
        var templateService = new RoadmapTemplateService(
            _h.UnitOfWork.Object,
            new RoadmapSubmissionChecker(_h.UnitOfWork.Object),
            new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object),
            _h.Audit.Object);

        var detail = await templateService.GetAsync(Submitter, 1);

        Assert.NotNull(detail.LastReview);
        Assert.Equal("changes_requested", detail.LastReview!.Decision);
        Assert.Equal("Cần sửa", detail.LastReview.GeneralNote);
        Assert.Equal("Tran Van Minh", detail.LastReview.ReviewerName);
        Assert.Equal("order", detail.LastReview.Parts.Single().PartType);
    }

    [Fact]
    public async Task GetAsync_LastReviewHiddenWhilePendingOrPublished()
    {
        var item = AddClaimedReview();
        await _sut.RejectAsync(Reviewer, item.Id, new RejectRoadmapRequestDto { Reason = "Từ chối" });
        var templateService = new RoadmapTemplateService(
            _h.UnitOfWork.Object,
            new RoadmapSubmissionChecker(_h.UnitOfWork.Object),
            new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object),
            _h.Audit.Object);

        Assert.NotNull((await templateService.GetAsync(Submitter, 1)).LastReview);
        _h.Roadmaps.Single().Status = RoadmapTemplateStatus.PendingReview;
        Assert.Null((await templateService.GetAsync(Submitter, 1)).LastReview);
        _h.Roadmaps.Single().Status = RoadmapTemplateStatus.Published;
        Assert.Null((await templateService.GetAsync(Submitter, 1)).LastReview);
    }

    [Fact]
    public void Controller_IsRestrictedToStaffAndExposesFiveActions()
    {
        var authorize = typeof(RoadmapReviewController).GetCustomAttribute<AuthorizeAttribute>();
        var methods = typeof(RoadmapReviewController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("Moderator,Administrator", authorize!.Roles);
        Assert.Equal(new[] { "Approve", "GetPackage", "Reject", "RequestChanges", "SaveChecklist" }, methods);
    }
}
