namespace StoryPlatform.Domain.Enums;

/// <summary>Cấp của Content Creator (Luồng 5, 5g); chỉ có nghĩa với tài khoản ContentCreator.</summary>
public enum CreatorTier
{
    /// <summary>Creator thường: hạn mức tháng 2 lượt AI + 5 lượt gửi duyệt.</summary>
    Standard = 1,
    /// <summary>Creator đối tác: hạn mức 6 + 10, trợ lý viết 40 lần/ngày, ưu tiên Cao trong hàng đợi duyệt.</summary>
    Partner = 2
}
