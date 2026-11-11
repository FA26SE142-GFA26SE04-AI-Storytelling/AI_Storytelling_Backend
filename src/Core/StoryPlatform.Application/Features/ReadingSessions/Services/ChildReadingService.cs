using StoryPlatform.Application.Features.ParentAuthoring;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Application.Features.ChildProfiles.ReadingSlots;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public class ChildReadingService : IChildReadingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildRoadmapEligibility _eligibility;
    private readonly IChildRoadmapProgressRecorder _recorder;
    private readonly IChildMediaUrlSigner? _mediaUrls;
    private readonly IChildRewardService _rewards;
    private readonly IReadingSlotService? _readingSlots;
    private readonly IPlanBenefitService? _planBenefits;
    private readonly IPlanUsageService? _planUsage;

    /// <param name="readingSlots">Hồ sơ đang chỉ xem (vượt số hồ sơ của gói) không mở được phiên đọc; null thì không chặn.</param>
    public ChildReadingService(
        IUnitOfWork unitOfWork,
        IChildRoadmapEligibility eligibility,
        IChildRoadmapProgressRecorder recorder,
        IChildRewardService rewards,
        IChildMediaUrlSigner? mediaUrls = null,
        IReadingSlotService? readingSlots = null,
        IPlanBenefitService? planBenefits = null,
        IPlanUsageService? planUsage = null)
    {
        _readingSlots = readingSlots;
        _planBenefits = planBenefits;
        _planUsage = planUsage;
        _mediaUrls = mediaUrls;
        _unitOfWork = unitOfWork;
        _eligibility = eligibility;
        _recorder = recorder;
        _rewards = rewards;
    }

    public async Task<ReadingSessionStartedDto> StartAsync(
        int childProfileId, string sessionKey, StartReadingRequestDto request, CancellationToken ct = default)
    {
        if ((request.StoryId.HasValue ? 1 : 0) + (request.CommunitySubmissionId.HasValue ? 1 : 0) + (request.FamilyConfirmationId.HasValue ? 1 : 0) != 1)
        {
            throw new BadRequestException("Hãy chọn đúng một truyện để đọc.");
        }

        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, ct);
        if (child == null || child.IsDeleted || child.Status != ChildProfileStatus.Active)
        {
            throw new ForbiddenException(ChildReadingPolicy.ProfileUnavailableMessage);
        }

        // Hồ sơ chỉ xem: trẻ thấy thông điệp chung, không lộ lý do gói (Bước 3.0).
        if (_readingSlots != null
            && !(await _readingSlots.GetReadableProfileIdsAsync(child.OwnerUserId, ct)).Contains(child.Id))
        {
            throw new ForbiddenException(ChildReadingPolicy.ProfileUnavailableMessage);
        }

        var childSession = await _unitOfWork.Repository<ChildSession>().FirstOrDefaultAsync(
            value => value.SessionKey == sessionKey && value.ChildProfileId == childProfileId, cancellationToken: ct);
        if (childSession == null || childSession.IsDeleted || childSession.IsIdleExpired(DateTime.UtcNow)
            || childSession.ChildAccessCredentialId.HasValue == childSession.SupervisorSessionId.HasValue)
        {
            throw new ForbiddenException(ChildReadingPolicy.ProfileUnavailableMessage);
        }

        var (story, version) = await ResolveTargetAsync(childProfileId, request, ct);
        var versionId = version.Id;

        // Community approval pins a version. Check its content without mutating the tracked source story.
        var eligibilityStory = request.CommunitySubmissionId.HasValue
            ? new Story
            {
                Id = story.Id,
                Scope = story.Scope,
                Status = story.Status,
                IsPublished = story.IsPublished,
                ArchivedAt = story.ArchivedAt,
                AgeBand = story.AgeBand,
                Content = version.Content
            }
            : story;
        if (!request.FamilyConfirmationId.HasValue && await _eligibility.EvaluateAsync(child, eligibilityStory, ct) != null)
        {
            throw new ForbiddenException(ChildReadingPolicy.StoryUnavailableMessage);
        }

        var storyId = story.Id;
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Mức Miễn phí: tối đa N truyện thư viện mới mỗi tuần, tính chung cả tài khoản. Khoá theo tài khoản
            // (trước khoá truyện) để hai trẻ mở cùng lúc không vượt giới hạn.
            if (request.StoryId.HasValue && story.Scope == StoryScope.Creator)
            {
                await EnsureWeeklyLibraryAllowanceAsync(child.OwnerUserId, storyId, ct);
            }

            // Serialize starts for this story before checking sessions and the unique progress row.
            await _unitOfWork.AcquireTransactionLockAsync(storyId, ct);
            if (request.FamilyConfirmationId is int lockedGrant)
                await new ParentStoryPackageReader(_unitOfWork, new StoryPlatform.Application.Features.ChildProfiles.Profiles.Services.ChildProfileAccessGuard(_unitOfWork)).AuthorizeAsync(childProfileId, lockedGrant, ct);
            var sessions = _unitOfWork.Repository<ReadingSession>();
            var open = (await sessions.FindAsync(
                    session => session.ChildProfileId == childProfileId
                               && session.StoryId == storyId
                               && session.StoryVersionId == versionId
                               && session.FamilyConfirmationId == request.FamilyConfirmationId
                               && !session.IsDeleted
                               && session.ForceExitRequestedAt == null
                               && (session.Status == SessionStatus.Started
                                   || session.Status == SessionStatus.Reading
                                   || session.Status == SessionStatus.Activity),
                    cancellationToken: ct))
                .OrderByDescending(session => session.StartedAt).FirstOrDefault();

            var progress = await GetOrCreateProgressAsync(childProfileId, storyId, ct);
            var resumed = open != null;
            if (open == null)
            {
                open = await sessions.AddAsync(new ReadingSession
                {
                    ChildProfileId = childProfileId,
                    StoryId = storyId,
                    StoryVersionId = versionId,
                    FamilyConfirmationId = request.FamilyConfirmationId,
                    ParentStoryPackageId = request.FamilyConfirmationId is int grantId ? (await _unitOfWork.Repository<ParentStoryOperation>().GetByIdAsync(grantId, ct))!.ParentStoryPackageId : null,
                    Status = SessionStatus.Started,
                    StartedAt = DateTime.UtcNow,
                    ChildAccessCredentialId = childSession.ChildAccessCredentialId,
                    SupervisorSessionId = childSession.SupervisorSessionId
                }, ct);
                await _unitOfWork.SaveChangesAsync(ct);
                await AddTelemetryAsync(open.Id, TelemetryEventType.SessionStarted, null, ct);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
            return new ReadingSessionStartedDto
            {
                SessionId = open.Id,
                StoryId = storyId,
                Resumed = resumed,
                LastPage = progress.LastPageRead,
                LastReadingMode = progress.LastReadingMode,
                IsBookmarked = progress.IsBookmarked,
                IsFavorited = progress.IsFavorited
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    public const string WeeklyLibraryLimitMessageFormat =
        "Đã mở đủ {0} truyện thư viện mới trong tuần. Truyện mới mở lại vào thứ Hai.";

    private async Task EnsureWeeklyLibraryAllowanceAsync(int ownerUserId, int storyId, CancellationToken ct)
    {
        if (_planBenefits == null || _planUsage == null)
        {
            return;
        }

        var limit = (await _planBenefits.GetForUserAsync(ownerUserId, ct)).WeeklyNewLibraryStories;
        if (limit == null)
        {
            return;
        }

        await _unitOfWork.AcquireTransactionLockAsync(-(1_000_000_000 + ownerUserId), ct);
        if (await _planUsage.HasOpenedLibraryStoryAsync(ownerUserId, storyId, ct))
        {
            return;
        }

        if (await _planUsage.CountNewLibraryStoriesThisWeekAsync(ownerUserId, DateTime.UtcNow, ct) >= limit)
        {
            throw new ForbiddenException(string.Format(WeeklyLibraryLimitMessageFormat, limit));
        }
    }

    private async Task<(Story Story, StoryVersion Version)> ResolveTargetAsync(
        int childProfileId, StartReadingRequestDto request, CancellationToken ct)
    {
        Story? story;
        StoryVersion? version;
        if (request.FamilyConfirmationId is int familyId)
        {
            var family = await new ParentStoryPackageReader(_unitOfWork, new StoryPlatform.Application.Features.ChildProfiles.Profiles.Services.ChildProfileAccessGuard(_unitOfWork)).AuthorizeAsync(childProfileId, familyId, ct);
            return (family.Story, family.Version);
        }
        if (request.CommunitySubmissionId.HasValue)
        {
            var submissionId = request.CommunitySubmissionId.Value;
            var item = await _unitOfWork.Repository<ModerationItem>().FirstOrDefaultAsync(
                value => value.Id == submissionId
                         && value.ItemType == ModerationItemType.CommunitySubmission
                         && value.Status == ModerationItemStatus.Approved
                         && !value.IsDeleted
                         && value.StoryId.HasValue
                         && value.StoryVersionId.HasValue,
                cancellationToken: ct);
            if (item == null)
            {
                throw new ForbiddenException(ChildReadingPolicy.StoryUnavailableMessage);
            }

            story = await _unitOfWork.Repository<Story>().GetByIdAsync(item.StoryId!.Value, ct);
            version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(item.StoryVersionId!.Value, ct);
        }
        else
        {
            var found = await ChildStoryLookup.FindAsync(_unitOfWork, childProfileId, request.StoryId!.Value, ct);
            story = found?.Story;
            version = found?.Version;
        }

        if (story == null || story.IsDeleted || version == null || version.IsDeleted || version.StoryId != story.Id)
        {
            throw new ForbiddenException(ChildReadingPolicy.StoryUnavailableMessage);
        }

        return (story, version);
    }

    private async Task<ReadingProgress> GetOrCreateProgressAsync(int childProfileId, int storyId, CancellationToken ct)
    {
        var repository = _unitOfWork.Repository<ReadingProgress>();
        var progress = await repository.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId && value.StoryId == storyId, cancellationToken: ct);
        if (progress != null)
        {
            return progress;
        }

        return await repository.AddAsync(
            new ReadingProgress { ChildProfileId = childProfileId, StoryId = storyId, LastPageRead = 1 }, ct);
    }

    private Task AddTelemetryAsync(int sessionId, TelemetryEventType type, object? payload, CancellationToken ct) =>
        _unitOfWork.Repository<TelemetryLog>().AddAsync(new TelemetryLog
        {
            ReadingSessionId = sessionId,
            EventType = type,
            EventPayload = payload == null ? null : JsonSerializer.Serialize(payload),
            OccurredAt = DateTime.UtcNow
        }, ct);

    private async Task<ReadingSession> RequireSessionAsync(int childProfileId, int sessionId, CancellationToken ct)
    {
        // Read without tracking so a second read after the workflow lock sees the latest saved state.
        var session = await _unitOfWork.Repository<ReadingSession>().FirstOrDefaultAsync(
            value => value.Id == sessionId, cancellationToken: ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
        {
            throw new NotFoundException("Phiên đọc", sessionId);
        }

        await ParentStoryPackageReader.ForSessionAsync(_unitOfWork, session, ct);
        return session;
    }

    /// <summary>
    /// Tranh của một scene theo thứ tự beat: mỗi beat lấy tranh đang dùng (IsCurrent) mới nhất.
    /// assets đã xếp mới nhất trước nên OrderByDescending(IsCurrent) (sắp xếp ổn định) vẫn giữ thứ tự mới nhất.
    /// </summary>
    private static List<ReadingIllustrationDto> SceneIllustrations(
        int sceneId, IEnumerable<IllustrationBeat> beats, IReadOnlyList<MediaAsset> assets) =>
        beats.Where(beat => beat.StorySceneId == sceneId)
            .OrderBy(beat => beat.BeatOrder)
            .Select(beat => new
            {
                Beat = beat,
                Asset = assets
                    .Where(asset => asset.Type == MediaType.Illustration && asset.IllustrationBeatId == beat.Id && asset.Url != null)
                    .OrderByDescending(asset => asset.IsCurrent)
                    .FirstOrDefault()
            })
            .Where(pair => pair.Asset is not null)
            .Select(pair => new ReadingIllustrationDto
            {
                BeatOrder = pair.Beat.BeatOrder,
                StartOffset = pair.Beat.StartOffset,
                EndOffset = pair.Beat.EndOffset,
                Url = pair.Asset!.Url
            }).ToList();

    public async Task<ReadingContentDto> GetContentAsync(int childProfileId, int sessionId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(childProfileId, sessionId, ct);
        if (session.Status == SessionStatus.ForceExited || session.ForceExitRequestedAt != null)
        {
            throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
        }

        var version = await RequireSessionVersionAsync(session, ct);
        var versionId = version.Id;
        var parentPackage = await ParentStoryPackageReader.ForSessionAsync(_unitOfWork, session, ct);
        var manifest = parentPackage is null ? null : JsonSerializer.Deserialize<ParentPackageManifest>(parentPackage.ManifestJson, ParentPackageContext.Json)!;
        var allowedAssets = manifest?.MediaAssetIds.ToList() ?? [];
        var allowedWords = manifest?.VocabularyIds.ToList() ?? [];
        var allowedListening = manifest?.ListeningIds.ToList() ?? [];

        var scenes = (await _unitOfWork.Repository<StoryScene>().FindAsync(
                scene => scene.StoryVersionId == versionId && !scene.IsDeleted, cancellationToken: ct))
            .OrderBy(scene => scene.SceneIndex).ToList();
        var sceneIds = scenes.Select(scene => scene.Id).ToList();
        var segments = await _unitOfWork.Repository<StorySegment>().FindAsync(
            segment => sceneIds.Contains(segment.StorySceneId) && !segment.IsDeleted, cancellationToken: ct);
        var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(
            beat => sceneIds.Contains(beat.StorySceneId) && !beat.IsDeleted, cancellationToken: ct);
        var assets = (await _unitOfWork.Repository<MediaAsset>().FindAsync(
            asset => asset.StoryVersionId == versionId && (parentPackage == null ? asset.ParentStoryPackageId == null : allowedAssets.Contains(asset.Id)) && asset.Status == MediaStatus.Ready && !asset.IsDeleted,
            cancellationToken: ct))
            .OrderByDescending(asset => asset.CompletedAt ?? asset.CreatedAt).ThenByDescending(asset => asset.Id).ToList();
        var vocabulary = await _unitOfWork.Repository<StoryVocabulary>().FindAsync(
            word => word.StoryVersionId == versionId && word.ParentStoryPackageId == session.ParentStoryPackageId && (parentPackage == null || allowedWords.Contains(word.Id)) && !word.IsDeleted, cancellationToken: ct);
        if (parentPackage is not null) assets = assets.Select(x => ParentStoryPackageReader.ProxyAsset(x, session.FamilyConfirmationId!.Value)).ToList();
        var vocabularyAudio = parentPackage is not null ? assets.Where(x => x.Type == MediaType.VocabularyAudio && x.StoryVocabularyId.HasValue).ToDictionary(x => x.StoryVocabularyId!.Value, x => x.Url!) : await VocabularyAudioLookup.FindUrlsAsync(
            _unitOfWork, vocabulary.Select(word => word.Id).ToList(), ct);

        var interactions = await _unitOfWork.Repository<ListeningInteraction>().FindAsync(
            interaction => interaction.StoryVersionId == versionId && interaction.ParentStoryPackageId == session.ParentStoryPackageId && (parentPackage == null || allowedListening.Contains(interaction.Id)) && !interaction.IsDeleted, cancellationToken: ct);
        var interactionIds = interactions.Select(interaction => interaction.Id).ToList();
        IReadOnlyList<ListeningInteractionOption> options = interactionIds.Count == 0 ? [] :
            await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
                option => interactionIds.Contains(option.ListeningInteractionId) && !option.IsDeleted, cancellationToken: ct);
        IReadOnlyList<ListeningInteractionAttempt> attempts = interactionIds.Count == 0 ? [] :
            await _unitOfWork.Repository<ListeningInteractionAttempt>().FindAsync(
                attempt => attempt.ReadingSessionId == sessionId && !attempt.IsDeleted, cancellationToken: ct);
        var listening = ChildListeningContent.Build(sessionId, scenes, segments, interactions, options, assets, attempts);
        var progress = await _unitOfWork.Repository<ReadingProgress>().FirstOrDefaultAsync(
            row => row.ChildProfileId == childProfileId && row.StoryId == session.StoryId, cancellationToken: ct);

        var content = new ReadingContentDto
        {
            SessionId = session.Id,
            StoryId = session.StoryId,
            Title = version.Title,
            Lesson = version.Lesson,
            ListenOnly = progress?.ListenOnly ?? false,
            Scenes = scenes.Select(scene => new ReadingSceneDto
            {
                SceneId = scene.Id,
                SceneIndex = scene.SceneIndex,
                Text = scene.SceneText,
                IllustrationUrl = assets.FirstOrDefault(asset =>
                    asset.Type == MediaType.Illustration && asset.StorySceneId == scene.Id)?.Url,
                Illustrations = SceneIllustrations(scene.Id, beats, assets),
                Segments = segments.Where(segment => segment.StorySceneId == scene.Id)
                    .OrderBy(segment => segment.SegmentOrder)
                    .Select(segment =>
                    {
                        var audio = assets.FirstOrDefault(asset =>
                            asset.Type == MediaType.TtsAudio && asset.StorySegmentId == segment.Id);
                        return new ReadingSegmentDto
                        {
                            SegmentId = segment.Id,
                            SegmentOrder = segment.SegmentOrder,
                            StartOffset = segment.StartOffset,
                            EndOffset = segment.EndOffset,
                            Text = segment.TextContent,
                            AudioUrl = audio?.Url,
                            WordTimingsJson = audio?.WordTimings
                        };
                    }).ToList(),
                Interaction = listening.GetValueOrDefault(scene.Id)?.Interaction,
                AttemptedInSession = listening.GetValueOrDefault(scene.Id)?.AttemptedInSession ?? false
            }).ToList(),
            Vocabulary = vocabulary.OrderBy(word => word.Id)
                .Select(word => new ReadingVocabularyDto
                {
                    Id = word.Id, Term = word.Term, AudioUrl = vocabularyAudio.GetValueOrDefault(word.Id)
                }).ToList()
        };
        // Media lưu bằng đường dẫn trong kho; ký link như gói media xem trước của phụ huynh để bé mở được tranh và giọng đọc.
        await _mediaUrls.SignAsync(content, ct);
        return content;
    }

    public async Task<ReadingProgressDto> SaveProgressAsync(
        int childProfileId, int sessionId, SaveProgressRequestDto request, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(childProfileId, sessionId, ct);
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Share StartAsync's lock to avoid duplicate progress rows and lost time updates.
            await _unitOfWork.AcquireTransactionLockAsync(session.StoryId, ct);
            session = await RequireSessionAsync(childProfileId, sessionId, ct);
            if (session.Status == SessionStatus.ForceExited || session.ForceExitRequestedAt != null)
            {
                throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
            }

            if (!ChildReadingPolicy.IsActive(session.Status))
            {
                throw new ConflictException("Phiên đọc này đã kết thúc.");
            }

            var version = await RequireSessionVersionAsync(session, ct);
            var versionId = version.Id;
            var sceneIndex = (long)request.Page - 1;
            if (request.Page < 1 || !await _unitOfWork.Repository<StoryScene>().ExistsAsync(
                    scene => scene.StoryVersionId == versionId && scene.SceneIndex == sceneIndex && !scene.IsDeleted, ct))
            {
                throw new BadRequestException("Trang này không có trong truyện.");
            }

            string? mode = null;
            if (!string.IsNullOrWhiteSpace(request.ReadingMode))
            {
                var requestedMode = request.ReadingMode.Trim();
                if (!Enum.TryParse<ReadingMode>(requestedMode, ignoreCase: true, out var parsed)
                    || !Enum.IsDefined(parsed)
                    || !string.Equals(requestedMode, parsed.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new BadRequestException("Chế độ đọc không hợp lệ.");
                }

                mode = parsed.ToString();
            }

            var storyId = session.StoryId;
            var repository = _unitOfWork.Repository<ReadingProgress>();
            var progress = await repository.FirstOrDefaultAsync(
                value => value.ChildProfileId == childProfileId && value.StoryId == storyId, cancellationToken: ct);
            var isNew = progress == null;
            progress ??= new ReadingProgress { ChildProfileId = childProfileId, StoryId = storyId };
            progress.LastPageRead = request.Page;
            if (mode != null)
            {
                progress.LastReadingMode = mode;
            }

            progress.UpdatedAt = DateTime.UtcNow;
            if (isNew)
            {
                // Keep new rows Added; Update would mark the generated-key entity as Modified in EF.
                await repository.AddAsync(progress, ct);
            }
            else
            {
                repository.Update(progress);
            }

            session.Status = SessionStatus.Reading;
            session.PagesCompleted = Math.Max(session.PagesCompleted, request.Page - 1);
            var addedSeconds = Math.Clamp(request.AddedSeconds, 0, ChildReadingPolicy.MaxAddedSecondsPerSave);
            session.TimeSpentSeconds = (int)Math.Min((long)session.TimeSpentSeconds + addedSeconds, int.MaxValue);
            session.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Repository<ReadingSession>().Update(session);

            await AddTelemetryAsync(session.Id, TelemetryEventType.PageViewed, new { page = request.Page }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);

            return new ReadingProgressDto
            {
                SessionId = session.Id,
                Page = progress.LastPageRead,
                ReadingMode = progress.LastReadingMode,
                TimeSpentSeconds = session.TimeSpentSeconds
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ReadingCompletedDto> CompleteAsync(int childProfileId, int sessionId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(childProfileId, sessionId, ct);
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Use the same lock as start/save, then reread before deciding whether completion is new.
            await _unitOfWork.AcquireTransactionLockAsync(session.StoryId, ct);
            session = await RequireSessionAsync(childProfileId, sessionId, ct);
            if (session.ForceExitRequestedAt != null || session.Status == SessionStatus.ForceExited)
            {
                throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
            }

            if (session.Status == SessionStatus.Completed)
            {
                var completed = await GetCompletedResultAsync(session, ct);
                await _unitOfWork.CommitTransactionAsync(ct);
                return completed;
            }

            if (!ChildReadingPolicy.IsActive(session.Status))
            {
                throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
            }

            var storyId = session.StoryId;
            var thisSessionId = session.Id;
            var isRetake = await _unitOfWork.Repository<ReadingSession>().ExistsAsync(
                other => other.ChildProfileId == childProfileId
                         && other.StoryId == storyId
                         && other.Id != thisSessionId
                         && other.Status == SessionStatus.Completed
                         && !other.IsDeleted,
                ct);

            session.Status = SessionStatus.Completed;
            session.CompletedAt = DateTime.UtcNow;
            session.UpdatedAt = session.CompletedAt;
            _unitOfWork.Repository<ReadingSession>().Update(session);
            await AddTelemetryAsync(session.Id, TelemetryEventType.SessionCompleted, new { isRetake }, ct);
            // Queue rewards in the same transaction before the roadmap recorder can commit it.
            var reward = await _rewards.OnStoryCompletedAsync(session, isRetake, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            // The recorder reuses this UOW transaction and may commit it. Queue all completion writes before it.
            var roadmap = await _recorder.RecordSessionCompletedAsync(session.Id, isRetake, ct);
            await _unitOfWork.CommitTransactionAsync(ct);
            return new ReadingCompletedDto
            {
                SessionId = session.Id,
                StoryId = storyId,
                IsRetake = isRetake,
                RoadmapStepCompleted = roadmap != null,
                ExpGained = reward.ExpGained,
                NewBadges = reward.NewBadgeCodes
                    .Concat(roadmap?.NewBadgeCodes ?? Array.Empty<string>())
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ReadingCompletedDto> GetCompletedResultAsync(ReadingSession session, CancellationToken ct)
    {
        var sessionId = session.Id;
        var completion = await _unitOfWork.Repository<TelemetryLog>().FirstOrDefaultAsync(
            log => log.ReadingSessionId == sessionId && log.EventType == TelemetryEventType.SessionCompleted
                   && !log.IsDeleted, cancellationToken: ct);
        var isRetake = false;
        if (completion?.EventPayload != null)
        {
            try
            {
                // Use the original decision: later completions must not turn an earlier first read into a retake.
                using var payload = JsonDocument.Parse(completion.EventPayload);
                isRetake = payload.RootElement.ValueKind == JsonValueKind.Object
                    && payload.RootElement.TryGetProperty("isRetake", out var value)
                    && value.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                // Legacy sessions may lack a usable receipt; never repeat their completion side effects.
            }
        }

        return new ReadingCompletedDto
        {
            SessionId = session.Id,
            StoryId = session.StoryId,
            IsRetake = isRetake,
            RoadmapStepCompleted = session.ChildRoadmapItemId.HasValue
        };
    }

    private enum ReadingFlag { Bookmark, Favorite, ListenOnly }

    public Task<ReadingFlagsDto> SetBookmarkAsync(int childProfileId, int sessionId, bool value, CancellationToken ct = default) =>
        SetFlagAsync(childProfileId, sessionId, value, ReadingFlag.Bookmark, ct);

    public Task<ReadingFlagsDto> SetFavoriteAsync(int childProfileId, int sessionId, bool value, CancellationToken ct = default) =>
        SetFlagAsync(childProfileId, sessionId, value, ReadingFlag.Favorite, ct);

    public Task<ReadingFlagsDto> SetListenOnlyAsync(int childProfileId, int sessionId, bool value, CancellationToken ct = default) =>
        SetFlagAsync(childProfileId, sessionId, value, ReadingFlag.ListenOnly, ct);

    private async Task<ReadingFlagsDto> SetFlagAsync(
        int childProfileId, int sessionId, bool value, ReadingFlag flag, CancellationToken ct)
    {
        var session = await RequireSessionAsync(childProfileId, sessionId, ct);
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(session.StoryId, ct);
            session = await RequireSessionAsync(childProfileId, sessionId, ct);
            if (session.ForceExitRequestedAt != null || session.Status == SessionStatus.ForceExited)
            {
                throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
            }

            var storyId = session.StoryId;
            var repository = _unitOfWork.Repository<ReadingProgress>();
            var progress = await repository.FirstOrDefaultAsync(
                row => row.ChildProfileId == childProfileId && row.StoryId == storyId, cancellationToken: ct);
            var isNew = progress == null;
            progress ??= new ReadingProgress { ChildProfileId = childProfileId, StoryId = storyId };
            switch (flag)
            {
                case ReadingFlag.Bookmark: progress.IsBookmarked = value; break;
                case ReadingFlag.Favorite: progress.IsFavorited = value; break;
                case ReadingFlag.ListenOnly: progress.ListenOnly = value; break;
            }

            progress.UpdatedAt = DateTime.UtcNow;
            if (isNew)
            {
                await repository.AddAsync(progress, ct);
            }
            else
            {
                repository.Update(progress);
            }

            if (flag != ReadingFlag.ListenOnly)
                await AddTelemetryAsync(session.Id,
                    flag == ReadingFlag.Bookmark ? TelemetryEventType.StoryBookmarked : TelemetryEventType.StoryFavorited,
                    new { value }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
            return new ReadingFlagsDto { IsBookmarked = progress.IsBookmarked, IsFavorited = progress.IsFavorited,
                ListenOnly = progress.ListenOnly };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ReadingFeelingDto> SetFeelingAsync(
        int childProfileId, int sessionId, SetFeelingRequestDto request, CancellationToken ct = default)
    {
        if (!StoryFeelingLabels.TryParse(request.Feeling, out var feeling))
        {
            throw new BadRequestException(ChildReadingPolicy.FeelingChoiceMessage);
        }

        var session = await RequireSessionAsync(childProfileId, sessionId, ct);
        if (session.ForceExitRequestedAt != null || session.Status == SessionStatus.ForceExited)
        {
            throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
        }

        if (session.Status != SessionStatus.Completed)
        {
            throw new ConflictException(ChildReadingPolicy.FeelingAfterReadingMessage);
        }

        session.Feeling = feeling;
        session.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<ReadingSession>().Update(session);
        await _unitOfWork.SaveChangesAsync(ct);

        return new ReadingFeelingDto
        {
            SessionId = session.Id,
            Feeling = feeling.ToString(),
            Label = StoryFeelingLabels.Label(feeling)
        };
    }

    public async Task<ForceExitSignalDto> GetSignalAsync(int childProfileId, int sessionId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(childProfileId, sessionId, ct);
        var shouldExit = session.ForceExitRequestedAt != null || session.Status == SessionStatus.ForceExited;
        if (shouldExit && ChildReadingPolicy.IsActive(session.Status))
        {
            session.Status = SessionStatus.ForceExited;
            session.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Repository<ReadingSession>().Update(session);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return new ForceExitSignalDto
        {
            ShouldExit = shouldExit,
            ReturnTo = shouldExit ? ReadingSessionInterruptPolicy.ReturnToJourneyMap : null,
            Message = shouldExit ? ReadingSessionInterruptPolicy.ChildMessage : null,
            SessionStatus = session.Status.ToString()
        };
    }

    public async Task LogEventAsync(
        int childProfileId, int sessionId, ReadingEventRequestDto request, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(childProfileId, sessionId, ct);
        if (!Enum.TryParse<TelemetryEventType>(request.EventType, ignoreCase: true, out var type)
            || !ChildReadingPolicy.ClientEvents.Contains(type))
        {
            throw new BadRequestException("Sự kiện này không được gửi từ ứng dụng của trẻ.");
        }

        if ((request.Payload?.Length ?? 0) > ChildReadingPolicy.MaxEventPayloadLength)
        {
            throw new BadRequestException("Nội dung sự kiện quá dài.");
        }

        await _unitOfWork.Repository<TelemetryLog>().AddAsync(new TelemetryLog
        {
            ReadingSessionId = session.Id,
            EventType = type,
            EventPayload = request.Payload,
            OccurredAt = DateTime.UtcNow
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<StoryVersion> RequireSessionVersionAsync(ReadingSession session, CancellationToken ct)
    {
        var versionId = session.StoryVersionId ?? throw new ForbiddenException(ChildReadingPolicy.StoryUnavailableMessage);
        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(versionId, ct);
        if (version == null || version.IsDeleted || version.StoryId != session.StoryId)
        {
            throw new ForbiddenException(ChildReadingPolicy.StoryUnavailableMessage);
        }

        return version;
    }
}
