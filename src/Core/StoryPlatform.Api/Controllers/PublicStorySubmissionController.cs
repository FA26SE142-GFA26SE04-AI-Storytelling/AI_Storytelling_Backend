using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>API cho Content Creator gửi, gửi lại hoặc rút yêu cầu duyệt truyện Public Library.</summary>
[Authorize(Roles = "ContentCreator")]
public class PublicStorySubmissionController : BaseApiController
{
    private readonly IPublicStorySubmissionService _submissionService;

    public PublicStorySubmissionController(IPublicStorySubmissionService submissionService)
    {
        _submissionService = submissionService;
    }

    /// <summary>Gửi một phiên bản truyện vào hàng đợi kiểm duyệt Public Library.</summary>
    /// <remarks>
    /// Chỉ chủ sở hữu truyện Creator đang Ready và có đầy đủ media mới được gửi. Nếu lần duyệt gần nhất
    /// bị yêu cầu sửa hoặc từ chối, phiên bản mới phải có nội dung, media hoặc artifact khác bản bị trả về.
    /// Mỗi truyện chỉ có một mục Pending/Claimed tại một thời điểm.
    /// </remarks>
    [HttpPost("submit")]
    public async Task<ActionResult<ApiResponse<PublicStorySubmissionResultDto>>> Submit(
        [FromBody] SubmitPublicStoryRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _submissionService.SubmitAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã gửi truyện đi duyệt.");
    }

    /// <summary>Rút yêu cầu duyệt khi chưa có Moderator nhận mục.</summary>
    /// <remarks>
    /// Chỉ Content Creator đã gửi yêu cầu mới được rút và chỉ khi mục còn Pending. Phiên bản sẽ trở về
    /// trạng thái chưa gửi duyệt để Creator tiếp tục chỉnh sửa hoặc gửi lại.
    /// </remarks>
    [HttpPost("{itemId:int}/withdraw")]
    public async Task<ActionResult<ApiResponse<PublicStorySubmissionResultDto>>> Withdraw(
        int itemId, CancellationToken cancellationToken)
    {
        var result = await _submissionService.WithdrawAsync(GetCurrentUserId(), itemId, cancellationToken);
        return HandleResult(result, "Đã rút yêu cầu duyệt.");
    }
}
