using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ParentAuthoring;

namespace StoryPlatform.Api.Controllers;

[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/manual-stories")]
public sealed class ChildManualStoriesController(ParentAuthoringService service) : ControllerBase
{
    private int ChildId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException();

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ParentFamilyConfirmationDto>>>> Library(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ParentFamilyConfirmationDto>>.Ok(await service.ChildLibraryAsync(ChildId, ct)));

    [HttpGet("{confirmationId:int}/package")]
    public async Task<ActionResult<ApiResponse<ParentPreparationDto>>> Package(int confirmationId, CancellationToken ct) =>
        Ok(ApiResponse<ParentPreparationDto>.Ok(await service.ReadChildPackageAsync(ChildId, confirmationId, ct)));
}
