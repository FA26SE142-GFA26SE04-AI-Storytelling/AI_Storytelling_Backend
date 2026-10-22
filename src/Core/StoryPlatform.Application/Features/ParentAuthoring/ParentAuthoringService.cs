using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.CreatorAuthoring;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;
using StoryPlatform.Application.Features.ManualAuthoring.Services;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

/// <summary>Family-only editing and handoff. AI suggestions are optional, through the separate assistance API.</summary>
public sealed class ParentAuthoringService : ManualAuthoringService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IUnitOfWork _uow;
    private readonly ISupervisionAccessGuard _guard;
    private readonly ParentManualAuthoringPolicy _policy;
    private readonly IStorySegmentService _segments;
    private readonly GrayZoneContentCheckService? _moderation;
    private readonly IMediaContextBuilder? _mediaContexts;
    private readonly IMediaGenerationService? _media;
    private readonly IGrayZoneEvaluator? _evaluator;

    public ParentAuthoringService(IUnitOfWork uow, CreatorAuthoringOptions options, IImportPreviewProtector previews,
        ISupervisionAccessGuard guard, IStorySegmentService segments, GrayZoneContentCheckService? moderation = null,
        IMediaContextBuilder? mediaContexts = null, IMediaGenerationService? media = null, IGrayZoneEvaluator? evaluator = null)
        : base(uow, options, previews, new ParentManualAuthoringPolicy(uow, guard))
    {
        _uow = uow; _guard = guard; _segments = segments; _moderation = moderation;
        _mediaContexts = mediaContexts; _media = media; _evaluator = evaluator;
        _policy = new ParentManualAuthoringPolicy(uow, guard);
    }

    public async Task<ParentDraftDto> CreateParentDraftAsync(int user, CreateParentDraftRequest request, CancellationToken ct)
    {
        var child = await _policy.RequireChildAsync(user, request.ChildProfileId, ct);
        var draft = await CreateDraftWithContextAsync(user, new CreateCreatorDraftRequest
        {
            OperationId = request.OperationId,
            Title = request.Title,
            Language = request.Language ?? child.Language,
            AgeBand = child.AgeBand == AgeBand.Age_9_10 ? "9-10" : "6-8",
            ExperienceMode = request.ExperienceMode
        }, new ManualDraftContext(request.ChildProfileId, request.Purpose), ct);
        return await GetParentDraftAsync(user, draft.StoryId, ct);
    }

    public async Task<ParentDraftDto> GetParentDraftAsync(int user, int storyId, CancellationToken ct)
    {
        var draft = await GetDraftAsync(user, storyId, ct);
        var story = await StoryAsync(user, storyId, ct);
        var locked = await _policy.IsLockedAsync(story.Id, ct);
        string? aiReason = locked ? "PARENT_STORY_FINALIZED" : null;
        if (!locked)
        {
            try { await new ParentAssistanceAccessPolicy(_uow, _guard).CaptureContextAsync(story, ct); }
            catch (StoryPlatform.Application.Features.CreatorAssistance.AssistanceException ex) { aiReason = ex.Code; }
        }
        return new(draft, story.ChildProfileId!.Value, story.WritingPurpose ?? "free",
            locked, locked ? "PARENT_STORY_FINALIZED" : null, aiReason is null, aiReason);
    }

    public async Task<IReadOnlyList<ParentOperationDto>> WorkflowAsync(int user, int id, CancellationToken ct)
    {
        var draft = await GetDraftAsync(user, id, ct);
        var story = await StoryAsync(user, id, ct);
        var fingerprint = (await ContextAsync(story, ct)).Fingerprint;
        var operations = await _uow.Repository<ParentStoryOperation>().FindAsync(x => x.StoryId == id && !x.IsDeleted, cancellationToken: ct);
        return operations.OrderByDescending(x => x.Id).Select(x => new ParentOperationDto(x.Id, x.Kind,
            x.StoryVersionId, x.StoryVersionId == draft.SelectedVersionId && x.ContextFingerprint == fingerprint,
            x.RevokedAt, JsonSerializer.Deserialize<JsonElement>(x.ResultJson, Json))).ToArray();
    }

    public Task<ParentDraftDto> SetupAsync(int user, int id, ParentSetupRequest request, CancellationToken ct) =>
        ExecuteAsync(user, id, "Setup", request, async (story, operation) =>
        {
            await SourceAsync(story, request, ct);
            ParentManualAuthoringPolicy.ValidatePurpose(request.Purpose);
            var mode = request.ExperienceMode ?? story.ExperienceMode;
            if (mode is not ("reading" or "listening" or "combined")) throw new BadRequestException("EXPERIENCE_MODE_INVALID");
            var child = await _policy.RequireChildAsync(user, story.ChildProfileId!.Value, ct);
            var language = request.Language ?? story.Language;
            if (language != child.Language || language is not ("vi" or "en")) throw new BadRequestException("LANGUAGE_CONTEXT_MISMATCH");
            if (language != story.Language || mode != story.ExperienceMode) story.AudienceRevision++;
            story.Language = language; story.ExperienceMode = mode;
            var age = child.AgeBand == AgeBand.Age_9_10 ? "9-10" : "6-8";
            if (story.AgeBand != age) { story.AgeBand = age; story.AudienceRevision++; }
            story.WritingPurpose = request.Purpose ?? story.WritingPurpose;
            if ((await CurrentAsync(id, ct))?.MediaState.MediaPackageRevision > 0) story.Status = StoryStatus.Draft;
            story.AuthoringRevisionToken = Guid.NewGuid().ToString("N");
            _uow.Repository<Story>().Update(story);
            return await GetParentDraftAsync(user, id, ct);
        }, ct);

    public async Task<ParentCheckDto> CheckAsync(int user, int id, ParentCheckRequest request, CancellationToken ct)
    {
        if (_moderation?.IsConfigured == true)
        {
            var story = await StoryAsync(user, id, ct);
            await _policy.EnsureWritableAsync(story, ct);
            var version = await SourceAsync(story, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            await _moderation.CheckAsync(user, id, version.Id, ct);
        }
        return await CheckCoreAsync(user, id, request, ct);
    }

    private Task<ParentCheckDto> CheckCoreAsync(int user, int id, ParentCheckRequest request, CancellationToken ct) =>
        ExecuteAsync(user, id, "Check", request, async (story, operation) =>
        {
            var version = await SourceAsync(story, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            var context = await ContextAsync(story, ct);
            var errors = new List<string>(); var warnings = new List<string>();
            if (GrayZoneGate.IsBlocked(story, version)) errors.Add("SAFETY_MODERATION_REQUIRED");
            var content = version.Content ?? string.Empty;
            var child = await _policy.RequireChildAsync(user, story.ChildProfileId!.Value, ct);
            if (story.Language != child.Language || story.AgeBand != (child.AgeBand == AgeBand.Age_9_10 ? "9-10" : "6-8"))
                errors.Add("CHILD_CONTEXT_SETUP_REQUIRED");
            if (string.IsNullOrWhiteSpace(version.Title)) errors.Add("TITLE_REQUIRED");
            if (string.IsNullOrWhiteSpace(content)) errors.Add("CONTENT_REQUIRED");
            if (context.Policy is null) errors.Add("SAFETY_POLICY_REQUIRED");
            else
            {
                if (context.Policy.MaxStoryLength <= 0) errors.Add("STORY_LENGTH_POLICY_INVALID");
                else if (Regex.Matches(content, @"\S+").Count > context.Policy.MaxStoryLength) errors.Add("STORY_TOO_LONG");
                // A keyword scan cannot substantiate a semantic safety score. Never synthesize 100/pass.
                if (context.Policy.SafetyScoreThreshold.HasValue) errors.Add("SEMANTIC_SAFETY_REVIEW_REQUIRED");
                if (context.Policy.ReadabilityScoreThreshold is < 0 or > 100) errors.Add("READABILITY_POLICY_INVALID");
                if (context.Policy.ReadabilityScoreThreshold.HasValue &&
                    !ReadabilityCalculator.EvaluateForProfile(content, story.Language, context.Learning?.ReadingLevel ?? 2,
                        context.Policy.ReadabilityScoreThreshold).Passed) errors.Add("READABILITY_POLICY_NOT_MET");
            }
            foreach (var term in context.Rules)
            {
                if (term.Rule == PolicyRule.Allowed) continue;
                var hit = new[] { term.Code, term.Name }.Where(x => !string.IsNullOrWhiteSpace(x)).Any(x =>
                    new[] { version.Title ?? string.Empty, content }.Any(text =>
                        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(x)}(?![\p{{L}}\p{{N}}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
                if (hit) errors.Add(term.Rule == PolicyRule.Blocked ? "BLOCKED_CATEGORY:" + term.Code : "POLICY_REVIEW_REQUIRED:" + term.Code);
            }
            if (content.Contains("___") || content.Contains("{{")) warnings.Add("UNFILLED_PLACEHOLDER");
            operation.StoryVersionId = version.Id; operation.ContextFingerprint = context.Fingerprint;
            return new ParentCheckDto(operation.Id, version.Id, context.Fingerprint, errors.Count == 0,
                errors.Distinct().ToArray(), warnings.ToArray(), "Technical limits, configured keywords in title/body and body readability only; no semantic safety assessment or AI.");
        }, ct);

    public Task<ParentContentConfirmationDto> ConfirmContentAsync(int user, int id, ParentContentConfirmRequest request, CancellationToken ct) =>
        ExecuteAsync(user, id, "ContentConfirmation", request, async (story, operation) =>
        {
            var version = await SourceAsync(story, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            var check = await LinkedAsync<ParentCheckDto>(story, request.CheckId, "Check", version.Id, ct);
            if (!check.CanPrepare) throw new ConflictException("CONTENT_CHECK_NOT_PASSED");
            if (request.Checklist is not { IntendedMeaning: true, SuitableForChild: true, PrivacyChecked: true, EndingChecked: true })
                throw new BadRequestException("PARENT_SELF_CHECK_REQUIRED");
            operation.StoryVersionId = version.Id; operation.ContextFingerprint = check.ContextFingerprint;
            return new ParentContentConfirmationDto(operation.Id, version.Id, check.CheckId, request.Checklist);
        }, ct);

    public async Task<ParentMediaGenerationDto> GenerateMediaAsync(int user, int id, ParentMediaGenerateRequest request, CancellationToken ct)
    {
        await GetDraftAsync(user, id, ct);
        var story = await StoryAsync(user, id, ct);
        string? contextSnapshot = null;
        var previous = await _uow.Repository<ParentStoryOperation>().FirstOrDefaultAsync(x => x.StoryId == id &&
            x.ActorUserId == user && x.OperationId == request.OperationId, cancellationToken: ct);
        if (previous is null)
        {
            await _policy.EnsureWritableAsync(story, ct);
            var version = await SourceAsync(story, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            await LinkedAsync<ParentContentConfirmationDto>(story, request.ContentConfirmationId, "ContentConfirmation", version.Id, ct);
            contextSnapshot = await new ParentAssistanceAccessPolicy(_uow, _guard).CaptureContextAsync(story, ct);
            if (_moderation?.IsConfigured != true || _evaluator?.IsConfigured != true || _mediaContexts is null)
                throw new ConflictException("MODERATION_NOT_CONFIGURED");
            await _moderation.CheckAsync(user, id, version.Id, ct);
        }
        return await ExecuteAsync(user, id, "MediaGeneration", request, async (locked, operation) =>
        {
            var version = await SourceAsync(locked, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            await LinkedAsync<ParentContentConfirmationDto>(locked, request.ContentConfirmationId, "ContentConfirmation", version.Id, ct);
            await new ParentAssistanceAccessPolicy(_uow, _guard).ValidateContextAsync(locked, contextSnapshot, ct);
            var assessment = GrayZoneWorkflowService.ReadAssessment(version);
            if (assessment is null || _evaluator is null || !await _evaluator.CanReuseAsync(assessment, ct))
                throw new ConflictException("CONTENT_MODERATION_CHECK_REQUIRED");
            GrayZoneGate.EnsureCanContinue(locked, version);
            if (await _uow.Repository<StoryGenerationJob>().ExistsAsync(x => x.StoryId == id &&
                x.StoryVersionId == version.Id && x.Operation == GenerationJobOperation.GenerateMediaPackage, ct))
                throw new ConflictException("MEDIA_JOB_ALREADY_EXISTS");
            var contextJson = _mediaContexts!.Build(new MediaContextBuildRequest(version.Id, version.Title, version.Content!, version.Lesson,
                null, null, null, null, contextSnapshot));
            await _uow.Repository<MediaContext>().AddAsync(new MediaContext { StoryVersionId = version.Id, Revision = 1, ContextJson = contextJson }, ct);
            MediaPackageConfirmation.Invalidate(version);
            _uow.Repository<StoryVersion>().Update(version);
            var job = new StoryGenerationJob
            {
                StoryId = id,
                StoryVersionId = version.Id,
                BaseStoryVersionId = version.Id,
                RequestedByUserId = user,
                OperationKey = $"p5:{id}:{version.Id}",
                Operation = GenerationJobOperation.GenerateMediaPackage,
                Status = GenerationJobStatus.Pending,
                Stage = JobStage.MediaPending,
                MaxAttempts = 3,
                StartedAt = DateTime.UtcNow
            };
            await _uow.Repository<StoryGenerationJob>().AddAsync(job, ct);
            locked.Status = StoryStatus.MediaProcessing;
            _uow.Repository<Story>().Update(locked);
            await _uow.SaveChangesAsync(ct);
            operation.StoryVersionId = version.Id;
            operation.ContextFingerprint = (await ContextAsync(locked, ct)).Fingerprint;
            return new ParentMediaGenerationDto(id, version.Id, job.Id, version.MediaState.MediaPackageRevision,
                $"/api/v1/stories/{id}/media/progress", $"/api/v1/stories/{id}/media/package");
        }, ct);
    }

    public Task<ParentPreparationDto> PrepareAsync(int user, int id, ParentPrepareRequest request, CancellationToken ct) =>
        ExecuteAsync(user, id, "Preparation", request, async (story, operation) =>
        {
            var version = await SourceAsync(story, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            await LinkedAsync<ParentContentConfirmationDto>(story, request.ContentConfirmationId, "ContentConfirmation", version.Id, ct);
            var context = await ContextAsync(story, ct);
            var parts = Segment(version.Content ?? string.Empty, request.SegmentEnds);
            // Text-only preparation never implicitly invokes providers. Opted-in media must be confirmed first.
            string[] missing = version.MediaState.MediaPackageRevision > 0
                ? story.Status == StoryStatus.Ready && MediaPackageConfirmation.IsConfirmed(version)
                    ? [] : ["MEDIA_PACKAGE_CONFIRMATION_REQUIRED"]
                : story.ExperienceMode == "reading" ? [] : ["MANUAL_AUDIO_PREPARATION_NOT_AVAILABLE"];
            operation.StoryVersionId = version.Id; operation.ContextFingerprint = context.Fingerprint;
            return new ParentPreparationDto(operation.Id, id, version.Id, story.ChildProfileId!.Value, story.AudienceRevision, context.Fingerprint,
                version.Title, story.Language, story.ExperienceMode, parts, missing.Length == 0, missing)
            { MediaPackageRevision = version.MediaState.MediaPackageRevision > 0 ? version.MediaState.MediaPackageRevision : null };
        }, ct);

    public async Task<ParentPreparationDto> PreviewAsync(int user, int id, int preparationId, CancellationToken ct)
    {
        await GetDraftAsync(user, id, ct); // Account status and current supervision, even for read-only preview.
        var story = await StoryAsync(user, id, ct);
        var version = await CurrentAsync(id, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
        var preparation = await LinkedAsync<ParentPreparationDto>(story, preparationId, "Preparation", version.Id, ct);
        return await WithMediaAsync(story, preparation, user, ct);
    }

    public Task<ParentFamilyConfirmationDto> ConfirmFamilyAsync(int user, int id, ParentFamilyConfirmRequest request, CancellationToken ct) =>
        ExecuteAsync(user, id, "FamilyConfirmation", request, async (story, operation) =>
        {
            await _guard.EnsurePermissionAsync(story.ChildProfileId!.Value, user, Permission.ApproveStory, ct);
            await _guard.EnsurePermissionAsync(story.ChildProfileId.Value, user, Permission.AssignActivity, ct);
            var version = await SourceAsync(story, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            var package = await LinkedAsync<ParentPreparationDto>(story, request.PreparationId, "Preparation", version.Id, ct);
            if (!package.IsReady) throw new ConflictException("PREPARATION_NOT_READY");
            await ValidatePreparationMediaAsync(story, version, package, ct);
            operation.StoryVersionId = version.Id; operation.ContextFingerprint = package.ContextFingerprint;
            // Deliberately do not set Ready, IsPublished or replace any older child's pinned grant.
            return new ParentFamilyConfirmationDto(operation.Id, id, version.Id, package.ChildProfileId, package.PreparationId, DateTime.UtcNow);
        }, ct);

    public async Task<ParentPreparationDto> ReadChildPackageAsync(int childId, int confirmationId, CancellationToken ct)
    {
        var grant = await _uow.Repository<ParentStoryOperation>().GetByIdAsync(confirmationId, ct);
        if (grant is null || grant.IsDeleted || grant.Kind != "FamilyConfirmation" || grant.RevokedAt.HasValue)
            throw new NotFoundException("Family confirmation", confirmationId);
        var decision = JsonSerializer.Deserialize<ParentFamilyConfirmationDto>(grant.ResultJson, Json)!;
        if (decision.ChildProfileId != childId) throw new NotFoundException("Family confirmation", confirmationId);
        var story = await _uow.Repository<Story>().GetByIdAsync(grant.StoryId, ct) ?? throw new NotFoundException("Story", grant.StoryId);
        await _policy.AuthorizeAsync(grant.ActorUserId, story, ct); // Revoked supervision revokes the issuing authority.
        await _guard.EnsurePermissionAsync(childId, grant.ActorUserId, Permission.ApproveStory, ct);
        await _guard.EnsurePermissionAsync(childId, grant.ActorUserId, Permission.AssignActivity, ct);
        var issuer = await _uow.Repository<UserAccount>().GetByIdAsync(grant.ActorUserId, ct);
        if (issuer is null || issuer.IsDeleted || issuer.Status != AccountStatus.Active || issuer.LockedUntil > DateTime.UtcNow)
            throw new ForbiddenException("GRANT_ISSUER_INACTIVE");
        var preparation = await LinkedAsync<ParentPreparationDto>(story, decision.PreparationId, "Preparation", decision.StoryVersionId, ct, pinnedSetup: true);
        var version = await _uow.Repository<StoryVersion>().GetByIdAsync(decision.StoryVersionId, ct)
            ?? throw new NotFoundException("StoryVersion", decision.StoryVersionId);
        await ValidatePreparationMediaAsync(story, version, preparation, ct);
        return await WithMediaAsync(story, preparation, grant.ActorUserId, ct);
    }

    public Task<bool> RevokeFamilyAsync(int user, int id, ParentFamilyRevokeRequest request, CancellationToken ct) =>
        ExecuteAsync(user, id, "FamilyRevocation", request, async (story, operation) =>
        {
            await SourceAsync(story, request, ct);
            var grant = await _uow.Repository<ParentStoryOperation>().GetByIdAsync(request.ConfirmationId, ct);
            if (grant is null || grant.StoryId != id || grant.Kind != "FamilyConfirmation" || grant.IsDeleted)
                throw new NotFoundException("Family confirmation", request.ConfirmationId);
            var wasActive = grant.RevokedAt is null;
            grant.RevokedAt ??= DateTime.UtcNow;
            if (wasActive && !await _uow.Repository<ParentStoryOperation>().ExistsAsync(x => x.StoryId == id &&
                x.Id != grant.Id && x.Kind == "FamilyConfirmation" && !x.IsDeleted && x.RevokedAt == null, ct))
            {
                story.AuthoringRevisionToken = Guid.NewGuid().ToString("N");
                story.AudienceRevision++;
                _uow.Repository<Story>().Update(story);
            }
            _uow.Repository<ParentStoryOperation>().Update(grant);
            operation.StoryVersionId = grant.StoryVersionId;
            return true;
        }, ct);

    public async Task<IReadOnlyList<ParentFamilyConfirmationDto>> ChildLibraryAsync(int childId, CancellationToken ct)
    {
        var grants = await _uow.Repository<ParentStoryOperation>().FindAsync(x => x.ChildProfileId == childId && x.Kind == "FamilyConfirmation" && !x.IsDeleted && x.RevokedAt == null, cancellationToken: ct);
        var result = new List<ParentFamilyConfirmationDto>();
        foreach (var row in grants.OrderByDescending(x => x.Id))
        {
            var grant = JsonSerializer.Deserialize<ParentFamilyConfirmationDto>(row.ResultJson, Json)!;
            if (grant.ChildProfileId != childId || result.Any(x => x.StoryId == grant.StoryId)) continue;
            try { await ReadChildPackageAsync(childId, grant.ConfirmationId, ct); result.Add(grant); }
            catch (ForbiddenException) { }
            catch (ConflictException) { }
            catch (NotFoundException) { }
        }
        return result;
    }

    private async Task ValidatePreparationMediaAsync(Story story, StoryVersion version, ParentPreparationDto preparation, CancellationToken ct)
    {
        if (version.MediaState.MediaPackageRevision == 0 && preparation.MediaPackageRevision is null) return;
        if (story.Status != StoryStatus.Ready || preparation.MediaPackageRevision != version.MediaState.MediaPackageRevision ||
            !MediaPackageConfirmation.IsConfirmed(version)) throw new ConflictException("MEDIA_PACKAGE_CONFIRMATION_REQUIRED");
        if (_media is null) throw new ConflictException("MEDIA_SERVICE_NOT_CONFIGURED");
        var package = await _media.GetPackageAsync(story.AuthorUserId, story.Id, ct);
        if (package.StoryVersionId != version.Id || !package.IsReady) throw new ConflictException("MEDIA_PACKAGE_INCOMPLETE");
        // Revalidate consent/context even if a previously confirmed package is read later.
        var contexts = await _uow.Repository<MediaContext>().FindAsync(x => x.StoryVersionId == version.Id, cancellationToken: ct);
        var context = contexts.OrderByDescending(x => x.Revision).FirstOrDefault() ?? throw new ConflictException("MEDIA_CONTEXT_REQUIRED");
        using var json = JsonDocument.Parse(context.ContextJson);
        await new ParentAssistanceAccessPolicy(_uow, _guard).ValidateContextAsync(story,
            json.RootElement.GetProperty("storyFacts").GetProperty("acceptedContext").GetRawText(), ct);
    }

    private async Task<ParentPreparationDto> WithMediaAsync(Story story, ParentPreparationDto preparation, int user, CancellationToken ct)
    {
        if (preparation.MediaPackageRevision is null) return preparation;
        var version = await _uow.Repository<StoryVersion>().GetByIdAsync(preparation.StoryVersionId, ct)
            ?? throw new NotFoundException("StoryVersion", preparation.StoryVersionId);
        await ValidatePreparationMediaAsync(story, version, preparation, ct);
        return preparation with { Media = await _media!.GetPackageAsync(user, story.Id, ct) };
    }

    private async Task<T> ExecuteAsync<T>(int user, int id, string kind, CreatorCommand request,
        Func<Story, ParentStoryOperation, Task<T>> action, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OperationId) || request.OperationId.Length > 100) throw new BadRequestException("OPERATION_ID_INVALID");
        var hash = Digest(new { kind, payload = JsonSerializer.SerializeToElement(request, request.GetType(), Json) });
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireTransactionLockAsync(-user, ct);
            await _uow.AcquireTransactionLockAsync(id, ct);
            await GetDraftAsync(user, id, ct);
            var story = await StoryAsync(user, id, ct);
            if (kind is "FamilyConfirmation" or "FamilyRevocation")
            {
                await _guard.EnsurePermissionAsync(story.ChildProfileId!.Value, user, Permission.ApproveStory, ct);
                await _guard.EnsurePermissionAsync(story.ChildProfileId.Value, user, Permission.AssignActivity, ct);
            }
            if (await _uow.Repository<StoryAuthoringOperation>().ExistsAsync(x => x.StoryId == id && x.OperationId == request.OperationId, ct))
                throw new ConflictException("OPERATION_ID_REUSED");
            var previous = await _uow.Repository<ParentStoryOperation>().FirstOrDefaultAsync(x => x.StoryId == id && x.ActorUserId == user && x.OperationId == request.OperationId, cancellationToken: ct);
            if (previous is not null)
            {
                if (previous.RequestHash != hash) throw new ConflictException("OPERATION_ID_REUSED");
                var replay = JsonSerializer.Deserialize<T>(previous.ResultJson, Json)!;
                await _uow.CommitTransactionAsync(ct);
                return replay;
            }
            if (kind != "FamilyRevocation") await _policy.EnsureWritableAsync(story, ct);
            var operation = new ParentStoryOperation { StoryId = id, ChildProfileId = story.ChildProfileId!.Value, ActorUserId = user, Kind = kind, OperationId = request.OperationId, RequestHash = hash };
            await _uow.Repository<ParentStoryOperation>().AddAsync(operation, ct);
            await _uow.SaveChangesAsync(ct);
            var result = await action(story, operation);
            operation.ResultJson = JsonSerializer.Serialize(result, Json);
            _uow.Repository<ParentStoryOperation>().Update(operation);
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);
            return result;
        }
        catch { await _uow.RollbackTransactionAsync(CancellationToken.None); throw; }
    }

    private async Task<Story> StoryAsync(int user, int id, CancellationToken ct)
    {
        var story = await _uow.Repository<Story>().GetByIdAsync(id, ct) ?? throw new NotFoundException("Story", id);
        await _policy.AuthorizeAsync(user, story, ct);
        return story;
    }
    private Task<StoryVersion?> CurrentAsync(int id, CancellationToken ct) => _uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == id && x.IsCurrent && !x.IsDeleted, cancellationToken: ct);
    private async Task<StoryVersion?> SourceAsync(Story story, CreatorVersionCommand request, CancellationToken ct)
    {
        var version = await CurrentAsync(story.Id, ct);
        if (request.RevisionToken != story.AuthoringRevisionToken || string.IsNullOrWhiteSpace(request.RevisionToken) || request.BaseVersionId != version?.Id)
            throw new ConflictException("AUTHORING_SOURCE_CHANGED");
        return version;
    }
    private async Task<T> LinkedAsync<T>(Story story, int operationId, string kind, int versionId, CancellationToken ct, bool pinnedSetup = false)
    {
        var row = await _uow.Repository<ParentStoryOperation>().GetByIdAsync(operationId, ct);
        if (row is null || row.IsDeleted || row.RevokedAt.HasValue || row.StoryId != story.Id || row.Kind != kind || row.StoryVersionId != versionId)
            throw new NotFoundException(kind, operationId);
        var linkedVersion = await _uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == versionId && x.StoryId == story.Id, cancellationToken: ct)
            ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
        GrayZoneGate.EnsureCanContinue(story, linkedVersion);
        var result = JsonSerializer.Deserialize<T>(row.ResultJson, Json)!;
        var pinned = pinnedSetup ? result as ParentPreparationDto : null;
        if (row.ContextFingerprint != (await ContextAsync(story, ct, pinned)).Fingerprint) throw new ConflictException("CHILD_CONTEXT_CHANGED");
        return result;
    }

    private sealed record PolicyTerm(string Code, string Name, PolicyRule RuleValue)
    { public PolicyRule Rule => RuleValue; }
    private sealed record Context(SafetyPolicy? Policy, LearningProfile? Learning, PolicyTerm[] Rules, string Fingerprint);
    private async Task<Context> ContextAsync(Story story, CancellationToken ct, ParentPreparationDto? pinned = null)
    {
        var child = await _uow.Repository<ChildProfile>().GetByIdAsync(story.ChildProfileId!.Value, ct) ?? throw new NotFoundException("ChildProfile", story.ChildProfileId.Value);
        var policy = await _uow.Repository<SafetyPolicy>().FirstOrDefaultAsync(x => x.ChildProfileId == child.Id, cancellationToken: ct);
        var learning = await _uow.Repository<LearningProfile>().FirstOrDefaultAsync(x => x.ChildProfileId == child.Id, cancellationToken: ct);
        var assignments = policy is null ? [] : await _uow.Repository<SafetyPolicyCategory>().FindAsync(x => x.SafetyPolicyId == policy.Id, cancellationToken: ct);
        var categories = await _uow.Repository<ContentCategory>().FindAsync(x => x.IsActive, cancellationToken: ct);
        // SafetyPolicyService materializes the optional age preset when policy is created.
        // Respect the saved policy; do not reapply a preset the owner deliberately disabled.
        var rules = categories.OrderBy(x => x.Id).Select(x => new PolicyTerm(x.Code, x.DisplayName,
            assignments.Where(a => a.ContentCategoryId == x.Id).Select(a => a.Rule).DefaultIfEmpty(PolicyRule.Allowed).Max())).ToArray();
        return new Context(policy, learning, rules, Digest(new
        {
            // Version 2 includes title keyword checks. Old body-only checks/packages must be rechecked.
            schema = 2,
            child.Id,
            child.Status,
            child.AgeBand,
            ChildLanguage = child.Language,
            Language = pinned?.Language ?? story.Language,
            ExperienceMode = pinned?.ExperienceMode ?? story.ExperienceMode,
            AudienceRevision = pinned?.AudienceRevision ?? story.AudienceRevision,
            policy = policy is null ? null : new
            {
                policy.Id,
                policy.MaxStoryLength,
                policy.SafetyScoreThreshold,
                policy.ReadabilityScoreThreshold,
                policy.RequiredApprovalMode,
                policy.ParentalGateEnabled,
                policy.AllowInteractiveStories
            },
            readingLevel = learning?.ReadingLevel,
            rules
        }));
    }

    private ParentSegmentDto[] Segment(string text, int[]? ends)
    {
        if (text.Length == 0) throw new ConflictException("CONTENT_REQUIRED");
        ends ??= _segments.CreateSegmentsForScene(new StoryScene { SceneText = text })
            .Select(x => x.StartOffset).Where(x => x > 0).Append(text.Length).Distinct().Order().ToArray();
        if (ends.Length == 0 || ends.Length > 5000 || ends[^1] != text.Length) throw new BadRequestException("SEGMENT_COVERAGE_INVALID");
        var result = new List<ParentSegmentDto>(); var start = 0;
        foreach (var end in ends)
        {
            if (end <= start || end > text.Length || (end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])))
                throw new BadRequestException("SEGMENT_BOUNDARY_INVALID");
            result.Add(new(result.Count + 1, start, end, text[start..end])); start = end;
        }
        return result.ToArray();
    }
    private static string Digest(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
}
