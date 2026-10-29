using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Interfaces;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.AIStoryInput.Services;

public sealed class AIStoryInputService : IAIStoryInputService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan GuardrailTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaleCheckingThreshold = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyList<string> VocabularyLevels =
        ["level_1", "level_2", "level_3", "level_4", "level_5"];

    private readonly IUnitOfWork _unitOfWork;
    private readonly IInputGuardrail _inputGuardrail;
    private readonly ITokenQuotaService _tokenQuotaService;
    private readonly StoryPlatform.Application.Features.CreatorAuthoring.CreatorAuthoringOptions _creatorOptions;
    private readonly StoryPlatform.Application.Features.ContentGeneration.LearningBatchOptions _learningOptions;

    public AIStoryInputService(IUnitOfWork unitOfWork, IInputGuardrail inputGuardrail, ITokenQuotaService tokenQuotaService,
        StoryPlatform.Application.Features.CreatorAuthoring.CreatorAuthoringOptions? creatorOptions = null,
        StoryPlatform.Application.Features.ContentGeneration.LearningBatchOptions? learningOptions = null)
    {
        _unitOfWork = unitOfWork;
        _inputGuardrail = inputGuardrail;
        _tokenQuotaService = tokenQuotaService;
        _creatorOptions = creatorOptions ?? new();
        _learningOptions = learningOptions ?? new();
    }

    public async Task<AIStoryInputContextDto> GetContextAsync(
        int userId,
        int childProfileId,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveContextAsync(userId, childProfileId, null, null, cancellationToken);
        return ToContextDto(context);
    }

    public async Task<CreatorAIStoryInputContextDto> GetCreatorContextAsync(int userId, string ageBand,
        int readingLevel = 2, string language = "vi", CancellationToken cancellationToken = default)
    {
        var context = await ResolveCreatorContextAsync(userId, new(ageBand, readingLevel), null, language, cancellationToken);
        return new CreatorAIStoryInputContextDto(context.AgeBand, context.ReadingLevel, context.Language, ["vi", "en"], VocabularyLevels,
            context.MaximumLength, "always_manual", context.Snapshot.SafetyScoreThreshold!.Value,
            context.Snapshot.BlockedCategoryCodes, ["reading_media_only", "learning"])
        { RecommendedTargetLength = _learningOptions.Profile(context.AgeBand).TargetWords };
    }

    public Task<AIStoryInputProgressDto> SubmitCreatorAsync(int userId, SubmitCreatorAIStoryInputRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ValidateDataAnnotations(request);
        return SubmitAsync(userId, new SubmitAIStoryInputRequestDto
        {
            CreatorAudience = new(request.AgeBand, request.ReadingLevel),
            ExistingStoryId = request.ExistingStoryId, IdempotencyKey = request.IdempotencyKey,
            OutputMode = request.OutputMode, Topic = request.Topic, Genre = request.Genre,
            CharacterMode = request.CharacterMode, Characters = request.Characters,
            SettingMode = request.SettingMode, Setting = request.Setting, Lesson = request.Lesson,
            VocabularyLevel = request.VocabularyLevel, Language = request.Language, TargetLength = request.TargetLength
        }, cancellationToken);
    }

    public async Task<AIStoryInputProgressDto> SubmitAsync(
        int userId,
        SubmitAIStoryInputRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.CreatorAudience is null) ValidateDataAnnotations(request);
        var outputMode = StoryOutputModeContract.Parse(request.OutputMode);
        var normalized = Normalize(request);
        ValidateCreativeInput(normalized);
        var creator = request.CreatorAudience is not null;
        if (!creator && request.ChildProfileId <= 0) throw new BadRequestException("CHILD_REQUIRED");
        var context = creator
            ? await ResolveCreatorContextAsync(userId, request.CreatorAudience!, normalized.VocabularyLevel, normalized.Language, cancellationToken)
            : await ResolveContextAsync(
            userId,
            request.ChildProfileId,
            normalized.VocabularyLevel,
            normalized.Language,
            cancellationToken);
        var profile = _learningOptions.Profile(context.AgeBand, normalized.TargetLength);
        normalized = normalized with { VocabularyLevel = context.VocabularyLevel, Language = context.Language, TargetLength = profile.TargetWords };
        var pinnedContext = context.Snapshot with { LearningProfile = profile };
        context = context with { Snapshot = pinnedContext, Fingerprint = Fingerprint(pinnedContext) };
        ValidateAgainstContext(normalized, context);

        var inputFingerprint = creator
            ? Fingerprint(new { input = normalized, outputMode, context = context.Fingerprint })
            : InputFingerprint(normalized, outputMode);
        var requestRepository = _unitOfWork.Repository<StoryGenerationRequest>();
        var existing = await requestRepository.FirstOrDefaultAsync(
            item => item.SubmittedByUserId == userId && item.IdempotencyKey == request.IdempotencyKey.Trim(),
            includeProperties: "Story",
            cancellationToken: cancellationToken);

        if (existing is not null)
        {
            if (existing.Story is null || existing.Story.Scope != (creator ? StoryScope.Creator : StoryScope.Parent))
                throw new ConflictException("Idempotency key thuộc luồng tạo truyện khác.");
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(existing.InputFingerprint),
                    Encoding.ASCII.GetBytes(inputFingerprint)))
            {
                throw new ConflictException("Idempotency key đã được sử dụng cho một nội dung khác.");
            }

            return await RecoverIfStaleAsync(existing, cancellationToken);
        }

        if (!creator) await _tokenQuotaService.EnsureWithinQuotaAsync(request.ChildProfileId, cancellationToken);

        Story story;
        if (request.ExistingStoryId.HasValue)
        {
            story = creator
                ? await LoadCreatorDraftAsync(userId, request.ExistingStoryId.Value, cancellationToken)
                : await LoadReusableDraftAsync(userId, request.ChildProfileId, request.ExistingStoryId.Value, cancellationToken);
            var hasActiveRequest = await requestRepository.ExistsAsync(
                item => item.StoryId == story.Id &&
                        (item.Status == GenerationInputStatus.PendingInput ||
                         item.Status == GenerationInputStatus.CheckingInput ||
                         item.Status == GenerationInputStatus.InputAccepted),
                cancellationToken);
            if (hasActiveRequest)
            {
                throw new ConflictException("Story draft đang có một yêu cầu input hoạt động hoặc đang chờ Generate Outline.");
            }
            if (await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(
                    item => item.StoryId == story.Id, cancellationToken))
                throw new ConflictException("OUTPUT_MODE_LOCKED: Story draft đã có generation job.");
            story.OutputMode = outputMode;
            if (creator)
            {
                story.AgeBand = context.AgeBand; story.ReadingLevel = context.ReadingLevel;
                story.Language = context.Language; story.VocabularyLevel = context.VocabularyLevel;
            }
        }
        else
        {
            story = new Story
            {
                Title = null,
                AuthorUserId = userId,
                ChildProfileId = creator ? null : request.ChildProfileId,
                Scope = creator ? StoryScope.Creator : StoryScope.Parent,
                Source = StorySource.Ai,
                OutputMode = outputMode,
                Status = StoryStatus.Draft,
                IsPublished = false,
                AgeBand = context.AgeBand,
                ReadingLevel = context.ReadingLevel,
                VocabularyLevel = normalized.VocabularyLevel,
                Language = normalized.Language
            };
        }

        var generationRequest = new StoryGenerationRequest
        {
            Story = story,
            StoryId = request.ExistingStoryId.HasValue ? story.Id : 0,
            SubmittedByUserId = userId,
            IdempotencyKey = request.IdempotencyKey.Trim(),
            InputFingerprint = inputFingerprint,
            ContextFingerprint = context.Fingerprint,
            ContextSnapshotJson = JsonSerializer.Serialize(context.Snapshot, JsonOptions),
            Status = GenerationInputStatus.CheckingInput,
            AttemptCount = 1,
            MaxAttempts = MaxAttempts,
            AttemptStartedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!request.ExistingStoryId.HasValue)
            {
                await _unitOfWork.Repository<Story>().AddAsync(story, cancellationToken);
            }
            else
            {
                _unitOfWork.Repository<Story>().Update(story);
            }

            generationRequest.StoryId = story.Id;
            await requestRepository.AddAsync(generationRequest, cancellationToken);
            if (!creator) await _tokenQuotaService.IncrementUsageAsync(request.ChildProfileId, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            var winner = await requestRepository.FirstOrDefaultAsync(
                item => item.SubmittedByUserId == userId && item.IdempotencyKey == request.IdempotencyKey.Trim(),
                includeProperties: "Story",
                cancellationToken: cancellationToken);
            if (winner is not null)
            {
                if (!string.Equals(winner.InputFingerprint, inputFingerprint, StringComparison.Ordinal))
                {
                    throw new ConflictException("Idempotency key đã được sử dụng cho một nội dung khác.");
                }

                return ToProgressDto(winner);
            }

            if (request.ExistingStoryId.HasValue && await requestRepository.ExistsAsync(
                    item => item.StoryId == story.Id &&
                            (item.Status == GenerationInputStatus.PendingInput ||
                             item.Status == GenerationInputStatus.CheckingInput ||
                             item.Status == GenerationInputStatus.InputAccepted),
                    cancellationToken))
            {
                throw new ConflictException("Story draft đang có một yêu cầu input hoạt động hoặc đang chờ Generate Outline.");
            }

            throw;
        }

        return await RunGuardrailAsync(generationRequest, story, normalized, context, cancellationToken);
    }

    public async Task<AIStoryInputProgressDto> GetProgressAsync(
        int userId,
        int storyId,
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await LoadRequestAsync(storyId, requestId, cancellationToken);
        if (!await GenerationStoryAccess.AuthorizeCreatorAsync(_unitOfWork, request.Story!, userId, cancellationToken))
            await EnsureGeneratePermissionAsync(userId, ParentStoryContext.RequireChildId(request.Story!), cancellationToken);
        return await RecoverIfStaleAsync(request, cancellationToken);
    }

    public async Task<AIStoryInputProgressDto> RetryAsync(
        int userId,
        int storyId,
        int requestId,
        RetryAIStoryInputRequestDto retry,
        CancellationToken cancellationToken = default)
    {
        ValidateDataAnnotations(retry);
        var request = await LoadRequestAsync(storyId, requestId, cancellationToken);
        var story = request.Story!;
        if (story.Scope == StoryScope.Creator)
            await GenerationStoryAccess.AuthorizeCreatorAsync(_unitOfWork, story, userId, cancellationToken);
        var normalized = Normalize(retry);
        ValidateCreativeInput(normalized);
        var context = await ResolveStoryContextAsync(userId, story, normalized.VocabularyLevel, normalized.Language, cancellationToken);
        var pinnedProfile = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, JsonOptions)?.LearningProfile;
        context = WithLearningProfile(context, pinnedProfile);
        if (normalized.TargetLength == 0 && pinnedProfile is not null) normalized = normalized with { TargetLength = pinnedProfile.TargetWords };
        normalized = normalized with { VocabularyLevel = context.VocabularyLevel, Language = context.Language };
        ValidateAgainstContext(normalized, context);

        if (request.LastRetryKey == retry.RetryKey.Trim())
        {
            return await RecoverIfStaleAsync(request, cancellationToken);
        }

        if (request.Status != GenerationInputStatus.InputCheckFailed || !request.CanRetry)
        {
            throw new ConflictException("Yêu cầu hiện tại không thể retry input guardrail.");
        }

        if (request.AttemptCount >= request.MaxAttempts)
        {
            throw new ConflictException("Yêu cầu đã sử dụng hết số lần kiểm tra input.");
        }

        var fingerprint = story.Scope == StoryScope.Creator
            ? Fingerprint(new { input = normalized, outputMode = story.OutputMode, context = context.Fingerprint })
            : InputFingerprint(normalized, story.OutputMode);
        if (!string.Equals(fingerprint, request.InputFingerprint, StringComparison.Ordinal))
        {
            throw new ConflictException("INPUT_CHANGED: hãy tạo request mới trên cùng Story draft.");
        }

        if (!string.Equals(context.Fingerprint, request.ContextFingerprint, StringComparison.Ordinal))
        {
            throw new ConflictException("INPUT_CONTEXT_CHANGED: hãy tạo request mới trên cùng Story draft.");
        }

        request.Status = GenerationInputStatus.CheckingInput;
        request.AttemptCount++;
        request.CanRetry = false;
        request.ReasonCode = null;
        request.FallbackMessage = null;
        request.LastRetryKey = retry.RetryKey.Trim();
        request.AttemptStartedAt = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            _unitOfWork.Repository<StoryGenerationRequest>().Update(request);
            RotateConcurrencyToken(request);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            var current = await _unitOfWork.Repository<StoryGenerationRequest>().FirstOrDefaultAsync(
                item => item.Id == requestId && item.StoryId == storyId,
                includeProperties: "Story",
                cancellationToken: cancellationToken);
            if (current?.LastRetryKey == retry.RetryKey.Trim())
            {
                return ToProgressDto(current);
            }

            throw;
        }

        return await RunGuardrailAsync(request, story, normalized, context, cancellationToken);
    }

    private async Task<AIStoryInputProgressDto> RunGuardrailAsync(
        StoryGenerationRequest generationRequest,
        Story story,
        AcceptedAIStoryInputSnapshot input,
        EffectiveContext originalContext,
        CancellationToken cancellationToken)
    {
        var attemptToken = generationRequest.ConcurrencyToken;
        InputGuardrailResult result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GuardrailTimeout);
            result = await _inputGuardrail.CheckAsync(new InputGuardrailRequest(
                input.Topic,
                input.Characters,
                input.Setting,
                input.Lesson,
                originalContext.BlockedTerms,
                originalContext.RestrictedTerms), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = TechnicalFailure("INPUT_CHECK_TIMEOUT", "Không thể hoàn tất kiểm tra nội dung lúc này. Vui lòng thử lại.");
        }
        catch (OperationCanceledException)
        {
            await FinalizeFailureAsync(generationRequest, attemptToken, "INPUT_CHECK_CANCELLED", true, CancellationToken.None);
            throw;
        }
        catch
        {
            result = TechnicalFailure("INPUT_CHECK_PROVIDER_ERROR", "Dịch vụ kiểm tra nội dung tạm thời chưa sẵn sàng. Vui lòng thử lại.");
        }

        if (!await IsCurrentAttemptAsync(generationRequest.Id, attemptToken, cancellationToken))
        {
            throw new ConflictException("Kết quả của attempt cũ đã bị loại bỏ.");
        }

        if (result.Decision == InputGuardrailDecision.Allow)
        {
            EffectiveContext currentContext;
            try
            {
                currentContext = await ResolveStoryContextAsync(generationRequest.SubmittedByUserId,
                    story, input.VocabularyLevel, input.Language, cancellationToken);
            }
            catch (AppException)
            {
                await FinalizeFailureAsync(generationRequest, attemptToken, "AUTHORIZATION_OR_CHILD_CHANGED", false, cancellationToken);
                return ToProgressDto(generationRequest);
            }

            var validStory = await _unitOfWork.Repository<Story>().ExistsAsync(
                item => item.Id == story.Id &&
                        item.AuthorUserId == generationRequest.SubmittedByUserId &&
                        item.Scope == story.Scope && item.ChildProfileId == story.ChildProfileId &&
                        item.Source == StorySource.Ai &&
                        item.Status == StoryStatus.Draft,
                cancellationToken);

            currentContext = WithLearningProfile(currentContext, originalContext.Snapshot.LearningProfile);
            if (!validStory || !string.Equals(currentContext.Fingerprint, originalContext.Fingerprint, StringComparison.Ordinal))
            {
                await FinalizeFailureAsync(generationRequest, attemptToken, "INPUT_CONTEXT_CHANGED", false, cancellationToken);
                return ToProgressDto(generationRequest);
            }

            await FinalizeAcceptedAsync(generationRequest, story, input, result, cancellationToken);
            return ToProgressDto(generationRequest);
        }

        generationRequest.Status = result.Decision == InputGuardrailDecision.Block
            ? GenerationInputStatus.InputBlocked
            : GenerationInputStatus.InputCheckFailed;
        generationRequest.GuardrailDecision = result.Decision.ToString();
        generationRequest.ReasonCode = result.ReasonCode;
        generationRequest.FallbackMessage = SanitizeFallback(result.FallbackMessage);
        generationRequest.CanRetry = result.Decision == InputGuardrailDecision.Error &&
                                     result.CanRetry &&
                                     generationRequest.AttemptCount < generationRequest.MaxAttempts;
        generationRequest.GuardrailCheckVersion = result.CheckVersion;
        generationRequest.GuardrailCheckedAt = DateTime.UtcNow;
        generationRequest.AttemptStartedAt = null;
        await SaveRequestUpdateAsync(generationRequest, cancellationToken);
        return ToProgressDto(generationRequest);
    }

    private async Task FinalizeAcceptedAsync(
        StoryGenerationRequest request,
        Story story,
        AcceptedAIStoryInputSnapshot input,
        InputGuardrailResult result,
        CancellationToken cancellationToken)
    {
        var handoff = new StoryGenerationJob
        {
            StoryId = story.Id,
            GenerationRequestId = request.Id,
            RequestedByUserId = request.SubmittedByUserId,
            OperationKey = request.IdempotencyKey,
            Operation = GenerationJobOperation.GenerateOutline,
            Stage = JobStage.OutlinePending,
            Status = GenerationJobStatus.Pending,
            AttemptNo = 0,
            MaxAttempts = 3,
            GuardrailResult = GuardrailResult.Passed,
            StartedAt = DateTime.UtcNow
        };

        request.Status = GenerationInputStatus.InputAccepted;
        request.AcceptedInputJson = JsonSerializer.Serialize(input, JsonOptions);
        request.GuardrailDecision = result.Decision.ToString();
        request.ReasonCode = result.ReasonCode;
        request.FallbackMessage = null;
        request.CanRetry = false;
        request.GuardrailCheckVersion = result.CheckVersion;
        request.GuardrailCheckedAt = DateTime.UtcNow;
        request.AttemptStartedAt = null;
        request.HandoffJob = handoff;
        request.HandoffCreatedAt = DateTime.UtcNow;
        story.Genre = input.Genre;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(handoff, cancellationToken);
            _unitOfWork.Repository<Story>().Update(story);
            _unitOfWork.Repository<StoryGenerationRequest>().Update(request);
            RotateConcurrencyToken(request);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task FinalizeFailureAsync(
        StoryGenerationRequest request,
        string attemptToken,
        string reasonCode,
        bool canRetry,
        CancellationToken cancellationToken)
    {
        if (!await IsCurrentAttemptAsync(request.Id, attemptToken, cancellationToken))
        {
            return;
        }

        request.Status = GenerationInputStatus.InputCheckFailed;
        request.GuardrailDecision = InputGuardrailDecision.Error.ToString();
        request.ReasonCode = reasonCode;
        request.FallbackMessage = "Không thể hoàn tất kiểm tra nội dung. Vui lòng thử lại.";
        request.CanRetry = canRetry && request.AttemptCount < request.MaxAttempts;
        request.GuardrailCheckVersion = RuleBasedInputGuardrail.Version;
        request.GuardrailCheckedAt = DateTime.UtcNow;
        request.AttemptStartedAt = null;
        await SaveRequestUpdateAsync(request, cancellationToken);
    }

    private async Task<AIStoryInputProgressDto> RecoverIfStaleAsync(
        StoryGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Status == GenerationInputStatus.CheckingInput &&
            request.AttemptStartedAt.HasValue &&
            request.AttemptStartedAt.Value < DateTime.UtcNow.Subtract(StaleCheckingThreshold))
        {
            await FinalizeFailureAsync(request, request.ConcurrencyToken, "INPUT_CHECK_LEASE_EXPIRED", true, cancellationToken);
        }

        return ToProgressDto(request);
    }

    private async Task SaveRequestUpdateAsync(StoryGenerationRequest request, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            _unitOfWork.Repository<StoryGenerationRequest>().Update(request);
            RotateConcurrencyToken(request);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private Task<bool> IsCurrentAttemptAsync(int requestId, string token, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<StoryGenerationRequest>().ExistsAsync(
            item => item.Id == requestId &&
                    item.Status == GenerationInputStatus.CheckingInput &&
                    item.ConcurrencyToken == token,
            cancellationToken);

    private static void RotateConcurrencyToken(StoryGenerationRequest request) =>
        request.ConcurrencyToken = Guid.NewGuid().ToString("N");

    private async Task<StoryGenerationRequest> LoadRequestAsync(int storyId, int requestId, CancellationToken cancellationToken)
    {
        var request = await _unitOfWork.Repository<StoryGenerationRequest>().FirstOrDefaultAsync(
            item => item.Id == requestId && item.StoryId == storyId,
            includeProperties: "Story",
            cancellationToken: cancellationToken);
        return request ?? throw new NotFoundException("Yêu cầu sinh truyện", requestId);
    }

    private async Task<Story> LoadReusableDraftAsync(
        int userId,
        int childProfileId,
        int storyId,
        CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().FirstOrDefaultAsync(
            item => item.Id == storyId &&
                    item.AuthorUserId == userId &&
                    item.Scope == StoryScope.Parent &&
                    item.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);
        if (story is null)
        {
            throw new NotFoundException("Story draft", storyId);
        }

        if (story.Source != StorySource.Ai || story.Status != StoryStatus.Draft)
        {
            throw new ConflictException("Chỉ có thể tiếp tục một AI Story đang ở trạng thái draft.");
        }

        return story;
    }

    private async Task<Story> LoadCreatorDraftAsync(int userId, int storyId, CancellationToken ct)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, ct)
            ?? throw new NotFoundException("Story", storyId);
        if (!await GenerationStoryAccess.AuthorizeCreatorAsync(_unitOfWork, story, userId, ct))
            throw new NotFoundException("Story", storyId);
        if (story.Status != StoryStatus.Draft) throw new ConflictException("AI_STORY_DRAFT_REQUIRED");
        return story;
    }

    private Task<EffectiveContext> ResolveStoryContextAsync(int userId, Story story, string vocabulary,
        string language, CancellationToken ct) => story.Scope == StoryScope.Creator
        ? ResolveCreatorContextAsync(userId, new(story.AgeBand, story.ReadingLevel ?? 2), vocabulary, language, ct)
        : ResolveContextAsync(userId, ParentStoryContext.RequireChildId(story), vocabulary, language, ct);

    private static EffectiveContext WithLearningProfile(EffectiveContext context, StoryPlatform.Contracts.AI.Models.LearningGenerationProfile? profile)
    {
        var snapshot = context.Snapshot with { LearningProfile = profile };
        return context with { Snapshot = snapshot, Fingerprint = Fingerprint(snapshot) };
    }

    private async Task<EffectiveContext> ResolveCreatorContextAsync(int userId, CreatorGenerationAudience audience,
        string? vocabulary, string? language, CancellationToken ct)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, ct);
        if (user is null || user.IsDeleted || user.Role != UserRole.ContentCreator || user.Status != AccountStatus.Active ||
            user.LockedUntil > DateTime.UtcNow)
            throw new ForbiddenException();
        if (audience.AgeBand is not ("6-8" or "9-10") || audience.ReadingLevel is < 1 or > 5)
            throw new BadRequestException("CREATOR_AUDIENCE_INVALID");
        if (_creatorOptions.GenerationMaximumLength is <= 0 or > 10000 ||
            _creatorOptions.GenerationSafetyScoreThreshold is < 0m or > 100m)
            throw new ConflictException("CREATOR_GENERATION_POLICY_INVALID");
        var selectedLanguage = string.IsNullOrWhiteSpace(language) ? "vi" : NormalizeToken(language);
        var selectedVocabulary = string.IsNullOrWhiteSpace(vocabulary) ? $"level_{audience.ReadingLevel}" : NormalizeToken(vocabulary);
        if (selectedLanguage is not ("vi" or "en") || !VocabularyLevels.Contains(selectedVocabulary))
            throw new BadRequestException("CREATOR_AUDIENCE_INVALID");
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(x => x.IsActive && !x.IsDeleted, cancellationToken: ct);
        var band = audience.AgeBand == "6-8" ? "Age_6_8" : "Age_9_10";
        var blocked = categories.Where(x => x.DefaultBlockedAgeBands.Contains(band)).OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
        var terms = blocked.SelectMany(x => new[] { x.Code, x.DisplayName }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Order().ToArray();
        // Platform audience policy, not a fabricated child consent or child quota.
        var snapshot = new AIStoryInputContextSnapshot(null, audience.AgeBand, audience.ReadingLevel,
            selectedVocabulary, selectedLanguage, _creatorOptions.GenerationMaximumLength, "always_manual", [], [], [],
            blocked.Select(x => x.Code).ToArray(), [], [], terms, SafetyScoreThreshold: _creatorOptions.GenerationSafetyScoreThreshold,
            AudienceScope: "Creator", CreatorUserId: userId);
        return new("", snapshot.AgeBand, snapshot.ReadingLevel, selectedVocabulary, selectedLanguage,
            snapshot.MaximumLength, ApprovalMode.AlwaysManual, snapshot, Fingerprint(snapshot), terms, []);
    }

    private async Task<EffectiveContext> ResolveContextAsync(
        int userId,
        int childProfileId,
        string? selectedVocabularyLevel,
        string? selectedLanguage,
        CancellationToken cancellationToken)
    {
        await EnsureGeneratePermissionAsync(userId, childProfileId, cancellationToken);
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        if (child.Status != ChildProfileStatus.Active)
        {
            throw new ConflictException("Child Profile phải đang Active để tạo truyện.");
        }

        var learning = await _unitOfWork.Repository<LearningProfile>().FirstOrDefaultAsync(
            item => item.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken)
            ?? throw new BadRequestException("Child Profile chưa có Learning Profile hợp lệ.");
        if (learning.ReadingLevel is < 1 or > 5)
        {
            throw new BadRequestException("Reading Level của Child Profile phải nằm trong thang 1-5.");
        }

        var safety = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            item => item.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken)
            ?? throw new BadRequestException("Child Profile chưa có Safety Policy.");
        if (!safety.ConsentRecorded || !safety.ConsentRecordedAt.HasValue || safety.ConsentPolicyVersion <= 0)
        {
            throw new BadRequestException("CONSENT_REQUIRED: Child Profile chưa có consent hợp lệ để sử dụng AI.");
        }
        if (ConsentPolicy.IsOutdated(safety.ConsentPolicyVersion))
        {
            throw new BadRequestException(
                "CONSENT_OUTDATED: Điều khoản đồng thuận đã thay đổi — Owner cần đồng ý lại trước khi tạo truyện mới.");
        }
        if (safety.SafetyScoreThreshold is < 0m or > 100m)
        {
            throw new BadRequestException("Safety Score Threshold phải nằm trong khoảng 0 đến 100.");
        }
        if (safety.ComprehensionThresholdPercent is < 0m or > 100m || safety.ComprehensionWindowSize <= 0)
        {
            throw new BadRequestException("Cấu hình comprehension của Safety Policy không hợp lệ.");
        }

        var personalCategories = await _unitOfWork.Repository<SafetyPolicyCategory>().FindAsync(
            item => item.SafetyPolicyId == safety.Id,
            includeProperties: "ContentCategory",
            cancellationToken: cancellationToken);

        var maximumLength = safety.MaxStoryLength;
        if (maximumLength <= 0)
        {
            throw new BadRequestException("Safety Policy chưa có Maximum Story Length hợp lệ.");
        }

        var approvalMode = safety.ParentalGateEnabled ||
                           safety.RequiredApprovalMode == ApprovalMode.AlwaysManual
            ? ApprovalMode.AlwaysManual
            : ApprovalMode.AutoPublishOnThreshold;
        var profileLanguage = child.Language.Trim().ToLowerInvariant();
        var language = string.IsNullOrWhiteSpace(selectedLanguage) ? profileLanguage : selectedLanguage.Trim().ToLowerInvariant();
        if (!string.Equals(language, profileLanguage, StringComparison.Ordinal))
        {
            throw new BadRequestException("Ngôn ngữ đã chọn chưa được cấu hình cho Child Profile.");
        }

        var vocabularyLevel = string.IsNullOrWhiteSpace(selectedVocabularyLevel)
            ? $"level_{learning.ReadingLevel}"
            : selectedVocabularyLevel.Trim().ToLowerInvariant();
        if (!VocabularyLevels.Contains(vocabularyLevel, StringComparer.Ordinal))
        {
            throw new BadRequestException("Vocabulary Level không hợp lệ.");
        }

        var categoryRules = MergeCategoryRules(personalCategories);
        var categoryNames = personalCategories
            .Where(item => item.ContentCategory is { IsActive: true })
            .Select(item => (item.ContentCategory!.Code, item.ContentCategory.DisplayName))
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        var allowedTerms = Terms(categoryRules, categoryNames, PolicyRule.Allowed);
        var restrictedTerms = Terms(categoryRules, categoryNames, PolicyRule.Restricted);
        var blockedTerms = Terms(categoryRules, categoryNames, PolicyRule.Blocked);
        var topics = await _unitOfWork.Repository<LearningProfileTopic>().FindAsync(
            item => item.LearningProfileId == learning.Id && item.Relation == TopicRelation.FavoriteTopic,
            cancellationToken: cancellationToken);
        var interests = topics.Select(item => item.Topic.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var ageBand = child.AgeBand == AgeBand.Age_6_8 ? "6-8" : "9-10";
        var snapshot = new AIStoryInputContextSnapshot(
            child.Id,
            ageBand,
            learning.ReadingLevel,
            vocabularyLevel,
            language,
            maximumLength,
            ToApprovalModeValue(approvalMode),
            interests,
            categoryRules.Where(item => item.Value == PolicyRule.Allowed).Select(item => item.Key).Order().ToArray(),
            categoryRules.Where(item => item.Value == PolicyRule.Restricted).Select(item => item.Key).Order().ToArray(),
            categoryRules.Where(item => item.Value == PolicyRule.Blocked).Select(item => item.Key).Order().ToArray(),
            allowedTerms,
            restrictedTerms,
            blockedTerms,
            safety.ReadabilityScoreThreshold,
            safety.ParentalGateEnabled,
            safety.SafetyScoreThreshold,
            learning.ComprehensionGoal,
            safety.ComprehensionThresholdPercent,
            safety.ComprehensionWindowSize,
            safety.ConsentPolicyVersion,
            safety.ConsentRecordedAt);

        return new EffectiveContext(
            child.Nickname,
            ageBand,
            learning.ReadingLevel,
            vocabularyLevel,
            language,
            maximumLength,
            approvalMode,
            snapshot,
            Fingerprint(snapshot),
            blockedTerms,
            restrictedTerms);
    }

    private async Task EnsureGeneratePermissionAsync(int userId, int childProfileId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken)
                   ?? throw new ForbiddenException();
        if (user.Status == AccountStatus.Suspended || user.Role != UserRole.User)
        {
            throw new ForbiddenException("Tài khoản không đủ điều kiện tạo truyện cho Child Profile.");
        }

        if (!await StoryPlatform.Application.Common.ChildOwnership.IsOwnerAsync(
                _unitOfWork, childProfileId, userId, cancellationToken))
        {
            throw new ForbiddenException("Bạn không phải người quản lý Child Profile này.");
        }
    }

    private static Dictionary<string, PolicyRule> MergeCategoryRules(
        IEnumerable<SafetyPolicyCategory> personal)
    {
        var merged = new Dictionary<string, PolicyRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in personal
                     .Where(item => item.ContentCategory is { IsActive: true })
                     .Select(item => (item.ContentCategory!.Code, item.Rule)))
        {
            if (!merged.TryGetValue(item.Code, out var current) || item.Rule > current)
            {
                merged[item.Code] = item.Rule;
            }
        }

        return merged;
    }

    private static IReadOnlyList<string> Terms(
        IReadOnlyDictionary<string, PolicyRule> rules,
        IReadOnlyDictionary<string, string> displayNames,
        PolicyRule rule) => rules
        .Where(item => item.Value == rule)
        .SelectMany(item => displayNames.TryGetValue(item.Key, out var displayName)
            ? new[] { item.Key, displayName }
            : new[] { item.Key })
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static void ValidateAgainstContext(AcceptedAIStoryInputSnapshot input, EffectiveContext context)
    {
        if (input.TargetLength > context.MaximumLength)
        {
            throw new BadRequestException($"Target Length không được vượt Maximum Length {context.MaximumLength} từ Safety Policy.");
        }
    }

    private static void ValidateCreativeInput(AcceptedAIStoryInputSnapshot input)
    {
        if (input.TargetLength is < 0 or > 10000) throw new BadRequestException("Target Length phải từ 0 đến 10000; 0 dùng độ dài theo nhóm tuổi.");
        if (input.Topic.Length is < 2 or > 200 || input.Lesson.Length is < 2 or > 500)
        {
            throw new BadRequestException("Topic và Lesson là bắt buộc và phải nằm trong giới hạn cho phép.");
        }

        if (input.CharacterMode == "specified" && input.Characters.Count == 0)
        {
            throw new BadRequestException("Character mode 'specified' yêu cầu ít nhất một mô tả nhân vật.");
        }

        if (input.Characters.Count > 10 || input.Characters.Any(item => item.Length > 100))
        {
            throw new BadRequestException("Danh sách nhân vật tối đa 10 mục và mỗi mục tối đa 100 ký tự.");
        }

        if (input.SettingMode == "specified" && string.IsNullOrWhiteSpace(input.Setting))
        {
            throw new BadRequestException("Setting mode 'specified' yêu cầu mô tả bối cảnh.");
        }
    }

    private static void ValidateDataAnnotations(object value)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), results, true))
        {
            throw new BadRequestException(string.Join(" ", results.Select(item => item.ErrorMessage)));
        }
    }

    private static AcceptedAIStoryInputSnapshot Normalize(AIStoryCreativeInputDto input)
    {
        var characterMode = NormalizeToken(input.CharacterMode);
        var settingMode = NormalizeToken(input.SettingMode);
        return new AcceptedAIStoryInputSnapshot(
            NormalizeText(input.Topic),
            string.IsNullOrWhiteSpace(input.Genre) ? null : NormalizeText(input.Genre),
            characterMode,
            characterMode == "ai_suggested"
                ? []
                : input.Characters.Select(NormalizeText).Where(item => item.Length > 0).ToArray(),
            settingMode,
            settingMode == "ai_suggested" || string.IsNullOrWhiteSpace(input.Setting) ? null : NormalizeText(input.Setting),
            NormalizeText(input.Lesson),
            NormalizeToken(input.VocabularyLevel),
            string.IsNullOrWhiteSpace(input.Language) ? string.Empty : NormalizeToken(input.Language),
            input.TargetLength);
    }

    private static string NormalizeText(string value) => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string NormalizeToken(string value) => NormalizeText(value).ToLowerInvariant();

    private static string ToApprovalModeValue(ApprovalMode mode) => mode == ApprovalMode.AlwaysManual
        ? "always_manual"
        : "auto_publish_on_threshold";

    private static string Fingerprint<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string InputFingerprint(AcceptedAIStoryInputSnapshot input, StoryOutputMode outputMode) =>
        outputMode == StoryOutputMode.Learning
            ? Fingerprint(input) // Giữ tương thích fingerprint của request cũ.
            : Fingerprint(new { input, outputMode = StoryOutputModeContract.Format(outputMode) });

    private static InputGuardrailResult TechnicalFailure(string code, string fallback) =>
        new(InputGuardrailDecision.Error, code, fallback, true, RuleBasedInputGuardrail.Version);

    private static string SanitizeFallback(string value) => value.Length <= 500 ? value : value[..500];

    private AIStoryInputContextDto ToContextDto(EffectiveContext context) => new()
    {
        ChildProfileId = context.Snapshot.ChildProfileId ?? throw new InvalidOperationException("CHILD_REQUIRED"),
        ChildNickname = context.ChildNickname,
        AgeBand = context.AgeBand,
        ReadingLevel = context.ReadingLevel,
        DefaultVocabularyLevel = context.VocabularyLevel,
        AvailableVocabularyLevels = VocabularyLevels,
        DefaultLanguage = context.Language,
        AvailableLanguages = [context.Language],
        MaximumLength = context.MaximumLength,
        RecommendedTargetLength = _learningOptions.Profile(context.AgeBand).TargetWords,
        RequiredApprovalMode = ToApprovalModeValue(context.ApprovalMode),
        ParentalGateEnabled = context.Snapshot.ParentalGateEnabled,
        SafetyScoreThreshold = context.Snapshot.SafetyScoreThreshold,
        ReadabilityScoreThreshold = context.Snapshot.ReadabilityScoreThreshold,
        ComprehensionGoal = context.Snapshot.ComprehensionGoal,
        ComprehensionThresholdPercent = context.Snapshot.ComprehensionThresholdPercent,
        ComprehensionWindowSize = context.Snapshot.ComprehensionWindowSize,
        Interests = context.Snapshot.Interests,
        AllowedCategoryCodes = context.Snapshot.AllowedCategoryCodes,
        RestrictedCategoryCodes = context.Snapshot.RestrictedCategoryCodes,
        BlockedCategoryCodes = context.Snapshot.BlockedCategoryCodes
    };

    private static AIStoryInputProgressDto ToProgressDto(StoryGenerationRequest request) => new()
    {
        StoryId = request.StoryId,
        OutputMode = StoryOutputModeContract.Format(request.Story?.OutputMode ?? StoryOutputMode.Learning),
        RequestId = request.Id,
        InputStatus = request.Status switch
        {
            GenerationInputStatus.PendingInput => "pending_input",
            GenerationInputStatus.CheckingInput => "checking_input",
            GenerationInputStatus.InputAccepted => "input_accepted",
            GenerationInputStatus.InputBlocked => "input_blocked",
            _ => "input_check_failed"
        },
        HandoffStatus = request.HandoffJobId.HasValue || request.HandoffJob is not null ? "pending_dispatch" : "none",
        HandoffJobId = request.HandoffJobId == 0 ? request.HandoffJob?.Id : request.HandoffJobId,
        AttemptCount = request.AttemptCount,
        MaxAttempts = request.MaxAttempts,
        ReasonCode = request.ReasonCode,
        FallbackMessage = request.FallbackMessage,
        CanRetry = request.CanRetry,
        CreatedAt = request.CreatedAt,
        UpdatedAt = request.UpdatedAt,
        GuardrailCheckedAt = request.GuardrailCheckedAt,
        HandoffCreatedAt = request.HandoffCreatedAt
    };

    private sealed record EffectiveContext(
        string ChildNickname,
        string AgeBand,
        int ReadingLevel,
        string VocabularyLevel,
        string Language,
        int MaximumLength,
        ApprovalMode ApprovalMode,
        AIStoryInputContextSnapshot Snapshot,
        string Fingerprint,
        IReadOnlyList<string> BlockedTerms,
        IReadOnlyList<string> RestrictedTerms);
}
