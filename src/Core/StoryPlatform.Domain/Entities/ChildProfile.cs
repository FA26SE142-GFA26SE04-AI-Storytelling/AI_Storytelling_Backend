using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class ChildProfile : BaseEntity
{
    public const string DefaultAvatarId = "avatar-default";

    public int OwnerUserId { get; set; }
    public virtual UserAccount? OwnerUser { get; set; }

    public string Nickname { get; set; } = string.Empty;
    /// <summary>Ảnh đại diện hiển thị ở ProfileSwitcher / màn hình Easy Login.</summary>
    public string AvatarId { get; set; } = DefaultAvatarId;
    public AgeBand AgeBand { get; set; }
    public string Language { get; set; } = "vi";
    public ChildProfileStatus Status { get; set; } = ChildProfileStatus.Draft;
    /// <summary>
    /// Phụ huynh chọn hồ sơ này để tiếp tục đọc khi số hồ sơ vượt quyền lợi gói (Luồng 1, 1e). Giá trị lớn nhất
    /// trên các hồ sơ của tài khoản là lần đổi gần nhất (giới hạn 1 lần mỗi tuần).
    /// </summary>
    public DateTime? ReadingSlotSelectedAt { get; set; }
}
