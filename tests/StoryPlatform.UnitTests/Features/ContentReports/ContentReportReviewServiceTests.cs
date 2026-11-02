using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentReports;

public class ContentReportReviewServiceTests
{
    private readonly ContentReportHarness _h = new();
    private readonly Mock<IStoryArchiveConsequences> _consequences = new();
    private readonly ContentReportReviewService _sut;

    private const int Moderator = ContentReportHarness.ModeratorId;
    private const int PublicStory = ContentReportHarness.PublicStoryId;

    public ContentReportReviewServiceTests()
    {
        _sut = new ContentReportReviewService(
            _h.UnitOfWork.Object, _h.Audit.Object, _h.Notifications.Object, _consequences.Object);
    }

    private void AddScene(int sceneId, int versionId = 50, string text = "Gọi 0912345678 ngay")
    {
        _h.Scenes.Add(new StoryScene
        {
            Id = sceneId,
            StoryVersionId = versionId,
            SceneIndex = sceneId,
            TextRangeStart = 0,
            TextRangeEnd = 1,
            SceneText = text
        });
        _h.Beats.Add(new IllustrationBeat
        {
            Id = sceneId * 10,
            StorySceneId = sceneId,
            BeatOrder = 1,
            StartOffset = 0,
            EndOffset = 1,
            VisualFocus = "x"
        });
        _h.Assets.Add(new MediaAsset
        {
            Id = sceneId * 100,
            StoryVersionId = versionId,
            StorySceneId = sceneId,
            IllustrationBeatId = sceneId * 10,
            Type = MediaType.Illustration,
            Status = MediaStatus.Ready,
            Url = $"https://cdn.test/img{sceneId}.png"
        });
        _h.Segments.Add(new StorySegment
        {
            Id = sceneId * 1000,
            StorySceneId = sceneId,
            SegmentOrder = 1,
            StartOffset = 0,
            EndOffset = 1,
            TextContent = "x"
        });
        _h.Assets.Add(new MediaAsset
        {
            Id = sceneId * 100 + 1,
            StoryVersionId = versionId,
            StorySceneId = sceneId,
            StorySegmentId = sceneId * 1000,
            Type = MediaType.TtsAudio,
            Status = MediaStatus.Ready,
            Url = $"https://cdn.test/audio{sceneId}.mp3"
        });
    }

    private Task<ContentReportDetailDto> GetDetailAsync(int itemId, int actorId = Moderator) =>
        _sut.GetDetailAsync(actorId, itemId);

    private Task<ContentReportDecisionResultDto> DismissAsync(
        int itemId, string reason = "Không vi phạm", int actorId = Moderator) =>
        _sut.DismissAsync(actorId, itemId, new DismissReportRequestDto { Reason = reason });

    private Task<ContentReportDecisionResultDto> ArchiveAsync(
        int itemId,
        ArchivedReason archiveReason = ArchivedReason.SafetyConcern,
        string reason = "Có cảnh bạo lực",
        int actorId = Moderator) =>
        _sut.ArchiveAsync(actorId, itemId, new ArchiveReportRequestDto
        {
            ArchiveReason = archiveReason,
            Reason = reason
        });

    private Task<ContentReportDecisionResultDto> EscalateAsync(
        int itemId, string reason = "Nghi Content Creator cố ý", int actorId = Moderator) =>
        _sut.EscalateAsync(actorId, itemId, new EscalateReportRequestDto { Reason = reason });

    private ModerationItem EscalatedItem(ContentReportReason reason = ContentReportReason.Cultural)
    {
        var item = _h.AddReportItem(reason);
        item.Status = ModerationItemStatus.Escalated;
        item.ClaimedAt = DateTime.UtcNow.AddDays(-5);
        item.EscalationReason = "Nghi Content Creator cố ý đưa nội dung xấu";
        item.EscalatedAt = DateTime.UtcNow.AddDays(-5);
        _h.Reports.Single().Status = ContentReportStatus.Escalated;
        return item;
    }

