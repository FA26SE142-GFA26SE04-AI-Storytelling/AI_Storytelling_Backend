namespace StoryPlatform.Application.Features.WishCards;

public class CreateWishCardRequestDto
{
    /// <summary>"Character", "Place", "Challenge", "Lesson", "Desire", "Event" hoặc "Ending".</summary>
    public string? Group { get; set; }
    public string? Label { get; set; }
    public string? ImageUrl { get; set; }
    public string? AudioUrl { get; set; }
    /// <summary>"6-8" hoặc "9-10"; để trống = mọi nhóm tuổi.</summary>
    public string? AgeBand { get; set; }
    public int? ContentCategoryId { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Sửa thẻ. Không đổi nhóm (muốn đổi nhóm thì tạo thẻ mới rồi ẩn thẻ cũ) và không đổi trạng thái hiện/ẩn.</summary>
public class UpdateWishCardRequestDto
{
    public string? Label { get; set; }
    public string? ImageUrl { get; set; }
    public string? AudioUrl { get; set; }
    public string? AgeBand { get; set; }
    public int? ContentCategoryId { get; set; }
    public int SortOrder { get; set; }
}

public class SetWishCardActiveRequestDto
{
    public bool IsActive { get; set; }
}

public class WishCardQueryDto
{
    public string? Group { get; set; }
    /// <summary>null = cả thẻ đang hiện lẫn đã ẩn.</summary>
    public bool? IsActive { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>Thẻ như Moderator thấy (có cả trạng thái hiện/ẩn).</summary>
public class WishCardDto
{
    public int Id { get; set; }
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? AudioUrl { get; set; }
    public string? AgeBand { get; set; }
    public int? ContentCategoryId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Thẻ như trẻ thấy: chỉ những gì cần để hiển thị và đọc tên thẻ.</summary>
public class ChildWishCardDto
{
    public int Id { get; set; }
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? AudioUrl { get; set; }
}
