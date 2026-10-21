namespace StoryPlatform.Application.Features.RoadmapTemplates;

public static class RoadmapReviewPolicy
{
    public const int MaxTextLength = 2000;
    public const int MaxNoteLength = 500;
    public const int MaxParts = 20;

    public static readonly string[] CriterionCodes =
        ["order", "theme", "name_description", "age_level"];

    public static string CriterionLabel(string code) => code switch
    {
        "order" => "Thứ tự hợp lý theo độ khó",
        "theme" => "Các truyện cùng chủ đề chính",
        "name_description" => "Tên và mô tả đúng nội dung, phù hợp trẻ",
        "age_level" => "Độ tuổi, reading level khớp với từng truyện",
        _ => code
    };

    public const string PartOrder = "order";
    public const string PartStory = "story";
    public const string PartNameDescription = "name_description";
    public const string PartAgeLevel = "age_level";
    public const string ActionRemove = "remove";
    public const string ActionReplace = "replace";
    public const string DecisionApproved = "approved";
    public const string DecisionChangesRequested = "changes_requested";
    public const string DecisionRejected = "rejected";

    public static class BlockerCodes
    {
        public const string OwnRoadmap = "own_roadmap";
        public const string ChecklistIncomplete = "checklist_incomplete";
        public const string StoriesNotReady = "stories_not_ready";
        public const string ComboTaken = "combo_taken";
    }
}
