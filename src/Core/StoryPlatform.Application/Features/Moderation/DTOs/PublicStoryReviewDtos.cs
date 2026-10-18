namespace StoryPlatform.Application.Features.Moderation.DTOs;

public class PublicReviewPackageDto
{
    public int ItemId { get; set; }
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public int VersionNo { get; set; }
    public bool IsResubmission { get; set; }
    public PublicReviewStoryDto Story { get; set; } = new();
    public PublicReviewVersionDto Version { get; set; } = new();
    public List<PublicReviewSceneDto> Scenes { get; set; } = new();
    public List<PublicReviewVocabularyDto> Vocabulary { get; set; } = new();
    public List<PublicReviewQuizDto> Quizzes { get; set; } = new();
    public List<PublicReviewDiscussionDto> DiscussionQuestions { get; set; } = new();
    public List<PublicReviewFlagDto> Flags { get; set; } = new();
    public bool AllFlagsResolved { get; set; }
    public PublicReviewProgressDto Progress { get; set; } = new();
    public PublicReviewChecklistDto Checklist { get; set; } = new();
    public List<ApproveBlockerDto> ApproveBlockers { get; set; } = new();
    public bool CanApprove { get; set; }
    public string? PreviousModeratorName { get; set; }
    public List<PublicReviewHistoryEntryDto> ReviewHistory { get; set; } = new();
}

public class PublicReviewStoryDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Genre { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public int? ReadingLevel { get; set; }
    public string? VocabularyLevel { get; set; }
    public string Language { get; set; } = string.Empty;
    public string? MoralLesson { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<PublicReviewLabelDto> Labels { get; set; } = new();
}

public class PublicReviewLabelDto
{
    public int ContentCategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public class PublicReviewVersionDto
{
    public string Title { get; set; } = string.Empty;
    public string? OutlineOpening { get; set; }
    public string? OutlineDevelopment { get; set; }
    public string? OutlineEnding { get; set; }
    public string? Content { get; set; }
    public string? Lesson { get; set; }
}

public class PublicReviewSceneDto
{
    public int SceneId { get; set; }
    public int SceneIndex { get; set; }
    public string SceneText { get; set; } = string.Empty;
    public string? VisualDescription { get; set; }
    public bool Viewed { get; set; }
    public List<PublicReviewIllustrationDto> Illustrations { get; set; } = new();
    public List<PublicReviewSegmentDto> Segments { get; set; } = new();
}

public class PublicReviewIllustrationDto
{
    public int BeatOrder { get; set; }
    public string VisualFocus { get; set; } = string.Empty;
    public int? AssetId { get; set; }
    public string? Url { get; set; }
}

public class PublicReviewSegmentDto
{
    public int SegmentId { get; set; }
    public int SegmentOrder { get; set; }
    public string Text { get; set; } = string.Empty;
    public int? AudioAssetId { get; set; }
    public string? AudioUrl { get; set; }
    public bool HasPronunciationWarning { get; set; }
    public bool Heard { get; set; }
}

public class PublicReviewVocabularyDto
{
    public int Id { get; set; }
    public string Term { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}

public class PublicReviewQuizDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? CorrectAnswer { get; set; }
    public string? Choices { get; set; }
}

public class PublicReviewDiscussionDto
{
    public int Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public bool IsMoralLesson { get; set; }
}

public class PublicReviewFlagDto
{
    public string FlagId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool Resolved { get; set; }
    public string? ResolvedReason { get; set; }
}

public class PublicReviewProgressDto
{
    public int TotalScenes { get; set; }
    public List<int> MissingSceneIds { get; set; } = new();
    public int AudioHeardCount { get; set; }
    public List<int> MissingWarningSegmentIds { get; set; } = new();
    public bool NeedsAtLeastOneAudio { get; set; }
    public bool RequiredViewsComplete { get; set; }
}

public class PublicReviewChecklistDto
{
    public bool? Safety { get; set; }
    public bool? AgeFit { get; set; }
    public bool? Education { get; set; }
    public bool? Language { get; set; }
    public bool? Copyright { get; set; }
    public bool? Image { get; set; }
    public bool? Audio { get; set; }
    public bool? Interaction { get; set; }

    public bool AllPassed =>
        Safety == true && AgeFit == true && Education == true && Language == true
        && Copyright == true && Image == true && Audio == true && Interaction == true;
}

public class MarkPartViewedRequestDto
{
    public string PartType { get; set; } = string.Empty;
    public int PartId { get; set; }
}

public class ResolveFlagRequestDto
{
    public string FlagId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class PublicReviewFlagsResultDto
{
    public List<PublicReviewFlagDto> Flags { get; set; } = new();
    public bool AllFlagsResolved { get; set; }
    public bool VersionCleared { get; set; }
}

public class SetContentLabelsRequestDto
{
    public List<int> ContentCategoryIds { get; set; } = new();
}

public class ApproveBlockerDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class PublicReviewDecisionResultDto
{
    public int ItemId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public bool StoryPublished { get; set; }
    public DateTime DecidedAt { get; set; }
}

public class ChangeRequestPartDto
{
    public string PartType { get; set; } = string.Empty;
    public int? PartId { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class RequestChangesDto
{
    public string GeneralNote { get; set; } = string.Empty;
    public List<ChangeRequestPartDto> Parts { get; set; } = new();
}

public class RejectRequestDto
{
    public string Reason { get; set; } = string.Empty;
}

public class SubmitPublicStoryRequestDto
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
}

public class PublicStorySubmissionResultDto
{
    public int ItemId { get; set; }
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsResubmission { get; set; }
    public int? PreviousItemId { get; set; }
}

public class PublicReviewHistoryEntryDto
{
    public int ItemId { get; set; }
    public int? StoryVersionId { get; set; }
    public int? VersionNo { get; set; }
    public string Decision { get; set; } = string.Empty;
    public int? DecidedByUserId { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Reason { get; set; }
    public string? Feedback { get; set; }
}

public class PublicVersionComparisonDto
{
    public string Against { get; set; } = string.Empty;
    public int BaseVersionId { get; set; }
    public int BaseVersionNo { get; set; }
    public int CurrentVersionId { get; set; }
    public int CurrentVersionNo { get; set; }
    public bool IsIdentical { get; set; }
    public List<TextDiffEntryDto> TextDiff { get; set; } = new();
    public PublicVersionMetricsDto BaseMetrics { get; set; } = new();
    public PublicVersionMetricsDto CurrentMetrics { get; set; } = new();
    public List<string> ChangedFields { get; set; } = new();
    public List<SceneChangeDto> SceneChanges { get; set; } = new();
    public List<ArtifactChangeDto> ArtifactChanges { get; set; } = new();
}

public class TextDiffEntryDto
{
    public string Type { get; set; } = string.Empty;
    public string? Before { get; set; }
    public string? After { get; set; }
}

public class PublicVersionMetricsDto
{
    public int WordCount { get; set; }
    public int SentenceCount { get; set; }
    public double AverageSentenceLength { get; set; }
    public decimal? ReadabilityFkgl { get; set; }
    public decimal? ReadabilityFre { get; set; }
}

public class SceneChangeDto
{
    public int SceneIndex { get; set; }
    public string Change { get; set; } = string.Empty;
    public bool TextChanged { get; set; }
    public bool ImageChanged { get; set; }
    public bool AudioChanged { get; set; }
}

public class ArtifactChangeDto
{
    public string Kind { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}
