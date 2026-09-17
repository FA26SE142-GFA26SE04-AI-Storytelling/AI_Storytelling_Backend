namespace StoryPlatform.Domain.Enums;

public enum JobStage
{
    InputValidated = 1,
    OutlineDraft = 2,
    OutlineApproved = 3,
    ContentReview = 4,
    Approved = 5,
    MediaProcessing = 6,
    Ready = 7,
    Rejected = 8,
    OutlinePending = 10,
    OutlineGenerating = 11,
    OutlineGenerated = 12,
    OutlineFailed = 14,
    OutlineRejected = 15,
    ContentPending = 20,
    ContentGenerating = 21,
    ContentStable = 22,
    ContentArtifactPending = 23,
    ContentArtifactGenerating = 24,
    ContentArtifactCompleted = 25,
    ContentPackageCompleted = 26,
    ContentFailed = 29,
    MediaPending = 30,
    MediaContextBuilding = 31,
    MediaSegmenting = 32,
    MediaGenerating = 33,
    MediaFinalizing = 34,
    MediaCompleted = 35,
    MediaFailed = 39
}
