namespace StoryPlatform.Domain.Enums;

/// <summary>Trạng thái hiển thị của một đánh giá truyện.</summary>
public enum StoryRatingStatus
{
    /// <summary>Đang hiện công khai, được tính vào điểm.</summary>
    Published = 1,
    /// <summary>Chờ Moderator (bị lọc tự động giữ lại, hoặc vừa sửa từ trạng thái chờ / bị ẩn).</summary>
    PendingModeration = 2,
    /// <summary>Moderator đã ẩn.</summary>
    Hidden = 3
}
