using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Làm quiz sau khi đọc, phía trẻ (Luồng 3 — mục 8).</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/quiz")]
public sealed class ChildQuizController : ControllerBase
{
    private readonly IChildQuizService _quiz;

    public ChildQuizController(IChildQuizService quiz)
    {
        _quiz = quiz;
    }

    private int ChildId => int.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException();

    /// <summary>Câu hỏi của truyện vừa đọc xong. Không bao giờ kèm đáp án.</summary>
    [HttpGet("sessions/{sessionId:int}")]
    public async Task<ActionResult<ApiResponse<QuizDto>>> GetQuiz(int sessionId, CancellationToken ct) =>
        Ok(ApiResponse<QuizDto>.Ok(await _quiz.GetQuizAsync(ChildId, sessionId, ct)));

    /// <summary>Nộp một câu. Bỏ qua = không gọi API này cho câu đó.</summary>
    [HttpPost("sessions/{sessionId:int}/answers")]
    public async Task<ActionResult<ApiResponse<QuizAnswerResultDto>>> SubmitAnswer(
        int sessionId, [FromBody] SubmitQuizAnswerRequestDto request, CancellationToken ct) =>
        Ok(ApiResponse<QuizAnswerResultDto>.Ok(await _quiz.SubmitAnswerAsync(ChildId, sessionId, request, ct)));

    [HttpGet("sessions/{sessionId:int}/result")]
    public async Task<ActionResult<ApiResponse<QuizResultDto>>> GetResult(int sessionId, CancellationToken ct) =>
        Ok(ApiResponse<QuizResultDto>.Ok(await _quiz.GetResultAsync(ChildId, sessionId, ct)));
}
