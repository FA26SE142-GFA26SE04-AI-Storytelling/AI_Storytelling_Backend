using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed partial class ParentStoryPackageService
{
    public Task<ParentPackageDto> ConfirmAsync(int actor, int storyId, int id, ParentPackageCommand request, CancellationToken ct) =>
        Command(actor, storyId, "parent-package-confirm", request, async _ =>
        {
            var p = await Package(actor, storyId, id, ct); Expect(p, request); Writable(p);
            if (p.Status != "Review" || Check(p)?.Passed != true || await ActivePackageJob(p.Id, ct)) throw new ConflictException("PACKAGE_NOT_READY");
            var (_, version, _) = await ParentPackageContext.ValidateAsync(uow, guard, p, ct);
            GrayZoneGate.EnsureCanContinue(await Owner(actor, storyId, ct), version);
            var manifest = await BuildManifestAsync(p, ct);
            p.ManifestJson = JsonSerializer.Serialize(manifest, Json);
            await ParentStoryPackageReader.ValidateManifestAsync(uow, p, ct);
            p.Status = "Confirmed";
            p.ConfirmedAt = DateTime.UtcNow; p.ConfirmedByUserId = actor; Rotate(p);
            uow.Repository<ParentStoryPackage>().Update(p); return Dto(p);
        }, ct, id);

    public Task<ParentFamilyConfirmationDto[]> DeliverAsync(int actor, int storyId, ParentPackageBatchGrantRequest request, CancellationToken ct) =>
        Command(actor, storyId, "parent-package-deliver", request, async story =>
        {
            if (request.Packages.Length is < 1 or > 100 || request.Packages.Select(x => x.PackageId).Distinct().Count() != request.Packages.Length)
                throw new BadRequestException("PACKAGE_RECIPIENTS_INVALID");
            var packages = new List<ParentStoryPackage>(); var errors = new List<string>();
            foreach (var target in request.Packages)
            {
                var p = await Package(actor, storyId, target.PackageId, ct);
                if (p.ConcurrencyToken != target.PackageToken || p.Status != "Confirmed")
                    errors.Add($"child:{p.ChildProfileId}:PACKAGE_NOT_CONFIRMED");
                else
                {
                    try
                    {
                        await ParentPackageContext.ValidateAsync(uow, guard, p, ct);
                        await ParentStoryPackageReader.ValidateManifestAsync(uow, p, ct);
                        var version = await uow.Repository<StoryVersion>().GetByIdAsync(p.StoryVersionId, ct) ?? throw new ConflictException("PARENT_PACKAGE_SOURCE_CHANGED");
                        GrayZoneGate.EnsureCanContinue(story, version);
                        if (await uow.Repository<ParentStoryOperation>().ExistsAsync(x => x.StoryId == storyId && x.ChildProfileId == p.ChildProfileId &&
                            x.Kind == "FamilyConfirmation" && !x.IsDeleted && x.RevokedAt == null, ct)) throw new ConflictException("CHILD_ALREADY_HAS_GRANT");
                    }
                    catch (AppException ex) { errors.Add($"child:{p.ChildProfileId}:{ex.Message}"); }
                }
                packages.Add(p);
            }
            if (packages.Select(x => x.ChildProfileId).Distinct().Count() != packages.Count) errors.Add("DUPLICATE_CHILD_RECIPIENT");
            if (packages.Select(x => x.StoryVersionId).Distinct().Count() != 1) errors.Add("PACKAGE_VERSIONS_DIFFER");
            if (errors.Count > 0) throw new ConflictException("PARENT_BATCH_NOT_READY: " + string.Join("; ", errors));
            var result = new List<ParentFamilyConfirmationDto>();
            foreach (var p in packages)
            {
                var preparation = new ParentStoryOperation { StoryId = storyId, StoryVersionId = p.StoryVersionId, ChildProfileId = p.ChildProfileId,
                    ParentStoryPackageId = p.Id, ActorUserId = actor, Kind = "Preparation", ContextFingerprint = p.ContextFingerprint,
                    OperationId = ParentPackageContext.Hash(new { request.OperationId, p.Id, kind = "prepare" }), RequestHash = p.SourceFingerprint };
                await uow.Repository<ParentStoryOperation>().AddAsync(preparation, ct); await uow.SaveChangesAsync(ct);
                var version = (await uow.Repository<StoryVersion>().GetByIdAsync(p.StoryVersionId, ct))!;
                preparation.ResultJson = JsonSerializer.Serialize(await ParentStoryPackageReader.PreparationAsync(uow, p, preparation.Id, version, ct), Json);
                uow.Repository<ParentStoryOperation>().Update(preparation);
                var grant = new ParentStoryOperation { StoryId = storyId, StoryVersionId = p.StoryVersionId, ChildProfileId = p.ChildProfileId,
                    ParentStoryPackageId = p.Id, ActorUserId = actor, Kind = "FamilyConfirmation", ContextFingerprint = p.ContextFingerprint,
                    OperationId = ParentPackageContext.Hash(new { request.OperationId, p.Id, kind = "grant" }), RequestHash = p.SourceFingerprint };
                await uow.Repository<ParentStoryOperation>().AddAsync(grant, ct); await uow.SaveChangesAsync(ct);
                var dto = new ParentFamilyConfirmationDto(grant.Id, storyId, p.StoryVersionId, p.ChildProfileId, preparation.Id, DateTime.UtcNow)
                    { ParentStoryPackageId = p.Id };
                grant.ResultJson = JsonSerializer.Serialize(dto, Json); uow.Repository<ParentStoryOperation>().Update(grant); result.Add(dto);
            }
            return result.ToArray();
        }, ct);

    public async Task<ParentPackagePreviewDto> PreviewAsync(int actor, int storyId, int id, CancellationToken ct)
    {
        var p = await Package(actor, storyId, id, ct);
        var (_, version, _) = await ParentPackageContext.ValidateAsync(uow, guard, p, ct);
        var manifest = p.Status == "Confirmed" ? JsonSerializer.Deserialize<ParentPackageManifest>(p.ManifestJson, Json)! : await BuildManifestAsync(p, ct);
        var words = await uow.Repository<StoryVocabulary>().FindAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, cancellationToken: ct);
        var quiz = await uow.Repository<QuizItem>().FindAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, cancellationToken: ct);
        var listening = await uow.Repository<ListeningInteraction>().FindAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, cancellationToken: ct);
        var listeningIds = listening.Select(x => x.Id).ToList();
        var options = await uow.Repository<ListeningInteractionOption>().FindAsync(x => listeningIds.Contains(x.ListeningInteractionId) && !x.IsDeleted, cancellationToken: ct);
        // Parent preview includes answers; child APIs use separate projections that hide them.
        var wordView = words.Select(x => new { x.Id, x.Term, x.Definition, x.SourceQuote, x.SourceStart, x.SourceEnd, x.Example }).ToArray();
        var quizView = quiz.Select(x => new { x.Id, Type = x.Type.ToString(), x.Question, x.CorrectAnswer, x.Choices, x.Explanation, x.Hint,
            x.LearningGoal, x.SourceQuote, x.SourceStart, x.SourceEnd }).ToArray();
        var listeningView = listening.Select(x => new { x.Id, Type = x.Type.ToString(), x.AnchorOffset, x.StorySceneId, x.Prompt,
            x.SourceQuote, x.SourceStart, x.SourceEnd, x.CharacterName, x.HiddenWord, x.Hint, x.RevealOffset, x.Explanation,
            x.MatchFeedback, x.NoMatchFeedback, x.WaitFeedback, Options = options.Where(o => o.ListeningInteractionId == x.Id)
                .OrderBy(o => o.SortOrder).Select(o => new { o.Id, o.SortOrder, o.Text, o.Feedback, o.IsMatch, o.CorrectOrder, o.ImageDescription }).ToArray() }).ToArray();
        // URLs are issued through the separately authorized asset endpoint; stored paths never leave this boundary.
        return new(await GetAsync(actor, storyId, id, ct), version.Title, version.Content!, manifest, wordView, quizView, listeningView,
            manifest.MediaAssetIds.Select(assetId => new { assetId, previewUrl = $"/api/v1/parent/stories/{storyId}/packages/{id}/assets/{assetId}" }).ToArray());
    }

    private async Task<ParentPackageManifest> BuildManifestAsync(ParentStoryPackage p, CancellationToken ct)
    {
        var c = Components(p);
        var roots = await uow.Repository<StoryGenerationJob>().FindAsync(x => x.ParentStoryPackageId == p.Id && x.ParentJobId == null, cancellationToken: ct);
        var needsGeneration = c.Vocabulary || c.Quiz || c.Listening || c.Images || c.Narration;
        if (needsGeneration && !roots.Any(x => x.Operation == GenerationJobOperation.BuildParentPackage && x.Status == GenerationJobStatus.Completed))
            throw new ConflictException("PACKAGE_LEARNING_INCOMPLETE");
        if ((c.Images || c.Narration || c.Listening) && !roots.Any(x => x.Operation == GenerationJobOperation.GenerateMediaPackage && x.Status == GenerationJobStatus.Completed))
            throw new ConflictException("MEDIA_PACKAGE_INCOMPLETE");
        var words = await uow.Repository<StoryVocabulary>().FindAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, cancellationToken: ct);
        var quiz = await uow.Repository<QuizItem>().FindAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, cancellationToken: ct);
        var listening = await uow.Repository<ListeningInteraction>().FindAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, cancellationToken: ct);
        var scenes = await uow.Repository<StoryScene>().FindAsync(x => x.StoryVersionId == p.StoryVersionId && !x.IsDeleted, cancellationToken: ct);
        var sceneIds = scenes.Select(x => x.Id).ToList();
        var segments = await uow.Repository<StorySegment>().FindAsync(x => sceneIds.Contains(x.StorySceneId) && !x.IsDeleted, cancellationToken: ct);
        var media = p.ManifestJson == "{}" ? ParentPackageManifest.Empty : JsonSerializer.Deserialize<ParentPackageManifest>(p.ManifestJson, Json)!;
        return new(c.Vocabulary ? words.Select(x => x.Id).ToArray() : [], c.Quiz ? quiz.Select(x => x.Id).ToArray() : [], c.Listening ? listening.Select(x => x.Id).ToArray() : [],
            sceneIds.ToArray(), segments.Select(x => x.Id).ToArray(), media.MediaAssetIds);
    }
}
