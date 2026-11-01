using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.Moderation;

/// <summary>
/// Quy tắc cố định của lượt duyệt truyện Public Library (WF-L5A UC-L5A-03): phần nào phải xem và 8 tiêu chí checklist.
/// </summary>
public static class PublicReviewRequirements
{
    public const string ScenePart = "scene";
    public const string AudioPart = "audio";

    public const string Safety = "safety";
    public const string AgeFit = "ageFit";
    public const string Education = "education";
    public const string Language = "language";
    public const string Copyright = "copyright";
    public const string Image = "image";
    public const string Audio = "audio";
    public const string Interaction = "interaction";

    public static readonly string[] ChecklistKeys =
    {
        Safety, AgeFit, Education, Language, Copyright, Image, Audio, Interaction
    };

    public static readonly IReadOnlyDictionary<string, string> ChecklistLabels =
        new Dictionary<string, string>
        {
            [Safety] = "An toàn",
            [AgeFit] = "Phù hợp tuổi",
            [Education] = "Giá trị giáo dục",
            [Language] = "Ngôn ngữ",
            [Copyright] = "Bản quyền",
            [Image] = "Ảnh",
            [Audio] = "Audio",
            [Interaction] = "Tương tác"
        };

    public const string SceneTextPart = "scene_text";
    public const string SceneImagePart = "scene_image";
    public const string SceneAudioPart = "scene_audio";
    public const string QuizPart = "quiz";
    public const string VocabularyPart = "vocabulary";
    public const string ListeningPart = "listening";
    public const string MetadataPart = "metadata";

    public static readonly string[] UnsupportedChangeParts =
        { "stop_point", "character_card", "summary" };

    public const int MaxGeneralNoteLength = 2000;
    public const int MaxPartNoteLength = 1000;
    public const int MaxChangeParts = 50;
    public const int MaxDecisionReasonLength = 1000;

    public const string ComparePrevious = "previous";
    public const string ComparePublic = "public";

    public static bool HasPronunciationWarning(MediaAsset? audio) =>
        audio != null
        && !string.IsNullOrWhiteSpace(audio.Url)
        && string.IsNullOrWhiteSpace(audio.WordTimings);
}

public static class ApproveBlockerCodes
{
    public const string StoryArchived = "story_archived";
    public const string StoryNotReady = "story_not_ready";
    public const string OwnContent = "own_content";
    public const string ViewsIncomplete = "views_incomplete";
    public const string FlagsUnresolved = "flags_unresolved";
    public const string ChecklistIncomplete = "checklist_incomplete";
}
