using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;

public class SetChildAccessCredentialRequestDto
{
    [Required(ErrorMessage = "Avatar không được để trống.")]
    [StringLength(100, ErrorMessage = "Avatar ID tối đa 100 ký tự.")]
    public string AvatarId { get; set; } = string.Empty;

    [Required(ErrorMessage = "PIN không được để trống.")]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN phải gồm đúng 4 chữ số.")]
    public string Pin { get; set; } = string.Empty;
}

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
    public string AvatarId { get; set; } = string.Empty;
    /// <summary>KHÔNG BAO GIỜ trả PinHash hay giá trị PIN dưới bất kỳ hình thức nào.</summary>
    public bool HasPin { get; set; }
    public bool IsLocked { get; set; }
}

public class LoginWithPinRequestDto
{
    [Required(ErrorMessage = "PIN không được để trống.")]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN phải gồm đúng 4 chữ số.")]
    public string Pin { get; set; } = string.Empty;
}

public class ChildSessionProfileDto
{
    public int ChildProfileId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string AgeBand { get; set; } = string.Empty;
}

public class EasyLoginCodeDto
{
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class LoginWithEasyLoginRequestDto
{
    [Required(ErrorMessage = "Mã EasyLogin không được để trống.")]
    public string Code { get; set; } = string.Empty;
}
