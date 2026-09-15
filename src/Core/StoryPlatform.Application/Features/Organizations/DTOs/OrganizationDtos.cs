using System.ComponentModel.DataAnnotations;
using StoryPlatform.Application.Features.Auth.DTOs;

namespace StoryPlatform.Application.Features.Organizations.DTOs;

public class OrganizationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactEmail { get; set; }
    public string VerificationStatus { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateOrganizationRequestDto
{
    [Required(ErrorMessage = "Tên tổ chức không được để trống.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Tên tổ chức phải từ 1 đến 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(255, ErrorMessage = "Địa chỉ tối đa 255 ký tự.")]
    public string? Address { get; set; }

    [EmailAddress(ErrorMessage = "Email liên hệ không đúng định dạng.")]
    [StringLength(150, ErrorMessage = "Email liên hệ tối đa 150 ký tự.")]
    public string? ContactEmail { get; set; }

    [Required(ErrorMessage = "Tên đăng nhập quản trị tổ chức không được để trống.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập từ 3 đến 50 ký tự.")]
    public string SchoolAdminUsername { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email quản trị tổ chức không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string SchoolAdminEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ và tên quản trị tổ chức không được để trống.")]
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự.")]
    public string SchoolAdminFullName { get; set; } = string.Empty;

    public string? SchoolAdminPhoneNumber { get; set; }
}

public class CreateOrganizationResponseDto
{
    public OrganizationDto Organization { get; set; } = null!;
    public CreatedAccountDto SchoolAdminAccount { get; set; } = null!;
}

public class CreateTeacherAccountRequestDto
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

    public string? PhoneNumber { get; set; }
}
