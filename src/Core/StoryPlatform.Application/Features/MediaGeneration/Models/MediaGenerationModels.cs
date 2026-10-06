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
    IReadOnlyList<int>? ScenesWithoutValidBeats = null);

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
    int? AssetId, string Status, string? Url, DateTimeOffset? UrlExpiresAt = null);

public sealed record SceneMediaItem(int SceneId, int SceneIndex, IReadOnlyList<IllustrationBeatMediaItem> Illustrations);

public sealed record StoryMediaPackage(int StoryId, int StoryVersionId, IReadOnlyList<SceneMediaItem> Scenes);

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
