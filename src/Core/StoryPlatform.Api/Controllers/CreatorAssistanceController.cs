using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;

namespace StoryPlatform.Api.Controllers;

[Authorize]
[Route("api/v1/creator/stories/{storyId:int}/assistance-tasks")]
[RequestSizeLimit(1_000_000)]
[ProducesResponseType(typeof(ApiResponse<object>), 400)]
[ProducesResponseType(typeof(ApiResponse<object>), 401)]
[ProducesResponseType(typeof(ApiResponse<object>), 403)]
[ProducesResponseType(typeof(ApiResponse<object>), 404)]
[ProducesResponseType(typeof(ApiResponse<object>), 409)]
[ProducesResponseType(typeof(ApiResponse<object>), 429)]
[ProducesResponseType(typeof(ApiResponse<object>), 502)]
[ProducesResponseType(typeof(ApiResponse<object>), 504)]
public sealed class CreatorAssistanceController(ICreatorAssistanceService service) : BaseApiController
{
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AssistanceTaskDto>), 201)]
    public async Task<ActionResult<ApiResponse<AssistanceTaskDto>>> Create(int storyId, CreateAssistanceTaskRequest request, CancellationToken ct) =>
        StatusCode(201, ApiResponse<AssistanceTaskDto>.Ok(await service.CreateAsync(GetCurrentUserId(), storyId, request, ct)));
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AssistanceTaskSummary>>>> List(int storyId, CancellationToken ct, int pageIndex = 1, int pageSize = 20) =>
        Ok(ApiResponse<PagedResult<AssistanceTaskSummary>>.Ok(await service.ListAsync(GetCurrentUserId(), storyId, pageIndex, pageSize, ct)));
    [HttpGet("{taskId:int}")]
    public async Task<ActionResult<ApiResponse<AssistanceTaskDto>>> Get(int storyId, int taskId, CancellationToken ct) =>
        Ok(ApiResponse<AssistanceTaskDto>.Ok(await service.GetAsync(GetCurrentUserId(), storyId, taskId, ct)));
    [HttpGet("{taskId:int}/steps")]
    public async Task<ActionResult<ApiResponse<PagedResult<AssistanceStepDto>>>> Steps(int storyId, int taskId, CancellationToken ct, int pageIndex = 1, int pageSize = 20) =>
        Ok(ApiResponse<PagedResult<AssistanceStepDto>>.Ok(await service.StepsAsync(GetCurrentUserId(), storyId, taskId, pageIndex, pageSize, ct)));
    [HttpPost("{taskId:int}/suggestions")]
    [ProducesResponseType(typeof(ApiResponse<AssistanceCommandResult>), 200)]
    [ProducesResponseType(typeof(ApiResponse<AssistanceCommandResult>), 202)]
    public async Task<ActionResult<ApiResponse<AssistanceCommandResult>>> Suggestions(int storyId, int taskId, SuggestAssistanceRequest request, CancellationToken ct) =>
        CommandResult(await service.SuggestAsync(GetCurrentUserId(), storyId, taskId, request, ct));
    [HttpPost("{taskId:int}/clarification-answer")]
    public async Task<ActionResult<ApiResponse<AssistanceCommandResult>>> Answer(int storyId, int taskId, AnswerAssistanceRequest request, CancellationToken ct) =>
        CommandResult(await service.AnswerAsync(GetCurrentUserId(), storyId, taskId, request, ct));
    [HttpPost("{taskId:int}/direction")]
    public async Task<ActionResult<ApiResponse<AssistanceCommandResult>>> Direction(int storyId, int taskId, ConfirmAssistanceDirectionRequest request, CancellationToken ct) =>
        CommandResult(await service.DirectionAsync(GetCurrentUserId(), storyId, taskId, request, ct));
    [HttpPost("{taskId:int}/trials")]
    [ProducesResponseType(typeof(ApiResponse<AssistanceCommandResult>), 200)]
    [ProducesResponseType(typeof(ApiResponse<AssistanceCommandResult>), 202)]
    public async Task<ActionResult<ApiResponse<AssistanceCommandResult>>> Trial(int storyId, int taskId, CreateAssistanceTrialRequest request, CancellationToken ct) =>
        CommandResult(await service.TrialAsync(GetCurrentUserId(), storyId, taskId, request, ct));
    [HttpPost("{taskId:int}/cancel")]
    public async Task<ActionResult<ApiResponse<AssistanceCommandResult>>> Cancel(int storyId, int taskId, CancelAssistanceRequest request, CancellationToken ct) =>
        CommandResult(await service.CancelAsync(GetCurrentUserId(), storyId, taskId, request, ct));
    [HttpPost("{taskId:int}/close")]
    public async Task<ActionResult<ApiResponse<AssistanceCommandResult>>> Close(int storyId, int taskId, AssistanceCommand request, CancellationToken ct) =>
        CommandResult(await service.CloseAsync(GetCurrentUserId(), storyId, taskId, request, ct));
    private ActionResult<ApiResponse<AssistanceCommandResult>> CommandResult(AssistanceCommandResult result)
    {
        if (result.IsRunning) Response.Headers.Location = result.PollUrl;
        return StatusCode(result.IsRunning ? 202 : 200, ApiResponse<AssistanceCommandResult>.Ok(result));
    }
}
