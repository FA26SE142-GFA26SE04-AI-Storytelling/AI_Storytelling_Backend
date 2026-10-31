using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Services;
using StoryPlatform.Application.Features.CreatorAuthoring;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>One test per guard/exception branch of AIStoryInputService (Report 5, Việc 3.3).</summary>
public sealed partial class AIStoryInputServiceTests
{
    private const string ActiveDraftMessage = "Story draft đang có một yêu cầu input hoạt động hoặc đang chờ Generate Outline.";

    // ---- Creative input validation ----

    [Fact]
    public async Task SubmitAsync_TopicCollapsesToOneCharacter_ThrowsBadRequest()
    {
        var store = CreateEligibleUnitOfWork();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            ParentService(store).SubmitAsync(1, ValidRequest(topic: "a ")));

        Assert.Equal("Topic và Lesson là bắt buộc và phải nằm trong giới hạn cho phép.", error.Message);
        Assert.Empty(store.Items<Story>());
    }

    [Fact]
    public async Task SubmitAsync_SpecifiedCharacterModeWithoutCharacters_ThrowsBadRequest()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            ParentService(CreateEligibleUnitOfWork()).SubmitAsync(1, With(ValidRequest(), characterMode: "specified")));

        Assert.Equal("Character mode 'specified' yêu cầu ít nhất một mô tả nhân vật.", error.Message);
    }

    [Fact]
    public async Task SubmitAsync_ElevenCharacters_ThrowsBadRequest()
    {
        var characters = Enumerable.Range(1, 11).Select(index => $"Nhân vật {index}").ToArray();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            ParentService(CreateEligibleUnitOfWork()).SubmitAsync(1, With(ValidRequest(), characterMode: "specified", characters: characters)));

        Assert.Equal("Danh sách nhân vật tối đa 10 mục và mỗi mục tối đa 100 ký tự.", error.Message);
    }

    [Fact]
    public async Task SubmitAsync_CharacterLongerThan100_ThrowsBadRequest()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            ParentService(CreateEligibleUnitOfWork()).SubmitAsync(1, With(ValidRequest(), characterMode: "specified", characters: [new string('a', 101)])));

        Assert.Equal("Danh sách nhân vật tối đa 10 mục và mỗi mục tối đa 100 ký tự.", error.Message);
    }

    [Fact]
    public async Task SubmitAsync_SpecifiedSettingModeWithBlankSetting_ThrowsBadRequest()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            ParentService(CreateEligibleUnitOfWork()).SubmitAsync(1, With(ValidRequest(), settingMode: "specified", setting: "   ")));

        Assert.Equal("Setting mode 'specified' yêu cầu mô tả bối cảnh.", error.Message);
    }

    [Fact]
    public async Task SubmitAsync_IdempotencyKeyTooShort_ThrowsBadRequest()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            ParentService(CreateEligibleUnitOfWork()).SubmitAsync(1, With(ValidRequest(), idempotencyKey: "short")));

        Assert.Contains("IdempotencyKey", error.Message);
    }

    [Fact]
    public async Task SubmitAsync_SpecifiedCharactersAndSetting_AreNormalizedIntoAcceptedSnapshot()
    {
        var store = CreateEligibleUnitOfWork();

        var result = await ParentService(store).SubmitAsync(1, With(ValidRequest(), characterMode: "specified",
            characters: ["  Mèo   con ", " "], settingMode: "specified", setting: "  Khu   rừng "));

        Assert.Equal("input_accepted", result.InputStatus);
        var accepted = System.Text.Json.JsonSerializer.Deserialize<StoryPlatform.Application.Features.AIStoryInput.Models.AcceptedAIStoryInputSnapshot>(
            Assert.Single(store.Items<StoryGenerationRequest>()).AcceptedInputJson!,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(["Mèo con"], accepted.Characters);
        Assert.Equal("Khu rừng", accepted.Setting);
    }

    // ---- Parent context resolution ----

    [Fact]
    public async Task GetContextAsync_UnknownUser_ThrowsForbidden()
    {
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => ParentService(CreateEligibleUnitOfWork()).GetContextAsync(99, 1));

        Assert.Equal("Bạn không có quyền truy cập tài nguyên này.", error.Message);
    }

    [Fact]
    public async Task GetContextAsync_SuspendedParent_ThrowsForbidden()
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<UserAccount>().Single().Status = AccountStatus.Suspended;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => ParentService(store).GetContextAsync(1, 1));

        Assert.Equal("Tài khoản không đủ điều kiện tạo truyện cho Child Profile.", error.Message);
    }

    [Fact]
    public async Task GetContextAsync_UserNotChildOwner_ThrowsForbidden()
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<ChildProfile>().Single().OwnerUserId = 99;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => ParentService(store).GetContextAsync(1, 1));

        Assert.Equal("Bạn không phải người quản lý Child Profile này.", error.Message);
    }

    [Fact]
    public async Task GetContextAsync_ChildProfileNotActive_ThrowsConflict()
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<ChildProfile>().Single().Status = ChildProfileStatus.Archived;

        var error = await Assert.ThrowsAsync<ConflictException>(() => ParentService(store).GetContextAsync(1, 1));

        Assert.Equal("Child Profile phải đang Active để tạo truyện.", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task GetContextAsync_ReadingLevelOutsideOneToFive_ThrowsBadRequest(int readingLevel)
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<LearningProfile>().Single().ReadingLevel = readingLevel;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => ParentService(store).GetContextAsync(1, 1));

        Assert.Equal("Reading Level của Child Profile phải nằm trong thang 1-5.", error.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task GetContextAsync_SafetyScoreThresholdOutOfRange_ThrowsBadRequest(int threshold)
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<SafetyPolicy>().Single().SafetyScoreThreshold = threshold;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => ParentService(store).GetContextAsync(1, 1));

        Assert.Equal("Safety Score Threshold phải nằm trong khoảng 0 đến 100.", error.Message);
    }

    [Fact]
    public async Task GetContextAsync_ComprehensionWindowZero_ThrowsBadRequest()
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<SafetyPolicy>().Single().ComprehensionWindowSize = 0;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => ParentService(store).GetContextAsync(1, 1));

        Assert.Equal("Cấu hình comprehension của Safety Policy không hợp lệ.", error.Message);
    }

    [Fact]
    public async Task GetContextAsync_ComprehensionThresholdAbove100_ThrowsBadRequest()
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<SafetyPolicy>().Single().ComprehensionThresholdPercent = 101m;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => ParentService(store).GetContextAsync(1, 1));

        Assert.Equal("Cấu hình comprehension của Safety Policy không hợp lệ.", error.Message);
    }

    [Fact]
    public async Task GetContextAsync_MaxStoryLengthZero_ThrowsBadRequest()
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<SafetyPolicy>().Single().MaxStoryLength = 0;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => ParentService(store).GetContextAsync(1, 1));

        Assert.Equal("Safety Policy chưa có Maximum Story Length hợp lệ.", error.Message);
    }

    [Fact]
    public async Task GetContextAsync_AutoPublishPolicyWithoutGate_ReturnsAutoPublishMode()
    {
        var store = CreateEligibleUnitOfWork();
        var policy = store.Items<SafetyPolicy>().Single();
        policy.RequiredApprovalMode = ApprovalMode.AutoPublishOnThreshold;
        policy.ParentalGateEnabled = false;

        var context = await ParentService(store).GetContextAsync(1, 1);

        Assert.Equal("auto_publish_on_threshold", context.RequiredApprovalMode);
        Assert.Equal("6-8", context.AgeBand);
    }

    [Fact]
    public async Task GetContextAsync_NineToTenChild_ReportsNineToTenAgeBand()
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<ChildProfile>().Single().AgeBand = AgeBand.Age_9_10;

        Assert.Equal("9-10", (await ParentService(store).GetContextAsync(1, 1)).AgeBand);
    }

    [Fact]
    public async Task SubmitAsync_LanguageDiffersFromChildProfile_ThrowsBadRequest()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            ParentService(CreateEligibleUnitOfWork()).SubmitAsync(1, With(ValidRequest(), language: "en")));

        Assert.Equal("Ngôn ngữ đã chọn chưa được cấu hình cho Child Profile.", error.Message);
    }

    // ---- Reusing an existing draft ----

    [Fact]
    public async Task SubmitAsync_ExistingDraftNotFound_ThrowsNotFound()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() =>
            ParentService(CreateEligibleUnitOfWork()).SubmitAsync(1, With(ValidRequest(), existingStoryId: 40)));

        Assert.Equal("Không tìm thấy Story draft với mã định danh [40].", error.Message);
    }

    [Fact]
    public async Task SubmitAsync_ExistingStoryNotDraft_ThrowsConflict()
    {
        var store = CreateEligibleUnitOfWork();
        store.Seed(ParentDraft(40, StoryStatus.OutlineReview));

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            ParentService(store).SubmitAsync(1, With(ValidRequest(), existingStoryId: 40)));

        Assert.Equal("Chỉ có thể tiếp tục một AI Story đang ở trạng thái draft.", error.Message);
    }

    [Fact]
    public async Task SubmitAsync_ExistingDraftWithActiveRequest_ThrowsConflict()
    {
        var store = CreateEligibleUnitOfWork();
        var draft = ParentDraft(40);
        store.Seed(draft);
        store.Seed(new StoryGenerationRequest { Id = 7, StoryId = 40, Story = draft, SubmittedByUserId = 1, IdempotencyKey = "other-key-01", Status = GenerationInputStatus.CheckingInput });

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            ParentService(store).SubmitAsync(1, With(ValidRequest(), existingStoryId: 40)));

        Assert.Equal(ActiveDraftMessage, error.Message);
    }

    [Fact]
    public async Task SubmitAsync_ExistingDraftAlreadyHasJob_ThrowsOutputModeLocked()
    {
        var store = CreateEligibleUnitOfWork();
        store.Seed(ParentDraft(40));
        store.Seed(new StoryGenerationJob { Id = 3, StoryId = 40, Operation = GenerationJobOperation.GenerateOutline, Status = GenerationJobStatus.Failed });

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            ParentService(store).SubmitAsync(1, With(ValidRequest(), existingStoryId: 40)));

        Assert.Equal("OUTPUT_MODE_LOCKED: Story draft đã có generation job.", error.Message);
    }

    [Fact]
    public async Task SubmitAsync_KeyBelongsToRequestWithoutStory_ThrowsConflict()
    {
        var store = CreateEligibleUnitOfWork();
        store.Seed(new StoryGenerationRequest { Id = 7, StoryId = 0, SubmittedByUserId = 1, IdempotencyKey = "operation-0001", InputFingerprint = "x" });

        var error = await Assert.ThrowsAsync<ConflictException>(() => ParentService(store).SubmitAsync(1, ValidRequest()));

        Assert.Equal("Idempotency key thuộc luồng tạo truyện khác.", error.Message);
    }

    // ---- Creator audience ----

    [Theory]
    [InlineData("5-7", 2, "vi")]
    [InlineData("6-8", 0, "vi")]
    [InlineData("6-8", 6, "vi")]
    [InlineData("6-8", 2, "fr")]
    public async Task GetCreatorContextAsync_InvalidAudienceOrLanguage_ThrowsBadRequest(string ageBand, int readingLevel, string language)
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreatorService(CreatorStore()).GetCreatorContextAsync(13, ageBand, readingLevel, language));

        Assert.Equal("CREATOR_AUDIENCE_INVALID", error.Message);
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(10001, 90)]
    [InlineData(1800, -1)]
    [InlineData(1800, 101)]
    public async Task GetCreatorContextAsync_InvalidPlatformPolicy_ThrowsConflict(int maximumLength, int threshold)
    {
        var options = new CreatorAuthoringOptions { GenerationMaximumLength = maximumLength, GenerationSafetyScoreThreshold = threshold };

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            CreatorService(CreatorStore(), options).GetCreatorContextAsync(13, "6-8"));

        Assert.Equal("CREATOR_GENERATION_POLICY_INVALID", error.Message);
    }

    [Fact]
    public async Task GetCreatorContextAsync_LockedCreator_ThrowsForbidden()
    {
        var store = CreatorStore();
        store.Items<UserAccount>().Single().LockedUntil = DateTime.UtcNow.AddMinutes(10);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreatorService(store).GetCreatorContextAsync(13, "6-8"));
    }

    [Fact]
    public async Task GetCreatorContextAsync_BlankLanguage_DefaultsToVietnameseAndReadingLevelVocabulary()
    {
        var context = await CreatorService(CreatorStore()).GetCreatorContextAsync(13, "9-10", 3, " ");

        Assert.Equal("vi", context.DefaultLanguage);
        Assert.Equal("9-10", context.AgeBand);
        Assert.Equal(3, context.ReadingLevel);
    }

    [Fact]
    public async Task SubmitCreatorAsync_ExistingCreatorDraft_ReappliesAudienceToStory()
    {
        var store = CreatorStore();
        store.Seed(CreatorDraft(50));

        var result = await CreatorService(store).SubmitCreatorAsync(13, CreatorRequest(ageBand: "9-10", existingStoryId: 50));

        Assert.Equal(50, result.StoryId);
        var story = Assert.Single(store.Items<Story>());
        Assert.Equal(("9-10", 2, "vi", "level_2"), (story.AgeBand, story.ReadingLevel, story.Language, story.VocabularyLevel));
    }

    [Fact]
    public async Task SubmitCreatorAsync_ExistingCreatorStoryNotDraft_ThrowsConflict()
    {
        var store = CreatorStore();
        store.Seed(CreatorDraft(50, StoryStatus.ContentReview));

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            CreatorService(store).SubmitCreatorAsync(13, CreatorRequest(existingStoryId: 50)));

        Assert.Equal("AI_STORY_DRAFT_REQUIRED", error.Message);
    }

    [Fact]
    public async Task SubmitCreatorAsync_ExistingStoryMissing_ThrowsNotFound()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() =>
            CreatorService(CreatorStore()).SubmitCreatorAsync(13, CreatorRequest(existingStoryId: 50)));

        Assert.Equal("Không tìm thấy Story với mã định danh [50].", error.Message);
    }

    // ---- Progress ----

    [Fact]
    public async Task GetProgressAsync_ChildOwner_ReturnsAcceptedProgress()
    {
        var store = CreateEligibleUnitOfWork();
        var service = ParentService(store);
        var submitted = await service.SubmitAsync(1, ValidRequest());

        var progress = await service.GetProgressAsync(1, submitted.StoryId, submitted.RequestId);

        Assert.Equal("input_accepted", progress.InputStatus);
        Assert.Equal("pending_dispatch", progress.HandoffStatus);
    }

    [Fact]
    public async Task GetProgressAsync_UnknownRequest_ThrowsNotFound()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() => ParentService(CreateEligibleUnitOfWork()).GetProgressAsync(1, 1, 77));

        Assert.Equal("Không tìm thấy Yêu cầu sinh truyện với mã định danh [77].", error.Message);
    }

    [Fact]
    public async Task GetProgressAsync_PendingInput_ReportsPendingInput()
    {
        var store = SeedRequest(GenerationInputStatus.PendingInput, startedMinutesAgo: null);

        Assert.Equal("pending_input", (await ParentService(store).GetProgressAsync(1, 40, 7)).InputStatus);
    }

    [Fact]
    public async Task GetProgressAsync_RecentCheckingInput_StaysCheckingInput()
    {
        var store = SeedRequest(GenerationInputStatus.CheckingInput, startedMinutesAgo: 1);

        Assert.Equal("checking_input", (await ParentService(store).GetProgressAsync(1, 40, 7)).InputStatus);
    }

    [Fact]
    public async Task GetProgressAsync_CheckingInputOlderThanLease_FinalizesAsRetryableFailure()
    {
        var store = SeedRequest(GenerationInputStatus.CheckingInput, startedMinutesAgo: 3);

        var progress = await ParentService(store).GetProgressAsync(1, 40, 7);

        Assert.Equal("input_check_failed", progress.InputStatus);
        Assert.Equal("INPUT_CHECK_LEASE_EXPIRED", progress.ReasonCode);
        Assert.True(progress.CanRetry);
    }

    // ---- Retry ----

    [Fact]
    public async Task RetryAsync_SameRetryKeyTwice_ReturnsCurrentProgressWithoutNewAttempt()
    {
        var (store, failed) = await FailedSubmissionAsync();
        var service = ParentService(store);
        await service.RetryAsync(1, failed.StoryId, failed.RequestId, ValidRetry());

        var replay = await service.RetryAsync(1, failed.StoryId, failed.RequestId, ValidRetry());

        Assert.Equal(2, replay.AttemptCount);
        Assert.Equal("input_accepted", replay.InputStatus);
    }

    [Fact]
    public async Task RetryAsync_RequestNotInFailedState_ThrowsConflict()
    {
        var store = CreateEligibleUnitOfWork();
        var accepted = await ParentService(store).SubmitAsync(1, ValidRequest());

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            ParentService(store).RetryAsync(1, accepted.StoryId, accepted.RequestId, ValidRetry()));

        Assert.Equal("Yêu cầu hiện tại không thể retry input guardrail.", error.Message);
    }

    [Fact]
    public async Task RetryAsync_AttemptsExhausted_ThrowsConflict()
    {
        var (store, failed) = await FailedSubmissionAsync();
        store.Items<StoryGenerationRequest>().Single().AttemptCount = 3;

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            ParentService(store).RetryAsync(1, failed.StoryId, failed.RequestId, ValidRetry()));

        Assert.Equal("Yêu cầu đã sử dụng hết số lần kiểm tra input.", error.Message);
    }

    [Fact]
    public async Task RetryAsync_ChangedTopic_ThrowsInputChanged()
    {
        var (store, failed) = await FailedSubmissionAsync();
        var retry = new RetryAIStoryInputRequestDto
        {
            RetryKey = "retry-0001", Topic = "Chủ đề khác", Lesson = "Biết giúp đỡ bạn bè", VocabularyLevel = "level_2",
            CharacterMode = "ai_suggested", SettingMode = "ai_suggested", TargetLength = 500
        };

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            ParentService(store).RetryAsync(1, failed.StoryId, failed.RequestId, retry));

        Assert.Equal("INPUT_CHANGED: hãy tạo request mới trên cùng Story draft.", error.Message);
    }

    [Fact]
    public async Task RetryAsync_SafetyPolicyChangedSinceSubmit_ThrowsContextChanged()
    {
        var (store, failed) = await FailedSubmissionAsync();
        store.Items<SafetyPolicy>().Single().MaxStoryLength = 650;

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            ParentService(store).RetryAsync(1, failed.StoryId, failed.RequestId, ValidRetry()));

        Assert.Equal("INPUT_CONTEXT_CHANGED: hãy tạo request mới trên cùng Story draft.", error.Message);
    }

    [Fact]
    public async Task RetryAsync_CreatorStory_ReauthorizesCreatorAndAccepts()
    {
        var store = CreatorStore();
        var failed = await CreatorService(store, guardrail: new ScriptedGuardrail(_ => throw new InvalidOperationException("down")))
            .SubmitCreatorAsync(13, CreatorRequest());
        Assert.Equal("input_check_failed", failed.InputStatus);
        var retry = new RetryAIStoryInputRequestDto
        {
            RetryKey = "retry-creator-1", Topic = "Tình bạn", Lesson = "Biết giúp đỡ bạn bè", VocabularyLevel = "level_2",
            Language = "vi", CharacterMode = "ai_suggested", SettingMode = "ai_suggested", TargetLength = 500
        };

        var accepted = await CreatorService(store).RetryAsync(13, failed.StoryId, failed.RequestId, retry);

        Assert.Equal("input_accepted", accepted.InputStatus);
    }

    // ---- Guardrail run outcomes ----

    [Fact]
    public async Task SubmitAsync_GuardrailTimesOut_FinalizesRetryableTimeout()
    {
        var store = CreateEligibleUnitOfWork();
        var guardrail = new ScriptedGuardrail(_ => throw new OperationCanceledException());

        var result = await ParentService(store, guardrail).SubmitAsync(1, ValidRequest());

        Assert.Equal("input_check_failed", result.InputStatus);
        Assert.Equal("INPUT_CHECK_TIMEOUT", result.ReasonCode);
        Assert.True(result.CanRetry);
    }

    [Fact]
    public async Task SubmitAsync_CallerCancelsDuringGuardrail_FinalizesCancelledAndRethrows()
    {
        var store = CreateEligibleUnitOfWork();
        using var cancellation = new CancellationTokenSource();
        var guardrail = new ScriptedGuardrail(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ParentService(store, guardrail).SubmitAsync(1, ValidRequest(), cancellation.Token));

        var request = Assert.Single(store.Items<StoryGenerationRequest>());
        Assert.Equal(GenerationInputStatus.InputCheckFailed, request.Status);
        Assert.Equal("INPUT_CHECK_CANCELLED", request.ReasonCode);
    }

    [Fact]
    public async Task SubmitAsync_AttemptTokenRotatedDuringGuardrail_ThrowsConflict()
    {
        var store = CreateEligibleUnitOfWork();
        var guardrail = new ScriptedGuardrail(_ =>
        {
            store.Items<StoryGenerationRequest>().Single().ConcurrencyToken = "someone-else";
            return Allow();
        });

        var error = await Assert.ThrowsAsync<ConflictException>(() => ParentService(store, guardrail).SubmitAsync(1, ValidRequest()));

        Assert.Equal("Kết quả của attempt cũ đã bị loại bỏ.", error.Message);
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task SubmitAsync_ChildDeactivatedDuringGuardrail_FinalizesNonRetryableFailure()
    {
        var store = CreateEligibleUnitOfWork();
        var guardrail = new ScriptedGuardrail(_ =>
        {
            store.Items<ChildProfile>().Single().Status = ChildProfileStatus.Archived;
            return Allow();
        });

        var result = await ParentService(store, guardrail).SubmitAsync(1, ValidRequest());

        Assert.Equal("input_check_failed", result.InputStatus);
        Assert.Equal("AUTHORIZATION_OR_CHILD_CHANGED", result.ReasonCode);
        Assert.False(result.CanRetry);
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task SubmitAsync_SafetyPolicyChangedDuringGuardrail_FinalizesContextChanged()
    {
        var store = CreateEligibleUnitOfWork();
        var guardrail = new ScriptedGuardrail(_ =>
        {
            store.Items<SafetyPolicy>().Single().MaxStoryLength = 650;
            return Allow();
        });

        var result = await ParentService(store, guardrail).SubmitAsync(1, ValidRequest());

        Assert.Equal("INPUT_CONTEXT_CHANGED", result.ReasonCode);
        Assert.False(result.CanRetry);
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task SubmitAsync_GuardrailFallbackLongerThan500_IsTruncated()
    {
        var store = CreateEligibleUnitOfWork();
        var guardrail = new ScriptedGuardrail(_ => new InputGuardrailResult(
            InputGuardrailDecision.Block, "INPUT_BLOCKED", new string('x', 600), false, "test"));

        var result = await ParentService(store, guardrail).SubmitAsync(1, ValidRequest());

        Assert.Equal("input_blocked", result.InputStatus);
        Assert.Equal(500, result.FallbackMessage!.Length);
    }

    // ---- helpers ----

    private static AIStoryInputService ParentService(FakeUnitOfWork store, IInputGuardrail? guardrail = null) =>
        new(store, guardrail ?? new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));

    private static AIStoryInputService CreatorService(FakeUnitOfWork store, CreatorAuthoringOptions? options = null,
        IInputGuardrail? guardrail = null) =>
        new(store, guardrail ?? new RuleBasedInputGuardrail(), new Moq.Mock<ITokenQuotaService>(Moq.MockBehavior.Strict).Object, options);

    private static FakeUnitOfWork CreatorStore()
    {
        var store = new FakeUnitOfWork();
        store.Seed(new UserAccount { Id = 13, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        return store;
    }

    private static Story ParentDraft(int id, StoryStatus status = StoryStatus.Draft) => new()
    {
        Id = id, AuthorUserId = 1, ChildProfileId = 1, Scope = StoryScope.Parent, Source = StorySource.Ai, Status = status
    };

    private static Story CreatorDraft(int id, StoryStatus status = StoryStatus.Draft) => new()
    {
        Id = id, AuthorUserId = 13, Scope = StoryScope.Creator, Source = StorySource.Ai, Status = status, AgeBand = "6-8"
    };

    private static FakeUnitOfWork SeedRequest(GenerationInputStatus status, int? startedMinutesAgo)
    {
        var store = CreateEligibleUnitOfWork();
        var draft = ParentDraft(40);
        store.Seed(draft);
        store.Seed(new StoryGenerationRequest
        {
            Id = 7, StoryId = 40, Story = draft, SubmittedByUserId = 1, IdempotencyKey = "operation-0007", Status = status,
            AttemptCount = 1, MaxAttempts = 3, ConcurrencyToken = "token-7",
            AttemptStartedAt = startedMinutesAgo is null ? null : DateTime.UtcNow.AddMinutes(-startedMinutesAgo.Value)
        });
        return store;
    }

    private static async Task<(FakeUnitOfWork Store, AIStoryInputProgressDto Failed)> FailedSubmissionAsync()
    {
        var store = CreateEligibleUnitOfWork();
        var failed = await ParentService(store, new ThrowingGuardrail()).SubmitAsync(1, ValidRequest());
        Assert.Equal("input_check_failed", failed.InputStatus);
        return (store, failed);
    }

    private static SubmitAIStoryInputRequestDto With(SubmitAIStoryInputRequestDto request,
        string? characterMode = null, IReadOnlyList<string>? characters = null, string? settingMode = null,
        string? setting = null, string? language = null, int? existingStoryId = null, string? idempotencyKey = null) => new()
    {
        ChildProfileId = request.ChildProfileId,
        IdempotencyKey = idempotencyKey ?? request.IdempotencyKey,
        Topic = request.Topic,
        Lesson = request.Lesson,
        VocabularyLevel = request.VocabularyLevel,
        CharacterMode = characterMode ?? request.CharacterMode,
        Characters = characters ?? request.Characters,
        SettingMode = settingMode ?? request.SettingMode,
        Setting = setting ?? request.Setting,
        Language = language ?? request.Language,
        ExistingStoryId = existingStoryId ?? request.ExistingStoryId,
        TargetLength = request.TargetLength
    };

    private static InputGuardrailResult Allow() =>
        new(InputGuardrailDecision.Allow, "INPUT_ALLOWED", string.Empty, false, "test");

    private sealed class ScriptedGuardrail(Func<InputGuardrailRequest, InputGuardrailResult> check) : IInputGuardrail
    {
        public Task<InputGuardrailResult> CheckAsync(InputGuardrailRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(check(request));
    }
}
