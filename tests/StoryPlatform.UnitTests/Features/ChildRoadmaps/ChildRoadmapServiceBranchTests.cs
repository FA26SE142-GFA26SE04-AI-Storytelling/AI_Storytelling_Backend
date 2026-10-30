using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

/// <summary>Guard and branch coverage for ChildRoadmapService (Report 5, Việc 3.4).</summary>
public class ChildRoadmapServiceBranchTests
{
    private const int Child = ChildRoadmapHarness.Child;
    private const int Owner = ChildRoadmapHarness.ParentId;
    private readonly ChildRoadmapHarness _h = new();
    private readonly ChildRoadmapService _sut;

    public ChildRoadmapServiceBranchTests()
    {
        var eligibility = new ChildRoadmapEligibility(_h.UnitOfWork.Object);
        _sut = new ChildRoadmapService(
            _h.UnitOfWork.Object,
            new ChildProfileAccessGuard(_h.UnitOfWork.Object),
            eligibility,
            new ChildRoadmapInserter(_h.UnitOfWork.Object),
            new ChildRoadmapAdvancer(_h.UnitOfWork.Object, eligibility),
            new ChildRoadmapNotifier(_h.UnitOfWork.Object, _h.Notifications.Object),
            _h.Audit.Object);
    }

    // ---- AddStoryAsync ----

    [Fact]
    public async Task AddStoryAsync_UndefinedPosition_ThrowsBadRequest()
    {
        var request = Add(10, Child);
        request.Position = (RoadmapInsertPosition)99;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.AddStoryAsync(Owner, request));

