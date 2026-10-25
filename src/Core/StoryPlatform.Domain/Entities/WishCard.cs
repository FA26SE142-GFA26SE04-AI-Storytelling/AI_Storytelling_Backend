using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một thẻ hình để trẻ chọn khi "Bé ước" / "Bé sáng tác" (WF-L2D). Do Moderator quản lý; ẩn chứ không xóa.
/// </summary>
public class WishCard : BaseEntity
{
    public WishCardGroup Group { get; set; }

    /// <summary>Nhãn chữ hiển thị dưới hình (và là nội dung audio nhãn).</summary>
    public string Label { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>Audio đọc tên thẻ; null nếu chưa có.</summary>
    public string? AudioUrl { get; set; }

    /// <summary>Nhóm tuổi phù hợp; null = mọi nhóm tuổi.</summary>
    public AgeBand? AgeBand { get; set; }

    /// <summary>Nhãn nội dung để lọc theo danh sách chặn/cho phép của trẻ; null = không gắn nhãn.</summary>
    public int? ContentCategoryId { get; set; }
    public virtual ContentCategory? ContentCategory { get; set; }

    public int SortOrder { get; set; }

    /// <summary>false = đã ẩn: chỉ bỏ khỏi lựa chọn mới, không xóa dữ liệu cũ.</summary>
    public bool IsActive { get; set; } = true;

    public int? CreatedByUserId { get; set; }
}
