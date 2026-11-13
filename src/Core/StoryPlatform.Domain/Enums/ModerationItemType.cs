namespace StoryPlatform.Domain.Enums;

public enum ModerationItemType
{
    PublicStoryReview = 1,
    GrayZoneStory = 2,
    ContentReport = 3,
    RoadmapTemplateReview = 4,
    CommunitySubmission = 5,
    PublicStoryRemoval = 6,
    /// <summary>Đánh giá truyện của phụ huynh bị lọc tự động giữ lại hoặc bị báo cáo.</summary>
    StoryRating = 7
}
