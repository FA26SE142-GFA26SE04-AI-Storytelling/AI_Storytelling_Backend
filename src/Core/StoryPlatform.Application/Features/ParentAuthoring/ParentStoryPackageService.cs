using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed partial class ParentStoryPackageService(IUnitOfWork uow, IChildProfileAccessGuard guard,
    StoryPlatform.Application.Features.ContentGeneration.LearningBatchOptions? learningOptions = null)
{
    private static readonly JsonSerializerOptions Json = ParentPackageContext.Json;
    public static async Task<bool> IsContentFinalizedAsync(IUnitOfWork uow, int storyId, CancellationToken ct)
    {
        var rows = await uow.Repository<StoryAuthoringOperation>().FindAsync(x => x.StoryId == storyId &&
            (x.Kind == "parent-content-finalize" || x.Kind == "parent-content-reopen"), cancellationToken: ct);
        return rows.OrderByDescending(x => x.Id).FirstOrDefault()?.Kind == "parent-content-finalize";
    }

    public Task<ParentContentFinalizationDto> FinalizeAsync(int actor, int storyId, ParentPackageFinalizeRequest request, CancellationToken ct) =>
        Command(actor, storyId, "parent-content-finalize", request, async story =>
        {
            var version = await Source(story, request, ct);
            if (string.IsNullOrWhiteSpace(version.Title) || string.IsNullOrWhiteSpace(version.Content)) throw new BadRequestException("CONTENT_REQUIRED");
            GrayZoneGate.EnsureCanContinue(story, version);
            if (await HasActiveJobs(storyId, ct)) throw new ConflictException("AUTHORING_JOB_ACTIVE");
            return new ParentContentFinalizationDto(storyId, version.Id, ParentPackageContext.Source(story, version), true);
        }, ct);

    public Task<ParentContentFinalizationDto> ReopenAsync(int actor, int storyId, ParentPackageFinalizeRequest request, CancellationToken ct) =>
        Command(actor, storyId, "parent-content-reopen", request, async story =>
        {
            var version = await Source(story, request, ct);
            if (await uow.Repository<ParentStoryOperation>().ExistsAsync(x => x.StoryId == storyId && x.Kind == "FamilyConfirmation" &&
                !x.IsDeleted && x.RevokedAt == null, ct)) throw new ConflictException("PARENT_STORY_FINALIZED");
            var packages = await uow.Repository<ParentStoryPackage>().FindAsync(x => x.StoryId == storyId && !x.IsDeleted, cancellationToken: ct);
            foreach (var package in packages)
            {
                package.Status = "Stale"; Rotate(package); uow.Repository<ParentStoryPackage>().Update(package);
            }
            var jobs = await uow.Repository<StoryGenerationJob>().FindAsync(x => x.StoryId == storyId && x.ParentStoryPackageId != null &&
                (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), cancellationToken: ct);
            foreach (var job in jobs) { job.Status = GenerationJobStatus.Cancelled; job.ConcurrencyToken = Guid.NewGuid().ToString("N"); uow.Repository<StoryGenerationJob>().Update(job); }
            uow.Repository<Story>().Update(story); // Attach the observed token before changing it.
            story.AuthoringRevisionToken = Guid.NewGuid().ToString("N"); uow.Repository<Story>().Update(story);
            return new ParentContentFinalizationDto(storyId, version.Id, ParentPackageContext.Source(story, version), false);
        }, ct);

    public Task<ParentPackageDto[]> CreateAsync(int actor, int storyId, CreateParentPackagesRequest request, CancellationToken ct) =>
        Command(actor, storyId, "parent-packages-create", request, async story =>
        {
            var version = await Source(story, request, ct);
            if (!await IsContentFinalizedAsync(uow, storyId, ct)) throw new ConflictException("PARENT_CONTENT_FINALIZATION_REQUIRED");
            if (request.Recipients.Length is < 1 or > 100 || request.Recipients.Select(x => x.ChildProfileId).Distinct().Count() != request.Recipients.Length)
                throw new BadRequestException("PACKAGE_RECIPIENTS_INVALID");
            var result = new List<ParentPackageDto>();
            foreach (var recipient in request.Recipients.OrderBy(x => x.ChildProfileId))
                result.Add(Dto(await AddPackage(story, version, actor, recipient.ChildProfileId,
                    recipient.Components ?? throw new BadRequestException("PACKAGE_COMPONENTS_REQUIRED"), null, [], ct)));
            return result.ToArray();
        }, ct);

    public Task<ParentPackageDto> ReviseAsync(int actor, int storyId, int packageId, ParentPackageReviseRequest request, CancellationToken ct) =>
        Command(actor, storyId, "parent-package-revise", request, async story =>
        {
            var previous = await Package(actor, storyId, packageId, ct); Expect(previous, request);
            if (!await IsContentFinalizedAsync(uow, storyId, ct)) throw new ConflictException("PARENT_CONTENT_FINALIZATION_REQUIRED");
            var version = await Current(storyId, ct);
            if (version.Id != previous.StoryVersionId) throw new ConflictException("PARENT_PACKAGE_SOURCE_CHANGED");
            var known = new[] { "vocabulary", "quiz", "listening", "images", "narration" };
            if (request.Regenerate.Any(x => !known.Contains(x))) throw new BadRequestException("PACKAGE_COMPONENT_INVALID");
            return Dto(await AddPackage(story, version, actor, previous.ChildProfileId, request.Components, previous, request.Regenerate, ct));
        }, ct, packageId);

    public Task<ParentPackageDto> GenerateAsync(int actor, int storyId, int packageId, ParentPackageCommand request, CancellationToken ct) =>
        Command(actor, storyId, "parent-package-generate", request, async _ =>
        {
            var p = await Package(actor, storyId, packageId, ct); Expect(p, request); Writable(p);
            await ParentPackageContext.ValidateAsync(uow, guard, p, ct, true);
            if (Check(p)?.Passed != true) throw new ConflictException("PACKAGE_CHECK_NOT_PASSED");
            if (await ActivePackageJob(p.Id, ct)) throw new ConflictException("PACKAGE_JOB_ACTIVE");
            var checkJob = await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.ParentStoryPackageId == p.Id && x.Operation == GenerationJobOperation.CheckParentPackage, cancellationToken: ct);
            await Queue(p, GenerationJobOperation.BuildParentPackage, ct, JsonSerializer.Deserialize<ParentPackageWorkState>(checkJob?.GenerationMetadataJson ?? "{}", Json));
            p.Status = "Generating"; p.LastErrorCode = null; Rotate(p); uow.Repository<ParentStoryPackage>().Update(p);
            return Dto(p);
        }, ct, packageId);

    public Task<ParentPackageDto> RetryAsync(int actor, int storyId, int packageId, ParentPackageRetryRequest request, CancellationToken ct) =>
        Command(actor, storyId, "parent-package-retry", request, async _ =>
        {
            var p = await Package(actor, storyId, packageId, ct); Expect(p, request); Writable(p);
            if (p.Status != "Failed") throw new ConflictException("PACKAGE_RETRY_NOT_AVAILABLE");
            await ParentPackageContext.ValidateAsync(uow, guard, p, ct, true);
            var failed = await uow.Repository<StoryGenerationJob>().FindAsync(x => x.ParentStoryPackageId == p.Id && x.Status == GenerationJobStatus.Failed,
                cancellationToken: ct);
            var targets = failed.Where(x => request.Components.Length == 0 || request.Components.Contains(Component(x.Operation)) || x.Operation == GenerationJobOperation.BuildParentPackage && request.Components.Any(c => c is "vocabulary" or "quiz" or "listening") || x.Operation == GenerationJobOperation.GenerateMediaPackage && request.Components.Any(c => c is "images" or "narration" or "listening")).ToArray();
            if (targets.Length == 0 || request.Components.Any(x => !new[] { "check", "learning", "media", "vocabulary", "quiz", "listening", "images", "narration" }.Contains(x)))
                throw new BadRequestException("PACKAGE_RETRY_COMPONENT_INVALID");
            var failedBatches = await uow.Repository<LearningArtifactGenerationBatch>().FindAsync(x => x.ParentStoryPackageId == p.Id && x.Status == GenerationJobStatus.Failed, cancellationToken: ct);
            foreach (var batch in failedBatches)
            {
                var selected = request.Components.Length == 0 || request.Components.Contains("learning") || request.Components.Any(x => batch.Kind.StartsWith(x, StringComparison.Ordinal));
                if (!selected) continue;
                if (batch.ErrorCode == "AI_OUTPUT_TRUNCATED") throw new ConflictException("AI_OUTPUT_TRUNCATED");
                uow.Repository<LearningArtifactGenerationBatch>().Update(batch);
                batch.Status = GenerationJobStatus.Pending; batch.AttemptCount = 0; batch.ErrorCode = null; batch.LeaseExpiresAt = null;
                batch.ConcurrencyToken = Guid.NewGuid().ToString("N"); uow.Repository<LearningArtifactGenerationBatch>().Update(batch);
            }
            var failedAssets = await uow.Repository<MediaAsset>().FindAsync(x => x.ParentStoryPackageId == p.Id &&
                (x.Status == MediaStatus.Failed || x.Status == MediaStatus.ManualReview), cancellationToken: ct);
            foreach (var asset in failedAssets)
            {
                var selected = request.Components.Length == 0 || request.Components.Contains("media") || request.Components.Contains("listening") ||
                    request.Components.Contains(asset.Type == MediaType.Illustration ? "images" : "narration");
                if (!selected) continue;
                asset.AttemptCount = 0; asset.Status = MediaStatus.Queued; asset.ValidationStatus = ValidationStatus.Pending;
                uow.Repository<MediaAsset>().Update(asset);
            }
            foreach (var job in targets)
            {
                var work = JsonSerializer.Deserialize<ParentPackageWorkState>(job.GenerationMetadataJson ?? "{}", Json)!;
                if (request.Components.Length == 0 || request.Components.Any(x => x is "learning" or "listening"))
                    foreach (var state in work.ListeningStates.Values) { state.ProviderCalls = 0; state.ValidationFailures = 0; state.PreviousErrors = []; }
                job.GenerationMetadataJson = JsonSerializer.Serialize(work, Json);
                if (job.ErrorCode == "AI_OUTPUT_TRUNCATED") throw new ConflictException("AI_OUTPUT_TRUNCATED");
                uow.Repository<StoryGenerationJob>().Update(job);
                job.Status = GenerationJobStatus.Pending; job.AttemptNo = 0; job.ErrorCode = null;
                job.LeaseExpiresAt = null; job.ConcurrencyToken = Guid.NewGuid().ToString("N");
                uow.Repository<StoryGenerationJob>().Update(job);
            }
            p.Status = targets.Any(x => x.Operation == GenerationJobOperation.CheckParentPackage) ? "PendingCheck" : "Generating";
            p.LastErrorCode = null; Rotate(p); uow.Repository<ParentStoryPackage>().Update(p); return Dto(p);
        }, ct, packageId);

    public async Task<ParentPackageDto[]> ListAsync(int actor, int storyId, CancellationToken ct)
    {
        await Owner(actor, storyId, ct);
        var rows = await uow.Repository<ParentStoryPackage>().FindAsync(x => x.StoryId == storyId && !x.IsDeleted, cancellationToken: ct);
        var result = new List<ParentPackageDto>();
        foreach (var row in rows.OrderByDescending(x => x.Id)) result.Add(await GetAsync(actor, storyId, row.Id, ct));
        return result.ToArray();
    }
    public async Task<ParentPackageDto> GetAsync(int actor, int storyId, int packageId, CancellationToken ct)
    {
        var p = await Package(actor, storyId, packageId, ct); var c = Components(p);
        var jobs = await uow.Repository<StoryGenerationJob>().FindAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, cancellationToken: ct);
        var vocabulary = await uow.Repository<StoryVocabulary>().CountAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, ct);
        var quiz = await uow.Repository<QuizItem>().CountAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, ct);
        var listening = await uow.Repository<ListeningInteraction>().CountAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, ct);
        string Progress(GenerationJobOperation op) => jobs.FirstOrDefault(x => x.Operation == op)?.Status.ToString() ?? "NotStarted";
        return Dto(p) with { VocabularyCount = vocabulary, QuizCount = quiz, ListeningCount = listening,
            ComponentStatus = new Dictionary<string, string> { ["check"] = Progress(GenerationJobOperation.CheckParentPackage),
                ["vocabulary"] = !c.Vocabulary ? "NotRequested" : vocabulary > 0 ? "Completed" : Progress(GenerationJobOperation.BuildParentPackage),
                ["quiz"] = !c.Quiz ? "NotRequested" : quiz > 0 ? "Completed" : Progress(GenerationJobOperation.BuildParentPackage),
                ["listening"] = !c.Listening ? "NotRequested" : Progress(GenerationJobOperation.BuildParentPackage),
                ["media"] = c.Images || c.Narration || c.Listening ? Progress(GenerationJobOperation.GenerateMediaPackage) : "NotRequested" } };
    }

    private async Task<ParentStoryPackage> AddPackage(Story story, StoryVersion version, int actor, int childId,
        ParentPackageComponents components, ParentStoryPackage? previous, string[] regenerate, CancellationToken ct)
    {
        await guard.EnsureOwnerAsync(childId, actor, ct);
        _ = await uow.Repository<ChildProfile>().GetByIdAsync(childId, ct) ?? throw new NotFoundException("ChildProfile", childId);
        var audience = await ParentPackageContext.CaptureAsync(uow, guard, actor, childId, ct, learningOptions: learningOptions);
        var revisions = await uow.Repository<ParentStoryPackage>().FindAsync(x => x.StoryVersionId == version.Id && x.ChildProfileId == childId, cancellationToken: ct);
        var p = new ParentStoryPackage { StoryId = story.Id, StoryVersionId = version.Id, ChildProfileId = childId, CreatedByUserId = actor,
            Revision = revisions.Select(x => x.Revision).DefaultIfEmpty().Max() + 1, PreviousPackageId = previous?.Id,
            SourceFingerprint = ParentPackageContext.Source(story, version), ContextFingerprint = audience.Fingerprint,
            ContextSnapshotJson = JsonSerializer.Serialize(audience, Json), ComponentsJson = JsonSerializer.Serialize(components, Json) };
        await uow.Repository<ParentStoryPackage>().AddAsync(p, ct); await uow.SaveChangesAsync(ct);
        await Queue(p, GenerationJobOperation.CheckParentPackage, ct, new() { Regenerate = regenerate });
        return p;
    }

    private async Task Queue(ParentStoryPackage p, GenerationJobOperation operation, CancellationToken ct, ParentPackageWorkState? work = null)
    {
        var roots = await uow.Repository<StoryGenerationJob>().FindAsync(x => x.ParentStoryPackageId == p.Id && x.Operation == operation && x.ParentJobId == null,
            cancellationToken: ct);
        if (roots.Count > 0) throw new ConflictException("PACKAGE_JOB_ALREADY_EXISTS");
        await uow.Repository<StoryGenerationJob>().AddAsync(new() { StoryId = p.StoryId, StoryVersionId = p.StoryVersionId,
            BaseStoryVersionId = p.StoryVersionId, ParentStoryPackageId = p.Id, RequestedByUserId = p.CreatedByUserId, Operation = operation,
            Status = GenerationJobStatus.Pending, StartedAt = DateTime.UtcNow, Stage = JobStage.ContentArtifactPending, OperationKey = $"parent-package:{p.Id}:{operation}",
            GenerationMetadataJson = JsonSerializer.Serialize(work ?? new ParentPackageWorkState(), Json), MaxAttempts = 3 }, ct);
    }
    private async Task<Story> Owner(int actor, int storyId, CancellationToken ct)
    {
        var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == storyId && !x.IsDeleted, cancellationToken: ct) ?? throw new NotFoundException("Story", storyId);
        var user = await uow.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == actor && !x.IsDeleted, cancellationToken: ct);
        if (user?.Role != UserRole.User || user.Status != AccountStatus.Active || user.LockedUntil > DateTime.UtcNow) throw new ForbiddenException();
        if (!StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story) || story.AuthorUserId != actor) throw new NotFoundException("Story", storyId);
        return story;
    }
    private async Task<ParentStoryPackage> Package(int actor, int storyId, int id, CancellationToken ct)
    {
        await Owner(actor, storyId, ct);
        return await uow.Repository<ParentStoryPackage>().FirstOrDefaultAsync(x => x.Id == id && x.StoryId == storyId && x.CreatedByUserId == actor && !x.IsDeleted,
            cancellationToken: ct) ?? throw new NotFoundException("ParentStoryPackage", id);
    }
    private async Task<StoryVersion> Current(int storyId, CancellationToken ct) =>
        await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent && !x.IsDeleted, cancellationToken: ct)
        ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
    private async Task<StoryVersion> Source(Story story, CreatorVersionCommand request, CancellationToken ct)
    {
        var version = await Current(story.Id, ct);
        if (string.IsNullOrWhiteSpace(request.RevisionToken) || request.RevisionToken != story.AuthoringRevisionToken || request.BaseVersionId != version.Id)
            throw new ConflictException("AUTHORING_SOURCE_CHANGED");
        return version;
    }
    private async Task<T> Command<T>(int actor, int storyId, string kind, CreatorCommand request, Func<Story, Task<T>> action,
        CancellationToken ct, int? packageId = null)
    {
        if (string.IsNullOrWhiteSpace(request.OperationId) || request.OperationId.Length > 100) throw new BadRequestException("OPERATION_ID_INVALID");
        var hash = ParentPackageContext.Hash(new { kind, packageId, payload = JsonSerializer.SerializeToElement(request, request.GetType(), Json) });
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(-actor, ct); await uow.AcquireTransactionLockAsync(storyId, ct);
            var story = await Owner(actor, storyId, ct);
            var receipt = await uow.Repository<StoryAuthoringOperation>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.OperationId == request.OperationId, cancellationToken: ct);
            if (receipt is not null)
            {
                if (receipt.ActorUserId != actor || receipt.Kind != kind || receipt.RequestHash != hash) throw new ConflictException("OPERATION_ID_REUSED");
                var replay = JsonSerializer.Deserialize<T>(receipt.OperationResponseJson, Json)!; await uow.CommitTransactionAsync(ct); return replay;
            }
            if (await uow.Repository<ParentStoryOperation>().ExistsAsync(x => x.StoryId == storyId && x.OperationId == request.OperationId, ct)) throw new ConflictException("OPERATION_ID_REUSED");
            var result = await action(story);
            var current = await Current(storyId, ct);
            await uow.Repository<StoryAuthoringOperation>().AddAsync(new() { StoryId = storyId, StoryVersionId = current.Id, ActorUserId = actor,
                Kind = kind, OperationId = request.OperationId, RequestHash = hash, OperationResponseJson = JsonSerializer.Serialize(result, Json) }, ct);
            await uow.SaveChangesAsync(ct); await uow.CommitTransactionAsync(ct); return result;
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); uow.DiscardTrackedChanges(); throw; }
    }
    private Task<bool> HasActiveJobs(int id, CancellationToken ct) => uow.Repository<StoryGenerationJob>().ExistsAsync(x => x.StoryId == id &&
        (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), ct);
    private Task<bool> ActivePackageJob(int id, CancellationToken ct) => uow.Repository<StoryGenerationJob>().ExistsAsync(x => x.ParentStoryPackageId == id &&
        (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), ct);
    internal static ParentPackageComponents Components(ParentStoryPackage p) => JsonSerializer.Deserialize<ParentPackageComponents>(p.ComponentsJson, Json)!;
    internal static ParentPackageCheck? Check(ParentStoryPackage p) => p.CheckResultJson == "{}" ? null : JsonSerializer.Deserialize<ParentPackageCheck>(p.CheckResultJson, Json);
    internal static ParentPackageDto Dto(ParentStoryPackage p) => new(p.Id, p.StoryId, p.StoryVersionId, p.ChildProfileId, p.Revision, p.ConcurrencyToken,
        p.Status, Components(p), Check(p), p.LastErrorCode, p.ConfirmedAt);
    internal static void Rotate(StoryPlatform.Application.Abstractions.Persistence.IUnitOfWork unitOfWork, ParentStoryPackage p)
    {
        unitOfWork.Repository<ParentStoryPackage>().Update(p); // Attach the observed token before changing it.
        p.ConcurrencyToken = Guid.NewGuid().ToString("N");
        unitOfWork.Repository<ParentStoryPackage>().Update(p);
    }
    private void Rotate(ParentStoryPackage p) => Rotate(uow, p);
    private static void Expect(ParentStoryPackage p, ParentPackageCommand request)
    { if (string.IsNullOrWhiteSpace(request.PackageToken) || p.ConcurrencyToken != request.PackageToken) throw new ConflictException("PACKAGE_REVISION_CHANGED"); }
    private static void Writable(ParentStoryPackage p)
    { if (p.Status is "Confirmed" or "Stale" or "Cancelled") throw new ConflictException("PACKAGE_IMMUTABLE"); }
    private static string Component(GenerationJobOperation op) => op == GenerationJobOperation.CheckParentPackage ? "check" : op == GenerationJobOperation.BuildParentPackage ? "learning" : "media";
}
