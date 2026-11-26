using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public partial class EventParticipationService
{
    private static bool IsActivityRoleEligible(EventAudience audience, UserRole role) => audience switch
    {
        EventAudience.Creator => role == UserRole.ContentCreator,
        EventAudience.Parent => role == UserRole.User,
        EventAudience.Both => role is UserRole.ContentCreator or UserRole.User,
        _ => false
    };

    private async Task<EventParticipationDto> RegisterActivityLockedAsync(
        UserAccount user, Event ev, RegisterEventRequestDto request, CancellationToken ct)
    {
        EventActivity? act = null;
        if (request.EventActivityId.HasValue)
            act = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                a => a.EventId == ev.Id && a.Id == request.EventActivityId.Value, cancellationToken: ct);
        else if (request.ActivityType.HasValue)
            act = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                a => a.EventId == ev.Id && a.ActivityType == request.ActivityType.Value, cancellationToken: ct);
        else if (request.TrackType.HasValue)
        {
            var mapped = request.TrackType.Value == EventTrackType.Creator
                ? EventActivityType.Submission
                : EventActivityType.ReadingMissions;
            act = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                a => a.EventId == ev.Id && a.ActivityType == mapped, cancellationToken: ct);
        }

        if (act == null)
            throw new BadRequestException("Sự kiện không có hoạt động này.");
        if (!IsActivityRoleEligible(act.AllowedAudience, user.Role))
            throw new ForbiddenException("Vai trò tài khoản của bạn không được phép đăng ký hoạt động này.");

        var existing = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
            p => p.EventId == ev.Id && p.EventActivityId == act.Id && p.UserId == user.Id, cancellationToken: ct);
        if (existing != null)
        {
            await _uow.CommitTransactionAsync(ct);
            return MapActivityParticipation(existing, act.ActivityType, user.Role, null);
        }

        var now = DateTime.UtcNow;
        if (now < act.RegistrationStartAt || now > act.RegistrationEndAt)
            throw new ConflictException("Ngoài thời gian đăng ký của hoạt động.");

        if (act.MaxRegistrations is int cap)
        {
            var currentCount = await _uow.Repository<EventParticipation>().CountAsync(
                p => p.EventId == ev.Id && p.EventActivityId == act.Id && !p.IsDeleted, ct);
            if (currentCount >= cap)
                throw new ConflictException("Hoạt động đã đạt giới hạn số lượng đăng ký.");
        }

        var activeRule = await _uow.Repository<EventRuleVersion>().FirstOrDefaultAsync(
            r => r.EventId == ev.Id && r.IsActive, cancellationToken: ct)
            ?? throw new ConflictException("Sự kiện chưa có phiên bản thể lệ hoạt động.");

        string? safetyWarning = null;
        if (act.ActivityType is EventActivityType.ReadingMissions or EventActivityType.Voting && user.Role == UserRole.User)
        {
            var children = await _uow.Repository<ChildProfile>().FindAsync(
                c => c.OwnerUserId == user.Id && c.Status == ChildProfileStatus.Active && !c.IsDeleted, cancellationToken: ct);
            if (children.Count == 0)
                throw new BadRequestException("Cần có ít nhất một hồ sơ bé đang hoạt động để tham gia hoạt động gia đình.");

            if (act.ActivityType == EventActivityType.ReadingMissions)
            {
                var submission = act.StorySource == EventReadingStorySource.CuratedOnly ? null :
                    await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                        a => a.EventId == ev.Id && a.ActivityType == EventActivityType.Submission, cancellationToken: ct);
                if (!EventParentReadiness.IsWaitingForSubmissions(act, submission, now))
                    await EventParentReadiness.EnsureActivityReadyAsync(_uow, ev, act, ct);

                var policies = await _uow.Repository<SafetyPolicy>().FindAsync(p => children.Select(c => c.Id).Contains(p.ChildProfileId), cancellationToken: ct);
                var blockedCount = 0;
                foreach (var policy in policies)
                {
                    var cats = await _uow.Repository<SafetyPolicyCategory>().FindAsync(sc => sc.SafetyPolicyId == policy.Id, cancellationToken: ct);
                    if (!SafetyPolicyEvaluator.IsCategoryPermitted(ev.MainCategoryId, cats)) blockedCount++;
                }
                if (blockedCount == children.Count && policies.Count > 0)
                    safetyWarning = "Cài đặt an toàn của bé hiện đang chặn chủ đề của sự kiện. Bạn cần mở khóa chủ đề này trước khi cho bé đọc.";
            }
        }

        var participation = new EventParticipation
        {
            EventId = ev.Id,
            TrackId = null,
            EventActivityId = act.Id,
            UserId = user.Id,
            AcceptedRuleVersionId = activeRule.Id,
            Status = EventParticipationStatus.Registered
        };
        await _uow.Repository<EventParticipation>().AddAsync(participation, ct);

        if (act.ActivityType == EventActivityType.Submission && act.GrantAiCredits > 0)
        {
            var alreadyGranted = await _uow.Repository<CreditLot>().ExistsAsync(
                l => l.UserId == user.Id && l.CreditType == CreditType.AI && l.Source == CreditSource.EventGrant && l.EventId == ev.Id, ct);
            if (!alreadyGranted)
            {
                await _uow.Repository<CreditLot>().AddAsync(new CreditLot
                {
                    UserId = user.Id,
                    CreditType = CreditType.AI,
                    Source = CreditSource.EventGrant,
                    Total = act.GrantAiCredits,
                    Remaining = act.GrantAiCredits,
                    ExpiresAt = act.EndAt,
                    EventId = ev.Id
                }, ct);
            }
        }

        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(user.Id, "EventActivityRegistered", nameof(EventParticipation), participation.Id, null, new { ev.Id, act.ActivityType }, ct);
        await _uow.CommitTransactionAsync(ct);
        return MapActivityParticipation(participation, act.ActivityType, user.Role, safetyWarning);
    }

    public async Task<EventSubmissionDto> SubmitActivityStoryAsync(
        int userId, int eventId, CreatorEventSubmitRequestDto request, CancellationToken ct = default)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(eventId, ct);
            await _uow.AcquireTransactionLockAsync(request.StoryId, ct);
            var result = await SubmitActivityStoryLockedAsync(userId, eventId, request, ct);
            await _uow.CommitTransactionAsync(ct);
            return result;
        }
        catch
        {
            await _uow.RollbackTransactionAsync(CancellationToken.None);
            _uow.DiscardTrackedChanges();
            throw;
        }
    }

    private async Task<EventSubmissionDto> SubmitActivityStoryLockedAsync(
        int userId, int eventId, CreatorEventSubmitRequestDto request, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        if (ev.ConfigVersion != EventConfigVersion.ActivityBased)
            throw new BadRequestException("Endpoint này chỉ áp dụng cho sự kiện theo mô hình hoạt động.");
        if (ev.Status != EventStatus.Published || ev.ResultsFinalizedAt.HasValue)
            throw new ConflictException("Sự kiện hiện không mở nộp bài.");

        var act = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
            a => a.EventId == eventId && a.ActivityType == EventActivityType.Submission, cancellationToken: ct)
            ?? throw new NotFoundException(nameof(EventActivity), eventId);
        var now = DateTime.UtcNow;
        if (now < act.StartAt || now > act.EndAt)
            throw new ConflictException("Ngoài thời gian nộp bài của hoạt động.");

        var user = await _uow.Repository<UserAccount>().GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(UserAccount), userId);
        if (!IsActivityRoleEligible(act.AllowedAudience, user.Role))
            throw new ForbiddenException("Vai trò tài khoản của bạn không thuộc đối tượng nộp bài.");

        var participation = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
            p => p.EventId == eventId && p.EventActivityId == act.Id && p.UserId == userId, cancellationToken: ct)
            ?? throw new ForbiddenException("Bạn chưa đăng ký hoạt động sáng tác của sự kiện này.");
        if (participation.Status == EventParticipationStatus.Disqualified)
            throw new ConflictException("Đăng ký của bạn đã bị loại.");

        var story = await _uow.Repository<Story>().GetByIdAsync(request.StoryId, ct)
            ?? throw new NotFoundException(nameof(Story), request.StoryId);
        if (story.AuthorUserId != userId || story.IsDeleted)
            throw new ForbiddenException("Bạn chỉ được nộp truyện do chính mình tạo.");

        var isCreator = user.Role == UserRole.ContentCreator;
        if (isCreator && story.Scope != StoryScope.Creator)
            throw new ForbiddenException("Creator chỉ được nộp truyện thuộc không gian sáng tác Creator.");
        if (!isCreator && story.Scope != StoryScope.Parent)
            throw new ForbiddenException("Parent chỉ được nộp truyện do mình tạo.");

        var version = await _uow.Repository<StoryVersion>().FirstOrDefaultAsync(
            v => v.StoryId == story.Id && v.IsCurrent, cancellationToken: ct)
            ?? throw new BadRequestException("Truyện chưa có phiên bản nội dung.");

        if (!isCreator)
        {
            if (!request.ConsentToEventSharing)
                throw new BadRequestException("Cần đồng ý chia sẻ bản văn dự thi trong sự kiện.");
            if (story.ChildProfileId is not int childId || !await _uow.Repository<ChildProfile>().ExistsAsync(
                child => child.Id == childId && child.OwnerUserId == userId &&
                    child.Status == ChildProfileStatus.Active && !child.IsDeleted, ct))
                throw new ForbiddenException("Hồ sơ bé của truyện không thuộc quyền quản lý hiện tại.");
            if (story.Status != StoryStatus.Ready || story.ArchivedAt.HasValue)
                throw new BadRequestException("Truyện cần ở trạng thái sẵn sàng trước khi nộp.");
            if (string.IsNullOrWhiteSpace(version.Content) || version.Content.Length > 50_000)
                throw new BadRequestException("Nội dung dự thi phải có từ 1 đến 50.000 ký tự.");
        }

        var existingForStory = await _uow.Repository<EventSubmission>().FirstOrDefaultAsync(
            s => s.EventId == eventId && s.StoryId == story.Id && !s.IsDeleted, cancellationToken: ct);
        if (existingForStory != null)
        {
            if (existingForStory.CreatorUserId != userId)
                throw new ConflictException("Truyện này đã được nộp trong sự kiện.");
            if (existingForStory.Status is EventSubmissionStatus.PendingReview or EventSubmissionStatus.Approved ||
                existingForStory.LockedStoryVersionId.HasValue)
                throw new ConflictException("Bài dự thi đang chờ duyệt hoặc đã chốt, không thể nộp lại lúc này.");
        }
        else if (act.MaxSubmissionsPerUser is int maxSubs)
        {
            var mySubs = await _uow.Repository<EventSubmission>().FindAsync(
                s => s.EventId == eventId && s.EventActivityId == act.Id && s.CreatorUserId == userId && !s.IsDeleted, cancellationToken: ct);
            var distinctStories = mySubs.Select(s => s.StoryId).Distinct().Count();
            if (distinctStories >= maxSubs)
                throw new ConflictException($"Bạn đã đạt giới hạn tối đa {maxSubs} truyện dự thi cho hoạt động này.");
        }

        var otherSubmissions = await _uow.Repository<EventSubmission>().FindAsync(
            s => s.EventId == eventId && s.StoryId != story.Id, cancellationToken: ct);
        double maxSim = 0d;
        int? refStoryId = null;
        foreach (var other in otherSubmissions)
        {
            var otherVersion = await _uow.Repository<StoryVersion>().GetByIdAsync(other.CurrentStoryVersionId, ct);
            var sim = EventMissionEvaluator.ComputeJaccardSimilarity(version.Content, otherVersion?.Content);
            if (sim > maxSim)
            {
                maxSim = sim;
                refStoryId = other.StoryId;
            }
        }

        var submission = existingForStory ?? new EventSubmission
        {
            EventId = eventId,
            ParticipationId = participation.Id,
            EventActivityId = act.Id,
            CreatorUserId = userId,
            StoryId = story.Id,
            CreatedAt = now
        };
        submission.CurrentStoryVersionId = version.Id;
        submission.SimilarityScore = Math.Round(maxSim, 4);
        submission.SimilarityReferenceStoryId = refStoryId;
        submission.Status = EventSubmissionStatus.PendingReview;
        submission.SubmittedAt = now;
        submission.UpdatedAt = now;
        if (!isCreator)
        {
            submission.ParentSubmissionTitle = string.IsNullOrWhiteSpace(version.Title) ? story.Title : version.Title;
            submission.ParentSubmissionText = version.Content;
        }
        if (existingForStory == null) await _uow.Repository<EventSubmission>().AddAsync(submission, ct);
        else _uow.Repository<EventSubmission>().Update(submission);
        await _uow.SaveChangesAsync(ct);

        if (isCreator)
        {
            var openReview = await _uow.Repository<ModerationItem>().FirstOrDefaultAsync(x => x.StoryId == story.Id
                && x.StoryVersionId == version.Id && x.ItemType == ModerationItemType.PublicStoryReview
                && (x.Status == ModerationItemStatus.Pending || x.Status == ModerationItemStatus.Claimed), cancellationToken: ct);
            if (story.IsPublished && version.Moderation.ReviewStatus == VersionReviewStatus.Approved)
            {
                await new EventIntegrationHooks(_uow).LinkApprovedStoryAsync(eventId, story.Id, version.Id, ct);
            }
            else if (openReview != null)
            {
                submission.LatestModerationItemId = openReview.Id;
                _uow.Repository<EventSubmission>().Update(submission);
                await _uow.SaveChangesAsync(ct);

                if (openReview.Status == ModerationItemStatus.Claimed)
                {
                    await EventSubmissionModerationAccess.ReleaseUnauthorizedClaimedItemForVersionAsync(
                        _uow, _audit, userId, story.Id, version.Id, now, ct);
                }
            }
            else
            {
                if (_publicSubmission == null) throw new ConflictException("Dịch vụ gửi duyệt chưa khả dụng.");
                var labels = await _uow.Repository<StoryCategory>().FindAsync(x => x.StoryId == story.Id, cancellationToken: ct);
                var queued = await _publicSubmission.SubmitForEventAsync(userId, new SubmitPublicStoryRequestDto
                {
                    StoryId = story.Id,
                    StoryVersionId = version.Id,
                    ContentCategoryIds = labels.Count > 0 ? labels.Select(x => x.ContentCategoryId).ToList() : [ev.MainCategoryId]
                }, waiveSubmitQuota: true, eventId: eventId, cancellationToken: ct);
                if (queued != null)
                {
                    submission.LatestModerationItemId = queued.ItemId;
                    _uow.Repository<EventSubmission>().Update(submission);
                    await _uow.SaveChangesAsync(ct);
                }
            }
        }

        await _audit.LogAsync(userId, "EventActivityStorySubmitted", nameof(EventSubmission), submission.Id, null, new { eventId, story.Id, isCreator }, ct);
        var created = MapSubmission(submission, Array.Empty<EventEvaluation>());
        created.SubmittedTitle ??= string.IsNullOrWhiteSpace(version.Title) ? story.Title : version.Title;
        return created;
    }

    public async Task<IReadOnlyList<EventSubmissionDto>> ListMySubmissionsAsync(int userId, int eventId, CancellationToken ct = default)
    {
        var items = await _uow.Repository<EventSubmission>().FindAsync(
            s => s.EventId == eventId && s.CreatorUserId == userId && !s.IsDeleted, cancellationToken: ct);
        if (items.Count == 0) return Array.Empty<EventSubmissionDto>();

        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct);
        var hideVotes = false;
        if (ev?.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            var voting = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                a => a.EventId == eventId && a.ActivityType == EventActivityType.Voting, cancellationToken: ct);
            hideVotes = voting != null && DateTime.UtcNow <= voting.EndAt;
        }
        else if (ev != null && EventVoting.IsEnabled(ev.ScoringMode))
        {
            var voting = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(
                t => t.EventId == eventId && t.TrackType == EventTrackType.Parent, cancellationToken: ct);
            var voteEnd = ev.VotingEndAt ?? voting?.ExecutionEndAt;
            hideVotes = voteEnd.HasValue && DateTime.UtcNow <= voteEnd.Value;
        }

        var votes = hideVotes
            ? Array.Empty<EventVote>()
            : await _uow.Repository<EventVote>().FindAsync(
                v => v.EventId == eventId && !v.IsDisqualified && !v.IsDeleted, cancellationToken: ct);

        // Bài của Creator không lưu tên lúc nộp; lấy tên phiên bản đã nộp, thiếu thì tên truyện.
        var untitled = items.Where(s => string.IsNullOrWhiteSpace(s.ParentSubmissionTitle)).ToList();
        var versionIds = untitled.Select(s => s.CurrentStoryVersionId).Distinct().ToList();
        var storyIds = untitled.Select(s => s.StoryId).Distinct().ToList();
        var versionTitles = versionIds.Count == 0
            ? new Dictionary<int, string?>()
            : (await _uow.Repository<StoryVersion>().FindAsync(v => versionIds.Contains(v.Id), cancellationToken: ct))
                .ToDictionary(v => v.Id, v => (string?)v.Title);
        var storyTitles = storyIds.Count == 0
            ? new Dictionary<int, string?>()
            : (await _uow.Repository<Story>().FindAsync(x => storyIds.Contains(x.Id), cancellationToken: ct))
                .ToDictionary(x => x.Id, x => (string?)x.Title);

        var result = new List<EventSubmissionDto>(items.Count);
        foreach (var s in items.OrderByDescending(x => x.SubmittedAt ?? x.CreatedAt).ThenByDescending(x => x.Id))
        {
            var evals = await _uow.Repository<EventEvaluation>().FindAsync(e => e.EventSubmissionId == s.Id, cancellationToken: ct);
            var subVotes = hideVotes
                ? null
                : votes.Where(v => (v.EventSubmissionId == s.Id || v.StoryId == s.StoryId) && v.StoryVersionId == s.LockedStoryVersionId).ToList();
            var dto = MapSubmission(s, evals, subVotes);
            if (string.IsNullOrWhiteSpace(dto.SubmittedTitle))
            {
                var versionTitle = versionTitles.GetValueOrDefault(s.CurrentStoryVersionId);
                dto.SubmittedTitle = string.IsNullOrWhiteSpace(versionTitle) ? storyTitles.GetValueOrDefault(s.StoryId) : versionTitle;
            }
            result.Add(dto);
        }
        return result;
    }

    private async Task<EventMissionDashboardDto> BuildActivityDashboardAsync(
        Event ev, EventParticipation participation, EventActivity act, CancellationToken ct)
    {
        var rule = await _uow.Repository<EventRuleVersion>().GetByIdAsync(participation.AcceptedRuleVersionId, ct)
            ?? throw new NotFoundException(nameof(EventRuleVersion), participation.AcceptedRuleVersionId);
        var allDefinitions = await _uow.Repository<EventTaskDefinition>().FindAsync(
            t => t.EventRuleVersionId == rule.Id, cancellationToken: ct);

        var definitions = act.ActivityType == EventActivityType.ReadingMissions
            ? allDefinitions.Where(d => d.TargetTrackType == EventTrackType.Parent || d.TargetTrackType == null).ToList()
            : new List<EventTaskDefinition>();

        IReadOnlyList<EventTaskProgress> progresses = act.ActivityType == EventActivityType.ReadingMissions
            ? await EventActivityEvidence.LoadForActivityAsync(_uow, participation, act, definitions, ct)
            : Array.Empty<EventTaskProgress>();

        var summary = EventMissionEvaluator.Evaluate(rule, definitions, progresses, enforceMandatoryInAllModes: true);

        var storyItems = await LoadActivityStoriesAsync(ev, participation, act, ct);

        return new EventMissionDashboardDto
        {
            Participation = MapActivityParticipation(participation, act.ActivityType, UserRole.User, null),
            CompletionMode = rule.CompletionMode,
            RequiredTaskCount = rule.RequiredTaskCount,
            RequiredPoints = rule.RequiredPoints,
            CompletedTasksCount = summary.CompletedCount,
            EarnedPoints = summary.EarnedPoints,
            AllRequirementsMet = summary.AllRequirementsMet,
            Tasks = summary.Tasks.Select(t => new EventTaskProgressItemDto
            {
                TaskDefinitionId = t.Task.Id,
                TaskCode = t.Task.TaskCode,
                Title = t.Task.Title,
                Description = t.Task.Description,
                VerifierType = t.Task.VerifierType,
                TargetValue = t.Task.TargetValue,
                CurrentValue = t.CurrentValue,
                IsCompleted = t.IsCompleted,
                IsMandatory = t.Task.IsMandatory,
                Points = t.Task.Points
            }).ToList(),
            Stories = storyItems
        };
    }

    private async Task<List<EventStoryLibraryItemDto>> LoadActivityStoriesAsync(
        Event ev, EventParticipation participation, EventActivity act, CancellationToken ct)
    {
        var storyItems = new List<EventStoryLibraryItemDto>();
        if (act.ActivityType is EventActivityType.ReadingMissions or EventActivityType.Voting)
        {
            var now = DateTime.UtcNow;
            var eventStories = await _uow.Repository<EventStory>().FindAsync(
                es => es.EventId == ev.Id && es.IsActive && !es.IsDeleted && es.Source != EventStorySource.ParentSubmission, cancellationToken: ct);
            if (act.ActivityType == EventActivityType.Voting)
            {
                var approvedSubmissions = await _uow.Repository<EventSubmission>().FindAsync(
                    s => s.EventId == ev.Id && !s.IsDeleted && s.Status == EventSubmissionStatus.Approved &&
                         s.LockedStoryVersionId.HasValue, cancellationToken: ct);
                var approvedVersions = approvedSubmissions.ToDictionary(s => s.Id, s => (s.StoryId, s.LockedStoryVersionId));
                eventStories = eventStories.Where(es => es.Source == EventStorySource.CreatorSubmission &&
                    es.EventSubmissionId is int id && approvedVersions.TryGetValue(id, out var entry) &&
                    entry.StoryId == es.StoryId && entry.LockedStoryVersionId == es.StoryVersionId).ToList();
            }
            else
            {
                eventStories = eventStories.Where(es => EventParentReadiness.IncludesReadableSource(es.Source, act.StorySource)).ToList();
            }

            var children = await _uow.Repository<ChildProfile>().FindAsync(
                c => c.OwnerUserId == participation.UserId && !c.IsDeleted, cancellationToken: ct);
            var childIds = children.Select(c => c.Id).ToHashSet();
            var childAgeBands = children.Select(c => c.AgeBand.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var storyIds = eventStories.Select(es => es.StoryId).Distinct().ToList();
            var sessions = childIds.Count == 0 || storyIds.Count == 0
                ? Array.Empty<ReadingSession>()
                : await _uow.Repository<ReadingSession>().FindAsync(
                    s => childIds.Contains(s.ChildProfileId) && storyIds.Contains(s.StoryId) && s.Status == SessionStatus.Completed &&
                         (act.ActivityType != EventActivityType.Voting || (s.CompletedAt >= act.StartAt && s.CompletedAt <= act.EndAt)),
                    cancellationToken: ct);
            var myVotes = (await _uow.Repository<EventVote>().FindAsync(
                v => v.EventId == ev.Id && v.ParentUserId == participation.UserId && !v.IsDeleted,
                cancellationToken: ct)).ToDictionary(v => v.StoryId);

            var daySeed = VietnamTime.LocalDate(now).DayOfYear;
            var rawItems = new List<(EventStoryLibraryItemDto Item, bool AgeMatch, int RotationKey)>();
            foreach (var es in eventStories)
            {
                var story = await _uow.Repository<Story>().GetByIdAsync(es.StoryId, ct);
                if (story == null || !story.IsPublished || story.IsDeleted) continue;
                if (act.ActivityType == EventActivityType.Voting && story.AuthorUserId == participation.UserId) continue;
                var hasRead = sessions.Any(s => s.StoryId == es.StoryId && s.StoryVersionId == es.StoryVersionId);
                myVotes.TryGetValue(es.StoryId, out var vote);
                rawItems.Add((new EventStoryLibraryItemDto
                {
                    StoryId = story.Id,
                    StoryVersionId = es.StoryVersionId,
                    Title = story.Title ?? string.Empty,
                    Description = story.Description ?? string.Empty,
                    AgeBand = story.AgeBand,
                    Genre = story.Genre ?? string.Empty,
                    Source = es.Source,
                    HasFamilyRead = hasRead,
                    MyVoteStars = vote is { IsDisqualified: false } ? vote.Stars : null,
                    MyVoteComment = vote is { IsDisqualified: false } ? vote.Comment : null
                }, childAgeBands.Contains(story.AgeBand), HashCode.Combine(participation.UserId, ev.Id, daySeed, es.StoryId)));
            }

            storyItems = rawItems
                .OrderByDescending(x => x.AgeMatch)
                .ThenBy(x => x.Item.HasFamilyRead)
                .ThenBy(x => x.RotationKey)
                .Select(x => x.Item)
                .ToList();
        }

        return storyItems;
    }

    private static EventParticipationDto MapActivityParticipation(
        EventParticipation p, EventActivityType actType, UserRole role, string? safetyWarning) => new()
        {
            Id = p.Id,
            EventId = p.EventId,
            TrackId = p.TrackId,
            TrackType = role == UserRole.ContentCreator ? EventTrackType.Creator : EventTrackType.Parent,
            EventActivityId = p.EventActivityId,
            ActivityType = actType,
            UserId = p.UserId,
            AcceptedRuleVersionId = p.AcceptedRuleVersionId,
            Status = p.Status,
            MissionCompletedAt = p.MissionCompletedAt,
            VerifiedAt = p.VerifiedAt,
            AppealDeadlineAt = EventAppealWindow.DeadlineFor(p),
            VerificationNotesJson = p.VerificationNotesJson,
            SafetyWarning = safetyWarning
        };

    private async Task<EventVoteDto> CastActivityVoteLockedAsync(
        int parentUserId, Event ev, CastEventVoteRequestDto request, CancellationToken ct)
    {
        if (ev.Status != EventStatus.Published || ev.ResultsFinalizedAt.HasValue)
            throw new ConflictException("Sự kiện hiện không mở bình chọn.");

        var user = await _uow.Repository<UserAccount>().GetByIdAsync(parentUserId, ct)
            ?? throw new NotFoundException(nameof(UserAccount), parentUserId);
        if (user.Role != UserRole.User)
            throw new ForbiddenException("Bản đầu hoạt động bình chọn chỉ mở cho Parent.");

        var votingAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
            a => a.EventId == ev.Id && a.ActivityType == EventActivityType.Voting, cancellationToken: ct)
            ?? throw new ConflictException("Sự kiện chưa bật hoạt động bình chọn.");
        var now = DateTime.UtcNow;
        if (now < votingAct.StartAt || now > votingAct.EndAt)
            throw new ConflictException("Ngoài thời gian bình chọn của hoạt động.");

        var participation = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
            p => p.EventId == ev.Id && p.EventActivityId == votingAct.Id && p.UserId == parentUserId && !p.IsDeleted,
            cancellationToken: ct);
        if (participation == null || participation.Status == EventParticipationStatus.Disqualified)
            throw new ForbiddenException("Bạn chưa đăng ký hợp lệ cho hoạt động bình chọn.");

        var eventStory = await _uow.Repository<EventStory>().FirstOrDefaultAsync(
            es => es.EventId == ev.Id && es.StoryId == request.StoryId && es.IsActive, cancellationToken: ct)
            ?? throw new BadRequestException("Truyện không thuộc danh sách dự thi của sự kiện.");
        if (eventStory.Source != EventStorySource.CreatorSubmission || eventStory.EventSubmissionId is not int submissionId)
            throw new ConflictException("Bài này không đủ điều kiện hiển thị cho bé đọc trên nền tảng để bình chọn.");
        var submission = await _uow.Repository<EventSubmission>().GetByIdAsync(submissionId, ct);
        if (submission == null || submission.EventId != ev.Id || submission.StoryId != request.StoryId ||
            submission.Status != EventSubmissionStatus.Approved || submission.LockedStoryVersionId != eventStory.StoryVersionId)
            throw new ConflictException("Chỉ được bình chọn đúng phiên bản bài dự thi đã được duyệt.");

        var story = await _uow.Repository<Story>().GetByIdAsync(request.StoryId, ct);
        if (story == null || !story.IsPublished || story.IsDeleted)
            throw new ConflictException("Truyện chưa xuất bản trên nền tảng cho bé đọc.");
        if (story.AuthorUserId == parentUserId)
            throw new ForbiddenException("Không thể tự bình chọn cho truyện của chính mình.");

        var children = await _uow.Repository<ChildProfile>().FindAsync(
            c => c.OwnerUserId == parentUserId && !c.IsDeleted, cancellationToken: ct);
        if (request.ChildProfileId.HasValue && children.All(c => c.Id != request.ChildProfileId.Value))
            throw new ForbiddenException("Hồ sơ trẻ không thuộc quyền quản lý của bạn.");
        var childIds = request.ChildProfileId.HasValue
            ? new HashSet<int> { request.ChildProfileId.Value }
            : children.Select(c => c.Id).ToHashSet();
        var sessions = childIds.Count == 0
            ? Array.Empty<ReadingSession>()
            : await _uow.Repository<ReadingSession>().FindAsync(
                r => childIds.Contains(r.ChildProfileId) && r.StoryId == request.StoryId &&
                     r.StoryVersionId == eventStory.StoryVersionId && r.Status == SessionStatus.Completed &&
                     r.CompletedAt >= votingAct.StartAt && r.CompletedAt <= votingAct.EndAt, cancellationToken: ct);
        var proof = sessions.OrderByDescending(r => r.CompletedAt).FirstOrDefault()
            ?? throw new ConflictException("Bé cần đọc xong đúng phiên bản truyện dự thi trong thời gian bình chọn trước khi đánh giá.");

        var existing = await _uow.Repository<EventVote>().FirstOrDefaultAsync(
            v => v.EventId == ev.Id && v.StoryId == request.StoryId && v.ParentUserId == parentUserId && !v.IsDeleted,
            cancellationToken: ct);
        if (existing != null && existing.IsDisqualified)
            throw new ConflictException("Phiếu bình chọn này đã bị loại do vi phạm và không thể chỉnh sửa.");

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (existing == null)
        {
            existing = new EventVote
            {
                EventId = ev.Id,
                EventSubmissionId = eventStory.EventSubmissionId,
                StoryId = request.StoryId,
                StoryVersionId = eventStory.StoryVersionId,
                ParticipationId = participation.Id,
                ParentUserId = parentUserId,
                ChildProfileId = proof.ChildProfileId,
                ReadingSessionId = proof.Id,
                Stars = request.Stars,
                Comment = comment
            };
            await _uow.Repository<EventVote>().AddAsync(existing, ct);
        }
        else
        {
            existing.Stars = request.Stars;
            existing.Comment = comment;
            existing.ChildProfileId = proof.ChildProfileId;
            existing.ReadingSessionId = proof.Id;
            existing.UpdatedAt = now;
            _uow.Repository<EventVote>().Update(existing);
        }

        await _uow.SaveChangesAsync(ct);

        var readingAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
            a => a.EventId == ev.Id && a.ActivityType == EventActivityType.ReadingMissions, cancellationToken: ct);
        if (readingAct != null)
        {
            var readingPart = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
                p => p.EventId == ev.Id && p.EventActivityId == readingAct.Id && p.UserId == parentUserId,
                cancellationToken: ct);
            if (readingPart is { Status: EventParticipationStatus.Registered })
            {
                var dash = await BuildActivityDashboardAsync(ev, readingPart, readingAct, ct);
                if (dash.AllRequirementsMet)
                {
                    readingPart.Status = EventParticipationStatus.MissionCompleted;
                    readingPart.MissionCompletedAt = now;
                    readingPart.UpdatedAt = now;
                    _uow.Repository<EventParticipation>().Update(readingPart);
                }
            }
        }
        if (participation.Status == EventParticipationStatus.Registered)
        {
            participation.Status = EventParticipationStatus.MissionCompleted;
            participation.MissionCompletedAt = now;
            participation.UpdatedAt = now;
            _uow.Repository<EventParticipation>().Update(participation);
        }
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(parentUserId, "EventVoteCast", nameof(EventVote), existing.Id, null, new { ev.Id, request.StoryId, request.Stars }, ct);
        await _uow.CommitTransactionAsync(ct);
        return MapVote(existing);
    }

    private async Task<EventLeaderboardDto> BuildActivityLeaderboardAsync(
        Event ev, bool includeHidden, bool verifiedOnly, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(ev.FinalLeaderboardSnapshotJson))
        {
            var frozen = JsonSerializer.Deserialize<EventLeaderboardDto>(ev.FinalLeaderboardSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (frozen != null) return frozen;
        }

        var votingAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
            a => a.EventId == ev.Id && a.ActivityType == EventActivityType.Voting, cancellationToken: ct)
            ?? throw new ConflictException("Sự kiện này không bật hoạt động bình chọn.");

        var hidden = !includeHidden && DateTime.UtcNow <= votingAct.EndAt;
        if (hidden)
        {
            return new EventLeaderboardDto
            {
                EventId = ev.Id,
                Hidden = true,
                ResultsFinalized = false,
                ScoringMode = EventScoringMode.ParentVoteAverage,
                MinVotesPerSubmission = votingAct.MinVotesPerSubmission
            };
        }

        var submissions = await _uow.Repository<EventSubmission>().FindAsync(
            s => s.EventId == ev.Id && s.Status == EventSubmissionStatus.Approved &&
                 s.LockedStoryVersionId != null && s.ParentSubmissionText == null,
            cancellationToken: ct);
        if (verifiedOnly)
        {
            var verified = await _uow.Repository<EventParticipation>().FindAsync(
                p => p.EventId == ev.Id && p.Status == EventParticipationStatus.Verified, cancellationToken: ct);
            var verifiedIds = verified.Select(p => p.Id).ToHashSet();
            submissions = submissions.Where(s => verifiedIds.Contains(s.ParticipationId)).ToList();
        }

        var votes = await _uow.Repository<EventVote>().FindAsync(
            v => v.EventId == ev.Id && !v.IsDisqualified && !v.IsDeleted, cancellationToken: ct);
        var eligibleVoters = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.EventId == ev.Id && p.Status != EventParticipationStatus.Disqualified && !p.IsDeleted, cancellationToken: ct);
        var voterIds = eligibleVoters.Select(p => p.Id).ToHashSet();
        var stories = await _uow.Repository<Story>().FindAsync(
            s => submissions.Select(x => x.StoryId).Contains(s.Id), cancellationToken: ct);
        var titleByStory = stories.ToDictionary(s => s.Id, s => s.Title ?? string.Empty);

        var candidates = submissions.Select(s =>
        {
            var subVotes = votes.Where(v => voterIds.Contains(v.ParticipationId) &&
                (v.EventSubmissionId == s.Id || v.StoryId == s.StoryId) && v.StoryVersionId == s.LockedStoryVersionId).ToList();
            var avg = subVotes.Count == 0 ? 0m : Math.Round((decimal)subVotes.Average(v => v.Stars), 4);
            return new CreatorVoteRankingCandidate(
                s.Id,
                s.ParticipationId,
                s.CreatorUserId,
                s.StoryId,
                titleByStory.GetValueOrDefault(s.StoryId, $"Truyện #{s.StoryId}"),
                subVotes.Count,
                avg,
                s.CreatedAt);
        }).ToList();

        var ranked = EventMissionEvaluator.RankCreatorByVotes(candidates, votingAct.MinVotesPerSubmission);
        var ranks = ranked.Select((candidate, index) => (candidate.SubmissionId, Rank: index + 1))
            .ToDictionary(item => item.SubmissionId, item => item.Rank);
        var entries = candidates
            .OrderBy(c => ranks.GetValueOrDefault(c.SubmissionId, int.MaxValue))
            .ThenByDescending(c => c.ValidVoteCount)
            .ThenBy(c => c.SubmissionId)
            .Select(candidate =>
            {
                var rank = ranks.GetValueOrDefault(candidate.SubmissionId);
                return new EventLeaderboardEntryDto
                {
                    Rank = rank,
                    AuthorTrackType = EventTrackType.Creator,
                    SubmissionId = candidate.SubmissionId,
                    ParticipationId = candidate.ParticipationId,
                    CreatorUserId = candidate.CreatorUserId,
                    StoryId = candidate.StoryId,
                    StoryTitle = candidate.StoryTitle,
                    ValidVoteCount = candidate.ValidVoteCount,
                    AverageStars = Math.Round(candidate.AverageStars, 2),
                    MeetsThreshold = rank > 0,
                    FirstSubmittedAt = candidate.FirstSubmittedAt
                };
            }).ToList();

        return new EventLeaderboardDto
        {
            EventId = ev.Id,
            Hidden = false,
            ResultsFinalized = ev.ResultsFinalizedAt.HasValue,
            FinalizedAt = ev.ResultsFinalizedAt,
            ScoringMode = EventScoringMode.ParentVoteAverage,
            MinVotesPerSubmission = votingAct.MinVotesPerSubmission,
            Entries = entries
        };
    }
}
