namespace StoryPlatform.Application.Features.DataRequests.DTOs;

public class CreateDataRequestDto
{
    public int ChildProfileId { get; set; }
    public string RequestType { get; set; } = string.Empty;
}

public class DataRequestDto
{
    public int Id { get; set; }
    public int ChildProfileId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
}

public class ExportedFileDto
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
}
