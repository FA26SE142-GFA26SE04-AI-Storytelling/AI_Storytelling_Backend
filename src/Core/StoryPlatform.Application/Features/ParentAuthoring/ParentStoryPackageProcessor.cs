using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ContentGeneration.Services;
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

/// <summary>Durable package orchestration, executed by the existing content worker.</summary>
public sealed partial class ParentStoryPackageProcessor(IUnitOfWork uow, IChildProfileAccessGuard guard, IAIStoryGenerationClient ai,
    ILearningBatchService batches, ListeningInteractionGenerator listening, ILogger<ParentStoryPackageProcessor> logger,
    IGrayZoneEvaluator moderation, StoryPlatform.Application.Features.MediaGeneration.Interfaces.IStorySegmentService segments,
    StoryPlatform.Application.Features.TokenQuota.Interfaces.ITokenQuotaService? tokenQuota = null,
        IAccountAiActivityService? aiActivity = null)
{
    private readonly IAccountAiActivityService _aiActivity =
        aiActivity ?? new StoryPlatform.Application.Features.AiActivity.Services.AccountAiActivityService(uow);
    private static readonly JsonSerializerOptions Json = ParentPackageContext.Json;
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var job = await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.ParentStoryPackageId != null &&
            (x.Operation == GenerationJobOperation.CheckParentPackage || x.Operation == GenerationJobOperation.BuildParentPackage) &&
            (x.NextAttemptAt == null || x.NextAttemptAt <= now) && (x.Status == GenerationJobStatus.Pending ||
                x.Status == GenerationJobStatus.Processing && x.LeaseExpiresAt <= now), cancellationToken: ct);
        if (job is null) return false;
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(job.StoryId, ct);
            var fresh = await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.Id == job.Id, cancellationToken: ct);
            if (fresh is null || fresh.ConcurrencyToken != job.ConcurrencyToken) { await uow.RollbackTransactionAsync(ct); return true; }
            job = fresh;
            uow.Repository<StoryGenerationJob>().Update(job);
            job.Status = GenerationJobStatus.Processing; job.AttemptNo++; job.ConcurrencyToken = Guid.NewGuid().ToString("N");
            job.LeaseExpiresAt = now.AddMinutes(10); job.StartedAt = now; uow.Repository<StoryGenerationJob>().Update(job);
            if (job.Operation == GenerationJobOperation.CheckParentPackage)
            {
                var checking = await Package(job, ct);
                checking.Status = "Checking"; ParentStoryPackageService.Rotate(uow, checking);
            }
            await uow.SaveChangesAsync(ct); await uow.CommitTransactionAsync(ct);
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); uow.DiscardTrackedChanges(); throw; }
        var token = job.ConcurrencyToken;
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["StoryId"] = job.StoryId, ["ParentStoryPackageId"] = job.ParentStoryPackageId, ["JobId"] = job.Id });
        try
        {
            var p = await Package(job, ct);
            var (story, version, audience) = await ParentPackageContext.ValidateAsync(uow, guard, p, ct, true);
            GrayZoneGate.EnsureCanContinue(story, version);
            if (job.Operation == GenerationJobOperation.CheckParentPackage) await CheckAsync(job, token, p, story, version, audience, ct);
            else await GenerateAsync(job, token, p, version, audience, ct);
            logger.LogInformation("Parent package work completed Package={PackageId} Operation={Operation}", p.Id, job.Operation);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Parent package work failed Package={PackageId} Operation={Operation} ErrorCode={ErrorCode}", job.ParentStoryPackageId, job.Operation, ParentPackageContext.ErrorCode(ex));
            await FailAsync(job, token, ex, ct);
        }
        return true;
    }

    private async Task CheckAsync(StoryGenerationJob job, string token, ParentStoryPackage p, Story story, StoryVersion version, ParentPackageAudience audience, CancellationToken ct)
    {
        var context = audience.Profile; var errors = new List<string>();
        if (ParentStoryPackageService.Components(p).Listening && !audience.AllowInteractiveStories) errors.Add("INTERACTIVE_STORIES_DISABLED");
        if (story.Language != context.Language) errors.Add("LANGUAGE_CONTEXT_MISMATCH");
        if (StoryWordCounter.Count(version.Content ?? "") > context.MaximumLength) errors.Add("STORY_TOO_LONG");
        if (!ReadabilityCalculator.EvaluateForProfile(version.Content ?? "", context.Language, context.ReadingLevel, context.ReadabilityScoreThreshold).Passed)
            errors.Add("READABILITY_POLICY_NOT_MET");
        var source = version.Title + "\n" + version.Content + "\n" + version.Lesson;
        foreach (var term in context.BlockedCategoryTerms ?? [])
            if (LearningArtifactGrounding.ContainsTerm(source, term)) errors.Add("BLOCKED_CATEGORY");
        if (errors.Count > 0)
        {
            var localCheck = new ParentPackageCheck(false, errors.Distinct().ToArray())
            {
                BackendPassed = false,
                SafetyStatus = ParentPackageAssessmentStatus.NotRun,
                GrayZoneStatus = ParentPackageAssessmentStatus.NotRun,
                Warnings = [],
                RequiresParentAcknowledgement = false
            };
            await Finish(job, token, p, async (_, package) =>
            {
                package.GrayZoneAssessmentJson = "{}";
                package.CheckResultJson = JsonSerializer.Serialize(localCheck, Json);
                package.Status = "Blocked";
                package.LastErrorCode = null;
                await Task.CompletedTask;
            }, ct);
            return;
        }
        var warnings = new List<ParentPackageAssessmentWarning>();
        StoryPlatform.Contracts.AI.Responses.EvaluateContentSafetyResponse? response = null;
        var safetyStatus = ParentPackageAssessmentStatus.Passed;
        try
        {
            response = await ai.EvaluateContentSafetyAsync(new()
            {
                RequestId = $"parent-package-{p.Id}-safety", Story = Content(version), AgeBand = context.AgeBand, Language = context.Language,
                BlockedTopics = context.BlockedCategoryTerms ?? [], RestrictedTopics = context.RestrictedCategoryTerms ?? [], ExecutionBudgetSeconds = 120
            }, ct) ?? throw new JsonException("CONTENT_SAFETY_SCORE_INVALID");
            ContentSafetyScoring.Validate(response, source);
            if (!response.IsAllowed || context.SafetyScoreThreshold.HasValue && (decimal)response.SafetyScore!.Value < context.SafetyScoreThreshold.Value)
            {
                safetyStatus = ParentPackageAssessmentStatus.Blocked;
                errors.Add("CONTENT_SAFETY_NOT_PASSED");
            }
        }
        catch (Exception ex) when (IsEvaluatorFailure(ex, ct))
        {
            response = null;
            safetyStatus = ParentPackageAssessmentStatus.Unavailable;
            warnings.Add(BuildUnavailableWarning("ContentSafety", ex, "CONTENT_SAFETY_UNAVAILABLE",
                "Chưa đánh giá được mức an toàn bằng AI. Bạn hãy tự đọc lại truyện và xác nhận trước khi tiếp tục."));
        }
        GrayZoneAssessment? assessment = null;
        var grayZoneStatus = ParentPackageAssessmentStatus.Passed;
        try
        {
            if (!moderation.IsConfigured && moderation is not GrayZoneEvaluator)
                throw new InvalidOperationException("MODERATION_NOT_CONFIGURED");
            var scan = new GrayZoneScan("phase3", context.AgeBand, context.Language, GrayZoneEvaluator.TextParts(version.Title, version.Content!),
                context.BlockedCategoryTerms ?? []) { ReadingLevel = context.ReadingLevel };
            assessment = await moderation.EvaluateAsync(scan, ct) ?? throw new InvalidOperationException("MODERATION_RESPONSE_INVALID");
            if (assessment.Zone == ModerationZone.HardBlock)
            {
                grayZoneStatus = ParentPackageAssessmentStatus.Blocked;
                errors.Add("MODERATION_HARD_BLOCK");
            }
            else if (assessment.Zone == ModerationZone.GrayZone)
            {
                grayZoneStatus = ParentPackageAssessmentStatus.Warning;
                warnings.Add(BuildGrayZoneWarning(assessment));
            }
        }
        catch (Exception ex) when (IsEvaluatorFailure(ex, ct))
        {
            assessment = null;
            grayZoneStatus = ParentPackageAssessmentStatus.Unavailable;
            warnings.Add(BuildUnavailableWarning("GrayZone", ex, "MODERATION_UNAVAILABLE",
                "Chưa kiểm tra được nội dung nhạy cảm bằng AI. Bạn hãy tự đọc lại truyện và xác nhận trước khi tiếp tục."));
        }
        var passed = errors.Count == 0;
        var check = new ParentPackageCheck(passed, errors.Distinct().ToArray(),
            response is not null ? (decimal)response.SafetyScore!.Value : null,
            response?.ScoreRuleVersion,
            response?.Evidence?.ToArray(),
            response?.ReasonCode,
            response?.Violations?.ToArray(),
            response?.Metadata)
        {
            BackendPassed = true,
            SafetyStatus = safetyStatus,
            GrayZoneStatus = grayZoneStatus,
            Warnings = warnings.ToArray(),
            RequiresParentAcknowledgement = passed && warnings.Count > 0
        };
        await Finish(job, token, p, async (_, package) =>
        {
            package.GrayZoneAssessmentJson = assessment is not null ? JsonSerializer.Serialize(assessment, Json) : "{}";
            package.CheckResultJson = JsonSerializer.Serialize(check, Json);
            package.Status = passed ? "Review" : "Blocked";
            package.LastErrorCode = null;
            if (passed) await ParentStoryPackageLayout.EnsureAsync(uow, version, segments, ct);
        }, ct);
    }

    private static bool IsEvaluatorFailure(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested && ex is OperationCanceledException) return false;
        if (ex is AppException app)
            return app is ConflictException && (app.Message.StartsWith("MODERATION_", StringComparison.Ordinal) ||
                app.Message.StartsWith("CONTENT_SAFETY_", StringComparison.Ordinal) ||
                app.Message.StartsWith("AI_", StringComparison.Ordinal));
        if (ex is InvalidOperationException)
            return ex.Message.StartsWith("MODERATION_", StringComparison.Ordinal) ||
                ex.Message.StartsWith("CONTENT_SAFETY_", StringComparison.Ordinal) ||
                ex.Message.StartsWith("AI_", StringComparison.Ordinal);
        return ex is AIServiceRequestException or System.Net.Http.HttpRequestException or TimeoutException or JsonException or FormatException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested;
    }

    private static ParentPackageAssessmentWarning BuildUnavailableWarning(string source, Exception ex, string fallbackCode, string reason)
    {
        var raw = ex is AIServiceRequestException ai && !string.IsNullOrWhiteSpace(ai.ErrorCode) ? ai.ErrorCode :
            ex is TimeoutException or OperationCanceledException ? "AI_EVALUATION_TIMEOUT" :
            ex is System.Net.Http.HttpRequestException ? "AI_PROVIDER_UNAVAILABLE" :
            System.Text.RegularExpressions.Regex.IsMatch(ex.Message ?? "", "^[A-Z][A-Z0-9_]{2,99}$") ? ex.Message! : fallbackCode;
        var code = System.Text.RegularExpressions.Regex.IsMatch(raw, "^[A-Z][A-Z0-9_]{2,99}$") ? raw : fallbackCode;
        return new(source, code, reason, [], DateTime.UtcNow);
    }

    private static ParentPackageAssessmentWarning BuildGrayZoneWarning(GrayZoneAssessment assessment)
    {
        var reasons = assessment.Findings
            .Select(f => string.IsNullOrWhiteSpace(f.Category) ? f.Reason : $"{f.Category}: {f.Reason}")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(GrayZoneEvaluator.Redact)
            .Distinct()
            .ToArray();
        var evidence = assessment.Findings
            .Select(f => f.EvidenceQuote)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => GrayZoneEvaluator.Redact(x!))
            .Distinct()
            .ToArray();
        var reason = reasons.Length > 0 ? string.Join("; ", reasons) : "Gray-zone content requires parent review and acknowledgement.";
        return new("GrayZone", "GRAY_ZONE_WARNING", reason, evidence, DateTime.UtcNow);
    }

    private async Task GenerateAsync(StoryGenerationJob job, string token, ParentStoryPackage p, StoryVersion version, ParentPackageAudience audience, CancellationToken ct)
    {
        var c = ParentStoryPackageService.Components(p);
        var state = JsonSerializer.Deserialize<ParentPackageWorkState>(job.GenerationMetadataJson ?? "{}", Json)!;
        // A moderator may have cleared this package after its check stopped before layout creation.
        await Checkpoint(job, token, async () => { await ParentStoryPackageLayout.EnsureAsync(uow, version, segments, ct); }, state, ct);
        await ReuseUnchangedAsync(job, token, p, state, ct);
        var generateVocabulary = c.Vocabulary && !state.Completed.Contains("vocabulary");
        var generateQuiz = c.Quiz && !state.Completed.Contains("quiz");
        if ((generateVocabulary || generateQuiz) && !state.Completed.Contains("learning"))
        {
            var result = await batches.GenerateSelectedAsync(job, token, version, audience.Profile, generateVocabulary, generateQuiz, ct);
            await Checkpoint(job, token, async () =>
            {
                if (generateVocabulary && !await uow.Repository<StoryVocabulary>().ExistsAsync(x => x.ParentStoryPackageId == p.Id, ct))
                    await uow.Repository<StoryVocabulary>().AddRangeAsync(result.Vocabulary.Items.Select(item =>
                    {
                        if (!LearningArtifactGrounding.TryAnchor(version.Content!, item.SourceQuote, out var anchor)) throw new JsonException("LEARNING_EVIDENCE_NOT_IN_STORY");
                        return new StoryVocabulary { ParentStoryPackageId = p.Id, StoryVersionId = version.Id, Term = item.Term, Definition = item.Definition,
                            SourceQuote = item.SourceQuote, SourceStart = anchor.Start, SourceEnd = anchor.End, Example = item.Example };
                    }), ct);
                if (generateQuiz && !await uow.Repository<QuizItem>().ExistsAsync(x => x.ParentStoryPackageId == p.Id, ct))
                {
                    var items = ContentGenerationService.MapPackageQuiz(result.Quiz.Items, version.Content!, audience.Profile);
                    foreach (var item in items) { item.ParentStoryPackageId = p.Id; item.StoryVersionId = version.Id; }
                    await uow.Repository<QuizItem>().AddRangeAsync(items, ct);
                }
                state.Completed = state.Completed.Append("learning").ToArray();
            }, state, ct);
        }
        if (c.Listening)
        {
            var scope = ListeningInteractionGenerator.CreateScope(version, audience.Profile) with { ParentStoryPackageId = p.Id };
            if (scope.Plan.Items.Count == 0) throw new ConflictException("LISTENING_PLAN_INFEASIBLE");
            foreach (var item in scope.Plan.Items)
            {
                var key = "listening:" + item.Type;
                if (state.Completed.Contains(key)) continue;
                var listeningState = state.ListeningStates.GetValueOrDefault(key) ?? new ListeningJobState();
                state.ListeningStates[key] = listeningState;
                var result = await listening.GenerateAsync(scope, item.Type, $"parent-package-{p.Id}", ct, listeningState,
                    async (value, cancellation) => await Checkpoint(job, token, () => Task.CompletedTask, state, cancellation));
                await Checkpoint(job, token, async () =>
                {
                    if (!await uow.Repository<ListeningInteraction>().ExistsAsync(x => x.ParentStoryPackageId == p.Id && x.Type == item.Type && !x.IsDeleted, ct))
                        await listening.InsertAsync(version.Id, result.Drafts, ct, p.Id);
                    state.Completed = state.Completed.Append(key).ToArray();
                }, state, ct);
            }
        }
        if (c.Listening && !(await listening.InspectAsync(ListeningInteractionGenerator.CreateScope(version, audience.Profile) with { ParentStoryPackageId = p.Id }, ct)).Valid)
            throw new ConflictException("LISTENING_PACKAGE_INVALID");
        await Finish(job, token, p, async (_, package) =>
        {
            if (c.Images || c.Narration || c.Listening)
            {
                var checkJob = await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.ParentStoryPackageId == p.Id && x.Operation == GenerationJobOperation.CheckParentPackage, cancellationToken: ct);
                var sourceState = JsonSerializer.Deserialize<ParentPackageWorkState>(checkJob?.GenerationMetadataJson ?? "{}", Json)!;
                await uow.Repository<StoryGenerationJob>().AddAsync(new() { StoryId = p.StoryId, StoryVersionId = p.StoryVersionId, ParentStoryPackageId = p.Id,
                    BaseStoryVersionId = p.StoryVersionId, RequestedByUserId = p.CreatedByUserId, Operation = GenerationJobOperation.GenerateMediaPackage,
                    Status = GenerationJobStatus.Pending, StartedAt = DateTime.UtcNow, MaxAttempts = 3, MediaPackageRevision = p.Revision,
                    GenerationMetadataJson = JsonSerializer.Serialize(sourceState, Json), OperationKey = $"parent-media:{p.Id}" }, ct);
                package.Status = "Generating";
            }
            else package.Status = "Review";
        }, ct);
    }

    private async Task Checkpoint(StoryGenerationJob observed, string token, Func<Task> action, ParentPackageWorkState state, CancellationToken ct)
    {
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(observed.StoryId, ct);
            var fresh = await RequireJob(observed.Id, token, ct); var p = await Package(fresh, ct);
            await ParentPackageContext.ValidateAsync(uow, guard, p, ct, true); await action();
            fresh.GenerationMetadataJson = JsonSerializer.Serialize(state, Json); fresh.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(10);
            uow.Repository<StoryGenerationJob>().Update(fresh); await uow.SaveChangesAsync(ct); await uow.CommitTransactionAsync(ct);
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); uow.DiscardTrackedChanges(); throw; }
    }
    private async Task Finish(StoryGenerationJob observed, string token, ParentStoryPackage p, Func<StoryGenerationJob, ParentStoryPackage, Task> action, CancellationToken ct)
    {
        var storyInfo = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == p.StoryId, cancellationToken: ct);
        await uow.BeginTransactionAsync(ct);
        try
        {
            if (storyInfo is not null)
                await uow.AcquireAccountAiActivityLockAsync(storyInfo.AuthorUserId, ct);
            await uow.AcquireTransactionLockAsync(p.StoryId, ct);
            var fresh = await RequireJob(observed.Id, token, ct); var package = await Package(fresh, ct);
            await ParentPackageContext.ValidateAsync(uow, guard, package, ct, true);
            await action(fresh, package); ParentStoryPackageService.Rotate(uow, package); uow.Repository<ParentStoryPackage>().Update(package);
            fresh.Status = GenerationJobStatus.Completed; fresh.CompletedAt = DateTime.UtcNow; fresh.LeaseExpiresAt = null;
            uow.Repository<StoryGenerationJob>().Update(fresh); await uow.SaveChangesAsync(ct);
            if (storyInfo is not null && package.Status is "Blocked" or "Failed" or "Stale" or "Cancelled" or "Confirmed")
                await _aiActivity.ReconcileStoryAiActivityAsync(storyInfo.AuthorUserId, p.StoryId, ct);
            await uow.CommitTransactionAsync(ct);
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); uow.DiscardTrackedChanges(); throw; }
    }
    private async Task FailAsync(StoryGenerationJob observed, string token, Exception error, CancellationToken ct)
    {
        var storyInfo = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == observed.StoryId, cancellationToken: ct);
        uow.DiscardTrackedChanges(); await uow.BeginTransactionAsync(ct);
        try
        {
            if (storyInfo is not null)
                await uow.AcquireAccountAiActivityLockAsync(storyInfo.AuthorUserId, ct);
            await uow.AcquireTransactionLockAsync(observed.StoryId, ct);
            var job = await RequireJob(observed.Id, token, ct); var p = await Package(job, ct);
            var code = ParentPackageContext.ErrorCode(error);
            var transient = error is TimeoutException or TaskCanceledException || error is AIServiceRequestException ai && ai.ErrorCode != "AI_OUTPUT_TRUNCATED" &&
                (ai.StatusCode is 408 or 429 || ai.StatusCode >= 500 && ai.ErrorCode != "AI_OUTPUT_INVALID");
            var retry = transient && job.AttemptNo < job.MaxAttempts;
            job.Status = retry ? GenerationJobStatus.Pending : GenerationJobStatus.Failed; job.ErrorCode = code.Length > 100 ? code[..100] : code; job.LeaseExpiresAt = null;
            if (retry) job.NextAttemptAt = DateTime.UtcNow.Add((error as AIServiceRequestException)?.RetryAfter ?? TimeSpan.FromSeconds(60));
            p.Status = retry ? p.Status : code is "CHILD_CONTEXT_CHANGED" or "PARENT_PACKAGE_SOURCE_CHANGED" ? "Stale" : "Failed";
            p.LastErrorCode = job.ErrorCode; ParentStoryPackageService.Rotate(uow, p);
            uow.Repository<StoryGenerationJob>().Update(job); uow.Repository<ParentStoryPackage>().Update(p);
            await uow.SaveChangesAsync(ct);
            if (!retry && storyInfo is not null)
                await _aiActivity.ReconcileStoryAiActivityAsync(storyInfo.AuthorUserId, observed.StoryId, ct);
            // Lỗi hẳn (không thử lại): trả lượt đang giữ của truyện nếu không còn gói nào đang chạy (Luồng 2).
            if (!retry && tokenQuota != null) await StoryPlatform.Application.Features.TokenQuota.Services.TokenQuotaService.ReleaseStoryCreditIfIdleAsync(uow, p.StoryId, ct);
            await uow.CommitTransactionAsync(ct);
        }
        catch (ConflictException) { await uow.RollbackTransactionAsync(CancellationToken.None); uow.DiscardTrackedChanges(); }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); uow.DiscardTrackedChanges(); throw; }
    }
    private async Task<StoryGenerationJob> RequireJob(int id, string token, CancellationToken ct) =>
        await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.Id == id && x.Status == GenerationJobStatus.Processing &&
            x.ConcurrencyToken == token && x.LeaseExpiresAt > DateTime.UtcNow, cancellationToken: ct) ?? throw new ConflictException("STALE_JOB_RESULT");
    private async Task<ParentStoryPackage> Package(StoryGenerationJob job, CancellationToken ct)
    {
        var p = await uow.Repository<ParentStoryPackage>().FirstOrDefaultAsync(x => x.Id == job.ParentStoryPackageId && !x.IsDeleted, cancellationToken: ct)
            ?? throw new ConflictException("PARENT_PACKAGE_NOT_FOUND");
        if (p.Status is "Confirmed" or "Stale" or "Cancelled" || p.StoryVersionId != job.StoryVersionId || p.StoryId != job.StoryId)
            throw new ConflictException("STALE_JOB_RESULT");
        return p;
    }
    private static StoryContentDto Content(StoryVersion v) => new() { Title = v.Title, Lesson = v.Lesson ?? "", StorySections = [new(1, "", v.Content!)] };
}
