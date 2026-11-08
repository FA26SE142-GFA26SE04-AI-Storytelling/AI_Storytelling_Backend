using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.Moderation;
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
        _h.ContentCategories.Add(new ContentCategory { Id = 5, Code = "friendship", DisplayName = "Tình bạn", IsActive = true });
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
        _sut.SubmitAsync(actorId, new SubmitPublicStoryRequestDto { StoryId = StoryId, StoryVersionId = versionId, ContentCategoryIds = [5] });

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
        _h.Versions[0].Moderation.ReviewStatus = returnedAs == ModerationItemStatus.Rejected
            ? VersionReviewStatus.Rejected
            : VersionReviewStatus.ChangesRequested;
        AddItem(90, returnedAs, V1, DateTime.UtcNow.AddHours(-1));
        return _h.CloneVersion(V1, 51);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubmitAsync_WithoutACover_IsRejectedAndNothingIsQueued(string? cover)
    {
        _h.Stories.Single().CoverImageUrl = cover;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => SubmitAsync());

        Assert.Contains("ảnh bìa", error.Message);
        Assert.Empty(_h.Items);
        Assert.Equal(VersionReviewStatus.None, _h.Versions[0].Moderation.ReviewStatus);
    }

    [Fact]
    public async Task SubmitAsync_FirstTime_CreatesPendingItemAndMarksVersionPending()
    {
        var result = await SubmitAsync();

        var item = Assert.Single(_h.Items);
        Assert.Equal("Pending", result.Status);
        Assert.False(result.IsResubmission);
        Assert.Equal(ModerationItemStatus.Pending, item.Status);
        Assert.Equal("[5]", item.ProposedContentCategoryIds);
        Assert.Empty(_h.StoryCategories);
        Assert.Equal(Creator, item.CreatedByUserId);
        Assert.Equal(VersionReviewStatus.Pending, _h.Versions[0].Moderation.ReviewStatus);
        Assert.NotNull(_h.Versions[0].Moderation.ReviewSubmittedAt);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.Audit.Verify(audit => audit.LogAsync(Creator, "PublicStorySubmitted", nameof(ModerationItem), item.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ModerationZone.Safe, false, true)]
    [InlineData(ModerationZone.GrayZone, false, true)]
    [InlineData(ModerationZone.HardBlock, false, false)]
    [InlineData(ModerationZone.Safe, true, false)]
    public async Task SubmitAsync_RequiresCurrentAssessmentAndPreservesEvidence(ModerationZone zone, bool changeSource, bool allowed)
    {
        var story = _h.Stories[0]; var version = _h.Versions[0];
        var evaluator = new Mock<IGrayZoneEvaluator>();
        evaluator.SetupGet(x => x.IsConfigured).Returns(true);
        evaluator.Setup(x => x.CanReuseAsync(It.IsAny<GrayZoneAssessment>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var scan = await new CreatorSafetyCheckService(_h.UnitOfWork.Object, evaluator.Object).BuildScanAsync(story, version, default);
        var assessment = new GrayZoneAssessment(zone, GrayZoneEvaluator.Fingerprint(scan), scan.Phase, "policy", "fixture", "model", 40,
            [new("paragraph", "paragraph:0", "context", "Review context", 0, version.Content!.Length)
                { EvidenceQuote = "Giữa truyện.", ContextNote = "Evidence for submitted version" }])
            { AgeBand = story.AgeBand, Language = story.Language, AudienceRevision = story.AudienceRevision };
        await new GrayZoneWorkflowService(_h.UnitOfWork.Object, _h.Audit.Object).RecordAsync(story.Id, version.Id, null, assessment, true, default);
        var snapshot = version.Moderation.GrayZoneFlags;
        if (changeSource) version.Content += " Changed.";
        var queue = new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object);
        var submission = new PublicStorySubmissionService(_h.UnitOfWork.Object, queue, _readiness.Object, _h.Audit.Object, evaluator.Object);
        if (!allowed)
        {
            await Assert.ThrowsAsync<ConflictException>(() => submission.SubmitAsync(Creator, new() { StoryId = StoryId, StoryVersionId = V1, ContentCategoryIds = [5] }));
            Assert.Empty(_h.Items);
            return;
        }
        var result = await submission.SubmitAsync(Creator, new() { StoryId = StoryId, StoryVersionId = V1, ContentCategoryIds = [5] });
        Assert.Equal(ModerationItemType.PublicStoryReview, Assert.Single(_h.Items).ItemType);
        await queue.ClaimAsync(PublicStoryReviewHarness.ModeratorId, result.ItemId);
        var package = await _h.Sut.GetPackageAsync(PublicStoryReviewHarness.ModeratorId, result.ItemId);
        Assert.Equal(V1, package.StoryVersionId);
        if (zone == ModerationZone.GrayZone) Assert.Equal("Giữa truyện.", Assert.Single(package.Flags).EvidenceQuote);
        Assert.Equal(snapshot, version.Moderation.GrayZoneFlags);
        evaluator.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WithoutLabels_IsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(Creator,
            new SubmitPublicStoryRequestDto { StoryId = StoryId, StoryVersionId = V1 }));
        Assert.Empty(_h.Items);
    }

    [Fact]
    public async Task SubmitAsync_MetadataOnly_ChangedLabelsCanReuseVersion()
    {
        _h.ContentCategories.Add(new ContentCategory { Id = 6, Code = "animals", DisplayName = "Động vật", IsActive = true });
        _h.Versions[0].Moderation.ReviewStatus = VersionReviewStatus.ChangesRequested;
        var previous = AddItem(90, ModerationItemStatus.ChangesRequested, V1, DateTime.UtcNow.AddMinutes(-1));
        previous.ProposedContentCategoryIds = "[5]";
        previous.Feedback = "{\"parts\":[{\"partType\":\"metadata\"}]}";

        var result = await _sut.SubmitAsync(Creator, new SubmitPublicStoryRequestDto
            { StoryId = StoryId, StoryVersionId = V1, ContentCategoryIds = [6, 6] });

        Assert.True(result.IsResubmission);
        Assert.Equal(90, result.PreviousItemId);
        Assert.Equal("[6]", _h.Items.Single(x => x.Id == result.ItemId).ProposedContentCategoryIds);
        Assert.Empty(_h.StoryCategories);
    }

    [Fact]
    public async Task SubmitAsync_MetadataOnly_UnchangedLabelsAreRejected()
    {
        _h.Versions[0].Moderation.ReviewStatus = VersionReviewStatus.ChangesRequested;
        var previous = AddItem(90, ModerationItemStatus.ChangesRequested, V1, DateTime.UtcNow.AddMinutes(-1));
        previous.ProposedContentCategoryIds = "[5]";
        previous.Feedback = "{\"parts\":[{\"partType\":\"metadata\"}]}";

        var error = await Assert.ThrowsAsync<BadRequestException>(() => SubmitAsync());
        Assert.Contains("Bộ nhãn chưa thay đổi", error.Message);
    }

    [Theory]
    [InlineData("{\"parts\":[{\"partType\":\"metadata\"},{\"partType\":\"scene_text\"}]}", ModerationItemStatus.ChangesRequested)]
    [InlineData("{\"parts\":[{\"partType\":\"metadata\"}]}", ModerationItemStatus.Rejected)]
    public async Task SubmitAsync_SameVersion_RequiresMetadataOnlyChangesRequest(string feedback, ModerationItemStatus status)
    {
        _h.ContentCategories.Add(new ContentCategory { Id = 6, Code = "animals", IsActive = true });
        _h.Versions[0].Moderation.ReviewStatus = status == ModerationItemStatus.Rejected
            ? VersionReviewStatus.Rejected : VersionReviewStatus.ChangesRequested;
        var previous = AddItem(90, status, V1, DateTime.UtcNow.AddMinutes(-1));
        previous.ProposedContentCategoryIds = "[5]";
        previous.Feedback = feedback;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(Creator,
            new SubmitPublicStoryRequestDto { StoryId = StoryId, StoryVersionId = V1, ContentCategoryIds = [6] }));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(999)]
    public async Task SubmitAsync_InactiveOrUnknownLabel_IsRejected(int id)
    {
        _h.ContentCategories.Add(new ContentCategory { Id = 6, Code = "old", IsActive = false });
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(Creator,
            new SubmitPublicStoryRequestDto { StoryId = StoryId, StoryVersionId = V1, ContentCategoryIds = [id] }));
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
        _h.Versions[0].Moderation.ReviewStatus = status;

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
        _h.Versions[0].Moderation.ReviewStatus = VersionReviewStatus.Pending;
        _h.Versions[0].Moderation.ReviewSubmittedAt = DateTime.UtcNow.AddHours(-1);

        var result = await _sut.WithdrawAsync(Creator, item.Id);

        Assert.Equal("Withdrawn", result.Status);
        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
        Assert.NotNull(item.WithdrawnAt);
        Assert.Equal(1, item.RowVersion);
        Assert.Equal(VersionReviewStatus.None, _h.Versions[0].Moderation.ReviewStatus);
        Assert.Null(_h.Versions[0].Moderation.ReviewSubmittedAt);
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

    [Fact]
    public async Task PendingWithdrawalAndResubmissionPreserveVersionContentAndMedia()
    {
        var first = await SubmitAsync();
        var version = _h.Versions[0];
        var content = version.Content;
        var media = System.Text.Json.JsonSerializer.Serialize(version.MediaState);
        var assets = System.Text.Json.JsonSerializer.Serialize(_h.Assets);
        var versions = _h.Versions.Count;
        await _sut.WithdrawAsync(Creator, first.ItemId);
        var publication = new CreatorPublicationService(_h.UnitOfWork.Object,
            new ModerationQueueService(_h.UnitOfWork.Object, _h.Audit.Object), _h.Audit.Object);
        Assert.True((await publication.GetStatusAsync(Creator, StoryId)).CanResubmitWithdrawn);
        await Assert.ThrowsAsync<ConflictException>(() => StoryPlatform.Application.Common.CreatorPublicationGuard.EnsureWritableAsync(
            _h.UnitOfWork.Object, StoryId, V1, default));
        var second = await SubmitAsync();
        Assert.NotEqual(first.ItemId, second.ItemId);
        Assert.Equal(V1, second.StoryVersionId);
        Assert.True(second.IsResubmission);
        Assert.Equal(first.ItemId, second.PreviousItemId);
        Assert.Equal(ModerationItemStatus.Withdrawn, _h.Items.Single(x => x.Id == first.ItemId).Status);
        Assert.Equal(ModerationItemStatus.Pending, _h.Items.Single(x => x.Id == second.ItemId).Status);
        Assert.False((await publication.GetStatusAsync(Creator, StoryId)).CanResubmitWithdrawn);
        await Assert.ThrowsAsync<ConflictException>(() => SubmitAsync());
        // Repeated withdrawals must not force version/media regeneration either.
        await _sut.WithdrawAsync(Creator, second.ItemId);
        var third = await SubmitAsync();
        Assert.Equal(second.ItemId, third.PreviousItemId);
        Assert.Equal(V1, third.StoryVersionId);
        Assert.Equal(content, version.Content);
        Assert.Equal(media, System.Text.Json.JsonSerializer.Serialize(version.MediaState));
        Assert.Equal(assets, System.Text.Json.JsonSerializer.Serialize(_h.Assets));
        Assert.Equal(versions, _h.Versions.Count);
        Assert.Empty(_h.Jobs);
    }

    [Theory]
    [InlineData(ModerationItemStatus.Rejected)]
    [InlineData(ModerationItemStatus.Approved)]
    [InlineData(ModerationItemStatus.ChangesRequested)]
    public async Task WithdrawalCannotBypassAnExistingDecision(ModerationItemStatus decision)
    {
        AddItem(80, decision, decidedAt: DateTime.UtcNow.AddHours(-1));
        var withdrawn = AddItem(81, ModerationItemStatus.Withdrawn);
        withdrawn.WithdrawnAt = DateTime.UtcNow;
        Assert.Null(await WithdrawnPublicStorySubmission.FindReusableAsync(_h.UnitOfWork.Object, _h.Stories[0], _h.Versions[0], default));
        await Assert.ThrowsAsync<ConflictException>(() => SubmitAsync());
    }

    [Fact]
    public async Task WithdrawalRechecksPendingAfterModeratorClaims()
    {
        var first = await SubmitAsync();
        var item = _h.Items.Single();
        Mock.Get(_h.UnitOfWork.Object.Repository<ModerationItem>())
            .Setup(x => x.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { item.Status = ModerationItemStatus.Claimed; item.ClaimedByUserId = PublicStoryReviewHarness.ModeratorId; return item; });
        await Assert.ThrowsAsync<ConflictException>(() => _sut.WithdrawAsync(Creator, first.ItemId));
        Assert.Null(item.WithdrawnAt);
        Assert.Equal(ModerationItemStatus.Claimed, item.Status);
        Assert.Equal(VersionReviewStatus.Pending, _h.Versions[0].Moderation.ReviewStatus);
    }
}
