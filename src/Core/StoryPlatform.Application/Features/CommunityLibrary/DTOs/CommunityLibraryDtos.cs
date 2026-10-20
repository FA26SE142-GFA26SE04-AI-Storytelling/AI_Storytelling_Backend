namespace StoryPlatform.Application.Features.CommunityLibrary;

public static class CommunityLibraryPolicy
{
    /// <summary>Tác giả hiển thị của mọi truyện Community (BR-L4F-06).</summary>
    public const string AuthorName = "Cộng đồng";
}

public class CommunityLibraryQueryDto
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>Một truyện trong thư viện cộng đồng, nhìn từ phía bé. Không có người nộp, bé nguồn, người duyệt.</summary>
public class CommunityStoryDto
{
    /// <summary>Mã dùng để mở truyện (không phải Id truyện gốc).</summary>
    public int SubmissionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public int? ReadingLevel { get; set; }
    public List<CommunityLabelDto> Labels { get; set; } = new();

    /// <summary>Luôn là "Cộng đồng".</summary>
    public string Author { get; set; } = CommunityLibraryPolicy.AuthorName;
    public DateTime? ApprovedAt { get; set; }
}
