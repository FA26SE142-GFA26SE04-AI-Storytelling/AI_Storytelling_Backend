using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CommunityLibrary;
using StoryPlatform.Application.Features.CommunityLibrary.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Thư viện cộng đồng phía bé (Luồng 4 — WF-L4F, Luồng 3 — WF-L3A).</summary>
[Authorize(Policy = "ChildSession")]
public class CommunityLibraryController : BaseApiController
{
    private readonly ICommunityLibraryService _libraryService;

    public CommunityLibraryController(ICommunityLibraryService libraryService)
    {
        _libraryService = libraryService;
    }

    /// <summary>Truyện Community đã duyệt, đã lọc theo độ tuổi và chính sách an toàn của đúng bé đang đăng nhập.</summary>
    /// <remarks>
    /// Lưu ý: chỉ gọi được bằng token của trẻ; Id hồ sơ lấy từ token. Tác giả luôn là "Cộng đồng"; không có người nộp
    /// hay bé nguồn. submissionId là mã dùng để mở truyện.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CommunityStoryDto>>>> List(
        [FromQuery] CommunityLibraryQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _libraryService.ListAsync(GetCurrentUserId(), query, cancellationToken);
        return HandleResult(result, "Lấy thư viện cộng đồng thành công.");
    }

    /// <summary>Thông tin một truyện Community (404 nếu bé không được thấy hoặc truyện đã gỡ).</summary>
    [HttpGet("{submissionId:int}")]
    public async Task<ActionResult<ApiResponse<CommunityStoryDto>>> Get(
        int submissionId, CancellationToken cancellationToken)
    {
        var result = await _libraryService.GetAsync(GetCurrentUserId(), submissionId, cancellationToken);
        return HandleResult(result, "Lấy truyện cộng đồng thành công.");
    }
}
