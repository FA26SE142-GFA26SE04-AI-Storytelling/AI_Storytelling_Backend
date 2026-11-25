using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public partial class EventManagementService
{
    private async Task<T> InTransactionAsync<T>(int? eventId, Func<Task<T>> action, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            if (eventId.HasValue) await _uow.AcquireEventLockAsync(eventId.Value, ct);
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

    private Task<IReadOnlyList<SubscriptionPlan>> GetCreatorRewardPlansAsync(CancellationToken ct) =>
        _uow.Repository<SubscriptionPlan>().FindAsync(p =>
            p.IsActive && !p.IsDeleted && p.Audience == PlanAudience.Creator && p.PlanType == PlanType.CreatorPack, cancellationToken: ct);

    public async Task<IReadOnlyList<EventRewardPlanDto>> ListCreatorRewardPlansAsync(CancellationToken ct = default) =>
        (await GetCreatorRewardPlansAsync(ct))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(p => p.Name)
            .Select(p => new EventRewardPlanDto(p.Id, p.Name, p.AiCredits, p.SubmitCredits))
            .ToList();

    private async Task<SubscriptionPlan> ResolveCreatorPackageAsync(EventTrackType trackType, string planName, CancellationToken ct)
    {
        if (trackType != EventTrackType.Creator)
            throw new BadRequestException("Gói Creator chỉ dành cho nhánh Creator.");

        var plans = await GetCreatorRewardPlansAsync(ct);
        var matches = plans.Where(p => string.Equals(p.Name.Trim(), planName.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
            throw new BadRequestException("Tên giải phải khớp với một Gói Creator đang hoạt động.");
        return matches[0];
    }

    private async Task ValidateCategoryAsync(int categoryId, CancellationToken ct)
    {
        if (!await _uow.Repository<ContentCategory>().ExistsAsync(
                x => x.Id == categoryId && x.IsActive && x.Group != ContentCategoryGroup.SafetyWarning, ct))
            throw new BadRequestException("Danh mục chính của sự kiện không hợp lệ.");
    }

    private static void ValidateEventConfiguration(CreateEventRequestDto request)
    {
        if (request.BudgetVnd < 0 || request.Tracks.Select(x => x.TrackType).Distinct().Count() != request.Tracks.Count)
            throw new BadRequestException("Ngân sách hoặc nhánh tham gia không hợp lệ.");
        ValidateParticipationSchedule(
            request.Format, request.Tracks, request.MaxParentRegistrations, request.SubmissionCutoffAt,
            request.ReviewCutoffAt, request.VotingStartAt, request.VotingEndAt,
            validateParentVoting: request.ScoringMode != EventScoringMode.CreatorVoteAverage);
        var enablesCreatorVoting = EventVoting.AllowsVoter(request.ScoringMode, EventTrackType.Creator);
        if (enablesCreatorVoting != (request.CreatorVotingSchedule != null))
            throw new BadRequestException("Cần lịch riêng cho hoạt động Parent nộp truyện và Creator bình chọn.");
        if (request.CreatorVotingSchedule is { } reverse)
        {
            if (request.Format != EventFormat.SharedCreatorParent)
                throw new BadRequestException("Cần bật cả hai nhóm tham gia để Creator bình chọn.");
            var parent = request.Tracks.SingleOrDefault(track => track.TrackType == EventTrackType.Parent);
            var creator = request.Tracks.SingleOrDefault(track => track.TrackType == EventTrackType.Creator);
            if (parent == null || creator == null || reverse.SubmissionCutoffAt <= parent.ExecutionStartAt ||
                reverse.SubmissionCutoffAt > parent.ExecutionEndAt || reverse.ReviewCutoffAt < reverse.SubmissionCutoffAt ||
                reverse.VotingStartAt < reverse.ReviewCutoffAt || reverse.VotingStartAt < creator.ExecutionStartAt ||
                reverse.VotingEndAt <= reverse.VotingStartAt || reverse.VotingEndAt > creator.ExecutionEndAt)
                throw new BadRequestException("Kiểm tra hạn nộp, hạn duyệt và giờ bình chọn của Creator theo lịch hai nhóm.");
        }
        var keys = new HashSet<(EventTrackType, string)>();
        decimal total = 0;
        foreach (var award in request.AwardSlots)
        {
            if (!request.Tracks.Any(x => x.TrackType == award.TrackType) ||
                !Enum.IsDefined(award.RewardType) || string.IsNullOrWhiteSpace(award.RankOrTier) ||
                string.IsNullOrWhiteSpace(award.RewardName) || award.SlotCount < 1 ||
                award.UnitValueVnd < 0 || award.CreditGrantAmount < 0 ||
                !keys.Add((award.TrackType, award.RankOrTier.Trim())))
                throw new BadRequestException("Cấu hình giải thưởng không hợp lệ.");
            if (award.RewardType == EventRewardType.PlatformCredits && award.CreditGrantAmount < 1)
                throw new BadRequestException("Giải tín dụng phải cấp ít nhất 1 lượt.");
            total += (decimal)award.UnitValueVnd * award.SlotCount;
        }
        if (total > request.BudgetVnd) throw new BadRequestException("Tổng giá trị giải thưởng vượt ngân sách.");
    }

    private static void ValidateParticipationSchedule(
        EventFormat format, IReadOnlyCollection<UpsertEventTrackDto> tracks, int? maxParentRegistrations,
        DateTime? submissionCutoff, DateTime? reviewCutoff, DateTime? votingStart, DateTime? votingEnd,
        bool validateParentVoting = true)
    {
        foreach (var track in tracks)
        {
            if (!Enum.IsDefined(track.TrackType) || track.MaxSubmissionsPerUser < 1 ||
                track.GrantAiCredits < 0 || track.GrantSubmitCredits < 0 ||
                track.ClaimWindowHours < 1 || track.MaxReallocationRounds < 0)
                throw new BadRequestException("Cấu hình nhánh tham gia không hợp lệ.");
            if (track.RegistrationEndAt <= track.RegistrationStartAt || track.ExecutionEndAt <= track.ExecutionStartAt ||
                track.ExecutionStartAt < track.RegistrationStartAt || track.RegistrationEndAt > track.ExecutionEndAt)
                throw new BadRequestException($"Mốc tham gia của {track.TrackType} không hợp lệ. Thời điểm kết thúc phải sau thời điểm bắt đầu.");
        }

        if (maxParentRegistrations is <= 0)
            throw new BadRequestException("Giới hạn đăng ký Parent phải lớn hơn 0.");
        if (format != EventFormat.SharedCreatorParent || !validateParentVoting) return;

        var creator = tracks.FirstOrDefault(track => track.TrackType == EventTrackType.Creator);
        var parent = tracks.FirstOrDefault(track => track.TrackType == EventTrackType.Parent);
        if (creator != null && submissionCutoff is { } cutoff &&
            (cutoff <= creator.ExecutionStartAt || cutoff > creator.ExecutionEndAt))
            throw new BadRequestException("Hạn nộp phải sau giờ mở và trong lịch tham gia của Creator.");
        if (submissionCutoff.HasValue && reviewCutoff < submissionCutoff)
            throw new BadRequestException("Hạn duyệt truyện phải từ hạn nộp truyện trở đi.");

        var effectiveReview = reviewCutoff ?? submissionCutoff ?? creator?.ExecutionEndAt;
        var effectiveVotingStart = votingStart ?? parent?.ExecutionStartAt;
        var effectiveVotingEnd = votingEnd ?? parent?.ExecutionEndAt;
        if (effectiveVotingStart < effectiveReview)
            throw new BadRequestException("Vòng bình chọn chỉ được mở sau khi danh sách bài dự thi đã được chốt và duyệt.");
        if (effectiveVotingEnd <= effectiveVotingStart ||
            (parent != null && (effectiveVotingStart < parent.ExecutionStartAt || effectiveVotingEnd > parent.ExecutionEndAt)))
            throw new BadRequestException("Giờ mở và đóng bình chọn phải nằm trong lịch Parent, với giờ đóng sau giờ mở.");
    }

    private static void ValidateRules(UpsertEventRuleRequestDto request, bool allowEmptyTasks = false)
    {
        if (string.IsNullOrWhiteSpace(request.RulesText) || !Enum.IsDefined(request.CompletionMode) ||
            (!allowEmptyTasks && request.Tasks.Count == 0))
            throw new BadRequestException("Phải có thể lệ, chế độ hoàn thành và ít nhất một nhiệm vụ hợp lệ.");
        if (request.Tasks.Count == 0)
        {
            EventCreatorRubric.Parse(request.CreatorRubricJson);
            return;
        }
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < request.Tasks.Count; i++)
        {
            var task = request.Tasks[i];
            var code = string.IsNullOrWhiteSpace(task.TaskCode) ? $"T{i + 1:00}" : task.TaskCode.Trim();
            if (!codes.Add(code) || code.Length > 40 || string.IsNullOrWhiteSpace(task.Title) ||
                task.Title.Trim().Length > 200 || task.Description.Length > 2000 ||
                !Enum.IsDefined(task.VerifierType) || task.TargetValue < 1 || task.Points < 0)
                throw new BadRequestException("Cấu hình nhiệm vụ không hợp lệ hoặc mã nhiệm vụ bị trùng.");
            EventTaskFilters.Parse(task.ConfigJson);
        }
        EventCreatorRubric.Parse(request.CreatorRubricJson);
        var mandatory = request.Tasks.Count(x => x.IsMandatory);
        if (request.CompletionMode == TrackCompletionMode.AllMandatory && mandatory == 0)
            throw new BadRequestException("Phải có ít nhất một nhiệm vụ bắt buộc.");
        if (request.CompletionMode == TrackCompletionMode.MinimumTasksCount &&
            (request.RequiredTaskCount is not > 0 || request.RequiredTaskCount > request.Tasks.Count || request.RequiredTaskCount < mandatory))
            throw new BadRequestException("Ngưỡng số nhiệm vụ phải nằm trong tổng số nhiệm vụ và bao gồm nhiệm vụ bắt buộc.");
        if (request.CompletionMode == TrackCompletionMode.MinimumPointsThreshold &&
            (request.RequiredPoints is not > 0 || request.RequiredPoints > request.Tasks.Sum(x => (long)x.Points)))
            throw new BadRequestException("Ngưỡng điểm không hợp lệ hoặc vượt tổng điểm nhiệm vụ.");
    }


    private async Task UpdateTracksAndAwardsAsync(int eventId, CreateEventRequestDto request, CancellationToken ct)
    {
        var tracks = await _uow.Repository<EventTrack>().FindAsync(x => x.EventId == eventId, cancellationToken: ct);
        var map = new Dictionary<EventTrackType, EventTrack>();
        foreach (var input in request.Tracks)
        {
            var track = tracks.SingleOrDefault(x => x.TrackType == input.TrackType);
            if (track == null)
            {
                track = new EventTrack { EventId = eventId, TrackType = input.TrackType };
                await _uow.Repository<EventTrack>().AddAsync(track, ct);
            }
            track.RegistrationStartAt = input.RegistrationStartAt;
            track.RegistrationEndAt = input.RegistrationEndAt;
            track.ExecutionStartAt = input.ExecutionStartAt;
            track.ExecutionEndAt = input.ExecutionEndAt;
            track.MaxSubmissionsPerUser = input.MaxSubmissionsPerUser;
            track.GrantAiCredits = input.GrantAiCredits;
            track.GrantSubmitCredits = input.GrantSubmitCredits;
            track.WaiveWeeklyLibraryLimit = input.WaiveWeeklyLibraryLimit;
            track.ClaimWindowHours = input.ClaimWindowHours;
            track.MaxReallocationRounds = input.MaxReallocationRounds;
            if (track.Id > 0) _uow.Repository<EventTrack>().Update(track);
            map.Add(track.TrackType, track);
        }
        // Drafts cannot have participants or awarded slots. Keep track IDs for retained branches.
        var awards = await _uow.Repository<EventAward>().FindAsync(x => x.EventId == eventId, cancellationToken: ct);
        if (awards.Any(x => x.ParticipationId.HasValue))
            throw new ConflictException("Không thể thay giải thưởng đã có người nhận.");
        _uow.Repository<EventAward>().DeleteRange(awards);
        await _uow.SaveChangesAsync(ct);
        _uow.Repository<EventTrack>().DeleteRange(tracks.Where(x => !map.ContainsKey(x.TrackType)));
        await _uow.SaveChangesAsync(ct);
        await SyncAwardSlotsAsync(eventId, map, request.AwardSlots, ct);
    }

    private async Task EnsureParentOpeningReadyAsync(int eventId, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct);
        if (ev?.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            var activities = await _uow.Repository<EventActivity>().FindAsync(x => x.EventId == eventId, cancellationToken: ct);
            var reading = activities.FirstOrDefault(x => x.ActivityType == EventActivityType.ReadingMissions);
            var submission = activities.FirstOrDefault(x => x.ActivityType == EventActivityType.Submission);
            if (reading == null || EventParentReadiness.IsWaitingForSubmissions(reading, submission, DateTime.UtcNow)) return;
            await EventParentReadiness.EnsureActivityReadyAsync(_uow, ev, reading, ct);
            return;
        }
        var parent = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(
            x => x.EventId == eventId && x.TrackType == EventTrackType.Parent, cancellationToken: ct);
        if (parent == null || parent.RegistrationStartAt > DateTime.UtcNow) return;
        if (ev?.Format == EventFormat.SharedCreatorParent && (ev.VotingStartAt ?? parent.ExecutionStartAt) > DateTime.UtcNow)
            return;
        await EventParentReadiness.EnsureAsync(_uow, eventId, ct);
    }

    public Task<EventRuleVersionDto> UpdateRulesSectionAsync(int actorUserId, int eventId, UpdateEventRulesSectionRequestDto request, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await LoadEditableDraftAsync(eventId, actorUserId, ct);
            if (string.IsNullOrWhiteSpace(request.RulesText))
                throw new BadRequestException("Nội dung thể lệ không được để trống.");
            if (!Enum.IsDefined(request.ScoringMode) || request.MinVotesPerSubmission < 1)
                throw new BadRequestException("Cách tính điểm hoặc ngưỡng phiếu tối thiểu không hợp lệ.");
            if (request.ScoringMode != ev.ScoringMode &&
                (EventVoting.AllowsVoter(ev.ScoringMode, EventTrackType.Creator) ||
                 EventVoting.AllowsVoter(request.ScoringMode, EventTrackType.Creator)))
                throw new BadRequestException("Hãy đổi chiều bình chọn trong form chỉnh sửa sự kiện.");
            EventCreatorRubric.Parse(request.CreatorRubricJson);

            ev.Status = EventStatus.Draft;
            ev.ApprovedByAdminId = null;
            ev.ScoringMode = request.ScoringMode;
            ev.MinVotesPerSubmission = request.MinVotesPerSubmission;
            ev.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<Event>().Update(ev);

            var existingVersions = await _uow.Repository<EventRuleVersion>().FindAsync(r => r.EventId == eventId, cancellationToken: ct);
            var previousActive = existingVersions.FirstOrDefault(v => v.IsActive);
            var previousTasks = previousActive == null
                ? new List<EventTaskDefinition>()
                : (await _uow.Repository<EventTaskDefinition>().FindAsync(t => t.EventRuleVersionId == previousActive.Id, cancellationToken: ct)).ToList();

            foreach (var old in existingVersions.Where(v => v.IsActive))
            {
                old.IsActive = false;
                _uow.Repository<EventRuleVersion>().Update(old);
            }

            var nextVersionNo = existingVersions.Count == 0 ? 1 : existingVersions.Max(v => v.VersionNo) + 1;
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                request.RulesText,
                request.RewardEligibilityText,
                request.TieBreakerPolicyText,
                request.InsufficientVotesPolicyText,
                request.ScoringMode,
                request.MinVotesPerSubmission,
                request.CreatorRubricJson
            });
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();

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
                CompletionMode = previousActive?.CompletionMode ?? TrackCompletionMode.AllMandatory,
                RequiredTaskCount = previousActive?.RequiredTaskCount,
                RequiredPoints = previousActive?.RequiredPoints,
                IsActive = true,
                ActivatedAt = DateTime.UtcNow
            };
            await _uow.Repository<EventRuleVersion>().AddAsync(rule, ct);
            await _uow.SaveChangesAsync(ct);

            var clonedTasks = new List<EventTaskDefinition>();
            foreach (var pt in previousTasks)
            {
                var def = new EventTaskDefinition
                {
                    EventRuleVersionId = rule.Id,
                    TaskCode = pt.TaskCode,
                    TargetTrackType = pt.TargetTrackType,
                    Title = pt.Title,
                    Description = pt.Description,
                    SortOrder = pt.SortOrder,
                    VerifierType = pt.VerifierType,
                    TargetValue = pt.TargetValue,
                    ConfigJson = pt.ConfigJson,
                    IsMandatory = pt.IsMandatory,
                    Points = pt.Points,
                    AllowMultiplePerStory = pt.AllowMultiplePerStory
                };
                await _uow.Repository<EventTaskDefinition>().AddAsync(def, ct);
                clonedTasks.Add(def);
            }
            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(actorUserId, "EventRulesSectionUpdated", nameof(EventRuleVersion), rule.Id, null, new { ev.Id, rule.VersionNo }, ct);
            return MapRule(rule, clonedTasks);
        }, ct);

    public Task<EventDetailDto> UpdateScheduleSectionAsync(int actorUserId, int eventId, UpdateEventScheduleSectionRequestDto request, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await LoadEditableDraftAsync(eventId, actorUserId, ct);
            if (EventVoting.CreatorSchedule(ev) != null)
                throw new BadRequestException("Hãy sửa lịch bình chọn trong form chỉnh sửa sự kiện.");
            if (!Enum.IsDefined(request.Format) || request.Tracks.Count == 0 ||
                request.Tracks.Select(x => x.TrackType).Distinct().Count() != request.Tracks.Count)
                throw new BadRequestException("Cấu hình lịch và nhánh tham gia không hợp lệ.");
            ValidateParticipationSchedule(
                request.Format, request.Tracks, request.MaxParentRegistrations, request.SubmissionCutoffAt,
                request.ReviewCutoffAt, request.VotingStartAt, request.VotingEndAt);

            ev.Status = EventStatus.Draft;
            ev.ApprovedByAdminId = null;
            ev.Format = request.Format;
            ev.TargetAgeBands = string.IsNullOrWhiteSpace(request.TargetAgeBands) ? ev.TargetAgeBands : request.TargetAgeBands.Trim();
            ev.MaxParentRegistrations = request.MaxParentRegistrations;
            ev.SubmissionCutoffAt = request.SubmissionCutoffAt;
            ev.ReviewCutoffAt = request.ReviewCutoffAt;
            ev.VotingStartAt = request.VotingStartAt;
            ev.VotingEndAt = request.VotingEndAt;
            ev.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<Event>().Update(ev);

            var existingTracks = await _uow.Repository<EventTrack>().FindAsync(x => x.EventId == eventId, cancellationToken: ct);
            var retainedTrackIds = new HashSet<int>();
            foreach (var input in request.Tracks)
            {
                var track = existingTracks.SingleOrDefault(x => x.TrackType == input.TrackType);
                if (track == null)
                {
                    track = new EventTrack { EventId = eventId, TrackType = input.TrackType };
                    await _uow.Repository<EventTrack>().AddAsync(track, ct);
                }
                track.RegistrationStartAt = input.RegistrationStartAt;
                track.RegistrationEndAt = input.RegistrationEndAt;
                track.ExecutionStartAt = input.ExecutionStartAt;
                track.ExecutionEndAt = input.ExecutionEndAt;
                track.MaxSubmissionsPerUser = input.MaxSubmissionsPerUser;
                track.GrantAiCredits = input.GrantAiCredits;
                track.GrantSubmitCredits = input.GrantSubmitCredits;
                track.WaiveWeeklyLibraryLimit = input.WaiveWeeklyLibraryLimit;
                track.ClaimWindowHours = input.ClaimWindowHours;
                track.MaxReallocationRounds = input.MaxReallocationRounds;
                track.AwardAllMissionCompleters = input.AwardAllMissionCompleters || track.AwardAllMissionCompleters;
                if (track.Id > 0)
                {
                    _uow.Repository<EventTrack>().Update(track);
                    retainedTrackIds.Add(track.Id);
                }
            }
            var removedTracks = existingTracks.Where(x => !retainedTrackIds.Contains(x.Id)).ToList();
            if (removedTracks.Count > 0)
            {
                var removedIds = removedTracks.Select(x => x.Id).ToHashSet();
                var orphanAwards = await _uow.Repository<EventAward>().FindAsync(a => a.EventId == eventId && a.TrackId.HasValue && removedIds.Contains(a.TrackId.Value), cancellationToken: ct);
                if (orphanAwards.Any(a => a.ParticipationId.HasValue))
                    throw new ConflictException("Không thể xóa nhánh đã có người nhận giải.");
                _uow.Repository<EventAward>().DeleteRange(orphanAwards);
                _uow.Repository<EventTrack>().DeleteRange(removedTracks);
            }
            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(actorUserId, "EventScheduleSectionUpdated", nameof(Event), ev.Id, null, new { ev.Format, TrackCount = request.Tracks.Count }, ct);
            return await BuildDetailAsync(ev, ct);
        }, ct);

    public Task<EventRuleVersionDto> UpdateTasksSectionAsync(int actorUserId, int eventId, UpdateEventTasksSectionRequestDto request, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await LoadEditableDraftAsync(eventId, actorUserId, ct);
            var existingVersions = await _uow.Repository<EventRuleVersion>().FindAsync(r => r.EventId == eventId, cancellationToken: ct);
            var previous = existingVersions.FirstOrDefault(v => v.IsActive);
            var ruleRequest = new UpsertEventRuleRequestDto
            {
                RulesText = previous?.RulesText ?? "Thể lệ chính thức của sự kiện.",
                RewardEligibilityText = previous?.RewardEligibilityText,
                TieBreakerPolicyText = previous?.TieBreakerPolicyText,
                InsufficientVotesPolicyText = previous?.InsufficientVotesPolicyText,
                CreatorRubricJson = previous?.CreatorRubricJson,
                CompletionMode = request.CompletionMode,
                RequiredTaskCount = request.RequiredTaskCount,
                RequiredPoints = request.RequiredPoints,
                Tasks = request.Tasks
            };
            ValidateRules(ruleRequest);
            ev.Status = EventStatus.Draft;
            ev.ApprovedByAdminId = null;
            _uow.Repository<Event>().Update(ev);
            foreach (var old in existingVersions.Where(v => v.IsActive))
            {
                old.IsActive = false;
                _uow.Repository<EventRuleVersion>().Update(old);
            }
            var nextVersionNo = existingVersions.Count == 0 ? 1 : existingVersions.Max(v => v.VersionNo) + 1;
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(ruleRequest);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();
            var rule = new EventRuleVersion
            {
                EventId = eventId,
                VersionNo = nextVersionNo,
                Revision = 1,
                ContentHash = hash,
                RulesText = ruleRequest.RulesText,
                RewardEligibilityText = ruleRequest.RewardEligibilityText,
                TieBreakerPolicyText = ruleRequest.TieBreakerPolicyText,
                InsufficientVotesPolicyText = ruleRequest.InsufficientVotesPolicyText,
                CreatorRubricJson = ruleRequest.CreatorRubricJson,
                CompletionMode = ruleRequest.CompletionMode,
                RequiredTaskCount = ruleRequest.RequiredTaskCount,
                RequiredPoints = ruleRequest.RequiredPoints,
                IsActive = true,
                ActivatedAt = DateTime.UtcNow
            };
            await _uow.Repository<EventRuleVersion>().AddAsync(rule, ct);
            await _uow.SaveChangesAsync(ct);
            var tasks = new List<EventTaskDefinition>();
            for (var i = 0; i < request.Tasks.Count; i++)
            {
                var t = request.Tasks[i];
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
            await _audit.LogAsync(actorUserId, "EventTasksSectionUpdated", nameof(EventRuleVersion), rule.Id, null, new { ev.Id, TaskCount = tasks.Count }, ct);
            return MapRule(rule, tasks);
        }, ct);

    public Task<EventDetailDto> UpdateAwardsSectionAsync(int actorUserId, int eventId, UpdateEventAwardsSectionRequestDto request, CancellationToken ct = default)
        => InTransactionAsync(eventId, async () =>
        {
            var ev = await LoadEditableDraftAsync(eventId, actorUserId, ct);
            if (request.BudgetVnd < 0) throw new BadRequestException("Ngân sách sự kiện không hợp lệ.");
            var tracks = (await _uow.Repository<EventTrack>().FindAsync(x => x.EventId == eventId, cancellationToken: ct))
                .ToDictionary(x => x.TrackType);
            if (tracks.Count == 0)
                throw new BadRequestException("Cần lưu nhánh tham gia ở mục Điều kiện và lịch trước khi cấu hình phần thưởng.");

            var keys = new HashSet<(EventTrackType, string)>();
            decimal total = 0;
            foreach (var award in request.AwardSlots)
            {
                if (!tracks.ContainsKey(award.TrackType) || !Enum.IsDefined(award.RewardType) ||
                    string.IsNullOrWhiteSpace(award.RankOrTier) || string.IsNullOrWhiteSpace(award.RewardName) ||
                    award.SlotCount < 1 || award.UnitValueVnd < 0 || award.CreditGrantAmount < 0 || award.SubmitCreditGrantAmount < 0 ||
                    !keys.Add((award.TrackType, award.RankOrTier.Trim())))
                    throw new BadRequestException("Cấu hình giải thưởng không hợp lệ.");
                if (award.RewardType == EventRewardType.PlatformCredits && award.CreditGrantAmount < 1)
                    throw new BadRequestException("Giải tín dụng phải cấp ít nhất 1 lượt.");
                total += (decimal)award.UnitValueVnd * award.SlotCount;
            }
            if (total > request.BudgetVnd) throw new BadRequestException("Tổng giá trị giải thưởng vượt ngân sách.");

            var existingAwards = await _uow.Repository<EventAward>().FindAsync(x => x.EventId == eventId, cancellationToken: ct);
            if (existingAwards.Any(x => x.ParticipationId.HasValue))
                throw new ConflictException("Không thể thay giải thưởng đã có người nhận.");

            if (tracks.TryGetValue(EventTrackType.Parent, out var parentTrack))
            {
                parentTrack.AwardAllMissionCompleters = request.AwardAllParentMissionCompleters;
                _uow.Repository<EventTrack>().Update(parentTrack);
            }

            ev.Status = EventStatus.Draft;
            ev.ApprovedByAdminId = null;
            ev.BudgetVnd = request.BudgetVnd;
            ev.UpdatedAt = DateTime.UtcNow;
            _uow.Repository<Event>().Update(ev);

            _uow.Repository<EventAward>().DeleteRange(existingAwards);
            await _uow.SaveChangesAsync(ct);
            await SyncAwardSlotsAsync(eventId, tracks, request.AwardSlots, ct);
            await _audit.LogAsync(actorUserId, "EventAwardsSectionUpdated", nameof(Event), ev.Id, null, new { ev.BudgetVnd, SlotCount = request.AwardSlots.Count }, ct);
            return await BuildDetailAsync(ev, ct);
        }, ct);

    private async Task<Event> LoadEditableDraftAsync(int eventId, int actorUserId, CancellationToken ct)
    {
        var ev = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        EventModeratorAccess.RequireOwner(ev, actorUserId);
        if (ev.Status is not (EventStatus.Draft or EventStatus.PendingApproval))
            throw new ConflictException("Chỉ được chỉnh sửa sự kiện khi còn ở trạng thái Nháp hoặc Chờ duyệt.");
        return ev;
    }
}
