using Moq;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ListeningInteractions;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class StoryReviewListeningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creator_package_confirmation_only_ignores_reports_pinned_to_a_previous_version(bool currentReport)
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        uow.Items<UserAccount>().Single().Role = UserRole.ContentCreator;
        var story = uow.Items<Story>().Single();
        CreatorGenerationTestContext.Configure(story, uow.Items<StoryGenerationRequest>().Single());
        uow.Seed(new ModerationItem { Id = 21, StoryId = story.Id,
            StoryVersionId = currentReport ? 1 : 99, ItemType = ModerationItemType.ContentReport, Status = ModerationItemStatus.Pending });
        var evaluator = new Mock<IGrayZoneEvaluator>();
        evaluator.SetupGet(x => x.IsConfigured).Returns(true);
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrayZoneScan scan, CancellationToken _) => new GrayZoneAssessment(ModerationZone.Safe,
                GrayZoneEvaluator.Fingerprint(scan), scan.Phase, "policy", "fixture", "fixture", 0, []));
        var workflow = new GrayZoneWorkflowService(uow, new Mock<IAuditLogWriter>().Object);
        var check = new GrayZoneContentCheckService(uow, evaluator.Object, workflow, new Mock<IChildProfileAccessGuard>().Object);
        var service = new StoryReviewService(uow, ListeningTestStory.AiClient().Object,
            new InMemoryProposalCache(), new InMemoryReviewCompletionStore(), moderation: check);
        var input = new ConfirmStoryPackageRequestDto { VersionId = 1, OperationId = "confirm-revised-package-01" };

        if (currentReport)
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmPackageAsync(1, story.Id, input));
            Assert.Equal("MODERATION_SOURCE_LOCKED", error.Message);
            Assert.Empty(uow.Items<StoryGenerationJob>());
        }
        else
        {
            await service.ConfirmPackageAsync(1, story.Id, input);
            Assert.Equal(StoryStatus.MediaProcessing, story.Status);
            Assert.Single(uow.Items<StoryGenerationJob>(), j => j.Operation == GenerationJobOperation.GenerateMediaPackage);
            Assert.Single(uow.Items<StoryAuthoringOperation>());
        }
        Assert.Equal(ModerationItemStatus.Pending, uow.Items<ModerationItem>().Single().Status);
    }

    [Fact]
    public async Task Cached_story_proposal_cannot_mutate_a_confirmed_package()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, _, cache) = Build(uow);
        var original = uow.Items<StoryVersion>().Single().Content;
        var proposalId = await cache.CreateAsync(new AIProposalDto
        {
            RequestedByUserId = 1, StoryId = 1, StoryVersionId = 1,
            ArtifactType = "story", OperationType = "partial_edit",
            SuggestedContent = JsonSerializer.SerializeToElement(new { content = "Nội dung mới chưa được xác nhận." }),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        await service.ConfirmPackageAsync(1, 1, new() { VersionId = 1, OperationId = "confirm-before-apply-01" });

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new ProposalService(cache, uow).ApplyProposalAsync(1, 1, proposalId));

        Assert.Equal("CONFIRMED_CONTENT_PACKAGE_IMMUTABLE", error.Message);
        Assert.Equal(original, uow.Items<StoryVersion>().Single().Content);
        Assert.Equal("pending", (await cache.GetAsync(proposalId))!.Status);
        Assert.Single(uow.Items<StoryGenerationJob>(), x => x.Operation == GenerationJobOperation.GenerateMediaPackage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_package_confirmation_is_durable_and_does_not_need_separate_listening_click(bool creator)
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        if (creator)
        {
            uow.Items<UserAccount>().Single().Role = UserRole.ContentCreator;
            CreatorGenerationTestContext.Configure(uow.Items<Story>().Single(), uow.Items<StoryGenerationRequest>().Single());
        }
        var (service, _, _) = Build(uow);
        Assert.Contains((await service.ValidateAsync(1, 1)).Checks, x => x.Name == "listening_reviewed" && !x.Passed);
        var input = new ConfirmStoryPackageRequestDto { VersionId = 1, OperationId = "confirm-whole-package-01" };
        var first = await service.ConfirmPackageAsync(1, 1, input);
        var (restarted, _, _) = Build(uow);
        var replay = await restarted.ConfirmPackageAsync(1, 1, input);
        Assert.Equal(first, replay);
        Assert.Single(uow.Items<StoryGenerationJob>(), x => x.Operation == GenerationJobOperation.GenerateMediaPackage);
        Assert.Single(uow.Items<StoryAuthoringOperation>());
        Assert.False(uow.Items<Story>().Single().IsPublished);
        Assert.Equal(creator ? StoryStatus.MediaProcessing : StoryStatus.Approved, uow.Items<Story>().Single().Status);
        await Assert.ThrowsAsync<ConflictException>(() => restarted.UpdateVocabularyAsync(1, 1,
            new UpdateVocabularyRequestDto { VersionId = 1 }));
    }

    [Theory]
    [InlineData("wrong-user")]
    [InlineData("stale-version")]
    [InlineData("invalid-listening")]
    [InlineData("policy-changed")]
    public async Task Package_confirmation_never_bypasses_permission_version_quality_or_policy(string scenario)
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        if (scenario == "invalid-listening") uow.Items<ListeningInteraction>().First().Prompt = "";
        if (scenario == "policy-changed") uow.Seed(new SafetyPolicy { Id = 1, ChildProfileId = 1, ReadabilityScoreThreshold = 99m });
        var (service, _, _) = Build(uow);
        var error = await Record.ExceptionAsync(() => service.ConfirmPackageAsync(scenario == "wrong-user" ? 99 : 1, 1,
            new() { VersionId = scenario == "stale-version" ? 99 : 1, OperationId = "confirm-blocked-package-01" }));
        if (scenario == "wrong-user") Assert.IsType<ForbiddenException>(error);
        else if (scenario == "invalid-listening") Assert.IsType<BadRequestException>(error);
        else Assert.IsType<ConflictException>(error);
        if (scenario == "policy-changed") Assert.Equal("READABILITY_POLICY_CHANGED", error!.Message);
        Assert.Empty(uow.Items<StoryGenerationJob>());
        Assert.Empty(uow.Items<StoryAuthoringOperation>());
        Assert.Equal(StoryStatus.ContentReview, uow.Items<Story>().Single().Status);
    }

    private static (StoryReviewService Service, InMemoryReviewCompletionStore Store, InMemoryProposalCache Cache) Build(
        StoryReviewServiceTests.FakeUnitOfWork uow)
    {
        var store = new InMemoryReviewCompletionStore();
        var cache = new InMemoryProposalCache();
        return (new StoryReviewService(uow, ListeningTestStory.AiClient().Object, cache, store), store, cache);
    }

    private static ListeningInteraction First(StoryReviewServiceTests.FakeUnitOfWork uow, ListeningInteractionType type) =>
        uow.Items<ListeningInteraction>().Where(x => x.Type == type && !x.IsDeleted).OrderBy(x => x.Id).First();

    [Fact]
    public async Task Get_returns_plan_items_and_no_issues_for_a_generated_package()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, _, _) = Build(uow);

        var dto = await service.GetListeningForReviewAsync(1, 1);

        Assert.True(dto.Required);
        Assert.False(dto.Reviewed);
        Assert.Empty(dto.Issues);
        Assert.Equal(dto.Plan.Sum(p => p.Count), dto.Items.Count);
        Assert.Contains(dto.Plan, p => p.Type == "EventOrdering");
    }

    [Fact]
    public async Task Single_paragraph_story_does_not_require_listening()
    {
        var (service, _, _) = Build(StoryReviewServiceTests.SeedContentReview());

        var dto = await service.GetListeningForReviewAsync(1, 1);

        Assert.False(dto.Required);
        Assert.Empty(dto.Plan);
        Assert.True(await service.CompleteListeningReviewAsync(1, 1)); // không bắt buộc thì bấm cũng không lỗi
    }

    [Fact]
    public void Unknown_fields_land_in_extension_data()
    {
        var input = JsonSerializer.Deserialize<UpdateListeningInteractionRequestDto>(
            "{\"versionId\":1,\"hiddenWord\":\"x\",\"options\":[{\"id\":1,\"isMatch\":true}]}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.True(input.ExtensionData!.ContainsKey("hiddenWord"));
        Assert.True(input.Options![0].ExtensionData!.ContainsKey("isMatch"));
    }

    [Fact]
    public async Task Update_rejects_non_editable_field()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, _, _) = Build(uow);
        var target = First(uow, ListeningInteractionType.Checkpoint);

        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateListeningInteractionAsync(1, 1, target.Id,
            new UpdateListeningInteractionRequestDto { VersionId = 1, ExtensionData = new() { ["anchorOffset"] = JsonSerializer.SerializeToElement(3) } }));

        Assert.Equal("FIELD_NOT_EDITABLE", error.Message);
    }

    [Fact]
    public async Task Update_with_grading_word_is_rejected_and_nothing_is_saved()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, _, _) = Build(uow);
        var target = First(uow, ListeningInteractionType.Checkpoint);
        var before = target.Prompt;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateListeningInteractionAsync(1, 1, target.Id,
            new UpdateListeningInteractionRequestDto { VersionId = 1, Prompt = "Sai rồi, Thỏ đang làm gì?" }));

        Assert.StartsWith("LISTENING_TEXT_INVALID:", error.Message);
        Assert.Contains("GRADING_WORD", error.Message);
        Assert.Equal(before, target.Prompt);
    }

    [Fact]
    public async Task Update_saves_text_retires_media_and_clears_confirmation()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, store, _) = Build(uow);
        var target = First(uow, ListeningInteractionType.Checkpoint);
        var media = new MediaAsset { Id = 1, ListeningInteractionId = target.Id, IsCurrent = true };
        uow.Seed(media);
        Assert.True(await service.CompleteListeningReviewAsync(1, 1));

        var dto = await service.UpdateListeningInteractionAsync(1, 1, target.Id,
            new UpdateListeningInteractionRequestDto { VersionId = 1, Prompt = "  Lúc trời tối, Thỏ thấy thế nào?  " });

        Assert.Equal("Lúc trời tối, Thỏ thấy thế nào?", target.Prompt);
        Assert.False(media.IsCurrent);
        Assert.False(store.IsListeningReviewed(1, 1));
        Assert.False(dto.Reviewed);
    }

    [Fact]
    public async Task Update_rejects_option_of_another_interaction_and_stale_version()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, _, _) = Build(uow);
        var target = First(uow, ListeningInteractionType.Checkpoint);
        var foreign = uow.Items<ListeningInteractionOption>().First(o => o.ListeningInteractionId != target.Id);

        var notFound = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateListeningInteractionAsync(1, 1, target.Id,
            new UpdateListeningInteractionRequestDto { VersionId = 1, Options = [new UpdateListeningOptionDto { Id = foreign.Id, Text = "Thỏ" }] }));
        var stale = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateListeningInteractionAsync(1, 1, target.Id,
            new UpdateListeningInteractionRequestDto { VersionId = 99, Prompt = "Thỏ thấy thế nào?" }));

        Assert.Equal("LISTENING_OPTION_NOT_FOUND", notFound.Message);
        Assert.Equal("STALE_STORY_VERSION", stale.Message);
    }

    [Fact]
    public async Task Complete_rejects_an_incomplete_package()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow, ListeningInteractionType.Checkpoint);
        var (service, store, _) = Build(uow);

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.CompleteListeningReviewAsync(1, 1));

        Assert.Equal("LISTENING_PACKAGE_INVALID", error.Message);
        Assert.False(store.IsListeningReviewed(1, 1));
    }

    [Fact]
    public async Task Complete_marks_a_valid_package()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, store, _) = Build(uow);

        Assert.True(await service.CompleteListeningReviewAsync(1, 1));

        Assert.True(store.IsListeningReviewed(1, 1));
        Assert.True((await service.GetListeningForReviewAsync(1, 1)).Reviewed);
    }
    [Fact]
    public async Task Regenerate_then_apply_replaces_only_that_type_and_clears_confirmation()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, store, cache) = Build(uow);
        Assert.True(await service.CompleteListeningReviewAsync(1, 1));
        var oldRolePlay = First(uow, ListeningInteractionType.RolePlay);
        var checkpointIds = uow.Items<ListeningInteraction>().Where(x => x.Type == ListeningInteractionType.Checkpoint).Select(x => x.Id).ToArray();

        var created = await service.CreateListeningProposalAsync(1, 1, ListeningInteractionType.RolePlay);
        Assert.False(oldRolePlay.IsDeleted); // chưa áp thì chưa đổi
        var applied = await new ProposalService(cache, uow, service).ApplyProposalAsync(1, 1, created.ProposalId);

        Assert.True(applied.Success);
        Assert.True(oldRolePlay.IsDeleted);
        Assert.Single(uow.Items<ListeningInteraction>(), x => x.Type == ListeningInteractionType.RolePlay && !x.IsDeleted);
        Assert.Equal(checkpointIds, uow.Items<ListeningInteraction>()
            .Where(x => x.Type == ListeningInteractionType.Checkpoint && !x.IsDeleted).Select(x => x.Id).ToArray());
        Assert.False(store.IsListeningReviewed(1, 1));
        Assert.Empty((await service.GetListeningForReviewAsync(1, 1)).Issues);
    }

    [Fact]
    public async Task Regenerate_of_a_type_outside_the_plan_conflicts()
    {
        var uow = ListeningTestStory.SeedReviewStory(); // 6-8 không có FillBlank
        var (service, _, _) = Build(uow);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateListeningProposalAsync(1, 1, ListeningInteractionType.FillBlank));

        Assert.Equal("LISTENING_TYPE_NOT_IN_PLAN", error.Message);
    }

    [Fact]
    public async Task Proposal_for_an_old_version_is_rejected()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, _, cache) = Build(uow);
        var created = await service.CreateListeningProposalAsync(1, 1, ListeningInteractionType.Checkpoint);
        var v1 = uow.Items<StoryVersion>().Single();
        v1.IsCurrent = false;
        uow.Seed(new StoryVersion { Id = 2, StoryId = 1, VersionNo = 2, Title = v1.Title, Content = v1.Content, Lesson = v1.Lesson, IsCurrent = true });

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new ProposalService(cache, uow, service).ApplyProposalAsync(1, 1, created.ProposalId));

        Assert.Equal("STALE_PROPOSAL_VERSION", error.Message);
    }

    [Fact]
    public async Task Validation_failure_while_regenerating_returns_the_type_code()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        var ai = ListeningTestStory.AiClient();
        ai.Setup(x => x.GenerateListeningInteractionAsync(Moq.It.IsAny<StoryPlatform.Contracts.AI.Requests.GenerateListeningInteractionRequest>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoryPlatform.Contracts.AI.Responses.GenerateListeningInteractionResponse()); // rỗng → COUNT_MISMATCH mãi
        var service = new StoryReviewService(uow, ai.Object, new InMemoryProposalCache(), new InMemoryReviewCompletionStore());

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateListeningProposalAsync(1, 1, ListeningInteractionType.RolePlay));

        Assert.Equal("ROLE_PLAY_VALIDATION_FAILED", error.Message);
    }
    private static ValidationCheckDto Check(ValidationResultDto result, string name) => result.Checks.Single(c => c.Name == name);

    [Fact]
    public async Task Validate_requires_a_valid_package_and_reviewer_confirmation()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var (service, _, _) = Build(uow);

        var before = await service.ValidateAsync(1, 1);
        await service.CompleteListeningReviewAsync(1, 1);
        var after = await service.ValidateAsync(1, 1);

        Assert.True(Check(before, "listening_valid").Passed);
        Assert.False(Check(before, "listening_reviewed").Passed);
        Assert.False(before.CanApprove);
        Assert.True(Check(after, "listening_reviewed").Passed);
    }

    [Fact]
    public async Task Validate_reports_missing_types_and_missing_context()
    {
        var missing = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(missing, ListeningInteractionType.Checkpoint);
        var noContext = ListeningTestStory.SeedReviewStory(withContext: false);

        var missingCheck = Check(await Build(missing).Service.ValidateAsync(1, 1), "listening_valid");
        var noContextCheck = Check(await Build(noContext).Service.ValidateAsync(1, 1), "listening_valid");

        Assert.False(missingCheck.Passed);
        Assert.Contains("LISTENING_MISSING", missingCheck.Message);
        Assert.False(noContextCheck.Passed);
        Assert.Contains("LISTENING_CONTEXT_MISSING", noContextCheck.Message);
    }

    [Fact]
    public async Task Validate_skips_listening_for_single_paragraph_and_reading_media_only()
    {
        var single = StoryReviewServiceTests.SeedContentReview();
        var mediaOnly = ListeningTestStory.SeedReviewStory();
        mediaOnly.Items<Story>().Single().OutputMode = StoryOutputMode.ReadingMediaOnly;

        foreach (var uow in new[] { single, mediaOnly })
        {
            var result = await Build(uow).Service.ValidateAsync(1, 1);
            Assert.True(Check(result, "listening_valid").Passed);
            Assert.True(Check(result, "listening_reviewed").Passed);
        }
    }

    [Fact]
    public async Task Auto_publish_waits_for_listening_confirmation()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        uow.Seed(new SafetyPolicy
        {
            Id = 1, ChildProfileId = 1, ParentalGateEnabled = false,
            RequiredApprovalMode = ApprovalMode.AutoPublishOnThreshold, SafetyScoreThreshold = 80m
        });
        var (service, _, _) = Build(uow);

        Assert.Null(await service.EvaluateAndApplyAutoPublishAsync(1));
        Assert.Equal(StoryStatus.ContentReview, uow.Items<Story>().Single().Status);

        await service.CompleteListeningReviewAsync(1, 1);
        var published = await service.EvaluateAndApplyAutoPublishAsync(1);

        Assert.NotNull(published);
        Assert.Equal(StoryStatus.Approved, uow.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task Review_package_shows_listening_status()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        var (service, _, _) = Build(uow);
        Assert.Equal("pending", (await service.GetReviewPackageAsync(1, 1)).Listening.State);

        await ListeningTestStory.SeedPackageAsync(uow);
        var ready = (await service.GetReviewPackageAsync(1, 1)).Listening;

        Assert.Equal("completed", ready.State);
        Assert.Equal(uow.Items<ListeningInteraction>().Count(x => !x.IsDeleted), ready.ItemCount);
        Assert.Equal("skipped", (await Build(StoryReviewServiceTests.SeedContentReview()).Service.GetReviewPackageAsync(1, 1)).Listening.State);
    }
    private static LearningArtifactService Artifacts(StoryReviewServiceTests.FakeUnitOfWork uow, Moq.Mock<StoryPlatform.Application.Abstractions.AI.IAIStoryGenerationClient> ai, InMemoryReviewCompletionStore store)
    {
        var guard = new Moq.Mock<StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces.IChildProfileAccessGuard>();
        return new LearningArtifactService(uow, ai.Object, guard.Object, reviewCompletionStore: store);
    }

    private static GenerateArtifactsRequestDto ListeningOnly => new() { IncludeVocabulary = false, IncludeQuiz = false, IncludeListening = true };

    [Fact]
    public async Task Generate_artifacts_rebuilds_every_plan_type_and_clears_confirmation()
    {
        var uow = ListeningTestStory.SeedReviewStory();
        await ListeningTestStory.SeedPackageAsync(uow);
        var store = new InMemoryReviewCompletionStore();
        store.MarkListeningReviewed(1, 1, 1);
        var old = uow.Items<ListeningInteraction>().ToArray();

        var result = await Artifacts(uow, ListeningTestStory.AiClient(), store).GenerateArtifactsAsync(1, 1, ListeningOnly);

        Assert.Equal(old.Length, result.ListeningCount);
        Assert.All(old, x => Assert.True(x.IsDeleted));
        Assert.Equal(old.Length, uow.Items<ListeningInteraction>().Count(x => !x.IsDeleted));
        Assert.False(store.IsListeningReviewed(1, 1));
    }

    [Fact]
    public async Task Generate_artifacts_skips_listening_without_calling_ai_for_single_paragraph_or_manual_story()
    {
        var single = StoryReviewServiceTests.SeedContentReview();
        var manual = ListeningTestStory.SeedReviewStory();
        manual.Items<Story>().Single().Source = StorySource.Manual;

        foreach (var uow in new[] { single, manual })
        {
            var ai = ListeningTestStory.AiClient();
            var result = await Artifacts(uow, ai, new InMemoryReviewCompletionStore()).GenerateArtifactsAsync(1, 1, ListeningOnly);
            Assert.Equal(0, result.ListeningCount);
            ai.Verify(x => x.GenerateListeningInteractionAsync(Moq.It.IsAny<StoryPlatform.Contracts.AI.Requests.GenerateListeningInteractionRequest>(), Moq.It.IsAny<CancellationToken>()), Moq.Times.Never);
        }
    }
}
