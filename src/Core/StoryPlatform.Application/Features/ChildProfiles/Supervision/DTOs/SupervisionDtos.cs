using System.ComponentModel.DataAnnotations;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Supervision.DTOs;

public class InvitationDto
{
    public int Id { get; set; }
    public int ChildProfileId { get; set; }
    public string InvitationCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}

public class CreateInvitationRequestDto
{
    /// <summary>Chỉ dùng để hiển thị, không dùng để xác thực người nhận.</summary>
    [EmailAddress(ErrorMessage = "Email người được mời không đúng định dạng.")]
    [StringLength(150, ErrorMessage = "Email người được mời tối đa 150 ký tự.")]
    public string? InviteeEmail { get; set; }

    [Range(1, 365, ErrorMessage = "Số ngày hết hạn phải từ 1 đến 365.")]
    public int ExpiresInDays { get; set; } = 7;
}

public class SupervisionRelationshipDto
{
    public int Id { get; set; }
    public int ChildProfileId { get; set; }
    public int SupervisorUserId { get; set; }
    public string SupervisorRole { get; set; } = string.Empty;
}

public class AcceptInvitationRequestDto
{
    [Required(ErrorMessage = "Mã mời không được để trống.")]
    public string InvitationCode { get; set; } = string.Empty;
}

public class TransferOwnershipRequestDto
{
    /// <summary>
    /// User ở phía đối diện: Additional Supervisor khi người gửi là Owner,
    /// hoặc Owner hiện tại khi người gửi là Additional Supervisor.
    /// </summary>
    [Required(ErrorMessage = "Vui lòng chọn người nhận yêu cầu đổi quyền Owner.")]
    public int TargetSupervisorUserId { get; set; }
}

public class CreatePermissionRequestRequestDto
{
    [Required(ErrorMessage = "Vui lòng chọn ít nhất 1 quyền cần xin.")]
    [MinLength(1, ErrorMessage = "Vui lòng chọn ít nhất 1 quyền cần xin.")]
    public List<Permission> Permissions { get; set; } = new();
}

public class PermissionRequestDto
{
    public int Id { get; set; }
    public int SupervisionRelationshipId { get; set; }
    public int RequesterUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}

public class OwnershipTransferRequestDto
{
    public int Id { get; set; }
    public int ChildProfileId { get; set; }
    public int CurrentOwnerUserId { get; set; }
    public int TargetSupervisorUserId { get; set; }
    public int RequesterUserId { get; set; }
    public int ResponderUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
