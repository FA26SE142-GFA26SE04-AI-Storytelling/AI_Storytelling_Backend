using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuthoringGuideManagement.DTOs;
using StoryPlatform.Application.Features.AuthoringGuideManagement.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Moderator / Administrator quản lý hướng dẫn sáng tác của Parent (bản nháp → xem trước → phát hành cả bộ).</summary>
[Authorize(Roles = "Moderator,Administrator")]
[Route("api/v1/moderation/authoring-guides")]
public sealed class AuthoringGuideManagementController(IAuthoringGuideManagementService service) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<GuideManagementOverviewDto>>> Overview(CancellationToken ct) =>
        Ok(ApiResponse<GuideManagementOverviewDto>.Ok(await service.GetOverviewAsync(GetCurrentUserId(), ct)));

    [HttpGet("versions")]
    public async Task<ActionResult<ApiResponse<PagedResult<GuideVersionSummaryDto>>>> Versions(
        CancellationToken ct, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20) =>
        Ok(ApiResponse<PagedResult<GuideVersionSummaryDto>>.Ok(await service.ListVersionsAsync(GetCurrentUserId(), pageIndex, pageSize, ct)));

    [HttpGet("versions/{versionId:int}")]
    public async Task<ActionResult<ApiResponse<GuideVersionDetailDto>>> Version(int versionId, CancellationToken ct) =>
        Ok(ApiResponse<GuideVersionDetailDto>.Ok(await service.GetVersionAsync(GetCurrentUserId(), versionId, ct)));

    [HttpPost("draft")]
    public async Task<ActionResult<ApiResponse<GuideDraftChangeDto>>> CreateDraft(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CreateGuideDraftRequest? request,
        CancellationToken ct) =>
        StatusCode(201, ApiResponse<GuideDraftChangeDto>.Ok(await service.CreateDraftAsync(GetCurrentUserId(), request ?? new(null), ct)));

    [HttpPut("draft/items/{guideId}")]
    public async Task<ActionResult<ApiResponse<GuideDraftChangeDto>>> UpsertItem(string guideId, UpsertGuideItemRequest request, CancellationToken ct) =>
        Ok(ApiResponse<GuideDraftChangeDto>.Ok(await service.UpsertItemAsync(GetCurrentUserId(), guideId, request, ct)));

    [HttpDelete("draft/items/{guideId}")]
    public async Task<ActionResult<ApiResponse<GuideDraftChangeDto>>> RemoveItem(string guideId, [FromQuery] string? revisionToken, CancellationToken ct) =>
        Ok(ApiResponse<GuideDraftChangeDto>.Ok(await service.RemoveItemAsync(GetCurrentUserId(), guideId, revisionToken, ct)));

    [HttpDelete("draft")]
    public async Task<ActionResult<ApiResponse<GuideDraftDiscardDto>>> DiscardDraft([FromQuery] string? revisionToken, CancellationToken ct) =>
        Ok(ApiResponse<GuideDraftDiscardDto>.Ok(await service.DiscardDraftAsync(GetCurrentUserId(), revisionToken, ct)));

    [HttpPost("draft/preview")]
    public async Task<ActionResult<ApiResponse<GuideDraftPreviewDto>>> Preview(PreviewGuideDraftRequest request, CancellationToken ct) =>
        Ok(ApiResponse<GuideDraftPreviewDto>.Ok(await service.PreviewAsync(GetCurrentUserId(), request, ct)));

    [HttpPost("draft/publish")]
    public async Task<ActionResult<ApiResponse<GuidePublishResultDto>>> Publish(PublishGuideDraftRequest request, CancellationToken ct) =>
        Ok(ApiResponse<GuidePublishResultDto>.Ok(await service.PublishAsync(GetCurrentUserId(), request, ct)));
}
