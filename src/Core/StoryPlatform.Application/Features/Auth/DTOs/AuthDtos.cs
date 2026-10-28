using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.Auth.DTOs;

public class LoginRequestDto
{
    [Required(ErrorMessage = "Email hoặc Tên đăng nhập không được để trống.")]
    public string Identifier { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequestDto
{
    [Required(ErrorMessage = "Tên đăng nhập không được để trống.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập từ 3 đến 50 ký tự.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    [StringLength(PasswordPolicy.MaxLength, MinimumLength = PasswordPolicy.MinLength, ErrorMessage = PasswordPolicy.LengthErrorMessage)]
    [RegularExpression(PasswordPolicy.Pattern, ErrorMessage = PasswordPolicy.CompositionErrorMessage)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống.")]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }
}

public class UserProfileDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public long ExpiresInSeconds { get; set; }
    public UserProfileDto User { get; set; } = null!;

}

public class RefreshTokenRequestDto
{
    [Required(ErrorMessage = "Refresh token không được để trống.")]
    public string RefreshToken { get; set; } = string.Empty;
}

public class ForgotPasswordRequestDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequestDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Reset token không được để trống.")]
    public string ResetToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
    [StringLength(PasswordPolicy.MaxLength, MinimumLength = PasswordPolicy.MinLength, ErrorMessage = PasswordPolicy.LengthErrorMessage)]
    [RegularExpression(PasswordPolicy.Pattern, ErrorMessage = PasswordPolicy.CompositionErrorMessage)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận mật khẩu mới không được để trống.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequestDto
{
    [Required(ErrorMessage = "Mật khẩu hiện tại không được để trống.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
    [StringLength(PasswordPolicy.MaxLength, MinimumLength = PasswordPolicy.MinLength, ErrorMessage = PasswordPolicy.LengthErrorMessage)]
    [RegularExpression(PasswordPolicy.Pattern, ErrorMessage = PasswordPolicy.CompositionErrorMessage)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận mật khẩu mới không được để trống.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Refresh token của phiên đang thao tác; bỏ trống để thu hồi mọi phiên.</summary>
    public string? CurrentRefreshToken { get; set; }
}

public class LogoutRequestDto
{
    [Required(ErrorMessage = "Refresh token không được để trống.")]
    public string RefreshToken { get; set; } = string.Empty;
}

public class VerifyEmailRequestDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã xác thực không được để trống.")]
    [RegularExpression(@"^\s*\d{6}\s*$", ErrorMessage = "Mã xác thực gồm 6 chữ số.")]
    public string Otp { get; set; } = string.Empty;
}

public class RequestEmailChangeRequestDto
{
    [Required(ErrorMessage = "Mật khẩu hiện tại không được để trống.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email mới không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string NewEmail { get; set; } = string.Empty;
}

public class ConfirmEmailChangeRequestDto
{
    [Required(ErrorMessage = "Mã xác thực không được để trống.")]
    [RegularExpression(@"^\s*\d{6}\s*$", ErrorMessage = "Mã xác thực gồm 6 chữ số.")]
    public string Otp { get; set; } = string.Empty;
}

public class UpdateProfileRequestDto
{
    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Địa chỉ ảnh đại diện tối đa 500 ký tự.")]
    public string? AvatarUrl { get; set; }
}

public class OwnedChildProfileSummaryDto
{
    public int Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class DeleteAccountPreviewDto
{
    public List<OwnedChildProfileSummaryDto> BlockingProfiles { get; set; } = new();
    public List<OwnedChildProfileSummaryDto> DraftProfiles { get; set; } = new();
    public bool CanDelete => BlockingProfiles.Count == 0;
}

public class DeleteAccountRequestDto
{
    [Required(ErrorMessage = "Mật khẩu hiện tại không được để trống.")]
    public string CurrentPassword { get; set; } = string.Empty;

    public bool DeleteDraftProfiles { get; set; }
}

public class ResendVerificationEmailRequestDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;
}

public class SessionDto
{
    public int Id { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class StaffActivationRequestDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã mời không được để trống.")]
    public string InviteToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    [StringLength(PasswordPolicy.MaxLength, MinimumLength = PasswordPolicy.MinLength, ErrorMessage = PasswordPolicy.LengthErrorMessage)]
    [RegularExpression(PasswordPolicy.Pattern, ErrorMessage = PasswordPolicy.CompositionErrorMessage)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Đã tick cam kết bảo mật dữ liệu trẻ em và quy tắc kiểm duyệt.</summary>
    public bool CommitmentAccepted { get; set; }
}

/// <summary>Chính sách mật khẩu đã chốt (Mục 9): tối thiểu 8 ký tự, có ít nhất 1 chữ cái và 1 chữ số.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 100;
    public const string Pattern = @"^(?=.*\p{L})(?=.*\d).+$";
    public const string LengthErrorMessage = "Mật khẩu phải từ 8 đến 100 ký tự.";
    public const string CompositionErrorMessage = "Mật khẩu phải chứa ít nhất 1 chữ cái và 1 chữ số.";
}

