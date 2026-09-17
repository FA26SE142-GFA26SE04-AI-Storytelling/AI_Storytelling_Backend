using System.ComponentModel.DataAnnotations;

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
    [Required(ErrorMessage = "Vui lòng chọn người nhận quyền Owner.")]
    public int TargetSupervisorUserId { get; set; }
}
