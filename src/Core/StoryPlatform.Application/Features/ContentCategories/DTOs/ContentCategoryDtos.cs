using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.ContentCategories.DTOs;

public class ContentCategoryDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class CreateContentCategoryRequestDto
{
    [Required(ErrorMessage = "Mã danh mục không được để trống.")]
    [StringLength(100, ErrorMessage = "Mã danh mục tối đa 100 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên hiển thị không được để trống.")]
    [StringLength(150, ErrorMessage = "Tên hiển thị tối đa 150 ký tự.")]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class UpdateContentCategoryRequestDto
{
    [Required(ErrorMessage = "Tên hiển thị không được để trống.")]
    [StringLength(150, ErrorMessage = "Tên hiển thị tối đa 150 ký tự.")]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
