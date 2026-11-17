namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Thông tin nhận tiền của Creator (Luồng 5, 5h): mỗi Creator một dòng, Creator tự sửa. Hệ thống không chuyển tiền,
/// Admin dùng thông tin này để chuyển khoản ngoài hệ thống.
/// </summary>
public class CreatorPayoutAccount : BaseEntity
{
    public int CreatorUserId { get; set; }
    public virtual UserAccount? Creator { get; set; }

    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
}
