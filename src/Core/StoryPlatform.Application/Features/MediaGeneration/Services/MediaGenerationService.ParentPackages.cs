using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    // Service instances are scoped per worker iteration. Only this package coordinator sets this scope.
    private ParentStoryPackage? _parentMediaScope;

    private async Task<MediaJobProcessResult> ProcessParentPackageMediaAsync(StoryGenerationJob observed, CancellationToken ct)
    {
        var token = observed.ConcurrencyToken;
        try
        {
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                await _unitOfWork.AcquireTransactionLockAsync(observed.StoryId, ct);
                var job = await FreshAsync<StoryGenerationJob>(observed.Id, ct) ?? throw new ConflictException("STALE_JOB_RESULT");
                if (job.ConcurrencyToken != observed.ConcurrencyToken) { await RollbackMediaAsync(ct); return MediaJobProcessResult.NoJob; }
                var otherActive = await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(x => x.StoryId == observed.StoryId && x.Id != observed.Id &&
                    x.Operation == GenerationJobOperation.GenerateMediaPackage && x.Status == GenerationJobStatus.Processing && x.LeaseExpiresAt > _clock.GetUtcNow().UtcDateTime, ct);
                if (otherActive) { await RollbackMediaAsync(ct); return MediaJobProcessResult.NoJob; }
                _parentMediaScope = await FreshAsync<ParentStoryPackage>(job.ParentStoryPackageId!.Value, ct) ?? throw new ConflictException("PARENT_PACKAGE_NOT_FOUND");
                if (_supervision is null || _parentMediaScope.Status != "Generating") throw new ConflictException("PACKAGE_IMMUTABLE");
                await ParentPackageContext.ValidateAsync(_unitOfWork, _supervision, _parentMediaScope, ct, true);
                _unitOfWork.Repository<StoryGenerationJob>().Update(job);
                job.Status = GenerationJobStatus.Processing; job.AttemptNo++; job.ConcurrencyToken = token = Guid.NewGuid().ToString("N");
                job.LeaseExpiresAt = _clock.GetUtcNow().UtcDateTime.Add(_jobLease); job.StartedAt = _clock.GetUtcNow().UtcDateTime;
                _unitOfWork.Repository<StoryGenerationJob>().Update(job); await _unitOfWork.SaveChangesAsync(ct); await _unitOfWork.CommitTransactionAsync(ct);
            }
            catch { await RollbackMediaAsync(CancellationToken.None); throw; }
            var p = _parentMediaScope!;
            var (story, version, _) = await ParentPackageContext.ValidateAsync(_unitOfWork, _supervision!, p, ct, true);
            var c = ParentStoryPackageService.Components(p);
            var work = JsonSerializer.Deserialize<ParentPackageWorkState>(observed.GenerationMetadataJson ?? "{}", ParentPackageContext.Json)!;
            MediaContext context;
            IReadOnlyList<StoryScene> scenes;
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                await _unitOfWork.AcquireTransactionLockAsync(p.StoryId, ct);
                await RequireClaimedJobAsync(observed.Id, token, ct);
                scenes = await ParentStoryPackageLayout.EnsureAsync(_unitOfWork, version, _segmentService, ct);
                context = await _unitOfWork.Repository<MediaContext>().FirstOrDefaultAsync(x => x.ParentStoryPackageId == p.Id, cancellationToken: ct)
                    ?? new MediaContext { StoryVersionId = p.StoryVersionId, ParentStoryPackageId = p.Id, Revision = p.Revision,
                        ContextJson = _contextBuilder.Build(new(p.StoryVersionId, version.Title, version.Content!, version.Lesson, null, null, null, null,
                            JsonSerializer.Serialize(new { audience = JsonSerializer.Deserialize<JsonElement>(p.ContextSnapshotJson),
                                ttsConfigurationFingerprint = _ttsProvider.ConfigurationFingerprint }))) };
                var pinnedTts = JsonSerializer.Deserialize<JsonElement>(context.ContextJson).GetProperty("storyFacts").GetProperty("acceptedContext").GetProperty("ttsConfigurationFingerprint").GetString();
                if (pinnedTts != _ttsProvider.ConfigurationFingerprint) throw new ConflictException("MEDIA_CONFIGURATION_CHANGED");
                if (context.Id == 0) { await _unitOfWork.Repository<MediaContext>().AddAsync(context, ct); await _unitOfWork.SaveChangesAsync(ct); }
                var job = await RequireClaimedJobAsync(observed.Id, token, ct); job.MediaContextId = context.Id;
                _unitOfWork.Repository<StoryGenerationJob>().Update(job); await _unitOfWork.SaveChangesAsync(ct); await _unitOfWork.CommitTransactionAsync(ct);
            }
            catch { await RollbackMediaAsync(CancellationToken.None); throw; }
            var state = new MediaState((await FreshAsync<StoryGenerationJob>(observed.Id, ct))!, version, story);
            await BindListeningInteractionsAsync(state, token, context, scenes, ct);
            var rolePlayStarts = c.Listening ? (await LoadListeningInteractionsAsync(version.Id, ct)).Where(x => x.Type == ListeningInteractionType.RolePlay && x.SourceStart.HasValue).Select(x => x.SourceStart!.Value).ToArray() : [];
            var selected = new List<int>();
            foreach (var scene in scenes.OrderBy(x => x.SceneIndex))
            {
                if (c.Images || c.Listening)
                {
                    var beats = await GetOrCreateBeatsAsync(observed.Id, token, version.Id, context, scene, ct);
                    if (c.Images)
                        foreach (var beat in beats)
                        {
                            var spec = _specificationBuilder.Build(version.Id, scene.Id, scene.SceneIndex, scene.SceneText, scene.VisualDescription, context.ContextJson)
                                .ForBeat(scene.SceneText[beat.StartOffset..beat.EndOffset], beat.VisualFocus);
                            var reusable = work.Regenerate.Contains("images") ? null : await ReusableIllustrationAsync(p, beat.Id, spec, ct);
                            if (reusable is null)
                            {
                                await EnsureIllustrationAsync(observed.Id, token, story.Id, version.Id, context, scene, beat, spec, ct);
                                reusable = await GetOrCreateIllustrationAssetAsync(version.Id, scene, beat, ct);
                            }
                            selected.Add(reusable.Id);
                        }
                }
                if (c.Narration || rolePlayStarts.Length > 0)
                {
                    var segments = await _unitOfWork.Repository<StorySegment>().FindAsync(x => x.StorySceneId == scene.Id && !x.IsDeleted, cancellationToken: ct);
                    foreach (var segment in segments.OrderBy(x => x.SegmentOrder))
                    {
                        if (!c.Narration && !rolePlayStarts.Any(start => scene.TextRangeStart + segment.StartOffset <= start && start < scene.TextRangeStart + segment.EndOffset)) continue;
                        var audioCandidates = await _unitOfWork.Repository<MediaAsset>().FindAsync(x =>
                            x.StoryVersionId == version.Id && x.StorySegmentId == segment.Id && x.Type == MediaType.TtsAudio && !x.IsDeleted &&
                            x.Status == MediaStatus.Ready && x.ValidationStatus == ValidationStatus.Passed, cancellationToken: ct);
                        var reusable = work.Regenerate.Contains("narration") ? null : audioCandidates.FirstOrDefault(x =>
                            x.ParentStoryPackageId == p.Id || CompatibleTts(x));
                        if (reusable is null)
                        {
                            await EnsureSegmentAudioAsync(observed.Id, token, story.Id, version.Id, context, scene, segment, ct);
                            reusable = await GetOrCreateSegmentAudioAssetAsync(version.Id, segment, ct);
                        }
                        selected.Add(reusable.Id);
                    }
                }
            }
            if (c.Vocabulary && c.Narration)
            {
                var words = await _unitOfWork.Repository<StoryVocabulary>().FindAsync(x => x.ParentStoryPackageId == p.Id && !x.IsDeleted, cancellationToken: ct);
                foreach (var word in words)
                {
                    await EnsureVocabularyAudioAsync(observed.Id, token, story.Id, version.Id, context, word, ct, required: true);
                    selected.Add((await GetOrCreateVocabularyAudioAssetAsync(version.Id, word, ct)).Id);
                }
            }
            if (c.Listening)
            {
                await EnsureListeningAudioAsync(observed.Id, token, story.Id, version.Id, context, ct);
                var interactions = await LoadListeningInteractionsAsync(version.Id, ct);
                var options = await LoadListeningOptionsAsync(interactions, ct);
                foreach (var interaction in interactions.Where(x => x.Type == ListeningInteractionType.PicturePrediction))
                {
                    var scene = scenes.Single(x => x.Id == interaction.StorySceneId);
                    var spec = _specificationBuilder.Build(version.Id, scene.Id, scene.SceneIndex, scene.SceneText, scene.VisualDescription, context.ContextJson);
                    foreach (var option in options.Where(x => x.ListeningInteractionId == interaction.Id))
                    {
                        var asset = await GetOrCreateClaimedListeningAssetAsync(observed.Id, token, version.Id, context.Id,
                            () => GetOrCreateListeningImageAssetAsync(version.Id, interaction.Id, option.Id, ct), ct);
                        await EnsureListeningImageAsync(observed.Id, token, story.Id, context, asset, spec, ct);
                    }
                }
                var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(x => x.StoryVersionId == version.Id && x.ParentStoryPackageId == p.Id && !x.IsDeleted &&
                    (x.Type == MediaType.ListeningAudio || x.Type == MediaType.ListeningImage), cancellationToken: ct);
                foreach (var interaction in interactions)
                {
                    foreach (var part in ListeningAudioParts.For(interaction, options))
                        selected.Add((ListeningMediaRequirements.FindAudio(assets, interaction.Id, part.OptionId, part.PartKey)
                            ?? throw new ConflictException("MEDIA_PACKAGE_INCOMPLETE")).Id);
                    if (interaction.Type == ListeningInteractionType.PicturePrediction)
                        foreach (var option in options.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted))
                            selected.Add((ListeningMediaRequirements.FindImage(assets, option.Id)
                                ?? throw new ConflictException("MEDIA_PACKAGE_INCOMPLETE")).Id);
                }
            }
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                await _unitOfWork.AcquireTransactionLockAsync(story.Id, ct); await AssertFreshAsync(observed.Id, token, version.Id, context.Id, ct);
                var package = (await FreshAsync<ParentStoryPackage>(p.Id, ct))!;
                foreach (var id in selected)
                {
                    var asset = await FreshAsync<MediaAsset>(id, ct);
                    if (asset?.Status != MediaStatus.Ready || asset.ValidationStatus != ValidationStatus.Passed || string.IsNullOrWhiteSpace(asset.Url))
                        throw new ConflictException("MEDIA_PACKAGE_INCOMPLETE");
                }
                package.ManifestJson = JsonSerializer.Serialize(ParentPackageManifest.Empty with { MediaAssetIds = selected.Distinct().ToArray() }, ParentPackageContext.Json);
                package.Status = "Review"; package.LastErrorCode = null;
                ParentStoryPackageService.Rotate(_unitOfWork, package); _unitOfWork.Repository<ParentStoryPackage>().Update(package);
                var job = await RequireClaimedJobAsync(observed.Id, token, ct); job.Status = GenerationJobStatus.Completed; job.CompletedAt = _clock.GetUtcNow().UtcDateTime;
                job.LeaseExpiresAt = null; job.ErrorCode = null; job.NextAttemptAt = null; _unitOfWork.Repository<StoryGenerationJob>().Update(job);
                await _unitOfWork.SaveChangesAsync(ct); await _unitOfWork.CommitTransactionAsync(ct);
            }
            catch { await RollbackMediaAsync(CancellationToken.None); throw; }
            return MediaJobProcessResult.Completed;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await RollbackMediaAsync(CancellationToken.None);
            await _unitOfWork.BeginTransactionAsync(CancellationToken.None);
            try
            {
                await _unitOfWork.AcquireTransactionLockAsync(observed.StoryId, CancellationToken.None);
                var job = await FreshAsync<StoryGenerationJob>(observed.Id, CancellationToken.None);
                var p = await FreshAsync<ParentStoryPackage>(observed.ParentStoryPackageId!.Value, CancellationToken.None);
                if (job?.ConcurrencyToken == token && p?.Status == "Generating")
                {
                    var retry = (ex is TransientMediaGenerationException || ex is TimeoutException or OperationCanceledException) && job.AttemptNo < job.MaxAttempts;
                    job.Status = retry ? GenerationJobStatus.Pending : GenerationJobStatus.Failed; job.ErrorCode = PublicErrorCode(ex); job.LeaseExpiresAt = null;
                    job.NextAttemptAt = retry ? _clock.GetUtcNow().UtcDateTime.Add((ex as TransientMediaGenerationException)?.RetryAfter ?? TimeSpan.FromSeconds(60)) : null;
                    p.Status = retry ? "Generating" : "Failed"; p.LastErrorCode = job.ErrorCode; ParentStoryPackageService.Rotate(_unitOfWork, p);
                    _unitOfWork.Repository<StoryGenerationJob>().Update(job); _unitOfWork.Repository<ParentStoryPackage>().Update(p);
                }
                await _unitOfWork.SaveChangesAsync(CancellationToken.None); await _unitOfWork.CommitTransactionAsync(CancellationToken.None);
            }
            catch { await RollbackMediaAsync(CancellationToken.None); throw; }
            return MediaJobProcessResult.Failed(false, PublicErrorCode(ex));
        }
        finally { _parentMediaScope = null; }
    }

    private async Task<MediaAsset?> ReusableIllustrationAsync(ParentStoryPackage p, int beatId, SceneSpecification spec, CancellationToken ct)
    {
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(x => x.StoryVersionId == p.StoryVersionId && x.IllustrationBeatId == beatId &&
            x.Type == MediaType.Illustration && x.Status == MediaStatus.Ready && x.ValidationStatus == ValidationStatus.Passed && !x.IsDeleted, cancellationToken: ct);
        foreach (var asset in assets.OrderByDescending(x => x.ParentStoryPackageId == p.Id))
        {
            if (asset.ParentStoryPackageId == p.Id) return asset;
            var binary = await _mediaStorage.ReadAsync(asset.Url!, ct);
            if ((await _alignmentEvaluator.EvaluateAsync(spec, binary, ct)).Passed && (await _safetyEvaluator.EvaluateAsync(spec, binary, ct)).Passed) return asset;
        }
        return null;
    }

    private bool CompatibleTts(MediaAsset asset)
    {
        if (_ttsProvider.ConfigurationFingerprint is not string expected || string.IsNullOrWhiteSpace(asset.ValidationResultJson)) return false;
        try { return JsonSerializer.Deserialize<JsonElement>(asset.ValidationResultJson).TryGetProperty("ttsConfigurationFingerprint", out var key) && key.GetString() == expected; }
        catch (JsonException) { return false; }
    }

    private async Task AssertParentPackageMediaAsync(StoryGenerationJob job, int versionId, int? contextId, CancellationToken ct)
    {
        if (_supervision is null) throw new ForbiddenException();
        var package = await FreshAsync<ParentStoryPackage>(job.ParentStoryPackageId!.Value, ct) ?? throw new ConflictException("PARENT_PACKAGE_NOT_FOUND");
        if (package.Status != "Generating" || package.StoryVersionId != versionId || package.StoryId != job.StoryId) throw new ConflictException("PACKAGE_IMMUTABLE");
        var (story, version, _) = await ParentPackageContext.ValidateAsync(_unitOfWork, _supervision, package, ct, true);
        if (contextId is int id && !await _unitOfWork.Repository<MediaContext>().ExistsAsync(x => x.Id == id && x.ParentStoryPackageId == package.Id, ct))
            throw new ConflictException("STALE_MEDIA_RESULT");
        GrayZoneGate.EnsureCanContinue(story, version);
    }
}
