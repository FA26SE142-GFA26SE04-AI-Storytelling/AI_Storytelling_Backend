using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class PublicStorySubmissionServiceTests
{
    private readonly PublicStoryReviewHarness _h = new();
    private readonly Mock<IMediaReadinessService> _readiness = new();
    private readonly PublicStorySubmissionService _sut;

    private const int Creator = PublicStoryReviewHarness.CreatorId;
    private const int StoryId = PublicStoryReviewHarness.StoryId;
    private const int V1 = PublicStoryReviewHarness.VersionId;

    public PublicStorySubmissionServiceTests()
    {
        _h.Items.Clear();
        _h.AddScene(1, 1);
        SetMediaReady(true);
        var queue = new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object);
        _sut = new PublicStorySubmissionService(_h.UnitOfWork.Object, queue, _readiness.Object, _h.Audit.Object);
    }

    private void SetMediaReady(bool ready) =>
        _readiness.Setup(service => service.CheckAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaReadinessResult(ready, 1, 1, 1, 1,
                Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>()));

    private Task<PublicStorySubmissionResultDto> SubmitAsync(int versionId = V1, int actorId = Creator) =>
        _sut.SubmitAsync(actorId, new SubmitPublicStoryRequestDto { StoryId = StoryId, StoryVersionId = versionId });

    private ModerationItem AddItem(int id, ModerationItemStatus status, int versionId = V1, DateTime? decidedAt = null)
    {
        var item = new ModerationItem
        {
            Id = id,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = status,
            StoryId = StoryId,
            StoryVersionId = versionId,
            CreatedByUserId = Creator,
            DecidedAt = decidedAt,
            DecidedByUserId = decidedAt.HasValue ? PublicStoryReviewHarness.ModeratorId : null
        };
        _h.Items.Add(item);
        return item;
    }

    private StoryVersion GivenReturnedV1AndIdenticalV2(ModerationItemStatus returnedAs)
    {
        _h.Versions[0].ReviewStatus = returnedAs == ModerationItemStatus.Rejected
            ? VersionReviewStatus.Rejected
            : VersionReviewStatus.ChangesRequested;
        AddItem(90, returnedAs, V1, DateTime.UtcNow.AddHours(-1));
        return _h.CloneVersion(V1, 51);
    }

    [Fact]
    public async Task SubmitAsync_FirstTime_CreatesPendingItemAndMarksVersionPending()
    {
        var result = await SubmitAsync();

        var item = Assert.Single(_h.Items);
        Assert.Equal("Pending", result.Status);
        Assert.False(result.IsResubmission);
        Assert.Equal(ModerationItemStatus.Pending, item.Status);
        Assert.Equal(Creator, item.CreatedByUserId);
        Assert.Equal(VersionReviewStatus.Pending, _h.Versions[0].ReviewStatus);
        Assert.NotNull(_h.Versions[0].ReviewSubmittedAt);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.Audit.Verify(audit => audit.LogAsync(Creator, "PublicStorySubmitted", nameof(ModerationItem), item.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ModerationItemStatus.ChangesRequested)]
    [InlineData(ModerationItemStatus.Rejected)]
    public async Task SubmitAsync_ChangedCopy_IsLinkedResubmission(ModerationItemStatus returnedAs)
    {
        var version = GivenReturnedV1AndIdenticalV2(returnedAs);
        version.Content += " Một câu mới.";

        var result = await SubmitAsync(version.Id);

        Assert.True(result.IsResubmission);
        Assert.Equal(90, result.PreviousItemId);
        Assert.Contains(_h.Items, item => item.Id == result.ItemId && item.IsResubmission && item.PreviousItemId == 90);
    }

    [Fact]
    public async Task SubmitAsync_IdenticalResubmission_IsRejected()
    {
        var version = GivenReturnedV1AndIdenticalV2(ModerationItemStatus.ChangesRequested);

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => SubmitAsync(version.Id));

        Assert.Contains("y nguyên", exception.Message);
        Assert.DoesNotContain(_h.Items, item => item.StoryVersionId == version.Id);
    }

    [Fact]
    public async Task SubmitAsync_MediaOnlyChange_IsAccepted()
    {
        var version = GivenReturnedV1AndIdenticalV2(ModerationItemStatus.Rejected);
        _h.Assets.First(asset => asset.StoryVersionId == version.Id && asset.Type == MediaType.Illustration).Url = "new-image";

        Assert.True((await SubmitAsync(version.Id)).IsResubmission);
    }

    [Theory]
    [InlineData(VersionReviewStatus.Pending)]
    [InlineData(VersionReviewStatus.Approved)]
    [InlineData(VersionReviewStatus.ChangesRequested)]
    [InlineData(VersionReviewStatus.Rejected)]
    public async Task SubmitAsync_VersionAlreadyInReviewLifecycle_IsRejected(VersionReviewStatus status)
    {
        _h.Versions[0].ReviewStatus = status;

        await Assert.ThrowsAnyAsync<AppException>(() => SubmitAsync());
    }

    [Fact]
    public async Task SubmitAsync_InvalidActorOwnershipScopeAndReadiness_AreRejected()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SubmitAsync(actorId: PublicStoryReviewHarness.ModeratorId));

        _h.Stories[0].AuthorUserId = 999;
        await Assert.ThrowsAsync<ForbiddenException>(() => SubmitAsync());
        _h.Stories[0].AuthorUserId = Creator;

        _h.Stories[0].Scope = StoryScope.Parent;
        await Assert.ThrowsAsync<NotFoundException>(() => SubmitAsync());
        _h.Stories[0].Scope = StoryScope.Creator;

        SetMediaReady(false);
        await Assert.ThrowsAsync<BadRequestException>(() => SubmitAsync());
    }

    [Fact]
    public async Task SubmitAsync_AuditFailure_RollsBack()
    {
        _h.Audit.Setup(audit => audit.LogAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SubmitAsync());

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithdrawAsync_PendingItem_MarksWithdrawnAndMakesVersionSubmittable()
    {
        var item = AddItem(70, ModerationItemStatus.Pending);
        _h.Versions[0].ReviewStatus = VersionReviewStatus.Pending;
        _h.Versions[0].ReviewSubmittedAt = DateTime.UtcNow.AddHours(-1);

        var result = await _sut.WithdrawAsync(Creator, item.Id);

        Assert.Equal("Withdrawn", result.Status);
        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
        Assert.NotNull(item.WithdrawnAt);
        Assert.Equal(1, item.RowVersion);
        Assert.Equal(VersionReviewStatus.None, _h.Versions[0].ReviewStatus);
        Assert.Null(_h.Versions[0].ReviewSubmittedAt);
    }

    [Fact]
    public async Task WithdrawAsync_ClaimedOrForeignItem_IsRejected()
    {
        var item = AddItem(70, ModerationItemStatus.Claimed);
        await Assert.ThrowsAsync<ConflictException>(() => _sut.WithdrawAsync(Creator, item.Id));

        item.Status = ModerationItemStatus.Pending;
        item.CreatedByUserId = 999;
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.WithdrawAsync(Creator, item.Id));
    }

    [Fact]
    public void Controller_IsRestrictedToContentCreatorAndExposesSubmitAndWithdraw()
    {
        var authorize = typeof(PublicStorySubmissionController).GetCustomAttribute<AuthorizeAttribute>();
        var methods = typeof(PublicStorySubmissionController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("ContentCreator", authorize!.Roles);
        Assert.Equal(new[] { "Submit", "Withdraw" }, methods);
    }
}
