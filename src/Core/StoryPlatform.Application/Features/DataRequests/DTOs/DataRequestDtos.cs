using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.DataRequests.DTOs;

public class CreateDataRequestDto
{
    public int ChildProfileId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public string? CurrentPassword { get; set; }
    public string? ConfirmNickname { get; set; }
}

public class DataRequestDto
{
    public int Id { get; set; }
    public int ChildProfileId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
}

public class RejectDataRequestDto
{
    [Required(ErrorMessage = "Lý do từ chối không được để trống.")]
    [StringLength(500, ErrorMessage = "Lý do từ chối tối đa 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

public class ExportedFileDto
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
}
