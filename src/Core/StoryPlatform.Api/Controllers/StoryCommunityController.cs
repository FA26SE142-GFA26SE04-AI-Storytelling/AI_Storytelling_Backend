using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.Stories.DTOs;
using StoryPlatform.Application.Features.Stories.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Thư viện chung: truyện của Creator đã được duyệt và công khai, cho mọi vai trò đăng nhập.</summary>
[Authorize(Roles = "User,ContentCreator,Moderator,Administrator")]
[Route("api/v1/Story/community")]
public class StoryCommunityController : BaseApiController
{
    private readonly IStoryService _storyService;
    private readonly IMediaGenerationService _mediaService;

    public StoryCommunityController(IStoryService storyService, IMediaGenerationService mediaService)
    {
        _storyService = storyService;
        _mediaService = mediaService;
    }

    /// <summary>Danh sách truyện Creator đã công khai, có lọc, phân trang và lượt xem/yêu thích; orderBy = views | favorites.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CommunityStoryDto>>>> GetStories(
        [FromQuery] StoryFilterRequestDto filter, CancellationToken cancellationToken)
    {
        var result = await _storyService.GetPublishedCreatorStoriesAsync(filter, cancellationToken);
        return HandleResult(result, "Lấy thư viện cộng đồng thành công.");
    }

    /// <summary>Truyện đang công khai của chính Creator, kèm lượt xem/yêu thích.</summary>
    [Authorize(Roles = "ContentCreator")]
    [HttpGet("/api/v1/creator/stories/published")]
    public async Task<ActionResult<ApiResponse<PagedResult<CommunityStoryDto>>>> GetMyPublishedStories(
        [FromQuery] StoryFilterRequestDto filter, CancellationToken cancellationToken)
    {
        var result = await _storyService.GetMyPublishedCreatorStoriesAsync(GetCurrentUserId(), filter, cancellationToken);
        return HandleResult(result, "Lấy danh sách truyện đã đăng thành công.");
    }

    /// <summary>Ảnh minh họa và giọng đọc (chỉ đọc) của một truyện Creator đã công khai.</summary>
    [HttpGet("{id:int}/media")]
    public async Task<ActionResult<ApiResponse<StoryMediaPackage>>> GetStoryMedia(
        int id, CancellationToken cancellationToken)
    {
        var package = await _mediaService.GetPublishedCreatorPackageAsync(id, cancellationToken);
        return HandleResult(package, "Lấy tranh và giọng đọc thành công.");
    }
}
