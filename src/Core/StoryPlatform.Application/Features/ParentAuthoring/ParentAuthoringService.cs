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

    public ParentAuthoringService(IUnitOfWork uow, CreatorAuthoringOptions options, IImportPreviewProtector previews,
        ISupervisionAccessGuard guard, IStorySegmentService segments)
        : base(uow, options, previews, new ParentManualAuthoringPolicy(uow, guard))
    {
        _uow = uow; _guard = guard; _segments = segments;
        _policy = new ParentManualAuthoringPolicy(uow, guard);
    }

    public async Task<ParentDraftDto> CreateParentDraftAsync(int user, CreateParentDraftRequest request, CancellationToken ct)
    {
        var child = await _policy.RequireChildAsync(user, request.ChildProfileId, ct);
        var draft = await CreateDraftWithContextAsync(user, new CreateCreatorDraftRequest
        {
            OperationId = request.OperationId, Title = request.Title,
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
            story.AuthoringRevisionToken = Guid.NewGuid().ToString("N");
            _uow.Repository<Story>().Update(story);
            return await GetParentDraftAsync(user, id, ct);
        }, ct);

    public Task<ParentCheckDto> CheckAsync(int user, int id, ParentCheckRequest request, CancellationToken ct) =>
        ExecuteAsync(user, id, "Check", request, async (story, operation) =>
        {
            var version = await SourceAsync(story, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            var context = await ContextAsync(story, ct);
            var errors = new List<string>(); var warnings = new List<string>();
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

    public Task<ParentPreparationDto> PrepareAsync(int user, int id, ParentPrepareRequest request, CancellationToken ct) =>
        ExecuteAsync(user, id, "Preparation", request, async (story, operation) =>
        {
            var version = await SourceAsync(story, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            await LinkedAsync<ParentContentConfirmationDto>(story, request.ContentConfirmationId, "ContentConfirmation", version.Id, ct);
            var context = await ContextAsync(story, ct);
            var parts = Segment(version.Content ?? string.Empty, request.SegmentEnds);
            // The initial backend contract supports full-text reading. Listening is explicitly NOT ready
            // until a manual audio upload/alignment contract exists; never fall back to paid TTS.
            string[] missing = story.ExperienceMode == "reading" ? [] : ["MANUAL_AUDIO_PREPARATION_NOT_AVAILABLE"];
            operation.StoryVersionId = version.Id; operation.ContextFingerprint = context.Fingerprint;
            return new ParentPreparationDto(operation.Id, id, version.Id, story.ChildProfileId!.Value, story.AudienceRevision, context.Fingerprint,
                version.Title, story.Language, story.ExperienceMode, parts, missing.Length == 0, missing);
        }, ct);

    public async Task<ParentPreparationDto> PreviewAsync(int user, int id, int preparationId, CancellationToken ct)
    {
        await GetDraftAsync(user, id, ct); // Account status and current supervision, even for read-only preview.
        var story = await StoryAsync(user, id, ct);
        var version = await CurrentAsync(id, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
        return await LinkedAsync<ParentPreparationDto>(story, preparationId, "Preparation", version.Id, ct);
    }

    public Task<ParentFamilyConfirmationDto> ConfirmFamilyAsync(int user, int id, ParentFamilyConfirmRequest request, CancellationToken ct) =>
        ExecuteAsync(user, id, "FamilyConfirmation", request, async (story, operation) =>
        {
            await _guard.EnsurePermissionAsync(story.ChildProfileId!.Value, user, Permission.ApproveStory, ct);
            await _guard.EnsurePermissionAsync(story.ChildProfileId.Value, user, Permission.AssignActivity, ct);
            var version = await SourceAsync(story, request, ct) ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            var package = await LinkedAsync<ParentPreparationDto>(story, request.PreparationId, "Preparation", version.Id, ct);
            if (!package.IsReady) throw new ConflictException("PREPARATION_NOT_READY");
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
        return await LinkedAsync<ParentPreparationDto>(story, decision.PreparationId, "Preparation", decision.StoryVersionId, ct, pinnedSetup: true);
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
            catch (ForbiddenException) { } catch (ConflictException) { } catch (NotFoundException) { }
        }
        return result;
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
            if (await _uow.Repository<StoryVersion>().ExistsAsync(x => x.StoryId == id && x.OperationId == request.OperationId, ct))
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
            schema = 2, child.Id, child.Status, child.AgeBand, ChildLanguage = child.Language,
            Language = pinned?.Language ?? story.Language,
            ExperienceMode = pinned?.ExperienceMode ?? story.ExperienceMode,
            AudienceRevision = pinned?.AudienceRevision ?? story.AudienceRevision,
            policy = policy is null ? null : new { policy.Id, policy.MaxStoryLength, policy.SafetyScoreThreshold,
                policy.ReadabilityScoreThreshold, policy.RequiredApprovalMode, policy.ParentalGateEnabled, policy.AllowInteractiveStories },
            readingLevel = learning?.ReadingLevel, rules
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
