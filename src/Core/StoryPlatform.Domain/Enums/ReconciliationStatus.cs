namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Trạng thái ngoại lệ đối soát: giao dịch Paid nhưng chưa được áp gói (UC-L2-13).
/// </summary>
public enum ReconciliationStatus
{
    Open = 1,
    Resolved = 2
}
