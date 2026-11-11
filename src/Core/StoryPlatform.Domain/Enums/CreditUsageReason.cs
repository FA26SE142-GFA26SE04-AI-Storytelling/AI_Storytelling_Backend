namespace StoryPlatform.Domain.Enums;

public enum CreditUsageReason
{
    /// <summary>Phụ huynh gửi yêu cầu tạo truyện AI.</summary>
    StoryRequest = 1,
    /// <summary>Admin trừ lượt của tài khoản (UC-L2-08).</summary>
    AdminDeduction = 2,
    /// <summary>Content Creator gửi truyện đi duyệt; hoàn lại khi bị từ chối hoặc rút khi còn chờ.</summary>
    PublicSubmission = 3
}
