using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.ChildProfiles.Safety;

namespace StoryPlatform.Api.Controllers;

/// <summary>Đọc và nghe truyện, phía trẻ (Luồng 3 — WF-L3A, WF-L3B), gồm cả luyện đọc thành tiếng (mục 4).</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/reading")]
public sealed class ChildReadingController : ControllerBase
{
    private readonly IChildStoryBrowseService _browse;
    private readonly IChildReadingService _reading;
    private readonly IChildNotebookService _notebook;
    private readonly IChildReadAloudService _readAloud;
    private readonly IChildListeningService _listening;
    private readonly IChildFreeAnswerService _freeAnswer;

    public ChildReadingController(
        IChildStoryBrowseService browse, IChildReadingService reading, IChildNotebookService notebook,
        IChildReadAloudService readAloud, IChildListeningService listening, IChildFreeAnswerService freeAnswer)
    {
        _browse = browse;
        _reading = reading;
        _notebook = notebook;
        _readAloud = readAloud;
        _listening = listening;
        _freeAnswer = freeAnswer;
    }

    private int ChildId
    {
        get
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!int.TryParse(claim, out var id))
            {
                throw new UnauthorizedAccessException();
            }

            return id;
        }
    }

    private string SessionKey
    {
        get
        {
            var sessionKey = User.FindFirstValue(ChildSession.SessionClaimType);
            if (sessionKey == null)
            {
                throw new UnauthorizedAccessException();
            }

            return sessionKey;
        }
    }

    [HttpGet("stories")]
    public async Task<ActionResult<ApiResponse<ChildStoryPageDto>>> SearchStories(
        [FromQuery] ChildStoryQueryDto query, CancellationToken ct)
    {
        var result = await _browse.SearchAsync(ChildId, query, ct);
        return Ok(ApiResponse<ChildStoryPageDto>.Ok(result));
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<ApiResponse<ReadingSessionStartedDto>>> Start(
        [FromBody] StartReadingRequestDto request, CancellationToken ct)
    {
        var result = await _reading.StartAsync(ChildId, SessionKey, request, ct);
        return Ok(ApiResponse<ReadingSessionStartedDto>.Ok(result));
    }

    [HttpGet("sessions/{sessionId:int}/content")]
    public async Task<ActionResult<ApiResponse<ReadingContentDto>>> GetContent(int sessionId, CancellationToken ct)
    {
        var result = await _reading.GetContentAsync(ChildId, sessionId, ct);
        return Ok(ApiResponse<ReadingContentDto>.Ok(result));
    }

    [HttpPut("sessions/{sessionId:int}/progress")]
    public async Task<ActionResult<ApiResponse<ReadingProgressDto>>> SaveProgress(
        int sessionId, [FromBody] SaveProgressRequestDto request, CancellationToken ct)
    {
        var result = await _reading.SaveProgressAsync(ChildId, sessionId, request, ct);
        return Ok(ApiResponse<ReadingProgressDto>.Ok(result));
    }

    [HttpPost("sessions/{sessionId:int}/complete")]
    public async Task<ActionResult<ApiResponse<ReadingCompletedDto>>> Complete(int sessionId, CancellationToken ct)
    {
        var result = await _reading.CompleteAsync(ChildId, sessionId, ct);
        return Ok(ApiResponse<ReadingCompletedDto>.Ok(result));
    }

    [HttpPut("sessions/{sessionId:int}/bookmark")]
    public async Task<ActionResult<ApiResponse<ReadingFlagsDto>>> SetBookmark(
        int sessionId, [FromBody] SetFlagRequestDto request, CancellationToken ct)
    {
        var result = await _reading.SetBookmarkAsync(ChildId, sessionId, request.Value, ct);
        return Ok(ApiResponse<ReadingFlagsDto>.Ok(result));
    }

    [HttpPut("sessions/{sessionId:int}/favorite")]
    public async Task<ActionResult<ApiResponse<ReadingFlagsDto>>> SetFavorite(
        int sessionId, [FromBody] SetFlagRequestDto request, CancellationToken ct)
    {
        var result = await _reading.SetFavoriteAsync(ChildId, sessionId, request.Value, ct);
        return Ok(ApiResponse<ReadingFlagsDto>.Ok(result));
    }

    /// <summary>"Chỉ nghe truyện": bật thì app không dừng để hỏi tương tác lúc nghe. Lưu theo truyện.</summary>
    [HttpPut("sessions/{sessionId:int}/listen-only")]
    public async Task<ActionResult<ApiResponse<ReadingFlagsDto>>> SetListenOnly(
        int sessionId, [FromBody] SetFlagRequestDto request, CancellationToken ct)
    {
        var result = await _reading.SetListenOnlyAsync(ChildId, sessionId, request.Value, ct);
        return Ok(ApiResponse<ReadingFlagsDto>.Ok(result));
    }

    /// <summary>Trẻ trả lời một tương tác lúc nghe. Bỏ qua thì không gọi. Không cộng hay trừ EXP.</summary>
    [HttpPost("sessions/{sessionId:int}/interactions/{interactionId:int}/attempts")]
    public async Task<ActionResult<ApiResponse<ListeningAttemptResultDto>>> SubmitListeningAttempt(
        int sessionId, int interactionId, [FromBody] SubmitListeningAttemptRequestDto request, CancellationToken ct)
    {
        var result = await _listening.SubmitAttemptAsync(ChildId, sessionId, interactionId, request, ct);
        return Ok(ApiResponse<ListeningAttemptResultDto>.Ok(result));
    }

    /// <summary>Ghi bước đọc âm/vẽ của tương tác chữ cái.</summary>
    [HttpPatch("sessions/{sessionId:int}/listening/{interactionId:int}/steps")]
    public async Task<IActionResult> SaveListeningSteps(int sessionId, int interactionId,
        [FromBody] ListeningStepsRequestDto request, CancellationToken ct)
    {
        await _listening.SaveStepsAsync(ChildId, sessionId, interactionId, request, ct);
        return Ok(ApiResponse<object>.Ok(new { }));
    }

    /// <summary>Trẻ chọn cảm nhận sau khi đọc xong. Chỉ ghi lại, không có phản hồi hay thưởng.</summary>
    [HttpPut("sessions/{sessionId:int}/feeling")]
    public async Task<ActionResult<ApiResponse<ReadingFeelingDto>>> SetFeeling(
        int sessionId, [FromBody] SetFeelingRequestDto request, CancellationToken ct)
    {
        var result = await _reading.SetFeelingAsync(ChildId, sessionId, request, ct);
        return Ok(ApiResponse<ReadingFeelingDto>.Ok(result));
    }

    /// <summary>App của trẻ gọi mỗi ≤10 giây để biết phiên có bị ngắt không (thông báo trong vòng 15 giây).</summary>
    [HttpGet("sessions/{sessionId:int}/signal")]
    public async Task<ActionResult<ApiResponse<ForceExitSignalDto>>> GetSignal(int sessionId, CancellationToken ct)
    {
        var result = await _reading.GetSignalAsync(ChildId, sessionId, ct);
        return Ok(ApiResponse<ForceExitSignalDto>.Ok(result));
    }

    [HttpPost("sessions/{sessionId:int}/events")]
    public async Task<ActionResult<ApiResponse<object?>>> LogEvent(
        int sessionId, [FromBody] ReadingEventRequestDto request, CancellationToken ct)
    {
        await _reading.LogEventAsync(ChildId, sessionId, request, ct);
        return Ok(ApiResponse<object?>.Ok(null, "Đã ghi nhận."));
    }

    [HttpGet("sessions/{sessionId:int}/vocabulary/{vocabularyId:int}")]
    public async Task<ActionResult<ApiResponse<VocabularyDefinitionDto>>> GetDefinition(
        int sessionId, int vocabularyId, CancellationToken ct)
    {
        var result = await _notebook.GetDefinitionAsync(ChildId, sessionId, vocabularyId, ct);
        return Ok(ApiResponse<VocabularyDefinitionDto>.Ok(result));
    }

    /// <summary>Trẻ đọc to một đoạn; trả từng chữ good / retry / skipped kèm lời khen. Giữ bản ghi lần gần nhất cho chủ hồ sơ nghe.</summary>
    [HttpPost("sessions/{sessionId:int}/segments/{segmentId:int}/read-aloud")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ReadAloudPolicy.MaxAudioBytes + 100_000)]
    [RequestFormLimits(MemoryBufferThreshold = ReadAloudPolicy.MaxAudioBytes + 100_000,
        MultipartBodyLengthLimit = ReadAloudPolicy.MaxAudioBytes + 100_000)]
    [RequireChildFeature(ChildFeatures.Voice)]
    public async Task<ActionResult<ApiResponse<ReadAloudResultDto>>> ReadAloud(
        int sessionId, int segmentId, [FromForm] ReadAloudForm form, CancellationToken ct)
    {
        var result = await _readAloud.CheckAsync(ChildId, sessionId, segmentId, await ReadAudioAsync(form, ct), ct);
        return Ok(ApiResponse<ReadAloudResultDto>.Ok(result));
    }

    /// <summary>Trẻ đọc theo đoạn luyện đọc to (readAloudPractice của nội dung truyện); chấm như đọc to theo đoạn.</summary>
    [HttpPost("sessions/{sessionId:int}/read-aloud-practice/{interactionId:int}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ReadAloudPolicy.MaxAudioBytes + 100_000)]
    [RequestFormLimits(MemoryBufferThreshold = ReadAloudPolicy.MaxAudioBytes + 100_000,
        MultipartBodyLengthLimit = ReadAloudPolicy.MaxAudioBytes + 100_000)]
    [RequireChildFeature(ChildFeatures.Voice)]
    public async Task<ActionResult<ApiResponse<ReadAloudResultDto>>> ReadAloudPractice(
        int sessionId, int interactionId, [FromForm] ReadAloudForm form, CancellationToken ct)
    {
        var result = await _readAloud.CheckPracticeAsync(ChildId, sessionId, interactionId, await ReadAudioAsync(form, ct), ct);
        return Ok(ApiResponse<ReadAloudResultDto>.Ok(result));
    }

    [HttpPost("sessions/{sessionId:int}/free-answer/{interactionId:int}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ReadAloudPolicy.MaxAudioBytes + 100_000)]
    [RequestFormLimits(MemoryBufferThreshold = ReadAloudPolicy.MaxAudioBytes + 100_000,
        MultipartBodyLengthLimit = ReadAloudPolicy.MaxAudioBytes + 100_000)]
    [RequireChildFeature(ChildFeatures.Voice)]
    public async Task<ActionResult<ApiResponse<object>>> SaveFreeAnswer(
        int sessionId, int interactionId, [FromForm] ReadAloudForm form, CancellationToken ct)
    {
        var saved = await _freeAnswer.SaveAsync(ChildId, sessionId, interactionId, await ReadAudioAsync(form, ct), ct);
        return Ok(ApiResponse<object>.Ok(new { saved }));
    }

    // Chặn file quá lớn trước khi đọc vào bộ nhớ để chấm và lưu bản ghi.
    private static async Task<ReadAloudRequest> ReadAudioAsync(ReadAloudForm form, CancellationToken ct)
    {
        if (form.Audio is { Length: > ReadAloudPolicy.MaxAudioBytes })
        {
            throw new BadRequestException(ReadAloudPolicy.AudioTooLargeMessage);
        }

        byte[] audio = [];
        if (form.Audio is { Length: > 0 })
        {
            using var buffer = new MemoryStream();
            await form.Audio.CopyToAsync(buffer, ct);
            audio = buffer.ToArray();
        }

        return new ReadAloudRequest { Audio = audio, ContentType = form.Audio?.ContentType, LineId = form.LineId };
    }
}

public sealed class ReadAloudForm
{
    /// <summary>Đoạn ghi âm (webm/ogg/wav/mp3/m4a/flac), tối đa 1 MB.</summary>
    public IFormFile? Audio { get; set; }
    /// <summary>Đọc luân phiên: id lượt thoại bé đang đọc (lời của nhân vật bé đã chọn).</summary>
    public int? LineId { get; set; }
}
