namespace StoryPlatform.Application.Features.ChildProfiles.ReadingSlots;

/// <summary>Hồ sơ được đọc / chỉ xem khi số hồ sơ Active vượt quyền lợi gói (Luồng 1, 1e).</summary>
public class ReadingSlotsDto
{
    /// <summary>Số hồ sơ được đọc cùng lúc (= số hồ sơ tối đa của gói).</summary>
    public int Slots { get; set; }
    /// <summary>True khi số hồ sơ Active nhiều hơn số chỗ: một số hồ sơ đang chỉ xem.</summary>
    public bool IsOverLimit { get; set; }
    /// <summary>Lần đổi tiếp theo (00:00 thứ Hai giờ Việt Nam, UTC); null = đổi được ngay.</summary>
    public DateTime? NextChangeAllowedAt { get; set; }
    public List<ReadingSlotProfileDto> Profiles { get; set; } = [];
}

public class ReadingSlotProfileDto
{
    public int ChildProfileId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    /// <summary>False = chỉ xem: trẻ không vào đọc được.</summary>
    public bool CanRead { get; set; }
    /// <summary>Phụ huynh đã chọn hồ sơ này.</summary>
    public bool IsSelected { get; set; }
}

public class SelectReadingSlotsRequestDto
{
    public List<int> ChildProfileIds { get; set; } = [];
}
