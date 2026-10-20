using StoryPlatform.Application.Common.Models;

namespace StoryPlatform.Application.Features.CommunityLibrary.Interfaces;

/// <summary>Thư viện cộng đồng phía bé (WF-L4F UC-L4F-02, WF-L3A).</summary>
public interface ICommunityLibraryService
{
    /// <summary>Truyện Community đã duyệt, đã lọc theo độ tuổi và chính sách an toàn của đúng bé này.</summary>
    Task<PagedResult<CommunityStoryDto>> ListAsync(
        int childProfileId, CommunityLibraryQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>Thông tin một truyện Community; NotFound nếu bé không được thấy hoặc truyện đã gỡ.</summary>
    Task<CommunityStoryDto> GetAsync(
        int childProfileId, int submissionId, CancellationToken cancellationToken = default);
}
