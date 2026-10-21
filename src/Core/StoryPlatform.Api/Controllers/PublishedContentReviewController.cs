using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Tab "Nội dung đã công khai" của Moderation view.</summary>
[Authorize(Roles = "Moderator,Administrator")]
public class PublishedContentReviewController : BaseApiController
{
    private readonly IPublishedContentReviewService _publishedContentService;
    private readonly IContentReportService _reportService;

    public PublishedContentReviewController(
        IPublishedContentReviewService publishedContentService,
        IContentReportService reportService)
    {
        _publishedContentService = publishedContentService;
        _reportService = reportService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PublishedContentItemDto>>>> List(
        [FromQuery] PublishedContentQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _publishedContentService.ListAsync(
            GetCurrentUserId(), query, cancellationToken);
        return HandleResult(result, "Lấy danh sách nội dung đã công khai thành công.");
    }

    [HttpGet("stories/{storyId:int}")]
    public async Task<ActionResult<ApiResponse<PublishedStoryContentDto>>> GetStory(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _publishedContentService.GetStoryContentAsync(
            GetCurrentUserId(), storyId, cancellationToken);
        return HandleResult(result, "Mở nội dung truyện thành công.");
    }

    [HttpGet("roadmaps/{roadmapId:int}")]
    public async Task<ActionResult<ApiResponse<PublishedRoadmapDto>>> GetRoadmap(
        int roadmapId, CancellationToken cancellationToken)
    {
        var result = await _publishedContentService.GetRoadmapAsync(
            GetCurrentUserId(), roadmapId, cancellationToken);
        return HandleResult(result, "Mở bộ truyện mẫu thành công.");
    }

    [Authorize(Roles = "Moderator")]
    [HttpPost("review-items")]
    public async Task<ActionResult<ApiResponse<ContentReportResultDto>>> CreateReviewItem(
        [FromBody] CreateReviewItemRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _reportService.CreateReviewItemAsync(
            GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã tạo mục rà soát.");
    }
}
