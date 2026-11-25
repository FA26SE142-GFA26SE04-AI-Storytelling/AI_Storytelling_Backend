using System;
using System.Collections.Generic;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class Event : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? BannerUrl { get; set; }
    public int MainCategoryId { get; set; }
    public virtual ContentCategory? MainCategory { get; set; }

    /// <summary>Danh sách nhóm tuổi mục tiêu phân tách bằng dấu phẩy: "6-8", "9-10", hoặc "6-8,9-10".</summary>
    public string TargetAgeBands { get; set; } = "6-8,9-10";
    public EventStatus Status { get; set; } = EventStatus.Draft;
    public EventConfigVersion ConfigVersion { get; set; } = EventConfigVersion.LegacyRoleBased;
    public EventFormat Format { get; set; } = EventFormat.Standard;
    public EventScoringMode ScoringMode { get; set; } = EventScoringMode.Rubric;
    public int MinVotesPerSubmission { get; set; } = 5;
    public int? MaxParentRegistrations { get; set; }
    public DateTime? SubmissionCutoffAt { get; set; }
    public DateTime? ReviewCutoffAt { get; set; }
    public DateTime? VotingStartAt { get; set; }
    public DateTime? VotingEndAt { get; set; }
    public string? CreatorVotingScheduleJson { get; set; }
    public string? FinalLeaderboardSnapshotJson { get; set; }
    public string? FinalAwardRankingSnapshotJson { get; set; }
    public DateTime? ResultsFinalizedAt { get; set; }
    public long BudgetVnd { get; set; }

    public int CreatedByUserId { get; set; }
    public int AssignedModeratorId { get; set; }
    public int? ApprovedByAdminId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? SummaryReportJson { get; set; }

    public virtual ICollection<EventTrack> Tracks { get; set; } = new List<EventTrack>();
    public virtual ICollection<EventActivity> Activities { get; set; } = new List<EventActivity>();
    public virtual ICollection<EventRuleVersion> RuleVersions { get; set; } = new List<EventRuleVersion>();
}

public class EventActivity : BaseEntity
{
    public int EventId { get; set; }
    public virtual Event? Event { get; set; }
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
    public bool WaiveWeeklyLibraryLimit { get; set; } = true;
    public EventReadingStorySource StorySource { get; set; } = EventReadingStorySource.Both;
    public int MinVotesPerSubmission { get; set; } = 5;
    public int ClaimWindowHours { get; set; } = 72;
    public int MaxReallocationRounds { get; set; } = 1;
    public bool AwardAllMissionCompleters { get; set; }
    public string? ConfigJson { get; set; }
}

public class EventModeratorAssignment : BaseEntity
{
    public int EventId { get; set; }
    public int ModeratorId { get; set; }
}

