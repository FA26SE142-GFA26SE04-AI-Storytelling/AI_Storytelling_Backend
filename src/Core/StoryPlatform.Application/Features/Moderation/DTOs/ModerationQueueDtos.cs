using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.DTOs;

/// <summary>Lệnh nội bộ để một luồng con đưa mục mới vào hàng đợi.</summary>
public class EnqueueModerationItemCommand
{
    public ModerationItemType ItemType { get; set; }
    public int? StoryId { get; set; }
    public int? StoryVersionId { get; set; }
    public int? ContentReportId { get; set; }
    public int? ReadingRoadmapId { get; set; }

    /// <summary>Người tạo / nộp nội dung — không ai được duyệt mục có người tạo là chính mình.</summary>
    public int? CreatedByUserId { get; set; }
    public int? SourceChildProfileId { get; set; }

    /// <summary>True khi report có lý do safety_concern (nâng ưu tiên lên Cao).</summary>
    public bool IsSafetyConcern { get; set; }
    public bool IsResubmission { get; set; }
    public int? PreviousItemId { get; set; }
}

public class ModerationQueueQueryDto
{
    public ModerationItemType? ItemType { get; set; }

    /// <summary>Mặc định chỉ xem mục đang chờ (Pending).</summary>
    public ModerationItemStatus? Status { get; set; }
    public ModerationPriority? Priority { get; set; }

    /// <summary>True: chỉ hiện mục đã quá hạn SLA.</summary>
    public bool OnlyOverdue { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ModerationItemSummaryDto
{
    public int Id { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public int? StoryId { get; set; }
    /// <summary>Tên truyện để nhận diện mục trong hàng đợi; không chứa nội dung bản thảo.</summary>
    public string? StoryTitle { get; set; }
    public int? StoryVersionId { get; set; }
    public int? ContentReportId { get; set; }
    public int? ReadingRoadmapId { get; set; }
    /// <summary>Mã tài khoản gửi duyệt; chỉ trả cho mục duyệt truyện công khai của Creator.</summary>
    public int? CreatedByUserId { get; set; }
    /// <summary>Tên hiển thị của Creator gửi duyệt (họ tên, dự phòng tên đăng nhập); null với các loại mục khác.</summary>
    public string? CreatedByName { get; set; }
    public bool IsResubmission { get; set; }
    public DateTime? SlaDueAt { get; set; }
    public bool IsOverdue { get; set; }
    public int? ClaimedByUserId { get; set; }

    /// <summary>Họ tên người đang giữ hoặc đã xử lý mục (tên đăng nhập nếu chưa có họ tên); null khi chưa có người nhận.</summary>
    public string? ClaimedByName { get; set; }
    public DateTime? ClaimedAt { get; set; }

    /// <summary>Lúc mục tự trả về hàng đợi nếu chưa quyết định (ClaimedAt + 2 giờ).</summary>
    public DateTime? ClaimExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Chi tiết hàng đợi của mục đang nhận. Nội dung truyện thật do từng luồng con dựng, không nằm ở đây.</summary>
public class ModerationItemDetailDto : ModerationItemSummaryDto
{
    public int RowVersion { get; set; }
    public string? Flags { get; set; }
    public string? Checklist { get; set; }
    public string? ReviewProgress { get; set; }
    /// <summary>Truyện đang được dùng ở đâu; chỉ có ở PublicStoryRemoval / PublicStoryReview, null với loại khác.</summary>
    public StoryImpactDto? StoryImpact { get; set; }
}

public class GrantReviewViewRequestDto
{
    /// <summary>"published_content" (cần StoryId) hoặc "roadmap_templates" (cần ReadingRoadmapId).</summary>
    public string SourceTab { get; set; } = string.Empty;
    public int? StoryId { get; set; }
    public int? StoryVersionId { get; set; }
    public int? ReadingRoadmapId { get; set; }
}

public class ModerationReviewViewDto
{
    public int Id { get; set; }
    public string SourceTab { get; set; } = string.Empty;
    public int? StoryId { get; set; }
    public int? StoryVersionId { get; set; }
    public int? ReadingRoadmapId { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Truyện đang được dùng ở đâu; chỉ có ở mục PublicStoryRemoval / PublicStoryReview.</summary>
public class StoryImpactDto
{
    /// <summary>Thẻ ước đang gắn với truyện, theo nhóm rồi thứ tự hiển thị.</summary>
    public List<StoryImpactWishCardDto> WishCards { get; set; } = new();
    /// <summary>Số điều ước Pending/Accepted có truyện này trong danh sách gợi ý.</summary>
    public int PendingWishCount { get; set; }
    /// <summary>Số mục lộ trình của bé đang Waiting/Open chứa truyện này.</summary>
    public int ActiveRoadmapItemCount { get; set; }
    /// <summary>true khi truyện xin công khai lại (từng công khai) và đang có thẻ ước: nên rà lại thẻ.</summary>
    public bool WishCardsNeedReview { get; set; }
}

public class StoryImpactWishCardDto
{
    public int WishCardId { get; set; }
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>Moderator hoặc Ai.</summary>
    public string Source { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    /// <summary>Số truyện Creator khác đang công khai, cùng nhóm tuổi, còn mang thẻ này. 0 = gỡ truyện này thì thẻ hết truyện để gợi ý.</summary>
    public int OtherPublishedStoryCount { get; set; }
}
