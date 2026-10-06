namespace StoryPlatform.Domain.Enums;

public enum OwnershipTransferRequestStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3,
    // Luồng cũ: Additional Supervisor khởi xướng. Chỉ giữ để đọc dữ liệu cũ; KHÔNG tạo mới (BR-1.13).
    PendingOwnerResponse = 4,
    AcceptedByOwner = 5,
    RejectedByOwner = 6,
    Cancelled = 7,
    Expired = 8
}
