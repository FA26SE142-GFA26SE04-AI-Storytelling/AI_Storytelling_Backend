using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.RoadmapTemplates;

public class RoadmapLifecycleTests
{
    private readonly RoadmapHarness _h = new();
    private readonly RoadmapTemplateService _sut;

    private const int Moderator = RoadmapHarness.ModeratorId;
    private const int OtherModerator = RoadmapHarness.OtherModeratorId;

    public RoadmapLifecycleTests()
    {
        var queue = new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object);
        _sut = new RoadmapTemplateService(
            _h.UnitOfWork.Object,
            new RoadmapSubmissionChecker(_h.UnitOfWork.Object),
            queue,
            _h.Audit.Object);
    }

    private ReadingRoadmap Published(int id = 2, int createdBy = OtherModerator) =>
        _h.AddRoadmap(id, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Published, createdBy, 10, 11);

    private void Suspend(int userId) =>
        _h.Users.Single(user => user.Id == userId).Status = AccountStatus.Suspended;

    private static ArchiveRoadmapRequestDto Reason(string reason = "Chủ đề không còn dùng") =>
        new() { Reason = reason };

    [Fact]
    public async Task CloneAsync_PublishedRoadmap_CopiesMetadataAndEveryStoryIntoOwnedDraft()
    {
        var source = Published();
        source.Description = "Mô tả gốc";
        source.ReadingLevelMax = 5;
        source.DefaultDaysPerStory = 4;
        _h.RoadmapItems.Single(item => item.ReadingRoadmapId == 2 && item.StoryId == 11).Days = 3;
        _h.Stories.Single(story => story.Id == 11).Status = StoryStatus.Rejected;

        var result = await _sut.CloneAsync(Moderator, source.Id);

        var clone = _h.Roadmaps.Single(roadmap => roadmap.Id == result.RoadmapId);
        var copied = _h.RoadmapItems.Where(item => item.ReadingRoadmapId == clone.Id)
            .OrderBy(item => item.Position).ToList();
        Assert.False(result.AlreadyExisted);
        Assert.Equal(source.Id, result.SourceRoadmapId);
        Assert.Equal(RoadmapTemplateStatus.Draft, clone.Status);
        Assert.Equal(Moderator, clone.CreatedByUserId);
        Assert.Equal(source.Id, clone.ClonedFromId);
        Assert.Equal(source.Name, clone.Name);
        Assert.Equal("Mô tả gốc", clone.Description);
        Assert.Equal(5, clone.ReadingLevelMax);
        Assert.Equal(4, clone.DefaultDaysPerStory);
        Assert.Equal(new[] { 10, 11 }, copied.Select(item => item.StoryId));
        Assert.Equal(3, copied[1].Days);
        Assert.Equal(RoadmapTemplateStatus.Published, source.Status);
        _h.Audit.Verify(writer => writer.LogAsync(
            Moderator, "RoadmapTemplateCloned", nameof(ReadingRoadmap), clone.Id,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(RoadmapTemplateStatus.Draft)]
    [InlineData(RoadmapTemplateStatus.PendingReview)]
    public async Task CloneAsync_OpenCloneAlreadyExists_ReturnsIt(RoadmapTemplateStatus status)
    {
        Published();
        var existing = _h.AddRoadmap(3, AgeBand.Age_6_8, 5, status, OtherModerator, 10);
        existing.ClonedFromId = 2;

        var result = await _sut.CloneAsync(Moderator, 2);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(existing.Id, result.RoadmapId);
        Assert.Equal(2, _h.Roadmaps.Count);
        _h.UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CloneAsync_OrphanDraftIsAllowed_ButActiveDraftIsConflict()
    {
        _h.AddRoadmap(2, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, OtherModerator, 10, 11);
        await Assert.ThrowsAsync<ConflictException>(() => _sut.CloneAsync(Moderator, 2));

        Suspend(OtherModerator);
        var result = await _sut.CloneAsync(Moderator, 2);

        Assert.Equal(2, _h.Roadmaps.Single(roadmap => roadmap.Id == result.RoadmapId).ClonedFromId);
        Assert.Equal(RoadmapTemplateStatus.Draft, _h.Roadmaps.Single(roadmap => roadmap.Id == 2).Status);
    }

    [Theory]
    [InlineData(RoadmapHarness.AdminId)]
    [InlineData(RoadmapHarness.ParentId)]
    public async Task CloneAsync_NonModeratorIsForbidden(int actorId)
    {
        Published();
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CloneAsync(actorId, 2));
    }

    [Fact]
    public async Task CloneAsync_AuditFailureRollsBack()
    {
        Published();
        _h.Audit.Setup(writer => writer.LogAsync(
                It.IsAny<int?>(), "RoadmapTemplateCloned", It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CloneAsync(Moderator, 2));

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ArchiveAsync_PublishedRoadmap_StoresTrimmedManualReasonAndKeepsContent()
    {
        var roadmap = Published();
        var publishedAt = roadmap.PublishedAt;

        var detail = await _sut.ArchiveAsync(Moderator, 2, Reason("  Chủ đề không còn dùng  "));

        Assert.Equal(RoadmapTemplateStatus.Archived, roadmap.Status);
        Assert.Equal(RoadmapArchiveReason.Manual, roadmap.ArchivedReason);
        Assert.Equal(Moderator, roadmap.ArchivedByUserId);
        Assert.Equal("Chủ đề không còn dùng", roadmap.ArchiveNote);
        Assert.Equal(publishedAt, roadmap.PublishedAt);
        Assert.Equal(2, _h.RoadmapItems.Count(item => item.ReadingRoadmapId == 2));
        Assert.Equal("Archived", detail.Status);
        _h.Audit.Verify(writer => writer.LogAsync(
            Moderator, "RoadmapTemplateArchived", nameof(ReadingRoadmap), 2,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public async Task ArchiveAsync_InvalidReasonIsBadRequest(string reason)
    {
        var roadmap = Published();
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ArchiveAsync(Moderator, 2, Reason(reason)));
        Assert.Equal(RoadmapTemplateStatus.Published, roadmap.Status);
    }

    [Fact]
    public async Task ArchiveAsync_RejectsNonPublishedAndReasonOverLimit()
    {
        _h.AddRoadmap(2, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, OtherModerator, 10);
        await Assert.ThrowsAsync<ConflictException>(() => _sut.ArchiveAsync(Moderator, 2, Reason()));

        _h.Roadmaps.Clear();
        Published();
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ArchiveAsync(Moderator, 2, Reason(new string('a', 501))));
    }

    [Fact]
    public async Task ListAsync_OrphanedReturnsOnlyInactiveModeratorDraftsWithOpenClone()
    {
        _h.AddRoadmap(2, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, OtherModerator, 10);
        _h.AddRoadmap(3, AgeBand.Age_9_10, 6, RoadmapTemplateStatus.Draft, Moderator, 12);
        Suspend(OtherModerator);
        var clone = await _sut.CloneAsync(Moderator, 2);

        var result = await _sut.ListAsync(
            Moderator, new RoadmapListQueryDto { Group = RoadmapListGroup.Orphaned });

        var item = Assert.Single(result.Items);
        Assert.Equal(2, item.Id);
        Assert.False(item.IsMine);
        Assert.Equal(clone.RoadmapId, item.OpenCloneId);
    }

    [Fact]
    public async Task ListAsync_PublishedShowsOpenClone_OtherGroupsDoNot()
    {
        Published();
        var clone = _h.AddRoadmap(3, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.PendingReview, Moderator, 10);
        clone.ClonedFromId = 2;

        var published = await _sut.ListAsync(
            Moderator, new RoadmapListQueryDto { Group = RoadmapListGroup.Published });
        var pending = await _sut.ListAsync(
            Moderator, new RoadmapListQueryDto { Group = RoadmapListGroup.Pending });

        Assert.Equal(3, Assert.Single(published.Items).OpenCloneId);
        Assert.Null(Assert.Single(pending.Items).OpenCloneId);
    }

    [Fact]
    public async Task GetAsync_OtherStaffCanViewDraftButParentCannot()
    {
        _h.AddRoadmap(2, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, OtherModerator, 10);

        Assert.False((await _sut.GetAsync(Moderator, 2)).IsMine);
        Assert.False((await _sut.GetAsync(RoadmapHarness.AdminId, 2)).IsMine);
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetAsync(RoadmapHarness.ParentId, 2));
    }

    [Fact]
    public async Task SubmitAsync_CloneOfOrphanDraftArchivesSourceInSameTransaction()
    {
        var source = _h.AddRoadmap(
            2, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, OtherModerator, 10, 11);
        Suspend(OtherModerator);
        var cloneId = (await _sut.CloneAsync(Moderator, 2)).RoadmapId;

        var result = await _sut.SubmitAsync(Moderator, cloneId);

        Assert.Equal("PendingReview", result.Status);
        Assert.Equal(RoadmapTemplateStatus.Archived, source.Status);
        Assert.Equal(RoadmapArchiveReason.ReplacedByClone, source.ArchivedReason);
        Assert.Equal(Moderator, source.ArchivedByUserId);
        Assert.Equal(2, _h.RoadmapItems.Count(item => item.ReadingRoadmapId == source.Id));
        _h.Audit.Verify(writer => writer.LogAsync(
            Moderator, "RoadmapTemplateArchived", nameof(ReadingRoadmap), source.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_CloneOfPublishedRoadmapLeavesSourcePublished()
    {
        var source = Published();
        var cloneId = (await _sut.CloneAsync(Moderator, 2)).RoadmapId;

        await _sut.SubmitAsync(Moderator, cloneId);

        Assert.Equal(RoadmapTemplateStatus.Published, source.Status);
        Assert.Null(source.ArchivedAt);
    }

    [Fact]
    public async Task SubmitAsync_FailingChecksLeavesOrphanSourceDraft()
    {
        var source = _h.AddRoadmap(
            2, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, OtherModerator, 10, 11);
        Suspend(OtherModerator);
        var cloneId = (await _sut.CloneAsync(Moderator, 2)).RoadmapId;
        _h.Stories.Single(story => story.Id == 11).IsPublished = false;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(Moderator, cloneId));

        Assert.Equal(RoadmapTemplateStatus.Draft, source.Status);
    }
}
