using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public partial class EventManagementService
{
    private static bool IsRoleEligible(EventAudience audience, UserRole role) => audience switch
    {
        EventAudience.Creator => role == UserRole.ContentCreator,
        EventAudience.Parent => role == UserRole.User,
        EventAudience.Both => role is UserRole.ContentCreator or UserRole.User,
        _ => false
    };

    private static bool IsAudienceSubset(EventAudience? target, EventAudience source) =>
        !target.HasValue || source == EventAudience.Both || target.Value == source;

    private static EventActivityType RequiredActivityForResultSource(EventAwardResultSource source) => source switch
    {
        EventAwardResultSource.SubmissionScore => EventActivityType.Submission,
        EventAwardResultSource.VotingAverage => EventActivityType.Voting,
        EventAwardResultSource.MissionCompletion => EventActivityType.ReadingMissions,
        _ => throw new BadRequestException("Nguồn kết quả xét giải không hợp lệ.")
    };

    private static void ValidateActivityBasedDraftRequest(CreateEventRequestDto request)
    {
        if (request.OpensAt.HasValue != request.ClosesAt.HasValue)
            throw new BadRequestException("Cần đặt cả ngày mở và ngày kết thúc sự kiện.");
        if (request.OpensAt is { } opensAt && request.ClosesAt is { } closesAt &&
            (opensAt.Kind != DateTimeKind.Utc || closesAt.Kind != DateTimeKind.Utc || closesAt <= opensAt))
            throw new BadRequestException("Ngày kết thúc sự kiện phải sau mốc mở. API yêu cầu thời điểm UTC.");
        if (request.BudgetVnd < 0)
            throw new BadRequestException("Ngân sách sự kiện không được âm.");
        if (request.Activities.Select(a => a.ActivityType).Distinct().Count() != request.Activities.Count)
            throw new BadRequestException("Mỗi loại hoạt động chỉ được bật tối đa một lần trong cùng sự kiện.");

        foreach (var act in request.Activities)
        {
            if (act.ActivityType == EventActivityType.Voting)
            {
                act.RegistrationStartAt = act.StartAt;
                act.RegistrationEndAt = act.EndAt;
                act.MaxRegistrations = null;
            }
            if (request.OpensAt is { } start && request.ClosesAt is { } end)
            {
                var dates = new (string Label, DateTime? At)[]
                {
                    ("Mở đăng ký", act.RegistrationStartAt), ("Đóng đăng ký", act.RegistrationEndAt),
                    ("Bắt đầu hoạt động", act.StartAt), ("Kết thúc hoạt động", act.EndAt),
                    ("Hạn duyệt bài", act.ReviewCutoffAt)
                };
                foreach (var (label, at) in dates)
                    if (at is { } time && (time < start || time > end))
                        throw new BadRequestException($"{label} của hoạt động {act.ActivityType} phải nằm trong thời gian sự kiện.");
            }
            if (!Enum.IsDefined(act.ActivityType) || !Enum.IsDefined(act.AllowedAudience))
                throw new BadRequestException("Loại hoạt động hoặc đối tượng tham gia không hợp lệ.");
            if (act.ActivityType is EventActivityType.ReadingMissions or EventActivityType.Voting &&
                act.AllowedAudience != EventAudience.Parent)
                throw new BadRequestException("Hoạt động đọc/nhiệm vụ và bình chọn hiện chỉ hỗ trợ đối tượng Parent.");
            if (act.GrantAiCredits < 0 || act.MaxSubmissionsPerUser is <= 0 || act.MaxRegistrations is <= 0 ||
                act.MinVotesPerSubmission < 1 || act.ClaimWindowHours < 1 || act.MaxReallocationRounds < 0)
                throw new BadRequestException("Thông số hoạt động không hợp lệ.");
            if (act.RegistrationEndAt < act.RegistrationStartAt || act.EndAt < act.StartAt ||
                act.StartAt < act.RegistrationStartAt || act.EndAt < act.RegistrationEndAt)
                throw new BadRequestException($"Khoảng thời gian hoạt động {act.ActivityType} không hợp lệ.");
            if (act.ReviewCutoffAt.HasValue && act.ReviewCutoffAt.Value < act.EndAt)
                throw new BadRequestException("Hạn duyệt bài phải từ mốc đóng nhận bài trở đi.");
        }

        decimal total = 0;
        var tierKeys = new HashSet<(EventActivityType?, string)>();
        foreach (var award in request.AwardSlots)
        {
            if (!Enum.IsDefined(award.RewardType) || string.IsNullOrWhiteSpace(award.RankOrTier) ||
                string.IsNullOrWhiteSpace(award.RewardName) || award.SlotCount < 1 ||
                award.UnitValueVnd < 0 || award.CreditGrantAmount < 0 ||
                (award.ResultSource.HasValue && !Enum.IsDefined(award.ResultSource.Value)) ||
                (award.TargetAudience.HasValue && !Enum.IsDefined(award.TargetAudience.Value)))
                throw new BadRequestException("Cấu hình hạng giải không hợp lệ.");
            if (award.RewardType == EventRewardType.PlatformCredits && award.CreditGrantAmount < 1)
                throw new BadRequestException("Giải tín dụng phải cấp ít nhất 1 lượt.");

            var actType = award.ActivityType ?? (award.ResultSource.HasValue ? RequiredActivityForResultSource(award.ResultSource.Value) : (EventActivityType?)null);
            if (!tierKeys.Add((actType, award.RankOrTier.Trim())))
                throw new BadRequestException("Tên hạng giải bị trùng trong cùng hoạt động.");

            total += (decimal)award.UnitValueVnd * award.SlotCount;
        }
        if (total > request.BudgetVnd)
            throw new BadRequestException("Tổng giá trị giải thưởng vượt ngân sách.");
    }

    private async Task SyncActivitiesAndAwardsAsync(int eventId, CreateEventRequestDto request, CancellationToken ct)
    {
        var existingActivities = await _uow.Repository<EventActivity>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);
        var actMap = new Dictionary<EventActivityType, EventActivity>();

        foreach (var input in request.Activities)
        {
            var entity = existingActivities.SingleOrDefault(x => x.ActivityType == input.ActivityType);
            if (entity == null)
            {
                entity = new EventActivity { EventId = eventId, ActivityType = input.ActivityType };
                await _uow.Repository<EventActivity>().AddAsync(entity, ct);
            }
            entity.AllowedAudience = input.AllowedAudience;
            entity.RegistrationStartAt = input.RegistrationStartAt;
            entity.RegistrationEndAt = input.RegistrationEndAt;
            entity.StartAt = input.StartAt;
            entity.EndAt = input.EndAt;
            entity.ReviewCutoffAt = input.ReviewCutoffAt;
            entity.GrantAiCredits = input.ActivityType == EventActivityType.Submission ? Math.Max(0, input.GrantAiCredits) : 0;
            entity.MaxSubmissionsPerUser = input.ActivityType == EventActivityType.Submission ? input.MaxSubmissionsPerUser : null;
            entity.MaxRegistrations = input.MaxRegistrations;
            entity.WaiveWeeklyLibraryLimit = input.WaiveWeeklyLibraryLimit;
            entity.StorySource = input.StorySource;
            entity.MinVotesPerSubmission = Math.Max(1, input.MinVotesPerSubmission);
            entity.ClaimWindowHours = Math.Max(1, input.ClaimWindowHours);
            entity.MaxReallocationRounds = Math.Max(0, input.MaxReallocationRounds);
            entity.AwardAllMissionCompleters = input.AwardAllMissionCompleters;
            entity.ConfigJson = input.ConfigJson;
            if (entity.Id > 0) _uow.Repository<EventActivity>().Update(entity);
            await _uow.SaveChangesAsync(ct);
            actMap[entity.ActivityType] = entity;
        }

        var existingAwards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId, cancellationToken: ct);
        if (existingAwards.Any(a => a.ParticipationId.HasValue))
            throw new ConflictException("Không thể thay giải thưởng đã có người nhận.");
        _uow.Repository<EventAward>().DeleteRange(existingAwards);
        await _uow.SaveChangesAsync(ct);

        var removedActivities = existingActivities.Where(a => !actMap.ContainsKey(a.ActivityType)).ToList();
        if (removedActivities.Count > 0)
        {
            var removedIds = removedActivities.Select(a => a.Id).ToHashSet();
            var hasParts = await _uow.Repository<EventParticipation>().ExistsAsync(
                p => p.EventId == eventId && p.EventActivityId.HasValue && removedIds.Contains(p.EventActivityId.Value), ct);
            if (hasParts)
                throw new ConflictException("Không thể tắt hoạt động đã có người đăng ký.");
            _uow.Repository<EventActivity>().DeleteRange(removedActivities);
            await _uow.SaveChangesAsync(ct);
        }

        foreach (var s in request.AwardSlots)
        {
            var actType = s.ActivityType ?? (s.ResultSource.HasValue ? RequiredActivityForResultSource(s.ResultSource.Value) : (EventActivityType?)null);
            EventActivity? activity = null;
            if (actType.HasValue && actMap.TryGetValue(actType.Value, out var matched))
                activity = matched;
            else if (s.EventActivityId.HasValue)
                activity = actMap.Values.FirstOrDefault(a => a.Id == s.EventActivityId.Value);

            var targetAud = s.TargetAudience ?? (s.ResultSource == EventAwardResultSource.MissionCompletion
                ? EventAudience.Parent
                : activity?.AllowedAudience);
            SubscriptionPlan? pack = null;
            if (s.RewardType == EventRewardType.CreatorPackage)
            {
                if (targetAud != EventAudience.Creator)
                    throw new BadRequestException("Gói Creator chỉ áp dụng cho giải có đối tượng nhận là Creator.");
                pack = await ResolveCreatorPackageAsync(EventTrackType.Creator, s.RewardName, ct);
            }

            for (var idx = 1; idx <= Math.Max(1, s.SlotCount); idx++)
            {
                await _uow.Repository<EventAward>().AddAsync(new EventAward
                {
                    EventId = eventId,
                    EventActivityId = activity?.Id,
                    ResultSource = s.ResultSource,
                    TargetAudience = targetAud,
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

    private async Task ValidateActivityBasedSubmitForApprovalAsync(
        Event ev,
        IReadOnlyList<EventActivity> activities,
        EventRuleVersion? activeRule,
        IReadOnlyList<EventTaskDefinition> tasks,
        IReadOnlyList<EventAward> awards,
        CancellationToken ct)
    {
        if (activities.Count == 0)
            throw new BadRequestException("Sự kiện phải bật ít nhất một hoạt động trước khi gửi duyệt.");

        foreach (var act in activities)
        {
            if (act.RegistrationEndAt < act.RegistrationStartAt || act.EndAt < act.StartAt ||
                act.StartAt < act.RegistrationStartAt || act.EndAt < act.RegistrationEndAt)
                throw new BadRequestException($"Lịch của hoạt động {act.ActivityType} không hợp lệ.");
            if (act.ActivityType is EventActivityType.ReadingMissions or EventActivityType.Voting &&
                act.AllowedAudience != EventAudience.Parent)
                throw new BadRequestException("Hoạt động đọc/nhiệm vụ và bình chọn hiện chỉ hỗ trợ đối tượng Parent.");
        }

        var sub = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.Submission);
        var reading = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.ReadingMissions);
        var voting = activities.FirstOrDefault(a => a.ActivityType == EventActivityType.Voting);

        if (activeRule == null || string.IsNullOrWhiteSpace(activeRule.RulesText))
            throw new BadRequestException("Phải thiết lập nội dung thể lệ trước khi gửi duyệt.");

        if (reading != null)
        {
            if (tasks.Count == 0)
                throw new BadRequestException("Khi bật hoạt động Đọc & Làm nhiệm vụ, cần cấu hình ít nhất một nhiệm vụ.");
            if (reading.StorySource == EventReadingStorySource.ApprovedSubmissionsOnly)
            {
                if (sub == null)
                    throw new BadRequestException("Nguồn truyện từ bài dự thi yêu cầu phải bật hoạt động Sáng tác/Nộp bài.");
                if (sub.AllowedAudience == EventAudience.Parent)
                    throw new BadRequestException("Nguồn chỉ dùng bài dự thi cần có Creator tham gia để cung cấp truyện bé có thể đọc.");
                var cutoff = sub.ReviewCutoffAt ?? sub.EndAt;
                if (reading.StartAt < cutoff)
                    throw new BadRequestException("Khi chỉ dùng bài dự thi làm nguồn đọc, lịch đọc phải bắt đầu sau mốc duyệt bài.");
            }
            if (reading.AwardAllMissionCompleters)
            {
                if (reading.MaxRegistrations is not > 0)
                    throw new BadRequestException("Khi bật cấp thưởng cho mọi gia đình hoàn thành nhiệm vụ, cần đặt giới hạn đăng ký cho hoạt động đọc.");
                var readingSlots = awards.Count(a => a.EventActivityId == reading.Id || a.ResultSource == EventAwardResultSource.MissionCompletion);
                if (readingSlots < reading.MaxRegistrations.Value)
                    throw new BadRequestException($"Số suất giải nhiệm vụ ({readingSlots}) phải đủ theo giới hạn đăng ký ({reading.MaxRegistrations.Value}).");
            }
        }

        if (voting != null)
        {
            if (sub == null)
                throw new BadRequestException("Hoạt động Bình chọn yêu cầu phải bật hoạt động Sáng tác/Nộp bài.");
            if (!sub.ReviewCutoffAt.HasValue || sub.ReviewCutoffAt.Value < sub.EndAt)
                throw new BadRequestException("Hoạt động Sáng tác cần có hạn duyệt bài sau hoặc bằng hạn đóng nhận bài trước khi mở bình chọn.");
            if (voting.StartAt < sub.ReviewCutoffAt.Value)
                throw new BadRequestException("Kỳ bình chọn chỉ được mở sau hạn duyệt bài.");
            if (sub.AllowedAudience == EventAudience.Parent)
                throw new BadRequestException("Hoạt động bình chọn của Parent cần nguồn bài đã duyệt mà bé đọc được trên nền tảng (Creator tham gia sáng tác).");
        }

        var activityById = activities.ToDictionary(a => a.Id);
        decimal totalValue = 0;
        foreach (var award in awards)
        {
            if (!award.ResultSource.HasValue)
                throw new BadRequestException($"Hạng giải '{award.RankOrTier}' chưa chọn nguồn kết quả xét giải.");

            var requiredType = RequiredActivityForResultSource(award.ResultSource.Value);
            var sourceAct = activities.FirstOrDefault(a => a.ActivityType == requiredType)
                ?? throw new BadRequestException($"Hạng giải '{award.RankOrTier}' chọn nguồn {award.ResultSource}, nhưng hoạt động {requiredType} chưa được bật.");

            if (award.EventActivityId.HasValue && activityById.TryGetValue(award.EventActivityId.Value, out var linked) && linked.Id != sourceAct.Id)
                throw new BadRequestException($"Hạng giải '{award.RankOrTier}' liên kết không khớp với hoạt động nguồn kết quả.");

            var audienceSource = award.ResultSource.Value == EventAwardResultSource.MissionCompletion
                ? reading!.AllowedAudience
                : sub!.AllowedAudience;
            if (!IsAudienceSubset(award.TargetAudience, audienceSource))
                throw new BadRequestException($"Đối tượng nhận của hạng giải '{award.RankOrTier}' vượt quá phạm vi đối tượng của hoạt động nguồn.");

            if (award.RewardType == EventRewardType.CreatorPackage)
            {
                if (award.TargetAudience != EventAudience.Creator)
                    throw new BadRequestException("Gói Creator chỉ áp dụng cho giải có đối tượng nhận là Creator.");
                var pack = await ResolveCreatorPackageAsync(EventTrackType.Creator, award.RewardName, ct);
                award.RewardName = pack.Name;
                award.CreditGrantAmount = pack.AiCredits;
                award.SubmitCreditGrantAmount = pack.SubmitCredits;
                _uow.Repository<EventAward>().Update(award);
            }

            totalValue += award.UnitValueVnd;
        }
        if (totalValue > ev.BudgetVnd)
            throw new BadRequestException("Tổng giá trị giải thưởng vượt ngân sách sự kiện.");

        if (reading != null && !EventParentReadiness.IsWaitingForSubmissions(reading, sub, DateTime.UtcNow))
            await EventParentReadiness.EnsureActivityReadyAsync(_uow, ev, reading, ct);
    }

    private async Task<List<EventActivityUserStateDto>> BuildActivityUserStatesAsync(
        Event ev,
        IReadOnlyList<EventActivity> activities,
        int userId,
        UserRole role,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var parts = await _uow.Repository<EventParticipation>().FindAsync(
            p => p.EventId == ev.Id && p.UserId == userId && !p.IsDeleted, cancellationToken: ct);
        var mySubs = await _uow.Repository<EventSubmission>().FindAsync(
            s => s.EventId == ev.Id && s.CreatorUserId == userId && !s.IsDeleted, cancellationToken: ct);

        // Chỉ hoạt động nguồn xét giải mới có hạn khiếu nại (sau khi công bố kết quả).
        var appealSourceActIds = new HashSet<int>();
        if (ev.ResultsPublishedAt.HasValue)
        {
            var eventAwards = await _uow.Repository<EventAward>().FindAsync(
                a => a.EventId == ev.Id && !a.IsDeleted, cancellationToken: ct);
            appealSourceActIds = eventAwards
                .Select(a => EventResultsQueryService.ResolveSourceActivityId(a, activities))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();
        }

        var result = new List<EventActivityUserStateDto>(activities.Count);
        foreach (var act in activities.OrderBy(a => a.StartAt).ThenBy(a => a.Id))
        {
            var eligible = IsRoleEligible(act.AllowedAudience, role);
            var part = parts.FirstOrDefault(p => p.EventActivityId == act.Id);
            var lifecycle = now < act.StartAt ? "NotOpen" : (now <= act.EndAt ? "Open" : "Ended");
            var subCount = act.ActivityType == EventActivityType.Submission
                ? mySubs.Where(s => !s.EventActivityId.HasValue || s.EventActivityId == act.Id).Select(s => s.StoryId).Distinct().Count()
                : 0;
            var activePart = part != null && part.Status != EventParticipationStatus.Disqualified;

            result.Add(new EventActivityUserStateDto
            {
                ActivityId = act.Id,
                ActivityType = act.ActivityType,
                AllowedAudience = act.AllowedAudience,
                LifecycleStatus = lifecycle,
                IsEligible = eligible,
                IsRegistered = part != null,
                ParticipationId = part?.Id,
                ParticipationStatus = part?.Status,
                AppealDeadlineAt = part != null && appealSourceActIds.Contains(act.Id) &&
                                   part.Status is EventParticipationStatus.Verified or EventParticipationStatus.VerificationFailed
                    ? ev.ResultsPublishedAt?.AddHours(EventAppealWindow.Hours)
                    : null,
                CanRegister = ev.Status == EventStatus.Published && eligible && part == null &&
                              act.ActivityType != EventActivityType.Voting &&
                              now >= act.RegistrationStartAt && now <= act.RegistrationEndAt,
                CanSubmit = ev.Status == EventStatus.Published && eligible && activePart &&
                            act.ActivityType == EventActivityType.Submission &&
                            now >= act.StartAt && now <= act.EndAt &&
                            (!act.MaxSubmissionsPerUser.HasValue || subCount < act.MaxSubmissionsPerUser.Value),
                CanDoMissions = ev.Status == EventStatus.Published && eligible && activePart &&
                                act.ActivityType == EventActivityType.ReadingMissions &&
                                now >= act.StartAt && now <= act.EndAt,
                CanVote = ev.Status == EventStatus.Published && eligible &&
                          (part == null || part.Status != EventParticipationStatus.Disqualified) &&
                          act.ActivityType == EventActivityType.Voting &&
                          now >= act.StartAt && now <= act.EndAt,
                SubmissionsCount = subCount,
                MaxSubmissionsPerUser = act.MaxSubmissionsPerUser
            });
        }
        return result;
    }

    public Task<CloneLegacyDraftResultDto> CloneLegacyDraftToActivityBasedAsync(int actorUserId, int sourceEventId, CancellationToken ct = default)
        => InTransactionAsync(sourceEventId, async () =>
        {
            var source = await _uow.Repository<Event>().GetByIdAsync(sourceEventId, ct)
                ?? throw new NotFoundException(nameof(Event), sourceEventId);
            EventModeratorAccess.RequireOwner(source, actorUserId);
            if (source.ConfigVersion != EventConfigVersion.LegacyRoleBased)
                throw new BadRequestException("Chỉ hỗ trợ sao chép từ sự kiện theo mô hình cũ sang mô hình hoạt động.");
            if (source.Status != EventStatus.Draft)
                throw new ConflictException("Chỉ có thể sao chép sự kiện cũ đang ở trạng thái bản nháp.");

            var warnings = new List<string>();
            var tracks = await _uow.Repository<EventTrack>().FindAsync(t => t.EventId == source.Id, cancellationToken: ct);
            var activeRule = await _uow.Repository<EventRuleVersion>().FirstOrDefaultAsync(r => r.EventId == source.Id && r.IsActive, cancellationToken: ct);
            var sourceTasks = activeRule == null
                ? new List<EventTaskDefinition>()
                : (await _uow.Repository<EventTaskDefinition>().FindAsync(t => t.EventRuleVersionId == activeRule.Id, cancellationToken: ct)).OrderBy(t => t.SortOrder).ToList();
            var sourceAwards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == source.Id, cancellationToken: ct);

            const string cloneSuffix = " (Bản hoạt động)";
            var cloned = new Event
            {
                Title = source.Title[..Math.Min(source.Title.Length, MaxTitleLength - cloneSuffix.Length)].TrimEnd() + cloneSuffix,
                Slug = BuildSlug($"{source.Title}-activity"),
                Description = source.Description,
                BannerUrl = source.BannerUrl,
                MainCategoryId = source.MainCategoryId,
                TargetAgeBands = source.TargetAgeBands,
                Status = EventStatus.Draft,
                ConfigVersion = EventConfigVersion.ActivityBased,
                Format = EventFormat.Standard,
                ScoringMode = source.ScoringMode,
                MinVotesPerSubmission = source.MinVotesPerSubmission,
                BudgetVnd = source.BudgetVnd,
                CreatedByUserId = actorUserId,
                AssignedModeratorId = actorUserId
            };
            await _uow.Repository<Event>().AddAsync(cloned, ct);
            await _uow.SaveChangesAsync(ct);

            var creatorTrack = tracks.FirstOrDefault(t => t.TrackType == EventTrackType.Creator);
            var parentTrack = tracks.FirstOrDefault(t => t.TrackType == EventTrackType.Parent);
            EventActivity? subAct = null;
            EventActivity? readAct = null;

            if (creatorTrack != null)
            {
                subAct = new EventActivity
                {
                    EventId = cloned.Id,
                    ActivityType = EventActivityType.Submission,
                    AllowedAudience = EventAudience.Creator,
                    RegistrationStartAt = creatorTrack.RegistrationStartAt,
                    RegistrationEndAt = creatorTrack.RegistrationEndAt,
                    StartAt = creatorTrack.ExecutionStartAt,
                    EndAt = source.SubmissionCutoffAt ?? creatorTrack.ExecutionEndAt,
                    ReviewCutoffAt = source.ReviewCutoffAt,
                    GrantAiCredits = creatorTrack.GrantAiCredits,
                    MaxSubmissionsPerUser = Math.Max(1, creatorTrack.MaxSubmissionsPerUser),
                    ClaimWindowHours = creatorTrack.ClaimWindowHours,
                    MaxReallocationRounds = creatorTrack.MaxReallocationRounds
                };
                await _uow.Repository<EventActivity>().AddAsync(subAct, ct);
                if (creatorTrack.GrantSubmitCredits > 0)
                    warnings.Add($"Đã bỏ trường Tặng lượt nộp miễn phí ({creatorTrack.GrantSubmitCredits} lượt) theo quy định sự kiện mới.");
            }

            if (parentTrack != null)
            {
                readAct = new EventActivity
                {
                    EventId = cloned.Id,
                    ActivityType = EventActivityType.ReadingMissions,
                    AllowedAudience = EventAudience.Parent,
                    RegistrationStartAt = parentTrack.RegistrationStartAt,
                    RegistrationEndAt = parentTrack.RegistrationEndAt,
                    StartAt = parentTrack.ExecutionStartAt,
                    EndAt = parentTrack.ExecutionEndAt,
                    MaxRegistrations = source.MaxParentRegistrations,
                    WaiveWeeklyLibraryLimit = parentTrack.WaiveWeeklyLibraryLimit,
                    StorySource = creatorTrack != null ? EventReadingStorySource.Both : EventReadingStorySource.CuratedOnly,
                    ClaimWindowHours = parentTrack.ClaimWindowHours,
                    MaxReallocationRounds = parentTrack.MaxReallocationRounds,
                    AwardAllMissionCompleters = parentTrack.AwardAllMissionCompleters
                };
                await _uow.Repository<EventActivity>().AddAsync(readAct, ct);
            }

            EventActivity? voteAct = null;
            if (source.ScoringMode == EventScoringMode.ParentVoteAverage)
            {
                if (source.VotingStartAt.HasValue && source.VotingEndAt.HasValue && parentTrack != null && subAct?.ReviewCutoffAt != null)
                {
                    voteAct = new EventActivity
                    {
                        EventId = cloned.Id,
                        ActivityType = EventActivityType.Voting,
                        AllowedAudience = EventAudience.Parent,
                        RegistrationStartAt = source.VotingStartAt.Value,
                        RegistrationEndAt = source.VotingEndAt.Value,
                        StartAt = source.VotingStartAt.Value,
                        EndAt = source.VotingEndAt.Value,
                        MaxRegistrations = null,
                        MinVotesPerSubmission = Math.Max(1, source.MinVotesPerSubmission)
                    };
                    await _uow.Repository<EventActivity>().AddAsync(voteAct, ct);
                }
                else
                {
                    warnings.Add("Chưa đủ mốc thời gian duyệt/bình chọn để tự động tạo hoạt động Bình chọn; vui lòng cấu hình lại trước khi gửi duyệt.");
                }
            }
            await _uow.SaveChangesAsync(ct);

            if (activeRule != null)
            {
                var rule = new EventRuleVersion
                {
                    EventId = cloned.Id,
                    VersionNo = 1,
                    Revision = 1,
                    ContentHash = activeRule.ContentHash,
                    RulesText = activeRule.RulesText,
                    RewardEligibilityText = activeRule.RewardEligibilityText,
                    TieBreakerPolicyText = activeRule.TieBreakerPolicyText,
                    InsufficientVotesPolicyText = activeRule.InsufficientVotesPolicyText,
                    CreatorRubricJson = activeRule.CreatorRubricJson,
                    CompletionMode = activeRule.CompletionMode,
                    RequiredTaskCount = activeRule.RequiredTaskCount,
                    RequiredPoints = activeRule.RequiredPoints,
                    IsActive = true,
                    ActivatedAt = DateTime.UtcNow
                };
                await _uow.Repository<EventRuleVersion>().AddAsync(rule, ct);
                await _uow.SaveChangesAsync(ct);

                var readingTasks = sourceTasks.Where(t => t.TargetTrackType != EventTrackType.Creator &&
                    t.VerifierType is not (TaskVerifierType.SubmitApprovedStory or TaskVerifierType.VoteEventStories)).ToList();
                if (readingTasks.Count < sourceTasks.Count)
                    warnings.Add("Một số nhiệm vụ gắn vai trò cũ (Nộp bài/Bình chọn) không còn dùng trong hoạt động Đọc & Nhiệm vụ và đã được bỏ qua.");

                foreach (var t in readingTasks)
                {
                    await _uow.Repository<EventTaskDefinition>().AddAsync(new EventTaskDefinition
                    {
                        EventRuleVersionId = rule.Id,
                        TaskCode = t.TaskCode,
                        TargetTrackType = EventTrackType.Parent,
                        Title = t.Title,
                        Description = t.Description,
                        SortOrder = t.SortOrder,
                        VerifierType = t.VerifierType,
                        TargetValue = t.TargetValue,
                        ConfigJson = t.ConfigJson,
                        IsMandatory = t.IsMandatory,
                        Points = t.Points,
                        AllowMultiplePerStory = t.AllowMultiplePerStory
                    }, ct);
                }
            }

            if (sourceAwards.Count > 0)
            {
                var trackById = tracks.ToDictionary(t => t.Id, t => t.TrackType);
                foreach (var aw in sourceAwards)
                {
                    var oldTrackType = aw.TrackId.HasValue && trackById.TryGetValue(aw.TrackId.Value, out var tt) ? tt : EventTrackType.Parent;
                    EventAwardResultSource? resultSource = null;
                    int? linkedActId = null;
                    if (oldTrackType == EventTrackType.Creator)
                    {
                        if (voteAct != null) { resultSource = EventAwardResultSource.VotingAverage; linkedActId = voteAct.Id; }
                        else if (subAct != null && source.ScoringMode == EventScoringMode.Rubric) { resultSource = EventAwardResultSource.SubmissionScore; linkedActId = subAct.Id; }
                    }
                    else if (readAct != null)
                    {
                        resultSource = EventAwardResultSource.MissionCompletion;
                        linkedActId = readAct.Id;
                    }
                    if (!resultSource.HasValue)
                        warnings.Add($"Hạng giải '{aw.RankOrTier}' chưa xác định được nguồn kết quả xét giải trong bản mới.");

                    await _uow.Repository<EventAward>().AddAsync(new EventAward
                    {
                        EventId = cloned.Id,
                        EventActivityId = linkedActId,
                        ResultSource = resultSource,
                        TargetAudience = oldTrackType == EventTrackType.Creator ? EventAudience.Creator : EventAudience.Parent,
                        RankOrTier = aw.RankOrTier,
                        SlotIndex = aw.SlotIndex,
                        RewardType = aw.RewardType,
                        RewardName = aw.RewardName,
                        UnitValueVnd = aw.UnitValueVnd,
                        CreditGrantAmount = aw.CreditGrantAmount,
                        SubmitCreditGrantAmount = aw.SubmitCreditGrantAmount,
                        Status = EventAwardStatus.AwardProposed
                    }, ct);
                }
            }

            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(actorUserId, "LegacyDraftClonedToActivityBased", nameof(Event), cloned.Id, null, new { sourceEventId, warnings.Count }, ct);

            return new CloneLegacyDraftResultDto
            {
                Draft = await BuildDetailAsync(cloned, ct, actorUserId, null),
                UnmappedWarnings = warnings
            };
        }, ct);

    private static EventActivityDto MapActivity(EventActivity a) => new()
    {
        Id = a.Id,
        ActivityType = a.ActivityType,
        AllowedAudience = a.AllowedAudience,
        RegistrationStartAt = a.RegistrationStartAt,
        RegistrationEndAt = a.RegistrationEndAt,
        StartAt = a.StartAt,
        EndAt = a.EndAt,
        ReviewCutoffAt = a.ReviewCutoffAt,
        GrantAiCredits = a.GrantAiCredits,
        MaxSubmissionsPerUser = a.MaxSubmissionsPerUser,
        MaxRegistrations = a.MaxRegistrations,
        WaiveWeeklyLibraryLimit = a.WaiveWeeklyLibraryLimit,
        StorySource = a.StorySource,
        MinVotesPerSubmission = a.MinVotesPerSubmission,
        ClaimWindowHours = a.ClaimWindowHours,
        MaxReallocationRounds = a.MaxReallocationRounds,
        AwardAllMissionCompleters = a.AwardAllMissionCompleters,
        ConfigJson = a.ConfigJson
    };
}
