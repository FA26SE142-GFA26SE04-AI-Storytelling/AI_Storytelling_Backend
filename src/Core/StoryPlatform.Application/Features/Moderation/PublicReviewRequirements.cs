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

    public static bool HasPronunciationWarning(MediaAsset? audio) =>
        audio != null
        && !string.IsNullOrWhiteSpace(audio.Url)
        && string.IsNullOrWhiteSpace(audio.WordTimings);
}
