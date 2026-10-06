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
    /// <summary>Email người được mời — bắt buộc; OTP xác thực sẽ gửi tới địa chỉ này (BR-1.4).</summary>
    [Required(ErrorMessage = "Email người được mời là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email người được mời không đúng định dạng.")]
    [StringLength(150, ErrorMessage = "Email người được mời tối đa 150 ký tự.")]
    public string? InviteeEmail { get; set; }

    [Range(1, 30, ErrorMessage = "Số ngày hết hạn phải từ 1 đến 30.")]
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

public class ClaimInvitationRequestDto
{
    [Required(ErrorMessage = "Mã mời không được để trống.")]
    public string InvitationCode { get; set; } = string.Empty;
}

public class ClaimInvitationResultDto
{
    public string MaskedEmail { get; set; } = string.Empty;
    public DateTime OtpExpiresAt { get; set; }
}

public class VerifyInvitationOtpRequestDto
{
    [Required(ErrorMessage = "Mã mời không được để trống.")]
    public string InvitationCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã OTP không được để trống.")]
    [RegularExpression("^[0-9]{6}$", ErrorMessage = "Mã OTP gồm đúng 6 chữ số.")]
    public string Otp { get; set; } = string.Empty;
}

/// <summary>Thông tin hồ sơ ở phạm vi cho phép, chỉ hiện sau khi xác thực OTP (Bước 1.6).</summary>
public class InvitationPreviewDto
{
    public int InvitationId { get; set; }
    public string InviterName { get; set; } = string.Empty;
    public string ChildNickname { get; set; } = string.Empty;
    public string ChildAgeBand { get; set; } = string.Empty;
}

public class RejectInvitationRequestDto
{
    [Required(ErrorMessage = "Mã mời không được để trống.")]
    public string InvitationCode { get; set; } = string.Empty;
}

public class TransferOwnershipRequestDto
{
    /// <summary>Additional Supervisor đang hoạt động sẽ nhận quyền Owner. Chỉ Owner hiện tại được gửi yêu cầu.</summary>
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
    public DateTime? ExpiresAt { get; set; }
}
