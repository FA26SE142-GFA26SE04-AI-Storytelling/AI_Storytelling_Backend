using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class ChildRoadmapServiceTests
{
    private readonly ChildRoadmapHarness _h = new();
    private readonly ChildRoadmapService _sut;

    private const int Child = ChildRoadmapHarness.Child;
    private const int OtherChild = ChildRoadmapHarness.OtherChild;
    private const int Owner = ChildRoadmapHarness.ParentId;
    private const int Second = ChildRoadmapHarness.SecondSupervisorId;
    private const int Stranger = ChildRoadmapHarness.StrangerId;

    public ChildRoadmapServiceTests()
    {
        var eligibility = new ChildRoadmapEligibility(_h.UnitOfWork.Object);
        _sut = new ChildRoadmapService(
            _h.UnitOfWork.Object,
            new SupervisionAccessGuard(_h.UnitOfWork.Object),
            eligibility,
            new ChildRoadmapInserter(_h.UnitOfWork.Object),
            new ChildRoadmapAdvancer(_h.UnitOfWork.Object, eligibility),
            new ChildRoadmapNotifier(_h.UnitOfWork.Object, _h.Notifications.Object),
            _h.Audit.Object);
    }

    private static AddRoadmapStoryRequestDto Add(
        int storyId, params int[] children) => new() { StoryId = storyId, ChildProfileIds = children.ToList() };

    private ChildRoadmapItem Item(int id) => _h.Items.Single(item => item.Id == id);

    [Fact]
    public async Task AddForWish_LinksFulfillmentToRoadmapInOneTransaction()
    {
        var wish = new StoryWish { Id = 55, ChildProfileId = Child, Status = StoryWishStatus.Pending, Kind = StoryWishKind.Wish };
        _h.Wishes.Add(wish);
        _h.WishSuggestions.Add(new() { Id = 1, StoryWishId = 55, StoryId = 10 });
        var result = await _sut.AddForWishAsync(Owner, Child, 10, 55);
        Assert.Equal(StoryWishStatus.Fulfilled, wish.Status);
        Assert.Equal(10, wish.FulfilledStoryId);
        Assert.True(wish.FulfilledWithExistingStory);
        Assert.Equal(10, Item(result.ItemId).StoryId);
        _h.UnitOfWork.Verify(x => x.AcquireTransactionLockAsync(-Child, It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddForWish_AlreadyAcceptedOrUnsuggestedStory_DoesNotInsert(bool accepted)
    {
        _h.Wishes.Add(new() { Id = 55, ChildProfileId = Child, Status = accepted ? StoryWishStatus.Accepted : StoryWishStatus.Pending,
            Kind = StoryWishKind.Wish });
        if (accepted)
            await Assert.ThrowsAsync<ConflictException>(() => _sut.AddForWishAsync(Owner, Child, 10, 55));
        else
            await Assert.ThrowsAsync<BadRequestException>(() => _sut.AddForWishAsync(Owner, Child, 10, 55));
        Assert.Empty(_h.Items);
        _h.UnitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SeedRoadmap()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting, 1);
        _h.AddItem(3, Child, 12, ChildRoadmapItemStatus.Waiting, 2);
        _h.AddItem(4, Child, 13, ChildRoadmapItemStatus.Waiting, 3);
    }

    [Fact]
    public async Task AddStory_EmptyRoadmap_AddsAndOpensImmediately()
    {
        var result = await _sut.AddStoryAsync(Owner, Add(10, Child));

        Assert.Equal("Added", result.Results.Single().Outcome);
        Assert.Equal(ChildRoadmapItemStatus.Open, Assert.Single(_h.Items).Status);
    }

    [Fact]
    public async Task AddStory_NextUpMovesExistingWaitingStepWithoutDuplicate()
    {
        SeedRoadmap();
        var request = Add(13, Child);
        request.Position = RoadmapInsertPosition.NextUp;

        var result = await _sut.AddStoryAsync(Owner, request);

        Assert.Equal("MovedToNext", result.Results.Single().Outcome);
        Assert.Equal(new[] { 13, 11, 12 }, _h.Items.Where(x => x.Status == ChildRoadmapItemStatus.Waiting)
            .OrderBy(x => x.Position).Select(x => x.StoryId));
        Assert.Equal(4, _h.Items.Count);
    }

    [Fact]
    public async Task AddStory_EndWithExistingStep_IsSkipped()
    {
        SeedRoadmap();

        var result = await _sut.AddStoryAsync(Owner, Add(11, Child));

        Assert.Equal(ChildRoadmapSkipCodes.AlreadyInRoadmap, result.Results.Single().SkipCode);
    }

    [Fact]
    public async Task AddStory_ChecksEachChildIndependently()
    {
        _h.Block(OtherChild, 5);
        _h.Tag(10, 5);

        var result = await _sut.AddStoryAsync(Owner, Add(10, Child, OtherChild));

        Assert.Equal("Added", result.Results.Single(x => x.ChildProfileId == Child).Outcome);
        Assert.Equal(ChildRoadmapSkipCodes.BlockedTopic,
            result.Results.Single(x => x.ChildProfileId == OtherChild).SkipCode);
    }

    [Fact]
    public async Task AddStory_WithoutPermission_IsPerChildSkip()
    {
        var result = await _sut.AddStoryAsync(Stranger, Add(10, Child));

        Assert.Equal(ChildRoadmapSkipCodes.NoPermission, result.Results.Single().SkipCode);
        Assert.Empty(_h.Items);
    }

    [Fact]
    public async Task AddStory_PrivateSiblingStoryNeedsApproveStoryForBothChildren()
    {
        var story = _h.Stories.Single(x => x.Id == 10);
        story.ChildProfileId = OtherChild;
        _h.AddSupervisor(Child, Second, SupervisorRole.AdditionalSupervisor,
            Permission.AssignActivity, Permission.ApproveStory);

        var denied = await _sut.AddStoryAsync(Second, Add(10, Child));
        Assert.Equal(ChildRoadmapSkipCodes.NoAccessToStory, denied.Results.Single().SkipCode);

        _h.AddSupervisor(OtherChild, Second, SupervisorRole.AdditionalSupervisor, Permission.ApproveStory);
        var allowed = await _sut.AddStoryAsync(Second, Add(10, Child));
        Assert.Equal("Added", allowed.Results.Single().Outcome);
    }

    [Fact]
    public async Task AddStory_PrivateSiblingDifferentReadingLevel_AddsWarning()
    {
        var story = _h.Stories.Single(x => x.Id == 10);
        story.ChildProfileId = OtherChild;
        story.ReadingLevel = 5;
        _h.AddLearning(Child, 2);

        var result = await _sut.AddStoryAsync(Owner, Add(10, Child));

        Assert.Contains("reading_level_differs", result.Results.Single().Warnings);
    }

    [Fact]
    public async Task AddStory_DifferentAgeIsParentsChoice_AndRemainsOpen()
    {
        _h.Stories.Single(x => x.Id == 10).AgeBand = "9-10";
        var result = await _sut.AddStoryAsync(Owner, Add(10, Child));
        Assert.Equal("Added", result.Results.Single().Outcome);
        Assert.Contains(result.Results.Single().Warnings, warning => warning.Contains("khác nhóm tuổi"));
        Assert.Equal(ChildRoadmapItemStatus.Open, _h.Items.Single().Status);
    }

    [Fact]
    public async Task AddStory_InvalidRequestIsRejected()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AddStoryAsync(Owner, Add(10)));
        var badDays = Add(10, Child);
        badDays.ExpectedDays = 91;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AddStoryAsync(Owner, badDays));
    }

    [Fact]
    public async Task AddStory_NotifiesOtherSupervisorsAndCommits()
    {
        _h.AddSupervisor(Child, Second, SupervisorRole.AdditionalSupervisor, Permission.ViewProgress);

        await _sut.AddStoryAsync(Owner, Add(10, Child));

        _h.Notifications.Verify(n => n.CreateAsync(
            Second, NotificationType.RoadmapStoryAdded, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_OpenStepMarksRemovedAndOpensNext()
    {
        SeedRoadmap();

        var result = await _sut.RemoveAsync(Owner, Child, 1);

        Assert.Equal(ChildRoadmapItemStatus.Removed, Item(1).Status);
        Assert.Equal(Owner, Item(1).RemovedByUserId);
        Assert.Equal(ChildRoadmapItemStatus.Open, Item(2).Status);
        Assert.Equal(11, result.Open!.StoryId);
    }

    [Theory]
    [InlineData(ChildRoadmapItemStatus.Completed)]
    [InlineData(ChildRoadmapItemStatus.Skipped)]
    [InlineData(ChildRoadmapItemStatus.Removed)]
    public async Task Remove_FinishedStepIsConflict(ChildRoadmapItemStatus status)
    {
        _h.AddItem(1, Child, 10, status);
        await Assert.ThrowsAsync<ConflictException>(() => _sut.RemoveAsync(Owner, Child, 1));
    }

    [Fact]
    public async Task Reorder_RequiresExactWaitingSet()
    {
        SeedRoadmap();

        await _sut.ReorderAsync(Owner, Child, new ReorderRoadmapRequestDto
            { ItemIds = new List<int> { 4, 2, 3 } });
        Assert.Equal(new[] { 13, 11, 12 }, _h.Items.Where(x => x.Status == ChildRoadmapItemStatus.Waiting)
            .OrderBy(x => x.Position).Select(x => x.StoryId));

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ReorderAsync(
            Owner, Child, new ReorderRoadmapRequestDto { ItemIds = new List<int> { 2, 3 } }));
    }

    [Fact]
    public async Task Clear_RemovesOnlyActiveStepsAndDoesNotOpenAnother()
    {
        SeedRoadmap();
        _h.AddItem(5, Child, 10, ChildRoadmapItemStatus.Completed);

        var roadmap = await _sut.ClearAsync(Owner, Child);

        Assert.All(new[] { 1, 2, 3, 4 }, id => Assert.Equal(ChildRoadmapItemStatus.Removed, Item(id).Status));
        Assert.Equal(ChildRoadmapItemStatus.Completed, Item(5).Status);
        Assert.Null(roadmap.Open);
        Assert.Empty(roadmap.Waiting);
    }

    [Fact]
    public async Task Get_ReturnsOpenOrderedQueueHistoryAndProgress()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Completed).CompletedAt = DateTime.UtcNow.AddMinutes(-10);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Open);
        _h.AddItem(3, Child, 12, ChildRoadmapItemStatus.Waiting, 2);
        _h.AddItem(4, Child, 13, ChildRoadmapItemStatus.Waiting, 1);

        var roadmap = await _sut.GetAsync(Owner, Child);

        Assert.Equal(11, roadmap.Open!.StoryId);
        Assert.Equal(new[] { 13, 12 }, roadmap.Waiting.Select(x => x.StoryId));
        Assert.Single(roadmap.History);
        Assert.Equal(1, roadmap.CompletedCount);
        Assert.Equal(4, roadmap.TotalCount);
    }

    [Fact]
    public async Task Get_AllowsActiveSupervisorsButRejectsStrangers()
    {
        _h.AddSupervisor(Child, Second, SupervisorRole.AdditionalSupervisor, Permission.ViewProgress);
        Assert.Equal(Child, (await _sut.GetAsync(Second, Child)).ChildProfileId);
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetAsync(Stranger, Child));
    }

    private void Publish(int storyId)
    {
        var story = _h.Stories.Single(existing => existing.Id == storyId);
        story.Scope = StoryScope.Creator;
        story.IsPublished = true;
    }

    private int[] WaitingStories() => _h.Items
        .Where(item => item.Status == ChildRoadmapItemStatus.Waiting)
        .OrderBy(item => item.Position)
        .Select(item => item.StoryId)
        .ToArray();

    private void VerifyNotified(int userId, NotificationType type, Times times) =>
        _h.Notifications.Verify(notification => notification.CreateAsync(
            userId, type, It.IsAny<string?>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task AddForWishAsync_EmptyRoadmap_AddsTheStoryAtOnceAsOpenWithTheWishNote()
    {
        Publish(10);

        var result = await _sut.AddForWishAsync(Owner, Child, 10);

        Assert.Equal("Added", result.Outcome);
        var item = Assert.Single(_h.Items);
        Assert.Equal(result.ItemId, item.Id);
        Assert.Equal(ChildRoadmapItemStatus.Open, item.Status);
        Assert.Equal(ChildRoadmapItemSource.Wish, item.Source);
        Assert.Equal("Từ điều ước của em", item.Note);
        Assert.Equal(Owner, item.AddedByUserId);
    }

    [Fact]
    public async Task AddForWishAsync_AlwaysGoesToTheNextUpPosition()
    {
        Publish(10);
        _h.AddItem(1, Child, 11, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 12, ChildRoadmapItemStatus.Waiting);
        _h.AddItem(3, Child, 13, ChildRoadmapItemStatus.Waiting);

        await _sut.AddForWishAsync(Owner, Child, 10);

        Assert.Equal(new[] { 10, 12, 13 }, WaitingStories());
        Assert.Equal(ChildRoadmapItemStatus.Open, Item(1).Status);
    }

    [Fact]
    public async Task AddForWishAsync_SupervisorWithOnlyApproveStory_CanDoIt()
    {
        Publish(10);
        _h.AddSupervisor(Child, Second, SupervisorRole.AdditionalSupervisor, Permission.ApproveStory);

        var result = await _sut.AddForWishAsync(Second, Child, 10);

        Assert.Equal("Added", result.Outcome);
        Assert.Equal(Second, _h.Items.Single().AddedByUserId);
    }

    [Fact]
    public async Task AddForWishAsync_SupervisorWithOnlyAssignActivity_IsForbidden()
    {
        Publish(10);
        _h.AddSupervisor(Child, Second, SupervisorRole.AdditionalSupervisor, Permission.AssignActivity);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.AddForWishAsync(Second, Child, 10));
        Assert.Empty(_h.Items);
    }

    [Fact]
    public async Task AddForWishAsync_NotASupervisor_IsForbidden()
    {
        Publish(10);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.AddForWishAsync(Stranger, Child, 10));
    }

    [Fact]
    public async Task AddForWishAsync_StoryWaitingInTheRoadmap_MovesToTheFrontAndIsTaggedWithoutANewRow()
    {
        Publish(12);
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);
        _h.AddItem(3, Child, 12, ChildRoadmapItemStatus.Waiting);

        var result = await _sut.AddForWishAsync(Owner, Child, 12);

        Assert.Equal("MovedToNext", result.Outcome);
        Assert.Equal(3, result.ItemId);
        Assert.Equal(new[] { 12, 11 }, WaitingStories());
        Assert.Equal(ChildRoadmapItemSource.Wish, Item(3).Source);
        Assert.Equal("Từ điều ước của em", Item(3).Note);
        Assert.Equal(3, _h.Items.Count);
    }

    [Fact]
    public async Task AddForWishAsync_StoryAlreadyOpen_IsOnlyTaggedAndNothingMoves()
    {
        Publish(10);
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting);

        var result = await _sut.AddForWishAsync(Owner, Child, 10);

        Assert.Equal("Tagged", result.Outcome);
        Assert.Equal(ChildRoadmapItemStatus.Open, Item(1).Status);
        Assert.Equal(ChildRoadmapItemSource.Wish, Item(1).Source);
        Assert.Equal(2, _h.Items.Count);
    }

    [Fact]
    public async Task AddForWishAsync_AnExistingNoteIsKept()
    {
        Publish(10);
        var existing = _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        existing.Note = "Lời nhắn của bố";

        await _sut.AddForWishAsync(Owner, Child, 10);

        Assert.Equal("Lời nhắn của bố", Item(1).Note);
        Assert.Equal(ChildRoadmapItemSource.Wish, Item(1).Source);
    }

    [Fact]
    public async Task AddForWishAsync_AFinishedStepOfTheSameStory_DoesNotBlockANewOne()
    {
        Publish(10);
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Completed);

        var result = await _sut.AddForWishAsync(Owner, Child, 10);

        Assert.Equal("Added", result.Outcome);
        Assert.Equal(2, _h.Items.Count);
    }

    [Fact]
    public async Task AddForWishAsync_ChildInHoldMode_StoryWaitsAtTheFront()
    {
        Publish(10);
        _h.AddHoldMode(Child);

        await _sut.AddForWishAsync(Owner, Child, 10);

        Assert.Equal(ChildRoadmapItemStatus.Waiting, _h.Items.Single().Status);
    }

    [Fact]
    public async Task AddForWishAsync_BlockedTopic_IsBadRequestWithTheReason()
    {
        Publish(10);
        _h.Block(Child, 5);
        _h.Tag(10, 5);

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.AddForWishAsync(Owner, Child, 10));

        Assert.Contains("chặn", exception.Message);
        Assert.Empty(_h.Items);
    }

    [Theory]
    [InlineData("wrong-age", "độ tuổi")]
    [InlineData("too-long", "dài")]
    [InlineData("archived", "không còn")]
    public async Task AddForWishAsync_UnsuitableStory_IsBadRequest(string problem, string fragment)
    {
        Publish(10);
        var story = _h.Stories.Single(existing => existing.Id == 10);
        switch (problem)
        {
            case "wrong-age":
                story.AgeBand = "9-10";
                break;
            case "too-long":
                _h.AddPolicy(Child, 100);
                story.Content = string.Join(' ', Enumerable.Repeat("bé", 500));
                break;
            case "archived":
                story.Status = StoryStatus.Archived;
                break;
        }

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.AddForWishAsync(Owner, Child, 10));

        Assert.Contains(fragment, exception.Message);
    }

    [Fact]
    public async Task AddForWishAsync_PrivateStoryOfAnotherChild_IsBadRequest()
    {
        var story = _h.Stories.Single(existing => existing.Id == 10);
        story.Scope = StoryScope.Parent;
        story.ChildProfileId = OtherChild;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AddForWishAsync(Owner, Child, 10));
    }

    [Fact]
    public async Task AddForWishAsync_PrivateStoryOfTheSameChild_IsAccepted()
    {
        var story = _h.Stories.Single(existing => existing.Id == 10);
        story.Scope = StoryScope.Parent;
        story.ChildProfileId = Child;

        var result = await _sut.AddForWishAsync(Owner, Child, 10);

        Assert.Equal("Added", result.Outcome);
    }

    [Fact]
    public async Task AddForWishAsync_UnknownOrDeletedStory_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.AddForWishAsync(Owner, Child, 999));

        _h.Stories.Single(story => story.Id == 10).IsDeleted = true;
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.AddForWishAsync(Owner, Child, 10));
    }

    [Theory]
    [InlineData(ChildProfileStatus.Suspended)]
    [InlineData(ChildProfileStatus.Archived)]
    [InlineData(ChildProfileStatus.Draft)]
    public async Task AddForWishAsync_InactiveProfile_IsConflict(ChildProfileStatus status)
    {
        Publish(10);
        _h.Children.Single(child => child.Id == Child).Status = status;

        await Assert.ThrowsAsync<ConflictException>(() => _sut.AddForWishAsync(Owner, Child, 10));
    }

    [Fact]
    public async Task AddForWishAsync_NotifiesTheOtherSupervisorsButNotTheActor()
    {
        Publish(10);
        _h.AddSupervisor(Child, Second, SupervisorRole.AdditionalSupervisor, Permission.ViewProgress);

        await _sut.AddForWishAsync(Owner, Child, 10);

        VerifyNotified(Second, NotificationType.RoadmapStoryAdded, Times.Once());
        VerifyNotified(Owner, NotificationType.RoadmapStoryAdded, Times.Never());
    }

    [Fact]
    public async Task AddForWishAsync_WritesAuditAndCommits()
    {
        Publish(10);

        var result = await _sut.AddForWishAsync(Owner, Child, 10);

        _h.Audit.Verify(audit => audit.LogAsync(
            Owner, "ChildRoadmapWishStoryAdded", nameof(ChildRoadmapItem), result.ItemId,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddForWishAsync_AuditFails_RollsBack()
    {
        Publish(10);
        _h.Audit.Setup(audit => audit.LogAsync(
                It.IsAny<int?>(), "ChildRoadmapWishStoryAdded", It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AddForWishAsync(Owner, Child, 10));

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
