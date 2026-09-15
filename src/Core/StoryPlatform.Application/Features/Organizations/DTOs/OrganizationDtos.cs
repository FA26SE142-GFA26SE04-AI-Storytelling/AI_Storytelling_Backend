using System.ComponentModel.DataAnnotations;

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
}
