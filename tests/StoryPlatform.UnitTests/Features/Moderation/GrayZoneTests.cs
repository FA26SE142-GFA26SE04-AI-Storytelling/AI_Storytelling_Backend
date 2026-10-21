using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public sealed class GrayZoneTests
{
    private static readonly GrayZoneScan Scan = new("phase3", "6-8", "vi", [new("section", "content", "Na giúp bạn sửa đồ chơi.")], []);
    private static GrayZoneOptions Options() => new() { Provider = "test-only", MaximumRiskScore = 100, WarningThreshold = 30, BlockThreshold = 70 };
    private sealed class Provider(decimal risk) : IModelModerationProvider
    {
        public string Name => "test-only";
        public int Calls { get; private set; }
        public Task<ModelModerationResult> EvaluateAsync(GrayZoneScan scan, CancellationToken ct)
        { Calls++; return Task.FromResult(new ModelModerationResult(risk, "test-model", [new("section", "content", "context", "Cần đối chiếu ngữ cảnh")]) { RubricVersion = scan.RubricVersion }); }
    }

    [Theory]
    [InlineData(0, ModerationZone.Safe)]
    [InlineData(29.99, ModerationZone.Safe)]
    [InlineData(30, ModerationZone.GrayZone)]
    [InlineData(69.99, ModerationZone.GrayZone)]
    [InlineData(70, ModerationZone.HardBlock)]
    [InlineData(100, ModerationZone.HardBlock)]
    public async Task ClassifiesExactThresholds(double risk, ModerationZone expected)
    {
        var provider = new Provider((decimal)risk);
        Assert.Equal(expected, (await new GrayZoneEvaluator(Options(), [provider], RubricTestData.Source()).EvaluateAsync(Scan, default)).Zone);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData("mail test@example.com", "unused")]
    [InlineData("Gọi 0912345678", "unused")]
    [InlineData("Nội dung chứa forbidden", "forbidden")]
    public async Task HardRulesDoNotCallModel(string content, string blocked)
    {
        var provider = new Provider(0);
        var result = await new GrayZoneEvaluator(new(), [provider], RubricTestData.Source()).EvaluateAsync(
            Scan with { Parts = [new("section", "content", content)], BlockedTerms = [blocked] }, default);
        Assert.Equal(ModerationZone.HardBlock, result.Zone);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData(-1)] [InlineData(101)]
    public async Task InvalidScoreNeverBecomesSafe(int risk) => await Assert.ThrowsAsync<InvalidOperationException>(() =>
        new GrayZoneEvaluator(Options(), [new Provider(risk)], RubricTestData.Source()).EvaluateAsync(Scan, default));

    [Fact]
    public async Task MissingProviderOrThresholdFailsExplicitly()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GrayZoneEvaluator(Options(), [], RubricTestData.Source()).EvaluateAsync(Scan, default));
        var options = Options(); options.WarningThreshold = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GrayZoneEvaluator(options, [new Provider(0)], RubricTestData.Source()).EvaluateAsync(Scan, default));
    }

    [Fact]
    public async Task ProviderFailureIsNotAClassification()
    {
        var provider = new Mock<IModelModerationProvider>(); provider.SetupGet(x => x.Name).Returns("test-only");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        await Assert.ThrowsAsync<TimeoutException>(() => new GrayZoneEvaluator(Options(), [provider.Object], RubricTestData.Source()).EvaluateAsync(Scan, default));
    }

    [Fact]
    public async Task FindingsMustReferenceSubmittedPart()
    {
        var provider = new Mock<IModelModerationProvider>(); provider.SetupGet(x => x.Name).Returns("test-only");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelModerationResult(50, "test", [new("artifact", "other-version", "context", "reason")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GrayZoneEvaluator(Options(), [provider.Object], RubricTestData.Source()).EvaluateAsync(Scan, default));
    }

    [Fact]
    public async Task ChangingPolicyInvalidatesCachedAssessment()
    {
        var options = Options(); var evaluator = new GrayZoneEvaluator(options, [new Provider(50)], RubricTestData.Source());
        var result = await evaluator.EvaluateAsync(Scan, default);
        Assert.True(await evaluator.CanReuseAsync(result, default)); options.BlockThreshold = 80;
        Assert.False(await evaluator.CanReuseAsync(result, default));
    }

    [Fact]
    public void StructuredAndLegacyFlagsBothRemainReadable()
    {
        Assert.Single(PublicReviewState.ParseVersionFlags("{\"sections\":[\"ending\"],\"reason\":\"r\"}"));
        var flag = Assert.Single(PublicReviewState.ParseVersionFlags("{\"flags\":[{\"id\":\"a\",\"kind\":\"section\",\"name\":\"content\",\"reason\":\"r\"}]}"));
        Assert.Equal("a", flag.Id);
    }

    [Fact]
    public void ParagraphOffsetsReferToExactSavedText()
    {
        const string body = "Na sửa xe.\r\n\r\nNam giúp bạn.";
        var parts = GrayZoneEvaluator.TextParts("Hai người bạn", body);
        Assert.Equal(3, parts.Count);
        foreach (var part in parts.Where(x => x.Kind == "paragraph"))
            Assert.Equal(part.Text, body[part.StartOffset!.Value..part.EndOffset!.Value]);
    }

    [Fact]
    public async Task ChangedAudienceCannotPersistOldAssessment()
    {
        var (h, service) = Seed(false);
        await Assert.ThrowsAsync<ConflictException>(() => service.RecordAsync(h.Stories[0].Id, h.Versions[0].Id,
            null, Assessment() with { AgeBand = "9-10" }, true, default));
        Assert.Empty(h.Items);
        Assert.Null(h.Versions[0].ModerationZone);
    }

    [Fact]
    public async Task BlockKeepsManualVersionUnavailableAndStickyHistory()
    {
        var (h, service) = Seed(true);
        await service.RecordAsync(h.Stories[0].Id, h.Versions[0].Id, null, Assessment(), true, default);
        var item = h.Items.Single(); item.Status = ModerationItemStatus.Claimed;
        item.ClaimedByUserId = PublicStoryReviewHarness.ModeratorId; item.ClaimedAt = DateTime.UtcNow;
        await service.DecideAsync(PublicStoryReviewHarness.ModeratorId, item.Id, new("Block", "Unsafe context", item.RowVersion), default);
        Assert.Equal(ModerationItemStatus.Blocked, item.Status);
        Assert.True(GrayZoneGate.IsBlocked(h.Stories[0], h.Versions[0]));
        Assert.True(h.Stories[0].EverGrayZone);
    }

    private static (PublicStoryReviewHarness H, GrayZoneWorkflowService Service) Seed(bool parent)
    {
        var h = new PublicStoryReviewHarness(); h.Items.Clear();
        h.Stories[0].Scope = parent ? StoryScope.Parent : StoryScope.Creator;
        h.Stories[0].Status = StoryStatus.Draft;
        h.Versions[0].ReviewStatus = VersionReviewStatus.None;
        return (h, new(h.UnitOfWork.Object, h.Audit.Object));
    }

    private static GrayZoneAssessment Assessment() => new(ModerationZone.GrayZone, "0123456789abcdef0123456789abcdef", "phase3", "test-policy",
        "test-only", "model", 50, [new("section", "content", "context", "reason")]);

    [Fact]
    public async Task ParentQueuesOneItemAndClearUnblocksOnlySafety()
    {
        var (h, service) = Seed(true); var story = h.Stories[0]; var v = h.Versions[0];
        Assert.True(await service.RecordAsync(story.Id, v.Id, null, Assessment(), false, default));
        Assert.True(await service.RecordAsync(story.Id, v.Id, null, Assessment(), false, default));
        var item = Assert.Single(h.Items);
        Assert.Equal(ModerationPriority.High, item.Priority); Assert.True(story.EverGrayZone);
        Assert.True(GrayZoneGate.IsBlocked(story, v));
        item.Status = ModerationItemStatus.Claimed; item.ClaimedByUserId = PublicStoryReviewHarness.ModeratorId; item.ClaimedAt = DateTime.UtcNow;
        await service.DecideAsync(PublicStoryReviewHarness.ModeratorId, item.Id, new("Clear", "Đã xem ngữ cảnh", item.RowVersion), default);
        Assert.True(v.GrayZoneCleared); Assert.Equal(StoryStatus.Draft, story.Status);
        Assert.False(GrayZoneGate.IsBlocked(story, v)); Assert.True(story.EverGrayZone);
    }

    [Fact]
    public async Task CreatorFlagsWithoutQueueOrStoryTransition()
    {
        var (h, service) = Seed(false);
        Assert.False(await service.RecordAsync(h.Stories[0].Id, h.Versions[0].Id, null, Assessment(), true, default));
        Assert.Empty(h.Items); Assert.Equal(StoryStatus.Draft, h.Stories[0].Status);
        Assert.True(h.Stories[0].EverGrayZone); Assert.Single(PublicReviewState.ParseVersionFlags(h.Versions[0].GrayZoneFlags));
        Assert.False(GrayZoneGate.IsBlocked(h.Stories[0], h.Versions[0]));
    }

    [Fact]
    public async Task ConfirmationCheckReusesPhaseThreeAssessmentInsteadOfCreatingSecondReview()
    {
        var (h, workflow) = Seed(false); var story = h.Stories[0]; var version = h.Versions[0];
        h.ContentCategories.Clear();
        var provider = new Mock<IModelModerationProvider>(); provider.SetupGet(x => x.Name).Returns("test-only");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrayZoneScan scan, CancellationToken _) =>
            {
                var part = scan.Parts.First(x => x.Kind == "paragraph");
                return new ModelModerationResult(50, "test-model", [new(part.Kind, part.Name, "context", "Review context")]) { RubricVersion = scan.RubricVersion };
            });
        var evaluator = new GrayZoneEvaluator(Options(), [provider.Object], RubricTestData.Source());
        var scan = new GrayZoneScan("phase3", story.AgeBand, story.Language,
            GrayZoneEvaluator.TextParts(version.Title, version.Content!).Concat([new ModerationText("section", "lesson", version.Lesson!)]).ToArray(), []) { ReadingLevel = story.ReadingLevel };
        var assessment = await evaluator.EvaluateAsync(scan, default);
        await workflow.RecordAsync(story.Id, version.Id, null, assessment, true, default);
        var checks = new GrayZoneContentCheckService(h.UnitOfWork.Object, evaluator, workflow,
            new Mock<StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces.ISupervisionAccessGuard>().Object);
        var result = await checks.CheckAsync(PublicStoryReviewHarness.CreatorId, story.Id, version.Id, default);
        Assert.Equal("GrayZone", result.Status);
        provider.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublicReviewCannotApproveUntilStructuredFlagsAreResolved()
    {
        var (h, service) = Seed(false);
        await service.RecordAsync(h.Stories[0].Id, h.Versions[0].Id, null, Assessment(), true, default);
        h.Stories[0].Status = StoryStatus.Ready;
        h.Items.Add(new ModerationItem { Id = 100, ItemType = ModerationItemType.PublicStoryReview,
            StoryId = h.Stories[0].Id, StoryVersionId = h.Versions[0].Id, Status = ModerationItemStatus.Claimed,
            ClaimedByUserId = PublicStoryReviewHarness.ModeratorId, ClaimedAt = DateTime.UtcNow,
            CreatedByUserId = PublicStoryReviewHarness.CreatorId });
        var package = await h.Sut.GetPackageAsync(PublicStoryReviewHarness.ModeratorId, 100);
        Assert.False(package.CanApprove); Assert.Contains(package.ApproveBlockers, x => x.Code == "flags_unresolved");
        await h.Sut.ResolveFlagAsync(PublicStoryReviewHarness.ModeratorId, 100, new() { FlagId = package.Flags[0].FlagId, Reason = "Đã xem" });
        Assert.True(h.Versions[0].GrayZoneCleared);
    }

    [Fact]
    public async Task StaleDecisionAndExpiredClaimCannotClear()
    {
        var (h, service) = Seed(true);
        await service.RecordAsync(h.Stories[0].Id, h.Versions[0].Id, null, Assessment(), true, default);
        var item = h.Items[0]; item.Status = ModerationItemStatus.Claimed;
        item.ClaimedByUserId = PublicStoryReviewHarness.ModeratorId; item.ClaimedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<ConflictException>(() => service.DecideAsync(PublicStoryReviewHarness.ModeratorId, item.Id, new("Clear", "reason", 99), default));
        item.ClaimedAt = DateTime.UtcNow.AddHours(-3);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(PublicStoryReviewHarness.ModeratorId, item.Id, default));
        Assert.False(h.Versions[0].GrayZoneCleared);
    }

    [Fact]
    public async Task ReplacedManualVersionClosesPendingItem()
    {
        var (h, service) = Seed(true);
        await service.RecordAsync(h.Stories[0].Id, h.Versions[0].Id, null, Assessment(), true, default);
        h.Versions[0].IsCurrent = false;
        await service.CloseObsoleteAsync(default);
        Assert.Equal(ModerationItemStatus.Closed, h.Items[0].Status);
    }

    [Theory]
    [InlineData(null)] [InlineData("{}")] [InlineData("not-json")] [InlineData("null")]
    public async Task SweepIgnoresMissingOrLegacyCheckpointAndContinues(string? flags)
    {
        var (h, service) = Seed(true);
        await service.RecordAsync(h.Stories[0].Id, h.Versions[0].Id, null, Assessment(), true, default);
        var valid = h.Items[0];
        h.Items.Insert(0, new ModerationItem { Id = 99, ItemType = ModerationItemType.GrayZoneStory,
            StoryId = h.Stories[0].Id, StoryVersionId = h.Versions[0].Id, Status = ModerationItemStatus.Pending, Flags = flags });
        h.Versions[0].IsCurrent = false;
        await service.CloseObsoleteAsync(default);
        Assert.Equal(ModerationItemStatus.Closed, valid.Status);
        Assert.Equal(ModerationItemStatus.Pending, h.Items[0].Status);
    }

    [Fact]
    public async Task FinalizedParentCannotPersistNewAssessment()
    {
        var (h, service) = Seed(true);
        h.ParentOperations.Add(new ParentStoryOperation { StoryId = h.Stories[0].Id, Kind = "FamilyConfirmation" });
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.RecordAsync(h.Stories[0].Id, h.Versions[0].Id,
            null, Assessment(), true, default));
        Assert.Equal("PARENT_STORY_FINALIZED", error.Message);
        Assert.Empty(h.Items); Assert.Null(h.Versions[0].GrayZoneFlags);
    }
}
