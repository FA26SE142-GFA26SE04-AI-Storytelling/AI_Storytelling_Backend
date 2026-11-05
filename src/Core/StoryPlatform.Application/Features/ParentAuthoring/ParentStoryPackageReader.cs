using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed class ParentStoryPackageReader(IUnitOfWork uow, IChildProfileAccessGuard guard)
{
    public static MediaAsset ProxyAsset(MediaAsset asset, int grantId) => new()
    {
        Id = asset.Id, StoryVersionId = asset.StoryVersionId, ParentStoryPackageId = asset.ParentStoryPackageId,
        StorySceneId = asset.StorySceneId, StorySegmentId = asset.StorySegmentId, IllustrationBeatId = asset.IllustrationBeatId,
        StoryVocabularyId = asset.StoryVocabularyId, ListeningInteractionId = asset.ListeningInteractionId,
        ListeningInteractionOptionId = asset.ListeningInteractionOptionId, AudioPartKey = asset.AudioPartKey,
        Type = asset.Type, Status = asset.Status, ValidationStatus = asset.ValidationStatus, WordTimings = asset.WordTimings,
        Url = $"/api/v1/child/manual-stories/{grantId}/assets/{asset.Id}", IsCurrent = true
    };
    public async Task<(ParentStoryPackage Package, ParentStoryOperation Grant, Story Story, StoryVersion Version)> AuthorizeAsync(int childId, int grantId, CancellationToken ct)
    {
        var grant = await uow.Repository<ParentStoryOperation>().FirstOrDefaultAsync(x => x.Id == grantId && x.Kind == "FamilyConfirmation" &&
            x.ChildProfileId == childId && x.RevokedAt == null && !x.IsDeleted, cancellationToken: ct) ?? throw new NotFoundException("Family confirmation", grantId);
        if (grant.ParentStoryPackageId is not int id) throw new ConflictException("LEGACY_FAMILY_PACKAGE");
        var p = await uow.Repository<ParentStoryPackage>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken: ct)
            ?? throw new NotFoundException("Family confirmation", grantId);
        if (p.Status != "Confirmed" || p.ChildProfileId != childId || p.CreatedByUserId != grant.ActorUserId ||
            p.StoryId != grant.StoryId || p.StoryVersionId != grant.StoryVersionId || grant.ContextFingerprint != p.ContextFingerprint)
            throw new NotFoundException("Family confirmation", grantId);
        var (story, version, _) = await ParentPackageContext.ValidateAsync(uow, guard, p, ct);
        GrayZoneGate.EnsureCanContinue(story, version);
        await ValidateManifestAsync(uow, p, ct);
        return (p, grant, story, version);
    }
    public async Task<ParentPreparationDto> ReadAsync(int childId, int grantId, CancellationToken ct)
    {
        var (p, grant, _, version) = await AuthorizeAsync(childId, grantId, ct);
        var receipt = JsonSerializer.Deserialize<ParentFamilyConfirmationDto>(grant.ResultJson, ParentPackageContext.Json)!;
        return await PreparationAsync(uow, p, receipt.PreparationId, version, ct);
    }
    public static async Task<ParentPreparationDto> PreparationAsync(IUnitOfWork uow, ParentStoryPackage p, int preparationId, StoryVersion version, CancellationToken ct)
    {
        var manifest = JsonSerializer.Deserialize<ParentPackageManifest>(p.ManifestJson, ParentPackageContext.Json)!;
        var ids = manifest.SceneIds.ToList();
        var scenes = (await uow.Repository<StoryScene>().FindAsync(x => ids.Contains(x.Id) && x.StoryVersionId == p.StoryVersionId, cancellationToken: ct))
            .OrderBy(x => x.SceneIndex).ToArray();
        var parts = scenes.Length > 0 ? scenes.Select((x, i) => new ParentSegmentDto(i + 1, x.TextRangeStart, x.TextRangeEnd, x.SceneText)).ToArray()
            : new[] { new ParentSegmentDto(1, 0, version.Content!.Length, version.Content) };
        return new(preparationId, p.StoryId, p.StoryVersionId, p.ChildProfileId, 0, p.ContextFingerprint, version.Title,
            JsonSerializer.Deserialize<ParentPackageAudience>(p.ContextSnapshotJson, ParentPackageContext.Json)!.Profile.Language,
            ParentStoryPackageService.Components(p) is { Narration: true } or { Listening: true } ? "combined" : "reading", parts, true, []) { ParentStoryPackageId = p.Id };
    }
    public static async Task ValidateManifestAsync(IUnitOfWork uow, ParentStoryPackage p, CancellationToken ct)
    {
        var manifest = JsonSerializer.Deserialize<ParentPackageManifest>(p.ManifestJson, ParentPackageContext.Json)
            ?? throw new ConflictException("PACKAGE_MANIFEST_INVALID");
        if (manifest.VocabularyIds is null || manifest.QuizIds is null || manifest.ListeningIds is null || manifest.MediaAssetIds is null || manifest.SceneIds is null || manifest.SegmentIds is null)
            throw new ConflictException("PACKAGE_MANIFEST_INVALID");
        var words = manifest.VocabularyIds.ToList(); var quiz = manifest.QuizIds.ToList(); var listening = manifest.ListeningIds.ToList();
        if (await uow.Repository<StoryVocabulary>().CountAsync(x => words.Contains(x.Id) && x.ParentStoryPackageId == p.Id && x.StoryVersionId == p.StoryVersionId && !x.IsDeleted, ct) != words.Count ||
            await uow.Repository<QuizItem>().CountAsync(x => quiz.Contains(x.Id) && x.ParentStoryPackageId == p.Id && x.StoryVersionId == p.StoryVersionId && !x.IsDeleted, ct) != quiz.Count ||
            await uow.Repository<ListeningInteraction>().CountAsync(x => listening.Contains(x.Id) && x.ParentStoryPackageId == p.Id && x.StoryVersionId == p.StoryVersionId && !x.IsDeleted, ct) != listening.Count)
            throw new ConflictException("PACKAGE_MANIFEST_INVALID");
        foreach (var id in manifest.MediaAssetIds)
        {
            var asset = await uow.Repository<MediaAsset>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken: ct);
            if (asset is null || asset.StoryVersionId != p.StoryVersionId || asset.Status != MediaStatus.Ready ||
                asset.ValidationStatus != ValidationStatus.Passed || string.IsNullOrWhiteSpace(asset.Url)) throw new ConflictException("PACKAGE_MEDIA_UNAVAILABLE");
            if (asset.ParentStoryPackageId != p.Id && asset.Type is not (MediaType.Illustration or MediaType.TtsAudio)) throw new ConflictException("PACKAGE_MANIFEST_INVALID");
        }
        if (listening.Count > 0)
        {
            var interactions = await uow.Repository<ListeningInteraction>().FindAsync(x => listening.Contains(x.Id) && !x.IsDeleted, cancellationToken: ct);
            var options = await uow.Repository<ListeningInteractionOption>().FindAsync(x => listening.Contains(x.ListeningInteractionId) && !x.IsDeleted, cancellationToken: ct);
            var assetIds = manifest.MediaAssetIds.ToList();
            var assets = (await uow.Repository<MediaAsset>().FindAsync(x => assetIds.Contains(x.Id) && !x.IsDeleted, cancellationToken: ct))
                .Select(x => ProxyAsset(x, 0)).ToArray();
            var sceneIds = manifest.SceneIds.ToList(); var segmentIds = manifest.SegmentIds.ToList();
            var scenes = await uow.Repository<StoryScene>().FindAsync(x => sceneIds.Contains(x.Id) && x.StoryVersionId == p.StoryVersionId && !x.IsDeleted, cancellationToken: ct);
            var segments = await uow.Repository<StorySegment>().FindAsync(x => segmentIds.Contains(x.Id) && !x.IsDeleted, cancellationToken: ct);
            foreach (var interaction in interactions)
                if (StoryPlatform.Application.Features.ListeningInteractions.Services.ListeningMediaRequirements.Missing(interaction, options, assets).Count > 0 ||
                    interaction.Type == ListeningInteractionType.RolePlay &&
                    StoryPlatform.Application.Features.ReadingSessions.ChildListeningContent.ModelAudioUrl(interaction, scenes, segments, assets) is null)
                    throw new ConflictException("PACKAGE_LISTENING_MEDIA_INCOMPLETE");
        }
    }
    public static async Task<ParentStoryPackage?> ForSessionAsync(IUnitOfWork uow, ReadingSession session, CancellationToken ct)
    {
        if (session.ParentStoryPackageId is not int id) return null;
        var reader = new ParentStoryPackageReader(uow, new StoryPlatform.Application.Features.ChildProfiles.Profiles.Services.ChildProfileAccessGuard(uow));
        var result = await reader.AuthorizeAsync(session.ChildProfileId, session.FamilyConfirmationId ?? throw new ForbiddenException(), ct);
        if (result.Package.Id != id || result.Package.StoryVersionId != session.StoryVersionId || result.Package.StoryId != session.StoryId) throw new ForbiddenException();
        return result.Package;
    }
}
