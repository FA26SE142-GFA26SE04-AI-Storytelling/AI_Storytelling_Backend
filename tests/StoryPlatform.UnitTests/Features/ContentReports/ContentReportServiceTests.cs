using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Services;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentReports;

public class ContentReportServiceTests
{
    private readonly ContentReportHarness _h = new();
    private readonly ContentReportService _sut;

    private const int Reporter = ContentReportHarness.ReporterId;
    private const int PublicStory = ContentReportHarness.PublicStoryId;

    public ContentReportServiceTests()
    {
        var queue = new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object);
        _sut = new ContentReportService(
            _h.UnitOfWork.Object,
            new ReportableStoryResolver(_h.UnitOfWork.Object),
            queue,
            _h.Audit.Object,
            _h.Notifications.Object);
    }

    private static CreateContentReportRequestDto Request(
        ContentReportReason reason = ContentReportReason.Inappropriate,
        int storyId = PublicStory) => new() { StoryId = storyId, Reason = reason };

    private Task<ContentReportResultDto> CreateAsync(
        CreateContentReportRequestDto request,
        int actorId = Reporter) => _sut.CreateAsync(actorId, request);

    private static CreateReviewItemRequestDto ReviewRequest(
        ContentReportReason reason = ContentReportReason.Inappropriate,
        int storyId = PublicStory,
        int? roadmapId = null) => new()
    {
        StoryId = storyId,
        Reason = reason,
        ReadingRoadmapId = roadmapId
    };

    private Task<ContentReportResultDto> CreateReviewAsync(
        CreateReviewItemRequestDto request,
        int actorId = ContentReportHarness.ModeratorId) =>
        _sut.CreateReviewItemAsync(actorId, request);

    [Fact]
    public async Task CreateAsync_Valid_CreatesPendingReportWithDisplayedVersion()
    {
        var before = DateTime.UtcNow;
        var request = Request(ContentReportReason.WrongAgeBand);
        request.Description = "  Quá khó với bé 6 tuổi  ";
        request.AttachmentUrl = "  https://example.com/nguon  ";

        var result = await CreateAsync(request);

        var report = Assert.Single(_h.Reports);
        Assert.Equal(report.Id, result.ReportId);
        Assert.Equal(PublicStory, report.StoryId);
        Assert.Equal(Reporter, report.ReporterUserId);
        Assert.Equal(50, report.StoryVersionId);
        Assert.Equal(ContentReportStatus.Pending, report.Status);
        Assert.False(report.IsReviewItem);
        Assert.Equal("Quá khó với bé 6 tuổi", report.Description);
        Assert.Equal("https://example.com/nguon", report.AttachmentUrl);
        Assert.True(report.CreatedAt >= before);
        Assert.Equal("WrongAgeBand", result.Reason);
        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task CreateAsync_PrivateStory_UsesCurrentVersion()
    {
        await CreateAsync(Request(storyId: ContentReportHarness.PrivateStoryId));

        Assert.Equal(60, Assert.Single(_h.Reports).StoryVersionId);
    }

    [Fact]
    public async Task CreateAsync_DoesNotArchiveOrUnpublishStory()
    {
        await CreateAsync(Request(ContentReportReason.SafetyConcern));

        var story = _h.Stories.Single(candidate => candidate.Id == PublicStory);
        Assert.True(story.IsPublished);
        Assert.Equal(StoryStatus.Ready, story.Status);
        Assert.Null(story.ArchivedAt);
    }

    [Fact]
    public async Task CreateAsync_BlankOptionalFields_AreStoredAsNull()
    {
        var request = Request();
        request.Description = "   ";
        request.AttachmentUrl = "  ";

        await CreateAsync(request);

        Assert.Null(Assert.Single(_h.Reports).Description);
        Assert.Null(Assert.Single(_h.Reports).AttachmentUrl);
    }

    [Theory]
    [InlineData(ContentReportReason.SafetyConcern, ModerationPriority.High)]
    [InlineData(ContentReportReason.Inappropriate, ModerationPriority.Medium)]
    [InlineData(ContentReportReason.WrongAgeBand, ModerationPriority.Medium)]
    [InlineData(ContentReportReason.Copyright, ModerationPriority.Medium)]
    [InlineData(ContentReportReason.Cultural, ModerationPriority.Medium)]
    [InlineData(ContentReportReason.Other, ModerationPriority.Medium)]
    public async Task CreateAsync_EnqueuesWithExpectedPriority(
        ContentReportReason reason, ModerationPriority expectedPriority)
    {
        var before = DateTime.UtcNow;

        await CreateAsync(Request(reason));

        var report = Assert.Single(_h.Reports);
        var item = Assert.Single(_h.Items);
        Assert.Equal(ModerationItemType.ContentReport, item.ItemType);
        Assert.Equal(ModerationItemStatus.Pending, item.Status);
        Assert.Equal(expectedPriority, item.Priority);
        Assert.Equal(report.Id, item.ContentReportId);
        Assert.Equal(50, item.StoryVersionId);
        Assert.Equal(Reporter, item.CreatedByUserId);
        Assert.InRange(item.SlaDueAt!.Value, before.AddHours(48), DateTime.UtcNow.AddHours(48));
    }

    [Fact]
    public async Task CreateAsync_InvalidReason_IsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateAsync(Request((ContentReportReason)99)));
        Assert.Empty(_h.Reports);
    }

    [Fact]
    public async Task CreateAsync_DescriptionLimit_IsEnforced()
    {
        var accepted = Request();
        accepted.Description = new string('a', 2000);
        await CreateAsync(accepted);
        Assert.Single(_h.Reports);

        _h.Reports.Clear();
        _h.Items.Clear();
        var rejected = Request();
        rejected.Description = new string('a', 2001);
        await Assert.ThrowsAsync<BadRequestException>(() => CreateAsync(rejected));
    }

    [Theory]
    [InlineData("không phải url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("/duong/dan/tuong/doi")]
    public async Task CreateAsync_InvalidAttachmentUrl_IsBadRequest(string url)
    {
        var request = Request();
        request.AttachmentUrl = url;

        await Assert.ThrowsAsync<BadRequestException>(() => CreateAsync(request));
        Assert.Empty(_h.Reports);
    }

    [Fact]
    public async Task CreateAsync_AttachmentOver500Characters_IsBadRequest()
    {
        var request = Request();
        request.AttachmentUrl = "https://example.com/" + new string('a', 490);

        await Assert.ThrowsAsync<BadRequestException>(() => CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_RequiresActiveParent()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateAsync(Request(), ContentReportHarness.ModeratorId));

        _h.Users.Single(user => user.Id == Reporter).Status = AccountStatus.Suspended;
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAsync(Request()));

        _h.Users.Single(user => user.Id == Reporter).Status = AccountStatus.Active;
        _h.Users.Single(user => user.Id == Reporter).IsDeleted = true;
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAsync(Request()));
        Assert.Empty(_h.Reports);
    }

    [Fact]
    public async Task CreateAsync_NoChildAccess_CreatesNothingAndStartsNoTransaction()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateAsync(Request(), ContentReportHarness.OtherParentId));

        Assert.Empty(_h.Reports);
        Assert.Empty(_h.Items);
        _h.UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SamePendingReport_IsConflict()
    {
        await CreateAsync(Request());

        await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(Request(ContentReportReason.Other)));
        Assert.Single(_h.Reports);
    }

    [Fact]
    public async Task CreateAsync_ResolvedOrReviewItemReport_DoesNotBlockNewParentReport()
    {
        _h.Reports.Add(new ContentReport
        {
            Id = 40,
            StoryId = PublicStory,
            ReporterUserId = Reporter,
            Reason = ContentReportReason.Other,
            Status = ContentReportStatus.Pending,
            IsReviewItem = true
        });
        await CreateAsync(Request());
        Assert.Equal(2, _h.Reports.Count);

        _h.Reports.Single(report => !report.IsReviewItem).Status = ContentReportStatus.ReviewedDismissed;
        await CreateAsync(Request(ContentReportReason.Cultural));
        Assert.Equal(3, _h.Reports.Count);
    }

    [Fact]
    public async Task CreateAsync_AnotherReporterOfSameStory_IsAllowed()
    {
        _h.AddChild(101, ContentReportHarness.OtherParentId, AgeBand.Age_6_8);
        await CreateAsync(Request());

        await CreateAsync(Request(), ContentReportHarness.OtherParentId);

        Assert.Equal(2, _h.Reports.Count);
    }

    [Fact]
    public async Task CreateAsync_WritesAuditAndCommitsTransaction()
    {
        await CreateAsync(Request());

        var report = Assert.Single(_h.Reports);
        _h.Audit.Verify(audit => audit.LogAsync(
            Reporter, "ContentReportCreated", nameof(ContentReport), report.Id,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_AuditFailure_RollsBack()
    {
        _h.Audit.Setup(audit => audit.LogAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(Request()));

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NotifiesEveryActiveModerator()
    {
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await CreateAsync(Request(ContentReportReason.Copyright));

        Assert.Equal(new[] { ContentReportHarness.ModeratorId, ContentReportHarness.OtherModeratorId },
            sent.Select(notification => notification.Recipient).OrderBy(id => id));
        Assert.All(sent, notification => Assert.Equal(NotificationType.ContentReported, notification.Type));

        using var payload = JsonDocument.Parse(sent[0].Payload!);
        Assert.Equal(Assert.Single(_h.Reports).Id, payload.RootElement.GetProperty("contentReportId").GetInt32());
        Assert.Equal(Assert.Single(_h.Items).Id, payload.RootElement.GetProperty("moderationItemId").GetInt32());
        Assert.Equal(PublicStory, payload.RootElement.GetProperty("storyId").GetInt32());
        Assert.Equal("Copyright", payload.RootElement.GetProperty("reason").GetString());
        Assert.False(payload.RootElement.TryGetProperty("reporterUserId", out _));
        Assert.DoesNotContain(sent, notification => notification.Recipient == Reporter);
        Assert.DoesNotContain(sent, notification => notification.Recipient == ContentReportHarness.AdminId);
    }

    [Fact]
    public async Task CreateAsync_NoActiveModerator_FallsBackToActiveAdministrators()
    {
        _h.Users.Single(user => user.Id == ContentReportHarness.ModeratorId).Status = AccountStatus.Suspended;
        _h.Users.Single(user => user.Id == ContentReportHarness.OtherModeratorId).IsDeleted = true;
        _h.AddUser(2, "admin_demo2", UserRole.Administrator);
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await CreateAsync(Request());

        Assert.Equal(new[] { ContentReportHarness.AdminId, 2 },
            sent.Select(notification => notification.Recipient).OrderBy(id => id));
    }

    [Fact]
    public async Task CreateAsync_NoStaffRecipient_StillCreatesReportWithoutNotification()
    {
        foreach (var user in _h.Users.Where(user =>
                     user.Role is UserRole.Moderator or UserRole.Administrator))
        {
            user.Status = AccountStatus.Suspended;
        }

        await CreateAsync(Request());

        Assert.Single(_h.Reports);
        Assert.Single(_h.Items);
        _h.Notifications.Verify(notification => notification.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NotificationFailure_RollsBack()
    {
        _h.Notifications.Setup(notification => notification.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(Request()));

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateReviewItemAsync_CreatesReviewReportForAnotherModerator()
    {
        await CreateReviewAsync(ReviewRequest(ContentReportReason.SafetyConcern));

        var report = Assert.Single(_h.Reports);
        var item = Assert.Single(_h.Items);
        Assert.True(report.IsReviewItem);
        Assert.Equal(ContentReportHarness.ModeratorId, report.ReporterUserId);
        Assert.Equal(50, report.StoryVersionId);
        Assert.Equal(ContentReportStatus.Pending, report.Status);
        Assert.Equal(ContentReportHarness.ModeratorId, item.CreatedByUserId);
        Assert.Equal(ModerationPriority.High, item.Priority);
        Assert.Equal(ModerationItemStatus.Pending, item.Status);

        var queue = new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object);
        var ownList = await queue.ListAsync(
            ContentReportHarness.ModeratorId, new ModerationQueueQueryDto());
        var otherList = await queue.ListAsync(
            ContentReportHarness.OtherModeratorId, new ModerationQueueQueryDto());
        Assert.Empty(ownList.Items);
        Assert.Contains(otherList.Items, candidate => candidate.Id == item.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            queue.ClaimAsync(ContentReportHarness.ModeratorId, item.Id));
    }

    [Fact]
    public async Task CreateReviewItemAsync_NotifiesOtherModeratorsButNotCreator()
    {
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await CreateReviewAsync(ReviewRequest(ContentReportReason.Copyright));

        var notification = Assert.Single(sent);
        Assert.Equal(ContentReportHarness.OtherModeratorId, notification.Recipient);
        Assert.Equal(NotificationType.ContentReported, notification.Type);
    }

    [Fact]
    public async Task CreateReviewItemAsync_NoOtherModerator_FallsBackToAdministrators()
    {
        _h.Users.Single(user => user.Id == ContentReportHarness.OtherModeratorId).Status = AccountStatus.Suspended;
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await CreateReviewAsync(ReviewRequest());

        Assert.Equal(ContentReportHarness.AdminId, Assert.Single(sent).Recipient);
    }

    [Fact]
    public async Task CreateReviewItemAsync_ValidatesActorStoryRoadmapAndDuplicate()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateReviewAsync(ReviewRequest(), ContentReportHarness.AdminId));

        _h.Stories.Single(story => story.Id == PublicStory).Status = StoryStatus.Archived;
        await Assert.ThrowsAsync<BadRequestException>(() => CreateReviewAsync(ReviewRequest()));
        _h.Stories.Single(story => story.Id == PublicStory).Status = StoryStatus.Ready;

        _h.ContentCategories.Add(new ContentCategory
        {
            Id = 5, Code = "friendship", DisplayName = "Tình bạn"
        });
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, DateTime.UtcNow, PublicStory);
        await CreateReviewAsync(ReviewRequest(roadmapId: 1));
        Assert.Equal(1, _h.Reports.Single().ReadingRoadmapId);
        Assert.Equal(1, _h.Items.Single().ReadingRoadmapId);
        await Assert.ThrowsAsync<ConflictException>(() => CreateReviewAsync(ReviewRequest()));
    }

    [Fact]
    public async Task CreateReviewItemAsync_DoesNotChangePublishedStory()
    {
        await CreateReviewAsync(ReviewRequest());

        var story = _h.Stories.Single(candidate => candidate.Id == PublicStory);
        Assert.True(story.IsPublished);
        Assert.Equal(StoryStatus.Ready, story.Status);
        Assert.Null(story.ArchivedAt);
    }

    [Fact]
    public void Controller_IsRestrictedToParentAndExposesCreate()
    {
        var authorize = typeof(ContentReportController).GetCustomAttribute<AuthorizeAttribute>();
        var methods = typeof(ContentReportController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("Parent", authorize!.Roles);
        Assert.Equal(new[] { "Create" }, methods);
    }

    private void CaptureNotifications(List<(int Recipient, NotificationType Type, string? Payload)> sent) =>
        _h.Notifications
            .Setup(notification => notification.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback((int recipient, NotificationType type, string? payload, CancellationToken _) =>
                sent.Add((recipient, type, payload)))
            .ReturnsAsync(new NotificationDto());
}
