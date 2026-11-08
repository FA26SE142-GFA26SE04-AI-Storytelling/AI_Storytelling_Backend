using System.Text.Json;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public sealed class CreatorSafetyCheckTests
{
    private readonly PublicStoryReviewHarness h = new();
    private readonly Mock<IGrayZoneEvaluator> evaluator = new();
    private CreatorSafetyCheckService Validity => new(h.UnitOfWork.Object, evaluator.Object);
    private Story Story => h.Stories[0];
    private StoryVersion Version => h.Versions[0];

    public CreatorSafetyCheckTests()
    {
        h.Items.Clear();
        evaluator.SetupGet(x => x.IsConfigured).Returns(true);
        evaluator.Setup(x => x.CanReuseAsync(It.IsAny<GrayZoneAssessment>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private async Task<GrayZoneAssessment> Store(ModerationZone zone = ModerationZone.Safe, string phase = "phase3")
    {
        var scan = CreatorSafetyCheckService.CreateScan(phase, Story, Version.Title, Version.Content!, Version.Lesson, []);
        var assessment = new GrayZoneAssessment(zone, GrayZoneEvaluator.Fingerprint(scan), phase, "fixture-policy", "fixture", "fixture-model", 40,
            [new("paragraph", "paragraph:0", "context", "Review context", 0, Version.Content!.Length)
                { EvidenceQuote = "Giữa truyện.", ContextNote = "Fixture context" }])
            { AgeBand = Story.AgeBand, Language = Story.Language, AudienceRevision = Story.AudienceRevision };
        await new GrayZoneWorkflowService(h.UnitOfWork.Object, h.Audit.Object).RecordAsync(Story.Id, Version.Id, null, assessment, true, default);
        return assessment;
    }

    [Theory]
    [InlineData(ModerationZone.Safe)]
    [InlineData(ModerationZone.GrayZone)]
    [InlineData(ModerationZone.HardBlock)]
    public async Task StatusReadsPersistedResultWithoutCallingAiOrCreatingQueueItems(ModerationZone zone)
    {
        await Store(zone, "phase3");
        var before = Version.Moderation.GrayZoneFlags;
        var service = new CreatorPublicationService(h.UnitOfWork.Object,
            new ModerationQueueService(h.UnitOfWork.Object, h.Audit.Object), h.Audit.Object, evaluator.Object);
        var status = await service.GetStatusAsync(Story.AuthorUserId, Story.Id);
        Assert.Equal("Current", status.SafetyCheck.State);
        Assert.Equal(zone.ToString(), status.SafetyCheck.Zone);
        Assert.Equal(before, Version.Moderation.GrayZoneFlags);
        Assert.Empty(h.Items);
        evaluator.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingDisabledMalformedAndProviderFailureAreNotSafe()
    {
        Assert.Equal("NotChecked", (await Validity.GetAsync(Story, Version, default)).State);
        Version.Moderation.GrayZoneFlags = "{\"assessment\":42}";
        Assert.Equal("NotChecked", (await Validity.GetAsync(Story, Version, default)).State);
        await Store();
        evaluator.Setup(x => x.CanReuseAsync(It.IsAny<GrayZoneAssessment>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("fixture unavailable"));
        Assert.Equal("Unavailable", (await Validity.GetAsync(Story, Version, default)).State);
        evaluator.SetupGet(x => x.IsConfigured).Returns(false);
        var disabled = await Validity.GetAsync(Story, Version, default);
        Assert.False(disabled.Required);
        Assert.Equal("Unavailable", disabled.State);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("content")]
    [InlineData("lesson")]
    [InlineData("age")]
    [InlineData("language")]
    [InlineData("reading")]
    [InlineData("audience")]
    [InlineData("rubric")]
    [InlineData("blocked-category")]
    public async Task ChangedSourceOrContextInvalidatesAssessment(string change)
    {
        await Store(phase: "phase3");
        switch (change)
        {
            case "title": Version.Title += " sửa"; break;
            case "content": Version.Content += " sửa"; break;
            case "lesson": Version.Lesson += " sửa"; break;
            case "age": Story.AgeBand = "9-10"; break;
            case "language": Story.Language = "en"; break;
            case "reading": Story.ReadingLevel++; break;
            case "audience": Story.AudienceRevision++; break;
            case "rubric": evaluator.Setup(x => x.CanReuseAsync(It.IsAny<GrayZoneAssessment>(), It.IsAny<CancellationToken>())).ReturnsAsync(false); break;
            case "blocked-category": h.ContentCategories.Add(new ContentCategory { Id = 99, Code = "new", DisplayName = "Mới", IsActive = true, DefaultBlockedAgeBands = ["Age_6_8"] }); break;
        }
        Assert.Equal("Stale", (await Validity.GetAsync(Story, Version, default)).State);
    }

    [Fact]
    public async Task ExplicitCheckReusesCurrentAssessmentWithoutProviderOrPersistence()
    {
        await Store();
        var checks = new GrayZoneContentCheckService(h.UnitOfWork.Object, evaluator.Object, null!, new Mock<IChildProfileAccessGuard>().Object);
        h.UnitOfWork.Invocations.Clear();
        var result = await checks.CheckAsync(Story.AuthorUserId, Story.Id, Version.Id, default);
        Assert.Equal("Safe", result.Status);
        Assert.False(result.WaitingForModeration);
        evaluator.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Never);
        h.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Bài học đã chỉnh sửa")]
    [InlineData("")]
    [InlineData(null)]
    public async Task CompleteContentWithoutAssessmentScansLesson(string? lesson)
    {
        Version.Lesson = lesson;
        var scan = await Validity.BuildScanAsync(Story, Version, default);
        Assert.Equal("phase3", scan.Phase);
        Assert.Equal(lesson ?? "", Assert.Single(scan.Parts, x => x.Kind == "section" && x.Name == "lesson").Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task IncompleteDraftWithoutStoryContentKeepsPhaseOne(string? content)
    {
        Version.Content = content;
        var scan = await Validity.BuildScanAsync(Story, Version, default);
        Assert.Equal("phase1", scan.Phase);
        Assert.Contains(scan.Parts, part => part.Kind == "section" && part.Name == "title");
    }

    [Fact]
    public async Task LegacyPhaseOneResultIsStaleUntilLessonIsIncludedInNewAssessment()
    {
        await Store(phase: "phase1");
        Assert.Equal("Stale", (await Validity.GetAsync(Story, Version, default)).State);
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrayZoneScan scan, CancellationToken _) => new GrayZoneAssessment(ModerationZone.Safe,
                GrayZoneEvaluator.Fingerprint(scan), scan.Phase, "policy", "fixture", "model", 0, [])
                { AgeBand = Story.AgeBand, Language = Story.Language });
        var checks = new GrayZoneContentCheckService(h.UnitOfWork.Object, evaluator.Object,
            new GrayZoneWorkflowService(h.UnitOfWork.Object, h.Audit.Object), new Mock<IChildProfileAccessGuard>().Object);
        await checks.CheckAsync(Story.AuthorUserId, Story.Id, Version.Id, default);
        Assert.Equal("phase3", CreatorSafetyCheckService.Read(Version)!.Phase);
        Assert.Equal("Current", (await Validity.GetAsync(Story, Version, default)).State);
        await checks.CheckAsync(Story.AuthorUserId, Story.Id, Version.Id, default);
        evaluator.Verify(x => x.EvaluateAsync(It.Is<GrayZoneScan>(scan => scan.Phase == "phase3"
            && scan.Parts.Any(part => part.Name == "lesson" && part.Text == Version.Lesson)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LessonOnlyRevisionIsSentToProviderAndCanBeBlocked()
    {
        await Store(phase: "phase3");
        var revision = h.CloneVersion(Version.Id, Version.Id + 1);
        revision.Lesson = "Bài học cần kiểm tra lại";
        Assert.Equal(Version.Title, revision.Title);
        Assert.Equal(Version.Content, revision.Content);
        Assert.Equal("NotChecked", (await Validity.GetAsync(Story, revision, default)).State);
        GrayZoneScan? captured = null;
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrayZoneScan scan, CancellationToken _) =>
            {
                captured = scan;
                return new GrayZoneAssessment(ModerationZone.HardBlock, GrayZoneEvaluator.Fingerprint(scan), scan.Phase,
                    "policy", "fixture", "model", 90, [new("section", "lesson", "context", "Lesson review")
                        { EvidenceQuote = revision.Lesson }]) { AgeBand = Story.AgeBand, Language = Story.Language };
            });
        var checks = new GrayZoneContentCheckService(h.UnitOfWork.Object, evaluator.Object,
            new GrayZoneWorkflowService(h.UnitOfWork.Object, h.Audit.Object), new Mock<IChildProfileAccessGuard>().Object);
        var result = await checks.CheckAsync(Story.AuthorUserId, Story.Id, revision.Id, default);
        Assert.Contains(captured!.Parts, x => x.Kind == "section" && x.Name == "lesson" && x.Text == revision.Lesson);
        Assert.Equal("HardBlock", result.Status);
        Assert.Equal("HardBlock", (await Validity.GetAsync(Story, revision, default)).Zone);
        Assert.Empty(h.Items);
    }

    [Fact]
    public async Task PipelinePhaseThreeFingerprintMatchesPublicationContext()
    {
        h.ContentCategories.Add(new ContentCategory { Id = 8, Code = "z", DisplayName = "Z", IsActive = true, DefaultBlockedAgeBands = ["Age_6_8"] });
        h.ContentCategories.Add(new ContentCategory { Id = 7, Code = "a", DisplayName = "A", IsActive = true, DefaultBlockedAgeBands = ["Age_6_8"] });
        // Reproduce the generator's snapshot and phase3 scan rather than calling the publication builder.
        var terms = new[] { "z", "Z", "a", "A" }.Distinct().Order().ToArray();
        var scan = new GrayZoneScan("phase3", Story.AgeBand, Story.Language,
            GrayZoneEvaluator.TextParts(Version.Title, Version.Content!).Concat([new ModerationText("section", "lesson", Version.Lesson!)]).ToArray(), terms)
            { ReadingLevel = Story.ReadingLevel };
        var assessment = new GrayZoneAssessment(ModerationZone.Safe, GrayZoneEvaluator.Fingerprint(scan), "phase3", "policy", "fixture", "model", 0, [])
            { AudienceRevision = Story.AudienceRevision, AgeBand = Story.AgeBand, Language = Story.Language };
        Version.Moderation.GrayZoneFlags = JsonSerializer.Serialize(new { assessment }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Current", (await Validity.GetAsync(Story, Version, default)).State);
        evaluator.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisabledModerationDoesNotEraseExistingHardBlock()
    {
        await Store(ModerationZone.HardBlock);
        evaluator.SetupGet(x => x.IsConfigured).Returns(false);
        var status = await Validity.GetAsync(Story, Version, default);
        Assert.False(status.Required);
        Assert.Equal("HardBlock", status.Zone);
    }

    [Fact]
    public async Task LegacyAssessmentUsesPinnedVersionAudienceWithoutRescanning()
    {
        var assessment = (await Store()) with { AudienceRevision = null };
        Version.Moderation.GrayZoneFlags = JsonSerializer.Serialize(new { assessment }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Current", (await Validity.GetAsync(Story, Version, default)).State);
        Story.AudienceRevision++;
        Assert.Equal("Stale", (await Validity.GetAsync(Story, Version, default)).State);
        evaluator.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreatorGrayZoneKeepsEvidenceInPublicReviewWithoutSeparateGrayQueue()
    {
        var assessment = await Store(ModerationZone.GrayZone);
        Assert.Empty(h.Items);
        var flags = PublicReviewState.ParseVersionFlags(Version.Moderation.GrayZoneFlags);
        Assert.Equal("Giữa truyện.", Assert.Single(flags).EvidenceQuote);
        Assert.Equal(Story.AudienceRevision, CreatorSafetyCheckService.Read(Version)!.AudienceRevision);
        h.Items.Add(new ModerationItem { Id = 999, StoryId = Story.Id, StoryVersionId = Version.Id,
            ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending });
        var checks = new GrayZoneContentCheckService(h.UnitOfWork.Object, evaluator.Object, null!, new Mock<IChildProfileAccessGuard>().Object);
        await Assert.ThrowsAsync<ConflictException>(() => checks.CheckAsync(Story.AuthorUserId, Story.Id, Version.Id, default));
        Assert.Equal(assessment.Fingerprint, CreatorSafetyCheckService.Read(Version)!.Fingerprint);
        evaluator.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
