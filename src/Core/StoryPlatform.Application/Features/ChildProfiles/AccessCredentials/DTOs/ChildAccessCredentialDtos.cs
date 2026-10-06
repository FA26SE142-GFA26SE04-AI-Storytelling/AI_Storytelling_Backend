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
