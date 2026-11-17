using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Tóm tắt cuối truyện trẻ gửi bố mẹ: ghi âm kể lại hoặc hỏi về tranh/cảnh còn thắc mắc.</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/reading/sessions/{sessionId:int}/summary")]
public sealed class ChildStorySummaryController : ControllerBase
{
    private const int MaxRequestBytes = StorySummaryPolicy.MaxAudioBytes + 100_000;
    private readonly IChildStorySummaryService _summaries;

    public ChildStorySummaryController(IChildStorySummaryService summaries)
    {
        _summaries = summaries;
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

    /// <summary>Trẻ gửi bản ghi âm kể lại truyện cho bố mẹ. Cần bố mẹ bật giọng nói; mỗi phiên gửi một lần.</summary>
    [HttpPost("recording")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MemoryBufferThreshold = MaxRequestBytes, MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<ApiResponse<StorySummarySentDto>>> SendRecording(
        int sessionId, [FromForm] StorySummaryRecordingForm form, CancellationToken ct)
    {
        if (form.Audio is { Length: > StorySummaryPolicy.MaxAudioBytes })
        {
            throw new BadRequestException(StorySummaryPolicy.AudioTooLargeMessage);
        }

        byte[] audio = [];
        if (form.Audio is { Length: > 0 })
        {
            using var buffer = new MemoryStream();
            await form.Audio.CopyToAsync(buffer, ct);
            audio = buffer.ToArray();
        }

        var result = await _summaries.SendRecordingAsync(ChildId, sessionId,
            new StorySummaryRecordingRequest { Audio = audio, ContentType = form.Audio?.ContentType }, ct);
        return Ok(ApiResponse<StorySummarySentDto>.Ok(result, result.Message));
    }

    /// <summary>Trẻ gửi một câu hỏi cho bố mẹ về một tranh của cảnh (hoặc cả cảnh khi cảnh không có tranh),
    /// kèm ghi âm và/hoặc chữ (không bắt buộc). Tối đa 5 câu mỗi phiên; ghi âm cần bố mẹ bật giọng nói.</summary>
    [HttpPost("questions")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MemoryBufferThreshold = MaxRequestBytes, MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<ApiResponse<StorySummaryQuestionSentDto>>> Ask(
        int sessionId, [FromForm] StorySummaryQuestionForm form, CancellationToken ct)
    {
        if (form.Audio is { Length: > StorySummaryPolicy.MaxAudioBytes })
        {
            throw new BadRequestException(StorySummaryPolicy.AudioTooLargeMessage);
        }

        byte[] audio = [];
        if (form.Audio is { Length: > 0 })
        {
            using var buffer = new MemoryStream();
            await form.Audio.CopyToAsync(buffer, ct);
            audio = buffer.ToArray();
        }

        var result = await _summaries.AskAsync(ChildId, sessionId, new StorySummaryQuestionRequest
        {
            SceneId = form.SceneId, BeatOrder = form.BeatOrder, Text = form.Text,
            Audio = audio, ContentType = form.Audio?.ContentType
        }, ct);
        return Ok(ApiResponse<StorySummaryQuestionSentDto>.Ok(result, result.Message));
    }

}

public sealed class StorySummaryRecordingForm
{
    /// <summary>Bản ghi âm tóm tắt (webm/ogg/wav/mp3/m4a/flac), tối đa 3 MB.</summary>
    public IFormFile? Audio { get; set; }
}

public sealed class StorySummaryQuestionForm
{
    /// <summary>Cảnh bé thắc mắc (scenes[].sceneId của màn đọc).</summary>
    public int SceneId { get; set; }
    /// <summary>Tranh bé chọn (illustrations[].beatOrder); bỏ trống khi cảnh không có tranh.</summary>
    public int? BeatOrder { get; set; }
    /// <summary>Câu bé gõ, tối đa 200 ký tự (không bắt buộc).</summary>
    public string? Text { get; set; }
    /// <summary>Ghi âm câu hỏi (webm/ogg/wav/mp3/m4a/flac), tối đa 3 MB (không bắt buộc).</summary>
    public IFormFile? Audio { get; set; }
}
