using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;

public class SetChildAccessCredentialRequestDto
{
    [Required(ErrorMessage = "Avatar không được để trống.")]
    [StringLength(100, ErrorMessage = "Avatar ID tối đa 100 ký tự.")]
    public string AvatarId { get; set; } = string.Empty;

    [Required(ErrorMessage = "PIN không được để trống.")]
    [RegularExpression(@"^\d{4,6}$", ErrorMessage = "PIN phải gồm 4-6 chữ số.")]
    public string Pin { get; set; } = string.Empty;
}

public class ChildSessionDto
{
    public int ChildProfileId { get; set; }
    public string AvatarId { get; set; } = string.Empty;
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
    [RegularExpression(@"^\d{4,6}$", ErrorMessage = "PIN phải gồm 4-6 chữ số.")]
    public string Pin { get; set; } = string.Empty;
}
