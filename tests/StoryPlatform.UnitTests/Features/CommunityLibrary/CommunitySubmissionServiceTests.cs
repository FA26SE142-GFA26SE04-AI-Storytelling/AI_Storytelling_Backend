using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;
using StoryPlatform.Application.Features.CommunityLibrary;
using StoryPlatform.Application.Features.CommunityLibrary.Services;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ContentReports;
using Xunit;

namespace StoryPlatform.UnitTests.Features.CommunityLibrary;

public class CommunitySubmissionServiceTests
{
    private readonly ContentReportHarness _h = new();
    private readonly CommunitySubmissionService _sut;

    private const int Story = ContentReportHarness.PrivateStoryId;
    private const int Child = ContentReportHarness.ChildId;
    private const int Owner = ContentReportHarness.ReporterId;
    private const int Second = ContentReportHarness.OtherParentId;
    private const int Moderator = ContentReportHarness.ModeratorId;

    public CommunitySubmissionServiceTests()
    {
        _sut = new CommunitySubmissionService(
            _h.UnitOfWork.Object,
            new SupervisionAccessGuard(_h.UnitOfWork.Object),
            new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object),
            _h.Audit.Object);
    }

    private static CommunitySubmitRequestDto Request(
        int storyId = Story, bool confirm = true, CopyrightDeclaration? declaration = null) =>
        new() { StoryId = storyId, ConfirmNoPersonalInfo = confirm, CopyrightDeclaration = declaration };

    private Story PrivateStory => _h.Stories.Single(story => story.Id == Story);
    private StoryVersion CurrentVersion => _h.Versions.Single(version => version.Id == 60);

    private static string[] Codes(CommunityEligibilityDto dto) => dto.Blockers.Select(b => b.Code).OrderBy(c => c).ToArray();

    // ---- Task 2: truyện hợp lệ ----

    [Fact]
    public async Task CheckAsync_ReadyAiStoryOfTheChild_CanBeSubmittedWithNoDeclaration()
    {
        var dto = await _sut.CheckAsync(Owner, Request());

        Assert.True(dto.CanSubmit);
        Assert.Empty(dto.Blockers);
        Assert.Empty(dto.PiiFindings);
        Assert.Equal(60, dto.StoryVersionId);
        Assert.Equal("none", dto.RequiredDeclaration);
        Assert.Equal(Story, dto.StoryId);
    }

    [Fact]
    public async Task CheckAsync_ChangesNothing()
    {
        await _sut.CheckAsync(Owner, Request());

        Assert.Empty(_h.Items);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Task 2: quyền ----

    [Fact]
    public async Task CheckAsync_SupervisorWithApproveStory_IsAllowed()
    {
        _h.AddSupervisor(Child, Second, SupervisorRole.AdditionalSupervisor, Permission.ApproveStory);

        Assert.True((await _sut.CheckAsync(Second, Request())).CanSubmit);
    }

    [Fact]
    public async Task CheckAsync_SupervisorWithoutApproveStory_IsForbidden()
    {
        _h.AddSupervisor(Child, Second, SupervisorRole.AdditionalSupervisor, Permission.ViewProgress);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CheckAsync(Second, Request()));
    }

    [Fact]
    public async Task CheckAsync_NotASupervisorOfTheSourceChild_IsForbidden()
    {
        _h.AddUser(30, "parent_stranger", UserRole.Parent);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CheckAsync(30, Request()));
    }

    [Theory]
    [InlineData(ContentReportHarness.ModeratorId)]
    [InlineData(ContentReportHarness.CreatorId)]
    [InlineData(ContentReportHarness.AdminId)]
    [InlineData(999)]
    public async Task CheckAsync_OnlyActiveParentsMaySubmit(int actorId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CheckAsync(actorId, Request()));
    }

    [Fact]
    public async Task CheckAsync_SuspendedParent_IsForbidden()
    {
        _h.Users.Single(user => user.Id == Owner).Status = AccountStatus.Suspended;

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CheckAsync(Owner, Request()));
    }

    [Fact]
    public async Task CheckAsync_UnknownOrDeletedStory_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CheckAsync(Owner, Request(999)));

        PrivateStory.IsDeleted = true;
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CheckAsync(Owner, Request()));
    }

    // ---- Task 2: điều kiện nộp ----

    [Fact]
    public async Task CheckAsync_PublicLibraryStory_IsNotPrivate()
    {
        var dto = await _sut.CheckAsync(Owner, Request(ContentReportHarness.PublicStoryId));

        Assert.False(dto.CanSubmit);
        Assert.Contains("story_not_private", Codes(dto));
    }

    [Fact]
    public async Task CheckAsync_PrivateStoryWithoutAChild_IsNotPrivate()
    {
        PrivateStory.ChildProfileId = null;

        var dto = await _sut.CheckAsync(Owner, Request());

        Assert.Contains("story_not_private", Codes(dto));
    }

    [Fact]
    public async Task CheckAsync_ArchivedStory_IsBlocked()
    {
        PrivateStory.Status = StoryStatus.Archived;

        Assert.Contains("story_archived", Codes(await _sut.CheckAsync(Owner, Request())));
    }

    [Fact]
    public async Task CheckAsync_ArchivedAtSet_IsBlocked()
    {
        PrivateStory.ArchivedAt = DateTime.UtcNow;

        Assert.Contains("story_archived", Codes(await _sut.CheckAsync(Owner, Request())));
    }

    [Theory]
    [InlineData(StoryStatus.Draft)]
    [InlineData(StoryStatus.MediaProcessing)]
    public async Task CheckAsync_StoryNotReady_IsBlocked(StoryStatus status)
    {
        PrivateStory.Status = status;

        Assert.Contains("story_not_ready", Codes(await _sut.CheckAsync(Owner, Request())));
    }

    [Fact]
    public async Task CheckAsync_NoCurrentVersion_IsBlocked()
    {
        CurrentVersion.IsCurrent = false;

        var dto = await _sut.CheckAsync(Owner, Request());

        Assert.Contains("no_current_version", Codes(dto));
        Assert.Null(dto.StoryVersionId);
    }

    [Theory]
    [InlineData(ContentReportStatus.Pending)]
    [InlineData(ContentReportStatus.Escalated)]
    public async Task CheckAsync_OpenReport_IsBlocked(ContentReportStatus status)
    {
        _h.AddReportItem(ContentReportReason.Inappropriate, Story, 60);
        _h.Reports.Single().Status = status;

        Assert.Contains("open_report", Codes(await _sut.CheckAsync(Owner, Request())));
    }

    [Theory]
    [InlineData(ContentReportStatus.ReviewedDismissed)]
    [InlineData(ContentReportStatus.ReviewedArchived)]
    [InlineData(ContentReportStatus.Closed)]
    public async Task CheckAsync_ResolvedReport_DoesNotBlock(ContentReportStatus status)
    {
        _h.AddReportItem(ContentReportReason.Inappropriate, Story, 60);
        _h.Reports.Single().Status = status;

        Assert.DoesNotContain("open_report", Codes(await _sut.CheckAsync(Owner, Request())));
    }

    [Fact]
    public async Task CheckAsync_ReportOfAnotherStory_DoesNotBlock()
    {
        _h.AddReportItem(ContentReportReason.Inappropriate, ContentReportHarness.PublicStoryId, 50);

        Assert.DoesNotContain("open_report", Codes(await _sut.CheckAsync(Owner, Request())));
    }

    [Theory]
    [InlineData(ModerationItemStatus.Pending)]
    [InlineData(ModerationItemStatus.Claimed)]
    [InlineData(ModerationItemStatus.Approved)]
    public async Task CheckAsync_AlreadySubmitted_IsBlocked(ModerationItemStatus status)
    {
        _h.Items.Add(new ModerationItem
        {
            Id = 80,
            ItemType = ModerationItemType.CommunitySubmission,
            Status = status,
            StoryId = Story,
            CreatedByUserId = Second
        });

        Assert.Contains("already_submitted", Codes(await _sut.CheckAsync(Owner, Request())));
    }

    [Theory]
    [InlineData(ModerationItemStatus.Rejected)]
    [InlineData(ModerationItemStatus.Withdrawn)]
    [InlineData(ModerationItemStatus.Removed)]
    [InlineData(ModerationItemStatus.Closed)]
    public async Task CheckAsync_EarlierSubmissionThatEnded_DoesNotBlock(ModerationItemStatus status)
    {
        _h.Items.Add(new ModerationItem
        {
            Id = 80,
            ItemType = ModerationItemType.CommunitySubmission,
            Status = status,
            StoryId = Story,
            CreatedByUserId = Owner
        });

        Assert.DoesNotContain("already_submitted", Codes(await _sut.CheckAsync(Owner, Request())));
    }

    [Fact]
    public async Task CheckAsync_SeveralProblems_AreAllListed()
    {
        PrivateStory.Status = StoryStatus.MediaProcessing;
        CurrentVersion.Content = "Gọi 0912345678";
        _h.AddReportItem(ContentReportReason.Inappropriate, Story, 60);

        var dto = await _sut.CheckAsync(Owner, Request());

        Assert.False(dto.CanSubmit);
        Assert.Equal(new[] { "open_report", "pii_found", "story_not_ready" }, Codes(dto));
        Assert.All(dto.Blockers, blocker => Assert.False(string.IsNullOrWhiteSpace(blocker.Message)));
    }

    // ---- Task 2: bản quyền (BR-L4F-04) ----

    [Fact]
    public async Task CheckAsync_ManualStoryWithoutADeclaration_NeedsOwnWork()
    {
        PrivateStory.Source = StorySource.Manual;

        var dto = await _sut.CheckAsync(Owner, Request());

        Assert.Contains("copyright_required", Codes(dto));
        Assert.Equal("own_work", dto.RequiredDeclaration);
    }

    [Fact]
    public async Task CheckAsync_ManualStoryDeclaredOwnWork_IsAllowed()
    {
        PrivateStory.Source = StorySource.Manual;

        var dto = await _sut.CheckAsync(Owner, Request(declaration: CopyrightDeclaration.OwnWork));

        Assert.True(dto.CanSubmit);
    }

    [Fact]
    public async Task CheckAsync_FamilyUseCommitment_CannotBeSubmitted_Ac1()
    {
        PrivateStory.Source = StorySource.Manual;

        var dto = await _sut.CheckAsync(Owner, Request(declaration: CopyrightDeclaration.FamilyUse));

        Assert.False(dto.CanSubmit);
        Assert.Contains("family_use", Codes(dto));
        Assert.DoesNotContain("copyright_required", Codes(dto));
    }

    [Fact]
    public async Task CheckAsync_ManualStoryDeclaredAiGenerated_IsNotAccepted()
    {
        PrivateStory.Source = StorySource.Manual;

        var dto = await _sut.CheckAsync(Owner, Request(declaration: CopyrightDeclaration.AiGenerated));

        Assert.Contains("copyright_required", Codes(dto));
    }

    [Fact]
    public async Task CheckAsync_AiStory_IgnoresAnyDeclaration()
    {
        var dto = await _sut.CheckAsync(Owner, Request(declaration: CopyrightDeclaration.FamilyUse));

        Assert.True(dto.CanSubmit);
    }

    // ---- Task 2: thông tin cá nhân (BR-L4F-05, AC2) ----

    [Fact]
    public async Task CheckAsync_ChildNicknameInTheText_IsBlockedWithTheLocation_Ac2()
    {
        _h.Children.Single(child => child.Id == Child).Nickname = "Bin";
        CurrentVersion.Content = "Hôm nay Bin đi chơi với mẹ.";

        var dto = await _sut.CheckAsync(Owner, Request());

        Assert.False(dto.CanSubmit);
        Assert.Contains("pii_found", Codes(dto));
        var finding = Assert.Single(dto.PiiFindings);
        Assert.Equal("child_nickname", finding.Kind);
        Assert.Equal("content", finding.Location);
    }

    [Fact]
    public async Task CheckAsync_ScansTitleScenesVocabularyAndQuizToo()
    {
        CurrentVersion.Title = "Thỏ ở lớp 2A";
        _h.Scenes.Add(new StoryScene
        {
            Id = 1,
            StoryVersionId = 60,
            SceneIndex = 3,
            TextRangeStart = 0,
            TextRangeEnd = 1,
            SceneText = "Gọi 0912345678 ngay"
        });
        _h.Quizzes.Add(new QuizItem
        {
            Id = 1,
            StoryVersionId = 60,
            Question = "Email là gì? an@example.com",
            CorrectAnswer = "x"
        });

        var dto = await _sut.CheckAsync(Owner, Request());

        var locations = dto.PiiFindings.Select(f => f.Location).ToList();
        Assert.Contains("title", locations);
        Assert.Contains(locations, location => location.StartsWith("scene"));
        Assert.Contains(locations, location => location.StartsWith("quiz"));
    }

    [Fact]
    public async Task CheckAsync_CleanText_HasNoFindings()
    {
        CurrentVersion.Content = "Chú thỏ nhảy qua suối và gặp bạn cáo.";

        Assert.Empty((await _sut.CheckAsync(Owner, Request())).PiiFindings);
    }

    // ---- Task 3: nộp ----

    [Fact]
    public async Task SubmitAsync_ValidAiStory_QueuesAPendingCommunityItemPinnedToTheCurrentVersion()
    {
        var before = DateTime.UtcNow;

        var result = await _sut.SubmitAsync(Owner, Request());

        var item = Assert.Single(_h.Items);
        Assert.Equal(item.Id, result.ItemId);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(ModerationItemType.CommunitySubmission, item.ItemType);
        Assert.Equal(ModerationItemStatus.Pending, item.Status);
        Assert.Equal(ModerationPriority.Low, item.Priority);
        Assert.Equal(Story, item.StoryId);
        Assert.Equal(60, item.StoryVersionId);
        Assert.Equal(60, result.StoryVersionId);
        Assert.Equal(Owner, item.CreatedByUserId);
        Assert.Equal(Child, item.SourceChildProfileId);
        Assert.False(item.IsResubmission);
        Assert.InRange(item.SlaDueAt!.Value, before.AddHours(72), DateTime.UtcNow.AddHours(72));
    }

    [Fact]
    public async Task SubmitAsync_RecordsTheCopyrightDeclaration()
    {
        await _sut.SubmitAsync(Owner, Request());
        Assert.Equal(CopyrightDeclaration.AiGenerated, _h.Items.Single().CopyrightDeclaration);

        _h.Items.Clear();
        PrivateStory.Source = StorySource.Manual;
        await _sut.SubmitAsync(Owner, Request(declaration: CopyrightDeclaration.OwnWork));
        Assert.Equal(CopyrightDeclaration.OwnWork, _h.Items.Single().CopyrightDeclaration);
    }

    [Fact]
    public async Task SubmitAsync_StoresThePiiCheckWithoutAnyRawText()
    {
        CurrentVersion.Content = "Chú thỏ nhảy qua suối.";

        await _sut.SubmitAsync(Owner, Request());

        var json = _h.Items.Single().PiiCheckResult!;
        Assert.Contains("\"findings\":[]", json);
        Assert.Contains("\"storyVersionId\":60", json);
        Assert.Contains("checkedAt", json);
        Assert.DoesNotContain("thỏ", json);
    }

    [Fact]
    public async Task SubmitAsync_ConfirmationMissing_IsBadRequestAndNothingIsQueued()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(Owner, Request(confirm: false)));

        Assert.Empty(_h.Items);
    }

    [Fact]
    public async Task SubmitAsync_PiiStillPresent_IsBadRequestListingTheReason()
    {
        CurrentVersion.Content = "Gọi 0912345678";

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(Owner, Request()));

        Assert.Contains("thông tin", exception.Message);
        Assert.Empty(_h.Items);
    }

    [Fact]
    public async Task SubmitAsync_FamilyUseStory_IsBadRequestExplainingTheCopyright_Ac1()
    {
        PrivateStory.Source = StorySource.Manual;

        var exception = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SubmitAsync(Owner, Request(declaration: CopyrightDeclaration.FamilyUse)));

        Assert.Contains("gia đình", exception.Message);
        Assert.Empty(_h.Items);
    }

    [Fact]
    public async Task SubmitAsync_AlreadySubmitted_IsConflict()
    {
        await _sut.SubmitAsync(Owner, Request());

        await Assert.ThrowsAsync<ConflictException>(() => _sut.SubmitAsync(Owner, Request()));
        Assert.Single(_h.Items);
    }

    [Theory]
    [InlineData("story_not_ready")]
    [InlineData("open_report")]
    [InlineData("not_private")]
    public async Task SubmitAsync_OtherBlockers_AreBadRequest(string problem)
    {
        var storyId = Story;
        switch (problem)
        {
            case "story_not_ready": PrivateStory.Status = StoryStatus.MediaProcessing; break;
            case "open_report": _h.AddReportItem(ContentReportReason.Inappropriate, Story, 60); break;
            case "not_private": storyId = ContentReportHarness.PublicStoryId; break;
        }

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(Owner, Request(storyId)));
        Assert.DoesNotContain(_h.Items, item => item.ItemType == ModerationItemType.CommunitySubmission);
    }

    [Fact]
    public async Task SubmitAsync_WithoutApproveStory_IsForbidden()
    {
        _h.AddSupervisor(Child, Second, SupervisorRole.AdditionalSupervisor, Permission.ViewProgress);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SubmitAsync(Second, Request()));
    }

    // ---- Task 3: nộp lại sau khi bị từ chối ----

    [Fact]
    public async Task SubmitAsync_AfterARejection_IsMarkedAsAResubmissionOfTheLatestRejectedItem()
    {
        _h.Items.Add(new ModerationItem
        {
            Id = 70,
            ItemType = ModerationItemType.CommunitySubmission,
            Status = ModerationItemStatus.Rejected,
            StoryId = Story,
            CreatedByUserId = Owner
        });
        _h.Items.Add(new ModerationItem
        {
            Id = 71,
            ItemType = ModerationItemType.CommunitySubmission,
            Status = ModerationItemStatus.Rejected,
            StoryId = Story,
            CreatedByUserId = Owner
        });

        await _sut.SubmitAsync(Owner, Request());

        var item = _h.Items.Single(candidate => candidate.Status == ModerationItemStatus.Pending);
        Assert.True(item.IsResubmission);
        Assert.Equal(71, item.PreviousItemId);
    }

    [Fact]
    public async Task SubmitAsync_AfterAWithdrawal_IsNotAResubmission()
    {
        _h.Items.Add(new ModerationItem
        {
            Id = 70,
            ItemType = ModerationItemType.CommunitySubmission,
            Status = ModerationItemStatus.Withdrawn,
            StoryId = Story,
            CreatedByUserId = Owner
        });

        await _sut.SubmitAsync(Owner, Request());

        Assert.False(_h.Items.Single(candidate => candidate.Status == ModerationItemStatus.Pending).IsResubmission);
    }

    // ---- Task 3: bản ghim (AC3), vai trò, audit, giao dịch ----

    [Fact]
    public async Task SubmitAsync_EditingTheSourceStoryAfterwardsDoesNotChangeThePinnedVersion_Ac3()
    {
        await _sut.SubmitAsync(Owner, Request());
        CurrentVersion.IsCurrent = false;
        _h.Versions.Add(new StoryVersion { Id = 61, StoryId = Story, VersionNo = 2, Title = "v2", IsCurrent = true });

        Assert.Equal(60, _h.Items.Single().StoryVersionId);
    }

    [Fact]
    public async Task SubmitAsync_TheQueuedItemIsVisibleToAModerator()
    {
        var queue = new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object);
        var result = await _sut.SubmitAsync(Owner, Request());

        var seenByModerator = await queue.ListAsync(Moderator, new StoryPlatform.Application.Features.Moderation.DTOs.ModerationQueueQueryDto());

        Assert.Contains(seenByModerator.Items, row => row.Id == result.ItemId);
    }

    [Fact]
    public async Task SubmitAsync_WritesAuditAndCommitsOneTransaction()
    {
        var result = await _sut.SubmitAsync(Owner, Request());

        _h.Audit.Verify(a => a.LogAsync(
            Owner, "CommunitySubmissionSubmitted", nameof(ModerationItem), result.ItemId,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_AuditFails_RollsBack()
    {
        _h.Audit.Setup(a => a.LogAsync(
                It.IsAny<int?>(), "CommunitySubmissionSubmitted", It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SubmitAsync(Owner, Request()));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Task 4: rút ----

    private ModerationItem Item(
        int id, ModerationItemStatus status, int createdBy = Owner, int storyId = Story)
    {
        var item = new ModerationItem
        {
            Id = id,
            ItemType = ModerationItemType.CommunitySubmission,
            Status = status,
            StoryId = storyId,
            StoryVersionId = 60,
            CreatedByUserId = createdBy
        };
        _h.Items.Add(item);
        return item;
    }

    [Fact]
    public async Task WithdrawAsync_PendingItem_BecomesWithdrawn()
    {
        var item = Item(80, ModerationItemStatus.Pending);
        var before = DateTime.UtcNow;

        var dto = await _sut.WithdrawAsync(Owner, 80);

        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
        Assert.True(item.WithdrawnAt >= before);
        Assert.Equal(1, item.RowVersion);
        Assert.Equal("Withdrawn", dto.Status);
        Assert.False(dto.CanWithdraw);
    }

    [Fact]
    public async Task WithdrawAsync_ThenSubmitAgain_Works()
    {
        var first = await _sut.SubmitAsync(Owner, Request());
        await _sut.WithdrawAsync(Owner, first.ItemId);

        var second = await _sut.SubmitAsync(Owner, Request());

        Assert.NotEqual(first.ItemId, second.ItemId);
    }

    [Theory]
    [InlineData(ModerationItemStatus.Claimed)]
    [InlineData(ModerationItemStatus.Approved)]
    [InlineData(ModerationItemStatus.Rejected)]
    [InlineData(ModerationItemStatus.Withdrawn)]
    [InlineData(ModerationItemStatus.Removed)]
    public async Task WithdrawAsync_NotPending_IsConflict(ModerationItemStatus status)
    {
        var item = Item(80, status);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.WithdrawAsync(Owner, 80));
        Assert.Equal(status, item.Status);
    }

    [Fact]
    public async Task WithdrawAsync_OnlyTheSubmitter_AndOnlyCommunityItems()
    {
        Item(80, ModerationItemStatus.Pending, createdBy: Second);
        _h.Items.Add(new ModerationItem
        {
            Id = 81,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Pending,
            CreatedByUserId = Owner
        });

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.WithdrawAsync(Owner, 80));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.WithdrawAsync(Owner, 81));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.WithdrawAsync(Owner, 999));
    }

    [Fact]
    public async Task WithdrawAsync_WritesAudit()
    {
        Item(80, ModerationItemStatus.Pending);

        await _sut.WithdrawAsync(Owner, 80);

        _h.Audit.Verify(a => a.LogAsync(
            Owner, "CommunitySubmissionWithdrawn", nameof(ModerationItem), 80,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Task 4: gỡ ----

    [Fact]
    public async Task RemoveAsync_ApprovedItem_BecomesRemoved()
    {
        var item = Item(80, ModerationItemStatus.Approved);
        var before = DateTime.UtcNow;

        var dto = await _sut.RemoveAsync(Owner, 80);

        Assert.Equal(ModerationItemStatus.Removed, item.Status);
        Assert.True(item.RemovedAt >= before);
        Assert.Equal("submitter_removed", item.ClosedReason);
        Assert.Equal(1, item.RowVersion);
        Assert.Equal("Removed", dto.Status);
    }

    [Theory]
    [InlineData(ModerationItemStatus.Pending)]
    [InlineData(ModerationItemStatus.Claimed)]
    [InlineData(ModerationItemStatus.Rejected)]
    [InlineData(ModerationItemStatus.Withdrawn)]
    [InlineData(ModerationItemStatus.Removed)]
    public async Task RemoveAsync_NotApproved_IsConflict(ModerationItemStatus status)
    {
        Item(80, status);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.RemoveAsync(Owner, 80));
    }

    [Fact]
    public async Task RemoveAsync_OnlyTheSubmitter()
    {
        Item(80, ModerationItemStatus.Approved, createdBy: Second);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.RemoveAsync(Owner, 80));
    }

    [Fact]
    public async Task RemoveAsync_WritesAudit()
    {
        Item(80, ModerationItemStatus.Approved);

        await _sut.RemoveAsync(Owner, 80);

        _h.Audit.Verify(a => a.LogAsync(
            Owner, "CommunitySubmissionRemoved", nameof(ModerationItem), 80,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveThenSubmitAgain_Works()
    {
        var first = await _sut.SubmitAsync(Owner, Request());
        _h.Items.Single().Status = ModerationItemStatus.Approved;
        await _sut.RemoveAsync(Owner, first.ItemId);

        var second = await _sut.SubmitAsync(Owner, Request());

        Assert.NotEqual(first.ItemId, second.ItemId);
    }

    // ---- Task 4: danh sách của tôi ----

    [Fact]
    public async Task ListMineAsync_ShowsOnlyMySubmissionsNewestFirstWithFriendlyStatuses()
    {
        Item(80, ModerationItemStatus.Pending);
        Item(81, ModerationItemStatus.Claimed);
        Item(82, ModerationItemStatus.Approved);
        var rejected = Item(83, ModerationItemStatus.Rejected);
        rejected.DecisionReason = "Còn tên trường";
        rejected.DecidedAt = DateTime.UtcNow;
        Item(84, ModerationItemStatus.Closed);
        Item(85, ModerationItemStatus.Pending, createdBy: Second);
        _h.Items.Add(new ModerationItem
        {
            Id = 86,
            ItemType = ModerationItemType.ContentReport,
            Status = ModerationItemStatus.Pending,
            CreatedByUserId = Owner
        });

        var list = await _sut.ListMineAsync(Owner);

        Assert.Equal(new[] { 84, 83, 82, 81, 80 }, list.Select(dto => dto.ItemId).ToArray());
        Assert.Equal(
            new[] { "Withdrawn", "Rejected", "Approved", "Pending", "Pending" },
            list.Select(dto => dto.Status).ToArray());
        Assert.Equal("Còn tên trường", list.Single(dto => dto.ItemId == 83).RejectReason);
        Assert.Equal("Truyện của bé", list[0].StoryTitle);
    }

    [Fact]
    public async Task ListMineAsync_ActionFlags()
    {
        Item(80, ModerationItemStatus.Pending);
        Item(81, ModerationItemStatus.Claimed);
        Item(82, ModerationItemStatus.Approved);

        var list = await _sut.ListMineAsync(Owner);

        Assert.True(list.Single(dto => dto.ItemId == 80).CanWithdraw);
        Assert.False(list.Single(dto => dto.ItemId == 81).CanWithdraw);
        Assert.True(list.Single(dto => dto.ItemId == 82).CanRemove);
        Assert.False(list.Single(dto => dto.ItemId == 80).CanRemove);
    }

    [Fact]
    public async Task ListMineAsync_NeverRevealsWhoReviewed()
    {
        var names = typeof(CommunitySubmissionDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, name => name.Contains("Moderator", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("DecidedBy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Claimed", StringComparison.OrdinalIgnoreCase));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ListMineAsync_IsLimitedToFifty()
    {
        for (var id = 100; id < 160; id++)
        {
            Item(id, ModerationItemStatus.Withdrawn);
        }

        Assert.Equal(50, (await _sut.ListMineAsync(Owner)).Count);
    }
}
