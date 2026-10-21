using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Features.Moderation.Rubrics;

namespace StoryPlatform.Api.Controllers;

[Authorize(Roles = "Moderator,Administrator")]
[Route("api/v1/moderation/rubrics")]
public sealed class ModerationRubricsController(ModerationRubricService service) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct, int pageIndex = 1, int pageSize = 20) =>
        HandleResult(await service.ListAsync(GetCurrentUserId(), status, pageIndex, pageSize, ct)).Result!;
    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct) => HandleResult(await service.ActiveAsync(GetCurrentUserId(), ct)).Result!;
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => HandleResult(await service.GetAsync(GetCurrentUserId(), id, ct)).Result!;
    [HttpPost]
    public async Task<IActionResult> Create(RubricWriteRequest request, CancellationToken ct) => HandleResult(await service.CreateAsync(GetCurrentUserId(), request, ct)).Result!;
    [HttpPost("{id:int}/clone")]
    public async Task<IActionResult> Clone(int id, RubricCloneRequest request, CancellationToken ct) => HandleResult(await service.CloneAsync(GetCurrentUserId(), id, request, ct)).Result!;
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, RubricWriteRequest request, CancellationToken ct) => HandleResult(await service.UpdateAsync(GetCurrentUserId(), id, request, ct)).Result!;
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] int revision, CancellationToken ct) => HandleResult(await service.DeleteAsync(GetCurrentUserId(), id, revision, ct)).Result!;
    [HttpPost("{id:int}/validate")]
    public async Task<IActionResult> Validate(int id, RubricRevisionRequest request, CancellationToken ct) => HandleResult(await service.ValidateAsync(GetCurrentUserId(), id, request.Revision, ct)).Result!;
    [HttpPost("{id:int}/evaluations")]
    public async Task<IActionResult> Evaluate(int id, RubricEvaluationRequest request, CancellationToken ct) => HandleResult(await service.EvaluateAsync(GetCurrentUserId(), id, request, ct)).Result!;
    [Authorize(Roles = "Administrator")]
    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id, RubricRevisionRequest request, CancellationToken ct) => HandleResult(await service.ActivateAsync(GetCurrentUserId(), id, request.Revision, ct)).Result!;
    [HttpGet("{id:int}/audit-logs")]
    public async Task<IActionResult> Logs(int id, CancellationToken ct, int pageIndex = 1, int pageSize = 20) => HandleResult(await service.AuditLogsAsync(GetCurrentUserId(), id, pageIndex, pageSize, ct)).Result!;
}
