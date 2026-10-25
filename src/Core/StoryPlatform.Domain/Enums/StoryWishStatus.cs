namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Vòng đời điều ước (WF-L2D). Trạng thái kết thúc: Fulfilled, Declined, Expired, Cancelled.
/// </summary>
public enum StoryWishStatus
{
    Draft = 1,
    Pending = 2,
    Accepted = 3,
    Fulfilled = 4,
    Declined = 5,
    Expired = 6,
    Cancelled = 7
}
