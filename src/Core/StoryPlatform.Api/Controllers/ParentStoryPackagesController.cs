using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ParentAuthoring;

namespace StoryPlatform.Api.Controllers;

[Authorize(Roles = "User")]
[Route("api/v1/parent/stories/{storyId:int}")]
public sealed class ParentStoryPackagesController(ParentStoryPackageService service) : BaseApiController
{
    [HttpPost("content/finalize")]
    public async Task<ActionResult<ApiResponse<ParentContentFinalizationDto>>> FinalizeContent(int storyId, ParentPackageFinalizeRequest request, CancellationToken ct) =>
        Ok(ApiResponse<ParentContentFinalizationDto>.Ok(await service.FinalizeAsync(GetCurrentUserId(), storyId, request, ct)));
    [HttpPost("content/reopen")]
    public async Task<ActionResult<ApiResponse<ParentContentFinalizationDto>>> Reopen(int storyId, ParentPackageFinalizeRequest request, CancellationToken ct) =>
        Ok(ApiResponse<ParentContentFinalizationDto>.Ok(await service.ReopenAsync(GetCurrentUserId(), storyId, request, ct)));
    [HttpPost("packages")]
    [ProducesResponseType(typeof(ApiResponse<ParentPackageDto[]>), 202)]
    public async Task<ActionResult<ApiResponse<ParentPackageDto[]>>> Create(int storyId, CreateParentPackagesRequest request, CancellationToken ct) =>
        Accepted(ApiResponse<ParentPackageDto[]>.Ok(await service.CreateAsync(GetCurrentUserId(), storyId, request, ct)));
    [HttpGet("packages")]
    public async Task<ActionResult<ApiResponse<ParentPackageDto[]>>> List(int storyId, CancellationToken ct) =>
        Ok(ApiResponse<ParentPackageDto[]>.Ok(await service.ListAsync(GetCurrentUserId(), storyId, ct)));
    [HttpGet("packages/{packageId:int}")]
    public async Task<ActionResult<ApiResponse<ParentPackageDto>>> Get(int storyId, int packageId, CancellationToken ct) =>
        Ok(ApiResponse<ParentPackageDto>.Ok(await service.GetAsync(GetCurrentUserId(), storyId, packageId, ct)));
    [HttpPost("packages/{packageId:int}/generate")]
    [ProducesResponseType(typeof(ApiResponse<ParentPackageDto>), 202)]
    public async Task<ActionResult<ApiResponse<ParentPackageDto>>> Generate(int storyId, int packageId, ParentPackageCommand request, CancellationToken ct) =>
        Accepted(ApiResponse<ParentPackageDto>.Ok(await service.GenerateAsync(GetCurrentUserId(), storyId, packageId, request, ct)));
    [HttpPost("packages/{packageId:int}/retry")]
    public async Task<ActionResult<ApiResponse<ParentPackageDto>>> Retry(int storyId, int packageId, ParentPackageRetryRequest request, CancellationToken ct) =>
        Accepted(ApiResponse<ParentPackageDto>.Ok(await service.RetryAsync(GetCurrentUserId(), storyId, packageId, request, ct)));
    [HttpPost("packages/{packageId:int}/revise")]
    public async Task<ActionResult<ApiResponse<ParentPackageDto>>> Revise(int storyId, int packageId, ParentPackageReviseRequest request, CancellationToken ct) =>
        Accepted(ApiResponse<ParentPackageDto>.Ok(await service.ReviseAsync(GetCurrentUserId(), storyId, packageId, request, ct)));
    [HttpGet("packages/{packageId:int}/preview")]
    public async Task<ActionResult<ApiResponse<ParentPackagePreviewDto>>> Preview(int storyId, int packageId, CancellationToken ct) =>
        Ok(ApiResponse<ParentPackagePreviewDto>.Ok(await service.PreviewAsync(GetCurrentUserId(), storyId, packageId, ct)));
    [HttpPost("packages/{packageId:int}/confirm")]
    public async Task<ActionResult<ApiResponse<ParentPackageDto>>> Confirm(int storyId, int packageId, ParentPackageCommand request, CancellationToken ct) =>
        Ok(ApiResponse<ParentPackageDto>.Ok(await service.ConfirmAsync(GetCurrentUserId(), storyId, packageId, request, ct)));
    [HttpPost("family-confirmations/batch")]
    public async Task<ActionResult<ApiResponse<ParentFamilyConfirmationDto[]>>> Deliver(int storyId, ParentPackageBatchGrantRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<ParentFamilyConfirmationDto[]>.Ok(await service.DeliverAsync(GetCurrentUserId(), storyId, request, ct)));
}
