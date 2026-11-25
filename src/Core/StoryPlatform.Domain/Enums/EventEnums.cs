using System.Text.Json.Serialization;

namespace StoryPlatform.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Published = 4,
    Paused = 5,
    Closed = 6,
    Archived = 7
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventFormat
{
    Standard = 1,
    SharedCreatorParent = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventConfigVersion
{
    LegacyRoleBased = 1,
    ActivityBased = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventActivityType
{
    Submission = 1,
    ReadingMissions = 2,
    Voting = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventAudience
{
    Creator = 1,
    Parent = 2,
    Both = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventReadingStorySource
{
    CuratedOnly = 1,
    ApprovedSubmissionsOnly = 2,
    Both = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventAwardResultSource
{
    SubmissionScore = 1,
    VotingAverage = 2,
    MissionCompletion = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventScoringMode
{
    Rubric = 1,
    ParentVoteAverage = 2,
    MissionCompletion = 3,
    CreatorVoteAverage = 4,
    CrossRoleVoteAverage = 5
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventTrackType
{
    Creator = 1,
    Parent = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrackCompletionMode
{
    AllMandatory = 1,
    MinimumTasksCount = 2,
    MinimumPointsThreshold = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskVerifierType
{
    ReadEventStories = 1,
    CompleteStoryQuiz = 2,
    SaveStoryVocabulary = 3,
    AnswerChildQuestion = 4,
    GuidedDiscussionCheckin = 5,
    RealLifePracticeCheckin = 6,
    RateAndReviewStory = 7,
    ActiveDaysCount = 8,
    SubmitApprovedStory = 9,
    VoteEventStories = 10
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventParticipationStatus
{
    Registered = 1,
    MissionCompleted = 2,
    PendingVerification = 3,
    Verified = 4,
    VerificationFailed = 5,
    ManualReview = 6,
    Disqualified = 7
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventSubmissionStatus
{
    Draft = 1,
    PendingReview = 2,
    ChangesRequested = 3,
    Approved = 4,
    Rejected = 5,
    Disqualified = 6,
    ReviewExpired = 7
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventStorySource
{
    CreatorSubmission = 1,
    ModeratorSeed = 2,
    ParentSubmission = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventAppealStatus
{
    Open = 1,
    UnderReview = 2,
    Accepted = 3,
    Rejected = 4,
    Escalated = 5
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventRewardType
{
    PhysicalGift = 1,
    PlatformCredits = 2,
    CashTransfer = 3,
    CreatorPackage = 4
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventAwardStatus
{
    AwardProposed = 1,
    AwardApproved = 2,
    AwaitingClaim = 3,
    Claimed = 4,
    FulfillmentProcessing = 5,
    Fulfilled = 6,
    Expired = 7,
    UnclaimedBudgetReturned = 8,
    Failed = 9
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RewardFulfillmentStatus
{
    Pending = 1,
    Processing = 2,
    Delivered = 3,
    Failed = 4
}
