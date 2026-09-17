namespace StoryPlatform.Application.Features.Administration.DTOs;

public class GrantAdministratorRequestDto
{
    public string Email { get; set; } = string.Empty;
    public bool ConfirmSchoolAdminConflict { get; set; } = false;
}

public class AdministratorAccountDto
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? RoleBeforeAdmin { get; set; }
}
