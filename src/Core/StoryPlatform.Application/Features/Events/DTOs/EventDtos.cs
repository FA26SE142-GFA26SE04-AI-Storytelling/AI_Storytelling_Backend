using System;
using System.Collections.Generic;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.DTOs;

public sealed record EventRewardPlanDto(int Id, string Name, int AiCredits, int SubmitCredits);

public sealed record EventTaskStoryOptionDto(
    int Id, string Title, EventStorySource Source, bool CanUseForReading, bool HasLearningContent);

public class EventSummaryDto
{
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? BannerUrl { get; set; }
    public int MainCategoryId { get; set; }
    public string? MainCategoryName { get; set; }
    public string TargetAgeBands { get; set; } = string.Empty;
    public EventStatus Status { get; set; }
    public EventConfigVersion ConfigVersion { get; set; } = EventConfigVersion.LegacyRoleBased;
    public EventFormat Format { get; set; } = EventFormat.Standard;
    public EventScoringMode ScoringMode { get; set; } = EventScoringMode.Rubric;
    public int MinVotesPerSubmission { get; set; } = 5;
    public int? MaxParentRegistrations { get; set; }
    public DateTime? SubmissionCutoffAt { get; set; }
    public DateTime? ReviewCutoffAt { get; set; }
    public DateTime? VotingStartAt { get; set; }
    public DateTime? VotingEndAt { get; set; }
    public EventVotingScheduleDto? CreatorVotingSchedule { get; set; }
    public DateTime? ResultsFinalizedAt { get; set; }
    public long BudgetVnd { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class EventTrackDto
{
    public int Id { get; set; }
    public EventTrackType TrackType { get; set; }
    public DateTime RegistrationStartAt { get; set; }
    public DateTime RegistrationEndAt { get; set; }
    public DateTime ExecutionStartAt { get; set; }
    public DateTime ExecutionEndAt { get; set; }
    public int MaxSubmissionsPerUser { get; set; }
    public int GrantAiCredits { get; set; }
    public int GrantSubmitCredits { get; set; }
    public bool WaiveWeeklyLibraryLimit { get; set; }
    public int ClaimWindowHours { get; set; }
    public int MaxReallocationRounds { get; set; }
    public bool AwardAllMissionCompleters { get; set; }
}

public class EventTaskDefinitionDto
{
    public int Id { get; set; }
    public string TaskCode { get; set; } = string.Empty;
    public EventTrackType? TargetTrackType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public TaskVerifierType VerifierType { get; set; }
    public int TargetValue { get; set; }
    public string? ConfigJson { get; set; }
    public bool IsMandatory { get; set; }
    public int Points { get; set; }
    public bool AllowMultiplePerStory { get; set; }
}

public class EventRuleVersionDto
{
    public int Id { get; set; }
    public int VersionNo { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string RulesText { get; set; } = string.Empty;
    public string? RewardEligibilityText { get; set; }
    public string? TieBreakerPolicyText { get; set; }
    public string? InsufficientVotesPolicyText { get; set; }
    public string? CreatorRubricJson { get; set; }
    public TrackCompletionMode CompletionMode { get; set; }
    public int? RequiredTaskCount { get; set; }
    public int? RequiredPoints { get; set; }
    public bool IsActive { get; set; }
    public List<EventTaskDefinitionDto> Tasks { get; set; } = new();
}

public class EventAwardSlotDto
{
    public bool NotificationFailed { get; set; }
    public int Id { get; set; }
    public int? TrackId { get; set; }
    public int? EventActivityId { get; set; }
    public EventActivityType? ActivityType { get; set; }
    public EventAwardResultSource? ResultSource { get; set; }
    public EventAudience? TargetAudience { get; set; }
    public string RankOrTier { get; set; } = string.Empty;
    public int SlotIndex { get; set; }
    public EventRewardType RewardType { get; set; }
    public string RewardName { get; set; } = string.Empty;
    public long UnitValueVnd { get; set; }
    public int CreditGrantAmount { get; set; }
    public int SubmitCreditGrantAmount { get; set; }
    public int? ParticipationId { get; set; }
    public EventAwardStatus Status { get; set; }
    public DateTime? ClaimDeadlineAt { get; set; }
}

public class EventActivityDto
{
    public int Id { get; set; }
    public EventActivityType ActivityType { get; set; }
    public EventAudience AllowedAudience { get; set; }
    public DateTime RegistrationStartAt { get; set; }
    public DateTime RegistrationEndAt { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public DateTime? ReviewCutoffAt { get; set; }
    public int GrantAiCredits { get; set; }
    public int? MaxSubmissionsPerUser { get; set; }
    public int? MaxRegistrations { get; set; }
    public bool WaiveWeeklyLibraryLimit { get; set; }
    public EventReadingStorySource StorySource { get; set; } = EventReadingStorySource.Both;
    public int MinVotesPerSubmission { get; set; } = 5;
    public int ClaimWindowHours { get; set; } = 72;
    public int MaxReallocationRounds { get; set; } = 1;
    public bool AwardAllMissionCompleters { get; set; }
    public string? ConfigJson { get; set; }
}

public class EventActivityUserStateDto
{
    public int ActivityId { get; set; }
    public EventActivityType ActivityType { get; set; }
    public EventAudience AllowedAudience { get; set; }
    public string LifecycleStatus { get; set; } = "NotOpen";
    public bool IsEligible { get; set; }
    public bool IsRegistered { get; set; }
    public int? ParticipationId { get; set; }
    public EventParticipationStatus? ParticipationStatus { get; set; }
    public DateTime? AppealDeadlineAt { get; set; }
    public bool CanRegister { get; set; }
    public bool CanSubmit { get; set; }
    public bool CanDoMissions { get; set; }
    public bool CanVote { get; set; }
    public int SubmissionsCount { get; set; }
    public int? MaxSubmissionsPerUser { get; set; }
}

public class UpsertEventActivityDto
{
    public EventActivityType ActivityType { get; set; }
    public EventAudience AllowedAudience { get; set; } = EventAudience.Both;
    public DateTime RegistrationStartAt { get; set; }
    public DateTime RegistrationEndAt { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public DateTime? ReviewCutoffAt { get; set; }
    public int GrantAiCredits { get; set; }
    public int? MaxSubmissionsPerUser { get; set; }
    public int? MaxRegistrations { get; set; }
    public bool WaiveWeeklyLibraryLimit { get; set; } = true;
    public EventReadingStorySource StorySource { get; set; } = EventReadingStorySource.Both;
    public int MinVotesPerSubmission { get; set; } = 5;
    public int ClaimWindowHours { get; set; } = 72;
    public int MaxReallocationRounds { get; set; } = 1;
    public bool AwardAllMissionCompleters { get; set; }
    public string? ConfigJson { get; set; }
}

public sealed record UpdateEventLeadModeratorRequestDto(int ModeratorId, List<int>? AssistantModeratorIds = null);

public class EventDetailDto : EventSummaryDto
{
    public bool? CanManageEvent { get; set; }
    public int? LeadModeratorId { get; set; }
    public List<int>? AssistantModeratorIds { get; set; }
    public bool? CanOperateEvent { get; set; }
    public List<EventTrackDto> Tracks { get; set; } = new();
    public List<EventActivityDto> Activities { get; set; } = new();
    public List<EventActivityUserStateDto> MyActivityStates { get; set; } = new();
    public EventRuleVersionDto? ActiveRule { get; set; }
    public List<EventAwardSlotDto> Awards { get; set; } = new();
    public int EventStoriesCount { get; set; }
}

public class CloneLegacyDraftResultDto
{
    public EventDetailDto Draft { get; set; } = null!;
    public List<string> UnmappedWarnings { get; set; } = new();
}

public class CreateEventRequestDto
{
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? BannerUrl { get; set; }
    public int MainCategoryId { get; set; }
    public string? MainCategoryName { get; set; }
    public string TargetAgeBands { get; set; } = "6-8,9-10";
    public EventConfigVersion ConfigVersion { get; set; } = EventConfigVersion.LegacyRoleBased;
    public EventFormat Format { get; set; } = EventFormat.Standard;
    public EventScoringMode ScoringMode { get; set; } = EventScoringMode.Rubric;
    public int MinVotesPerSubmission { get; set; } = 5;
    public int? MaxParentRegistrations { get; set; }
    public DateTime? SubmissionCutoffAt { get; set; }
    public DateTime? ReviewCutoffAt { get; set; }
    public DateTime? VotingStartAt { get; set; }
    public DateTime? VotingEndAt { get; set; }
    public EventVotingScheduleDto? CreatorVotingSchedule { get; set; }
    public long BudgetVnd { get; set; }
    public List<UpsertEventTrackDto> Tracks { get; set; } = new();
    public List<UpsertEventActivityDto> Activities { get; set; } = new();
    public List<UpsertEventAwardSlotDto> AwardSlots { get; set; } = new();
}

public sealed record EventVotingScheduleDto(DateTime SubmissionCutoffAt, DateTime ReviewCutoffAt, DateTime VotingStartAt, DateTime VotingEndAt);

public class UpsertEventTrackDto
{
    public EventTrackType TrackType { get; set; }
    public DateTime RegistrationStartAt { get; set; }
    public DateTime RegistrationEndAt { get; set; }
    public DateTime ExecutionStartAt { get; set; }
    public DateTime ExecutionEndAt { get; set; }
    public int MaxSubmissionsPerUser { get; set; } = 1;
    public int GrantAiCredits { get; set; }
    public int GrantSubmitCredits { get; set; }
    public bool WaiveWeeklyLibraryLimit { get; set; } = true;
    public int ClaimWindowHours { get; set; } = 72;
    public int MaxReallocationRounds { get; set; } = 1;
    public bool AwardAllMissionCompleters { get; set; }
}

public class UpsertEventAwardSlotDto
{
    public EventTrackType TrackType { get; set; } = EventTrackType.Creator;
    public EventActivityType? ActivityType { get; set; }
    public int? EventActivityId { get; set; }
    public EventAwardResultSource? ResultSource { get; set; }
    public EventAudience? TargetAudience { get; set; }
    public string RankOrTier { get; set; } = string.Empty;
    public int SlotCount { get; set; } = 1;
    public EventRewardType RewardType { get; set; } = EventRewardType.PhysicalGift;
    public string RewardName { get; set; } = string.Empty;
    public long UnitValueVnd { get; set; }
    public int CreditGrantAmount { get; set; }
    public int SubmitCreditGrantAmount { get; set; }
}

public class UpsertEventRuleRequestDto
{
    public string RulesText { get; set; } = string.Empty;
    public string? RewardEligibilityText { get; set; }
    public string? TieBreakerPolicyText { get; set; }
    public string? InsufficientVotesPolicyText { get; set; }
    public string? CreatorRubricJson { get; set; }
    public TrackCompletionMode CompletionMode { get; set; } = TrackCompletionMode.AllMandatory;
    public int? RequiredTaskCount { get; set; }
    public int? RequiredPoints { get; set; }
    public List<UpsertEventTaskDto> Tasks { get; set; } = new();
}

public class UpsertEventTaskDto
{
    public string? TaskCode { get; set; }
    public EventTrackType? TargetTrackType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public TaskVerifierType VerifierType { get; set; }
    public int TargetValue { get; set; } = 1;
    public string? ConfigJson { get; set; }
    public bool IsMandatory { get; set; } = true;
    public int Points { get; set; } = 10;
    public bool AllowMultiplePerStory { get; set; }
}

public class EventApprovalDecisionDto
{
    public bool Approved { get; set; }
    public string? Note { get; set; }
}

public class SeedEventStoriesRequestDto
{
    public List<int> StoryIds { get; set; } = new();
}

public class RegisterEventRequestDto
{
    public EventTrackType? TrackType { get; set; }
    public int? EventActivityId { get; set; }
    public EventActivityType? ActivityType { get; set; }
    public bool AcceptRules { get; set; }
}

public class EventParticipationDto
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int? TrackId { get; set; }
    public EventTrackType TrackType { get; set; }
    public int? EventActivityId { get; set; }
    public EventActivityType? ActivityType { get; set; }
    public int UserId { get; set; }
    public int AcceptedRuleVersionId { get; set; }
    public EventParticipationStatus Status { get; set; }
    public DateTime? MissionCompletedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? AppealDeadlineAt { get; set; }
    public string? VerificationNotesJson { get; set; }
    public string? SafetyWarning { get; set; }
}

public class CreatorEventSubmitRequestDto
{
    public int StoryId { get; set; }
    public bool ConsentToEventSharing { get; set; }
}

public class EventSubmissionDto
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int? EventActivityId { get; set; }
    public int AuthorUserId { get; set; }
    public int StoryId { get; set; }
    public int CurrentStoryVersionId { get; set; }
    public int? LockedStoryVersionId { get; set; }
    public double? SimilarityScore { get; set; }
    public int? SimilarityReferenceStoryId { get; set; }
    public EventSubmissionStatus Status { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public decimal? AverageScore { get; set; }
    public int EvaluationCount { get; set; }
    public int ValidVoteCount { get; set; }
    public decimal? AverageStars { get; set; }
    public string? SubmittedTitle { get; set; }
    public string? SubmittedText { get; set; }
}

public sealed record ReviewParentEventSubmissionDto(int StoryVersionId, DateTime? SubmittedAt, bool Approved);

public class CompleteEventTaskRequestDto
{
    public int? ChildProfileId { get; set; }
    public int? StoryId { get; set; }
    public int? ReadingSessionId { get; set; }
    public int? StoryRatingId { get; set; }
    public string? SelfDeclaredPayloadJson { get; set; }
}

public class EventTaskProgressItemDto
{
    public int TaskDefinitionId { get; set; }
    public string TaskCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TaskVerifierType VerifierType { get; set; }
    public int TargetValue { get; set; }
    public int CurrentValue { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsMandatory { get; set; }
    public int Points { get; set; }
}

public class EventMissionDashboardDto
{
    public EventParticipationDto Participation { get; set; } = null!;
    public TrackCompletionMode CompletionMode { get; set; }
    public int? RequiredTaskCount { get; set; }
    public int? RequiredPoints { get; set; }
    public int CompletedTasksCount { get; set; }
    public int EarnedPoints { get; set; }
    public bool AllRequirementsMet { get; set; }
    public List<EventTaskProgressItemDto> Tasks { get; set; } = new();
    public List<EventStoryLibraryItemDto> Stories { get; set; } = new();
    public List<EventStoryLibraryItemDto> VotingStories { get; set; } = new();
}

public class EventStoryLibraryItemDto
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AgeBand { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public EventStorySource Source { get; set; }
    public bool HasFamilyRead { get; set; }
    public string? VotingText { get; set; }
    public int? MyVoteStars { get; set; }
    public string? MyVoteComment { get; set; }
}

public class CreateAppealRequestDto
{
    public int ParticipationId { get; set; }
    public string ReasonText { get; set; } = string.Empty;
    public string? EvidenceJson { get; set; }
}

public class DecideAppealRequestDto
{
    public EventAppealStatus Decision { get; set; }
    public string ResolutionNote { get; set; } = string.Empty;
}

public class EventAppealDto
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int ParticipationId { get; set; }
    public int AppellantUserId { get; set; }
    public string ReasonText { get; set; } = string.Empty;
    public EventAppealStatus Status { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class EvaluateSubmissionRequestDto
{
    public string CriteriaScoresJson { get; set; } = "{}";
    public decimal TotalWeightedScore { get; set; }
    public string? Comment { get; set; }
}

public class ClaimRewardRequestDto
{
    public string? RecipientName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ShippingAddress { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountHolder { get; set; }
}

public sealed class EventRewardRecipientDto
{
    public int AwardId { get; set; }
    public EventRewardType RewardType { get; set; }
    public string? RecipientName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ShippingAddress { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountHolder { get; set; }
    public string? LegacyBankAccountText { get; set; }
    public bool IsLegacyPlaintext { get; set; }
}

public sealed record EventAwardNotificationResultDto(int SentCount, IReadOnlyList<int> FailedAwardIds);

public class FulfillRewardRequestDto
{
    public bool Delivered { get; set; }
    public string? TrackingOrBankTxCode { get; set; }
    public string? FailureReason { get; set; }
}

public class EventReportSummaryDto
{
    public int EventId { get; set; }
    public EventStatus Status { get; set; }
    public int CreatorRegistrations { get; set; }
    public int CreatorSubmissions { get; set; }
    public int ParentSubmissions { get; set; }
    public int PublishedEventStories { get; set; }
    public int ParentRegistrations { get; set; }
    public int ParentVerifiedCount { get; set; }
    public int AwardsProposedOrApproved { get; set; }
    public int AwardsFulfilled { get; set; }
    public long ApprovedBudgetVnd { get; set; }
    public long FulfilledValueVnd { get; set; }
    public int Retention7DayActiveUsers { get; set; }
    public int Retention30DayActiveUsers { get; set; }
    public int FamiliesCompletedFirstRead { get; set; }
    public decimal SecondStoryReturnRate { get; set; }
    public decimal SubmissionsMeetingVoteThresholdRate { get; set; }
}

public class UpdateEventRulesSectionRequestDto
{
    public string RulesText { get; set; } = string.Empty;
    public string? RewardEligibilityText { get; set; }
    public string? TieBreakerPolicyText { get; set; }
    public string? InsufficientVotesPolicyText { get; set; }
    public EventScoringMode ScoringMode { get; set; } = EventScoringMode.ParentVoteAverage;
    public int MinVotesPerSubmission { get; set; } = 5;
    public string? CreatorRubricJson { get; set; }
}

public class UpdateEventScheduleSectionRequestDto
{
    public EventFormat Format { get; set; } = EventFormat.SharedCreatorParent;
    public string TargetAgeBands { get; set; } = "6-8,9-10";
    public int? MaxParentRegistrations { get; set; }
    public DateTime? SubmissionCutoffAt { get; set; }
    public DateTime? ReviewCutoffAt { get; set; }
    public DateTime? VotingStartAt { get; set; }
    public DateTime? VotingEndAt { get; set; }
    public List<UpsertEventTrackDto> Tracks { get; set; } = new();
}

public class UpdateEventTasksSectionRequestDto
{
    public TrackCompletionMode CompletionMode { get; set; } = TrackCompletionMode.AllMandatory;
    public int? RequiredTaskCount { get; set; }
    public int? RequiredPoints { get; set; }
    public List<UpsertEventTaskDto> Tasks { get; set; } = new();
}

public class UpdateEventAwardsSectionRequestDto
{
    public long BudgetVnd { get; set; }
    public bool AwardAllParentMissionCompleters { get; set; }
    public List<UpsertEventAwardSlotDto> AwardSlots { get; set; } = new();
}

public class CastEventVoteRequestDto
{
    public int StoryId { get; set; }
    public int Stars { get; set; }
    public string? Comment { get; set; }
    public int? ChildProfileId { get; set; }
}

public class DisqualifyEventVoteRequestDto
{
    public string Reason { get; set; } = string.Empty;
}

public class EventVoteDto
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int? EventSubmissionId { get; set; }
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public int ParentUserId { get; set; }
    public EventTrackType VoterTrackType { get; set; } = EventTrackType.Parent;
    public int Stars { get; set; }
    public string? Comment { get; set; }
    public int? ReadingSessionId { get; set; }
    public bool IsDisqualified { get; set; }
    public string? DisqualifiedReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class EventLeaderboardEntryDto
{
    public int Rank { get; set; }
    public EventTrackType AuthorTrackType { get; set; } = EventTrackType.Creator;
    public int SubmissionId { get; set; }
    public int ParticipationId { get; set; }
    public int CreatorUserId { get; set; }
    public int StoryId { get; set; }
    public string StoryTitle { get; set; } = string.Empty;
    public int ValidVoteCount { get; set; }
    public decimal AverageStars { get; set; }
    public bool MeetsThreshold { get; set; }
    public DateTime FirstSubmittedAt { get; set; }
}

public class EventLeaderboardDto
{
    public int EventId { get; set; }
    public bool Hidden { get; set; }
    public bool ResultsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public EventScoringMode ScoringMode { get; set; }
    public int MinVotesPerSubmission { get; set; }
    public List<EventLeaderboardEntryDto> Entries { get; set; } = new();
}
