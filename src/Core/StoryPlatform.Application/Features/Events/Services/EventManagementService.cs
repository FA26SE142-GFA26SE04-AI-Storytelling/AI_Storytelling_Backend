using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public partial class EventManagementService : IEventManagementService
{
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogWriter _audit;

    public EventManagementService(IUnitOfWork uow, IAuditLogWriter audit)
    {
        _uow = uow;
        _audit = audit;
    }

    public async Task<IReadOnlyList<EventSummaryDto>> ListEventsAsync(bool includeDrafts, CancellationToken ct = default, int? moderatorId = null)
    {
        var items = includeDrafts
            ? await _uow.Repository<Event>().GetAllAsync(ct)
            : await _uow.Repository<Event>().FindAsync(e => e.Status == EventStatus.Published || e.Status == EventStatus.Closed, cancellationToken: ct);

        if (includeDrafts && moderatorId.HasValue)
        {
            var assignments = await _uow.Repository<EventModeratorAssignment>().FindAsync(
                assignment => assignment.ModeratorId == moderatorId.Value, cancellationToken: ct);
            var eventIds = assignments.Select(assignment => assignment.EventId).ToHashSet();
            items = items.Where(campaign => EventModeratorAccess.OwnerId(campaign) == moderatorId.Value ||
                eventIds.Contains(campaign.Id)).ToList();
        }
        var categoryRepo = _uow.Repository<ContentCategory>();
        var categoryIds = items.Select(e => e.MainCategoryId).Distinct().ToList();
        var categoryNames = categoryRepo != null && categoryIds.Count > 0
            ? (await categoryRepo.FindAsync(c => categoryIds.Contains(c.Id), cancellationToken: ct))
                .ToDictionary(c => c.Id, c => c.DisplayName)
            : new Dictionary<int, string>();

        return items.OrderByDescending(e => e.CreatedAt)
            .Select(e => MapSummary(e, categoryNames.GetValueOrDefault(e.MainCategoryId)))
            .ToList();
    }

    public async Task<EventDetailDto> GetEventDetailAsync(int eventId, CancellationToken ct = default, bool includeUnpublished = false, int? currentUserId = null, UserRole? currentUserRole = null)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        if (!includeUnpublished && ev.Status is not (EventStatus.Published or EventStatus.Closed))
            throw new NotFoundException(nameof(Event), eventId);
        if (currentUserRole == UserRole.Moderator && currentUserId.HasValue &&
            ev.Status is not (EventStatus.Published or EventStatus.Closed))
            await EventModeratorAccess.RequireOperatorAsync(_uow, ev, currentUserId.Value, ct);
        return await BuildDetailAsync(ev, ct, currentUserId, currentUserRole);
    }

    public Task<EventDetailDto> CreateEventAsync(int actorUserId, CreateEventRequestDto request, CancellationToken ct = default)
        => InTransactionAsync(null, async () =>
        {
            ValidateCreateRequest(request);
            await ValidateCategoryAsync(request.MainCategoryId, ct);

            var ev = new Event
            {
                Title = request.Title.Trim(),
                Slug = BuildSlug(request.Title),
                Description = request.Description.Trim(),
                BannerUrl = string.IsNullOrWhiteSpace(request.BannerUrl) ? null : request.BannerUrl.Trim(),
                MainCategoryId = request.MainCategoryId,
                TargetAgeBands = string.IsNullOrWhiteSpace(request.TargetAgeBands) ? "6-8,9-10" : request.TargetAgeBands.Trim(),
                Status = EventStatus.Draft,
                ConfigVersion = Enum.IsDefined(request.ConfigVersion) ? request.ConfigVersion : EventConfigVersion.LegacyRoleBased,
                OpensAt = request.ConfigVersion == EventConfigVersion.ActivityBased ? request.OpensAt : null,
                ClosesAt = request.ConfigVersion == EventConfigVersion.ActivityBased ? request.ClosesAt : null,
                Format = Enum.IsDefined(request.Format) ? request.Format : EventFormat.Standard,
                ScoringMode = Enum.IsDefined(request.ScoringMode) ? request.ScoringMode : EventScoringMode.Rubric,
                MinVotesPerSubmission = request.MinVotesPerSubmission > 0 ? request.MinVotesPerSubmission : 5,
                MaxParentRegistrations = request.MaxParentRegistrations,
                SubmissionCutoffAt = request.SubmissionCutoffAt,
                ReviewCutoffAt = request.ReviewCutoffAt,
                VotingStartAt = request.VotingStartAt,
                VotingEndAt = request.VotingEndAt,
                CreatorVotingScheduleJson = request.CreatorVotingSchedule is { } reverse ? JsonSerializer.Serialize(reverse) : null,
                BudgetVnd = request.BudgetVnd,
                CreatedByUserId = actorUserId,
                AssignedModeratorId = actorUserId
            };

            await _uow.Repository<Event>().AddAsync(ev, ct);
            await _uow.SaveChangesAsync(ct);

            if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
            {
                await SyncActivitiesAndAwardsAsync(ev.Id, request, ct);
            }
            else
            {
                var trackMap = new Dictionary<EventTrackType, EventTrack>();
                foreach (var t in request.Tracks)
                {
                    var track = new EventTrack
                    {
                        EventId = ev.Id,
                        TrackType = t.TrackType,
                        RegistrationStartAt = t.RegistrationStartAt,
                        RegistrationEndAt = t.RegistrationEndAt,
                        ExecutionStartAt = t.ExecutionStartAt,
                        ExecutionEndAt = t.ExecutionEndAt,
                        MaxSubmissionsPerUser = Math.Max(1, t.MaxSubmissionsPerUser),
                        GrantAiCredits = Math.Max(0, t.GrantAiCredits),
                        GrantSubmitCredits = Math.Max(0, t.GrantSubmitCredits),
                        WaiveWeeklyLibraryLimit = t.WaiveWeeklyLibraryLimit,
                        ClaimWindowHours = Math.Max(1, t.ClaimWindowHours),
                        MaxReallocationRounds = Math.Max(0, t.MaxReallocationRounds),
                        AwardAllMissionCompleters = t.AwardAllMissionCompleters
                    };
                    await _uow.Repository<EventTrack>().AddAsync(track, ct);
                    await _uow.SaveChangesAsync(ct);
                    trackMap[t.TrackType] = track;
                }

                await SyncAwardSlotsAsync(ev.Id, trackMap, request.AwardSlots, ct);
            }
            await _audit.LogAsync(actorUserId, "EventCreated", nameof(Event), ev.Id, null, new { ev.Title, ev.BudgetVnd, ev.ConfigVersion }, ct);
            return await BuildDetailAsync(ev, ct);
        }, ct);

    public Task<EventDetailDto> UpdateEventAsync(int actorUserId, int eventId, CreateEventRequestDto request, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            EventModeratorAccess.RequireOwner(ev, actorUserId);
            if (ev.Status is not (EventStatus.Draft or EventStatus.PendingApproval))
                throw new ConflictException("Chỉ được chỉnh sửa sự kiện khi còn ở trạng thái Nháp hoặc Chờ duyệt.");
            if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
            {
                request.ConfigVersion = EventConfigVersion.ActivityBased;
                if (!request.OpensAt.HasValue && !request.ClosesAt.HasValue)
                {
                    request.OpensAt = ev.OpensAt;
                    request.ClosesAt = ev.ClosesAt;
                }
            }
            ValidateCreateRequest(request);

            await ValidateCategoryAsync(request.MainCategoryId, ct);
            ev.Status = EventStatus.Draft;
            ev.ApprovedByAdminId = null;
            ev.Title = request.Title.Trim();
            ev.Description = request.Description.Trim();
            ev.BannerUrl = string.IsNullOrWhiteSpace(request.BannerUrl) ? null : request.BannerUrl.Trim();
            ev.MainCategoryId = request.MainCategoryId;
            ev.TargetAgeBands = string.IsNullOrWhiteSpace(request.TargetAgeBands) ? "6-8,9-10" : request.TargetAgeBands.Trim();
            ev.Format = Enum.IsDefined(request.Format) ? request.Format : ev.Format;
            ev.ScoringMode = Enum.IsDefined(request.ScoringMode) ? request.ScoringMode : ev.ScoringMode;
            ev.MinVotesPerSubmission = request.MinVotesPerSubmission > 0 ? request.MinVotesPerSubmission : ev.MinVotesPerSubmission;
            ev.MaxParentRegistrations = request.MaxParentRegistrations;
            ev.SubmissionCutoffAt = request.SubmissionCutoffAt;
            ev.ReviewCutoffAt = request.ReviewCutoffAt;
            ev.VotingStartAt = request.VotingStartAt;
            ev.VotingEndAt = request.VotingEndAt;
            ev.CreatorVotingScheduleJson = request.CreatorVotingSchedule is { } reverse ? JsonSerializer.Serialize(reverse) : null;
            ev.BudgetVnd = request.BudgetVnd;
            if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
            {
                ev.OpensAt = request.OpensAt;
                ev.ClosesAt = request.ClosesAt;
            }
            ev.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<Event>().Update(ev);
            await _uow.SaveChangesAsync(ct);

            if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
                await SyncActivitiesAndAwardsAsync(ev.Id, request, ct);
            else
                await UpdateTracksAndAwardsAsync(ev.Id, request, ct);
            await _audit.LogAsync(actorUserId, "EventUpdated", nameof(Event), ev.Id, null, new { ev.Title, ev.BudgetVnd }, ct);
            return await BuildDetailAsync(ev, ct);
        }, ct);

    public Task<EventRuleVersionDto> UpsertRuleVersionAsync(int actorUserId, int eventId, UpsertEventRuleRequestDto request, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            EventModeratorAccess.RequireOwner(ev, actorUserId);
            if (ev.Status is not (EventStatus.Draft or EventStatus.PendingApproval))
                throw new ConflictException("Chỉ được sửa thể lệ khi sự kiện còn nháp hoặc chờ duyệt.");
            var allowEmptyTasks = ev.ConfigVersion == EventConfigVersion.ActivityBased &&
                !await _uow.Repository<EventActivity>().ExistsAsync(a => a.EventId == eventId && a.ActivityType == EventActivityType.ReadingMissions, ct);
            ValidateRules(request, allowEmptyTasks);
            ev.Status = EventStatus.Draft;
            ev.ApprovedByAdminId = null;
            _uow.Repository<Event>().Update(ev);

            var existingVersions = await _uow.Repository<EventRuleVersion>().FindAsync(r => r.EventId == eventId, cancellationToken: ct);
            foreach (var old in existingVersions.Where(v => v.IsActive))
            {
                old.IsActive = false;
                _uow.Repository<EventRuleVersion>().Update(old);
            }

            var nextVersionNo = existingVersions.Count == 0 ? 1 : existingVersions.Max(v => v.VersionNo) + 1;
            var payloadJson = JsonSerializer.Serialize(new { request.RulesText, request.CreatorRubricJson, request.CompletionMode, request.RequiredTaskCount, request.RequiredPoints, request.Tasks });
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();

            var rule = new EventRuleVersion
            {
                EventId = eventId,
                VersionNo = nextVersionNo,
                Revision = 1,
                ContentHash = hash,
                RulesText = request.RulesText.Trim(),
                RewardEligibilityText = string.IsNullOrWhiteSpace(request.RewardEligibilityText) ? null : request.RewardEligibilityText.Trim(),
                TieBreakerPolicyText = string.IsNullOrWhiteSpace(request.TieBreakerPolicyText) ? null : request.TieBreakerPolicyText.Trim(),
                InsufficientVotesPolicyText = string.IsNullOrWhiteSpace(request.InsufficientVotesPolicyText) ? null : request.InsufficientVotesPolicyText.Trim(),
                CreatorRubricJson = request.CreatorRubricJson,
                CompletionMode = request.CompletionMode,
                RequiredTaskCount = request.RequiredTaskCount,
                RequiredPoints = request.RequiredPoints,
                IsActive = true,
                ActivatedAt = DateTime.UtcNow
            };
            await _uow.Repository<EventRuleVersion>().AddAsync(rule, ct);
            await _uow.SaveChangesAsync(ct);

            var tasks = new List<EventTaskDefinition>();
            for (var i = 0; i < request.Tasks.Count; i++)
            {
                var t = request.Tasks[i];
                if (t.TargetValue < 1) throw new BadRequestException("TargetValue của mỗi nhiệm vụ phải >= 1 (BR-TK02).");
                var def = new EventTaskDefinition
                {
                    EventRuleVersionId = rule.Id,
                    TaskCode = string.IsNullOrWhiteSpace(t.TaskCode) ? $"T{i + 1:00}" : t.TaskCode.Trim(),
                    TargetTrackType = t.TargetTrackType,
                    Title = t.Title.Trim(),
                    Description = t.Description.Trim(),
                    SortOrder = t.SortOrder > 0 ? t.SortOrder : i + 1,
                    VerifierType = t.VerifierType,
                    TargetValue = t.TargetValue,
                    ConfigJson = t.ConfigJson,
                    IsMandatory = t.IsMandatory,
                    Points = Math.Max(0, t.Points),
                    AllowMultiplePerStory = t.AllowMultiplePerStory
                };
                await _uow.Repository<EventTaskDefinition>().AddAsync(def, ct);
                tasks.Add(def);
            }

            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(actorUserId, "EventRuleVersionActivated", nameof(EventRuleVersion), rule.Id, null, new { ev.Id, rule.VersionNo, TaskCount = tasks.Count }, ct);

            return MapRule(rule, tasks);
        }, ct);

    public Task<EventSummaryDto> SubmitForApprovalAsync(int actorUserId, int eventId, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            EventModeratorAccess.RequireOwner(ev, actorUserId);
            if (ev.Status != EventStatus.Draft)
                throw new ConflictException("Chỉ gửi duyệt được sự kiện ở trạng thái Draft.");

            var activeRule = await _uow.Repository<EventRuleVersion>().FirstOrDefaultAsync(r => r.EventId == eventId && r.IsActive, cancellationToken: ct);
            var tasks = activeRule == null
                ? new List<EventTaskDefinition>()
                : (await _uow.Repository<EventTaskDefinition>().FindAsync(t => t.EventRuleVersionId == activeRule.Id, cancellationToken: ct)).ToList();
            var awards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);

            if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
            {
                var activities = await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);
                await ValidateActivityBasedSubmitForApprovalAsync(ev, activities, activeRule, tasks, awards, ct);
            }
            else
            {
                var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == eventId, cancellationToken: ct);
                if (tracks.Count == 0) throw new BadRequestException("Event phải có ít nhất một nhánh tham gia (EV-02).");
                if (activeRule == null) throw new BadRequestException("Phải thiết lập thể lệ và ít nhất 1 nhiệm vụ trước khi gửi duyệt.");
                if (tasks.Count == 0) throw new BadRequestException("Event phải có ít nhất 1 nhiệm vụ hợp lệ (BR-TK01).");

                foreach (var task in tasks.Where(task => task.VerifierType == TaskVerifierType.VoteEventStories))
                {
                    if (task.TargetTrackType is not { } voter || !EventVoting.AllowsVoter(ev.ScoringMode, voter))
                        throw new BadRequestException("Nhiệm vụ bình chọn phải gán cho nhóm được phép bình chọn.");
                }

                foreach (var track in tracks)
                {
                    var hasAwardsForRole = awards.Any(a => a.TrackId == track.Id) || (track.TrackType == EventTrackType.Parent && track.AwardAllMissionCompleters);
                    if (hasAwardsForRole)
                    {
                        var hasRoleTask = tasks.Any(t => t.TargetTrackType == track.TrackType || (ev.Format == EventFormat.Standard && t.TargetTrackType == null));
                        if (!hasRoleTask)
                            throw new BadRequestException($"Khi bật phần thưởng cho vai trò {track.TrackType}, cần cấu hình ít nhất một nhiệm vụ cho vai trò này trước khi gửi duyệt.");
                    }

                    if (track.TrackType == EventTrackType.Parent && track.AwardAllMissionCompleters)
                    {
                        if (ev.MaxParentRegistrations is not > 0)
                            throw new BadRequestException("Khi bật giải thưởng cấp cho mọi gia đình hoàn thành nhiệm vụ, cần công bố giới hạn đăng ký Parent.");
                        var parentSlotCount = awards.Count(a => a.TrackId == track.Id);
                        if (parentSlotCount < ev.MaxParentRegistrations.Value)
                            throw new BadRequestException($"Số suất giải Parent ({parentSlotCount}) phải đủ theo giới hạn đăng ký Parent đã công bố ({ev.MaxParentRegistrations.Value}).");
                    }
                }

                foreach (var award in awards.Where(a => a.RewardType == EventRewardType.CreatorPackage))
                {
                    var track = tracks.Single(t => t.Id == award.TrackId);
                    var pack = await ResolveCreatorPackageAsync(track.TrackType, award.RewardName, ct);
                    award.RewardName = pack.Name;
                    award.CreditGrantAmount = pack.AiCredits;
                    award.SubmitCreditGrantAmount = pack.SubmitCredits;
                    _uow.Repository<EventAward>().Update(award);
                }
            }

            ev.Status = EventStatus.PendingApproval;
            ev.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<Event>().Update(ev);
            await _uow.SaveChangesAsync(ct);

            await _audit.LogAsync(actorUserId, "EventSubmittedForApproval", nameof(Event), ev.Id, null, new { ev.Status }, ct);
            return MapSummary(ev);
        }, ct);

    public Task<EventSummaryDto> DecideApprovalAsync(int adminUserId, int eventId, EventApprovalDecisionDto request, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            var returningApproved = ev.Status == EventStatus.Approved && !request.Approved;
            if (ev.Status != EventStatus.PendingApproval && !returningApproved)
                throw new ConflictException("Chỉ có thể duyệt sự kiện đang chờ hoặc trả về sự kiện chưa phát hành.");
            if (returningApproved && string.IsNullOrWhiteSpace(request.Note))
                throw new BadRequestException("Cần ghi rõ lý do trả sự kiện về bản nháp.");

            ev.Status = request.Approved ? EventStatus.Approved : EventStatus.Draft;
            ev.ApprovedByAdminId = request.Approved ? adminUserId : null;
            ev.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<Event>().Update(ev);
            await _uow.SaveChangesAsync(ct);

            await _audit.LogAsync(adminUserId, request.Approved ? "EventApproved" : "EventApprovalRejected", nameof(Event), ev.Id, null, new { request.Approved, request.Note }, ct);
            return MapSummary(ev);
        }, ct);

    public Task<EventSummaryDto> PublishEventAsync(int actorUserId, int eventId, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            EventModeratorAccess.RequireOwner(ev, actorUserId);
            if (ev.Status != EventStatus.Approved)
                throw new ConflictException("Không thể phát hành khi Event và giải thưởng chưa được Admin phê duyệt (EV-03).");

            await EnsureParentOpeningReadyAsync(ev.Id, ct);
            ev.Status = EventStatus.Published;
            ev.PublishedAt = DateTime.UtcNow;
            ev.UpdatedAt = ev.PublishedAt;
            _uow.Repository<Event>().Update(ev);
            await _uow.SaveChangesAsync(ct);

            await _audit.LogAsync(actorUserId, "EventPublished", nameof(Event), ev.Id, null, new { ev.PublishedAt }, ct);
            return MapSummary(ev);
        }, ct);

    public async Task<IReadOnlyList<EventTaskStoryOptionDto>> ListTaskFilterStoriesAsync(int eventId, CancellationToken ct = default)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        var reading = ev.ConfigVersion == EventConfigVersion.ActivityBased
            ? await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                x => x.EventId == eventId && x.ActivityType == EventActivityType.ReadingMissions, cancellationToken: ct)
            : null;
        var links = await _uow.Repository<EventStory>().FindAsync(
            x => x.EventId == eventId && x.IsActive && !x.IsDeleted && x.Source != EventStorySource.ParentSubmission, cancellationToken: ct);
        var byStoryId = links.ToDictionary(x => x.StoryId);
        var ids = byStoryId.Keys.ToList();
        var stories = await _uow.Repository<Story>().FindAsync(x => ids.Contains(x.Id) && x.IsPublished && !x.IsDeleted, cancellationToken: ct);

        return stories.OrderBy(x => x.Title).ThenBy(x => x.Id).Select(story =>
        {
            var source = byStoryId[story.Id].Source;
            return new EventTaskStoryOptionDto(story.Id, story.Title ?? $"Truyện #{story.Id}", source,
                EventParentReadiness.IncludesReadableSource(source, reading?.StorySource),
                story.OutputMode == StoryOutputMode.Learning);
        }).ToList();
    }

    public Task<int> SeedFallbackStoriesAsync(int actorUserId, int eventId, SeedEventStoriesRequestDto request, CancellationToken ct = default) =>
        InTransactionAsync(eventId, () => SeedFallbackStoriesLockedAsync(actorUserId, eventId, request, ct), ct);

    private async Task<int> SeedFallbackStoriesLockedAsync(int actorUserId, int eventId, SeedEventStoriesRequestDto request, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        EventModeratorAccess.RequireOwner(ev, actorUserId);
        if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            var readingAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                a => a.EventId == eventId && a.ActivityType == EventActivityType.ReadingMissions, cancellationToken: ct);
            if (readingAct is { StorySource: EventReadingStorySource.ApprovedSubmissionsOnly })
                throw new ConflictException("Cấu hình nguồn truyện hiện chỉ dùng bài dự thi đã duyệt.");
            var votingAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                a => a.EventId == eventId && a.ActivityType == EventActivityType.Voting, cancellationToken: ct);
            if (ev.ResultsFinalizedAt.HasValue || ev.Status is EventStatus.Closed or EventStatus.Archived ||
                (votingAct != null && DateTime.UtcNow >= votingAct.StartAt))
                throw new ConflictException("Danh sách truyện sự kiện đã chốt.");
        }
        else if (ev.Format == EventFormat.SharedCreatorParent)
        {
            var parentTrack = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(
                t => t.EventId == eventId && t.TrackType == EventTrackType.Parent, cancellationToken: ct);
            if (ev.ResultsFinalizedAt.HasValue || ev.Status is EventStatus.Closed or EventStatus.Archived ||
                DateTime.UtcNow >= (ev.VotingStartAt ?? parentTrack?.ExecutionStartAt ?? DateTime.MaxValue))
                throw new ConflictException("Danh sách truyện sự kiện đã chốt.");
        }
        var added = 0;

        foreach (var storyId in request.StoryIds.Distinct())
        {
            var story = await _uow.Repository<Story>().GetByIdAsync(storyId, ct);
            if (story == null || !story.IsPublished) continue;

            var exists = await _uow.Repository<EventStory>().ExistsAsync(es => es.EventId == eventId && es.StoryId == storyId, ct);
            if (exists) continue;

            var version = await _uow.Repository<StoryVersion>().FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: ct)
                ?? await _uow.Repository<StoryVersion>().FirstOrDefaultAsync(v => v.StoryId == storyId, cancellationToken: ct);
            if (version == null) continue;

            await _uow.Repository<EventStory>().AddAsync(new EventStory
            {
                EventId = ev.Id,
                StoryId = story.Id,
                StoryVersionId = version.Id,
                Source = EventStorySource.ModeratorSeed,
                IsActive = true
            }, ct);
            added++;
        }

        if (added > 0)
        {
            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(actorUserId, "EventSeedStoriesAdded", nameof(Event), ev.Id, null, new { added }, ct);
        }

        return added;
    }

    public async Task<EventReportSummaryDto> GetSummaryAsync(int eventId, CancellationToken ct = default)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == eventId, cancellationToken: ct);
        var activities = ev.ConfigVersion == EventConfigVersion.ActivityBased
            ? await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == eventId, cancellationToken: ct)
            : Array.Empty<EventActivity>();
        var creatorTrackId = tracks.FirstOrDefault(t => t.TrackType == EventTrackType.Creator)?.Id;
        var parentTrackId = tracks.FirstOrDefault(t => t.TrackType == EventTrackType.Parent)?.Id;
        var subActivityId = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.Submission)?.Id;
        var readActivityId = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.ReadingMissions)?.Id;

        var participations = await _uow.Repository<EventParticipation>().FindAsync(p => p.EventId == eventId, cancellationToken: ct);
        var submissions = await _uow.Repository<EventSubmission>().FindAsync(s => s.EventId == eventId, cancellationToken: ct);
        var eventStories = await _uow.Repository<EventStory>().FindAsync(s => s.EventId == eventId && s.IsActive, cancellationToken: ct);
        var awards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);

        var fulfilledAwards = awards.Where(a => a.Status == EventAwardStatus.Fulfilled).ToList();
        var activeAwards = awards.Where(a => a.ParticipationId.HasValue && a.Status != EventAwardStatus.Expired && a.Status != EventAwardStatus.UnclaimedBudgetReturned).ToList();

        var eventEndAt = ev.ClosesAt ?? (activities.Count > 0
            ? activities.Max(a => a.EndAt)
            : (tracks.Count > 0 ? tracks.Max(t => t.ExecutionEndAt) : ev.UpdatedAt ?? ev.CreatedAt));
        var participantUserIds = participations.Select(p => p.UserId).Distinct().ToHashSet();
        var retention7 = await CountRetainedUsersAsync(participantUserIds, eventEndAt, 7, ct);
        var retention30 = await CountRetainedUsersAsync(participantUserIds, eventEndAt, 30, ct);

        var familyReadingStories = eventStories.Where(story => story.Source != EventStorySource.ParentSubmission).ToList();
        var (familiesFirstRead, secondStoryReturnRate) = await ComputeTrialReadingMetricsAsync(eventId, familyReadingStories, ct);
        var hasVotingActivity = ev.ConfigVersion == EventConfigVersion.ActivityBased
            ? activities.Any(a => a.ActivityType == EventActivityType.Voting)
            : EventVoting.IsEnabled(ev.ScoringMode);
        var approvedSubmissions = submissions.Where(s => s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId.HasValue &&
            (ev.ConfigVersion == EventConfigVersion.ActivityBased ? hasVotingActivity && s.ParentSubmissionText == null : EventVoting.RanksAuthors(ev.ScoringMode, s.ParentSubmissionText != null ? EventTrackType.Parent : EventTrackType.Creator))).ToList();
        decimal thresholdRate = 0m;
        if (approvedSubmissions.Count > 0)
        {
            var votes = await _uow.Repository<EventVote>().FindAsync(v => v.EventId == eventId && !v.IsDisqualified && !v.IsDeleted, cancellationToken: ct);
            var minVotes = ev.ConfigVersion == EventConfigVersion.ActivityBased
                ? Math.Max(1, activities.FirstOrDefault(a => a.ActivityType == EventActivityType.Voting)?.MinVotesPerSubmission ?? 5)
                : Math.Max(1, ev.MinVotesPerSubmission);
            var eligibleVoters = participations.Where(p => p.Status != EventParticipationStatus.Disqualified).Select(p => p.Id).ToHashSet();
            var meetingCount = approvedSubmissions.Count(s => votes.Count(v => eligibleVoters.Contains(v.ParticipationId) &&
                (v.EventSubmissionId == s.Id || v.StoryId == s.StoryId) && v.StoryVersionId == s.LockedStoryVersionId) >= minVotes);
            thresholdRate = Math.Round((decimal)meetingCount / approvedSubmissions.Count, 4);
        }

        return new EventReportSummaryDto
        {
            EventId = ev.Id,
            Status = ev.Status,
            CreatorRegistrations = creatorTrackId.HasValue
                ? participations.Count(p => p.TrackId == creatorTrackId.Value)
                : (subActivityId.HasValue ? participations.Count(p => p.EventActivityId == subActivityId.Value) : 0),
            CreatorSubmissions = submissions.Count(s => s.ParentSubmissionText == null),
            ParentSubmissions = submissions.Count(s => s.ParentSubmissionText != null),
            PublishedEventStories = eventStories.Count,
            ParentRegistrations = parentTrackId.HasValue
                ? participations.Count(p => p.TrackId == parentTrackId.Value)
                : (readActivityId.HasValue ? participations.Count(p => p.EventActivityId == readActivityId.Value) : 0),
            ParentVerifiedCount = parentTrackId.HasValue
                ? participations.Count(p => p.TrackId == parentTrackId.Value && p.Status == EventParticipationStatus.Verified)
                : (readActivityId.HasValue ? participations.Count(p => p.EventActivityId == readActivityId.Value && p.Status == EventParticipationStatus.Verified) : 0),
            AwardsProposedOrApproved = activeAwards.Count,
            AwardsFulfilled = fulfilledAwards.Count,
            ApprovedBudgetVnd = ev.BudgetVnd,
            FulfilledValueVnd = fulfilledAwards.Sum(a => a.UnitValueVnd),
            Retention7DayActiveUsers = retention7,
            Retention30DayActiveUsers = retention30,
            FamiliesCompletedFirstRead = familiesFirstRead,
            SecondStoryReturnRate = secondStoryReturnRate,
            SubmissionsMeetingVoteThresholdRate = thresholdRate
        };
    }

    public Task<bool> DeleteDraftAsync(int adminUserId, int eventId, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await _uow.Repository<Event>().FirstOrDefaultAsync(e => e.Id == eventId && !e.IsDeleted, cancellationToken: ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            if (ev.Status != EventStatus.Draft ||
                await _uow.Repository<EventParticipation>().ExistsAsync(p => p.EventId == eventId, ct))
                throw new ConflictException("Chỉ được xóa bản nháp sự kiện chưa có người tham gia.");

            ev.IsDeleted = true;
            ev.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<Event>().Update(ev);
            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(adminUserId, "EventDraftDeleted", nameof(Event), eventId, null,
                new { ev.Title, ev.ConfigVersion }, ct);
            return true;
        }, ct);

    public Task<EventSummaryDto> CloseEventAsync(int actorUserId, int eventId, CancellationToken ct = default) =>
        InTransactionAsync(eventId, () => CloseEventLockedAsync(actorUserId, eventId, ct), ct);

    private async Task<EventSummaryDto> CloseEventLockedAsync(int actorUserId, int eventId, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        EventModeratorAccess.RequireOwner(ev, actorUserId);
        var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == eventId, cancellationToken: ct);
        var activities = ev.ConfigVersion == EventConfigVersion.ActivityBased
            ? await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == eventId, cancellationToken: ct)
            : Array.Empty<EventActivity>();
        EventExecution.EnsureEnded(ev, tracks, activities, allowPaused: true);
        var eventAwards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId && !a.IsDeleted, cancellationToken: ct);
        if (eventAwards.Count > 0 && !ev.ResultsFinalizedAt.HasValue && !eventAwards.Any(a => a.Status is
                EventAwardStatus.AwaitingClaim or EventAwardStatus.Claimed or EventAwardStatus.FulfillmentProcessing or
                EventAwardStatus.Fulfilled or EventAwardStatus.Expired or EventAwardStatus.UnclaimedBudgetReturned or
                EventAwardStatus.Failed))
            throw new ConflictException("Cần chốt và duyệt danh sách trao giải trước khi đóng sự kiện.");
        if (await _uow.Repository<EventParticipation>().ExistsAsync(p => p.EventId == eventId &&
                (p.Status == EventParticipationStatus.Registered || p.Status == EventParticipationStatus.MissionCompleted ||
                 p.Status == EventParticipationStatus.PendingVerification || p.Status == EventParticipationStatus.ManualReview), ct))
            throw new ConflictException("Còn hồ sơ tham gia chưa được xác minh hoặc xử lý.");

        var openAppeals = await _uow.Repository<EventAppeal>().ExistsAsync(
            a => a.EventId == eventId && (a.Status == EventAppealStatus.Open || a.Status == EventAppealStatus.UnderReview || a.Status == EventAppealStatus.Escalated), ct);
        if (openAppeals)
            throw new ConflictException("Còn đơn khiếu nại đang mở; cần xử lý hết trước khi đóng sự kiện.");

        if (!ev.ResultsFinalizedAt.HasValue)
            await EventAppealWindow.EnsureNoOpenWindowAsync(_uow, eventId, DateTime.UtcNow, ct);

        var pendingAwards = await _uow.Repository<EventAward>().ExistsAsync(
            a => a.EventId == eventId && a.ParticipationId != null &&
                 (a.Status == EventAwardStatus.AwardProposed || a.Status == EventAwardStatus.AwaitingClaim ||
                  a.Status == EventAwardStatus.Claimed || a.Status == EventAwardStatus.FulfillmentProcessing ||
                  a.Status == EventAwardStatus.Failed), ct);
        if (pendingAwards)
            throw new ConflictException("Còn suất giải thưởng chưa hoàn tất đối soát hoặc trao thưởng.");

        // Suất không có người đủ điều kiện nhận: ngân sách coi như đã hoàn khi sự kiện đóng.
        foreach (var award in eventAwards.Where(a => a.Status == EventAwardStatus.AwardProposed && a.ParticipationId == null))
        {
            award.Status = EventAwardStatus.UnclaimedBudgetReturned;
            award.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<EventAward>().Update(award);
        }

        var summary = await GetSummaryAsync(eventId, ct);
        ev.SummaryReportJson = JsonSerializer.Serialize(summary);
        ev.Status = EventStatus.Closed;
        ev.ClosedAt = DateTime.UtcNow;
        ev.UpdatedAt = ev.ClosedAt;
        _uow.Repository<Event>().Update(ev);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "EventClosed", nameof(Event), ev.Id, null, summary, ct);
        return MapSummary(ev);
    }

    private async Task<int> CountRetainedUsersAsync(HashSet<int> userIds, DateTime eventEndAt, int days, CancellationToken ct)
    {
        if (userIds.Count == 0 || DateTime.UtcNow <= eventEndAt) return 0;
        var from = eventEndAt;
        var to = eventEndAt.AddDays(days);
        var children = await _uow.Repository<ChildProfile>().FindAsync(c => userIds.Contains(c.OwnerUserId), cancellationToken: ct);
        var childOwnerMap = children.ToDictionary(c => c.Id, c => c.OwnerUserId);
        var childIds = childOwnerMap.Keys.ToHashSet();

        var activeUsers = new HashSet<int>();
        if (childIds.Count > 0)
        {
            var sessions = await _uow.Repository<ReadingSession>().FindAsync(
                s => childIds.Contains(s.ChildProfileId) && s.StartedAt >= from && s.StartedAt <= to, cancellationToken: ct);
            foreach (var s in sessions)
            {
                if (childOwnerMap.TryGetValue(s.ChildProfileId, out var ownerId)) activeUsers.Add(ownerId);
            }
        }

        var newStories = await _uow.Repository<Story>().FindAsync(
            s => userIds.Contains(s.AuthorUserId) && s.CreatedAt >= from && s.CreatedAt <= to, cancellationToken: ct);
        foreach (var st in newStories) activeUsers.Add(st.AuthorUserId);

        return activeUsers.Count;
    }

    private async Task<(int FamiliesFirstRead, decimal SecondStoryReturnRate)> ComputeTrialReadingMetricsAsync(
        int eventId, IReadOnlyList<EventStory> eventStories, CancellationToken ct)
    {
        if (eventStories.Count == 0) return (0, 0m);
        var storyIds = eventStories.Select(s => s.StoryId).Distinct().ToList();
        var sessions = await _uow.Repository<ReadingSession>().FindAsync(
            s => storyIds.Contains(s.StoryId) && s.Status == SessionStatus.Completed, cancellationToken: ct);
        if (sessions.Count == 0) return (0, 0m);

        var childIds = sessions.Select(s => s.ChildProfileId).Distinct().ToList();
        var children = await _uow.Repository<ChildProfile>().FindAsync(c => childIds.Contains(c.Id), cancellationToken: ct);
        var ownerByChild = children.ToDictionary(c => c.Id, c => c.OwnerUserId);

        var storiesByFamily = new Dictionary<int, HashSet<int>>();
        foreach (var s in sessions)
        {
            if (!ownerByChild.TryGetValue(s.ChildProfileId, out var ownerId)) continue;
            if (!storiesByFamily.TryGetValue(ownerId, out var set))
            {
                set = new HashSet<int>();
                storiesByFamily[ownerId] = set;
            }
            set.Add(s.StoryId);
        }

        var firstRead = storiesByFamily.Count;
        if (firstRead == 0) return (0, 0m);
        var returning = storiesByFamily.Values.Count(set => set.Count >= 2);
        return (firstRead, Math.Round((decimal)returning / firstRead, 4));
    }

    private async Task SyncAwardSlotsAsync(int eventId, Dictionary<EventTrackType, EventTrack> tracks, List<UpsertEventAwardSlotDto> slots, CancellationToken ct)
    {
        foreach (var s in slots)
        {
            if (!tracks.TryGetValue(s.TrackType, out var track)) continue;
            var pack = s.RewardType == EventRewardType.CreatorPackage
                ? await ResolveCreatorPackageAsync(s.TrackType, s.RewardName, ct)
                : null;
            for (var idx = 1; idx <= Math.Max(1, s.SlotCount); idx++)
            {
                await _uow.Repository<EventAward>().AddAsync(new EventAward
                {
                    EventId = eventId,
                    TrackId = track.Id,
                    RankOrTier = s.RankOrTier.Trim(),
                    SlotIndex = idx,
                    RewardType = s.RewardType,
                    RewardName = pack?.Name ?? s.RewardName.Trim(),
                    UnitValueVnd = s.UnitValueVnd,
                    CreditGrantAmount = pack?.AiCredits ?? s.CreditGrantAmount,
                    SubmitCreditGrantAmount = pack?.SubmitCredits ?? s.SubmitCreditGrantAmount,
                    Status = EventAwardStatus.AwardProposed
                }, ct);
            }
        }
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<EventDetailDto> BuildDetailAsync(Event ev, CancellationToken ct, int? currentUserId = null, UserRole? currentUserRole = null)
    {
        var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == ev.Id, cancellationToken: ct);
        var activities = ev.ConfigVersion == EventConfigVersion.ActivityBased
            ? await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == ev.Id, cancellationToken: ct)
            : Array.Empty<EventActivity>();
        var activityMap = activities.ToDictionary(a => a.Id);
        var activeRule = await _uow.Repository<EventRuleVersion>().FirstOrDefaultAsync(r => r.EventId == ev.Id && r.IsActive, cancellationToken: ct);
        var tasks = activeRule == null
            ? new List<EventTaskDefinition>()
            : (await _uow.Repository<EventTaskDefinition>().FindAsync(t => t.EventRuleVersionId == activeRule.Id, cancellationToken: ct)).OrderBy(t => t.SortOrder).ToList();
        var awards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == ev.Id, cancellationToken: ct);
        var storyCount = await _uow.Repository<EventStory>().CountAsync(s => s.EventId == ev.Id && s.IsActive, ct);
        var myStates = ev.ConfigVersion == EventConfigVersion.ActivityBased && currentUserId.HasValue && currentUserRole.HasValue
            ? await BuildActivityUserStatesAsync(ev, activities, currentUserId.Value, currentUserRole.Value, ct)
            : new List<EventActivityUserStateDto>();

        var staffView = currentUserRole is UserRole.Administrator or UserRole.Moderator;
        var assistantIds = staffView
            ? (await _uow.Repository<EventModeratorAssignment>().FindAsync(assignment => assignment.EventId == ev.Id, cancellationToken: ct))
                .Select(assignment => assignment.ModeratorId).OrderBy(id => id).ToList()
            : null;

        var categoryRepo = _uow.Repository<ContentCategory>();
        var mainCategory = categoryRepo != null
            ? await categoryRepo.GetByIdAsync(ev.MainCategoryId, ct)
            : null;

        return new EventDetailDto
        {
            CanManageEvent = currentUserRole == UserRole.Moderator
                ? EventModeratorAccess.OwnerId(ev) == currentUserId
                : null,
            LeadModeratorId = staffView ? EventModeratorAccess.OwnerId(ev) : null,
            AssistantModeratorIds = currentUserRole == UserRole.Administrator ? assistantIds : null,
            CanOperateEvent = staffView ? currentUserRole == UserRole.Administrator ||
                EventModeratorAccess.OwnerId(ev) == currentUserId ||
                (currentUserId.HasValue && assistantIds!.Contains(currentUserId.Value)) : null,
            Id = ev.Id,
            Title = ev.Title,
            Slug = ev.Slug,
            Description = ev.Description,
            BannerUrl = ev.BannerUrl,
            MainCategoryId = ev.MainCategoryId,
            MainCategoryName = mainCategory?.DisplayName,
            TargetAgeBands = ev.TargetAgeBands,
            Status = ev.Status,
            ConfigVersion = ev.ConfigVersion,
            OpensAt = ev.OpensAt,
            ClosesAt = ev.ClosesAt,
            Format = ev.Format,
            ScoringMode = ev.ScoringMode,
            MinVotesPerSubmission = ev.MinVotesPerSubmission,
            MaxParentRegistrations = ev.MaxParentRegistrations,
            SubmissionCutoffAt = ev.SubmissionCutoffAt,
            ReviewCutoffAt = ev.ReviewCutoffAt,
            VotingStartAt = ev.VotingStartAt,
            VotingEndAt = ev.VotingEndAt,
            CreatorVotingSchedule = EventVoting.CreatorSchedule(ev),
            ResultsFinalizedAt = ev.ResultsFinalizedAt,
            BudgetVnd = ev.BudgetVnd,
            CreatedAt = ev.CreatedAt,
            PublishedAt = ev.PublishedAt,
            ClosedAt = ev.ClosedAt,
            Tracks = tracks.Select(t => new EventTrackDto
            {
                Id = t.Id,
                TrackType = t.TrackType,
                RegistrationStartAt = t.RegistrationStartAt,
                RegistrationEndAt = t.RegistrationEndAt,
                ExecutionStartAt = t.ExecutionStartAt,
                ExecutionEndAt = t.ExecutionEndAt,
                MaxSubmissionsPerUser = t.MaxSubmissionsPerUser,
                GrantAiCredits = t.GrantAiCredits,
                GrantSubmitCredits = t.GrantSubmitCredits,
                WaiveWeeklyLibraryLimit = t.WaiveWeeklyLibraryLimit,
                ClaimWindowHours = t.ClaimWindowHours,
                MaxReallocationRounds = t.MaxReallocationRounds,
                AwardAllMissionCompleters = t.AwardAllMissionCompleters
            }).ToList(),
            Activities = activities.OrderBy(a => a.StartAt).ThenBy(a => a.Id).Select(MapActivity).ToList(),
            MyActivityStates = myStates,
            ActiveRule = activeRule == null ? null : MapRule(activeRule, tasks),
            Awards = awards.Select(a => new EventAwardSlotDto
            {
                Id = a.Id,
                TrackId = a.TrackId,
                EventActivityId = a.EventActivityId,
                ActivityType = a.EventActivityId.HasValue && activityMap.TryGetValue(a.EventActivityId.Value, out var act) ? act.ActivityType : null,
                ResultSource = a.ResultSource,
                TargetAudience = a.TargetAudience,
                RankOrTier = a.RankOrTier,
                SlotIndex = a.SlotIndex,
                RewardType = a.RewardType,
                RewardName = a.RewardName,
                UnitValueVnd = a.UnitValueVnd,
                CreditGrantAmount = a.CreditGrantAmount,
                SubmitCreditGrantAmount = a.SubmitCreditGrantAmount,
                ParticipationId = a.ParticipationId,
                Status = a.Status,
                ClaimDeadlineAt = a.ClaimDeadlineAt
            }).ToList(),
            EventStoriesCount = storyCount
        };
    }

    private const int MaxTitleLength = 200;

    private static void ValidateCreateRequest(CreateEventRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw new BadRequestException("Tên sự kiện không được để trống.");
        if (req.Title.Trim().Length > MaxTitleLength)
            throw new BadRequestException($"Tên sự kiện không được vượt quá {MaxTitleLength} ký tự.");
        if (req.ConfigVersion == EventConfigVersion.ActivityBased)
            ValidateActivityBasedDraftRequest(req);
        else
            ValidateEventConfiguration(req);
    }

    private static string BuildSlug(string title)
    {
        var baseSlug = new string(title.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        // Hậu tố ngẫu nhiên: thời gian theo giây trùng nhau khi hai sự kiện cùng tên được tạo trong một giây.
        // 10 ký tự để tên 200 ký tự (bản sao thêm "-activity") vẫn vừa cột Slug 220 ký tự.
        return $"{(string.IsNullOrEmpty(baseSlug) ? "event" : baseSlug)}-{Guid.NewGuid().ToString("N")[..10]}";
    }

    private static EventSummaryDto MapSummary(Event ev, string? mainCategoryName = null) => new()
    {
        Id = ev.Id,
        Title = ev.Title,
        Slug = ev.Slug,
        Description = ev.Description,
        BannerUrl = ev.BannerUrl,
        MainCategoryId = ev.MainCategoryId,
        MainCategoryName = mainCategoryName,
        TargetAgeBands = ev.TargetAgeBands,
        Status = ev.Status,
        ConfigVersion = ev.ConfigVersion,
        OpensAt = ev.OpensAt,
        ClosesAt = ev.ClosesAt,
        Format = ev.Format,
        ScoringMode = ev.ScoringMode,
        MinVotesPerSubmission = ev.MinVotesPerSubmission,
        MaxParentRegistrations = ev.MaxParentRegistrations,
        SubmissionCutoffAt = ev.SubmissionCutoffAt,
        ReviewCutoffAt = ev.ReviewCutoffAt,
        VotingStartAt = ev.VotingStartAt,
        VotingEndAt = ev.VotingEndAt,
        CreatorVotingSchedule = EventVoting.CreatorSchedule(ev),
        ResultsFinalizedAt = ev.ResultsFinalizedAt,
        BudgetVnd = ev.BudgetVnd,
        CreatedAt = ev.CreatedAt,
        PublishedAt = ev.PublishedAt,
        ClosedAt = ev.ClosedAt
    };

    private static EventRuleVersionDto MapRule(EventRuleVersion rule, List<EventTaskDefinition> tasks) => new()
    {
        Id = rule.Id,
        VersionNo = rule.VersionNo,
        ContentHash = rule.ContentHash,
        RulesText = rule.RulesText,
        RewardEligibilityText = rule.RewardEligibilityText,
        TieBreakerPolicyText = rule.TieBreakerPolicyText,
        InsufficientVotesPolicyText = rule.InsufficientVotesPolicyText,
        CreatorRubricJson = rule.CreatorRubricJson,
        CompletionMode = rule.CompletionMode,
        RequiredTaskCount = rule.RequiredTaskCount,
        RequiredPoints = rule.RequiredPoints,
        IsActive = rule.IsActive,
        Tasks = tasks.Select(t => new EventTaskDefinitionDto
        {
            Id = t.Id,
            TaskCode = t.TaskCode,
            TargetTrackType = t.TargetTrackType,
            Title = t.Title,
            Description = t.Description,
            SortOrder = t.SortOrder,
            VerifierType = t.VerifierType,
            TargetValue = t.TargetValue,
            ConfigJson = t.ConfigJson,
            IsMandatory = t.IsMandatory,
            Points = t.Points,
            AllowMultiplePerStory = t.AllowMultiplePerStory
        }).ToList()
    };
}
