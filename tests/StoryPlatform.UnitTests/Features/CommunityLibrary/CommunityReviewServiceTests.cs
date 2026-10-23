using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.CommunityLibrary;
using StoryPlatform.Application.Features.CommunityLibrary.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ContentReports;
using Xunit;

namespace StoryPlatform.UnitTests.Features.CommunityLibrary;

public class CommunityReviewServiceTests
{
    private readonly ContentReportHarness _h = new();
    private readonly CommunityReviewService _sut;

    private const int Story = ContentReportHarness.PrivateStoryId;
    private const int Submitter = ContentReportHarness.ReporterId;
    private const int Reviewer = ContentReportHarness.OtherModeratorId;
    private const int Moderator = ContentReportHarness.ModeratorId;
    private const int Admin = ContentReportHarness.AdminId;

    public CommunityReviewServiceTests()
    {
        _sut = new CommunityReviewService(_h.UnitOfWork.Object, _h.Audit.Object, _h.Notifications.Object);
        _h.ContentCategories.Add(new ContentCategory { Id = 5, Code = "friendship", DisplayName = "Tình bạn", IsActive = true });
        _h.ContentCategories.Add(new ContentCategory { Id = 6, Code = "animals", DisplayName = "Động vật", IsActive = true });
        _h.ContentCategories.Add(new ContentCategory { Id = 7, Code = "retired", DisplayName = "Đã ngừng", IsActive = false });
    }

    /// <summary>Mục Community của truyện riêng 20, ghim phiên bản 60, do <paramref name="claimedBy"/> giữ.</summary>
    private ModerationItem Claimed(int claimedBy = Reviewer, int createdBy = Submitter)
    {
        var item = new ModerationItem
        {
            Id = 80,
            ItemType = ModerationItemType.CommunitySubmission,
            Status = ModerationItemStatus.Claimed,
            StoryId = Story,
            StoryVersionId = 60,
            CreatedByUserId = createdBy,
            SourceChildProfileId = ContentReportHarness.ChildId,
            ClaimedByUserId = claimedBy,
            ClaimedAt = DateTime.UtcNow.AddMinutes(-5),
            CopyrightDeclaration = CopyrightDeclaration.AiGenerated,
            PiiCheckResult = "{\"findings\":[]}"
        };
        _h.Items.Add(item);
        _h.StoryCategories.Add(new StoryCategory { Id = 1, StoryId = Story, ContentCategoryId = 5 });
        return item;
    }

    private static SaveCommunityChecklistRequestDto Checklist(bool passed = true) => new()
    {
        Entries = new[] { "safety_baseline", "no_personal_info", "metadata_labels" }
            .Select(code => new CommunityChecklistEntryDto { Code = code, Passed = passed }).ToList()
    };

    private StoryVersion Version => _h.Versions.Single(version => version.Id == 60);

    // ---- gói duyệt ----

    [Fact]
    public async Task GetPackageAsync_ShowsThePinnedVersionNotTheCurrentOne()
    {
        Claimed();
        Version.Title = "Bản đã ghim";
        Version.Content = "Nội dung ghim";
        Version.IsCurrent = false;
        _h.Versions.Add(new StoryVersion { Id = 61, StoryId = Story, VersionNo = 2, Title = "Bản mới", Content = "Khác", IsCurrent = true });

        var package = await _sut.GetPackageAsync(Reviewer, 80);

        Assert.Equal(80, package.ItemId);
        Assert.Equal(60, package.Version.VersionId);
        Assert.Equal("Bản đã ghim", package.Version.Title);
        Assert.Equal("Nội dung ghim", package.Version.Content);
    }

    [Fact]
    public async Task GetPackageAsync_MasksPersonalDataForTheModerator()
    {
        Claimed();
        Version.Content = "Gọi 0912345678 hoặc mail an@example.com, bé Bé 100 rất vui";

        var package = await _sut.GetPackageAsync(Reviewer, 80);

        Assert.DoesNotContain("0912345678", package.Version.Content);
        Assert.DoesNotContain("an@example.com", package.Version.Content);
        Assert.Contains("[số điện thoại]", package.Version.Content);
        Assert.Contains("[email]", package.Version.Content);
        Assert.Contains("[tên bé]", package.Version.Content);
    }

