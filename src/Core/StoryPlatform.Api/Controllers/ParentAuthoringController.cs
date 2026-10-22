using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.ParentAuthoring;

namespace StoryPlatform.Api.Controllers;

[Authorize(Roles = "Parent")]
[Route("api/v1/parent/stories")]
public sealed class ParentAuthoringController(ParentAuthoringService service) : BaseApiController
{
    [HttpPost("manual")]
    public async Task<ActionResult<ApiResponse<ParentDraftDto>>> Create(CreateParentDraftRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<ParentDraftDto>.Ok(await service.CreateParentDraftAsync(GetCurrentUserId(), request, ct)));

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CreatorDraftDto>>>> Mine(CancellationToken ct, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20) =>
        Ok(ApiResponse<PagedResult<CreatorDraftDto>>.Ok(await service.GetMineAsync(GetCurrentUserId(), pageIndex, pageSize, ct)));

    [HttpGet("{storyId:int}")]
    public async Task<ActionResult<ApiResponse<ParentDraftDto>>> Get(int storyId, CancellationToken ct) =>
        Ok(ApiResponse<ParentDraftDto>.Ok(await service.GetParentDraftAsync(GetCurrentUserId(), storyId, ct)));

    [HttpGet("{storyId:int}/workflow")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ParentOperationDto>>>> Workflow(int storyId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ParentOperationDto>>.Ok(await service.WorkflowAsync(GetCurrentUserId(), storyId, ct)));

    [HttpPatch("{storyId:int}/setup")]
    public async Task<ActionResult<ApiResponse<ParentDraftDto>>> Setup(int storyId, ParentSetupRequest request, CancellationToken ct) =>
        Ok(ApiResponse<ParentDraftDto>.Ok(await service.SetupAsync(GetCurrentUserId(), storyId, request, ct)));

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
    public async Task<ActionResult<ApiResponse<CreatorImportPreviewDto>>> PreviewImport(int storyId, PreviewCreatorImportRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CreatorImportPreviewDto>.Ok(await service.PreviewImportAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpPost("{storyId:int}/imports/preview-file")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5_250_000)]
    public async Task<ActionResult<ApiResponse<CreatorImportPreviewDto>>> PreviewFile(int storyId, [FromForm] CreatorImportFileForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Length == 0) return BadRequest(ApiResponse<CreatorImportPreviewDto>.Fail("FILE_REQUIRED"));
        await using var stream = form.File.OpenReadStream();
        return Ok(ApiResponse<CreatorImportPreviewDto>.Ok(await service.PreviewFileAsync(GetCurrentUserId(), storyId,
            form.RevisionToken, form.OperationId, form.File.FileName, stream, ct)));
    }

    [HttpPost("{storyId:int}/imports/confirm")]
    public async Task<ActionResult<ApiResponse<CreatorContentSavedDto>>> ConfirmImport(int storyId, ConfirmCreatorImportRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<CreatorContentSavedDto>.Ok(await service.ConfirmImportAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpPost("{storyId:int}/content/checks")]
    public async Task<ActionResult<ApiResponse<ParentCheckDto>>> Check(int storyId, ParentCheckRequest request, CancellationToken ct) =>
        Ok(ApiResponse<ParentCheckDto>.Ok(await service.CheckAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpPost("{storyId:int}/content/confirm")]
    public async Task<ActionResult<ApiResponse<ParentContentConfirmationDto>>> ConfirmContent(int storyId, ParentContentConfirmRequest request, CancellationToken ct) =>
        Ok(ApiResponse<ParentContentConfirmationDto>.Ok(await service.ConfirmContentAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpPost("{storyId:int}/preparations")]
    public async Task<ActionResult<ApiResponse<ParentPreparationDto>>> Prepare(int storyId, ParentPrepareRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<ParentPreparationDto>.Ok(await service.PrepareAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpPost("{storyId:int}/manual/media/generate")]
    [ProducesResponseType(typeof(ApiResponse<ParentMediaGenerationDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ParentMediaGenerationDto>>> GenerateMedia(
        int storyId, ParentMediaGenerateRequest request, CancellationToken ct) =>
        Accepted(ApiResponse<ParentMediaGenerationDto>.Ok(await service.GenerateMediaAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpGet("{storyId:int}/preparations/{preparationId:int}")]
    public async Task<ActionResult<ApiResponse<ParentPreparationDto>>> Preview(int storyId, int preparationId, CancellationToken ct) =>
        Ok(ApiResponse<ParentPreparationDto>.Ok(await service.PreviewAsync(GetCurrentUserId(), storyId, preparationId, ct)));

    [HttpPost("{storyId:int}/family-confirmations")]
    public async Task<ActionResult<ApiResponse<ParentFamilyConfirmationDto>>> ConfirmFamily(int storyId, ParentFamilyConfirmRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<ParentFamilyConfirmationDto>.Ok(await service.ConfirmFamilyAsync(GetCurrentUserId(), storyId, request, ct)));

    [HttpPost("{storyId:int}/family-confirmations/revoke")]
    public async Task<ActionResult<ApiResponse<bool>>> RevokeFamily(int storyId, ParentFamilyRevokeRequest request, CancellationToken ct) =>
        Ok(ApiResponse<bool>.Ok(await service.RevokeFamilyAsync(GetCurrentUserId(), storyId, request, ct)));
}
