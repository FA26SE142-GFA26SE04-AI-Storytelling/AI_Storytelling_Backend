using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;

public class ChildSessionDto
{
    public int ChildProfileId { get; set; }
    public string AvatarId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public long ExpiresInSeconds { get; set; }
}

public class ChildAccessCredentialDto
{
    public int ChildProfileId { get; set; }

    /// <summary>Hồ sơ đã có Easy Login đang hiệu lực chưa. KHÔNG BAO GIỜ trả secret hay hash.</summary>
    public bool HasEasyLogin { get; set; }

    public DateTime? EasyLoginCreatedAt { get; set; }
}

public class ChildSessionProfileDto
{
    public int ChildProfileId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string AgeBand { get; set; } = string.Empty;
}

/// <summary>Secret QR lâu dài — chỉ trả đúng một lần lúc tạo/tạo lại; client dựng QR từ giá trị này.</summary>
public class EasyLoginSecretDto
{
    public string Secret { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class LoginWithEasyLoginRequestDto
{
    [Required(ErrorMessage = "Mã EasyLogin không được để trống.")]
    [StringLength(128, ErrorMessage = "Mã EasyLogin không hợp lệ.")]
    public string Secret { get; set; } = string.Empty;
}

public class StartSupervisedChildSessionRequestDto
{
    /// <summary>Refresh token của phiên Supervisor đang bàn giao thiết bị — dùng để ghi audit, không cấp lại.</summary>
    [Required(ErrorMessage = "Refresh token của phiên Supervisor không được để trống.")]
    public string SupervisorRefreshToken { get; set; } = string.Empty;
}

public class StartStoryHandoverRequestDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Hãy chọn truyện cho bé đọc.")]
    public int StoryId { get; set; }

    /// <summary>Refresh token của phiên Supervisor đang bàn giao thiết bị — dùng để gắn phiên trẻ, không cấp lại.</summary>
    [Required(ErrorMessage = "Refresh token của phiên Supervisor không được để trống.")]
    public string SupervisorRefreshToken { get; set; } = string.Empty;
}

/// <summary>Child Session "Cho bé đọc truyện này" — app mở thẳng StoryId bằng AccessToken của trẻ.</summary>
public class ChildStoryHandoverDto : ChildSessionDto
{
    public int StoryId { get; set; }
}

/// <summary>Bé nào của phụ huynh đọc được truyện — để bật nút "Cho bé đọc truyện này" và gợi ý bé.</summary>
public class StoryHandoverOptionsDto
{
    public int StoryId { get; set; }

    /// <summary>Bé chủ truyện riêng nếu đọc được; nếu không, bé duy nhất đọc được; còn lại null.</summary>
    public int? SuggestedChildProfileId { get; set; }

    public List<StoryHandoverChildDto> Children { get; set; } = new();
}

public class StoryHandoverChildDto
{
    public int ChildProfileId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public bool CanRead { get; set; }

    /// <summary>Mã trong ChildRoadmapSkipCodes; null khi đọc được.</summary>
    public string? SkipCode { get; set; }

    public string? Message { get; set; }
}
