using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.Outline.DTOs;
using StoryPlatform.Application.Features.Outline.Guardrails;
using StoryPlatform.Application.Features.Outline.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Outline.Services;

public sealed class OutlineService : IOutlineService, IOutlineJobProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan JobLease = TimeSpan.FromMinutes(4);
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAIStoryGenerationClient _aiClient;
    private readonly IOutlineReviewGuardrail _guardrail;
    private readonly IOutlineJobFailureFinalizer _failureFinalizer;

    public OutlineService(
        IUnitOfWork unitOfWork,
        IAIStoryGenerationClient aiClient,
        IOutlineReviewGuardrail guardrail,
        IOutlineJobFailureFinalizer failureFinalizer)
    {
        _unitOfWork = unitOfWork;
        _aiClient = aiClient;
        _guardrail = guardrail;
        _failureFinalizer = failureFinalizer;
    }

    public async Task<OutlineProgressDto> GetCurrentAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var story = await LoadAuthorizedStoryForReviewAsync(userId, storyId, cancellationToken);
        var versions = await VersionsAsync(storyId, cancellationToken);
        var jobs = await JobsAsync(storyId, cancellationToken);
        return ToProgress(story, versions.FirstOrDefault(item => item.IsCurrent), jobs);
    }

    public async Task<IReadOnlyList<OutlineVersionDto>> GetVersionsAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await LoadAuthorizedStoryForReviewAsync(userId, storyId, cancellationToken);
        return (await VersionsAsync(storyId, cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<OutlineVersionDto> GetVersionAsync(
        int userId,
        int storyId,
        int versionNo,
        CancellationToken cancellationToken = default)
    {
        await LoadAuthorizedStoryForReviewAsync(userId, storyId, cancellationToken);
        return ToDto(await LoadVersionAsync(storyId, versionNo, cancellationToken));
    }

    public async Task<OutlineVersionDto> EditAsync(
        int userId,
        int storyId,
        int versionNo,
        EditOutlineRequestDto input,
        CancellationToken cancellationToken = default)
    {
        ValidateObject(input);
        var created = await InStoryTransactionAsync(storyId, async () =>
        {
            var story = await LoadAuthorizedStoryAsync(userId, storyId, Permission.GenerateStory, cancellationToken);
            EnsureReviewState(story);
            var current = await LoadCurrentVersionAsync(storyId, versionNo, cancellationToken);
            EnsureOutlineNotApproved(current);
            await EnsureNoActiveOutlineJobAsync(storyId, cancellationToken);
            var context = await LoadAcceptedContextAsync(storyId, cancellationToken);
            var safety = _guardrail.Validate(
                input.Title.Trim(), input.Opening.Trim(), input.Development.Trim(), input.Ending.Trim(),
                PolicyTerms(context.BlockedCategoryTerms, context.BlockedCategoryCodes),
                PolicyTerms(context.RestrictedCategoryTerms, context.RestrictedCategoryCodes));
            if (!safety.IsAllowed)
            {
                throw new BadRequestException($"{safety.ReasonCode}: {safety.FallbackMessage}");
            }

            var next = NewVersion(storyId, await NextVersionNoAsync(storyId, cancellationToken), VersionEditType.HumanEdited,
                userId, input.Title, input.Opening, input.Development, input.Ending);
            await SwitchCurrentVersionAsync(story, current, next, cancellationToken);
            return next;
        }, cancellationToken);
        return ToDto(created);
    }

    public async Task<OutlineProgressDto> RegenerateAsync(
        int userId,
        int storyId,
        int versionNo,
        RegenerateOutlineRequestDto input,
        CancellationToken cancellationToken = default)
    {
        ValidateObject(input);
        await InStoryTransactionAsync(storyId, async () =>
        {
            var story = await LoadAuthorizedStoryAsync(userId, storyId, Permission.GenerateStory, cancellationToken);
            EnsureReviewState(story);
            var current = await LoadCurrentVersionAsync(storyId, versionNo, cancellationToken);
            EnsureOutlineNotApproved(current);
            var key = input.OperationKey.Trim();
            var existing = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
                item => item.RequestedByUserId == userId &&
                        item.Operation == GenerationJobOperation.RegenerateOutline &&
                        item.OperationKey == key,
                cancellationToken: cancellationToken);
            if (existing is null)
            {
                await EnsureNoActiveOutlineJobAsync(storyId, cancellationToken);
                var generationRequest = await LoadAcceptedRequestAsync(storyId, cancellationToken);
                var job = new StoryGenerationJob
                {
                    StoryId = storyId,
                    GenerationRequestId = generationRequest.Id,
                    RequestedByUserId = userId,
                    OperationKey = key,
                    Operation = GenerationJobOperation.RegenerateOutline,
                    Stage = JobStage.OutlinePending,
                    Status = GenerationJobStatus.Pending,
                    BaseStoryVersionId = current.Id,
                    AttemptNo = 0,
                    MaxAttempts = 3,
                    StartedAt = DateTime.UtcNow
                };
                await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(job, cancellationToken);
            }
            else if (existing.StoryId != storyId || existing.BaseStoryVersionId != current.Id)
            {
                throw new ConflictException("Operation key đã được sử dụng cho Story hoặc base version khác.");
            }

            return true;
        }, cancellationToken);

        return await GetCurrentAsync(userId, storyId, cancellationToken);
    }

    public async Task<OutlineProgressDto> RetryInitialAsync(
        int userId,
        int storyId,
        RetryOutlineRequestDto input,
        CancellationToken cancellationToken = default)
    {
        ValidateObject(input);
        await InStoryTransactionAsync(storyId, async () =>
        {
            var story = await LoadAuthorizedStoryAsync(userId, storyId, Permission.GenerateStory, cancellationToken);
            if (story.Source != StorySource.Ai || story.Status != StoryStatus.Draft ||
                await _unitOfWork.Repository<StoryVersion>().ExistsAsync(item => item.StoryId == storyId, cancellationToken))
            {
                throw new ConflictException("Chỉ Story draft chưa có outline mới có thể retry initial generation.");
            }

            var key = input.OperationKey.Trim();
            var existing = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
                item => item.RequestedByUserId == userId &&
                        item.Operation == GenerationJobOperation.GenerateOutline &&
                        item.OperationKey == key,
                cancellationToken: cancellationToken);
            if (existing is not null)
            {
                if (existing.StoryId != storyId)
                {
                    throw new ConflictException("Operation key đã được sử dụng cho Story khác.");
                }
                return true;
            }

            await EnsureNoActiveOutlineJobAsync(storyId, cancellationToken);
            var failedInitial = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
                item => item.StoryId == storyId &&
                        item.Operation == GenerationJobOperation.GenerateOutline &&
                        item.Status == GenerationJobStatus.Failed,
                cancellationToken: cancellationToken);
            if (failedInitial is null)
            {
                throw new ConflictException("Story không có initial outline generation thất bại để retry.");
            }

            var request = await LoadAcceptedRequestAsync(storyId, cancellationToken);
            await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(new StoryGenerationJob
            {
                StoryId = storyId,
                GenerationRequestId = request.Id,
                RequestedByUserId = userId,
                OperationKey = key,
                Operation = GenerationJobOperation.GenerateOutline,
                Stage = JobStage.OutlinePending,
                Status = GenerationJobStatus.Pending,
                AttemptNo = 0,
                MaxAttempts = 3,
                StartedAt = DateTime.UtcNow
            }, cancellationToken);
            return true;
        }, cancellationToken);

        return await GetCurrentAsync(userId, storyId, cancellationToken);
    }

    public async Task<OutlineProgressDto> ApproveAsync(
        int userId,
        int storyId,
        int versionNo,
        ApproveOutlineRequestDto input,
        CancellationToken cancellationToken = default)
    {
        ValidateObject(input);
        return await InStoryTransactionAsync(storyId, async () =>
        {
            var story = await LoadAuthorizedStoryForApprovalAsync(userId, storyId, cancellationToken);
            EnsureReviewState(story);
            var version = await LoadCurrentVersionAsync(storyId, versionNo, cancellationToken);
            if (version.Content is not null)
            {
                throw new ConflictException("Chỉ outline chưa có content mới được approve trong Phase 2.");
            }

            await EnsureNoActiveOutlineJobAsync(storyId, cancellationToken);
            var key = input.ApprovalKey.Trim();
            var existing = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
                item => item.RequestedByUserId == userId &&
                        item.Operation == GenerationJobOperation.GenerateContent &&
                        item.OperationKey == key,
                cancellationToken: cancellationToken);
            if (existing is not null)
            {
                if (existing.StoryId != storyId || (existing.BaseStoryVersionId ?? existing.StoryVersionId) != version.Id)
                {
                    throw new ConflictException("Approval key đã được sử dụng cho StoryVersion khác.");
                }
                return ToProgress(story, version, await JobsAsync(storyId, cancellationToken));
            }

            if (version.OutlineApprovedAt.HasValue || await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(
                    item => item.Operation == GenerationJobOperation.GenerateContent &&
                            (item.BaseStoryVersionId == version.Id || item.StoryVersionId == version.Id),
                    cancellationToken))
            {
                throw new ConflictException("StoryVersion này đã được approve bằng một approval key khác.");
            }

            var generationRequest = await LoadAcceptedRequestAsync(storyId, cancellationToken);
            var handoff = new StoryGenerationJob
            {
                StoryId = storyId,
                GenerationRequestId = generationRequest.Id,
                BaseStoryVersionId = version.Id,
                RequestedByUserId = userId,
                OperationKey = key,
                Operation = GenerationJobOperation.GenerateContent,
                Stage = JobStage.ContentPending,
                Status = GenerationJobStatus.Pending,
                AttemptNo = 0,
                MaxAttempts = 3,
                StartedAt = DateTime.UtcNow
            };
            version.OutlineApprovedByUserId = userId;
            version.OutlineApprovedAt = DateTime.UtcNow;

            var sourceJob = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
                item => item.StoryVersionId == version.Id &&
                        (item.Operation == GenerationJobOperation.GenerateOutline ||
                         item.Operation == GenerationJobOperation.RegenerateOutline),
                cancellationToken: cancellationToken);
            _unitOfWork.Repository<StoryVersion>().Update(version);
            if (sourceJob is not null)
            {
                sourceJob.Stage = JobStage.OutlineApproved;
                _unitOfWork.Repository<StoryGenerationJob>().Update(sourceJob);
                RotateToken(sourceJob);
            }

            await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(handoff, cancellationToken);
            return ToProgress(story, version, (await JobsAsync(storyId, cancellationToken)).Append(handoff).ToArray());
        }, cancellationToken);
    }

    public async Task<OutlineProgressDto> RejectAsync(
        int userId,
        int storyId,
        int versionNo,
        RejectOutlineRequestDto input,
        CancellationToken cancellationToken = default)
    {
        ValidateObject(input);
        return await InStoryTransactionAsync(storyId, async () =>
        {
            var story = await LoadAuthorizedStoryAsync(userId, storyId, Permission.ApproveStory, cancellationToken);
            EnsureReviewState(story);
            var version = await LoadCurrentVersionAsync(storyId, versionNo, cancellationToken);
            await EnsureNoActiveOutlineJobAsync(storyId, cancellationToken);
            EnsureOutlineNotApproved(version);
            var job = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
                item => item.StoryVersionId == version.Id &&
                        (item.Operation == GenerationJobOperation.GenerateOutline ||
                         item.Operation == GenerationJobOperation.RegenerateOutline),
                cancellationToken: cancellationToken);
            story.Status = StoryStatus.Rejected;
            if (job is not null)
            {
                job.Stage = JobStage.OutlineRejected;
                job.FallbackMessage = input.Reason.Trim();
            }

            _unitOfWork.Repository<Story>().Update(story);
            if (job is not null)
            {
                _unitOfWork.Repository<StoryGenerationJob>().Update(job);
                RotateToken(job);
            }
            return ToProgress(story, version, await JobsAsync(storyId, cancellationToken));
        }, cancellationToken);
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var job = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
            item => ((item.Status == GenerationJobStatus.Pending && item.Stage == JobStage.OutlinePending) ||
                     (item.Status == GenerationJobStatus.Processing && item.Stage == JobStage.OutlineGenerating &&
                      item.LeaseExpiresAt < now)) &&
                    (item.Operation == GenerationJobOperation.GenerateOutline ||
                     item.Operation == GenerationJobOperation.RegenerateOutline),
            cancellationToken: cancellationToken);
        if (job is null)
        {
            return false;
        }

        job.Status = GenerationJobStatus.Processing;
        job.Stage = JobStage.OutlineGenerating;
        job.StartedAt = DateTime.UtcNow;
        job.LeaseExpiresAt = DateTime.UtcNow.Add(JobLease);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            RotateToken(job);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return false;
        }

        var claimedToken = job.ConcurrencyToken;
        try
        {
            await GenerateAndPersistAsync(job, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[OutlineJob {job.Id} Error] {exception}");
            await _failureFinalizer.MarkFailedAsync(
                job.Id,
                claimedToken,
                PublicErrorCode(exception),
                CancellationToken.None);
        }

        return true;
    }

    private async Task GenerateAndPersistAsync(StoryGenerationJob job, CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken)
                    ?? throw new InvalidOperationException("STORY_NOT_FOUND");
        var request = job.GenerationRequestId.HasValue
            ? await _unitOfWork.Repository<StoryGenerationRequest>().GetByIdAsync(job.GenerationRequestId.Value, cancellationToken)
            : await _unitOfWork.Repository<StoryGenerationRequest>().FirstOrDefaultAsync(
                item => item.HandoffJobId == job.Id, cancellationToken: cancellationToken);
        if (story.Source != StorySource.Ai || story.Status is StoryStatus.Archived or StoryStatus.Ready ||
            request is null || request.Status != GenerationInputStatus.InputAccepted ||
            string.IsNullOrWhiteSpace(request.AcceptedInputJson))
        {
            throw new InvalidOperationException("INVALID_PHASE1_HANDOFF");
        }

        var input = JsonSerializer.Deserialize<AcceptedAIStoryInputSnapshot>(request.AcceptedInputJson, JsonOptions)
                    ?? throw new InvalidOperationException("ACCEPTED_INPUT_MISSING");
        var context = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, JsonOptions)
                      ?? throw new InvalidOperationException("CONTEXT_SNAPSHOT_MISSING");
        ValidateConsentContext(context);
        StoryVersion? baseVersion = null;
        if (job.Operation == GenerationJobOperation.RegenerateOutline)
        {
            baseVersion = job.BaseStoryVersionId.HasValue
                ? await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(item => item.Id == job.BaseStoryVersionId.Value, cancellationToken: cancellationToken)
                : null;
            if (story.Status != StoryStatus.OutlineReview || baseVersion is null || !baseVersion.IsCurrent)
            {
                throw new InvalidOperationException("STALE_BASE_VERSION");
            }
        }
        else if (story.Status != StoryStatus.Draft)
        {
            throw new InvalidOperationException("STORY_STATE_CHANGED");
        }

        var aiRequest = new GenerateOutlineRequest
        {
            RequestId = $"outline-job-{job.Id}",
            AgeBand = context.AgeBand,
            ReadingLevel = context.ReadingLevel.ToString(),
            VocabularyLevel = context.VocabularyLevel,
            Language = context.Language,
            Source = "ai",
            Interests = context.Interests,
            ComprehensionGoal = context.ComprehensionGoal,
            StoryParameters = new StoryParametersDto
            {
                Genre = input.Genre,
                CharacterMode = input.CharacterMode,
                Characters = input.Characters,
                SettingMode = input.SettingMode,
                Setting = input.Setting ?? string.Empty,
                Topic = input.Topic,
                Lesson = input.Lesson,
                RequestedLength = input.TargetLength
            },
            Constraints = new GenerationConstraintsDto
            {
                MaximumWords = context.MaximumLength,
                AllowedTopics = context.AllowedCategoryCodes,
                RestrictedTopics = PolicyTerms(context.RestrictedCategoryTerms, context.RestrictedCategoryCodes),
                BlockedTopics = PolicyTerms(context.BlockedCategoryTerms, context.BlockedCategoryCodes)
            }
        };
        var result = await _aiClient.GenerateOutlineAsync(aiRequest, cancellationToken);
        var safety = _guardrail.Validate(result.Title, result.Outline.Opening, result.Outline.Development,
            result.Outline.Ending,
            PolicyTerms(context.BlockedCategoryTerms, context.BlockedCategoryCodes),
            PolicyTerms(context.RestrictedCategoryTerms, context.RestrictedCategoryCodes));
        if (!safety.IsAllowed)
        {
            throw new InvalidOperationException(safety.ReasonCode);
        }

        if (job.Operation == GenerationJobOperation.RegenerateOutline)
        {
            var stillCurrent = await _unitOfWork.Repository<StoryVersion>().ExistsAsync(
                item => item.Id == job.BaseStoryVersionId && item.StoryId == story.Id && item.IsCurrent,
                cancellationToken);
            if (!stillCurrent)
            {
                throw new InvalidOperationException("STALE_BASE_VERSION");
            }
        }

        var versions = await VersionsAsync(story.Id, cancellationToken);
        var current = versions.FirstOrDefault(item => item.IsCurrent);
        var created = NewVersion(story.Id, versions.Select(item => item.VersionNo).DefaultIfEmpty().Max() + 1,
            job.Operation == GenerationJobOperation.GenerateOutline ? VersionEditType.Initial : VersionEditType.AiRegenerated,
            null, result.Title, result.Outline.Opening, result.Outline.Development, result.Outline.Ending);
        story.Title = created.Title;
        story.Status = StoryStatus.OutlineReview;
        job.StoryVersion = created;
        job.Stage = JobStage.OutlineGenerated;
        job.Status = GenerationJobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.LeaseExpiresAt = null;
        job.AttemptNo = result.Metadata.AttemptCount;
        job.ErrorCode = null;
        job.FallbackMessage = null;
        job.GenerationMetadataJson = JsonSerializer.Serialize(result.Metadata, JsonOptions);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (current is not null)
            {
                current.IsCurrent = false;
                _unitOfWork.Repository<StoryVersion>().Update(current);
            }
            await _unitOfWork.Repository<StoryVersion>().AddAsync(created, cancellationToken);
            _unitOfWork.Repository<Story>().Update(story);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            RotateToken(job);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task SwitchCurrentVersionAsync(
        Story story,
        StoryVersion current,
        StoryVersion next,
        CancellationToken cancellationToken)
    {
        current.IsCurrent = false;
        story.Title = next.Title;
        _unitOfWork.Repository<StoryVersion>().Update(current);
        await _unitOfWork.Repository<StoryVersion>().AddAsync(next, cancellationToken);
        _unitOfWork.Repository<Story>().Update(story);
    }

    private async Task<Story> LoadAuthorizedStoryForApprovalAsync(
        int userId,
        int storyId,
        CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken)
                   ?? throw new ForbiddenException();
        if (user.Status == AccountStatus.Suspended || user.Role is not (UserRole.Parent or UserRole.Teacher))
        {
            throw new ForbiddenException();
        }
        var relationship = await _unitOfWork.Repository<SupervisionRelationship>().FirstOrDefaultAsync(
            item => item.ChildProfileId == story.ChildProfileId && item.SupervisorUserId == userId && item.RevokedAt == null,
            cancellationToken: cancellationToken);
        if (relationship is null)
        {
            throw new ForbiddenException();
        }
        return story;
    }

    private async Task<Story> LoadAuthorizedStoryAsync(
        int userId,
        int storyId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken)
                   ?? throw new ForbiddenException();
        if (user.Status == AccountStatus.Suspended || user.Role is not (UserRole.Parent or UserRole.Teacher))
        {
            throw new ForbiddenException();
        }
        var relationship = await _unitOfWork.Repository<SupervisionRelationship>().FirstOrDefaultAsync(
            item => item.ChildProfileId == story.ChildProfileId && item.SupervisorUserId == userId && item.RevokedAt == null,
            cancellationToken: cancellationToken);
        if (relationship is null || !await _unitOfWork.Repository<SupervisionPermission>().ExistsAsync(
                item => item.SupervisionRelationshipId == relationship.Id && item.Permission == permission,
                cancellationToken))
        {
            throw new ForbiddenException();
        }
        return story;
    }

    private async Task<Story> LoadAuthorizedStoryForReviewAsync(
        int userId,
        int storyId,
        CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken)
                   ?? throw new ForbiddenException();
        if (user.Status == AccountStatus.Suspended || user.Role is not (UserRole.Parent or UserRole.Teacher))
        {
            throw new ForbiddenException();
        }
        var relationship = await _unitOfWork.Repository<SupervisionRelationship>().FirstOrDefaultAsync(
            item => item.ChildProfileId == story.ChildProfileId && item.SupervisorUserId == userId && item.RevokedAt == null,
            cancellationToken: cancellationToken);
        if (relationship is null)
        {
            throw new ForbiddenException();
        }
        var canGenerate = await _unitOfWork.Repository<SupervisionPermission>().ExistsAsync(
            item => item.SupervisionRelationshipId == relationship.Id && item.Permission == Permission.GenerateStory,
            cancellationToken);
        var canApprove = await _unitOfWork.Repository<SupervisionPermission>().ExistsAsync(
            item => item.SupervisionRelationshipId == relationship.Id && item.Permission == Permission.ApproveStory,
            cancellationToken);
        if (!canGenerate && !canApprove)
        {
            throw new ForbiddenException();
        }
        return story;
    }

    private async Task<StoryGenerationRequest> LoadAcceptedRequestAsync(int storyId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<StoryGenerationRequest>().FirstOrDefaultAsync(
            item => item.StoryId == storyId && item.Status == GenerationInputStatus.InputAccepted,
            cancellationToken: cancellationToken)
        ?? throw new ConflictException("Story không có Accepted Input Snapshot hợp lệ.");

    private async Task<AIStoryInputContextSnapshot> LoadAcceptedContextAsync(int storyId, CancellationToken cancellationToken)
    {
        var request = await LoadAcceptedRequestAsync(storyId, cancellationToken);
        var context = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, JsonOptions)
                      ?? throw new ConflictException("Context Snapshot không hợp lệ.");
        ValidateConsentContext(context);
        return context;
    }

    private static void ValidateConsentContext(AIStoryInputContextSnapshot context)
    {
        if (!context.ConsentRecordedAt.HasValue || context.ConsentPolicyVersion <= 0)
            throw new InvalidOperationException("CONSENT_REQUIRED");
    }

    private async Task<StoryVersion> LoadCurrentVersionAsync(int storyId, int versionNo, CancellationToken cancellationToken)
    {
        var version = await LoadVersionAsync(storyId, versionNo, cancellationToken);
        if (!version.IsCurrent)
        {
            throw new ConflictException("StoryVersion không còn là current version.");
        }
        return version;
    }

    private async Task<StoryVersion> LoadVersionAsync(int storyId, int versionNo, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(
            item => item.StoryId == storyId && item.VersionNo == versionNo,
            cancellationToken: cancellationToken)
        ?? throw new NotFoundException("StoryVersion", versionNo);

    private Task<IReadOnlyList<StoryVersion>> VersionsAsync(int storyId, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<StoryVersion>().FindAsync(item => item.StoryId == storyId, cancellationToken: cancellationToken);

    private Task<IReadOnlyList<StoryGenerationJob>> JobsAsync(int storyId, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<StoryGenerationJob>().FindAsync(item => item.StoryId == storyId, cancellationToken: cancellationToken);

    private async Task<int> NextVersionNoAsync(int storyId, CancellationToken cancellationToken) =>
        (await VersionsAsync(storyId, cancellationToken)).Select(item => item.VersionNo).DefaultIfEmpty().Max() + 1;

    private async Task EnsureNoActiveOutlineJobAsync(int storyId, CancellationToken cancellationToken)
    {
        if (await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(
                item => item.StoryId == storyId &&
                        (item.Operation == GenerationJobOperation.GenerateOutline ||
                         item.Operation == GenerationJobOperation.RegenerateOutline) &&
                        (item.Status == GenerationJobStatus.Pending || item.Status == GenerationJobStatus.Processing),
                cancellationToken))
        {
            throw new ConflictException("Đang có một thao tác outline khác hoạt động cho Story này.");
        }
    }

    private static void EnsureOutlineNotApproved(StoryVersion version)
    {
        if (version.OutlineApprovedAt.HasValue || version.Content is not null)
            throw new ConflictException("Outline đã duyệt và bàn giao; không thể chỉnh sửa trong Phase 2.");
    }

    private static void EnsureReviewState(Story story)
    {
        if (story.Source != StorySource.Ai || story.Status != StoryStatus.OutlineReview)
        {
            throw new ConflictException("Story không ở trạng thái outline_review.");
        }
    }

    private static StoryVersion NewVersion(
        int storyId, int versionNo, VersionEditType editType, int? editorUserId,
        string title, string opening, string development, string ending) => new()
    {
        StoryId = storyId,
        VersionNo = versionNo,
        EditType = editType,
        EditorUserId = editorUserId,
        Title = title.Trim(),
        OutlineOpening = opening.Trim(),
        OutlineDevelopment = development.Trim(),
        OutlineEnding = ending.Trim(),
        Content = null,
        Lesson = null,
        IsCurrent = true
    };

    private static OutlineVersionDto ToDto(StoryVersion version) => new()
    {
        Id = version.Id,
        VersionNo = version.VersionNo,
        EditType = version.EditType.ToString(),
        Title = version.Title,
        Opening = version.OutlineOpening ?? string.Empty,
        Development = version.OutlineDevelopment ?? string.Empty,
        Ending = version.OutlineEnding ?? string.Empty,
        IsCurrent = version.IsCurrent,
        EditorUserId = version.EditorUserId,
        OutlineApprovedByUserId = version.OutlineApprovedByUserId,
        OutlineApprovedAt = version.OutlineApprovedAt,
        CreatedAt = version.CreatedAt
    };

    private static OutlineProgressDto ToProgress(
        Story story,
        StoryVersion? current,
        IReadOnlyList<StoryGenerationJob> jobs)
    {
        var active = jobs.Where(item => item.Status is GenerationJobStatus.Pending or GenerationJobStatus.Processing)
            .OrderByDescending(item => item.CreatedAt).FirstOrDefault();
        var latestFinished = jobs.Where(item => item.Status is GenerationJobStatus.Completed or GenerationJobStatus.Failed)
            .OrderByDescending(item => item.CompletedAt ?? item.CreatedAt).FirstOrDefault();
        var lastErrorCode = latestFinished?.Status == GenerationJobStatus.Failed ? latestFinished.ErrorCode : null;

        return new OutlineProgressDto
        {
            StoryId = story.Id,
            StoryStatus = ToSnake(story.Status.ToString()),
            CurrentVersion = current is null ? null : ToDto(current),
            ActiveOperation = active is null ? null : ToSnake(active.Operation.ToString()),
            ActiveJobStatus = active is null ? null : ToSnake(active.Status.ToString()),
            LastErrorCode = lastErrorCode
        };
    }

    private static string PublicErrorCode(Exception exception) => exception.Message switch
    {
        "STALE_BASE_VERSION" => "STALE_BASE_VERSION",
        "INVALID_PHASE1_HANDOFF" => "INVALID_PHASE1_HANDOFF",
        "ACCEPTED_INPUT_MISSING" => "ACCEPTED_INPUT_MISSING",
        "CONTEXT_SNAPSHOT_MISSING" => "CONTEXT_SNAPSHOT_MISSING",
        "STORY_STATE_CHANGED" => "STORY_STATE_CHANGED",
        "OUTLINE_BLOCKED_CONTENT" => "OUTLINE_BLOCKED_CONTENT",
        "OUTLINE_RESTRICTED_CONTENT" => "OUTLINE_RESTRICTED_CONTENT",
        "OUTLINE_SCHEMA_INVALID" => "OUTLINE_SCHEMA_INVALID",
        "OUTLINE_CONTAINS_PII" => "OUTLINE_CONTAINS_PII",
        "OUTLINE_PROMPT_LEAKAGE" => "OUTLINE_PROMPT_LEAKAGE",
        _ when exception is AIServiceRequestException aiServiceException => aiServiceException.ErrorCode,
        _ => "OUTLINE_GENERATION_FAILED"
    };

    private static void RotateToken(StoryGenerationJob job) => job.ConcurrencyToken = Guid.NewGuid().ToString("N");

    private static IReadOnlyList<string> PolicyTerms(
        IReadOnlyList<string>? terms,
        IReadOnlyList<string> fallbackCodes) => terms is { Count: > 0 } ? terms : fallbackCodes;

    private static string ToSnake(string value) => string.Concat(value.Select((character, index) =>
        char.IsUpper(character) && index > 0 ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    private async Task<T> InStoryTransactionAsync<T>(
        int storyId,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            var result = await action();
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static void ValidateObject(object value)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), results, true))
        {
            throw new BadRequestException(string.Join(" ", results.Select(item => item.ErrorMessage)));
        }
    }
}
