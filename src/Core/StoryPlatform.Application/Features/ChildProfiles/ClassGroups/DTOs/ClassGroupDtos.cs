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

public class BulkEnrollRowResultDto
{
    public int RowNumber { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ChildProfileId { get; set; }
    public string? InvitationCode { get; set; }
}

public class BulkEnrollResultDto
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<BulkEnrollRowResultDto> Rows { get; set; } = new();
}