    [Fact]
    public async Task GetPackageAsync_ShowsLabelsCopyrightAndTheStoredPiiSummary()
    {
        var item = Claimed();
        item.IsResubmission = true;

        var package = await _sut.GetPackageAsync(Reviewer, 80);

        Assert.Equal(new[] { "friendship" }, package.Labels.Select(label => label.Code).ToArray());
        Assert.Equal(5, package.Labels.Single().Id);
        Assert.Equal("AiGenerated", package.CopyrightDeclaration);
        Assert.Contains("findings", package.PiiCheckSummary);
        Assert.True(package.IsResubmission);
    }

    [Fact]
    public async Task GetPackageAsync_RescansThePinnedVersionAndListsOnlyKindAndLocation()
    {
        Claimed();
        Version.Content = "Lớp 2A học vui";

        var package = await _sut.GetPackageAsync(Reviewer, 80);

        var finding = Assert.Single(package.PiiRescan);
        Assert.Equal("class", finding.Kind);
        Assert.Equal("content", finding.Location);
    }

    [Fact]
    public async Task GetPackageAsync_ChecklistStartsUnansweredWithThreeCriteria()
    {
        Claimed();

        var package = await _sut.GetPackageAsync(Reviewer, 80);

        Assert.Equal(
            new[] { "safety_baseline", "no_personal_info", "metadata_labels" },
            package.Checklist.Select(entry => entry.Code).ToArray());
        Assert.All(package.Checklist, entry => Assert.False(entry.Passed));
        Assert.All(package.Checklist, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Label)));
    }

    [Fact]
    public async Task GetPackageAsync_ClaimExpiryIsTwoHoursAfterTheClaim_AndAuditIsWritten()
    {
        var item = Claimed();

        var package = await _sut.GetPackageAsync(Reviewer, 80);

        Assert.Equal(item.ClaimedAt!.Value.Add(ModerationQueuePolicy.ClaimTimeout), package.ClaimExpiresAt);
        _h.Audit.Verify(a => a.LogAsync(
            Reviewer, "CommunityReviewPackageOpened", nameof(ModerationItem), 80,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPackageAsync_ChecklistNotDone_BlocksApprove()
    {
        Claimed();

        var package = await _sut.GetPackageAsync(Reviewer, 80);

        Assert.False(package.CanApprove);
        Assert.Contains(package.Blockers, blocker => blocker.Code == "checklist_incomplete");
    }

    [Fact]
    public async Task GetPackageAsync_AfterAFullyPassedChecklist_CanApprove()
    {
        Claimed();
        await _sut.SaveChecklistAsync(Reviewer, 80, Checklist());

        var package = await _sut.GetPackageAsync(Reviewer, 80);

        Assert.True(package.CanApprove);
        Assert.Empty(package.Blockers);
    }

    [Fact]
    public async Task GetPackageAsync_PiiStillThere_BlocksApprove()
    {
        Claimed();
        Version.Content = "Gọi 0912345678";
        await _sut.SaveChecklistAsync(Reviewer, 80, Checklist());

        var package = await _sut.GetPackageAsync(Reviewer, 80);

        Assert.Contains(package.Blockers, blocker => blocker.Code == "pii_found");
        Assert.False(package.CanApprove);
    }

    [Fact]
    public async Task GetPackageAsync_ArchivedStoryOrNoLabelsOrOwnSubmission_BlockApprove()
    {
        Claimed(claimedBy: Moderator, createdBy: Moderator);
        _h.Stories.Single(story => story.Id == Story).Status = StoryStatus.Archived;
        _h.StoryCategories.Clear();
        await _sut.SaveChecklistAsync(Moderator, 80, Checklist());

        var package = await _sut.GetPackageAsync(Moderator, 80);

        Assert.Equal(
            new[] { "no_labels", "own_submission", "story_archived" },
            package.Blockers.Select(blocker => blocker.Code).OrderBy(code => code).ToArray());
    }

    [Fact]
    public async Task GetPackageAsync_AdministratorHoldingTheItem_CanOpenIt()
    {
        Claimed(claimedBy: Admin);

        Assert.Equal(80, (await _sut.GetPackageAsync(Admin, 80)).ItemId);
    }

    [Fact]
    public async Task GetPackageAsync_SomeoneWhoDoesNotHoldTheItem_IsForbidden()
    {
        Claimed(claimedBy: Reviewer);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetPackageAsync(Moderator, 80));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetPackageAsync(Submitter, 80));
    }

    [Fact]
    public async Task GetPackageAsync_ItemNotClaimed_OrClaimOlderThanTwoHours_IsForbidden()
    {
        var item = Claimed();
        item.ClaimedAt = DateTime.UtcNow.AddHours(-3);
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetPackageAsync(Reviewer, 80));

        item.ClaimedAt = DateTime.UtcNow.AddMinutes(-5);
        item.Status = ModerationItemStatus.Pending;
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetPackageAsync(Reviewer, 80));
    }

    [Fact]
    public async Task GetPackageAsync_OtherItemTypeOrUnknownItem_IsRejected()
    {
        var item = Claimed();
        item.ItemType = ModerationItemType.PublicStoryReview;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GetPackageAsync(Reviewer, 80));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetPackageAsync(Reviewer, 999));
    }

    [Fact]
    public async Task GetPackageAsync_PinnedVersionMissing_IsNotFound()
    {
        var item = Claimed();
        item.StoryVersionId = 999;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetPackageAsync(Reviewer, 80));
    }

    [Fact]
    public async Task GetPackageAsync_SuspendedReviewer_IsForbidden()
    {
        Claimed();
        _h.Users.Single(user => user.Id == Reviewer).Status = AccountStatus.Suspended;

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetPackageAsync(Reviewer, 80));
    }

    // ---- checklist ----

    [Fact]
    public async Task SaveChecklistAsync_StoresAnswersBumpsRowVersionAndWritesAudit()
    {
        var item = Claimed();
        var request = Checklist();
        request.Entries[1].Passed = false;
        request.Entries[1].Note = "  Còn tên trường  ";

        var result = await _sut.SaveChecklistAsync(Reviewer, 80, request);

        Assert.True(result.Single(entry => entry.Code == "safety_baseline").Passed);
        var piiEntry = result.Single(entry => entry.Code == "no_personal_info");
        Assert.False(piiEntry.Passed);
        Assert.Equal("Còn tên trường", piiEntry.Note);
        Assert.False(string.IsNullOrWhiteSpace(item.ReviewProgress));
        Assert.Equal(1, item.RowVersion);
        _h.Audit.Verify(a => a.LogAsync(
            Reviewer, "CommunityReviewChecklistSaved", nameof(ModerationItem), 80,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveChecklistAsync_PartialEntriesKeepEarlierAnswers()
    {
        Claimed();
        await _sut.SaveChecklistAsync(Reviewer, 80, Checklist());

        var result = await _sut.SaveChecklistAsync(Reviewer, 80, new SaveCommunityChecklistRequestDto
        {
            Entries = new List<CommunityChecklistEntryDto> { new() { Code = "metadata_labels", Passed = false } }
        });

        Assert.False(result.Single(entry => entry.Code == "metadata_labels").Passed);
        Assert.True(result.Single(entry => entry.Code == "safety_baseline").Passed);
    }

    [Fact]
    public async Task SaveChecklistAsync_InvalidInput_IsBadRequestAndStoresNothing()
    {
        var item = Claimed();
        var unknown = new SaveCommunityChecklistRequestDto
        {
            Entries = new List<CommunityChecklistEntryDto> { new() { Code = "media", Passed = true } }
        };
        var duplicate = new SaveCommunityChecklistRequestDto
        {
            Entries = new List<CommunityChecklistEntryDto>
            {
                new() { Code = "safety_baseline", Passed = true }, new() { Code = "safety_baseline", Passed = false }
            }
        };
        var longNote = Checklist();
        longNote.Entries[0].Note = new string('a', 501);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveChecklistAsync(Reviewer, 80, unknown));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveChecklistAsync(Reviewer, 80, duplicate));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveChecklistAsync(Reviewer, 80, longNote));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SaveChecklistAsync(Reviewer, 80, new SaveCommunityChecklistRequestDto()));

        Assert.Null(item.ReviewProgress);
    }

    [Fact]
    public async Task SaveChecklistAsync_SomeoneWhoDoesNotHoldTheItem_IsForbidden()
    {
        Claimed(claimedBy: Reviewer);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SaveChecklistAsync(Moderator, 80, Checklist()));
    }

    // ---- nhãn ----

    [Fact]
    public async Task SetLabelsAsync_ReplacesTheLabelsWithModeratorTaggedOnes()
    {
        Claimed();

        var labels = await _sut.SetLabelsAsync(Reviewer, 80, new SetCommunityLabelsRequestDto
        {
            ContentCategoryIds = new List<int> { 6 }
        });

        Assert.Equal(new[] { "animals" }, labels.Select(label => label.Code).ToArray());
        var links = _h.StoryCategories.Where(link => link.StoryId == Story).ToList();
        Assert.Single(links);
        Assert.Equal(6, links[0].ContentCategoryId);
        Assert.Equal(TagSource.Moderator, links[0].Source);
        Assert.Equal(1, _h.Items.Single().RowVersion);
        _h.Audit.Verify(a => a.LogAsync(
            Reviewer, "CommunityReviewLabelsChanged", nameof(ModerationItem), 80,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLabelsAsync_KeepsExistingLinksThatAreStillWanted()
    {
        Claimed();

        var labels = await _sut.SetLabelsAsync(Reviewer, 80, new SetCommunityLabelsRequestDto
        {
            ContentCategoryIds = new List<int> { 5, 6, 6 }
        });

        Assert.Equal(new[] { 5, 6 }, labels.Select(label => label.Id).OrderBy(id => id).ToArray());
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 999 })]
    [InlineData(new[] { 7 })]
    public async Task SetLabelsAsync_EmptyUnknownOrInactive_IsBadRequestAndKeepsLabels(int[] ids)
    {
        Claimed();

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetLabelsAsync(Reviewer, 80,
            new SetCommunityLabelsRequestDto { ContentCategoryIds = ids.ToList() }));

        Assert.Equal(new[] { 5 }, _h.StoryCategories.Where(link => link.StoryId == Story)
            .Select(link => link.ContentCategoryId).ToArray());
    }

    [Fact]
    public async Task SetLabelsAsync_SomeoneWhoDoesNotHoldTheItem_IsForbidden()
    {
        Claimed(claimedBy: Reviewer);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SetLabelsAsync(Moderator, 80,
            new SetCommunityLabelsRequestDto { ContentCategoryIds = new List<int> { 6 } }));
    }

    // ---- Approve ----

    private async Task ReadyToApproveAsync()
    {
        Claimed();
        await _sut.SaveChecklistAsync(Reviewer, 80, Checklist());
    }

    [Fact]
    public async Task ApproveAsync_ApprovesTheItemAndKeepsTheSourceStoryUntouched()
    {
        await ReadyToApproveAsync();
        var item = _h.Items.Single();
        var storyBefore = _h.Stories.Single(story => story.Id == Story).Status;
        var before = DateTime.UtcNow;

        var result = await _sut.ApproveAsync(Reviewer, 80);

        Assert.Equal(ModerationItemStatus.Approved, item.Status);
        Assert.Equal(Reviewer, item.DecidedByUserId);
        Assert.True(item.DecidedAt >= before);
        Assert.Contains("\"decision\":\"approved\"", item.Feedback);
        Assert.Contains("safety_baseline", item.Feedback);
        Assert.Equal(2, item.RowVersion);
        Assert.Equal("Approved", result.Decision);
        Assert.Equal(Story, result.StoryId);
        Assert.Equal(storyBefore, _h.Stories.Single(story => story.Id == Story).Status);
    }

    [Fact]
    public async Task ApproveAsync_NotifiesTheSubmitterWithoutNamingTheModerator()
    {
        await ReadyToApproveAsync();

        await _sut.ApproveAsync(Reviewer, 80);

        _h.Notifications.Verify(n => n.CreateAsync(
            Submitter, NotificationType.CommunityLibraryReviewed,
            It.Is<string?>(payload => payload!.Contains("\"decision\":\"approved\"")
                                      && payload.Contains("\"itemId\":80")
                                      && payload.Contains($"\"storyId\":{Story}")
                                      && !payload.Contains("moderator", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_SubmitterNoLongerActive_StillApprovesWithoutANotification()
    {
        await ReadyToApproveAsync();
        _h.Users.Single(user => user.Id == Submitter).Status = AccountStatus.Suspended;

        await _sut.ApproveAsync(Reviewer, 80);

        Assert.Equal(ModerationItemStatus.Approved, _h.Items.Single().Status);
        _h.Notifications.Verify(n => n.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_WritesAuditAndCommitsOneTransaction()
    {
        await ReadyToApproveAsync();

        await _sut.ApproveAsync(Reviewer, 80);

        _h.Audit.Verify(a => a.LogAsync(
            Reviewer, "CommunitySubmissionApproved", nameof(ModerationItem), 80,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_AuditFails_RollsBack()
    {
        await ReadyToApproveAsync();
        _h.Audit.Setup(a => a.LogAsync(
                It.IsAny<int?>(), "CommunitySubmissionApproved", It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ApproveAsync(Reviewer, 80));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_ChecklistIncomplete_IsBadRequestAndChangesNothing()
    {
        var item = Claimed();
        var partial = Checklist();
        partial.Entries[2].Passed = false;
        await _sut.SaveChecklistAsync(Reviewer, 80, partial);

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => _sut.ApproveAsync(Reviewer, 80));

        Assert.Contains("Metadata", exception.Message);
        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
        _h.UnitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_PiiTheDetectorStillFinds_IsBlocked()
    {
        await ReadyToApproveAsync();
        Version.Content = "Lớp 2A trường Tiểu học Hoa Mai";

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => _sut.ApproveAsync(Reviewer, 80));

        Assert.Contains("thông tin", exception.Message);
        Assert.Equal(ModerationItemStatus.Claimed, _h.Items.Single().Status);
    }

    [Fact]
    public async Task ApproveAsync_StoryArchivedMeanwhile_IsBlocked()
    {
        await ReadyToApproveAsync();
        _h.Stories.Single(story => story.Id == Story).Status = StoryStatus.Archived;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ApproveAsync(Reviewer, 80));
    }

    [Fact]
    public async Task ApproveAsync_NoLabels_IsBlockedUntilTheModeratorLabelsIt()
    {
        await ReadyToApproveAsync();
        _h.StoryCategories.Clear();

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ApproveAsync(Reviewer, 80));

        await _sut.SetLabelsAsync(Reviewer, 80, new SetCommunityLabelsRequestDto { ContentCategoryIds = new List<int> { 6 } });
        await _sut.ApproveAsync(Reviewer, 80);

        Assert.Equal(ModerationItemStatus.Approved, _h.Items.Single().Status);
    }

    [Fact]
    public async Task ApproveAsync_TheSubmitterThemselvesAsReviewer_IsBlocked()
    {
        Claimed(claimedBy: Moderator, createdBy: Moderator);
        await _sut.SaveChecklistAsync(Moderator, 80, Checklist());

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => _sut.ApproveAsync(Moderator, 80));

        Assert.Contains("chính mình", exception.Message);
    }

    [Fact]
    public async Task ApproveAsync_SomeoneWhoDoesNotHoldTheItem_IsForbidden()
    {
        await ReadyToApproveAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ApproveAsync(Moderator, 80));
    }

    [Fact]
    public async Task ApproveAsync_AdministratorHoldingTheItem_CanApprove()
    {
        Claimed(claimedBy: Admin);
        await _sut.SaveChecklistAsync(Admin, 80, Checklist());

        await _sut.ApproveAsync(Admin, 80);

        Assert.Equal(Admin, _h.Items.Single().DecidedByUserId);
    }

    // ---- Reject ----

    [Fact]
    public async Task RejectAsync_RejectsWithTheReasonAndKeepsTheChecklistInTheFeedback()
    {
        var item = Claimed();
        await _sut.SaveChecklistAsync(Reviewer, 80, Checklist());

        var result = await _sut.RejectAsync(Reviewer, 80, new RejectCommunityRequestDto { Reason = "  Còn tên trường  " });

        Assert.Equal(ModerationItemStatus.Rejected, item.Status);
        Assert.Equal("Còn tên trường", item.DecisionReason);
        Assert.Equal(Reviewer, item.DecidedByUserId);
        Assert.NotNull(item.DecidedAt);
        Assert.Contains("\"decision\":\"rejected\"", item.Feedback);
        Assert.Equal("Rejected", result.Decision);
    }

    [Fact]
    public async Task RejectAsync_NeedsNoChecklistAndWorksEvenWhenApproveIsBlocked_Ac1()
    {
        Claimed();
        Version.Content = "Bé học Trường Tiểu học Hoa Mai";

        await _sut.RejectAsync(Reviewer, 80, new RejectCommunityRequestDto { Reason = "Có tên trường" });

        Assert.Equal(ModerationItemStatus.Rejected, _h.Items.Single().Status);
    }

    [Fact]
    public async Task RejectAsync_NotifiesTheSubmitterWithTheReason()
    {
        Claimed();

        await _sut.RejectAsync(Reviewer, 80, new RejectCommunityRequestDto { Reason = "Có tên trường" });

        _h.Notifications.Verify(n => n.CreateAsync(
            Submitter, NotificationType.CommunityLibraryReviewed,
            It.Is<string?>(payload => payload!.Contains("\"decision\":\"rejected\"")
                                      && System.Text.Json.JsonDocument.Parse(payload).RootElement
                                          .GetProperty("reason").GetString() == "Có tên trường"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectAsync_ReasonMissing_IsBadRequest(string reason)
    {
        var item = Claimed();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RejectAsync(Reviewer, 80, new RejectCommunityRequestDto { Reason = reason }));

        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
    }

    [Fact]
    public async Task RejectAsync_ReasonLimits()
    {
        Claimed();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RejectAsync(Reviewer, 80, new RejectCommunityRequestDto { Reason = new string('a', 1001) }));
        await _sut.RejectAsync(Reviewer, 80, new RejectCommunityRequestDto { Reason = new string('a', 1000) });
    }

    [Fact]
    public async Task RejectAsync_WritesAuditAndCommits()
    {
        Claimed();
        await _sut.RejectAsync(Reviewer, 80, new RejectCommunityRequestDto { Reason = "Lý do" });

        _h.Audit.Verify(a => a.LogAsync(
            Reviewer, "CommunitySubmissionRejected", nameof(ModerationItem), 80,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectAsync_AuditFails_RollsBack()
    {
        Claimed();
        _h.Audit.Setup(a => a.LogAsync(
                It.IsAny<int?>(), "CommunitySubmissionRejected", It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.RejectAsync(Reviewer, 80, new RejectCommunityRequestDto { Reason = "Lý do" }));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task RejectAsync_SomeoneWhoDoesNotHoldTheItem_IsForbidden()
    {
        Claimed(claimedBy: Reviewer);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.RejectAsync(Moderator, 80, new RejectCommunityRequestDto { Reason = "Lý do" }));
    }

    [Fact]
    public async Task AfterAReject_TheSubmitterCanSubmitAgain()
    {
        Claimed();
        await _sut.RejectAsync(Reviewer, 80, new RejectCommunityRequestDto { Reason = "Có tên trường" });
        var submissions = new CommunitySubmissionService(
            _h.UnitOfWork.Object,
            new StoryPlatform.Application.Features.ChildProfiles.Supervision.Services.SupervisionAccessGuard(_h.UnitOfWork.Object),
            new StoryPlatform.Application.Features.Moderation.Services.ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object),
            _h.Audit.Object);

        var result = await submissions.SubmitAsync(Submitter, new CommunitySubmitRequestDto
        {
            StoryId = Story,
            ConfirmNoPersonalInfo = true
        });

        Assert.NotEqual(80, result.ItemId);
        Assert.True(_h.Items.Single(item => item.Id == result.ItemId).IsResubmission);
        Assert.Equal(80, _h.Items.Single(item => item.Id == result.ItemId).PreviousItemId);
    }
}