        Assert.Equal("Vị trí phải là NextUp (đọc tiếp theo) hoặc End (thêm vào cuối).", error.Message);
    }

    [Fact]
    public async Task AddStoryAsync_TwentyOneChildren_ThrowsBadRequest()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AddStoryAsync(Owner, Add(10, Enumerable.Range(1, 21).ToArray())));

        Assert.Equal("Phải chọn từ 1 đến 20 con.", error.Message);
    }

    [Fact]
    public async Task AddStoryAsync_NullChildList_ThrowsBadRequest()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AddStoryAsync(Owner, new AddRoadmapStoryRequestDto { StoryId = 10, ChildProfileIds = null! }));

        Assert.Equal("Phải chọn từ 1 đến 20 con.", error.Message);
    }

    [Fact]
    public async Task AddStoryAsync_ExpectedDaysZero_ThrowsBadRequest()
    {
        var request = Add(10, Child);
        request.ExpectedDays = 0;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.AddStoryAsync(Owner, request));

        Assert.Equal("Số ngày mong đọc xong từ 1 đến 90.", error.Message);
    }

    [Fact]
    public async Task AddStoryAsync_NoteLongerThan500_ThrowsBadRequest()
    {
        var request = Add(10, Child);
        request.Note = new string('a', 501);

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.AddStoryAsync(Owner, request));

        Assert.Equal("Lời nhắn tối đa 500 ký tự.", error.Message);
        Assert.Empty(_h.Items);
    }

    [Fact]
    public async Task AddStoryAsync_NoteOfExactly500AfterTrim_IsStoredTrimmed()
    {
        var request = Add(10, Child);
        request.Note = "  " + new string('a', 500) + "  ";
        request.ExpectedDays = 90;

        await _sut.AddStoryAsync(Owner, request);

        var item = Assert.Single(_h.Items);
        Assert.Equal(500, item.Note!.Length);
        Assert.Equal(90, item.ExpectedDays);
    }

    [Fact]
    public async Task AddStoryAsync_UnknownStory_ThrowsNotFound()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() => _sut.AddStoryAsync(Owner, Add(999, Child)));

        Assert.Equal("Không tìm thấy Truyện với mã định danh [999].", error.Message);
    }

    [Fact]
    public async Task AddStoryAsync_DeletedStory_ThrowsNotFound()
    {
        _h.Stories.Single(story => story.Id == 10).IsDeleted = true;

        var error = await Assert.ThrowsAsync<NotFoundException>(() => _sut.AddStoryAsync(Owner, Add(10, Child)));

        Assert.Equal("Không tìm thấy Truyện với mã định danh [10].", error.Message);
    }

    [Fact]
    public async Task AddStoryAsync_DuplicateChildIds_AreProcessedOnce()
    {
        var result = await _sut.AddStoryAsync(Owner, Add(10, Child, Child));

        Assert.Single(result.Results);
        Assert.Single(_h.Items);
    }

    [Fact]
    public async Task AddStoryAsync_UnknownChild_IsSkippedAsNoPermission()
    {
        var result = await _sut.AddStoryAsync(Owner, Add(10, 555));

        var skipped = Assert.Single(result.Results);
        Assert.Equal(("Skipped", ChildRoadmapSkipCodes.NoPermission), (skipped.Outcome, skipped.SkipCode));
        Assert.Equal("Bạn không có quyền giao truyện cho bé này.", skipped.Message);
    }

    [Fact]
    public async Task AddStoryAsync_SuspendedChild_IsSkippedAsProfileInactive()
    {
        _h.Children.Single(child => child.Id == Child).Status = ChildProfileStatus.Suspended;

        var result = await _sut.AddStoryAsync(Owner, Add(10, Child));

        var skipped = Assert.Single(result.Results);
        Assert.Equal(ChildRoadmapSkipCodes.ProfileInactive, skipped.SkipCode);
        Assert.Equal("Hồ sơ của bé đang không hoạt động.", skipped.Message);
        Assert.Empty(_h.Items);
    }

    [Fact]
    public async Task AddStoryAsync_ParentStoryWithoutChild_IsSkippedAsStoryUnavailable()
    {
        _h.Stories.Single(story => story.Id == 10).ChildProfileId = null;

        var skipped = Assert.Single((await _sut.AddStoryAsync(Owner, Add(10, Child))).Results);

        Assert.Equal(ChildRoadmapSkipCodes.StoryUnavailable, skipped.SkipCode);
        Assert.Equal("Truyện hiện không còn đọc được.", skipped.Message);
    }

    [Fact]
    public async Task AddStoryAsync_CreatorLibraryStory_IsAddedWithoutStoryOwnershipCheck()
    {
        var story = _h.Stories.Single(item => item.Id == 10);
        story.Scope = StoryScope.Creator;
        story.ChildProfileId = null;

        var added = Assert.Single((await _sut.AddStoryAsync(Owner, Add(10, ChildRoadmapHarness.OtherChild))).Results);

        Assert.Equal("Added", added.Outcome);
    }

    [Fact]
    public async Task AddStoryAsync_AuditWriteFails_RollsBackAndRethrows()
    {
        FailAudit();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AddStoryAsync(Owner, Add(10, Child)));

        Assert.Equal("audit down", error.Message);
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- RemoveAsync ----

    [Fact]
    public async Task RemoveAsync_UnknownItem_ThrowsNotFound()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() => _sut.RemoveAsync(Owner, Child, 99));

        Assert.Equal("Không tìm thấy Bước trong lộ trình với mã định danh [99].", error.Message);
    }

    [Fact]
    public async Task RemoveAsync_ItemOfAnotherChild_ThrowsNotFound()
    {
        _h.AddItem(1, ChildRoadmapHarness.OtherChild, 10, ChildRoadmapItemStatus.Waiting);

        var error = await Assert.ThrowsAsync<NotFoundException>(() => _sut.RemoveAsync(Owner, Child, 1));

        Assert.Equal("Không tìm thấy Bước trong lộ trình với mã định danh [1].", error.Message);
    }

    [Fact]
    public async Task RemoveAsync_FinishedStep_ThrowsConflictWithMessage()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Completed);

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.RemoveAsync(Owner, Child, 1));

        Assert.Equal("Truyện này đã đọc xong, đã bỏ qua hoặc đã gỡ nên không gỡ được nữa.", error.Message);
    }

    [Fact]
    public async Task RemoveAsync_WaitingStepInMiddle_RenumbersLaterStepsAndKeepsOpenStep()
    {
        SeedRoadmap();

        var roadmap = await _sut.RemoveAsync(Owner, Child, 3);

        Assert.Equal(10, roadmap.Open!.StoryId);
        Assert.Equal([(11, 1), (13, 2)], roadmap.Waiting.Select(item => (item.StoryId, item.Position)).ToArray());
        Assert.Equal(ChildRoadmapItemStatus.Removed, Item(3).Status);
    }

    [Fact]
    public async Task RemoveAsync_AuditWriteFails_RollsBackAndRethrows()
    {
        SeedRoadmap();
        FailAudit();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RemoveAsync(Owner, Child, 2));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- ReorderAsync ----

    [Fact]
    public async Task ReorderAsync_DuplicateItemIds_ThrowsBadRequest()
    {
        SeedRoadmap();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ReorderAsync(Owner, Child, new ReorderRoadmapRequestDto { ItemIds = [2, 2, 3] }));

        Assert.Equal("Danh sách phải gồm đúng các truyện đang chờ, mỗi truyện một lần; truyện đang mở không kéo thả được.", error.Message);
    }

    [Fact]
    public async Task ReorderAsync_IncludesOpenStep_ThrowsBadRequest()
    {
        SeedRoadmap();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ReorderAsync(Owner, Child, new ReorderRoadmapRequestDto { ItemIds = [1, 2, 3] }));
    }

    [Fact]
    public async Task ReorderAsync_NullListOnEmptyQueue_ReturnsRoadmapUnchanged()
    {
        var roadmap = await _sut.ReorderAsync(Owner, Child, new ReorderRoadmapRequestDto { ItemIds = null! });

        Assert.Empty(roadmap.Waiting);
        Assert.Equal(0, roadmap.TotalCount);
    }

    [Fact]
    public async Task ReorderAsync_AuditWriteFails_RollsBackAndRethrows()
    {
        SeedRoadmap();
        FailAudit();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ReorderAsync(Owner, Child, new ReorderRoadmapRequestDto { ItemIds = [4, 3, 2] }));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- ClearAsync ----

    [Fact]
    public async Task ClearAsync_NoActiveSteps_ReturnsWithoutOpeningTransaction()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Completed);

        var roadmap = await _sut.ClearAsync(Owner, Child);

        Assert.Equal(1, roadmap.CompletedCount);
        _h.UnitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _h.Audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClearAsync_AuditWriteFails_RollsBackAndRethrows()
    {
        SeedRoadmap();
        FailAudit();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ClearAsync(Owner, Child));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- AddForWishAsync ----

    [Fact]
    public async Task AddForWishAsync_SuspendedChild_ThrowsConflict()
    {
        _h.Children.Single(child => child.Id == Child).Status = ChildProfileStatus.Suspended;

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.AddForWishAsync(Owner, Child, 10));

        Assert.Equal("Hồ sơ của bé đang không hoạt động nên chưa thêm được truyện.", error.Message);
    }

    // ---- helpers ----

    private static AddRoadmapStoryRequestDto Add(int storyId, params int[] children) =>
        new() { StoryId = storyId, ChildProfileIds = children.ToList() };

    private ChildRoadmapItem Item(int id) => _h.Items.Single(item => item.Id == id);

    private void SeedRoadmap()
    {
        _h.AddItem(1, Child, 10, ChildRoadmapItemStatus.Open);
        _h.AddItem(2, Child, 11, ChildRoadmapItemStatus.Waiting, 1);
        _h.AddItem(3, Child, 12, ChildRoadmapItemStatus.Waiting, 2);
        _h.AddItem(4, Child, 13, ChildRoadmapItemStatus.Waiting, 3);
    }

    private void FailAudit() =>
        _h.Audit.Setup(audit => audit.LogAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit down"));
}
