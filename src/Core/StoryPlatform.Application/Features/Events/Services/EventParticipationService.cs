using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.ReadingSlots;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public partial class EventParticipationService : IEventParticipationService
{
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogWriter _audit;
    private readonly IReadingSlotService? _readingSlots;
    private readonly INotificationService? _notifications;
    private readonly IPublicStorySubmissionService? _publicSubmission;

    public EventParticipationService(
        IUnitOfWork uow,
        IAuditLogWriter audit,
        IReadingSlotService? readingSlots = null,
        INotificationService? notifications = null,
        IPublicStorySubmissionService? publicSubmission = null)
    {
        _uow = uow;
        _audit = audit;
        _readingSlots = readingSlots;
        _notifications = notifications;
        _publicSubmission = publicSubmission;
    }

    public async Task<EventParticipationDto> RegisterAsync(int userId, int eventId, RegisterEventRequestDto request, CancellationToken ct = default)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(eventId, ct);
            if (!request.AcceptRules) throw new BadRequestException("Bạn phải chấp nhận thể lệ sự kiện (EV-11).");

            var user = await _uow.Repository<UserAccount>().GetByIdAsync(userId, ct)
                ?? throw new NotFoundException(nameof(UserAccount), userId);
            if (user.Status != AccountStatus.Active) throw new ForbiddenException("Tài khoản chưa ở trạng thái hoạt động.");

            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            if (ev.Status != EventStatus.Published) throw new ConflictException("Sự kiện chưa mở đăng ký.");

            if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
                return await RegisterActivityLockedAsync(user, ev, request, ct);

            var trackType = request.TrackType ?? throw new BadRequestException("Thiếu nhánh tham gia.");
            if (trackType == EventTrackType.Creator && user.Role != UserRole.ContentCreator)
                throw new ForbiddenException("Chỉ tài khoản Content Creator mới được đăng ký nhánh sáng tác.");
            if (trackType == EventTrackType.Parent && user.Role != UserRole.User)
                throw new ForbiddenException("Chỉ tài khoản Phụ huynh mới được đăng ký nhánh gia đình.");

            var track = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(t => t.EventId == eventId && t.TrackType == trackType, cancellationToken: ct)
                ?? throw new BadRequestException("Sự kiện không có nhánh tham gia này.");

            var existing = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
                p => p.EventId == eventId && p.TrackId == track.Id && p.UserId == userId, cancellationToken: ct);
            if (existing != null)
            {
                await _uow.CommitTransactionAsync(ct);
                return MapParticipation(existing, track.TrackType, null);
            }


            var now = DateTime.UtcNow;
            if (now < track.RegistrationStartAt || now > track.RegistrationEndAt)
                throw new ConflictException("Ngoài thời gian đăng ký của nhánh tham gia (EV-08).");

            if (request.TrackType == EventTrackType.Parent)
            {
                if (ev.MaxParentRegistrations is int cap)
                {
                    var currentParents = await _uow.Repository<EventParticipation>().CountAsync(
                        p => p.EventId == eventId && p.TrackId == track.Id && !p.IsDeleted, ct);
                    if (currentParents >= cap)
                        throw new ConflictException("Sự kiện đã đạt giới hạn đăng ký gia đình.");
                }
                if (ev.Format == EventFormat.Standard || now >= (ev.VotingStartAt ?? track.ExecutionStartAt))
                    await EventParentReadiness.EnsureAsync(_uow, eventId, ct);
            }

            var activeRule = await _uow.Repository<EventRuleVersion>().FirstOrDefaultAsync(r => r.EventId == eventId && r.IsActive, cancellationToken: ct)
                ?? throw new ConflictException("Sự kiện chưa có phiên bản thể lệ hoạt động.");

            string? safetyWarning = null;
            if (request.TrackType == EventTrackType.Parent)
            {
                var children = await _uow.Repository<ChildProfile>().FindAsync(
                    c => c.OwnerUserId == userId && c.Status == ChildProfileStatus.Active && !c.IsDeleted, cancellationToken: ct);
                if (children.Count == 0)
                    throw new BadRequestException("Cần có ít nhất một hồ sơ bé đang hoạt động để tham gia nhánh gia đình.");

                var policies = await _uow.Repository<SafetyPolicy>().FindAsync(p => children.Select(c => c.Id).Contains(p.ChildProfileId), cancellationToken: ct);
                var blockedCount = 0;
                foreach (var policy in policies)
                {
                    var cats = await _uow.Repository<SafetyPolicyCategory>().FindAsync(sc => sc.SafetyPolicyId == policy.Id, cancellationToken: ct);
                    if (!SafetyPolicyEvaluator.IsCategoryPermitted(ev.MainCategoryId, cats)) blockedCount++;
                }
                if (blockedCount == children.Count && policies.Count > 0)
                {
                    safetyWarning = "Cài đặt an toàn của bé hiện đang chặn chủ đề của sự kiện. Bạn cần mở khóa chủ đề này trước khi cho bé đọc.";
                }
            }

            var participation = new EventParticipation
            {
                EventId = eventId,
                TrackId = track.Id,
                UserId = userId,
                AcceptedRuleVersionId = activeRule.Id,
                Status = EventParticipationStatus.Registered
            };
            await _uow.Repository<EventParticipation>().AddAsync(participation, ct);

            if (request.TrackType == EventTrackType.Creator)
            {
                if (track.GrantAiCredits > 0)
                {
                    await _uow.Repository<CreditLot>().AddAsync(new CreditLot
                    {
                        UserId = userId,
                        CreditType = CreditType.AI,
                        Source = CreditSource.EventGrant,
                        Total = track.GrantAiCredits,
                        Remaining = track.GrantAiCredits,
                        ExpiresAt = track.ExecutionEndAt,
                        EventId = eventId
                    }, ct);
                }
                if (track.GrantSubmitCredits > 0)
                {
                    await _uow.Repository<CreditLot>().AddAsync(new CreditLot
                    {
                        UserId = userId,
                        CreditType = CreditType.Submit,
                        Source = CreditSource.EventGrant,
                        Total = track.GrantSubmitCredits,
                        Remaining = track.GrantSubmitCredits,
                        ExpiresAt = track.ExecutionEndAt,
                        EventId = eventId
                    }, ct);
                }
            }

            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(userId, "EventRegistered", nameof(EventParticipation), participation.Id, null, new { eventId, track.TrackType }, ct);
            await _uow.CommitTransactionAsync(ct);
            return MapParticipation(participation, track.TrackType, safetyWarning);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(CancellationToken.None);
            _uow.DiscardTrackedChanges();
            throw;
        }
    }

    public async Task<EventSubmissionDto> SubmitCreatorStoryAsync(int creatorUserId, int eventId, CreatorEventSubmitRequestDto request, CancellationToken ct = default)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(eventId, ct);
            await _uow.AcquireTransactionLockAsync(request.StoryId, ct);
            var result = await SubmitCreatorStoryLockedAsync(creatorUserId, eventId, request, ct);
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

    private async Task<EventSubmissionDto> SubmitCreatorStoryLockedAsync(int creatorUserId, int eventId, CreatorEventSubmitRequestDto request, CancellationToken ct)
    {
        var track = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(t => t.EventId == eventId && t.TrackType == EventTrackType.Creator, cancellationToken: ct)
            ?? throw new NotFoundException(nameof(EventTrack), eventId);
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        if (ev.ScoringMode == EventScoringMode.CreatorVoteAverage)
            throw new ConflictException("Hoạt động này chỉ nhận bài dự thi từ Parent.");
        var now = DateTime.UtcNow;
        var submissionEnd = ev.SubmissionCutoffAt ?? track.ExecutionEndAt;
        if (now < track.ExecutionStartAt || now > submissionEnd)
            throw new ConflictException("Ngoài thời gian nộp bài dự thi của Creator.");

        var participation = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
            p => p.EventId == eventId && p.TrackId == track.Id && p.UserId == creatorUserId, cancellationToken: ct)
            ?? throw new ForbiddenException("Bạn chưa đăng ký tham gia sự kiện này.");

        if (ev.Status != EventStatus.Published || participation.Status == EventParticipationStatus.Disqualified)
            throw new ConflictException("Sự kiện hoặc đăng ký không còn hoạt động.");
        var story = await _uow.Repository<Story>().GetByIdAsync(request.StoryId, ct)
            ?? throw new NotFoundException(nameof(Story), request.StoryId);
        if (story.AuthorUserId != creatorUserId || story.Scope != StoryScope.Creator)
            throw new ForbiddenException("Bạn không phải tác giả Creator của truyện này.");

        var version = await _uow.Repository<StoryVersion>().FirstOrDefaultAsync(v => v.StoryId == story.Id && v.IsCurrent, cancellationToken: ct)
            ?? throw new BadRequestException("Truyện chưa có phiên bản nội dung.");

        var existingForUser = await _uow.Repository<EventSubmission>().FirstOrDefaultAsync(
            s => s.EventId == eventId && s.CreatorUserId == creatorUserId, cancellationToken: ct);
        if (existingForUser != null && existingForUser.StoryId != story.Id)
            throw new ConflictException("Mỗi Creator chỉ được nộp tối đa 1 truyện dự thi cho mỗi sự kiện.");
        if (existingForUser?.LockedStoryVersionId is int locked && locked != version.Id)
            throw new ConflictException("Bản dự thi đã được chốt, không thể thay bằng phiên bản khác.");

        // Quét trùng lặp văn bản với các bài dự thi khác trong cùng Event ngay khi nộp bài (Flow 03A.7)
        var otherSubmissions = await _uow.Repository<EventSubmission>().FindAsync(
            s => s.EventId == eventId && s.CreatorUserId != creatorUserId, cancellationToken: ct);
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

        if (existingForUser == null)
        {
            existingForUser = new EventSubmission
            {
                EventId = eventId,
                ParticipationId = participation.Id,
                CreatorUserId = creatorUserId,
                StoryId = story.Id,
                CurrentStoryVersionId = version.Id,
                SimilarityScore = Math.Round(maxSim, 4),
                SimilarityReferenceStoryId = refStoryId,
                Status = EventSubmissionStatus.PendingReview,
                CreatedAt = now,
                SubmittedAt = now
            };
            await _uow.Repository<EventSubmission>().AddAsync(existingForUser, ct);
        }
        else
        {
            existingForUser.CurrentStoryVersionId = version.Id;
            existingForUser.SimilarityScore = Math.Round(maxSim, 4);
            existingForUser.SimilarityReferenceStoryId = refStoryId;
            existingForUser.Status = EventSubmissionStatus.PendingReview;
            existingForUser.SubmittedAt = now;
            existingForUser.UpdatedAt = now;
            _uow.Repository<EventSubmission>().Update(existingForUser);
        }

        await _uow.SaveChangesAsync(ct);
        // Reuse the existing F-10 gate and quota charging instead of creating a parallel review workflow.
        var openReview = await _uow.Repository<ModerationItem>().FirstOrDefaultAsync(x => x.StoryId == story.Id
            && x.StoryVersionId == version.Id && x.ItemType == ModerationItemType.PublicStoryReview
            && (x.Status == ModerationItemStatus.Pending || x.Status == ModerationItemStatus.Claimed), cancellationToken: ct);
        if (story.IsPublished && version.Moderation.ReviewStatus == VersionReviewStatus.Approved)
        {
            await new EventIntegrationHooks(_uow).LinkApprovedStoryAsync(eventId, story.Id, version.Id, ct);
        }
        else if (openReview != null)
        {
            existingForUser.LatestModerationItemId = openReview.Id;
            _uow.Repository<EventSubmission>().Update(existingForUser);
            await _uow.SaveChangesAsync(ct);

            if (openReview.Status == ModerationItemStatus.Claimed)
            {
                await EventSubmissionModerationAccess.ReleaseUnauthorizedClaimedItemForVersionAsync(
                    _uow, _audit, creatorUserId, story.Id, version.Id, now, ct);
            }
        }
        else
        {
            if (_publicSubmission == null) throw new ConflictException("Dịch vụ gửi duyệt chưa khả dụng.");
            var labels = await _uow.Repository<StoryCategory>().FindAsync(x => x.StoryId == story.Id, cancellationToken: ct);
            var queued = await _publicSubmission.SubmitAsync(creatorUserId, new SubmitPublicStoryRequestDto
            {
                StoryId = story.Id, StoryVersionId = version.Id,
                ContentCategoryIds = labels.Count > 0 ? labels.Select(x => x.ContentCategoryId).ToList() : [ev.MainCategoryId]
            }, ct);
            if (queued != null)
            {
                existingForUser.LatestModerationItemId = queued.ItemId;
                _uow.Repository<EventSubmission>().Update(existingForUser);
                await _uow.SaveChangesAsync(ct);
            }
        }
        await _audit.LogAsync(creatorUserId, "EventStorySubmitted", nameof(EventSubmission), existingForUser.Id, null, new { story.Id, maxSim }, ct);

        return new EventSubmissionDto
        {
            Id = existingForUser.Id,
            EventId = existingForUser.EventId,
            StoryId = existingForUser.StoryId,
            CurrentStoryVersionId = existingForUser.CurrentStoryVersionId,
            LockedStoryVersionId = existingForUser.LockedStoryVersionId,
            SimilarityScore = existingForUser.SimilarityScore,
            SimilarityReferenceStoryId = existingForUser.SimilarityReferenceStoryId,
            Status = existingForUser.Status,
            SubmittedAt = existingForUser.SubmittedAt
        };
    }

    public async Task<EventMissionDashboardDto> GetMissionDashboardAsync(int userId, int eventId, CancellationToken ct = default)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            var activities = await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);
            var parts = await _uow.Repository<EventParticipation>().FindAsync(
                p => p.EventId == eventId && p.UserId == userId && p.EventActivityId.HasValue && !p.IsDeleted, cancellationToken: ct);
            var voting = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.Voting &&
                                parts.Any(p => p.EventActivityId == a.Id && p.Status != EventParticipationStatus.Disqualified));
            var targetAct = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.ReadingMissions &&
                                parts.Any(p => p.EventActivityId == a.Id))
                            ?? voting
                            ?? activities.FirstOrDefault(a => parts.Any(p => p.EventActivityId == a.Id))
                            ?? throw new NotFoundException(nameof(EventParticipation), eventId);
            var part = parts.First(p => p.EventActivityId == targetAct.Id);
            var dashboard = await BuildActivityDashboardAsync(ev, part, targetAct, ct);
            if (voting != null)
            {
                dashboard.VotingStories = targetAct.Id == voting.Id ? dashboard.Stories :
                    await LoadActivityStoriesAsync(ev, userId, voting, ct);
            }
            return dashboard;
        }

        var user = await _uow.Repository<UserAccount>().GetByIdAsync(userId, ct);
        var preferredTrack = user?.Role == UserRole.ContentCreator ? EventTrackType.Creator : EventTrackType.Parent;
        var track = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(t => t.EventId == eventId && t.TrackType == preferredTrack, cancellationToken: ct)
            ?? await _uow.Repository<EventTrack>().FirstOrDefaultAsync(t => t.EventId == eventId, cancellationToken: ct)
            ?? throw new NotFoundException(nameof(EventTrack), eventId);
        var participation = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
            p => p.EventId == eventId && p.TrackId == track.Id && p.UserId == userId, cancellationToken: ct)
            ?? throw new NotFoundException(nameof(EventParticipation), eventId);

        return await BuildDashboardAsync(participation, track, ct);
    }

    public async Task<EventMissionDashboardDto> RecordTaskProgressAsync(int userId, int eventId, int taskDefinitionId, CompleteEventTaskRequestDto request, CancellationToken ct = default)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        var now = DateTime.UtcNow;
        EventTrack? track = null;
        EventActivity? readingAct = null;
        EventParticipation participation;

        if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            readingAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                a => a.EventId == eventId && a.ActivityType == EventActivityType.ReadingMissions, cancellationToken: ct)
                ?? throw new NotFoundException(nameof(EventActivity), eventId);
            if (now < readingAct.StartAt || now > readingAct.EndAt)
                throw new ConflictException("Ngoài thời gian thực hiện nhiệm vụ của sự kiện.");
            participation = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
                p => p.EventId == eventId && p.EventActivityId == readingAct.Id && p.UserId == userId, cancellationToken: ct)
                ?? throw new NotFoundException(nameof(EventParticipation), eventId);
        }
        else
        {
            track = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(t => t.EventId == eventId && t.TrackType == EventTrackType.Parent, cancellationToken: ct)
                ?? throw new NotFoundException(nameof(EventTrack), eventId);
            if (now < track.ExecutionStartAt || now > track.ExecutionEndAt)
                throw new ConflictException("Ngoài thời gian thực hiện nhiệm vụ của sự kiện.");
            participation = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
                p => p.EventId == eventId && p.TrackId == track.Id && p.UserId == userId, cancellationToken: ct)
                ?? throw new NotFoundException(nameof(EventParticipation), eventId);
        }

        var task = await _uow.Repository<EventTaskDefinition>().GetByIdAsync(taskDefinitionId, ct)
            ?? throw new NotFoundException(nameof(EventTaskDefinition), taskDefinitionId);
        if (task.EventRuleVersionId != participation.AcceptedRuleVersionId)
            throw new BadRequestException("Nhiệm vụ không thuộc phiên bản thể lệ bạn đã đăng ký.");
        if (ev.Status != EventStatus.Published || participation.Status == EventParticipationStatus.Disqualified)
            throw new ConflictException("Sự kiện hoặc đăng ký không còn hoạt động.");
        if (task.VerifierType is not (TaskVerifierType.GuidedDiscussionCheckin or TaskVerifierType.RealLifePracticeCheckin))
            throw new BadRequestException("Nhiệm vụ này được xác minh từ hoạt động thực tế, không thể tự xác nhận.");
        if (!request.StoryId.HasValue || string.IsNullOrWhiteSpace(request.SelfDeclaredPayloadJson))
            throw new BadRequestException("Hãy chọn truyện và xác nhận hoạt động đã thực hiện.");

        if (request.ChildProfileId.HasValue)
        {
            var child = await _uow.Repository<ChildProfile>().GetByIdAsync(request.ChildProfileId.Value, ct)
                ?? throw new NotFoundException(nameof(ChildProfile), request.ChildProfileId.Value);
            if (child.OwnerUserId != userId || child.IsDeleted)
                throw new ForbiddenException("Hồ sơ trẻ không thuộc quyền quản lý của bạn (BR-CP02).");
            if (_readingSlots != null)
            {
                var readable = await _readingSlots.GetReadableProfileIdsAsync(userId, ct);
                if (!readable.Contains(child.Id))
                    throw new ForbiddenException("Hồ sơ trẻ hiện không nằm trong suất đọc hoạt động.");
            }
        }

        if (request.StoryId.HasValue)
        {
            var inEvent = await _uow.Repository<EventStory>().ExistsAsync(es => es.EventId == eventId && es.StoryId == request.StoryId.Value && es.IsActive, ct);
            if (!inEvent) throw new BadRequestException("Truyện không thuộc danh sách truyện của sự kiện.");
            var filter = EventTaskFilters.Parse(task.ConfigJson);
            var categories = filter.CategoryIds is not null
                ? await EventTaskFilters.LoadCategoriesAsync(_uow, [request.StoryId.Value], ct)
                : EventTaskFilters.NoCategories;
            if (!filter.Includes(request.StoryId.Value, categories))
                throw new BadRequestException("Truyện không thuộc phạm vi của nhiệm vụ này.");
        }

        var localDate = DateOnly.FromDateTime(VietnamTime.LocalDate(now));
        await _uow.Repository<EventTaskProgress>().AddAsync(new EventTaskProgress
        {
            ParticipationId = participation.Id,
            TaskDefinitionId = task.Id,
            ChildProfileId = request.ChildProfileId,
            StoryId = request.StoryId,
            SelfDeclaredPayloadJson = request.SelfDeclaredPayloadJson,
            ActivityLocalDate = localDate,
            CompletedAt = now
        }, ct);

        await _uow.SaveChangesAsync(ct);

        var dashboard = ev.ConfigVersion == EventConfigVersion.ActivityBased
            ? await BuildActivityDashboardAsync(ev, participation, readingAct!, ct)
            : await BuildDashboardAsync(participation, track!, ct);
        if (dashboard.AllRequirementsMet && participation.Status == EventParticipationStatus.Registered)
        {
            participation.Status = EventParticipationStatus.MissionCompleted;
            participation.MissionCompletedAt = now;
            participation.UpdatedAt = now;
            _uow.Repository<EventParticipation>().Update(participation);
            await _uow.SaveChangesAsync(ct);
            dashboard.Participation = ev.ConfigVersion == EventConfigVersion.ActivityBased
                ? MapActivityParticipation(participation, readingAct!.ActivityType, UserRole.User, null)
                : MapParticipation(participation, track!.TrackType, null);
        }

        return dashboard;
    }

    public Task<int> VerifyEventParticipationsAsync(int actorUserId, int eventId, CancellationToken ct = default) =>
        InEventResultTransactionAsync(eventId, async () =>
        {
            var campaign = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            await EventModeratorAccess.RequireOperatorAsync(_uow, campaign, actorUserId, ct);
            return await VerifyEventParticipationsCoreLockedAsync(actorUserId, eventId, ct);
        }, ct);

    internal async Task<int> VerifyEventParticipationsCoreLockedAsync(int? actorUserId, int eventId, CancellationToken ct)
    {
        var campaign = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == eventId, cancellationToken: ct);
        var activities = campaign.ConfigVersion == EventConfigVersion.ActivityBased
            ? await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == eventId, cancellationToken: ct)
            : Array.Empty<EventActivity>();
        EventExecution.EnsureEnded(campaign, tracks, activities);
        EnsureResultsEditable(campaign);
        if (campaign.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            var expiredReviews = await _uow.Repository<EventSubmission>().FindAsync(
                s => s.EventId == eventId && s.Status == EventSubmissionStatus.PendingReview, cancellationToken: ct);
            foreach (var submission in expiredReviews)
            {
                submission.Status = EventSubmissionStatus.ReviewExpired;
                submission.UpdatedAt = DateTime.UtcNow;
                _uow.Repository<EventSubmission>().Update(submission);
            }
            if (expiredReviews.Count > 0)
                await _audit.LogAsync(actorUserId, "EventSubmissionReviewsExpired", nameof(Event), eventId,
                    null, new { Count = expiredReviews.Count }, ct);
        }
        var participations = await _uow.Repository<EventParticipation>().FindAsync(p => p.EventId == eventId, cancellationToken: ct);
        var acceptedAppeals = await _uow.Repository<EventAppeal>().FindAsync(
            a => a.EventId == eventId && a.Status == EventAppealStatus.Accepted && !a.IsDeleted, cancellationToken: ct);
        var verifiedByAppeal = acceptedAppeals.Select(a => a.ParticipationId).ToHashSet();
        var verifiedCount = 0;

        foreach (var p in participations)
        {
            var previouslyFailed = p.Status == EventParticipationStatus.VerificationFailed;
            if (p.Status is EventParticipationStatus.Disqualified or EventParticipationStatus.ManualReview) continue;
            if (p.Status == EventParticipationStatus.Verified && verifiedByAppeal.Contains(p.Id))
            {
                verifiedCount++;
                continue;
            }

            if (campaign.ConfigVersion == EventConfigVersion.ActivityBased)
            {
                var act = activities.FirstOrDefault(a => a.Id == p.EventActivityId);
                if (act == null) continue;
                if (act.ActivityType == EventActivityType.Submission)
                {
                    var approvedSubs = await _uow.Repository<EventSubmission>().FindAsync(
                        s => s.ParticipationId == p.Id && s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId != null,
                        cancellationToken: ct);
                    var ok = approvedSubs.Count > 0;
                    if (ok)
                        p.MissionCompletedAt ??= approvedSubs.Min(s => s.UpdatedAt ?? s.SubmittedAt ?? s.CreatedAt);
                    p.Status = ok ? EventParticipationStatus.Verified : EventParticipationStatus.VerificationFailed;
                    p.VerifiedAt = previouslyFailed && p.Status == EventParticipationStatus.VerificationFailed
                        ? p.VerifiedAt ?? DateTime.UtcNow : DateTime.UtcNow;
                    p.VerificationNotesJson = JsonSerializer.Serialize(new { ok, reason = ok ? null : "Chưa có bài dự thi đạt kiểm duyệt." });
                }
                else if (act.ActivityType == EventActivityType.ReadingMissions)
                {
                    var dashboard = await BuildActivityDashboardAsync(campaign, p, act, ct);
                    p.Status = dashboard.AllRequirementsMet ? EventParticipationStatus.Verified : EventParticipationStatus.VerificationFailed;
                    p.VerifiedAt = previouslyFailed && p.Status == EventParticipationStatus.VerificationFailed
                        ? p.VerifiedAt ?? DateTime.UtcNow : DateTime.UtcNow;
                    if (dashboard.AllRequirementsMet) p.MissionCompletedAt ??= p.VerifiedAt;
                    p.VerificationNotesJson = JsonSerializer.Serialize(new
                    {
                        dashboard.AllRequirementsMet,
                        dashboard.CompletedTasksCount,
                        dashboard.EarnedPoints,
                        failedTasks = dashboard.Tasks.Where(t => !t.IsCompleted).Select(t => new { t.TaskCode, t.Title, t.CurrentValue, t.TargetValue })
                    });
                }
                else if (act.ActivityType == EventActivityType.Voting)
                {
                    var hasVote = await _uow.Repository<EventVote>().ExistsAsync(
                        v => v.EventId == eventId && v.ParentUserId == p.UserId && !v.IsDisqualified && !v.IsDeleted, ct);
                    p.Status = hasVote ? EventParticipationStatus.Verified : EventParticipationStatus.VerificationFailed;
                    p.VerifiedAt = previouslyFailed && p.Status == EventParticipationStatus.VerificationFailed
                        ? p.VerifiedAt ?? DateTime.UtcNow : DateTime.UtcNow;
                    p.VerificationNotesJson = JsonSerializer.Serialize(new { hasVote });
                }

                if (p.Status == EventParticipationStatus.Verified) verifiedCount++;
                p.UpdatedAt = DateTime.UtcNow;
                _uow.Repository<EventParticipation>().Update(p);
                continue;
            }

            var track = tracks.FirstOrDefault(t => t.Id == p.TrackId);
            if (track == null) continue;

            if (track.TrackType == EventTrackType.Creator)
            {
                var sub = await _uow.Repository<EventSubmission>().FirstOrDefaultAsync(s => s.ParticipationId == p.Id, cancellationToken: ct);
                var ok = sub != null && sub.Status == EventSubmissionStatus.Approved && sub.LockedStoryVersionId.HasValue;
                if (ok || EventVoting.AllowsVoter(campaign.ScoringMode, EventTrackType.Creator))
                {
                    var dashboard = await BuildDashboardAsync(p, track, ct);
                    ok = dashboard.Tasks.Count == 0 ? ok : dashboard.AllRequirementsMet;
                    if (ok && campaign.ScoringMode == EventScoringMode.MissionCompletion)
                        p.MissionCompletedAt ??= sub!.UpdatedAt ?? sub.SubmittedAt ?? sub.CreatedAt;
                }
                p.Status = ok ? EventParticipationStatus.Verified : EventParticipationStatus.VerificationFailed;
                p.VerifiedAt = previouslyFailed && p.Status == EventParticipationStatus.VerificationFailed
                        ? p.VerifiedAt ?? DateTime.UtcNow : DateTime.UtcNow;
                p.VerificationNotesJson = JsonSerializer.Serialize(new { ok, reason = ok ? null : "Chưa có bài dự thi đạt kiểm duyệt F-10." });
            }
            else
            {
                var dashboard = await BuildDashboardAsync(p, track, ct);
                p.Status = dashboard.AllRequirementsMet ? EventParticipationStatus.Verified : EventParticipationStatus.VerificationFailed;
                p.VerifiedAt = previouslyFailed && p.Status == EventParticipationStatus.VerificationFailed
                        ? p.VerifiedAt ?? DateTime.UtcNow : DateTime.UtcNow;
                p.VerificationNotesJson = JsonSerializer.Serialize(new
                {
                    dashboard.AllRequirementsMet,
                    dashboard.CompletedTasksCount,
                    dashboard.EarnedPoints,
                    failedTasks = dashboard.Tasks.Where(t => !t.IsCompleted).Select(t => new { t.TaskCode, t.Title, t.CurrentValue, t.TargetValue })
                });
            }

            if (p.Status == EventParticipationStatus.Verified) verifiedCount++;
            p.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<EventParticipation>().Update(p);
        }

        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "EventParticipationsVerified", nameof(Event), eventId, null, new { verifiedCount }, ct);
        return verifiedCount;
    }

    public Task<EventAppealDto> CreateAppealAsync(int userId, int eventId, CreateAppealRequestDto request, CancellationToken ct = default) =>
        InEventResultTransactionAsync(eventId, () => CreateAppealLockedAsync(userId, eventId, request, ct), ct);

    private async Task<EventAppealDto> CreateAppealLockedAsync(int userId, int eventId, CreateAppealRequestDto request, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);

        var participation = await _uow.Repository<EventParticipation>().GetByIdAsync(request.ParticipationId, ct)
            ?? throw new NotFoundException(nameof(EventParticipation), request.ParticipationId);
        if (participation.UserId != userId || participation.EventId != eventId)
            throw new ForbiddenException("Bạn không có quyền khiếu nại hồ sơ này (BR-AP01).");
        if (string.IsNullOrWhiteSpace(request.ReasonText))
            throw new BadRequestException("Hãy nhập lý do khiếu nại.");

        EventAward? targetAward = null;
        if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            if (ev.Status is not (EventStatus.Published or EventStatus.Paused) || !ev.ResultsPublishedAt.HasValue)
                throw new ConflictException("Chỉ có thể gửi khiếu nại sau khi kết quả sự kiện được công bố.");
            var deadline = ev.ResultsPublishedAt.Value.AddHours(EventAppealWindow.Hours);
            if (DateTime.UtcNow >= deadline)
                throw new ConflictException($"Đã quá hạn {EventAppealWindow.Hours} giờ để gửi khiếu nại.");

            var activities = await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == eventId && !a.IsDeleted, cancellationToken: ct);
            var eventAwards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId && !a.IsDeleted, cancellationToken: ct);
            var sourceActIds = eventAwards
                .Select(a => EventResultsQueryService.ResolveSourceActivityId(a, activities))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();
            if (!participation.EventActivityId.HasValue || !sourceActIds.Contains(participation.EventActivityId.Value))
                throw new ConflictException("Hoạt động này không thuộc nguồn xét giải của sự kiện.");

            var userParts = await _uow.Repository<EventParticipation>().FindAsync(
                p => p.EventId == eventId && p.UserId == userId && !p.IsDeleted, cancellationToken: ct);
            var userPartIds = userParts.Select(p => p.Id).ToHashSet();
            var holdsAward = eventAwards.Any(a =>
                a.ParticipationId.HasValue && userPartIds.Contains(a.ParticipationId.Value) &&
                a.Status is EventAwardStatus.AppealHold or EventAwardStatus.AwaitingClaim or
                    EventAwardStatus.Claimed or EventAwardStatus.FulfillmentProcessing or EventAwardStatus.Fulfilled);
            if (holdsAward)
                throw new ConflictException("Người đang giữ suất giải không thể gửi khiếu nại.");

            var kind = Enum.IsDefined(request.Kind) ? request.Kind : EventAppealKind.Verification;
            if (kind == EventAppealKind.Verification)
            {
                if (participation.Status != EventParticipationStatus.VerificationFailed)
                    throw new ConflictException("Chỉ có thể khiếu nại kết quả xác minh khi hồ sơ không đạt.");
            }
            else
            {
                if (participation.Status != EventParticipationStatus.Verified)
                    throw new ConflictException("Chỉ hồ sơ đã xác minh đạt mới có thể khiếu nại thứ hạng hoặc người nhận giải.");
                if (request.TargetAwardId.HasValue)
                {
                    targetAward = eventAwards.FirstOrDefault(a => a.Id == request.TargetAwardId.Value)
                        ?? throw new BadRequestException("Suất giải bị khiếu nại không thuộc sự kiện này.");
                }
            }
        }
        else
        {
            EnsureResultsEditable(ev);
            if (ev.Status != EventStatus.Published || participation.Status != EventParticipationStatus.VerificationFailed)
                throw new ConflictException("Chỉ có thể khiếu nại khi hồ sơ đã có kết quả xác minh không đạt.");
            if (EventAppealWindow.DeadlineFor(participation) is { } deadline && DateTime.UtcNow >= deadline)
                throw new ConflictException($"Đã quá hạn {EventAppealWindow.Hours} giờ để gửi khiếu nại.");
        }

        // BR-AP07: Không cho phép Appeal ghi đè quyết định Hard Safety Block của F-10
        var blockedSubmission = await _uow.Repository<EventSubmission>().ExistsAsync(
            s => s.ParticipationId == participation.Id && s.Status == EventSubmissionStatus.Rejected, ct);
        if (blockedSubmission)
            throw new ForbiddenException("Không hỗ trợ khiếu nại đối với bài dự thi bị từ chối ở kiểm duyệt an toàn F-10 (BR-AP07).");

        var hasActive = await _uow.Repository<EventAppeal>().ExistsAsync(
            a => a.ParticipationId == participation.Id && (a.Status == EventAppealStatus.Open || a.Status == EventAppealStatus.UnderReview || a.Status == EventAppealStatus.Escalated), ct);
        if (hasActive)
            throw new ConflictException("Đã có một đơn khiếu nại đang mở cho hồ sơ này (BR-AP03).");

        var appealKind = ev.ConfigVersion == EventConfigVersion.ActivityBased && Enum.IsDefined(request.Kind)
            ? request.Kind
            : EventAppealKind.Verification;

        var appeal = new EventAppeal
        {
            EventId = eventId,
            ParticipationId = participation.Id,
            AppellantUserId = userId,
            ReasonText = request.ReasonText.Trim(),
            EvidenceJson = request.EvidenceJson,
            Kind = appealKind,
            TargetAwardId = appealKind == EventAppealKind.Ranking ? request.TargetAwardId : null,
            Status = EventAppealStatus.Open
        };
        await _uow.Repository<EventAppeal>().AddAsync(appeal, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(userId, "EventAppealCreated", nameof(EventAppeal), appeal.Id, null,
            new { participation.Id, appeal.Kind, appeal.TargetAwardId }, ct);

        return MapAppeal(appeal, targetAward);
    }

    public async Task<IReadOnlyList<EventAppealDto>> ListAppealsAsync(int eventId, CancellationToken ct = default)
    {
        var list = await _uow.Repository<EventAppeal>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);
        var awardIds = list.Where(a => a.TargetAwardId.HasValue).Select(a => a.TargetAwardId!.Value).Distinct().ToList();
        var awards = awardIds.Count == 0
            ? new Dictionary<int, EventAward>()
            : (await _uow.Repository<EventAward>().FindAsync(a => awardIds.Contains(a.Id), cancellationToken: ct))
                .ToDictionary(a => a.Id);
        return list.OrderByDescending(a => a.CreatedAt)
            .Select(a => MapAppeal(a, a.TargetAwardId.HasValue ? awards.GetValueOrDefault(a.TargetAwardId.Value) : null))
            .ToList();
    }

    public async Task<EventAppealDto> DecideAppealAsync(int actorUserId, int appealId, DecideAppealRequestDto request, CancellationToken ct = default)
    {
        var appeal = await _uow.Repository<EventAppeal>().FirstOrDefaultAsync(a => a.Id == appealId, cancellationToken: ct)
            ?? throw new NotFoundException(nameof(EventAppeal), appealId);
        var (dto, notifyEventTitle) = await InEventResultTransactionAsync(appeal.EventId,
            () => DecideAppealLockedAsync(actorUserId, appealId, request, ct), ct);

        if (notifyEventTitle != null && _notifications != null)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    eventId = dto.EventId,
                    eventTitle = notifyEventTitle,
                    appealId = dto.Id,
                    resolutionNote = dto.ResolutionNote
                });
                await _notifications.CreateAsync(dto.AppellantUserId, NotificationType.EventAppealAccepted, payload, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _uow.DiscardTrackedChanges();
            }
        }

        return dto;
    }

    private async Task<(EventAppealDto Dto, string? NotifyEventTitle)> DecideAppealLockedAsync(
        int actorUserId, int appealId, DecideAppealRequestDto request, CancellationToken ct)
    {
        var appeal = await _uow.Repository<EventAppeal>().GetByIdAsync(appealId, ct)
            ?? throw new NotFoundException(nameof(EventAppeal), appealId);
        var campaign = await _uow.Repository<Event>().GetByIdAsync(appeal.EventId, ct)
            ?? throw new NotFoundException(nameof(Event), appeal.EventId);
        await EventModeratorAccess.RequireOperatorAsync(_uow, campaign, actorUserId, ct);
        if (campaign.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            if (campaign.Status is EventStatus.Closed or EventStatus.Archived)
                throw new ConflictException("Sự kiện đã đóng, không thể xử lý khiếu nại.");
        }
        else
        {
            EnsureResultsEditable(campaign);
        }
        if (appeal.Status is EventAppealStatus.Accepted or EventAppealStatus.Rejected)
            throw new ConflictException("Khiếu nại này đã có kết luận.");
        if (request.Decision is not (EventAppealStatus.Accepted or EventAppealStatus.Rejected or EventAppealStatus.UnderReview or EventAppealStatus.Escalated))
            throw new BadRequestException("Quyết định xử lý khiếu nại không hợp lệ.");
        if (appeal.Kind == EventAppealKind.Ranking && request.Decision == EventAppealStatus.Accepted)
            throw new ConflictException("Hãy chấp nhận đơn khiếu nại xếp hạng bằng cách thu hồi suất giải và gắn đơn vào thao tác đó.");
        if (string.IsNullOrWhiteSpace(request.ResolutionNote))
            throw new BadRequestException("Phải nhập lý do kết luận xử lý khiếu nại (BR-AP05).");

        appeal.Status = request.Decision;
        appeal.ReviewedByUserId = actorUserId;
        appeal.ResolutionNote = request.ResolutionNote.Trim();
        appeal.ResolvedAt = DateTime.UtcNow;
        appeal.UpdatedAt = appeal.ResolvedAt;
        _uow.Repository<EventAppeal>().Update(appeal);

        if (request.Decision == EventAppealStatus.Accepted && appeal.Kind == EventAppealKind.Verification)
        {
            var p = await _uow.Repository<EventParticipation>().GetByIdAsync(appeal.ParticipationId, ct);
            if (p != null)
            {
                p.Status = EventParticipationStatus.Verified;
                p.VerifiedAt = DateTime.UtcNow;
                p.UpdatedAt = p.VerifiedAt;
                _uow.Repository<EventParticipation>().Update(p);
            }
        }

        await _uow.SaveChangesAsync(ct);

        if (request.Decision == EventAppealStatus.Accepted &&
            campaign.ConfigVersion == EventConfigVersion.ActivityBased &&
            !campaign.ResultsFinalizedAt.HasValue)
        {
            var activities = await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == campaign.Id, cancellationToken: ct);
            if (EventExecution.HasEnded(campaign, Array.Empty<EventTrack>(), activities))
            {
                await new EventRewardService(_uow, _audit).RecalculateActivityProposedAwardsInTransactionAsync(campaign, activities, ct);
                await _uow.SaveChangesAsync(ct);
            }
        }

        EventAward? targetAward = appeal.TargetAwardId.HasValue
            ? await _uow.Repository<EventAward>().GetByIdAsync(appeal.TargetAwardId.Value, ct)
            : null;
        await _audit.LogAsync(actorUserId, "EventAppealDecided", nameof(EventAppeal), appeal.Id, null, new { appeal.Status, appeal.ResolutionNote }, ct);
        return (MapAppeal(appeal, targetAward), request.Decision == EventAppealStatus.Accepted ? campaign.Title : null);
    }

    private async Task<EventMissionDashboardDto> BuildDashboardAsync(EventParticipation participation, EventTrack track, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(participation.EventId, ct);
        var rule = await _uow.Repository<EventRuleVersion>().GetByIdAsync(participation.AcceptedRuleVersionId, ct)
            ?? throw new NotFoundException(nameof(EventRuleVersion), participation.AcceptedRuleVersionId);
        var allDefinitions = await _uow.Repository<EventTaskDefinition>().FindAsync(t => t.EventRuleVersionId == rule.Id, cancellationToken: ct);
        var definitions = allDefinitions
            .Where(d => d.TargetTrackType == track.TrackType || d.TargetTrackType == null)
            .ToList();

        IReadOnlyList<EventTaskProgress> progresses;
        if (track.TrackType == EventTrackType.Creator)
        {
            var sub = await _uow.Repository<EventSubmission>().FirstOrDefaultAsync(
                s => s.ParticipationId == participation.Id && s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId != null,
                cancellationToken: ct);
            var list = new List<EventTaskProgress>();
            if (sub != null)
            {
                foreach (var d in definitions.Where(x => x.VerifierType == TaskVerifierType.SubmitApprovedStory))
                {
                    list.Add(new EventTaskProgress
                    {
                        ParticipationId = participation.Id,
                        TaskDefinitionId = d.Id,
                        StoryId = sub.StoryId,
                        CompletedAt = sub.UpdatedAt ?? sub.SubmittedAt ?? sub.CreatedAt
                    });
                }
            }
            progresses = list;
        }
        else
        {
            progresses = await EventActivityEvidence.LoadAsync(_uow, participation, track, definitions, ct);
        }

        if (track.TrackType == EventTrackType.Creator && definitions.Any(d => d.VerifierType == TaskVerifierType.VoteEventStories))
        {
            var voteTasks = definitions.Where(d => d.VerifierType == TaskVerifierType.VoteEventStories).ToList();
            progresses = progresses.Concat(await EventActivityEvidence.LoadAsync(_uow, participation, track, voteTasks, ct)).ToList();
        }
        var summary = EventMissionEvaluator.Evaluate(rule, definitions, progresses);

        var storyItems = new List<EventStoryLibraryItemDto>();
        if (track.TrackType == EventTrackType.Parent)
        {
            var now = DateTime.UtcNow;
            var votingOpen = ev == null || ev.Format == EventFormat.Standard || now >= (ev.VotingStartAt ?? track.ExecutionStartAt);
            if (votingOpen)
            {
                var eventStories = await _uow.Repository<EventStory>().FindAsync(es => es.EventId == participation.EventId && es.IsActive && es.Source != EventStorySource.ParentSubmission, cancellationToken: ct);
                var children = await _uow.Repository<ChildProfile>().FindAsync(c => c.OwnerUserId == participation.UserId && !c.IsDeleted, cancellationToken: ct);
                var childIds = children.Select(c => c.Id).ToHashSet();
                var childAgeBands = children.Select(c => c.AgeBand.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var storyIds = eventStories.Select(es => es.StoryId).Distinct().ToList();
                var sessions = childIds.Count == 0 || storyIds.Count == 0
                    ? Array.Empty<ReadingSession>()
                    : await _uow.Repository<ReadingSession>().FindAsync(
                        s => childIds.Contains(s.ChildProfileId) && storyIds.Contains(s.StoryId) && s.Status == SessionStatus.Completed,
                        cancellationToken: ct);
                var myVotes = (await _uow.Repository<EventVote>().FindAsync(
                    v => v.EventId == participation.EventId && v.ParentUserId == participation.UserId && !v.IsDeleted,
                    cancellationToken: ct)).ToDictionary(v => v.StoryId);

                var rawItems = new List<(EventStoryLibraryItemDto Item, bool AgeMatch, int RotationKey)>();
                var daySeed = VietnamTime.LocalDate(now).DayOfYear;
                foreach (var es in eventStories)
                {
                    var story = await _uow.Repository<Story>().GetByIdAsync(es.StoryId, ct);
                    if (story == null || !story.IsPublished) continue;
                    var hasRead = sessions.Any(s => s.StoryId == es.StoryId && s.StoryVersionId == es.StoryVersionId);
                    myVotes.TryGetValue(es.StoryId, out var vote);
                    var rotationKey = HashCode.Combine(participation.UserId, participation.EventId, daySeed, es.StoryId);
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
                    }, childAgeBands.Contains(story.AgeBand), rotationKey));
                }

                storyItems = rawItems
                    .OrderByDescending(x => x.AgeMatch)
                    .ThenBy(x => x.Item.HasFamilyRead)
                    .ThenBy(x => x.RotationKey)
                    .Select(x => x.Item)
                    .ToList();
            }
        }

        if (track.TrackType == EventTrackType.Creator && ev != null)
            storyItems = await LoadCreatorVotingStoriesAsync(ev, participation, ct);

        return new EventMissionDashboardDto
        {
            Participation = MapParticipation(participation, track.TrackType, null),
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

    public async Task<IReadOnlyList<EventSubmissionDto>> ListSubmissionsAsync(int eventId, CancellationToken ct = default)
    {
        var items = await _uow.Repository<EventSubmission>().FindAsync(s => s.EventId == eventId, cancellationToken: ct);
        var votes = await _uow.Repository<EventVote>().FindAsync(v => v.EventId == eventId && !v.IsDisqualified && !v.IsDeleted, cancellationToken: ct);
        var result = new List<EventSubmissionDto>(items.Count);
        foreach (var s in items.OrderByDescending(x => x.Id))
        {
            var evals = await _uow.Repository<EventEvaluation>().FindAsync(e => e.EventSubmissionId == s.Id, cancellationToken: ct);
            var subVotes = votes.Where(v => v.EventSubmissionId == s.Id && v.StoryVersionId == s.LockedStoryVersionId).ToList();
            var dto = MapSubmission(s, evals, subVotes);
            dto.SubmittedTitle = s.ParentSubmissionTitle;
            dto.SubmittedText = s.ParentSubmissionText;
            result.Add(dto);
        }
        return result;
    }

    public async Task<EventSubmissionDto?> GetMySubmissionAsync(int creatorUserId, int eventId, CancellationToken ct = default)
    {
        var s = await _uow.Repository<EventSubmission>().FirstOrDefaultAsync(
            x => x.EventId == eventId && x.CreatorUserId == creatorUserId, cancellationToken: ct);
        if (s == null) return null;
        var evals = await _uow.Repository<EventEvaluation>().FindAsync(e => e.EventSubmissionId == s.Id, cancellationToken: ct);
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct);
        if (ev != null && EventVoting.IsEnabled(ev.ScoringMode))
        {
            var voting = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(
                t => t.EventId == eventId && t.TrackType == EventTrackType.Parent, cancellationToken: ct);
            var voteEnd = s.ParentSubmissionText != null
                ? EventVoting.CreatorSchedule(ev)?.VotingEndAt
                : ev.VotingEndAt ?? voting?.ExecutionEndAt;
            if (voteEnd.HasValue && DateTime.UtcNow <= voteEnd.Value)
                return MapSubmission(s, evals);
        }
        var subVotes = await _uow.Repository<EventVote>().FindAsync(
            v => v.EventId == eventId && v.EventSubmissionId == s.Id && v.StoryVersionId == s.LockedStoryVersionId &&
                 !v.IsDisqualified && !v.IsDeleted, cancellationToken: ct);
        return MapSubmission(s, evals, subVotes);
    }

    public async Task<EventParticipationDto?> GetMyParticipationAsync(
        int userId, int eventId, EventTrackType trackType, CancellationToken ct = default)
    {
        var track = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(
            t => t.EventId == eventId && t.TrackType == trackType, cancellationToken: ct);
        if (track == null) return null;

        var p = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
            x => x.EventId == eventId && x.TrackId == track.Id && x.UserId == userId, cancellationToken: ct);
        return p == null ? null : MapParticipation(p, trackType, null);
    }

    public async Task<IReadOnlyList<EventParticipationDto>> ListMyParticipationsAsync(int userId, CancellationToken ct = default)
    {
        var list = await _uow.Repository<EventParticipation>().FindAsync(p => p.UserId == userId, cancellationToken: ct);
        if (list.Count == 0) return Array.Empty<EventParticipationDto>();

        var user = await _uow.Repository<UserAccount>().GetByIdAsync(userId, ct);
        var role = user?.Role ?? UserRole.User;
        var trackIds = list.Where(p => p.TrackId.HasValue).Select(p => p.TrackId!.Value).Distinct().ToList();
        var tracks = trackIds.Count == 0
            ? new Dictionary<int, EventTrackType>()
            : (await _uow.Repository<EventTrack>().FindAsync(t => trackIds.Contains(t.Id), cancellationToken: ct))
                .ToDictionary(t => t.Id, t => t.TrackType);
        var actIds = list.Where(p => p.EventActivityId.HasValue).Select(p => p.EventActivityId!.Value).Distinct().ToList();
        var acts = actIds.Count == 0
            ? new Dictionary<int, EventActivityType>()
            : (await _uow.Repository<EventActivity>().FindAsync(a => actIds.Contains(a.Id), cancellationToken: ct))
                .ToDictionary(a => a.Id, a => a.ActivityType);

        return list.OrderByDescending(p => p.Id)
            .Select(p => p.EventActivityId.HasValue && acts.TryGetValue(p.EventActivityId.Value, out var at)
                ? MapActivityParticipation(p, at, role, null)
                : MapParticipation(p, p.TrackId.HasValue && tracks.TryGetValue(p.TrackId.Value, out var tt) ? tt : EventTrackType.Parent, null))
            .ToList();
    }

    private static EventSubmissionDto MapSubmission(EventSubmission s, IReadOnlyList<EventEvaluation> evals, IReadOnlyList<EventVote>? votes = null) => new()
    {
        Id = s.Id,
        EventId = s.EventId,
        EventActivityId = s.EventActivityId,
        AuthorUserId = s.CreatorUserId,
        StoryId = s.StoryId,
        CurrentStoryVersionId = s.CurrentStoryVersionId,
        LockedStoryVersionId = s.LockedStoryVersionId,
        SubmittedTitle = s.ParentSubmissionTitle,
        SimilarityScore = s.SimilarityScore,
        SimilarityReferenceStoryId = s.SimilarityReferenceStoryId,
        Status = s.Status,
        SubmittedAt = s.SubmittedAt,
        EvaluationCount = evals.Count,
        AverageScore = evals.Count == 0 ? null : Math.Round(evals.Average(e => e.TotalWeightedScore), 2),
        ValidVoteCount = votes?.Count ?? 0,
        AverageStars = votes is { Count: > 0 } ? Math.Round((decimal)votes.Average(v => v.Stars), 2) : null
    };

    private static EventParticipationDto MapParticipation(EventParticipation p, EventTrackType trackType, string? safetyWarning) => new()
    {
        Id = p.Id,
        EventId = p.EventId,
        TrackId = p.TrackId,
        TrackType = trackType,
        EventActivityId = p.EventActivityId,
        UserId = p.UserId,
        AcceptedRuleVersionId = p.AcceptedRuleVersionId,
        Status = p.Status,
        MissionCompletedAt = p.MissionCompletedAt,
        VerifiedAt = p.VerifiedAt,
        AppealDeadlineAt = EventAppealWindow.DeadlineFor(p),
        VerificationNotesJson = p.VerificationNotesJson,
        SafetyWarning = safetyWarning
    };

    public async Task<EventVoteDto> CastVoteAsync(int parentUserId, int eventId, CastEventVoteRequestDto request, CancellationToken ct = default)
    {
        if (request.Stars is < 1 or > 5)
            throw new BadRequestException("Điểm bình chọn phải từ 1 đến 5 sao.");
        if (!string.IsNullOrWhiteSpace(request.Comment) && request.Comment.Trim().Length > 2000)
            throw new BadRequestException("Nhận xét không được vượt quá 2000 ký tự.");

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(eventId, ct);
            var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
                return await CastActivityVoteLockedAsync(parentUserId, ev, request, ct);

            if (ev.Status != EventStatus.Published || ev.Format != EventFormat.SharedCreatorParent || ev.ResultsFinalizedAt.HasValue)
                throw new ConflictException("Sự kiện hiện không mở bình chọn.");

            var eventStory = await _uow.Repository<EventStory>().FirstOrDefaultAsync(
                es => es.EventId == eventId && es.StoryId == request.StoryId && es.IsActive, cancellationToken: ct)
                ?? throw new BadRequestException("Truyện không thuộc danh sách dự thi của sự kiện.");
            var fromParent = eventStory.Source == EventStorySource.ParentSubmission;
            var voterTrack = fromParent ? EventTrackType.Creator : EventTrackType.Parent;
            if (!EventVoting.AllowsVoter(ev.ScoringMode, voterTrack))
                throw new ConflictException("Hoạt động này chưa bật bình chọn.");

            var (track, participation) = await RequireVotingParticipantAsync(parentUserId, eventId, voterTrack, ct);
            var now = DateTime.UtcNow;
            var reverse = fromParent ? EventVoting.CreatorSchedule(ev) : null;
            var voteStart = reverse?.VotingStartAt ?? ev.VotingStartAt ?? track.ExecutionStartAt;
            var voteEnd = reverse?.VotingEndAt ?? ev.VotingEndAt ?? track.ExecutionEndAt;
            if (now < voteStart || now > voteEnd)
                throw new ConflictException("Ngoài thời gian bình chọn của hoạt động.");

            ReadingSession? proof = null;
            if (fromParent)
            {
                var submission = eventStory.EventSubmissionId is int submissionId
                    ? await _uow.Repository<EventSubmission>().GetByIdAsync(submissionId, ct)
                    : null;
                if (submission == null || submission.LockedStoryVersionId != eventStory.StoryVersionId || submission.Status != EventSubmissionStatus.Approved ||
                    submission.ParentSubmissionText == null || submission.CreatorUserId == parentUserId)
                    throw new ForbiddenException("Bạn không thể bình chọn cho bài này.");
            }
            else
            {
                var children = await _uow.Repository<ChildProfile>().FindAsync(
                    c => c.OwnerUserId == parentUserId && !c.IsDeleted, cancellationToken: ct);
                if (request.ChildProfileId.HasValue && children.All(c => c.Id != request.ChildProfileId.Value))
                    throw new ForbiddenException("Hồ sơ trẻ không thuộc quyền quản lý của bạn.");
                var childIds = request.ChildProfileId.HasValue
                    ? new HashSet<int> { request.ChildProfileId.Value }
                    : children.Select(c => c.Id).ToHashSet();
                var sessions = await _uow.Repository<ReadingSession>().FindAsync(
                    r => childIds.Contains(r.ChildProfileId) && r.StoryId == request.StoryId &&
                         r.StoryVersionId == eventStory.StoryVersionId && r.Status == SessionStatus.Completed &&
                         r.CompletedAt >= voteStart && r.CompletedAt <= voteEnd, cancellationToken: ct);
                proof = sessions.OrderByDescending(r => r.CompletedAt).FirstOrDefault()
                    ?? throw new ConflictException("Bé cần đọc xong đúng phiên bản truyện dự thi trong thời gian bình chọn trước khi đánh giá.");
            }

            var existing = await _uow.Repository<EventVote>().FirstOrDefaultAsync(
                v => v.EventId == eventId && v.StoryId == request.StoryId && v.ParentUserId == parentUserId && !v.IsDeleted,
                cancellationToken: ct);
            if (existing != null && existing.IsDisqualified)
                throw new ConflictException("Phiếu bình chọn này đã bị loại do vi phạm và không thể chỉnh sửa.");

            var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
            if (existing == null)
            {
                existing = new EventVote
                {
                    EventId = eventId,
                    EventSubmissionId = eventStory.EventSubmissionId,
                    StoryId = request.StoryId,
                    StoryVersionId = eventStory.StoryVersionId,
                    ParticipationId = participation.Id,
                    ParentUserId = parentUserId,
                    ChildProfileId = proof?.ChildProfileId,
                    ReadingSessionId = proof?.Id,
                    Stars = request.Stars,
                    Comment = comment
                };
                await _uow.Repository<EventVote>().AddAsync(existing, ct);
            }
            else
            {
                existing.Stars = request.Stars;
                existing.Comment = comment;
                existing.ChildProfileId = proof?.ChildProfileId;
                existing.ReadingSessionId = proof?.Id;
                existing.UpdatedAt = now;
                _uow.Repository<EventVote>().Update(existing);
            }

            await _uow.SaveChangesAsync(ct);
            var dashboard = await BuildDashboardAsync(participation, track, ct);
            if (dashboard.AllRequirementsMet && participation.Status == EventParticipationStatus.Registered)
            {
                participation.Status = EventParticipationStatus.MissionCompleted;
                participation.MissionCompletedAt = now;
                participation.UpdatedAt = now;
                _uow.Repository<EventParticipation>().Update(participation);
                await _uow.SaveChangesAsync(ct);
            }

            await _audit.LogAsync(parentUserId, "EventVoteCast", nameof(EventVote), existing.Id, null, new { eventId, request.StoryId, request.Stars }, ct);
            await _uow.CommitTransactionAsync(ct);
            return MapVote(existing);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(CancellationToken.None);
            _uow.DiscardTrackedChanges();
            throw;
        }
    }

    public async Task<IReadOnlyList<EventVoteDto>> ListVotesAsync(int eventId, CancellationToken ct = default)
    {
        var votes = await _uow.Repository<EventVote>().FindAsync(v => v.EventId == eventId && !v.IsDeleted, cancellationToken: ct);
        if (votes.Count == 0) return Array.Empty<EventVoteDto>();

        var storyIds = votes.Select(v => v.StoryId).Distinct().ToList();
        var userIds = votes.Select(v => v.ParentUserId).Distinct().ToList();
        var sessionIds = votes.Where(v => v.ReadingSessionId.HasValue).Select(v => v.ReadingSessionId!.Value).Distinct().ToList();
        var titles = (await _uow.Repository<Story>().FindAsync(s => storyIds.Contains(s.Id), cancellationToken: ct))
            .ToDictionary(s => s.Id, s => s.Title);
        var names = (await _uow.Repository<UserAccount>().FindAsync(u => userIds.Contains(u.Id), cancellationToken: ct))
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName);
        var completed = sessionIds.Count == 0
            ? new Dictionary<int, DateTime?>()
            : (await _uow.Repository<ReadingSession>().FindAsync(r => sessionIds.Contains(r.Id), cancellationToken: ct))
                .ToDictionary(r => r.Id, r => r.CompletedAt);

        return votes.OrderByDescending(v => v.CreatedAt).Select(v =>
        {
            var dto = MapVote(v);
            dto.StoryTitle = titles.GetValueOrDefault(v.StoryId);
            dto.VoterName = names.GetValueOrDefault(v.ParentUserId);
            dto.ReadingCompletedAt = v.ReadingSessionId is int sid ? completed.GetValueOrDefault(sid) : null;
            return dto;
        }).ToList();
    }

    public async Task<EventVoteDto> DisqualifyVoteAsync(int moderatorUserId, int voteId, DisqualifyEventVoteRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BadRequestException("Phải nhập lý do loại phiếu bình chọn.");

        var vote = await _uow.Repository<EventVote>().FirstOrDefaultAsync(v => v.Id == voteId, cancellationToken: ct)
            ?? throw new NotFoundException(nameof(EventVote), voteId);
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(vote.EventId, ct);
            var ev = await _uow.Repository<Event>().GetByIdAsync(vote.EventId, ct)
                ?? throw new NotFoundException(nameof(Event), vote.EventId);
            await EventModeratorAccess.RequireOperatorAsync(_uow, ev, moderatorUserId, ct);
            EnsureResultsEditable(ev);
            vote = await _uow.Repository<EventVote>().GetByIdAsync(voteId, ct)
                ?? throw new NotFoundException(nameof(EventVote), voteId);
            if (vote.IsDisqualified)
            {
                await _uow.CommitTransactionAsync(ct);
                return MapVote(vote);
            }
            vote.IsDisqualified = true;
            vote.DisqualifiedReason = request.Reason.Trim();
            vote.DisqualifiedByUserId = moderatorUserId;
            vote.DisqualifiedAt = DateTime.UtcNow;
            vote.UpdatedAt = vote.DisqualifiedAt;
            _uow.Repository<EventVote>().Update(vote);
            await _uow.SaveChangesAsync(ct);

            var participation = await _uow.Repository<EventParticipation>().GetByIdAsync(vote.ParticipationId, ct);
            if (participation != null && participation.Status is EventParticipationStatus.MissionCompleted or EventParticipationStatus.Verified)
            {
                if (ev?.ConfigVersion == EventConfigVersion.ActivityBased && participation.EventActivityId.HasValue)
                {
                    var act = await _uow.Repository<EventActivity>().GetByIdAsync(participation.EventActivityId.Value, ct);
                    if (act?.ActivityType == EventActivityType.ReadingMissions)
                    {
                        var dashboard = await BuildActivityDashboardAsync(ev, participation, act, ct);
                        if (!dashboard.AllRequirementsMet)
                        {
                            participation.Status = participation.Status == EventParticipationStatus.Verified
                                ? EventParticipationStatus.VerificationFailed
                                : EventParticipationStatus.Registered;
                            participation.MissionCompletedAt = null;
                            participation.UpdatedAt = DateTime.UtcNow;
                            _uow.Repository<EventParticipation>().Update(participation);
                            await _uow.SaveChangesAsync(ct);
                        }
                    }
                }
                else if (participation.TrackId.HasValue)
                {
                    var track = await _uow.Repository<EventTrack>().GetByIdAsync(participation.TrackId.Value, ct);
                    if (track != null)
                    {
                        var dashboard = await BuildDashboardAsync(participation, track, ct);
                        if (!dashboard.AllRequirementsMet)
                        {
                            participation.Status = participation.Status == EventParticipationStatus.Verified
                                ? EventParticipationStatus.VerificationFailed
                                : EventParticipationStatus.Registered;
                            participation.MissionCompletedAt = null;
                            participation.UpdatedAt = DateTime.UtcNow;
                            _uow.Repository<EventParticipation>().Update(participation);
                            await _uow.SaveChangesAsync(ct);
                        }
                    }
                }
            }

            await _audit.LogAsync(moderatorUserId, "EventVoteDisqualified", nameof(EventVote), vote.Id, null, new { vote.EventId, vote.DisqualifiedReason }, ct);
            await _uow.CommitTransactionAsync(ct);
            return MapVote(vote);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(CancellationToken.None);
            _uow.DiscardTrackedChanges();
            throw;
        }
    }

    public async Task<EventLeaderboardDto> GetLeaderboardAsync(int eventId, bool includeHidden, CancellationToken ct = default, int? userId = null)
    {
        if (!includeHidden && userId.HasValue)
        {
            var campaign = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            if (EventVoting.AllowsVoter(campaign.ScoringMode, EventTrackType.Creator) &&
                !await _uow.Repository<EventParticipation>().ExistsAsync(p => p.EventId == eventId && p.UserId == userId.Value &&
                    p.Status != EventParticipationStatus.Disqualified && !p.IsDeleted, ct))
                throw new ForbiddenException("Chỉ người tham gia sự kiện mới được xem bảng kết quả này.");
        }
        return await BuildLeaderboardAsync(eventId, includeHidden, verifiedOnly: false, ct);
    }

    internal async Task<EventLeaderboardDto> BuildLeaderboardAsync(int eventId, bool includeHidden, bool verifiedOnly, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
            return await BuildActivityLeaderboardAsync(ev, includeHidden, verifiedOnly, ct);
        if (ev.ScoringMode == EventScoringMode.MissionCompletion)
            throw new ConflictException("Sự kiện xét thưởng theo nhiệm vụ và không có bảng xếp hạng.");
        if (!string.IsNullOrWhiteSpace(ev.FinalLeaderboardSnapshotJson))
        {
            var frozen = JsonSerializer.Deserialize<EventLeaderboardDto>(ev.FinalLeaderboardSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (frozen != null) return frozen;
        }

        var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == eventId, cancellationToken: ct);
        var parentTrack = tracks.FirstOrDefault(t => t.TrackType == EventTrackType.Parent);
        var voteEnd = ev.VotingEndAt ?? parentTrack?.ExecutionEndAt ?? tracks.Select(t => t.ExecutionEndAt).DefaultIfEmpty(DateTime.MaxValue).Max();
        var reverse = EventVoting.CreatorSchedule(ev);
        if (reverse != null && (!EventVoting.AllowsVoter(ev.ScoringMode, EventTrackType.Parent) || reverse.VotingEndAt > voteEnd))
            voteEnd = reverse.VotingEndAt;
        var hidden = !includeHidden && DateTime.UtcNow <= voteEnd;
        if (hidden)
        {
            return new EventLeaderboardDto
            {
                EventId = eventId,
                Hidden = true,
                ResultsFinalized = false,
                ScoringMode = ev.ScoringMode,
                MinVotesPerSubmission = ev.MinVotesPerSubmission
            };
        }

        var submissions = await _uow.Repository<EventSubmission>().FindAsync(
            s => s.EventId == eventId && s.Status == EventSubmissionStatus.Approved && s.LockedStoryVersionId != null,
            cancellationToken: ct);
        if (verifiedOnly)
        {
            var verified = await _uow.Repository<EventParticipation>().FindAsync(
                p => p.EventId == eventId && p.Status == EventParticipationStatus.Verified, cancellationToken: ct);
            var verifiedIds = verified.Select(p => p.Id).ToHashSet();
            submissions = submissions.Where(s => verifiedIds.Contains(s.ParticipationId)).ToList();
        }
        var votes = await _uow.Repository<EventVote>().FindAsync(
            v => v.EventId == eventId && !v.IsDisqualified && !v.IsDeleted, cancellationToken: ct);
        var eligibleVoters = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.EventId == eventId && p.Status != EventParticipationStatus.Disqualified && !p.IsDeleted, cancellationToken: ct);
        var voterIds = eligibleVoters.Select(p => p.Id).ToHashSet();
        var stories = await _uow.Repository<Story>().FindAsync(
            s => submissions.Select(x => x.StoryId).Contains(s.Id), cancellationToken: ct);
        var titleByStory = stories.ToDictionary(s => s.Id, s => s.Title ?? string.Empty);

        var candidates = submissions.Select(s =>
        {
            var subVotes = votes.Where(v => voterIds.Contains(v.ParticipationId) && v.EventSubmissionId == s.Id &&
                v.StoryVersionId == s.LockedStoryVersionId).ToList();
            var avg = subVotes.Count == 0 ? 0m : Math.Round((decimal)subVotes.Average(v => v.Stars), 4);
            return new CreatorVoteRankingCandidate(
                s.Id,
                s.ParticipationId,
                s.CreatorUserId,
                s.StoryId,
                s.ParentSubmissionTitle ?? titleByStory.GetValueOrDefault(s.StoryId, $"Truyện #{s.StoryId}"),
                subVotes.Count,
                avg,
                s.CreatedAt);
        }).ToList();

        var authorTracks = submissions.ToDictionary(s => s.Id, s => s.ParentSubmissionText != null
            ? EventTrackType.Parent : EventTrackType.Creator);
        var entries = new List<EventLeaderboardEntryDto>();
        foreach (var group in candidates.GroupBy(c => authorTracks[c.SubmissionId]))
        {
            if (!EventVoting.RanksAuthors(ev.ScoringMode, group.Key) && ev.ScoringMode != EventScoringMode.Rubric) continue;
            var ranked = EventMissionEvaluator.RankCreatorByVotes(group, ev.MinVotesPerSubmission);
            var ranks = ranked.Select((candidate, index) => (candidate.SubmissionId, Rank: index + 1))
                .ToDictionary(item => item.SubmissionId, item => item.Rank);
            foreach (var candidate in group.OrderBy(c => ranks.GetValueOrDefault(c.SubmissionId, int.MaxValue))
                         .ThenByDescending(c => c.ValidVoteCount).ThenBy(c => c.SubmissionId))
            {
                var rank = ranks.GetValueOrDefault(candidate.SubmissionId);
                entries.Add(new EventLeaderboardEntryDto
                {
                    Rank = rank,
                    AuthorTrackType = group.Key,
                    SubmissionId = candidate.SubmissionId,
                    ParticipationId = candidate.ParticipationId,
                    CreatorUserId = candidate.CreatorUserId,
                    StoryId = candidate.StoryId,
                    StoryTitle = candidate.StoryTitle,
                    ValidVoteCount = candidate.ValidVoteCount,
                    AverageStars = Math.Round(candidate.AverageStars, 2),
                    MeetsThreshold = rank > 0,
                    FirstSubmittedAt = candidate.FirstSubmittedAt
                });
            }
        }

        return new EventLeaderboardDto
        {
            EventId = eventId,
            Hidden = false,
            ResultsFinalized = ev.ResultsFinalizedAt.HasValue,
            FinalizedAt = ev.ResultsFinalizedAt,
            ScoringMode = ev.ScoringMode,
            MinVotesPerSubmission = ev.MinVotesPerSubmission,
            Entries = entries
        };
    }

    private static void EnsureResultsEditable(Event? campaign)
    {
        if (campaign?.ResultsFinalizedAt.HasValue == true || campaign?.Status is EventStatus.Closed or EventStatus.Archived)
            throw new ConflictException("Kết quả sự kiện đã chốt.");
    }

    private async Task<T> InEventResultTransactionAsync<T>(int eventId, Func<Task<T>> action, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireEventLockAsync(eventId, ct);
            var result = await action();
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

    private static EventVoteDto MapVote(EventVote v) => new()
    {
        Id = v.Id,
        EventId = v.EventId,
        EventSubmissionId = v.EventSubmissionId,
        StoryId = v.StoryId,
        StoryVersionId = v.StoryVersionId,
        ParentUserId = v.ParentUserId,
        VoterTrackType = v.ReadingSessionId.HasValue ? EventTrackType.Parent : EventTrackType.Creator,
        Stars = v.Stars,
        Comment = v.Comment,
        ReadingSessionId = v.ReadingSessionId,
        IsDisqualified = v.IsDisqualified,
        DisqualifiedReason = v.DisqualifiedReason,
        CreatedAt = v.CreatedAt,
        UpdatedAt = v.UpdatedAt
    };

    private static EventAppealDto MapAppeal(EventAppeal a, EventAward? targetAward = null) => new()
    {
        Id = a.Id,
        EventId = a.EventId,
        ParticipationId = a.ParticipationId,
        AppellantUserId = a.AppellantUserId,
        ReasonText = a.ReasonText,
        Kind = a.Kind,
        TargetAwardId = a.TargetAwardId,
        TargetAwardLabel = targetAward == null ? null : $"{targetAward.RankOrTier} · Suất #{targetAward.SlotIndex}",
        Status = a.Status,
        ResolutionNote = a.ResolutionNote,
        CreatedAt = a.CreatedAt,
        ResolvedAt = a.ResolvedAt
    };
}
