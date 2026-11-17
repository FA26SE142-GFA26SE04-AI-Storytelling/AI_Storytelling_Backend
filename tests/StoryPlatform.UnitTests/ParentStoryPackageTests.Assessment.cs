using System.Net.Http;
using System.Text.Json;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class ParentStoryPackageTests
{
    [Fact]
    public async Task Content_safety_unavailable_completes_as_warning_requires_ack_and_preserves_gray_zone_result()
    {
        var f = new Fixture(); await f.Initialize();
        f.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider down"));
        var packages = await f.Packages.CreateAsync(1, f.StoryId, f.Create("cs-unavail", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));

        var actual = await f.Packages.GetAsync(1, f.StoryId, packages[0].PackageId, default);
        Assert.Equal("Review", actual.Status);
        Assert.Null(actual.LastErrorCode);
        Assert.NotNull(actual.Check);
        Assert.True(actual.Check!.Passed);
        Assert.True(actual.Check.BackendPassed);
        Assert.Null(actual.Check.SafetyScore);
        Assert.Equal(ParentPackageAssessmentStatus.Unavailable, actual.Check.SafetyStatus);
        Assert.Equal(ParentPackageAssessmentStatus.Passed, actual.Check.GrayZoneStatus);
        Assert.True(actual.Check.RequiresParentAcknowledgement);
        var warning = Assert.Single(actual.Check.Warnings);
        Assert.Equal("ContentSafety", warning.Source);
        Assert.Empty(f.Store.Items<ModerationItem>());
    }

    [Fact]
    public async Task Gray_zone_unavailable_completes_as_warning_preserves_safety_score()
    {
        var f = new Fixture(); await f.Initialize();
        f.Moderation.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("MODERATION_NOT_CONFIGURED"));
        var packages = await f.Packages.CreateAsync(1, f.StoryId, f.Create("gz-unavail", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));

        var actual = await f.Packages.GetAsync(1, f.StoryId, packages[0].PackageId, default);
        Assert.Equal("Review", actual.Status);
        Assert.True(actual.Check!.Passed);
        Assert.Equal(100m, actual.Check.SafetyScore);
        Assert.Equal(ParentPackageAssessmentStatus.Passed, actual.Check.SafetyStatus);
        Assert.Equal(ParentPackageAssessmentStatus.Unavailable, actual.Check.GrayZoneStatus);
        Assert.True(actual.Check.RequiresParentAcknowledgement);
        var warning = Assert.Single(actual.Check.Warnings);
        Assert.Equal("GrayZone", warning.Source);
        Assert.Equal("MODERATION_NOT_CONFIGURED", warning.Code);
    }

    [Fact]
    public async Task Both_evaluators_unavailable_completes_as_review_with_two_warnings()
    {
        var f = new Fixture(); await f.Initialize();
        f.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timed out"));
        f.Moderation.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("moderation unreachable"));
        var packages = await f.Packages.CreateAsync(1, f.StoryId, f.Create("both-unavail", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));

        var actual = await f.Packages.GetAsync(1, f.StoryId, packages[0].PackageId, default);
        Assert.Equal("Review", actual.Status);
        Assert.True(actual.Check!.Passed);
        Assert.Null(actual.Check.SafetyScore);
        Assert.Equal(ParentPackageAssessmentStatus.Unavailable, actual.Check.SafetyStatus);
        Assert.Equal(ParentPackageAssessmentStatus.Unavailable, actual.Check.GrayZoneStatus);
        Assert.True(actual.Check.RequiresParentAcknowledgement);
        Assert.Equal(2, actual.Check.Warnings.Length);
    }

    [Fact]
    public async Task Gray_zone_warning_creates_no_moderation_item_and_uses_real_evidence_only()
    {
        var f = new Fixture(); await f.Initialize();
        f.Moderation.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrayZoneAssessment(ModerationZone.GrayZone, "fingerprint", "phase3", "1", "test", "test", 60,
            [
                new ModerationFinding("paragraph", "paragraph:0", "tone", "Reason without quote"),
                new ModerationFinding("paragraph", "paragraph:1", "tone", "Reason with quote") { EvidenceQuote = "Tom sees a small dog." }
            ]));
        var packages = await f.Packages.CreateAsync(1, f.StoryId, f.Create("gz-warn", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));

        var actual = await f.Packages.GetAsync(1, f.StoryId, packages[0].PackageId, default);
        Assert.Equal("Review", actual.Status);
        Assert.True(actual.Check!.Passed);
        Assert.Equal(ParentPackageAssessmentStatus.Warning, actual.Check.GrayZoneStatus);
        Assert.True(actual.Check.RequiresParentAcknowledgement);
        var warning = Assert.Single(actual.Check.Warnings);
        Assert.Equal("GRAY_ZONE_WARNING", warning.Code);
        Assert.Equal(["Tom sees a small dog."], warning.Evidence);
        Assert.Empty(f.Store.Items<ModerationItem>());
    }

    [Fact]
    public async Task Hard_block_and_safety_rejection_remain_blocked_even_if_ack_true()
    {
        var f = new Fixture(); await f.Initialize();
        f.Moderation.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrayZoneAssessment(ModerationZone.HardBlock, "fingerprint", "phase3", "1", "test", "test", 95,
            [new ModerationFinding("paragraph", "paragraph:0", "violence", "Unsafe") { EvidenceQuote = "Tom has a red ball." }]));
        var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("hard-block", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));

        var blocked = await f.Packages.GetAsync(1, f.StoryId, created[0].PackageId, default);
        Assert.Equal("Blocked", blocked.Status);
        Assert.False(blocked.Check!.Passed);
        Assert.Equal(ParentPackageAssessmentStatus.Blocked, blocked.Check.GrayZoneStatus);
        await Assert.ThrowsAsync<ConflictException>(() => f.Packages.ConfirmAsync(1, f.StoryId, blocked.PackageId,
            new() { OperationId = "confirm-blocked", PackageToken = blocked.PackageToken, AcknowledgeAssessmentWarnings = true }, default));
    }

    [Fact]
    public async Task Confirm_requires_ack_when_warnings_exist_persists_receipt_and_deliver_verifies_snapshot()
    {
        var f = new Fixture(); await f.Initialize();
        f.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timeout"));
        var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("ack-flow", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));
        var ready = await f.Packages.GetAsync(1, f.StoryId, created[0].PackageId, default);

        var err = await Assert.ThrowsAsync<ConflictException>(() => f.Packages.ConfirmAsync(1, f.StoryId, ready.PackageId,
            new() { OperationId = "confirm-no-ack", PackageToken = ready.PackageToken, AcknowledgeAssessmentWarnings = false }, default));
        Assert.Equal("PACKAGE_ASSESSMENT_ACKNOWLEDGEMENT_REQUIRED", err.Message);

        var confirmCmd = new ParentPackageConfirmRequest
        {
            OperationId = "confirm-with-ack",
            PackageToken = ready.PackageToken,
            AcknowledgeAssessmentWarnings = true
        };
        var confirmed = await f.Packages.ConfirmAsync(1, f.StoryId, ready.PackageId, confirmCmd, default);
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.NotNull(confirmed.Check!.Acknowledgement);
        Assert.Equal(confirmed.PackageId, confirmed.Check.Acknowledgement!.PackageId);
        Assert.Equal(confirmed.Revision, confirmed.Check.Acknowledgement.PackageRevision);
        Assert.Equal(1, confirmed.Check.Acknowledgement.AcknowledgedByUserId);
        Assert.Equal(ready.Check!.Warnings.Length, confirmed.Check.Acknowledgement.Warnings.Length);

        var replayed = await f.Packages.ConfirmAsync(1, f.StoryId, ready.PackageId, confirmCmd, default);
        Assert.Equal(
            JsonSerializer.Serialize(confirmed.Check.Acknowledgement, ParentPackageContext.Json),
            JsonSerializer.Serialize(replayed.Check!.Acknowledgement, ParentPackageContext.Json));
        Assert.Equal(confirmed.Check.Acknowledgement.PackageId, replayed.Check.Acknowledgement!.PackageId);
        Assert.Equal(confirmed.Check.Acknowledgement.PackageRevision, replayed.Check.Acknowledgement.PackageRevision);
        Assert.Equal(confirmed.Check.Acknowledgement.AcknowledgedByUserId, replayed.Check.Acknowledgement.AcknowledgedByUserId);
        Assert.Equal(confirmed.Check.Acknowledgement.AcknowledgedAtUtc, replayed.Check.Acknowledgement.AcknowledgedAtUtc);
        Assert.Equal(confirmed.Check.Acknowledgement.SourceFingerprint, replayed.Check.Acknowledgement.SourceFingerprint);
        Assert.Equal(confirmed.Check.Acknowledgement.ContextFingerprint, replayed.Check.Acknowledgement.ContextFingerprint);
        Assert.Equal(confirmed.Check.Acknowledgement.Warnings.Select(w => w.Code), replayed.Check.Acknowledgement.Warnings.Select(w => w.Code));
        Assert.Equal(confirmed.Check.Acknowledgement.Warnings.SelectMany(w => w.Evidence), replayed.Check.Acknowledgement.Warnings.SelectMany(w => w.Evidence));

        var grants = await f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver-ack", confirmed), default);
        Assert.Single(grants);

        var revised = await f.Packages.ReviseAsync(1, f.StoryId, confirmed.PackageId,
            new() { OperationId = "revise-ack", PackageToken = confirmed.PackageToken, Components = new() }, default);
        Assert.True(await f.Worker.ProcessNextAsync(default));
        var revisedReady = await f.Packages.GetAsync(1, f.StoryId, revised.PackageId, default);
        Assert.Null(revisedReady.Check!.Acknowledgement);
        await Assert.ThrowsAsync<ConflictException>(() => f.Packages.ConfirmAsync(1, f.StoryId, revisedReady.PackageId,
            new() { OperationId = "confirm-rev-no-ack", PackageToken = revisedReady.PackageToken, AcknowledgeAssessmentWarnings = false }, default));
    }

    [Fact]
    public async Task Local_readability_failure_and_cancellation_remain_blocking_or_noncompleted()
    {
        var complexBody = "Nevertheless, the extraordinarily complicated and disproportionately convoluted pedagogical explanation regarding the multifaceted astronomical phenomena continued uninterrupted throughout the entire afternoon without any pauses or simplified phrasing whatsoever for young readers to comprehend easily.";
        var f = new Fixture(); await f.Initialize(complexBody);
        f.Store.Items<SafetyPolicy>().Single(x => x.ChildProfileId == 3).ReadabilityScoreThreshold = 90;
        var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("local-fail", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));

        var blocked = await f.Packages.GetAsync(1, f.StoryId, created[0].PackageId, default);
        Assert.Equal("Blocked", blocked.Status);
        Assert.Contains("READABILITY_POLICY_NOT_MET", blocked.Check!.Errors);
        Assert.False(blocked.Check.Passed);
        Assert.False(blocked.Check.BackendPassed);
        Assert.Equal(ParentPackageAssessmentStatus.NotRun, blocked.Check.SafetyStatus);
        Assert.Equal(ParentPackageAssessmentStatus.NotRun, blocked.Check.GrayZoneStatus);
        f.Ai.Verify(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        var f2 = new Fixture(); await f2.Initialize();
        using var cts = new CancellationTokenSource();
        f2.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var created2 = await f2.Packages.CreateAsync(1, f2.StoryId, f2.Create("cancel-check", 4), default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f2.Worker.ProcessNextAsync(cts.Token));
        var stillChecking = await f2.Packages.GetAsync(1, f2.StoryId, created2[0].PackageId, default);
        Assert.Equal("Checking", stillChecking.Status);
    }

    [Fact]
    public async Task Legacy_confirm_receipt_replays_when_ack_false_and_rejects_when_ack_true()
    {
        var f = new Fixture(); await f.Initialize();
        var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("legacy-replay", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));
        var ready = await f.Packages.GetAsync(1, f.StoryId, created[0].PackageId, default);

        var legacyCmd = new ParentPackageCommand { OperationId = "legacy-confirm-op", PackageToken = ready.PackageToken };
        var legacyHash = ParentPackageContext.Hash(new
        {
            kind = "parent-package-confirm",
            packageId = (int?)ready.PackageId,
            payload = JsonSerializer.SerializeToElement(legacyCmd, typeof(ParentPackageCommand), ParentPackageContext.Json)
        });
        var expectedDto = ready with { Status = "Confirmed", ConfirmedAt = DateTime.UtcNow };
        f.Store.Items<StoryAuthoringOperation>().Add(new()
        {
            Id = 900,
            StoryId = f.StoryId,
            StoryVersionId = f.Saved.Version.VersionId,
            ActorUserId = 1,
            Kind = "parent-package-confirm",
            OperationId = "legacy-confirm-op",
            RequestHash = legacyHash,
            OperationResponseJson = JsonSerializer.Serialize(expectedDto, ParentPackageContext.Json)
        });

        var replayed = await f.Packages.ConfirmAsync(1, f.StoryId, ready.PackageId,
            new() { OperationId = "legacy-confirm-op", PackageToken = ready.PackageToken, AcknowledgeAssessmentWarnings = false }, default);
        Assert.Equal(expectedDto.Status, replayed.Status);
        Assert.Equal(expectedDto.PackageId, replayed.PackageId);

        var err = await Assert.ThrowsAsync<ConflictException>(() => f.Packages.ConfirmAsync(1, f.StoryId, ready.PackageId,
            new() { OperationId = "legacy-confirm-op", PackageToken = ready.PackageToken, AcknowledgeAssessmentWarnings = true }, default));
        Assert.Equal("OPERATION_ID_REUSED", err.Message);
    }

    [Fact]
    public async Task Old_package_moderation_holds_block_confirm_and_deliver()
    {
        var f = new Fixture(); await f.Initialize();
        var packages = await f.Ready(3);
        var pkg = f.Store.Items<ParentStoryPackage>().Single(x => x.Id == packages[0].PackageId);
        f.Store.Items<ModerationItem>().Add(new()
        {
            Id = 500,
            StoryId = f.StoryId,
            StoryVersionId = f.Saved.Version.VersionId,
            ParentStoryPackageId = pkg.Id,
            SourceChildProfileId = 3,
            CreatedByUserId = 1,
            ItemType = ModerationItemType.GrayZoneStory,
            Status = ModerationItemStatus.Escalated,
            Flags = "{}",
            SlaDueAt = DateTime.UtcNow.AddHours(24)
        });

        var err = await Assert.ThrowsAsync<ConflictException>(() => f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver-held", packages), default));
        Assert.Contains("SAFETY_MODERATION_REQUIRED", err.Message);
    }

    [Fact]
    public async Task Valid_safety_disallow_remains_blocked_when_gray_zone_unavailable()
    {
        var f = new Fixture(); await f.Initialize();
        f.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluateContentSafetyResponse
            {
                IsAllowed = false,
                RiskLevel = "high",
                SafetyScore = 40,
                ScoreRuleVersion = ContentSafetyScoring.Version,
                Evidence = ["Tom has a red ball."]
            });
        f.Moderation.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("gray zone down"));
        var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("safety-block-gz-unavail", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));

        var blocked = await f.Packages.GetAsync(1, f.StoryId, created[0].PackageId, default);
        Assert.Equal("Blocked", blocked.Status);
        Assert.False(blocked.Check!.Passed);
        Assert.Equal(ParentPackageAssessmentStatus.Blocked, blocked.Check.SafetyStatus);
        Assert.Equal(ParentPackageAssessmentStatus.Unavailable, blocked.Check.GrayZoneStatus);
        await Assert.ThrowsAsync<ConflictException>(() => f.Packages.ConfirmAsync(1, f.StoryId, blocked.PackageId,
            new() { OperationId = "confirm-safety-block", PackageToken = blocked.PackageToken, AcknowledgeAssessmentWarnings = true }, default));
    }

    [Fact]
    public async Task Tampered_acknowledgement_prevents_atomic_two_child_delivery()
    {
        var f = new Fixture(); await f.Initialize();
        f.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timeout"));
        var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("tamper-batch", 3, 4), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));
        Assert.True(await f.Worker.ProcessNextAsync(default));

        var p0 = await f.Packages.GetAsync(1, f.StoryId, created[0].PackageId, default);
        var p1 = await f.Packages.GetAsync(1, f.StoryId, created[1].PackageId, default);
        var c0 = await f.Packages.ConfirmAsync(1, f.StoryId, p0.PackageId,
            new() { OperationId = "confirm-0", PackageToken = p0.PackageToken, AcknowledgeAssessmentWarnings = true }, default);
        var c1 = await f.Packages.ConfirmAsync(1, f.StoryId, p1.PackageId,
            new() { OperationId = "confirm-1", PackageToken = p1.PackageToken, AcknowledgeAssessmentWarnings = true }, default);

        var row1 = f.Store.Items<ParentStoryPackage>().Single(x => x.Id == c1.PackageId);
        var check1 = JsonSerializer.Deserialize<ParentPackageCheck>(row1.CheckResultJson, ParentPackageContext.Json)!;
        row1.CheckResultJson = JsonSerializer.Serialize(check1 with { Acknowledgement = check1.Acknowledgement! with { PackageRevision = 99 } }, ParentPackageContext.Json);

        var err = await Assert.ThrowsAsync<ConflictException>(() => f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver-tampered", c0, c1), default));
        Assert.Contains("PACKAGE_ASSESSMENT_ACKNOWLEDGEMENT_REQUIRED", err.Message);
        Assert.DoesNotContain(f.Store.Items<ParentStoryOperation>(), x => x.Kind == "FamilyConfirmation");
    }

    [Fact]
    public async Task Unexpected_invalid_operation_and_missing_consent_are_not_converted_to_warnings()
    {
        var f = new Fixture(); await f.Initialize();
        f.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected internal state"));
        var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("unexpected-ioe", 3), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));
        var failed = await f.Packages.GetAsync(1, f.StoryId, created[0].PackageId, default);
        Assert.Equal("Failed", failed.Status);

        f.Store.Items<SafetyPolicy>().Single(x => x.ChildProfileId == 4).ConsentRecorded = false;
        var created2 = await f.Packages.CreateAsync(1, f.StoryId, f.Create("no-consent", 4), default);
        Assert.True(await f.Worker.ProcessNextAsync(default));
        var failedConsent = await f.Packages.GetAsync(1, f.StoryId, created2[0].PackageId, default);
        Assert.Equal("Failed", failedConsent.Status);
        Assert.Equal("CONSENT_REQUIRED", failedConsent.LastErrorCode);
    }
}
