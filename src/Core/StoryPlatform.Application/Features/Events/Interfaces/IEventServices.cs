using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Interfaces;

public interface IEventManagementService
{
    Task<IReadOnlyList<EventRewardPlanDto>> ListCreatorRewardPlansAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EventTaskStoryOptionDto>> ListTaskFilterStoriesAsync(int eventId, CancellationToken ct = default);
    Task<IReadOnlyList<EventSummaryDto>> ListEventsAsync(bool includeDrafts, CancellationToken ct = default, int? moderatorId = null);
    Task<EventDetailDto> GetEventDetailAsync(int eventId, CancellationToken ct = default, bool includeUnpublished = false, int? currentUserId = null, UserRole? currentUserRole = null);
    Task<EventDetailDto> UpdateLeadModeratorAsync(int adminUserId, int eventId, UpdateEventLeadModeratorRequestDto request, CancellationToken ct = default);
    Task EnsureStaffEventAccessAsync(int eventId, int actorUserId, CancellationToken ct = default);
    Task<EventDetailDto> CreateEventAsync(int actorUserId, CreateEventRequestDto request, CancellationToken ct = default);
    Task<EventDetailDto> UpdateEventAsync(int actorUserId, int eventId, CreateEventRequestDto request, CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(int adminUserId, int eventId, CancellationToken ct = default);
    Task<CloneLegacyDraftResultDto> CloneLegacyDraftToActivityBasedAsync(int actorUserId, int sourceEventId, CancellationToken ct = default);
    Task<EventRuleVersionDto> UpdateRulesSectionAsync(int actorUserId, int eventId, UpdateEventRulesSectionRequestDto request, CancellationToken ct = default);
    Task<EventDetailDto> UpdateScheduleSectionAsync(int actorUserId, int eventId, UpdateEventScheduleSectionRequestDto request, CancellationToken ct = default);
    Task<EventRuleVersionDto> UpdateTasksSectionAsync(int actorUserId, int eventId, UpdateEventTasksSectionRequestDto request, CancellationToken ct = default);
    Task<EventDetailDto> UpdateAwardsSectionAsync(int actorUserId, int eventId, UpdateEventAwardsSectionRequestDto request, CancellationToken ct = default);
    Task<EventRuleVersionDto> UpsertRuleVersionAsync(int actorUserId, int eventId, UpsertEventRuleRequestDto request, CancellationToken ct = default);
    Task<EventSummaryDto> SubmitForApprovalAsync(int actorUserId, int eventId, CancellationToken ct = default);
    Task<EventSummaryDto> DecideApprovalAsync(int adminUserId, int eventId, EventApprovalDecisionDto request, CancellationToken ct = default);
    Task<EventSummaryDto> PublishEventAsync(int actorUserId, int eventId, CancellationToken ct = default);
    Task<int> SeedFallbackStoriesAsync(int actorUserId, int eventId, SeedEventStoriesRequestDto request, CancellationToken ct = default);
    Task<EventReportSummaryDto> GetSummaryAsync(int eventId, CancellationToken ct = default);
    Task<EventSummaryDto> CloseEventAsync(int actorUserId, int eventId, CancellationToken ct = default);
}

public interface IEventParticipationService
{
    Task<EventParticipationDto> RegisterAsync(int userId, int eventId, RegisterEventRequestDto request, CancellationToken ct = default);
    Task<EventSubmissionDto> SubmitCreatorStoryAsync(int creatorUserId, int eventId, CreatorEventSubmitRequestDto request, CancellationToken ct = default);
    Task<EventSubmissionDto> SubmitParentStoryAsync(int userId, int eventId, CreatorEventSubmitRequestDto request, CancellationToken ct = default);
    Task<EventSubmissionDto> SubmitActivityStoryAsync(int userId, int eventId, CreatorEventSubmitRequestDto request, CancellationToken ct = default);
    Task<EventSubmissionDto> ReviewParentStoryAsync(int userId, int eventId, int submissionId, ReviewParentEventSubmissionDto request, CancellationToken ct = default);
    Task<IReadOnlyList<EventSubmissionDto>> ListMySubmissionsAsync(int userId, int eventId, CancellationToken ct = default);
    Task<EventMissionDashboardDto> GetMissionDashboardAsync(int userId, int eventId, CancellationToken ct = default);
    Task<EventMissionDashboardDto> RecordTaskProgressAsync(int userId, int eventId, int taskDefinitionId, CompleteEventTaskRequestDto request, CancellationToken ct = default);
    Task<EventVoteDto> CastVoteAsync(int parentUserId, int eventId, CastEventVoteRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<EventVoteDto>> ListVotesAsync(int eventId, CancellationToken ct = default);
    Task<EventVoteDto> DisqualifyVoteAsync(int moderatorUserId, int voteId, DisqualifyEventVoteRequestDto request, CancellationToken ct = default);
    Task<EventLeaderboardDto> GetLeaderboardAsync(int eventId, bool includeHidden, CancellationToken ct = default, int? userId = null);
    Task<int> VerifyEventParticipationsAsync(int actorUserId, int eventId, CancellationToken ct = default);
    Task<EventAppealDto> CreateAppealAsync(int userId, int eventId, CreateAppealRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<EventAppealDto>> ListAppealsAsync(int eventId, CancellationToken ct = default);
    Task<EventAppealDto> DecideAppealAsync(int actorUserId, int appealId, DecideAppealRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<EventSubmissionDto>> ListSubmissionsAsync(int eventId, CancellationToken ct = default);
    Task<EventSubmissionDto?> GetMySubmissionAsync(int creatorUserId, int eventId, CancellationToken ct = default);
    Task<EventParticipationDto?> GetMyParticipationAsync(int userId, int eventId, EventTrackType trackType, CancellationToken ct = default);
    Task<IReadOnlyList<EventParticipationDto>> ListMyParticipationsAsync(int userId, CancellationToken ct = default);
}

public interface IEventRewardService
{
    Task EvaluateSubmissionAsync(int evaluatorUserId, int submissionId, EvaluateSubmissionRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<EventAwardSlotDto>> ProposeAwardsAsync(int moderatorUserId, int eventId, CancellationToken ct = default);
    Task<IReadOnlyList<EventAwardSlotDto>> ApproveAwardsAsync(int adminUserId, int eventId, CancellationToken ct = default);
    Task<IReadOnlyList<EventAwardSlotDto>> GetMyRewardsAsync(int userId, CancellationToken ct = default);
    Task<EventAwardSlotDto> ClaimRewardAsync(int userId, int awardId, ClaimRewardRequestDto request, CancellationToken ct = default);
    Task<int> ProcessExpiredClaimsAsync(int actorUserId, int eventId, CancellationToken ct = default);
    Task<EventRewardRecipientDto> GetRewardRecipientAsync(int operatorUserId, int awardId, CancellationToken ct = default);
    Task<EventAwardNotificationResultDto> ResendAwardNotificationsAsync(int actorUserId, int eventId, CancellationToken ct = default);
    Task<EventAwardSlotDto> UpdateFulfillmentAsync(int operatorUserId, int awardId, FulfillRewardRequestDto request, CancellationToken ct = default);
}

public interface IEventIntegrationHooks
{
    Task EnsureStoryNotLockedInEventAsync(int storyId, CancellationToken ct = default);
    Task<bool> IsStoryExemptFromWeeklyLimitAsync(int ownerUserId, int storyId, CancellationToken ct = default);
    Task RecordActivityForActiveEventsAsync(
        int ownerUserId,
        int? childProfileId,
        int? storyId,
        TaskVerifierType verifierType,
        int? readingSessionId = null,
        int? storyRatingId = null,
        CancellationToken ct = default);
}
