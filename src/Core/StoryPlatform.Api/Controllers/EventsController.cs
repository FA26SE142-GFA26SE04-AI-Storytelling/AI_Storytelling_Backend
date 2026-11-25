using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Interfaces;

namespace StoryPlatform.Api.Controllers;

[Route("api/v1/events")]
public class EventsController : BaseApiController
{
    private readonly IEventManagementService _management;
    private readonly IEventParticipationService _participation;
    private readonly IEventRewardService _rewards;

    public EventsController(
        IEventManagementService management,
        IEventParticipationService participation,
        IEventRewardService rewards)
    {
        _management = management;
        _participation = participation;
        _rewards = rewards;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventSummaryDto>>>> List([FromQuery] bool includeDrafts = false, CancellationToken ct = default)
    {
        var role = GetCurrentUserRole();
        var allowDrafts = includeDrafts && role is "Moderator" or "Administrator";
        return HandleResult(await _management.ListEventsAsync(allowDrafts, ct, role == "Moderator" ? GetCurrentUserId() : null));
    }

    [HttpGet("reward-plans")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventRewardPlanDto>>>> ListRewardPlans(CancellationToken ct = default) =>
        HandleResult(await _management.ListCreatorRewardPlansAsync(ct));

    [HttpGet("{eventId:int}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<EventDetailDto>>> Detail(int eventId, CancellationToken ct = default)
    {
        var roleStr = GetCurrentUserRole();
        StoryPlatform.Domain.Enums.UserRole? parsedRole = System.Enum.TryParse<StoryPlatform.Domain.Enums.UserRole>(roleStr, out var r) ? r : null;
        return HandleResult(await _management.GetEventDetailAsync(eventId, ct,
            includeUnpublished: roleStr is "Moderator" or "Administrator",
            currentUserId: GetCurrentUserId(),
            currentUserRole: parsedRole));
    }

    [HttpPut("{eventId:int}/lead-moderator")]
    [HasPermission(Permissions.Admin.EventApprove)]
    public async Task<ActionResult<ApiResponse<EventDetailDto>>> UpdateLeadModerator(int eventId,
        [FromBody] UpdateEventLeadModeratorRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _management.UpdateLeadModeratorAsync(GetCurrentUserId(), eventId, request, ct),
            "Đã cập nhật phân công Moderator.");

    [HttpPost("{eventId:int}/clone-to-activity-draft")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<CloneLegacyDraftResultDto>>> CloneToActivityDraft(int eventId, CancellationToken ct = default) =>
        HandleResult(await _management.CloneLegacyDraftToActivityBasedAsync(GetCurrentUserId(), eventId, ct),
            "Đã sao chép bản nháp sang mô hình sự kiện theo hoạt động.");

    [HttpPost]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventDetailDto>>> Create([FromBody] CreateEventRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _management.CreateEventAsync(GetCurrentUserId(), request, ct), "Đã tạo bản nháp sự kiện.");

    [HttpPut("{eventId:int}")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventDetailDto>>> Update(int eventId, [FromBody] CreateEventRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _management.UpdateEventAsync(GetCurrentUserId(), eventId, request, ct), "Đã cập nhật sự kiện.");

    [HttpDelete("{eventId:int}")]
    [HasPermission(Permissions.Admin.EventApprove)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteDraft(int eventId, CancellationToken ct = default) =>
        HandleResult(await _management.DeleteDraftAsync(GetCurrentUserId(), eventId, ct), "Đã xóa bản nháp sự kiện.");

    [HttpPut("{eventId:int}/rules-section")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventRuleVersionDto>>> UpdateRulesSection(int eventId, [FromBody] UpdateEventRulesSectionRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _management.UpdateRulesSectionAsync(GetCurrentUserId(), eventId, request, ct), "Đã lưu mục thể lệ sự kiện.");

    [HttpPut("{eventId:int}/schedule-section")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventDetailDto>>> UpdateScheduleSection(int eventId, [FromBody] UpdateEventScheduleSectionRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _management.UpdateScheduleSectionAsync(GetCurrentUserId(), eventId, request, ct), "Đã lưu mục điều kiện và lịch.");

    [HttpPut("{eventId:int}/tasks-section")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventRuleVersionDto>>> UpdateTasksSection(int eventId, [FromBody] UpdateEventTasksSectionRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _management.UpdateTasksSectionAsync(GetCurrentUserId(), eventId, request, ct), "Đã lưu mục nhiệm vụ.");

    [HttpPut("{eventId:int}/awards-section")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventDetailDto>>> UpdateAwardsSection(int eventId, [FromBody] UpdateEventAwardsSectionRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _management.UpdateAwardsSectionAsync(GetCurrentUserId(), eventId, request, ct), "Đã lưu mục phần thưởng.");

    [HttpPost("{eventId:int}/rules")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventRuleVersionDto>>> UpsertRules(int eventId, [FromBody] UpsertEventRuleRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _management.UpsertRuleVersionAsync(GetCurrentUserId(), eventId, request, ct), "Đã lưu phiên bản thể lệ và nhiệm vụ động.");

    [HttpPost("{eventId:int}/submit-approval")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventSummaryDto>>> SubmitApproval(int eventId, CancellationToken ct = default) =>
        HandleResult(await _management.SubmitForApprovalAsync(GetCurrentUserId(), eventId, ct), "Đã gửi sự kiện chờ Admin phê duyệt.");

    [HttpPost("{eventId:int}/approve")]
    [HasPermission(Permissions.Admin.EventApprove)]
    public async Task<ActionResult<ApiResponse<EventSummaryDto>>> Approve(int eventId, [FromBody] EventApprovalDecisionDto request, CancellationToken ct = default) =>
        HandleResult(await _management.DecideApprovalAsync(GetCurrentUserId(), eventId, request, ct));

    [HttpPost("{eventId:int}/publish")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventSummaryDto>>> Publish(int eventId, CancellationToken ct = default) =>
        HandleResult(await _management.PublishEventAsync(GetCurrentUserId(), eventId, ct), "Đã phát hành sự kiện.");

    [HttpGet("{eventId:int}/task-filter-stories")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventTaskStoryOptionDto>>>> ListTaskFilterStories(
        int eventId, CancellationToken ct = default)
    {
        await _management.EnsureStaffEventAccessAsync(eventId, GetCurrentUserId(), ct);
        return HandleResult(await _management.ListTaskFilterStoriesAsync(eventId, ct));
    }

    [HttpPost("{eventId:int}/seed-stories")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<int>>> SeedStories(int eventId, [FromBody] SeedEventStoriesRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _management.SeedFallbackStoriesAsync(GetCurrentUserId(), eventId, request, ct), "Đã bổ sung truyện dự phòng vào thư viện sự kiện.");

    [HttpGet("{eventId:int}/summary")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<EventReportSummaryDto>>> Summary(int eventId, CancellationToken ct = default)
    {
        await _management.EnsureStaffEventAccessAsync(eventId, GetCurrentUserId(), ct);
        return HandleResult(await _management.GetSummaryAsync(eventId, ct));
    }

    [HttpPost("{eventId:int}/close")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventSummaryDto>>> Close(int eventId, CancellationToken ct = default) =>
        HandleResult(await _management.CloseEventAsync(GetCurrentUserId(), eventId, ct), "Đã tổng kết và đóng sự kiện.");

    [HttpPost("{eventId:int}/register")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventParticipationDto>>> Register(int eventId, [FromBody] RegisterEventRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.RegisterAsync(GetCurrentUserId(), eventId, request, ct), "Đăng ký tham gia sự kiện thành công.");

    [HttpPost("{eventId:int}/creator-submit")]
    [HasPermission(Permissions.Creator.Studio)]
    public async Task<ActionResult<ApiResponse<EventSubmissionDto>>> CreatorSubmit(int eventId, [FromBody] CreatorEventSubmitRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.SubmitCreatorStoryAsync(GetCurrentUserId(), eventId, request, ct), "Đã nộp truyện dự thi.");

    [HttpPost("{eventId:int}/parent-submit")]
    [HasPermission(Permissions.Parent.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventSubmissionDto>>> ParentSubmit(int eventId, [FromBody] CreatorEventSubmitRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.SubmitParentStoryAsync(GetCurrentUserId(), eventId, request, ct), "Đã nộp bài dự thi của Parent.");

    [HttpPost("{eventId:int}/activities/submission/submit")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventSubmissionDto>>> SubmitActivityStory(int eventId, [FromBody] CreatorEventSubmitRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.SubmitActivityStoryAsync(GetCurrentUserId(), eventId, request, ct), "Đã nộp bài dự thi.");

    [HttpGet("{eventId:int}/my-submissions")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventSubmissionDto>>>> MySubmissions(int eventId, CancellationToken ct = default) =>
        HandleResult(await _participation.ListMySubmissionsAsync(GetCurrentUserId(), eventId, ct));

    [HttpPost("{eventId:int}/submissions/{submissionId:int}/review-parent")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<EventSubmissionDto>>> ReviewParent(int eventId, int submissionId, [FromBody] ReviewParentEventSubmissionDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.ReviewParentStoryAsync(GetCurrentUserId(), eventId, submissionId, request, ct));

    [HttpGet("{eventId:int}/submissions")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventSubmissionDto>>>> ListSubmissions(int eventId, CancellationToken ct = default)
    {
        await _management.EnsureStaffEventAccessAsync(eventId, GetCurrentUserId(), ct);
        return HandleResult(await _participation.ListSubmissionsAsync(eventId, ct));
    }

    [HttpGet("{eventId:int}/my-submission")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventSubmissionDto?>>> MySubmission(int eventId, CancellationToken ct = default) =>
        HandleResult<EventSubmissionDto?>(await _participation.GetMySubmissionAsync(GetCurrentUserId(), eventId, ct));

    [HttpGet("{eventId:int}/my-participation")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventParticipationDto?>>> MyParticipation(
        int eventId, [FromQuery] StoryPlatform.Domain.Enums.EventTrackType trackType = StoryPlatform.Domain.Enums.EventTrackType.Creator, CancellationToken ct = default) =>
        HandleResult<EventParticipationDto?>(await _participation.GetMyParticipationAsync(GetCurrentUserId(), eventId, trackType, ct));

    [HttpGet("my-participations")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventParticipationDto>>>> MyParticipations(CancellationToken ct = default) =>
        HandleResult(await _participation.ListMyParticipationsAsync(GetCurrentUserId(), ct));

    [HttpGet("{eventId:int}/mission-dashboard")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventMissionDashboardDto>>> MissionDashboard(int eventId, CancellationToken ct = default) =>
        HandleResult(await _participation.GetMissionDashboardAsync(GetCurrentUserId(), eventId, ct));

    [HttpPost("{eventId:int}/votes")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventVoteDto>>> CastVote(int eventId, [FromBody] CastEventVoteRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.CastVoteAsync(GetCurrentUserId(), eventId, request, ct), "Đã ghi nhận phiếu bình chọn.");

    [HttpGet("{eventId:int}/votes")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventVoteDto>>>> ListVotes(int eventId, CancellationToken ct = default)
    {
        await _management.EnsureStaffEventAccessAsync(eventId, GetCurrentUserId(), ct);
        return HandleResult(await _participation.ListVotesAsync(eventId, ct));
    }

    [HttpPost("votes/{voteId:int}/disqualify")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<EventVoteDto>>> DisqualifyVote(int voteId, [FromBody] DisqualifyEventVoteRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.DisqualifyVoteAsync(GetCurrentUserId(), voteId, request, ct), "Đã loại phiếu bình chọn vi phạm.");

    [HttpGet("{eventId:int}/leaderboard")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<EventLeaderboardDto>>> Leaderboard(int eventId, CancellationToken ct = default)
    {
        var isStaff = GetCurrentUserRole() is "Moderator" or "Administrator";
        var actor = GetCurrentUserId();
        if (isStaff) await _management.EnsureStaffEventAccessAsync(eventId, actor, ct);
        return HandleResult(await _participation.GetLeaderboardAsync(eventId, isStaff, ct, actor));
    }

    [HttpPost("{eventId:int}/tasks/{taskDefinitionId:int}/complete")]
    [HasPermission(Permissions.Parent.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventMissionDashboardDto>>> CompleteTask(int eventId, int taskDefinitionId, [FromBody] CompleteEventTaskRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.RecordTaskProgressAsync(GetCurrentUserId(), eventId, taskDefinitionId, request, ct), "Đã ghi nhận tiến độ nhiệm vụ.");

    [HttpPost("{eventId:int}/verify")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<int>>> Verify(int eventId, CancellationToken ct = default) =>
        HandleResult(await _participation.VerifyEventParticipationsAsync(GetCurrentUserId(), eventId, ct), "Đã chạy xác minh kết quả sự kiện.");

    [HttpPost("{eventId:int}/appeals")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventAppealDto>>> CreateAppeal(int eventId, [FromBody] CreateAppealRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.CreateAppealAsync(GetCurrentUserId(), eventId, request, ct), "Đã gửi đơn khiếu nại.");

    [HttpGet("{eventId:int}/appeals")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventAppealDto>>>> ListAppeals(int eventId, CancellationToken ct = default)
    {
        await _management.EnsureStaffEventAccessAsync(eventId, GetCurrentUserId(), ct);
        return HandleResult(await _participation.ListAppealsAsync(eventId, ct));
    }

    [HttpPost("appeals/{appealId:int}/decide")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<EventAppealDto>>> DecideAppeal(int appealId, [FromBody] DecideAppealRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _participation.DecideAppealAsync(GetCurrentUserId(), appealId, request, ct), "Đã xử lý đơn khiếu nại.");

    [HttpPost("submissions/{submissionId:int}/evaluate")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<object>>> EvaluateSubmission(int submissionId, [FromBody] EvaluateSubmissionRequestDto request, CancellationToken ct = default)
    {
        await _rewards.EvaluateSubmissionAsync(GetCurrentUserId(), submissionId, request, ct);
        return HandleResult<object>(new { evaluated = true }, "Đã lưu điểm chấm bài dự thi.");
    }

    [HttpPost("{eventId:int}/awards/propose")]
    [HasPermission(Permissions.Moderation.EventManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventAwardSlotDto>>>> ProposeAwards(int eventId, CancellationToken ct = default) =>
        HandleResult(await _rewards.ProposeAwardsAsync(GetCurrentUserId(), eventId, ct), "Đã đề xuất danh sách nhận giải.");

    [HttpPost("{eventId:int}/awards/approve")]
    [HasPermission(Permissions.Admin.EventApprove)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventAwardSlotDto>>>> ApproveAwards(int eventId, CancellationToken ct = default) =>
        HandleResult(await _rewards.ApproveAwardsAsync(GetCurrentUserId(), eventId, ct), "Đã phê duyệt danh sách nhận giải.");

    [HttpGet("my-rewards")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventAwardSlotDto>>>> MyRewards(CancellationToken ct = default) =>
        HandleResult(await _rewards.GetMyRewardsAsync(GetCurrentUserId(), ct));

    [HttpPost("awards/{awardId:int}/claim")]
    [HasPermission(Permissions.Shared.EventParticipate)]
    public async Task<ActionResult<ApiResponse<EventAwardSlotDto>>> ClaimReward(int awardId, [FromBody] ClaimRewardRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _rewards.ClaimRewardAsync(GetCurrentUserId(), awardId, request, ct), "Đã xác nhận nhận thưởng.");

    [HttpPost("{eventId:int}/awards/process-expired")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<int>>> ProcessExpired(int eventId, CancellationToken ct = default) =>
        HandleResult(await _rewards.ProcessExpiredClaimsAsync(GetCurrentUserId(), eventId, ct), "Đã xử lý các suất thưởng quá hạn.");

    [HttpGet("awards/{awardId:int}/recipient")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ApiResponse<EventRewardRecipientDto>>> RewardRecipient(int awardId, CancellationToken ct = default) =>
        HandleResult(await _rewards.GetRewardRecipientAsync(GetCurrentUserId(), awardId, ct));

    [HttpPost("{eventId:int}/awards/notify")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<EventAwardNotificationResultDto>>> NotifyAwardWinners(int eventId, CancellationToken ct = default) =>
        HandleResult(await _rewards.ResendAwardNotificationsAsync(GetCurrentUserId(), eventId, ct));

    [HttpPost("awards/{awardId:int}/fulfill")]
    [HasPermission(Permissions.Moderation.EventOperate)]
    public async Task<ActionResult<ApiResponse<EventAwardSlotDto>>> Fulfill(int awardId, [FromBody] FulfillRewardRequestDto request, CancellationToken ct = default) =>
        HandleResult(await _rewards.UpdateFulfillmentAsync(GetCurrentUserId(), awardId, request, ct), "Đã cập nhật trạng thái trao thưởng.");
}
