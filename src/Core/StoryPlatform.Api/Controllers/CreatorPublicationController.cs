using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
namespace StoryPlatform.Api.Controllers;
[Authorize]
public sealed class CreatorPublicationController(ICreatorPublicationService service) : BaseApiController
{
    [Authorize(Roles = "ContentCreator")]
    [HttpGet("/api/v1/creator/stories/{storyId:int}/publication")]
    public async Task<ActionResult<ApiResponse<CreatorPublicationStatus>>> GetStatus(int storyId, CancellationToken ct) =>
        HandleResult(await service.GetStatusAsync(GetCurrentUserId(), storyId, ct));
    [Authorize(Roles = "ContentCreator")]
    [HttpPost("/api/v1/creator/stories/{storyId:int}/removal-requests")]
    public async Task<ActionResult<ApiResponse<CreatorPublicationResult>>> RequestRemoval(int storyId, [FromBody] CreatorRemovalRequest input, CancellationToken ct) =>
        HandleResult(await service.RequestRemovalAsync(GetCurrentUserId(), storyId, input, ct));
    [Authorize(Roles = "ContentCreator")]
    [HttpPost("/api/v1/creator/stories/{storyId:int}/revisions")]
    public async Task<ActionResult<ApiResponse<CreatorPublicationResult>>> CreateRevision(int storyId, [FromBody] CreatorRevisionRequest input, CancellationToken ct) =>
        HandleResult(await service.CreateRevisionAsync(GetCurrentUserId(), storyId, input, ct));
    [Authorize(Roles = "Moderator,Administrator")]
    [HttpPost("/api/v1/moderation/public-story-removals/{itemId:int}/decision")]
    public async Task<ActionResult<ApiResponse<CreatorPublicationResult>>> DecideRemoval(int itemId, [FromBody] RemovalDecisionRequest input, CancellationToken ct) =>
        HandleResult(await service.DecideRemovalAsync(GetCurrentUserId(), itemId, input, ct));
}
