namespace StoryPlatform.Application.Features.StoryWishes;

/// <summary>Luật xử lý điều ước của phụ huynh và các mốc thời gian (WF-L2D UC-L2D-04).</summary>
public static class StoryWishDecisionPolicy
{
    /// <summary>Hạn xử lý: 7 ngày từ lúc vào Pending.</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(7);
    /// <summary>Ngày 6 chưa ai xử lý → story_wish_expiring.</summary>
    public static readonly TimeSpan ExpiryWarningAfter = TimeSpan.FromDays(6);
    /// <summary>Truyện mới bị Từ chối và 7 ngày không có bản mới gửi duyệt → điều ước Declined.</summary>
    public static readonly TimeSpan RejectedStoryGrace = TimeSpan.FromDays(7);

    public const int MaxDeclineReasonLength = 500;
    public const int MaxTopicLength = 200;
    public const int ListLimit = 50;

    public const string NoteExpired = "expired";
    public const string NoteStoryArchived = "story_archived";
    public const string NoteStoryRejected = "story_rejected";
    public const string NoteParentCancelled = "parent_cancelled";
    public const string NoteProfileInactive = "profile_inactive";

    public const string AlreadyHandledMessage = "Điều ước này đã được xử lý.";
    public const string OnlySuggestedMessage = "Chỉ chọn được truyện trong danh sách gợi ý của điều ước.";
    public const string CommunityNotSupportedMessage =
        "Truyện Cộng đồng chưa thêm được vào lộ trình. Hãy chọn truyện khác hoặc tạo truyện mới.";
    public const string WishOnlyMessage = "Truyện bé sáng tác không dùng truyện có sẵn.";
    public const string ChildMismatchMessage = "Yêu cầu sinh truyện phải dành cho đúng bé đã ước.";
    public const string ReasonTooLongMessage = "Lý do từ chối tối đa 500 ký tự.";

    /// <summary>Chủ đề điền sẵn: "nhân vật ở nơi chốn[: thử thách]", tối đa 200 ký tự (giới hạn của form sinh truyện).</summary>
    public static string Topic(string character, string place, string? challenge)
    {
        var topic = string.IsNullOrWhiteSpace(challenge)
            ? $"{character} ở {place}"
            : $"{character} ở {place}: {challenge}";
        return topic.Length <= MaxTopicLength ? topic : topic[..MaxTopicLength];
    }

    /// <summary>Khóa chống gửi trùng cố định theo điều ước: bấm hai lần hay hai phụ huynh cùng bấm vẫn chỉ ra một truyện.</summary>
    public static string GenerationKey(int wishId) => $"story-wish-{wishId:D6}";
}