public class EventTrack : BaseEntity
{
    public int EventId { get; set; }
    public virtual Event? Event { get; set; }
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

public class EventRuleVersion : BaseEntity
{
    public int EventId { get; set; }
    public virtual Event? Event { get; set; }
    public int VersionNo { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public string ContentHash { get; set; } = string.Empty;
    public string RulesText { get; set; } = string.Empty;
    public string? RewardEligibilityText { get; set; }
    public string? TieBreakerPolicyText { get; set; }
    public string? InsufficientVotesPolicyText { get; set; }
    public string? CreatorRubricJson { get; set; }

    public TrackCompletionMode CompletionMode { get; set; } = TrackCompletionMode.AllMandatory;
    public int? RequiredTaskCount { get; set; }
    public int? RequiredPoints { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ActivatedAt { get; set; }

    public virtual ICollection<EventTaskDefinition> TaskDefinitions { get; set; } = new List<EventTaskDefinition>();
}

public class EventTaskDefinition : BaseEntity
{
    public int EventRuleVersionId { get; set; }
    public virtual EventRuleVersion? RuleVersion { get; set; }

    public string TaskCode { get; set; } = string.Empty;
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

public class EventParticipation : BaseEntity
{
    public int EventId { get; set; }
    public virtual Event? Event { get; set; }
    public int? TrackId { get; set; }
    public virtual EventTrack? Track { get; set; }
    public int? EventActivityId { get; set; }
    public virtual EventActivity? Activity { get; set; }
    public int UserId { get; set; }
    public int AcceptedRuleVersionId { get; set; }

    public EventParticipationStatus Status { get; set; } = EventParticipationStatus.Registered;
    public DateTime? MissionCompletedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerificationNotesJson { get; set; }
}

public class EventSubmission : BaseEntity
{
    public int EventId { get; set; }
    public virtual Event? Event { get; set; }
    public int ParticipationId { get; set; }
    public virtual EventParticipation? Participation { get; set; }
    public int? EventActivityId { get; set; }
    public virtual EventActivity? Activity { get; set; }
    public int CreatorUserId { get; set; }
    public int StoryId { get; set; }
    public int CurrentStoryVersionId { get; set; }
    public int? LockedStoryVersionId { get; set; }
    public int? LatestModerationItemId { get; set; }

    public string? ParentSubmissionText { get; set; }
    public string? ParentSubmissionTitle { get; set; }
    public double? SimilarityScore { get; set; }
    public int? SimilarityReferenceStoryId { get; set; }
    public EventSubmissionStatus Status { get; set; } = EventSubmissionStatus.Draft;
    public DateTime? SubmittedAt { get; set; }
}

public class EventStory : BaseEntity
{
    public int EventId { get; set; }
    public virtual Event? Event { get; set; }
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public EventStorySource Source { get; set; } = EventStorySource.CreatorSubmission;
    public int? EventSubmissionId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EventTaskProgress : BaseEntity
{
    public int ParticipationId { get; set; }
    public virtual EventParticipation? Participation { get; set; }
    public int TaskDefinitionId { get; set; }
    public virtual EventTaskDefinition? TaskDefinition { get; set; }

    public int? ChildProfileId { get; set; }
    public int? StoryId { get; set; }
    public int? ReadingSessionId { get; set; }
    public int? StoryRatingId { get; set; }
    public int? EventVoteId { get; set; }
    public string? SelfDeclaredPayloadJson { get; set; }
    public DateOnly ActivityLocalDate { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

public class EventAppeal : BaseEntity
{
    public int EventId { get; set; }
    public int ParticipationId { get; set; }
    public virtual EventParticipation? Participation { get; set; }
    public int AppellantUserId { get; set; }

    public string ReasonText { get; set; } = string.Empty;
    public string? EvidenceJson { get; set; }
    public EventAppealStatus Status { get; set; } = EventAppealStatus.Open;
    public int? ReviewedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class EventEvaluation : BaseEntity
{
    public int EventSubmissionId { get; set; }
    public virtual EventSubmission? Submission { get; set; }
    public int EvaluatorUserId { get; set; }
    public string CriteriaScoresJson { get; set; } = "{}";
    public decimal TotalWeightedScore { get; set; }
    public string? Comment { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

public class EventAward : BaseEntity
{
    public int EventId { get; set; }
    public virtual Event? Event { get; set; }
    public int? TrackId { get; set; }
    public int? EventActivityId { get; set; }
    public virtual EventActivity? Activity { get; set; }
    public EventAwardResultSource? ResultSource { get; set; }
    public EventAudience? TargetAudience { get; set; }
    public string RankOrTier { get; set; } = string.Empty;
    public int SlotIndex { get; set; } = 1;
    public EventRewardType RewardType { get; set; } = EventRewardType.PhysicalGift;
    public string RewardName { get; set; } = string.Empty;
    public long UnitValueVnd { get; set; }
    public int CreditGrantAmount { get; set; }
    public int SubmitCreditGrantAmount { get; set; }

    public int? ParticipationId { get; set; }
    public int? ReallocatedFromAwardId { get; set; }
    public int ReallocationRound { get; set; }
    public EventAwardStatus Status { get; set; } = EventAwardStatus.AwardProposed;
    public DateTime? ClaimDeadlineAt { get; set; }
    public int? ApprovedByAdminId { get; set; }
}

public class RewardFulfillment : BaseEntity
{
    public int EventAwardId { get; set; }
    public virtual EventAward? Award { get; set; }
    public int AttemptNo { get; set; } = 1;
    public string? RecipientInfoProtectedJson { get; set; }
    public string? TrackingOrBankTxCode { get; set; }
    public RewardFulfillmentStatus Status { get; set; } = RewardFulfillmentStatus.Pending;
    public int? HandledByUserId { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? FailureReason { get; set; }
}

public class EventVote : BaseEntity
{
    public int EventId { get; set; }
    public virtual Event? Event { get; set; }
    public int? EventSubmissionId { get; set; }
    public virtual EventSubmission? Submission { get; set; }
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public int ParticipationId { get; set; }
    public virtual EventParticipation? Participation { get; set; }
    public int ParentUserId { get; set; }
    public int? ChildProfileId { get; set; }
    public int? ReadingSessionId { get; set; }
    public int Stars { get; set; }
    public string? Comment { get; set; }
    public bool IsDisqualified { get; set; }
    public string? DisqualifiedReason { get; set; }
    public int? DisqualifiedByUserId { get; set; }
    public DateTime? DisqualifiedAt { get; set; }
}
