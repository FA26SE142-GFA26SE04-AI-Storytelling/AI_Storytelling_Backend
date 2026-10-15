using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;

namespace StoryPlatform.Api.Controllers;

[Authorize]
[Route("api/v1/creator/stories")]
public sealed class CreatorAuthoringController(ICreatorAuthoringService service) : BaseApiController
{
    [HttpPost("manual")]
    public async Task<ActionResult<ApiResponse<CreatorDraftDto>>> Create(CreateCreatorDraftRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<CreatorDraftDto>.Ok(await service.CreateDraftAsync(GetCurrentUserId(), request, ct)));

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CreatorDraftDto>>>> Mine(
        CancellationToken ct, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20) =>
        Ok(ApiResponse<PagedResult<CreatorDraftDto>>.Ok(await service.GetMineAsync(GetCurrentUserId(), pageIndex, pageSize, ct)));

    [HttpGet("{storyId:int}")]
    public async Task<ActionResult<ApiResponse<CreatorDraftDto>>> Get(int storyId, CancellationToken ct) =>
        Ok(ApiResponse<CreatorDraftDto>.Ok(await service.GetDraftAsync(GetCurrentUserId(), storyId, ct)));

    [HttpPost("{storyId:int}/manual/versions")]
    public async Task<ActionResult<ApiResponse<CreatorContentSavedDto>>> Save(int storyId, SaveCreatorContentRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<CreatorContentSavedDto>.Ok(await service.SaveContentAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpGet("{storyId:int}/content/versions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CreatorVersionDto>>>> Versions(int storyId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<CreatorVersionDto>>.Ok(await service.GetVersionsAsync(GetCurrentUserId(), storyId, ct)));

    [HttpGet("{storyId:int}/content/versions/{versionId:int}")]
    public async Task<ActionResult<ApiResponse<CreatorVersionDto>>> Version(int storyId, int versionId, CancellationToken ct) =>
        Ok(ApiResponse<CreatorVersionDto>.Ok(await service.GetVersionAsync(GetCurrentUserId(), storyId, versionId, ct)));

    [HttpPost("{storyId:int}/content/restore")]
    public async Task<ActionResult<ApiResponse<CreatorContentSavedDto>>> Restore(int storyId, RestoreCreatorVersionRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<CreatorContentSavedDto>.Ok(await service.RestoreVersionAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpPost("{storyId:int}/imports/preview")]
    public async Task<ActionResult<ApiResponse<CreatorImportPreviewDto>>> Preview(int storyId, PreviewCreatorImportRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<CreatorImportPreviewDto>.Ok(await service.PreviewImportAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpPost("{storyId:int}/imports/preview-file")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5_250_000)]
    public async Task<ActionResult<ApiResponse<CreatorImportPreviewDto>>> PreviewFile(
        int storyId, [FromForm] CreatorImportFileForm form, CancellationToken ct)
    {
        await using var stream = form.File.OpenReadStream();
        var result = await service.PreviewFileAsync(GetCurrentUserId(), storyId, form.RevisionToken,
            form.OperationId, form.File.FileName, stream, ct);
        return StatusCode(201, ApiResponse<CreatorImportPreviewDto>.Ok(result));
    }

    [HttpPost("{storyId:int}/imports/confirm")]
    public async Task<ActionResult<ApiResponse<CreatorContentSavedDto>>> Confirm(int storyId, ConfirmCreatorImportRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<CreatorContentSavedDto>.Ok(await service.ConfirmImportAsync(GetCurrentUserId(), storyId, request, ct)));
}

public sealed class CreatorImportFileForm
{
    [Required] public IFormFile File { get; set; } = null!;
    [Required] public string RevisionToken { get; set; } = string.Empty;
    [Required, StringLength(100)] public string OperationId { get; set; } = string.Empty;
}
