namespace StoryPlatform.Application.Features.MediaGeneration.Models;

public sealed record StoryTextBlock(string BlockId, int StartOffset, int EndOffset, string Text);

public sealed record SceneSelection(int SceneIndex, IReadOnlyList<string> BlockIds, string? Focus = null);

public sealed record IllustrationBeatSelection(int BeatOrder, int StartOffset, int EndOffset, string VisualFocus);

public sealed record IllustrationBeatPlanRequest(string SceneText, string? SceneVisualDescription, string MediaContextJson);

public sealed record ValidatedScene(
    int SceneIndex, int TextRangeStart, int TextRangeEnd, string SceneText, string? VisualDescription);

public sealed record MediaContextBuildRequest(
    int StoryVersionId,
    string Title,
    string Content,
    string? Lesson,
    string? OutlineOpening,
    string? OutlineDevelopment,
    string? OutlineEnding,
    string? AcceptedInputJson,
    string? ContextSnapshotJson);

public sealed record SceneSegmentationRequest(
    string StoryContent, IReadOnlyList<StoryTextBlock> Blocks, string MediaContextJson);

public sealed record SceneSpecification(
    int StoryVersionId,
    int StorySceneId,
    int SceneIndex,
    string SceneText,
    string? VisualDescription,
    string MediaContextJson,
    IReadOnlyList<string> MustShow,
    IReadOnlyList<string> MustNotContradict,
    string? PromptFeedback = null,
    string? BeatText = null,
    string? BeatVisualFocus = null)
{
    /// <summary>
    /// Returns a new SceneSpecification with prompt feedback appended for failure-aware regeneration.
    /// Pass null to indicate first attempt (no feedback).
    /// </summary>
    public SceneSpecification WithFeedback(string? feedback) =>
        new(StoryVersionId, StorySceneId, SceneIndex, SceneText, VisualDescription,
            MediaContextJson, MustShow, MustNotContradict, feedback, BeatText, BeatVisualFocus);

    public SceneSpecification ForBeat(string beatText, string visualFocus) =>
        new(StoryVersionId, StorySceneId, SceneIndex, SceneText, VisualDescription,
            MediaContextJson, new[] { visualFocus }, MustNotContradict, null, beatText, visualFocus);
}

public enum MediaEvaluationDecision { Pass = 1, Fail = 2, Uncertain = 3 }

public sealed record MediaEvaluationResult(MediaEvaluationDecision Decision, string? Reason = null)
{
    public bool Passed => Decision == MediaEvaluationDecision.Pass;
}

public sealed record MediaGenerationProgress(
    int StoryId,
    int? ApprovedStoryVersionId,
    string StoryStatus,
    string JobStatus,
    int SceneCount,
    int ReadyIllustrations,
    int ReadyAudio,
    bool IsReady,
    string? ErrorCode,
    int RequiredIllustrations = 0,
    IReadOnlyList<int>? MissingIllustrationBeatIds = null,
    IReadOnlyList<int>? MissingAudioSegmentIds = null,
    IReadOnlyList<int>? ManualReviewAssetIds = null,
    IReadOnlyList<int>? ScenesWithoutValidBeats = null)
{
    public MediaBranchProgress Images { get; init; } = new("Preparing", 0, 0, null, null);
    public MediaBranchProgress Audio { get; init; } = new("Preparing", 0, 0, null, null);
    public int PackageRevision { get; init; }
    public bool IsGenerated { get; init; }
    public bool IsAwaitingConfirmation { get; init; }
    public bool IsConfirmed { get; init; }
    public int? ConfirmedByUserId { get; init; }
    public DateTime? ConfirmedAt { get; init; }
}

public sealed record MediaReadinessResult(
    bool IsReady,
    int SceneCount,
    int RequiredIllustrations,
    int ReadyIllustrations,
    int ReadyAudio,
    IReadOnlyList<int> ScenesWithoutValidBeats,
    IReadOnlyList<int> MissingIllustrationBeatIds,
    IReadOnlyList<int> MissingAudioSegmentIds,
    IReadOnlyList<int> ManualReviewAssetIds);

public sealed record IllustrationBeatMediaItem(
    int BeatId, int BeatOrder, int StartOffset, int EndOffset, string VisualFocus,
    int? AssetId, string Status, string? Url);

public sealed record SceneMediaItem(int SceneId, int SceneIndex, IReadOnlyList<IllustrationBeatMediaItem> Illustrations)
{
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<AudioMediaItem> Audio { get; init; } = [];
}

public sealed record AudioMediaItem(int SegmentId, int SegmentOrder, string Text, int? AssetId,
    string Status, string? Url, string? WordTimings);

public sealed record StoryMediaPackage(int StoryId, int StoryVersionId, IReadOnlyList<SceneMediaItem> Scenes)
{
    public IReadOnlyList<MediaReplacementItem> Replacements { get; init; } = [];
    public bool IsReady { get; init; }
    public int PackageRevision { get; init; }
    public bool IsGenerated { get; init; }
    public bool IsAwaitingConfirmation { get; init; }
    public bool IsConfirmed { get; init; }
    public int? ConfirmedByUserId { get; init; }
    public DateTime? ConfirmedAt { get; init; }
}

public sealed record MediaJobProcessResult(
    bool JobFound,
    bool Success,
    bool IsPermanentFailure,
    string? ErrorCode = null)
{
    public static MediaJobProcessResult NoJob { get; } = new(false, true, false);
    public static MediaJobProcessResult Completed { get; } = new(true, true, false);
    public static MediaJobProcessResult Failed(bool permanent, string errorCode) =>
        new(true, false, permanent, errorCode);
}

public sealed record ConfirmMediaPackageRequest(
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int StoryVersionId,
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int PackageRevision);

public sealed record RegenerateMediaRequest(
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int StoryVersionId,
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)] int PackageRevision,
    [System.ComponentModel.DataAnnotations.Required] bool? ExpectedConfirmed,
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)] string OperationId = "");

public sealed record KeepCurrentMediaRequest(
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)] string OperationId,
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int StoryVersionId,
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int PackageRevision);

public sealed record MediaBranchProgress(string Status, int RequiredCount, int ReadyCount, string? ErrorCode, DateTime? NextRetryAt);
public sealed record MediaReplacementItem(int CandidateAssetId, int PreviousAssetId, string Type, int TargetId, string Status);
