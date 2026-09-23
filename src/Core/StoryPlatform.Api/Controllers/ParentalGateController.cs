using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.ParentalGate.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ParentalGate.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Requires a supervisor to re-enter a real email and password before the UI leaves a child
/// session for a guardian or administrator area. Verification never creates a new login session.
/// </summary>
public class ParentalGateController : BaseApiController
{
    private readonly IParentalGateService _parentalGateService;

    public ParentalGateController(IParentalGateService parentalGateService)
    {
        _parentalGateService = parentalGateService;
    }

    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> Verify(
        [FromBody] VerifyParentalGateRequestDto request,
        CancellationToken cancellationToken)
    {
        await _parentalGateService.VerifyAsync(request, cancellationToken);
        return HandleResult<object?>(null, "Vượt qua Parental Gate thành công.");
    }
}
