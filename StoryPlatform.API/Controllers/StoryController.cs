using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.BLL.Common.Models;
using StoryPlatform.BLL.Modules.Story.DTOs;
using StoryPlatform.BLL.Modules.Story.Interfaces;

namespace StoryPlatform.API.Controllers;

public class StoryController : BaseApiController
{
    private readonly IStoryService _storyService;

    public StoryController(IStoryService storyService)
    {
        _storyService = storyService;
    }

    /// <summary>
    /// Lấy danh sách câu chuyện có hỗ trợ bộ lọc và phân trang
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PagedResult<StoryDto>>>> GetStories(
        [FromQuery] StoryFilterRequestDto filter, 
        CancellationToken cancellationToken)
    {
        var result = await _storyService.GetStoriesAsync(filter, cancellationToken);
        return HandleResult(result, "Lấy danh sách truyện thành công.");
    }

    /// <summary>
    /// Lấy chi tiết câu chuyện theo ID
    /// </summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<StoryDto>>> GetStoryById(
        int id, 
        CancellationToken cancellationToken)
    {
        var story = await _storyService.GetStoryByIdAsync(id, cancellationToken);
        return HandleResult(story, "Lấy chi tiết truyện thành công.");
    }

    /// <summary>
    /// Tạo câu chuyện mới (yêu cầu đăng nhập)
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ApiResponse<StoryDto>>> CreateStory(
        [FromBody] CreateStoryRequestDto request, 
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var story = await _storyService.CreateStoryAsync(currentUserId, request, cancellationToken);
        return StatusCode(201, ApiResponse<StoryDto>.Ok(story, "Tạo câu chuyện thành công."));
    }

    /// <summary>
    /// Chỉnh sửa câu chuyện (yêu cầu là tác giả)
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<StoryDto>>> UpdateStory(
        int id, 
        [FromBody] UpdateStoryRequestDto request, 
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var story = await _storyService.UpdateStoryAsync(id, currentUserId, request, cancellationToken);
        return HandleResult(story, "Cập nhật câu chuyện thành công.");
    }

    /// <summary>
    /// Xóa câu chuyện (Soft Delete - yêu cầu là tác giả)
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteStory(
        int id, 
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var result = await _storyService.DeleteStoryAsync(id, currentUserId, cancellationToken);
        return HandleResult(result, "Xóa câu chuyện thành công.");
    }

    /// <summary>
    /// Phát hành câu chuyện công khai (Publish)
    /// </summary>
    [HttpPatch("{id:int}/publish")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> PublishStory(
        int id, 
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var result = await _storyService.PublishStoryAsync(id, currentUserId, cancellationToken);
        return HandleResult(result, "Phát hành câu chuyện thành công.");
    }
}
