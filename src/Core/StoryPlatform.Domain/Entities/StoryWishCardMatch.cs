using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>Thẻ ước mô tả một truyện (WF-L2D UC-L2D-03 "ánh xạ thẻ ước của từng truyện"). Mỗi cặp truyện–thẻ một dòng.</summary>
public class StoryWishCardMatch : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int WishCardId { get; set; }
    public virtual WishCard? WishCard { get; set; }

    public WishCardMatchSource Source { get; set; } = WishCardMatchSource.Moderator;

    public int? CreatedByUserId { get; set; }
}
