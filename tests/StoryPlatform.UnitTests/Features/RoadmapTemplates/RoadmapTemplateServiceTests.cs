using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.RoadmapTemplates;

public class RoadmapTemplateServiceTests
{
    private readonly RoadmapHarness _h = new();
    private readonly ModerationQueueService _queue;
    private readonly RoadmapTemplateService _sut;

    private const int Moderator = RoadmapHarness.ModeratorId;
    private const int OtherModerator = RoadmapHarness.OtherModeratorId;

    public RoadmapTemplateServiceTests()
    {
        _queue = new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object);
        _sut = new RoadmapTemplateService(
            _h.UnitOfWork.Object,
            new RoadmapSubmissionChecker(_h.UnitOfWork.Object),
            _queue,
            _h.Audit.Object);
    }

    private static SaveRoadmapRequestDto ValidRequest() => new()
    {
        Name = "  Bộ tình bạn  ",
        Description = "  Học cách chia sẻ  ",
        AgeBand = "6-8",
        MainCategoryId = 5,
        ReadingLevelMin = 1,
        ReadingLevelMax = 3,
        DefaultDaysPerStory = 2
    };

    [Fact]
    public async Task CreateAsync_Valid_CreatesOwnedDraftAndReturnsChecks()
    {
        var result = await _sut.CreateAsync(Moderator, ValidRequest());

        var roadmap = Assert.Single(_h.Roadmaps);
        Assert.Equal("Bộ tình bạn", roadmap.Name);
        Assert.Equal("Học cách chia sẻ", roadmap.Description);
        Assert.Equal(RoadmapTemplateStatus.Draft, roadmap.Status);
        Assert.Equal(Moderator, roadmap.CreatedByUserId);
        Assert.Equal("6-8", result.AgeBand);
        Assert.True(result.IsMine);
        Assert.False(result.CanSubmit);
        Assert.Equal(6, result.Checks.Count);
        _h.Audit.Verify(audit => audit.LogAsync(
            Moderator, "RoadmapTemplateCreated", nameof(ReadingRoadmap), roadmap.Id,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(RoadmapHarness.AdminId)]
    [InlineData(RoadmapHarness.ParentId)]
    public async Task CreateAsync_NonModerator_IsForbidden(int actorId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateAsync(actorId, ValidRequest()));
        Assert.Empty(_h.Roadmaps);
    }

    [Fact]
    public async Task CreateAsync_ValidatesNameCategoryRangeAndDays()
    {
        var request = ValidRequest();
        request.Name = " ";
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, request));

        request = ValidRequest();
        request.MainCategoryId = 7;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, request));

        request = ValidRequest();
        request.ReadingLevelMin = 4;
        request.ReadingLevelMax = 3;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, request));

        request = ValidRequest();
        request.DefaultDaysPerStory = 31;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, request));
    }

    [Fact]
    public async Task UpdateAsync_OwnerDraft_UpdatesMetadata()
    {
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, Moderator);
        var request = ValidRequest();
        request.Name = "Bộ mới";
        request.AgeBand = "9-10";
        request.MainCategoryId = 6;

        var result = await _sut.UpdateAsync(Moderator, 1, request);

        Assert.Equal("Bộ mới", result.Name);
        Assert.Equal("9-10", result.AgeBand);
        Assert.Equal(6, result.MainCategory!.Id);
    }

    [Fact]
    public async Task UpdateAsync_RejectsOtherOwnerAndNonDraft()
    {
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, Moderator);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.UpdateAsync(OtherModerator, 1, ValidRequest()));

        _h.Roadmaps.Single().Status = RoadmapTemplateStatus.PendingReview;
        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.UpdateAsync(Moderator, 1, ValidRequest()));
    }

    [Theory]
    [InlineData(RoadmapTemplateStatus.Draft)]
    [InlineData(RoadmapTemplateStatus.PendingReview)]
    [InlineData(RoadmapTemplateStatus.Published)]
    [InlineData(RoadmapTemplateStatus.Archived)]
    public async Task GetAsync_AnotherModeratorOrAdministrator_CanSeeEveryStatus(
        RoadmapTemplateStatus status)
    {
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, status, Moderator, 10);

        var byModerator = await _sut.GetAsync(OtherModerator, 1);
        var byAdmin = await _sut.GetAsync(RoadmapHarness.AdminId, 1);

        Assert.False(byModerator.IsMine);
        Assert.False(byAdmin.IsMine);
        Assert.Equal(status.ToString(), byModerator.Status);
    }

    [Fact]
    public async Task ListCandidatesAsync_ReturnsOnlyEligibleMetadataWithFilters()
    {
        _h.StoryCategories.Add(new StoryCategory
        {
            Id = 1, StoryId = 10, ContentCategoryId = 5
        });
        _h.StoryCategories.Add(new StoryCategory
        {
            Id = 2, StoryId = 12, ContentCategoryId = 5
        });
        _h.Stories.Single(story => story.Id == 11).Status = StoryStatus.Rejected;

        var result = await _sut.ListCandidatesAsync(RoadmapHarness.AdminId, new CandidateQueryDto
        {
            AgeBand = "6-8",
            ReadingLevelMin = 2,
            ReadingLevelMax = 2,
            ContentCategoryId = 5
        });

        var story = Assert.Single(result.Items);
        Assert.Equal(10, story.StoryId);
        Assert.Equal("friendship", story.Labels.Single().Code);
    }

    [Fact]
    public async Task SetItemsAsync_ReplacesListInOrderAndPersistsDays()
    {
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, Moderator, 10);

        var result = await _sut.SetItemsAsync(Moderator, 1, new SetRoadmapItemsRequestDto
        {
            Items =
            [
                new RoadmapItemInputDto { StoryId = 11, Days = 5 },
                new RoadmapItemInputDto { StoryId = 10 }
            ]
        });

        Assert.Equal(new[] { 11, 10 }, result.Stories.Select(story => story.StoryId));
        Assert.Equal(new[] { 1, 2 }, result.Stories.Select(story => story.Position));
        Assert.Equal(5, result.Stories[0].Days);
        _h.UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetItemsAsync_RejectsDuplicateBadDaysAndIneligibleStory()
    {
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, Moderator);
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetItemsAsync(
            Moderator, 1, new SetRoadmapItemsRequestDto
            {
                Items =
                [
                    new RoadmapItemInputDto { StoryId = 10 },
                    new RoadmapItemInputDto { StoryId = 10 }
                ]
            }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetItemsAsync(
            Moderator, 1, new SetRoadmapItemsRequestDto
            {
                Items = [new RoadmapItemInputDto { StoryId = 10, Days = 31 }]
            }));

        _h.Stories.Single(story => story.Id == 10).IsPublished = false;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetItemsAsync(
            Moderator, 1, new SetRoadmapItemsRequestDto
            {
                Items = [new RoadmapItemInputDto { StoryId = 10 }]
            }));
    }

    [Fact]
    public async Task SubmitAsync_ValidRoadmap_QueuesLowPriorityItemAndHidesItFromCreator()
    {
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, Moderator, 10, 11);
        var before = DateTime.UtcNow;

        var result = await _sut.SubmitAsync(Moderator, 1);

        var item = Assert.Single(_h.Items);
        Assert.Equal("PendingReview", result.Status);
        Assert.Equal(RoadmapTemplateStatus.PendingReview, roadmap.Status);
        Assert.True(roadmap.SubmittedAt >= before);
        Assert.Equal(ModerationItemType.RoadmapTemplateReview, item.ItemType);
        Assert.Equal(ModerationPriority.Low, item.Priority);
        Assert.InRange(item.SlaDueAt!.Value, before.AddHours(71), before.AddHours(73));
        Assert.Empty((await _queue.ListAsync(Moderator, new ModerationQueueQueryDto())).Items);
        Assert.Contains((await _queue.ListAsync(OtherModerator, new ModerationQueueQueryDto())).Items,
            candidate => candidate.Id == item.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => _queue.ClaimAsync(Moderator, item.Id));
    }

    [Fact]
    public async Task SubmitAsync_FailingChecksAndExistingOpenItem_DoNotChangeRoadmap()
    {
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, Moderator);
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(Moderator, 1));
        Assert.Equal(RoadmapTemplateStatus.Draft, roadmap.Status);

        _h.RoadmapItems.Add(new ReadingRoadmapItem
        {
            Id = 100, ReadingRoadmapId = 1, StoryId = 10, Position = 1
        });
        _h.Items.Add(new ModerationItem
        {
            Id = 40,
            ItemType = ModerationItemType.RoadmapTemplateReview,
            Status = ModerationItemStatus.Pending,
            ReadingRoadmapId = 1,
            CreatedByUserId = Moderator
        });
        await Assert.ThrowsAsync<ConflictException>(() => _sut.SubmitAsync(Moderator, 1));
        Assert.Equal(RoadmapTemplateStatus.Draft, roadmap.Status);
    }

    [Fact]
    public async Task SubmitAsync_AuditFailure_RollsBackTransaction()
    {
        _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, Moderator, 10, 11);
        _h.Audit.Setup(audit => audit.LogAsync(
                It.IsAny<int?>(), "RoadmapTemplateSubmitted", It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SubmitAsync(Moderator, 1));

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithdrawAsync_PendingItem_ReturnsRoadmapToDraftAndKeepsStories()
    {
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.PendingReview, Moderator, 10, 11);
        var item = new ModerationItem
        {
            Id = 40,
            ItemType = ModerationItemType.RoadmapTemplateReview,
            Status = ModerationItemStatus.Pending,
            ReadingRoadmapId = 1,
            CreatedByUserId = Moderator
        };
        _h.Items.Add(item);

        var result = await _sut.WithdrawAsync(Moderator, 1);

        Assert.Equal("Draft", result.Status);
        Assert.Equal(RoadmapTemplateStatus.Draft, roadmap.Status);
        Assert.Null(roadmap.SubmittedAt);
        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
        Assert.NotNull(item.WithdrawnAt);
        Assert.Equal(1, item.RowVersion);
        Assert.Equal(2, _h.RoadmapItems.Count);
    }

    [Fact]
    public async Task WithdrawAsync_ClaimedOrForeignItem_IsRejected()
    {
        _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.PendingReview, Moderator, 10);
        _h.Items.Add(new ModerationItem
        {
            Id = 40,
            ItemType = ModerationItemType.RoadmapTemplateReview,
            Status = ModerationItemStatus.Claimed,
            ReadingRoadmapId = 1,
            CreatedByUserId = Moderator,
            ClaimedByUserId = OtherModerator
        });

        await Assert.ThrowsAsync<ConflictException>(() => _sut.WithdrawAsync(Moderator, 1));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.WithdrawAsync(OtherModerator, 1));
    }

    [Fact]
    public async Task ListAsync_ReturnsTheRequestedGroupsAndOnlyOwnDrafts()
    {
        _h.AddRoadmap(1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, Moderator, 10);
        _h.AddRoadmap(2, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, OtherModerator, 10);
        _h.AddRoadmap(3, AgeBand.Age_6_8, 6, RoadmapTemplateStatus.PendingReview, OtherModerator, 10, 11);
        _h.AddRoadmap(4, AgeBand.Age_9_10, 6, RoadmapTemplateStatus.Published, OtherModerator, 12);
        _h.AddRoadmap(5, AgeBand.Age_9_10, 6, RoadmapTemplateStatus.Archived, Moderator, 12);

        var drafts = await _sut.ListAsync(Moderator,
            new RoadmapListQueryDto { Group = RoadmapListGroup.Drafts });
        var pending = await _sut.ListAsync(RoadmapHarness.AdminId,
            new RoadmapListQueryDto { Group = RoadmapListGroup.Pending });
        var published = await _sut.ListAsync(Moderator,
            new RoadmapListQueryDto { Group = RoadmapListGroup.Published });
        var archived = await _sut.ListAsync(Moderator,
            new RoadmapListQueryDto { Group = RoadmapListGroup.Archived });

        Assert.Equal(1, Assert.Single(drafts.Items).Id);
        Assert.Equal(3, Assert.Single(pending.Items).Id);
        Assert.Equal(2, Assert.Single(pending.Items).StoryCount);
        Assert.Equal(4, Assert.Single(published.Items).Id);
        Assert.Equal(5, Assert.Single(archived.Items).Id);
    }

    [Fact]
    public async Task ListAsync_RejectsInvalidGroupAndNonStaff()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ListAsync(
            Moderator, new RoadmapListQueryDto { Group = (RoadmapListGroup)99 }));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(
            RoadmapHarness.ParentId, new RoadmapListQueryDto { Group = RoadmapListGroup.Published }));
    }

    [Fact]
    public void Controller_IsRestrictedToStaffAndWriteEndpointsRequireModerator()
    {
        var authorize = typeof(RoadmapTemplateController).GetCustomAttribute<AuthorizeAttribute>();
        var methods = typeof(RoadmapTemplateController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.Name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("Moderator,Administrator", authorize!.Roles);
        Assert.Equal(new[]
        {
            "Archive", "Clone", "Create", "Get", "List", "ListCandidates", "SetItems", "Submit", "Update",
            "Withdraw"
        }, methods.Select(method => method.Name));
        Assert.All(methods.Where(method => method.Name is
                "Archive" or "Clone" or "Create" or "SetItems" or "Submit" or "Update" or "Withdraw"),
            method => Assert.Equal("Moderator",
                method.GetCustomAttribute<AuthorizeAttribute>()?.Roles));
    }
}
