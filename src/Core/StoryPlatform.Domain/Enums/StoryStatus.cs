namespace StoryPlatform.Domain.Enums;

public enum StoryStatus
{
    Draft = 1,
    OutlineReview = 2,
    ContentReview = 3,
    Approved = 4,
    MediaProcessing = 5,
    Ready = 6,
    Rejected = 7,
    // Giá trị 8 từng là Archived; không tái sử dụng.
    MediaReview = 9
}
