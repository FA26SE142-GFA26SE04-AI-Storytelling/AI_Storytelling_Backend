namespace StoryPlatform.Application.Features.MediaGeneration.Models;

public sealed record StoryTextBlock(string BlockId, int StartOffset, int EndOffset, string Text);

public sealed record SceneSelection(int SceneIndex, IReadOnlyList<string> BlockIds, string? Focus = null);

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
    string? PromptFeedback = null)
{
    /// <summary>
    /// Returns a new SceneSpecification with prompt feedback appended for failure-aware regeneration.
    /// Pass null to indicate first attempt (no feedback).
    /// </summary>
    public SceneSpecification WithFeedback(string? feedback) =>
        new(StoryVersionId, StorySceneId, SceneIndex, SceneText, VisualDescription,
            MediaContextJson, MustShow, MustNotContradict, feedback);
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
    string? ErrorCode);

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
