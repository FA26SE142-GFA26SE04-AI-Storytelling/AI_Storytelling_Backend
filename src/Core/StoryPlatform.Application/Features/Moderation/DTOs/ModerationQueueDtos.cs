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
    public int? StoryVersionId { get; set; }
    public int? ContentReportId { get; set; }
    public int? ReadingRoadmapId { get; set; }
    public bool IsResubmission { get; set; }
    public DateTime? SlaDueAt { get; set; }
    public bool IsOverdue { get; set; }
    public int? ClaimedByUserId { get; set; }
    public DateTime? ClaimedAt { get; set; }

    /// <summary>Lúc mục tự trả về hàng đợi nếu chưa quyết định (ClaimedAt + 2 giờ).</summary>
    public DateTime? ClaimExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Chi tiết hàng đợi của mục đang nhận. Nội dung truyện thật do từng luồng con dựng, không nằm ở đây.</summary>
public class ModerationItemDetailDto : ModerationItemSummaryDto
{
    public string? Flags { get; set; }
    public string? Checklist { get; set; }
    public string? ReviewProgress { get; set; }
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