    private void CaptureNotifications(
        List<(int Recipient, NotificationType Type, string? Payload)> sent) =>
        _h.Notifications
            .Setup(notification => notification.CreateAsync(
                It.IsAny<int>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback((int recipient, NotificationType type, string? payload, CancellationToken _) =>
                sent.Add((recipient, type, payload)));

    [Fact]
    public async Task GetDetailAsync_ReturnsReportStoryVersionAndMedia()
    {
        var item = _h.AddReportItem(ContentReportReason.Copyright);
        var report = _h.Reports.Single();
        report.AttachmentUrl = "https://example.com/nguon";
        AddScene(1);
        var version = _h.Versions.Single(existing => existing.Id == 50);
        version.Content = "Mở đầu. Kết thúc.";
        version.Lesson = "Biết chia sẻ";
        _h.ContentCategories.Add(new ContentCategory
        {
            Id = 5, Code = "friendship", DisplayName = "Tình bạn"
        });
        _h.StoryCategories.Add(new StoryCategory
        {
            Id = 1, StoryId = PublicStory, ContentCategoryId = 5
        });
        _h.Quizzes.Add(new QuizItem
        {
            Id = 1,
            StoryVersionId = 50,
            Type = QuizType.TrueFalse,
            Question = "Đúng?",
            CorrectAnswer = "Đúng"
        });
        _h.Vocabulary.Add(new StoryVocabulary
        {
            Id = 1, StoryVersionId = 50, Term = "chia sẻ", Definition = "cùng dùng"
        });

        var detail = await GetDetailAsync(item.Id);

        Assert.Equal(item.Id, detail.ItemId);
        Assert.Equal(report.Id, detail.ReportId);
        Assert.Equal("Copyright", detail.Reason);
        Assert.Equal("https://example.com/nguon", detail.AttachmentUrl);
        Assert.Equal(PublicStory, detail.Story.StoryId);
        Assert.Equal("friendship", detail.Story.Labels.Single().Code);
        Assert.Equal(50, detail.Version!.VersionId);
        Assert.Equal("Mở đầu. Kết thúc.", detail.Version.Content);
        var scene = Assert.Single(detail.Scenes);
        Assert.Equal(["https://cdn.test/img1.png"], scene.ImageUrls);
        Assert.Equal(["https://cdn.test/audio1.mp3"], scene.AudioUrls);
        Assert.Equal("chia sẻ", detail.Vocabulary.Single().Term);
        Assert.Equal("Đúng", detail.Quizzes.Single().CorrectAnswer);
    }

    [Fact]
    public async Task GetDetailAsync_MasksSensitiveTextWithoutMaskingUrls()
    {
        var item = _h.AddReportItem(
            storyId: ContentReportHarness.PrivateStoryId, versionId: 60);
        _h.Children.Single().Nickname = "Bé Na";
        _h.Reports.Single().Description = "Bé Na gọi 0912345678";
        _h.Reports.Single().AttachmentUrl = "https://example.com/0912345678";
        var story = _h.Stories.Single(existing => existing.Id == ContentReportHarness.PrivateStoryId);
        story.Title = "Chuyến đi của Bé Na";
        var version = _h.Versions.Single(existing => existing.Id == 60);
        version.Content = "Bé Na gọi 0987654321 hoặc mail na@example.com";
        AddScene(1, 60, "Bé Na nói số 0912345678");
        _h.Quizzes.Add(new QuizItem
        {
            Id = 1,
            StoryVersionId = 60,
            Type = QuizType.ShortAnswer,
            Question = "Bé Na tên gì?",
            CorrectAnswer = "Bé Na"
        });

        var detail = await GetDetailAsync(item.Id);

        Assert.Equal("[tên bé] gọi [số điện thoại]", detail.Description);
        Assert.Equal("https://example.com/0912345678", detail.AttachmentUrl);
        Assert.Equal("Chuyến đi của [tên bé]", detail.Story.Title);
        Assert.Equal("[tên bé] gọi [số điện thoại] hoặc mail [email]", detail.Version!.Content);
        Assert.Equal("[tên bé] nói số [số điện thoại]", detail.Scenes.Single().Text);
        Assert.Equal("[tên bé] tên gì?", detail.Quizzes.Single().Question);
    }

    [Fact]
    public async Task GetDetailAsync_ShowsListeningMaskedAndWithoutDeletedRows()
    {
        var item = _h.AddReportItem(storyId: ContentReportHarness.PrivateStoryId, versionId: 60);
        _h.Children.Single().Nickname = "Bé Na";
        _h.ListeningInteractions.Add(new ListeningInteraction
        {
            Id = 1, StoryVersionId = 60, Type = ListeningInteractionType.Checkpoint, AnchorOffset = 10,
            Kind = CheckpointKind.Emotion, Prompt = "Bé Na thấy gì?", MatchFeedback = "Gọi 0912345678 nhé"
        });
        _h.ListeningInteractions.Add(new ListeningInteraction
        {
            Id = 2, StoryVersionId = 60, Type = ListeningInteractionType.RolePlay, AnchorOffset = 20, IsDeleted = true
        });
        _h.ListeningOptions.Add(new ListeningInteractionOption
        { Id = 11, ListeningInteractionId = 1, SortOrder = 2, Text = "Vui", Feedback = "Ý hay" });
        _h.ListeningOptions.Add(new ListeningInteractionOption
        { Id = 10, ListeningInteractionId = 1, SortOrder = 1, Text = "Bé Na sợ", Feedback = "Đúng rồi", IsMatch = true });

        var detail = await GetDetailAsync(item.Id);
        var listening = Assert.Single(detail.Listening);
        Assert.Equal("Checkpoint", listening.Type);
        Assert.Equal("Emotion", listening.Kind);
        Assert.Equal("[tên bé] thấy gì?", listening.Prompt);
        Assert.Equal("Gọi [số điện thoại] nhé", listening.MatchFeedback);
        Assert.Equal(new[] { "[tên bé] sợ", "Vui" }, listening.Options.Select(x => x.Text));
        Assert.True(listening.Options[0].IsMatch);
    }

    [Fact]
    public async Task GetDetailAsync_DoesNotExposeReporterOrChildProfile()
    {
        var item = _h.AddReportItem(
            storyId: ContentReportHarness.PrivateStoryId, versionId: 60);

        var json = JsonSerializer.Serialize(await GetDetailAsync(item.Id));

        Assert.DoesNotContain("parent_demo1", json);
        Assert.DoesNotContain("reporter", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ChildProfile", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(typeof(ContentReportDetailDto).GetProperties(),
            property => property.Name.Contains("Reporter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetDetailAsync_MissingOrForeignVersion_ReturnsStoryWithoutVersionContent()
    {
        var withoutVersion = _h.AddReportItem(versionId: null);
        var first = await GetDetailAsync(withoutVersion.Id);
        Assert.Null(first.Version);
        Assert.Empty(first.Scenes);

        _h.Items.Clear();
        _h.Reports.Clear();
        var foreignVersion = _h.AddReportItem(versionId: 60);
        var second = await GetDetailAsync(foreignVersion.Id);
        Assert.Null(second.Version);
    }

    [Fact]
    public async Task GetDetailAsync_DeletedVersion_IsNotReturned()
    {
        var item = _h.AddReportItem();
        _h.Versions.Single(version => version.Id == 50).IsDeleted = true;

        var detail = await GetDetailAsync(item.Id);

        Assert.Null(detail.Version);
        Assert.Empty(detail.Scenes);
    }

    [Fact]
    public async Task GetDetailAsync_WritesAuditEveryTime()
    {
        var item = _h.AddReportItem();

        await GetDetailAsync(item.Id);
        await GetDetailAsync(item.Id);

        _h.Audit.Verify(audit => audit.LogAsync(
            Moderator,
            "ContentReportOpened",
            nameof(ModerationItem),
            item.Id,
            null,
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetDetailAsync_ReviewItemIsMarked()
    {
        var item = _h.AddReportItem();
        _h.Reports.Single().IsReviewItem = true;

        var detail = await GetDetailAsync(item.Id);

        Assert.True(detail.IsReviewItem);
    }

    [Fact]
    public async Task GetDetailAsync_ClaimedBySomeoneElse_IsForbidden()
    {
        var item = _h.AddReportItem();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            GetDetailAsync(item.Id, ContentReportHarness.OtherModeratorId));
    }

    [Fact]
    public async Task GetDetailAsync_ExpiredClaim_IsConflict()
    {
        var item = _h.AddReportItem();
        item.ClaimedAt = DateTime.UtcNow.AddHours(-2).AddMinutes(-1);

        await Assert.ThrowsAsync<ConflictException>(() => GetDetailAsync(item.Id));
    }

    [Fact]
    public async Task GetDetailAsync_UnclaimedItem_IsForbidden()
    {
        var item = _h.AddReportItem();
        item.Status = ModerationItemStatus.Pending;
        item.ClaimedByUserId = null;

        await Assert.ThrowsAsync<ForbiddenException>(() => GetDetailAsync(item.Id));
    }

    [Fact]
    public async Task GetDetailAsync_SuspendedModerator_IsForbidden()
    {
        var item = _h.AddReportItem();
        _h.Users.Single(user => user.Id == Moderator).Status = AccountStatus.Suspended;

        await Assert.ThrowsAsync<ForbiddenException>(() => GetDetailAsync(item.Id));
    }

    [Fact]
    public async Task GetDetailAsync_InvalidActorItemAndReportStates_AreRejected()
    {
        var item = _h.AddReportItem();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            GetDetailAsync(item.Id, ContentReportHarness.ReporterId));

        item.ItemType = ModerationItemType.PublicStoryReview;
        await Assert.ThrowsAsync<BadRequestException>(() => GetDetailAsync(item.Id));

        item.ItemType = ModerationItemType.ContentReport;
        item.ContentReportId = null;
        await Assert.ThrowsAsync<BadRequestException>(() => GetDetailAsync(item.Id));

        item.ContentReportId = _h.Reports.Single().Id;
        _h.Reports.Single().Status = ContentReportStatus.ReviewedDismissed;
        await Assert.ThrowsAsync<ConflictException>(() => GetDetailAsync(item.Id));
    }

    [Fact]
    public async Task GetDetailAsync_UnknownOrDeletedItem_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => GetDetailAsync(9999));

        var item = _h.AddReportItem();
        item.IsDeleted = true;
        await Assert.ThrowsAsync<NotFoundException>(() => GetDetailAsync(item.Id));
    }

    [Fact]
    public async Task GetDetailAsync_DeletedReport_IsNotFound()
    {
        var item = _h.AddReportItem();
        _h.Reports.Single().IsDeleted = true;

        await Assert.ThrowsAsync<NotFoundException>(() => GetDetailAsync(item.Id));
    }

    [Fact]
    public async Task DismissAsync_RecordsDecisionWithoutChangingStory()
    {
        var item = _h.AddReportItem(ContentReportReason.SafetyConcern);
        var result = await DismissAsync(item.Id, "  Không vi phạm  ");

        Assert.Equal("Dismissed", result.Decision);
        Assert.False(result.StoryArchived);
        Assert.Equal(ModerationItemStatus.Dismissed, item.Status);
        Assert.Equal(Moderator, item.DecidedByUserId);
        Assert.Equal("Không vi phạm", item.DecisionReason);
        Assert.Equal(1, item.RowVersion);
        var report = _h.Reports.Single();
        Assert.Equal(ContentReportStatus.ReviewedDismissed, report.Status);
        Assert.Equal(Moderator, report.ReviewedByUserId);
        Assert.NotNull(report.ResolvedAt);
        var story = _h.Stories.Single(existing => existing.Id == PublicStory);
        Assert.Equal(StoryStatus.Ready, story.Status);
        Assert.True(story.IsPublished);
    }

    [Fact]
    public async Task DismissAsync_NotifiesOnlyReporterWithoutPrivateDecisionData()
    {
        var item = _h.AddReportItem();
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await DismissAsync(item.Id, "Lý do riêng tư của Moderator");

        var notification = Assert.Single(sent);
        Assert.Equal(ContentReportHarness.ReporterId, notification.Recipient);
        Assert.Equal(NotificationType.ContentReportResolved, notification.Type);
        using var json = JsonDocument.Parse(notification.Payload!);
        Assert.Equal("ReviewedDismissed", json.RootElement.GetProperty("resolution").GetString());
        Assert.False(json.RootElement.TryGetProperty("moderatorId", out _));
        Assert.DoesNotContain("Lý do riêng tư", notification.Payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task DismissAsync_BlankReason_IsBadRequest(string? reason)
    {
        var item = _h.AddReportItem();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.DismissAsync(Moderator, item.Id, new DismissReportRequestDto { Reason = reason! }));

        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
        _h.UnitOfWork.Verify(unit =>
            unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DismissAsync_ReasonLengthIsValidated()
    {
        var item = _h.AddReportItem();
        await Assert.ThrowsAsync<BadRequestException>(() =>
            DismissAsync(item.Id, new string('a', 1001)));

        await DismissAsync(item.Id, new string('a', 1000));
        Assert.Equal(ModerationItemStatus.Dismissed, item.Status);
    }

    [Fact]
    public async Task DismissAsync_AuditsAndCommitsTransaction()
    {
        var item = _h.AddReportItem();

        await DismissAsync(item.Id);

        _h.Audit.Verify(audit => audit.LogAsync(
            Moderator,
            "ContentReportDismissed",
            nameof(ModerationItem),
            item.Id,
            It.IsAny<object?>(),
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(unit =>
            unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DismissAsync_NotificationFailure_RollsBack()
    {
        var item = _h.AddReportItem();
        _h.Notifications.Setup(notification => notification.CreateAsync(
                It.IsAny<int>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DismissAsync(item.Id));

        _h.UnitOfWork.Verify(unit =>
            unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit =>
            unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DismissAsync_InactiveReporter_IsStillCompletedWithoutNotification()
    {
        var item = _h.AddReportItem();
        _h.Users.Single(user => user.Id == ContentReportHarness.ReporterId).Status = AccountStatus.Suspended;

        await DismissAsync(item.Id);

        Assert.Equal(ModerationItemStatus.Dismissed, item.Status);
        _h.Notifications.Verify(notification => notification.CreateAsync(
            It.IsAny<int>(),
            It.IsAny<NotificationType>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DismissAsync_AdministratorHoldingItem_CanDismiss()
    {
        var item = _h.AddReportItem();
        item.ClaimedByUserId = ContentReportHarness.AdminId;

        await DismissAsync(item.Id, actorId: ContentReportHarness.AdminId);

        Assert.Equal(ContentReportHarness.AdminId, item.DecidedByUserId);
    }

    [Fact]
    public async Task DismissAsync_CalledTwice_SecondCallIsForbidden()
    {
        var item = _h.AddReportItem();
        await DismissAsync(item.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => DismissAsync(item.Id));
    }

    [Fact]
    public async Task DismissAsync_ClaimedBySomeoneElse_IsForbidden()
    {
        var item = _h.AddReportItem();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            DismissAsync(item.Id, actorId: ContentReportHarness.OtherModeratorId));
    }

    [Fact]
    public async Task DismissAsync_ReviewItem_NotifiesItsModeratorReporter()
    {
        var item = _h.AddReportItem(reporterId: ContentReportHarness.OtherModeratorId);
        _h.Reports.Single().IsReviewItem = true;
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await DismissAsync(item.Id);

        Assert.Equal(ContentReportHarness.OtherModeratorId, Assert.Single(sent).Recipient);
    }

    [Fact]
    public async Task ArchiveAsync_AppliesTheArchiveConsequencesOnceWithTheDecisionData()
    {
        var item = _h.AddReportItem(ContentReportReason.SafetyConcern);
        var before = DateTime.UtcNow;

        await ArchiveAsync(item.Id, ArchivedReason.SafetyConcern);

        _consequences.Verify(c => c.ApplyAsync(
            It.Is<StoryArchiveContext>(context =>
                context.Story.Id == PublicStory
                && context.Reason == ArchivedReason.SafetyConcern
                && context.ActorUserId == Moderator
                && context.ReporterUserId == ContentReportHarness.ReporterId
                && context.Now >= before),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveAsync_AppliesConsequencesInsideTheTransactionAfterTheStoryIsArchived()
    {
        var item = _h.AddReportItem(ContentReportReason.Copyright);
        StoryStatus? storyStatusSeen = null;
        var inTransaction = false;
        _h.UnitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => inTransaction = true).Returns(Task.CompletedTask);
        _consequences.Setup(c => c.ApplyAsync(It.IsAny<StoryArchiveContext>(), It.IsAny<CancellationToken>()))
            .Callback<StoryArchiveContext, CancellationToken>((context, _) =>
            {
                storyStatusSeen = context.Story.Status;
                Assert.True(inTransaction);
            })
            .Returns(Task.CompletedTask);

        await ArchiveAsync(item.Id, ArchivedReason.Copyright);

        Assert.Equal(StoryStatus.Rejected, storyStatusSeen);
    }

    [Fact]
    public async Task ArchiveAsync_StoryAlreadyArchived_DoesNotApplyTheConsequencesAgain()
    {
        var item = _h.AddReportItem(ContentReportReason.SafetyConcern);
        var story = _h.Stories.Single(existing => existing.Id == PublicStory);
        story.Status = StoryStatus.Rejected;
        story.ArchivedAt = DateTime.UtcNow.AddDays(-1);

        await ArchiveAsync(item.Id, ArchivedReason.SafetyConcern);

        _consequences.Verify(c => c.ApplyAsync(
            It.IsAny<StoryArchiveContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchiveAsync_RejectedWithoutRemovalMetadata_StillAppliesConsequences()
    {
        var item = _h.AddReportItem(ContentReportReason.SafetyConcern);
        var story = _h.Stories.Single(existing => existing.Id == PublicStory);
        story.Status = StoryStatus.Rejected;

        await ArchiveAsync(item.Id, ArchivedReason.SafetyConcern);

        Assert.NotNull(story.ArchivedAt);
        _consequences.Verify(c => c.ApplyAsync(
            It.IsAny<StoryArchiveContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DismissAsync_DoesNotApplyTheArchiveConsequences()
    {
        var item = _h.AddReportItem(ContentReportReason.Cultural);

        await DismissAsync(item.Id);

        _consequences.Verify(c => c.ApplyAsync(
            It.IsAny<StoryArchiveContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchiveAsync_ConsequencesFail_RollsTheWholeDecisionBack()
    {
        var item = _h.AddReportItem(ContentReportReason.SafetyConcern);
        _consequences.Setup(c => c.ApplyAsync(It.IsAny<StoryArchiveContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ArchiveAsync(item.Id, ArchivedReason.SafetyConcern));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _h.Notifications.Verify(n => n.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchiveAsync_ArchivesStoryAndRecordsDecision()
    {
        var item = _h.AddReportItem(ContentReportReason.SafetyConcern);

        var result = await ArchiveAsync(item.Id, ArchivedReason.SafetyConcern);

        Assert.Equal("Archived", result.Decision);
        Assert.True(result.StoryArchived);
        Assert.Equal(ModerationItemStatus.Archived, item.Status);
        Assert.Equal(1, item.RowVersion);
        Assert.Equal(ContentReportStatus.ReviewedArchived, _h.Reports.Single().Status);
        var story = _h.Stories.Single(existing => existing.Id == PublicStory);
        Assert.Equal(StoryStatus.Rejected, story.Status);
        Assert.Equal(ArchivedReason.SafetyConcern, story.ArchivedReason);
        Assert.NotNull(story.ArchivedAt);
        Assert.False(story.IsDeleted);
        Assert.True(story.IsPublished);
    }

    [Theory]
    [InlineData(ArchivedReason.SafetyConcern)]
    [InlineData(ArchivedReason.Copyright)]
    [InlineData(ArchivedReason.Inappropriate)]
    public async Task ArchiveAsync_AcceptsModeratorArchiveReasons(ArchivedReason archiveReason)
    {
        var item = _h.AddReportItem();

        await ArchiveAsync(item.Id, archiveReason);

        Assert.Equal(archiveReason, _h.Stories.Single(existing => existing.Id == PublicStory).ArchivedReason);
    }

    [Theory]
    [InlineData(ArchivedReason.SupervisorPreference)]
    [InlineData((ArchivedReason)99)]
    public async Task ArchiveAsync_RejectsUnsupportedArchiveReason(ArchivedReason archiveReason)
    {
        var item = _h.AddReportItem();

        await Assert.ThrowsAsync<BadRequestException>(() => ArchiveAsync(item.Id, archiveReason));

        Assert.Equal(StoryStatus.Ready, _h.Stories.Single(existing => existing.Id == PublicStory).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ArchiveAsync_BlankDecisionReason_IsBadRequest(string reason)
    {
        var item = _h.AddReportItem();

        await Assert.ThrowsAsync<BadRequestException>(() => ArchiveAsync(item.Id, reason: reason));

        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
        Assert.Equal(StoryStatus.Ready, _h.Stories.Single(existing => existing.Id == PublicStory).Status);
        _h.UnitOfWork.Verify(unit =>
            unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchiveAsync_NotifiesReporterAndPublicStoryCreator()
    {
        var item = _h.AddReportItem();
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await ArchiveAsync(item.Id, ArchivedReason.Copyright, "Chi tiết nội bộ");

        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, notification =>
            notification.Recipient == ContentReportHarness.ReporterId
            && notification.Type == NotificationType.ContentReportResolved);
        var creator = Assert.Single(sent,
            notification => notification.Recipient == ContentReportHarness.CreatorId);
        Assert.Equal(NotificationType.CreatorContentFlagged, creator.Type);
        using var json = JsonDocument.Parse(creator.Payload!);
        Assert.Equal("Copyright", json.RootElement.GetProperty("reason").GetString());
        Assert.DoesNotContain("Chi tiết nội bộ", creator.Payload);
    }

    [Fact]
    public async Task ArchiveAsync_PrivateStory_NotifiesAuthorWithArchivedByModeration()
    {
        var item = _h.AddReportItem(
            storyId: ContentReportHarness.PrivateStoryId,
            versionId: 60,
            reporterId: ContentReportHarness.OtherParentId);
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await ArchiveAsync(item.Id, ArchivedReason.Inappropriate);

        Assert.Contains(sent, notification =>
            notification.Recipient == ContentReportHarness.ReporterId
            && notification.Type == NotificationType.StoryArchivedByModeration);
        Assert.Contains(sent, notification =>
            notification.Recipient == ContentReportHarness.OtherParentId
            && notification.Type == NotificationType.ContentReportResolved);
    }

    [Fact]
    public async Task ArchiveAsync_WhenReporterIsAlsoAuthor_SendsOneResolutionNotification()
    {
        var item = _h.AddReportItem(
            storyId: ContentReportHarness.PrivateStoryId,
            versionId: 60,
            reporterId: ContentReportHarness.ReporterId);
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await ArchiveAsync(item.Id, ArchivedReason.Inappropriate);

        var notification = Assert.Single(sent);
        Assert.Equal(ContentReportHarness.ReporterId, notification.Recipient);
        Assert.Equal(NotificationType.ContentReportResolved, notification.Type);
    }

    [Fact]
    public async Task ArchiveAsync_AlreadyArchivedStory_OnlyClosesReportAndNotifiesReporter()
    {
        var item = _h.AddReportItem();
        var story = _h.Stories.Single(existing => existing.Id == PublicStory);
        var archivedAt = DateTime.UtcNow.AddDays(-2);
        story.Status = StoryStatus.Rejected;
        story.ArchivedReason = ArchivedReason.SupervisorPreference;
        story.ArchivedAt = archivedAt;
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await ArchiveAsync(item.Id);

        Assert.Equal(ArchivedReason.SupervisorPreference, story.ArchivedReason);
        Assert.Equal(archivedAt, story.ArchivedAt);
        Assert.Equal(ContentReportHarness.ReporterId, Assert.Single(sent).Recipient);
        Assert.Equal(ContentReportStatus.ReviewedArchived, _h.Reports.Single().Status);
    }

    [Fact]
    public async Task ArchiveAsync_InactiveCreator_StillArchivesAndNotifiesReporter()
    {
        var item = _h.AddReportItem();
        _h.Users.Single(user => user.Id == ContentReportHarness.CreatorId).Status = AccountStatus.Suspended;
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        await ArchiveAsync(item.Id);

        Assert.Equal(StoryStatus.Rejected, _h.Stories.Single(existing => existing.Id == PublicStory).Status);
        Assert.Equal(ContentReportHarness.ReporterId, Assert.Single(sent).Recipient);
    }

    [Fact]
    public async Task ArchiveAsync_AuditsAndCommitsTransaction()
    {
        var item = _h.AddReportItem();

        await ArchiveAsync(item.Id);

        _h.Audit.Verify(audit => audit.LogAsync(
            Moderator,
            "ContentReportArchived",
            nameof(ModerationItem),
            item.Id,
            It.IsAny<object?>(),
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(unit =>
            unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveAsync_ClaimedBySomeoneElse_IsForbiddenAndStoryIsUntouched()
    {
        var item = _h.AddReportItem();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ArchiveAsync(item.Id, actorId: ContentReportHarness.OtherModeratorId));

        Assert.Equal(StoryStatus.Ready, _h.Stories.Single(existing => existing.Id == PublicStory).Status);
    }

    [Fact]
    public async Task ArchiveAsync_CalledTwice_SecondCallIsForbidden()
    {
        var item = _h.AddReportItem();
        await ArchiveAsync(item.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => ArchiveAsync(item.Id));
    }

    [Fact]
    public async Task ArchiveAsync_NotificationFailure_RollsBack()
    {
        var item = _h.AddReportItem();
        _h.Notifications.Setup(notification => notification.CreateAsync(
                It.IsAny<int>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ArchiveAsync(item.Id));

        _h.UnitOfWork.Verify(unit =>
            unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit =>
            unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EscalateAsync_MovesCaseAndNotifiesOnlyActiveAdministrators()
    {
        var item = _h.AddReportItem(ContentReportReason.Copyright);
        _h.AddUser(2, "admin_demo2", UserRole.Administrator);
        var sent = new List<(int Recipient, NotificationType Type, string? Payload)>();
        CaptureNotifications(sent);

        var result = await EscalateAsync(item.Id, "Report nhắm vào nội dung tôi từng duyệt");

        Assert.Equal("Escalated", result.Decision);
        Assert.Equal(ModerationItemStatus.Escalated, item.Status);
        Assert.Equal("Report nhắm vào nội dung tôi từng duyệt", item.EscalationReason);
        Assert.Null(item.DecidedByUserId);
        Assert.Null(item.DecidedAt);
        Assert.Equal(ContentReportStatus.Escalated, _h.Reports.Single().Status);
        Assert.Null(_h.Reports.Single().ResolvedAt);
        Assert.Equal(new[] { ContentReportHarness.AdminId, 2 },
            sent.Select(notification => notification.Recipient).OrderBy(id => id));
        Assert.All(sent, notification => Assert.Equal(NotificationType.ContentReportEscalated, notification.Type));
        Assert.DoesNotContain("Report nhắm", sent[0].Payload);
        Assert.DoesNotContain(sent, notification => notification.Recipient == ContentReportHarness.ReporterId);
    }

    [Fact]
    public async Task EscalateAsync_NoActiveAdministrator_IsConflictWithoutTransaction()
    {
        var item = _h.AddReportItem();
        _h.Users.Single(user => user.Id == ContentReportHarness.AdminId).Status = AccountStatus.Suspended;

        await Assert.ThrowsAsync<ConflictException>(() => EscalateAsync(item.Id));

        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
        Assert.Equal(ContentReportStatus.Pending, _h.Reports.Single().Status);
        _h.UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EscalateAsync_RequiresModeratorAndBoundedReason()
    {
        var item = _h.AddReportItem();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            EscalateAsync(item.Id, actorId: ContentReportHarness.AdminId));
        await Assert.ThrowsAsync<BadRequestException>(() => EscalateAsync(item.Id, " "));
        await Assert.ThrowsAsync<BadRequestException>(() => EscalateAsync(item.Id, new string('a', 1001)));
    }

    [Fact]
    public async Task EscalateAsync_NotificationFailure_RollsBack()
    {
        var item = _h.AddReportItem();
        _h.Notifications.Setup(notification => notification.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => EscalateAsync(item.Id));

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Administrator_CanReadAndDismissEscalatedCaseWithoutClaimTimeout()
    {
        var item = EscalatedItem();

        var detail = await GetDetailAsync(item.Id, ContentReportHarness.AdminId);
        await DismissAsync(item.Id, "Đã đối chiếu, không vi phạm", ContentReportHarness.AdminId);

        Assert.Equal("Escalated", detail.ItemStatus);
        Assert.Equal("Nghi Content Creator cố ý đưa nội dung xấu", detail.EscalationReason);
        Assert.Equal(ModerationItemStatus.Dismissed, item.Status);
        Assert.Equal(ContentReportHarness.AdminId, item.DecidedByUserId);
        Assert.Equal(ContentReportStatus.ReviewedDismissed, _h.Reports.Single().Status);
    }

    [Fact]
    public async Task Moderator_CannotReadOrDecideEscalatedCase()
    {
        var item = EscalatedItem();

        await Assert.ThrowsAsync<ForbiddenException>(() => GetDetailAsync(item.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => DismissAsync(item.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => ArchiveAsync(item.Id));
    }

    [Fact]
    public async Task Administrator_CannotReadOrdinaryModeratorClaim()
    {
        var item = _h.AddReportItem();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            GetDetailAsync(item.Id, ContentReportHarness.AdminId));
    }

    [Fact]
    public void Controller_IsRestrictedToStaffAndExposesFourEndpoints()
    {
        var authorize = typeof(ContentReportReviewController).GetCustomAttribute<AuthorizeAttribute>();
        var methods = typeof(ContentReportReviewController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("Moderator,Administrator", authorize!.Roles);
        Assert.Equal(["Archive", "Dismiss", "Escalate", "GetDetail"], methods);
    }
}
