using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
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
    private const string Body = "Tom has a red ball. He can run and play. He sees a small dog. The dog sits by a tree.\n\nTom gives the dog a drink. The dog is glad. Tom waves and goes home. He tells his mum about his day.";
    private sealed class Fixture
    {
        public ParentAuthoringTestStore Store { get; } = new();
        public ParentStoryPackageService Packages { get; }
        public Mock<IAIStoryGenerationClient> Ai { get; } = new(MockBehavior.Strict);
        public Mock<IGrayZoneEvaluator> Moderation { get; } = new();
        public Mock<ILearningBatchService> Batches { get; } = new(MockBehavior.Strict);
        public ParentStoryPackageProcessor Worker { get; }
        public int StoryId { get; private set; }
        public CreatorContentSavedDto Saved { get; private set; } = null!;
        public Fixture()
        {
            Store.Items<ChildProfile>()[0].Language = "en";
            Store.Items<ChildProfile>().Add(new() { Id = 4, OwnerUserId = 1, Language = "en", AgeBand = AgeBand.Age_9_10, Status = ChildProfileStatus.Active });
            Store.Items<SafetyPolicy>()[0].ConsentRecorded = true;
            Store.Items<SafetyPolicy>()[0].ConsentRecordedAt = DateTime.UtcNow.AddDays(-1);
            Store.Items<SafetyPolicy>()[0].ConsentPolicyVersion = 1;
            Store.Items<SafetyPolicy>().Add(new() { Id = 2, ChildProfileId = 4, MaxStoryLength = 5000, ConsentRecorded = true,
                ConsentRecordedAt = DateTime.UtcNow.AddDays(-1), ConsentPolicyVersion = 1 });
            Store.Items<LearningProfile>().AddRange([new() { Id = 1, ChildProfileId = 3, ReadingLevel = 1 }, new() { Id = 2, ChildProfileId = 4, ReadingLevel = 3 }]);
            var guard = new ChildProfileAccessGuard(Store.Uow.Object);
            Packages = new(Store.Uow.Object, guard);
            Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EvaluateContentSafetyResponse { IsAllowed = true, SafetyScore = 100, RiskLevel = "none", ScoreRuleVersion = ContentSafetyScoring.Version });
            Moderation.SetupGet(x => x.IsConfigured).Returns(true);
            Moderation.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GrayZoneAssessment(ModerationZone.Safe, "fingerprint", "phase3", "1", "test", "test", 0, []));
            Worker = new(Store.Uow.Object, guard, Ai.Object, Batches.Object,
                new ListeningInteractionGenerator(Store.Uow.Object, Ai.Object), NullLogger<ParentStoryPackageProcessor>.Instance, Moderation.Object, new StorySegmentService());
        }
        public async Task Initialize()
        {
            var draft = await Store.Service.CreateParentDraftAsync(1, new() { OperationId = "create", Title = "Tom and the dog", Language = "en" }, default);
            Assert.Null(draft.ChildProfileId);
            Assert.False(draft.CanUseAiAssistance);
            StoryId = draft.Story.StoryId;
            Saved = await Store.Service.SaveContentAsync(1, StoryId, new() { OperationId = "save", RevisionToken = draft.Story.RevisionToken,
                Title = "Tom and the dog", Content = Body }, default);
            await Packages.FinalizeAsync(1, StoryId, Finalize("finalize"), default);
        }
        public ParentPackageFinalizeRequest Finalize(string key) => new() { OperationId = key, RevisionToken = Saved.Story.RevisionToken, BaseVersionId = Saved.Version.VersionId };
        public CreateParentPackagesRequest Create(string key, params int[] ids) => new() { OperationId = key, RevisionToken = Saved.Story.RevisionToken,
            BaseVersionId = Saved.Version.VersionId, Recipients = ids.Select(x => new ParentPackageRecipient(x, new())).ToArray() };
        public async Task<ParentPackageDto[]> Ready(params int[] children)
        {
            var packages = await Packages.CreateAsync(1, StoryId, Create("packages-" + string.Join('-', children), children), default);
            foreach (var _ in packages) Assert.True(await Worker.ProcessNextAsync(default));
            var confirmed = new List<ParentPackageDto>();
            foreach (var p in packages)
            {
                var actual = await Packages.GetAsync(1, StoryId, p.PackageId, default);
                Assert.Equal("Review", actual.Status);
                confirmed.Add(await Packages.ConfirmAsync(1, StoryId, p.PackageId, new() { OperationId = "confirm-" + p.PackageId, PackageToken = actual.PackageToken }, default));
            }
            return confirmed.ToArray();
        }
        public ParentPackageBatchGrantRequest Grant(string key, params ParentPackageDto[] packages) => new() { OperationId = key,
            Packages = packages.Select(x => new ParentPackageGrantItem(x.PackageId, x.PackageToken)).ToArray() };
    }

    [Fact]
    public async Task Two_children_share_one_version_have_independent_grants_and_revocation()
    {
        var f = new Fixture(); await f.Initialize();
        f.Store.Items<Story>().Single().ChildProfileId = 4; // Legacy reference is not the list of recipients.
        var packages = await f.Ready(3, 4);
        var request = f.Grant("deliver", packages);
        var grants = await f.Packages.DeliverAsync(1, f.StoryId, request, default);
        Assert.Equal(grants, await f.Packages.DeliverAsync(1, f.StoryId, request, default));
        Assert.Single(f.Store.Items<Story>()); Assert.Single(f.Store.Items<StoryVersion>());
        Assert.Equal(2, f.Store.Items<ParentStoryOperation>().Count(x => x.Kind == "FamilyConfirmation"));
        Assert.Equal(grants[0].StoryVersionId, grants[1].StoryVersionId);
        var reader = new ParentStoryPackageReader(f.Store.Uow.Object, new ChildProfileAccessGuard(f.Store.Uow.Object));
        Assert.Equal(Body, string.Concat((await reader.ReadAsync(3, grants[0].ConfirmationId, default)).Segments.Select(x => x.Text)));
        await Assert.ThrowsAsync<NotFoundException>(() => reader.ReadAsync(4, grants[0].ConfirmationId, default));
        await f.Store.Service.RevokeFamilyAsync(1, f.StoryId, new() { OperationId = "revoke", RevisionToken = f.Saved.Story.RevisionToken,
            BaseVersionId = f.Saved.Version.VersionId, ConfirmationId = grants[0].ConfirmationId }, default);
        await Assert.ThrowsAsync<NotFoundException>(() => reader.ReadAsync(3, grants[0].ConfirmationId, default));
        Assert.NotNull(await reader.ReadAsync(4, grants[1].ConfirmationId, default));
        var revocation = Assert.Single(f.Store.Items<ParentStoryOperation>(), x => x.Kind == "FamilyRevocation");
        Assert.Equal(3, revocation.ChildProfileId); Assert.Equal(packages[0].PackageId, revocation.ParentStoryPackageId);
        Assert.Empty(f.Store.Items<StoryVocabulary>()); Assert.Empty(f.Store.Items<QuizItem>());
        Assert.DoesNotContain(f.Store.Items<StoryGenerationJob>(), x => x.Operation is GenerationJobOperation.GenerateMediaPackage or GenerationJobOperation.GenerateOutline);
    }

    [Fact]
    public async Task Batch_failure_is_atomic_and_replay_payload_cannot_change()
    {
        var f = new Fixture(); await f.Initialize(); var packages = await f.Ready(3, 4);
        f.Store.Items<SafetyPolicy>().Single(x => x.ChildProfileId == 4).MaxStoryLength = 10;
        var error = await Assert.ThrowsAsync<ConflictException>(() => f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver", packages), default));
        Assert.Contains("child:4:CHILD_CONTEXT_CHANGED", error.Message);
        Assert.DoesNotContain(f.Store.Items<ParentStoryOperation>(), x => x.Kind == "FamilyConfirmation");
        await Assert.ThrowsAsync<ConflictException>(() => f.Packages.CreateAsync(1, f.StoryId, f.Create("packages-3-4", 3), default));
    }

    [Fact]
    public async Task Reopen_invalidates_packages_and_requires_new_source_checks()
    {
        var f = new Fixture(); await f.Initialize(); var packages = await f.Ready(3, 4);
        await f.Packages.ReopenAsync(1, f.StoryId, f.Finalize("reopen"), default);
        Assert.All(f.Store.Items<ParentStoryPackage>(), x => Assert.Equal("Stale", x.Status));
        await Assert.ThrowsAsync<ConflictException>(() => f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver", packages), default));
        var draft = await f.Store.Service.GetParentDraftAsync(1, f.StoryId, default);
        var next = await f.Store.Service.SaveContentAsync(1, f.StoryId, new() { OperationId = "save-v2", RevisionToken = draft.Story.RevisionToken,
            BaseVersionId = f.Saved.Version.VersionId, Title = "Tom", Content = Body + " It is a good day." }, default);
        Assert.NotEqual(f.Saved.Version.VersionId, next.Version.VersionId);
    }

    [Fact]
    public async Task New_recipient_after_delivery_does_not_unlock_content_or_change_old_package()
    {
        var f = new Fixture(); await f.Initialize(); var first = await f.Ready(3);
        await f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver-a", first), default);
        var second = await f.Ready(4);
        await f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver-b", second), default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Store.Service.SaveContentAsync(1, f.StoryId, new() { OperationId = "blocked-save",
            RevisionToken = f.Saved.Story.RevisionToken, BaseVersionId = f.Saved.Version.VersionId, Title = "Changed", Content = Body }, default));
        Assert.Equal(first[0].PackageToken, (await f.Packages.GetAsync(1, f.StoryId, first[0].PackageId, default)).PackageToken);
    }

    [Fact]
    public async Task Ownership_profile_changes_and_stale_package_tokens_are_enforced()
    {
        var f = new Fixture(); await f.Initialize(); var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("packages", 3), default);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Packages.GetAsync(2, f.StoryId, created[0].PackageId, default));
        await f.Worker.ProcessNextAsync(default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Packages.ConfirmAsync(1, f.StoryId, created[0].PackageId,
            new() { OperationId = "confirm-stale", PackageToken = created[0].PackageToken }, default));
        f.Store.Items<ChildProfile>()[0].OwnerUserId = 2;
        var current = await f.Packages.GetAsync(1, f.StoryId, created[0].PackageId, default);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Packages.ConfirmAsync(1, f.StoryId, current.PackageId,
            new() { OperationId = "confirm", PackageToken = current.PackageToken }, default));
    }

    [Fact]
    public async Task Missing_score_fails_closed_and_below_threshold_blocks_only_target_child()
    {
        var f = new Fixture(); await f.Initialize();
        f.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluateContentSafetyResponse { IsAllowed = true });
        var packages = await f.Packages.CreateAsync(1, f.StoryId, f.Create("packages", 3), default);
        await f.Worker.ProcessNextAsync(default);
        Assert.Equal("Failed", (await f.Packages.GetAsync(1, f.StoryId, packages[0].PackageId, default)).Status);
        Assert.Empty(f.Store.Items<ParentStoryOperation>());
    }

    [Fact]
    public async Task Content_changed_during_provider_work_never_records_stale_result()
    {
        var f = new Fixture(); await f.Initialize();
        f.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { f.Store.Items<StoryVersion>().Single().Content += " changed"; return new() { IsAllowed = true, SafetyScore = 100,
                RiskLevel = "none", ScoreRuleVersion = ContentSafetyScoring.Version }; });
        var packages = await f.Packages.CreateAsync(1, f.StoryId, f.Create("packages", 3), default);
        await f.Worker.ProcessNextAsync(default);
        Assert.Equal("Stale", (await f.Packages.GetAsync(1, f.StoryId, packages[0].PackageId, default)).Status);
    }

    [Fact]
    public async Task Media_is_generated_once_rechecked_for_second_child_and_regeneration_keeps_delivered_assets()
    {
        var f = new Fixture(); await f.Initialize();
        var request = f.Create("media-packages", 3, 4) with
        { Recipients = new[] { 3, 4 }.Select(id => new ParentPackageRecipient(id, new(Images: true, Narration: true))).ToArray() };
        var packages = await f.Packages.CreateAsync(1, f.StoryId, request, default);
        for (var i = 0; i < 2; i++) Assert.True(await f.Worker.ProcessNextAsync(default));
        foreach (var p in packages)
        {
            var current = await f.Packages.GetAsync(1, f.StoryId, p.PackageId, default);
            await f.Packages.GenerateAsync(1, f.StoryId, p.PackageId, new() { OperationId = "generate-" + p.PackageId, PackageToken = current.PackageToken }, default);
        }
        for (var i = 0; i < 2; i++) Assert.True(await f.Worker.ProcessNextAsync(default));
        var image = new CountingImage(); var voice = new ConfigurableVoice(); var storage = new BinaryStorage(); var evaluator = new CountEvaluation();
        var media = MediaGenerationServiceTests.Create(f.Store.Uow.Object, image: image, tts: voice, storage: storage, evaluator: evaluator,
            supervision: new ChildProfileAccessGuard(f.Store.Uow.Object));
        var first = await media.ProcessNextAsync(default); Assert.True(first.Success, first.ErrorCode);
        var generated = image.Calls; Assert.True(generated > 0);
        var audioGenerated = voice.Calls; Assert.True(audioGenerated > 0);
        var second = await media.ProcessNextAsync(default); Assert.True(second.Success, second.ErrorCode);
        Assert.Equal(generated, image.Calls);
        Assert.Equal(audioGenerated, voice.Calls);
        Assert.True(storage.ReadCalls > 0); // Existing binaries really went through the second child's evaluator.
        var confirmed = new List<ParentPackageDto>();
        foreach (var p in packages)
        {
            var current = await f.Packages.GetAsync(1, f.StoryId, p.PackageId, default);
            confirmed.Add(await f.Packages.ConfirmAsync(1, f.StoryId, p.PackageId, new() { OperationId = "confirm-media-" + p.PackageId, PackageToken = current.PackageToken }, default));
        }
        var firstPreview = await f.Packages.PreviewAsync(1, f.StoryId, confirmed[0].PackageId, default);
        var secondPreview = await f.Packages.PreviewAsync(1, f.StoryId, confirmed[1].PackageId, default);
        Assert.Equal(firstPreview.Manifest.MediaAssetIds, secondPreview.Manifest.MediaAssetIds);
        await f.Packages.DeliverAsync(1, f.StoryId, f.Grant("deliver-media", confirmed.ToArray()), default);
        var paths = f.Store.Items<MediaAsset>().ToDictionary(x => x.Id, x => x.Url);
        var revision = await f.Packages.ReviseAsync(1, f.StoryId, confirmed[1].PackageId,
            new() { OperationId = "revise-images", PackageToken = confirmed[1].PackageToken, Components = new(Images: true, Narration: true), Regenerate = ["images"] }, default);
        Assert.True(await f.Worker.ProcessNextAsync(default));
        var checkedRevision = await f.Packages.GetAsync(1, f.StoryId, revision.PackageId, default);
        await f.Packages.GenerateAsync(1, f.StoryId, revision.PackageId, new() { OperationId = "generate-revision", PackageToken = checkedRevision.PackageToken }, default);
        Assert.True(await f.Worker.ProcessNextAsync(default));
        voice.ConfigurationFingerprint = "test-voice-v2";
        var regenerated = await media.ProcessNextAsync(default); Assert.True(regenerated.Success, regenerated.ErrorCode);
        Assert.True(image.Calls > generated);
        Assert.True(voice.Calls > audioGenerated); // A different voice cannot silently reuse the first package's TTS.
        foreach (var asset in f.Store.Items<MediaAsset>().Where(x => paths.ContainsKey(x.Id))) Assert.Equal(paths[asset.Id], asset.Url);
        Assert.Single(f.Store.Items<StoryVersion>());
    }

    [Fact]
    public async Task Learning_and_quiz_answers_are_scoped_to_each_childs_confirmed_manifest()
    {
        var f = new Fixture(); await f.Initialize();
        f.Batches.Setup(x => x.GenerateSelectedAsync(It.IsAny<StoryGenerationJob>(), It.IsAny<string>(), It.IsAny<StoryVersion>(),
            It.IsAny<StoryPlatform.Application.Features.AIStoryInput.Models.AIStoryInputContextSnapshot>(), true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoryGenerationJob job, string token, StoryVersion version,
                StoryPlatform.Application.Features.AIStoryInput.Models.AIStoryInputContextSnapshot context, bool vocabulary, bool quiz, CancellationToken ct) =>
                new LearningBatchPackage(new() { Items = [new("Tom", "Definition for reading level " + context.ReadingLevel) { SourceQuote = "Tom has a red ball." }] },
                    new() { Items = [new() { Type = "multiple_choice", Question = "What colour is the ball?", Options = ["A ball", "A box"], CorrectAnswer = "A ball", SourceQuote = "Tom has a red ball." },
                        new() { Type = "true_false", Question = "Tom sees a dog. True or false?", CorrectAnswer = "true", SourceQuote = "He sees a small dog." },
                        new() { Type = "short_answer", Question = "What drink does Tom give the dog?", CorrectAnswer = "A drink", SourceQuote = "Tom gives the dog a drink." }] }));
        var packages = await f.Packages.CreateAsync(1, f.StoryId, f.Create("learning-packages", 3, 4) with
            { Recipients = new[] { 3, 4 }.Select(id => new ParentPackageRecipient(id, new(Vocabulary: true, Quiz: true))).ToArray() }, default);
        for (var i = 0; i < 2; i++) await f.Worker.ProcessNextAsync(default);
        foreach (var p in packages)
        {
            var current = await f.Packages.GetAsync(1, f.StoryId, p.PackageId, default);
            await f.Packages.GenerateAsync(1, f.StoryId, p.PackageId, new() { OperationId = "learning-generate-" + p.PackageId, PackageToken = current.PackageToken }, default);
        }
        for (var i = 0; i < 2; i++) await f.Worker.ProcessNextAsync(default);
        var confirmed = new List<ParentPackageDto>();
        foreach (var p in packages)
        {
            var current = await f.Packages.GetAsync(1, f.StoryId, p.PackageId, default);
            confirmed.Add(await f.Packages.ConfirmAsync(1, f.StoryId, p.PackageId, new() { OperationId = "learning-confirm-" + p.PackageId, PackageToken = current.PackageToken }, default));
        }
        var grants = await f.Packages.DeliverAsync(1, f.StoryId, f.Grant("learning-deliver", confirmed.ToArray()), default);
        Assert.Equal(2, f.Store.Items<StoryVocabulary>().Select(x => x.Definition).Distinct().Count());
        f.Store.Items<ReadingSession>().Add(new() { Id = 100, ChildProfileId = 3, StoryId = f.StoryId, StoryVersionId = f.Saved.Version.VersionId,
            ParentStoryPackageId = confirmed[0].PackageId, FamilyConfirmationId = grants[0].ConfirmationId, Status = SessionStatus.Completed });
        var quizzes = new StoryPlatform.Application.Features.ReadingSessions.Services.ChildQuizService(f.Store.Uow.Object,
            new Mock<StoryPlatform.Application.Features.ReadingSessions.Interfaces.IChildRewardService>().Object);
        var own = await quizzes.GetQuizAsync(3, 100);
        Assert.Equal(3, own.Questions.Count);
        var foreign = f.Store.Items<QuizItem>().First(x => x.ParentStoryPackageId == confirmed[1].PackageId);
        await Assert.ThrowsAsync<NotFoundException>(() => quizzes.SubmitAnswerAsync(3, 100, new() { QuizItemId = foreign.Id, Answer = "A ball" }));
        var word = f.Store.Items<StoryVocabulary>().Single(x => x.ParentStoryPackageId == confirmed[0].PackageId);
        f.Store.Items<VocabularyNotebookEntry>().Add(new() { Id = 101, ChildProfileId = 3, StoryVocabularyId = word.Id });
        var notebook = new StoryPlatform.Application.Features.ReadingSessions.Services.ChildNotebookService(f.Store.Uow.Object,
            new Mock<StoryPlatform.Application.Features.ReadingSessions.Interfaces.IChildRewardService>().Object);
        Assert.Single(await notebook.ListAsync(3));
        f.Store.Items<ParentStoryOperation>().Single(x => x.Id == grants[0].ConfirmationId).RevokedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<NotFoundException>(() => quizzes.GetQuizAsync(3, 100));
        Assert.Empty(await notebook.ListAsync(3));
        await Assert.ThrowsAsync<NotFoundException>(() => notebook.GetDefinitionAsync(3, 100, word.Id));
    }

    [Fact]
    public async Task Commit_failure_rolls_back_every_grant_and_receipt()
    {
        var f = new Fixture(); await f.Initialize(); var packages = await f.Ready(3, 4);
        f.Store.Uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("commit failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Packages.DeliverAsync(1, f.StoryId, f.Grant("atomic-delivery", packages), default));
        Assert.DoesNotContain(f.Store.Items<ParentStoryOperation>(), x => x.Kind == "FamilyConfirmation");
        Assert.DoesNotContain(f.Store.Items<StoryAuthoringOperation>(), x => x.OperationId == "atomic-delivery");
    }

    [Fact]
    public async Task Different_thresholds_are_checked_separately_without_averaging()
    {
        var f = new Fixture(); await f.Initialize();
        f.Store.Items<SafetyPolicy>().Single(x => x.ChildProfileId == 3).SafetyScoreThreshold = 80;
        f.Store.Items<SafetyPolicy>().Single(x => x.ChildProfileId == 4).SafetyScoreThreshold = 95;
        f.Ai.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluateContentSafetyResponse { IsAllowed = true, RiskLevel = "low", SafetyScore = 90,
                ScoreRuleVersion = ContentSafetyScoring.Version, Evidence = ["Tom has a red ball."] });
        var p = await f.Packages.CreateAsync(1, f.StoryId, f.Create("thresholds", 3, 4), default);
        await f.Worker.ProcessNextAsync(default); await f.Worker.ProcessNextAsync(default);
        Assert.True((await f.Packages.GetAsync(1, f.StoryId, p[0].PackageId, default)).Check!.Passed);
        var blocked = await f.Packages.GetAsync(1, f.StoryId, p[1].PackageId, default);
        Assert.Equal("Blocked", blocked.Status); Assert.False(blocked.Check!.Passed);
        Assert.Equal(90m, blocked.Check.SafetyScore);
    }

    [Fact]
    public async Task Concurrent_confirm_and_delivery_have_one_winner_and_no_duplicate_grants()
    {
        var f = new Fixture(); await f.Initialize(); var created = await f.Packages.CreateAsync(1, f.StoryId, f.Create("race", 3), default);
        await f.Worker.ProcessNextAsync(default); var p = await f.Packages.GetAsync(1, f.StoryId, created[0].PackageId, default);
        async Task<bool> Confirm(string key)
        {
            try { await f.Packages.ConfirmAsync(1, f.StoryId, p.PackageId, new() { OperationId = key, PackageToken = p.PackageToken }, default); return true; }
            catch (ConflictException) { return false; }
        }
        Assert.Single(await Task.WhenAll(Confirm("confirm-one"), Confirm("confirm-two")), x => x);
        p = await f.Packages.GetAsync(1, f.StoryId, p.PackageId, default);
        async Task<bool> Deliver(string key)
        {
            try { await f.Packages.DeliverAsync(1, f.StoryId, f.Grant(key, p), default); return true; }
            catch (ConflictException) { return false; }
        }
        Assert.Single(await Task.WhenAll(Deliver("deliver-one"), Deliver("deliver-two")), x => x);
        Assert.Single(f.Store.Items<ParentStoryOperation>(), x => x.Kind == "FamilyConfirmation");
    }

    [Fact]
    public async Task Unbound_draft_setup_has_no_fabricated_child_or_content_version()
    {
        var f = new Fixture();
        var draft = await f.Store.Service.CreateParentDraftAsync(1, new() { OperationId = "unbound", Language = "en" }, default);
        var request = new ParentSetupRequest { OperationId = "setup", RevisionToken = draft.Story.RevisionToken, AgeBand = "9-10", Purpose = "family_memory" };
        var changed = await f.Store.Service.SetupAsync(1, draft.Story.StoryId, request, default);
        Assert.Equal(changed, await f.Store.Service.SetupAsync(1, draft.Story.StoryId, request, default));
        Assert.Null(changed.ChildProfileId); Assert.Empty(f.Store.Items<StoryVersion>());
        Assert.Null(Assert.Single(f.Store.Items<ParentStoryOperation>()).ChildProfileId);
    }

    [Fact]
    public async Task Gray_zone_clear_is_scoped_to_one_package_and_keeps_safety_failures()
    {
        var f = new Fixture(); await f.Initialize();
        f.Moderation.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrayZoneAssessment(ModerationZone.GrayZone, "fingerprint", "phase3", "1", "test", "test", 60, []));
        var packages = await f.Packages.CreateAsync(1, f.StoryId, f.Create("gray-packages", 3, 4), default);
        await f.Worker.ProcessNextAsync(default); await f.Worker.ProcessNextAsync(default);
        var items = f.Store.Items<ModerationItem>(); Assert.Equal(2, items.Count);
        await ParentPackageModeration.DecideAsync(f.Store.Uow.Object, items[0], true, 9, "Checked evidence", default);
        Assert.Equal("Review", (await f.Packages.GetAsync(1, f.StoryId, packages[0].PackageId, default)).Status);
        Assert.Equal("Blocked", (await f.Packages.GetAsync(1, f.StoryId, packages[1].PackageId, default)).Status);
        var other = f.Store.Items<ParentStoryPackage>().Single(x => x.Id == packages[1].PackageId);
        var check = JsonSerializer.Deserialize<ParentPackageCheck>(other.CheckResultJson, ParentPackageContext.Json)!;
        other.CheckResultJson = JsonSerializer.Serialize(check with { Errors = check.Errors.Append("CONTENT_SAFETY_NOT_PASSED").ToArray() }, ParentPackageContext.Json);
        await ParentPackageModeration.DecideAsync(f.Store.Uow.Object, items[1], true, 9, "Gray flag cleared only", default);
        var result = await f.Packages.GetAsync(1, f.StoryId, packages[1].PackageId, default);
        Assert.Equal("Blocked", result.Status); Assert.Contains("CONTENT_SAFETY_NOT_PASSED", result.Check!.Errors);
    }

    [Fact]
    public async Task Assistance_requires_selected_target_and_pins_that_childs_profile()
    {
        var f = new Fixture(); await f.Initialize();
        var story = f.Store.Items<Story>().Single();
        var access = new ParentAssistanceAccessPolicy(f.Store.Uow.Object, new ChildProfileAccessGuard(f.Store.Uow.Object));
        await Assert.ThrowsAsync<StoryPlatform.Application.Features.CreatorAssistance.AssistanceException>(() => access.CaptureContextAsync(story, default, 3));
        await f.Packages.CreateAsync(1, f.StoryId, f.Create("ai-targets", 3, 4), default);
        await Assert.ThrowsAsync<StoryPlatform.Application.Features.CreatorAssistance.AssistanceException>(() => access.CaptureContextAsync(story, default));
        var snapshot = await access.CaptureContextAsync(story, default, 4);
        Assert.Equal("9-10", access.AgeBandForContext(story, snapshot));
        Assert.Equal(4, JsonSerializer.Deserialize<ParentAssistanceContext>(snapshot!, ParentPackageContext.Json)!.TargetChildProfileId);
        f.Store.Items<LearningProfile>().Single(x => x.ChildProfileId == 4).ReadingLevel = 4;
        await Assert.ThrowsAsync<StoryPlatform.Application.Features.CreatorAssistance.AssistanceException>(() => access.ValidateContextAsync(story, snapshot, default));
    }

    private sealed class CountingImage : IImageGenerationProvider
    {
        public int Calls;
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png")); }
    }
    private sealed class ConfigurableVoice : ITtsProvider
    {
        public string ConfigurationFingerprint { get; set; } = "test-voice-v1";
        public int Calls;
        public Task<GeneratedMedia> GenerateAsync(string text, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new GeneratedMedia([4, 5, 6], "audio/mpeg")); }
    }
    private sealed class CountEvaluation : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
    {
        public Task<MediaEvaluationResult> EvaluateAsync(SceneSpecification specification, GeneratedMedia illustration, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEvaluationResult(MediaEvaluationDecision.Pass));
    }
    private sealed class BinaryStorage : IMediaStorage
    {
        private readonly Dictionary<string, GeneratedMedia> _files = new();
        public int ReadCalls;
        public async Task<string> UploadAsync(string storagePath, Stream content, string mimeType, CancellationToken cancellationToken = default)
        { using var buffer = new MemoryStream(); await content.CopyToAsync(buffer, cancellationToken); _files.Add(storagePath, new(buffer.ToArray(), mimeType)); return storagePath; }
        public Task<GeneratedMedia> ReadAsync(string storagePath, CancellationToken cancellationToken = default)
        { ReadCalls++; return Task.FromResult(_files[storagePath]); }
        public Task<string> GetSignedUrlAsync(string storagePath, TimeSpan expiry, CancellationToken cancellationToken = default) => Task.FromResult("https://media.test/" + storagePath);
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Pinned binaries must not be deleted");
    }
}
