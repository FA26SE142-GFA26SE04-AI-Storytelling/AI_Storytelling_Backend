using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Luồng 2 — Existing Story Workflow.
/// Cung cấp API cho Parent/Teacher:
///   - Import nội dung truyện thô.
///   - Evaluate để quyết định Suitable / Adapt / Blocked.
///   - Adapt / Manual Edit / Keep Original (đều tạo StoryVersion mới).
///   - Archive.
///
/// Sau khi có StoryVersion ổn định, controller gọi
/// <see cref="IStableVersionArtifactHandoffService.QueueArtifactsAsync"/> để
/// vào chuỗi Phase 3 (Vocabulary → Quiz → Discussion).
/// </summary>
[ApiController]
[Authorize(Roles = "Parent,Teacher")]
[Route("api/v1/stories")]
public sealed class ExistingStoriesController : BaseApiController
{
    private readonly IExistingStoryService _existingStory;
    private readonly IExistingStoryEvaluationService _evaluation;
    private readonly IStableVersionArtifactHandoffService _handoff;

    public ExistingStoriesController(
        IExistingStoryService existingStory,
        IExistingStoryEvaluationService evaluation,
        IStableVersionArtifactHandoffService handoff)
    {
        _existingStory = existingStory;
        _evaluation = evaluation;
        _handoff = handoff;
    }

    #region Intake

    [HttpPost("import")]
    public async Task<ActionResult<ApiResponse<ImportStoryResponseDto>>> Import(
        [FromBody] ImportStoryRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _existingStory.ImportAsync(GetCurrentUserId(), request, cancellationToken);
        return Ok(ApiResponse<ImportStoryResponseDto>.Ok(result, "Import truyện thành công."));
    }

    [HttpPost("import-file")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5_250_000)]
    public async Task<ActionResult<ApiResponse<ImportStoryResponseDto>>> ImportFile(
        [FromForm] ImportExistingStoryFileForm form,
        CancellationToken cancellationToken)
    {
        if (form.File is null || form.File.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("File upload không được để trống."));
        if (form.File.Length > 5_000_000)
            return BadRequest(ApiResponse<object>.Fail("File upload vượt quá giới hạn 5 MB."));

        await using var stream = form.File.OpenReadStream();
        var result = await _existingStory.ImportDocumentAsync(GetCurrentUserId(), new ImportStoryDocumentRequestDto
        {
            Content = stream,
            FileName = form.File.FileName,
            ContentType = form.File.ContentType,
            Title = form.Title,
            ChildProfileId = form.ChildProfileId,
            Language = form.Language,
            IdempotencyKey = form.IdempotencyKey
        }, cancellationToken);
        return Ok(ApiResponse<ImportStoryResponseDto>.Ok(result, "Import file truyện thành công."));
    }

    #endregion

    #region Evaluation

    [HttpPost("{storyId:int}/existing/evaluate")]
    public async Task<ActionResult<ApiResponse<ExistingStoryEvaluationDto>>> Evaluate(
        int storyId,
        [FromBody] EvaluateExistingStoryRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _evaluation.EvaluateAsync(
            GetCurrentUserId(), storyId, request.StoryVersionId, cancellationToken);
        return Ok(ApiResponse<ExistingStoryEvaluationDto>.Ok(result, "Đánh giá truyện hoàn tất."));
    }

    [HttpGet("{storyId:int}/existing/evaluation")]
    public async Task<ActionResult<ApiResponse<ExistingStoryEvaluationDto>>> GetLatestEvaluation(
        int storyId,
        [FromQuery] int storyVersionId,
        CancellationToken cancellationToken)
    {
        var result = await _evaluation.GetLatestAsync(
            GetCurrentUserId(), storyId, storyVersionId, cancellationToken);
        return Ok(ApiResponse<ExistingStoryEvaluationDto>.Ok(result, "Lấy đánh giá mới nhất."));
    }

    #endregion

    #region Versioning

    [HttpPost("{storyId:int}/existing/adapt")]
    public async Task<ActionResult<ApiResponse<VersionMutationResponseDto>>> Adapt(
        int storyId,
        [FromBody] AdaptExistingStoryRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.StoryId != storyId)
            return BadRequest(ApiResponse<object>.Fail("StoryId không khớp."));
        var result = await _existingStory.AdaptAsync(GetCurrentUserId(), request, cancellationToken);

        // Sau Adapt, gọi evaluation + handoff (nếu Suitable) hoặc chờ supervisor quyết định.
        var evaluation = await _evaluation.EvaluateAsync(
            GetCurrentUserId(), storyId, result.StoryVersionId, cancellationToken);
        result.Decision = evaluation.DecisionText;
        if (evaluation.Decision == ExistingStoryDecision.Suitable)
        {
            await _handoff.QueueArtifactsAsync(
                storyId, result.StoryVersionId, GetCurrentUserId(), generationRequestId: null,
                cancellationToken: cancellationToken);
        }
        return Ok(ApiResponse<VersionMutationResponseDto>.Ok(result, "AI adapt thành công."));
    }

    [HttpPut("{storyId:int}/existing/content")]
    public async Task<ActionResult<ApiResponse<VersionMutationResponseDto>>> UpdateContent(
        int storyId,
        [FromBody] ManualEditRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.StoryId != storyId)
            return BadRequest(ApiResponse<object>.Fail("StoryId không khớp."));
        var result = await _existingStory.UpdateContentAsync(GetCurrentUserId(), request, cancellationToken);

        var evaluation = await _evaluation.EvaluateAsync(
            GetCurrentUserId(), storyId, result.StoryVersionId, cancellationToken);
        result.Decision = evaluation.DecisionText;
        if (evaluation.Decision == ExistingStoryDecision.Suitable)
        {
            await _handoff.QueueArtifactsAsync(
                storyId, result.StoryVersionId, GetCurrentUserId(), generationRequestId: null,
                cancellationToken: cancellationToken);
        }
        return Ok(ApiResponse<VersionMutationResponseDto>.Ok(result, "Cập nhật nội dung thành công."));
    }

    [HttpPost("{storyId:int}/existing/keep-original")]
    public async Task<ActionResult<ApiResponse<VersionMutationResponseDto>>> KeepOriginal(
        int storyId,
        [FromBody] KeepOriginalRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.StoryId != storyId)
            return BadRequest(ApiResponse<object>.Fail("StoryId không khớp."));
        var result = await _existingStory.KeepOriginalAsync(GetCurrentUserId(), request, cancellationToken);

        await _handoff.QueueArtifactsAsync(
            storyId, request.StoryVersionId, GetCurrentUserId(), generationRequestId: null,
            cancellationToken: cancellationToken);
        return Ok(ApiResponse<VersionMutationResponseDto>.Ok(result, "Giữ nguyên bản gốc."));
    }

    [HttpPost("{storyId:int}/existing/archive")]
    public async Task<ActionResult<ApiResponse<bool>>> Archive(
        int storyId,
        [FromBody] ArchiveExistingStoryRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.StoryId != storyId)
            return BadRequest(ApiResponse<object>.Fail("StoryId không khớp."));
        var result = await _existingStory.ArchiveAsync(GetCurrentUserId(), request, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(result, "Lưu trữ truyện."));
    }

    #endregion
}

public sealed class EvaluateExistingStoryRequestDto
{
    public int StoryVersionId { get; set; }
}

public sealed class ImportExistingStoryFileForm
{
    public IFormFile File { get; set; } = null!;
    public string? Title { get; set; }
    public int ChildProfileId { get; set; }
    public string Language { get; set; } = "vi";
    public string? IdempotencyKey { get; set; }
}
