using Moq;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
namespace StoryPlatform.UnitTests.Features.Moderation;

public sealed class CreatorPublicationServiceTests
{
    private readonly PublicStoryReviewHarness h = new();
    private readonly CreatorPublicationService service;
    private readonly ModerationQueueService queue;
    private const int Creator = PublicStoryReviewHarness.CreatorId, Moderator = PublicStoryReviewHarness.ModeratorId;
    private const int StoryId = PublicStoryReviewHarness.StoryId, VersionId = PublicStoryReviewHarness.VersionId;
    public CreatorPublicationServiceTests()
    {
        h.Items.Clear(); h.Stories[0].IsPublished = true; h.Versions[0].Moderation.ReviewStatus = VersionReviewStatus.Approved;
        h.Versions[0].Moderation.ReviewSubmittedAt = DateTime.UtcNow.AddDays(-1);
        queue = new(h.UnitOfWork.Object, h.Audit.Object);
        service = new(h.UnitOfWork.Object, queue, h.Audit.Object);
    }
    private Task<CreatorPublicationResult> Request() => service.RequestRemovalAsync(Creator, StoryId, new() { Reason = "Chá»‰nh sá»­a ná»™i dung" });

    [Fact]
    public async Task RemovalRequest_DoesNotUnlockUntilClaimedModeratorApproves_ThenClonesVersion()
    {
        var old = h.Versions[0];
        h.Vocabulary.Add(new() { Id = 1, StoryVersionId = old.Id, Term = "rÃ¹a", Definition = "con váº­t" });
        var result = await Request();
        Assert.True(h.Stories[0].IsPublished);
        var pending = await service.GetStatusAsync(Creator, StoryId);
        Assert.Equal("PublicStoryRemoval", pending.OpenItemType);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateRevisionAsync(Creator, StoryId, new() { SourceVersionId = VersionId }));
        await Assert.ThrowsAsync<ConflictException>(() => CreatorPublicationGuard.EnsureWritableAsync(h.UnitOfWork.Object, StoryId, VersionId, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DecideRemovalAsync(Creator, result.ItemId!.Value, new() { Approve = true, Reason = "Tá»± gá»¡" }));
        var claim = await queue.ClaimAsync(Moderator, result.ItemId!.Value);
        await service.DecideRemovalAsync(Moderator, result.ItemId.Value, new() { Approve = true, Reason = "Cho phÃ©p gá»¡ Ä‘á»ƒ chá»‰nh sá»­a", RowVersion = claim.RowVersion });
        Assert.False(h.Stories[0].IsPublished);
        // Approval removes the public story, but never unlocks the historical version itself.
        await Assert.ThrowsAsync<ConflictException>(() => CreatorPublicationGuard.EnsureWritableAsync(h.UnitOfWork.Object, StoryId, VersionId, default));
        var revision = await service.CreateRevisionAsync(Creator, StoryId, new() { SourceVersionId = VersionId });
        Assert.NotEqual(old.Id, revision.VersionId);
        Assert.Equal("ContentReview", revision.Status);
        Assert.False(old.IsCurrent);
        Assert.Equal(VersionReviewStatus.Approved, old.Moderation.ReviewStatus);
        var current = h.Versions.Single(x => x.IsCurrent);
        Assert.Equal(VersionReviewStatus.None, current.Moderation.ReviewStatus);
        Assert.Null(current.Moderation.ReviewSubmittedAt);
        Assert.Equal(old.Id, current.BaseVersionId);
        Assert.Equal(old.Content, current.Content);
        Assert.Equal(2, h.Vocabulary.Count);
        Assert.Empty(h.Assets.Where(x => x.StoryVersionId == current.Id));
        await CreatorPublicationGuard.EnsureWritableAsync(h.UnitOfWork.Object, StoryId, current.Id, default);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateRevisionAsync(Creator, StoryId, new() { SourceVersionId = old.Id }));
    }

    [Fact]
    public async Task RejectedRemoval_KeepsPublishedAndLocked()
    {
        var result = await Request();
        var claim = await queue.ClaimAsync(Moderator, result.ItemId!.Value);
        await service.DecideRemovalAsync(Moderator, result.ItemId.Value, new() { Approve = false, Reason = "ChÆ°a cháº¥p thuáº­n", RowVersion = claim.RowVersion });
        Assert.True(h.Stories[0].IsPublished);
        Assert.Null(h.Items[0].RemovedAt);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateRevisionAsync(Creator, StoryId, new() { SourceVersionId = VersionId }));
    }

    [Theory]
    [InlineData(ModerationItemStatus.Withdrawn, VersionReviewStatus.None)]
    [InlineData(ModerationItemStatus.ChangesRequested, VersionReviewStatus.ChangesRequested)]
    [InlineData(ModerationItemStatus.Rejected, VersionReviewStatus.Rejected)]
    public async Task ReturnedOrWithdrawnVersion_StaysImmutable_RevisionIsEditable(ModerationItemStatus status, VersionReviewStatus review)
    {
        h.Stories[0].IsPublished = false; h.Versions[0].Moderation.ReviewStatus = review;
        h.Items.Add(new()
        {
            Id = 1,
            ItemType = ModerationItemType.PublicStoryReview,
            StoryId = StoryId,
            StoryVersionId = VersionId,
            Status = status,
            CreatedByUserId = Creator
        });
        await Assert.ThrowsAsync<ConflictException>(() => CreatorPublicationGuard.EnsureWritableAsync(h.UnitOfWork.Object, StoryId, VersionId, default));
        var revision = await service.CreateRevisionAsync(Creator, StoryId, new() { SourceVersionId = VersionId });
        await CreatorPublicationGuard.EnsureWritableAsync(h.UnitOfWork.Object, StoryId, revision.VersionId, default);
        Assert.Equal(review, h.Versions.Single(x => x.Id == VersionId).Moderation.ReviewStatus);
    }

    [Theory]
    [InlineData(ModerationItemStatus.Pending)]
    [InlineData(ModerationItemStatus.Claimed)]
    public async Task OpenSubmission_BlocksRevisionAndAllArtifactUpdates(ModerationItemStatus status)
    {
        h.Stories[0].IsPublished = false; h.Versions[0].Moderation.ReviewStatus = VersionReviewStatus.Pending;
        h.Items.Add(new() { Id = 1, ItemType = ModerationItemType.PublicStoryReview, StoryId = StoryId, StoryVersionId = VersionId, Status = status });
        var review = new StoryReviewService(h.UnitOfWork.Object, new Mock<IAIStoryGenerationClient>(MockBehavior.Strict).Object, new InMemoryProposalCache());
        await Assert.ThrowsAsync<ConflictException>(() => review.UpdateVocabularyAsync(Creator, StoryId, new() { VersionId = VersionId }));
        await Assert.ThrowsAsync<ConflictException>(() => review.UpdateQuizAsync(Creator, StoryId, new() { VersionId = VersionId }));
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateRevisionAsync(Creator, StoryId, new() { SourceVersionId = VersionId }));
        Assert.Single(h.Versions);
    }

    [Fact]
    public async Task RemovalDecision_RequiresCurrentClaimRevisionAndOwnership()
    {
        var result = await Request();
        var claim = await queue.ClaimAsync(Moderator, result.ItemId!.Value);
        await Assert.ThrowsAsync<ConflictException>(() => service.DecideRemovalAsync(Moderator, result.ItemId.Value,
            new() { Approve = true, Reason = "Gá»¡", RowVersion = claim.RowVersion - 1 }));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateRevisionAsync(PublicStoryReviewHarness.OtherModeratorId, StoryId,
            new() { SourceVersionId = VersionId }));
    }
}
