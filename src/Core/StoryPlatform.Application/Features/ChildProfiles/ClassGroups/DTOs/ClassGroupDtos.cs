using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.ChildProfiles.ClassGroups.DTOs;

public class ClassGroupDto
{
    public int Id { get; set; }
    public int TeacherUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? OrganizationId { get; set; }
}

public class CreateClassGroupRequestDto
{
    [Required(ErrorMessage = "Tên lớp không được để trống.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Tên lớp từ 1 đến 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Để trống nếu lớp không thuộc Organization.</summary>
    public int? OrganizationId { get; set; }
}

public class UpdateClassGroupRequestDto
{
    [Required(ErrorMessage = "Tên lớp không được để trống.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Tên lớp từ 1 đến 150 ký tự.")]
    public string Name { get; set; } = string.Empty;
}
