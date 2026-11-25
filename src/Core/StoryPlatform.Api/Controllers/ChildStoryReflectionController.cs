using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.ChildProfiles.Safety;

namespace StoryPlatform.Api.Controllers;

/// <summary>Cảm nhận sau khi đọc, phía trẻ: cảm xúc, cảnh thích nhất và lời bé về cảnh đó (viết hoặc kể).</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[RequireChildFeature(ChildFeatures.Reflection)]
[Route("api/v1/child/reading/sessions/{sessionId:int}/reflection")]
public sealed class ChildStoryReflectionController : ControllerBase
{
    private const int MaxRequestBytes = StorySummaryPolicy.MaxAudioBytes + 100_000;
    private readonly IChildStoryReflectionService _reflections;

    public ChildStoryReflectionController(IChildStoryReflectionService reflections)
    {
        _reflections = reflections;
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

    /// <summary>Cảm nhận bé đã chọn trong phiên này và bé có được ghi âm không (canRecord).</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<ChildStoryReflectionDto>>> Get(int sessionId, CancellationToken ct)
    {
        var result = await _reflections.GetAsync(ChildId, sessionId, ct);
        return Ok(ApiResponse<ChildStoryReflectionDto>.Ok(result));
    }

    /// <summary>Bé chọn 1–2 cảm xúc sau khi đọc xong; chọn lại thì thay toàn bộ.</summary>
    [HttpPut("emotions")]
    public async Task<ActionResult<ApiResponse<ChildStoryReflectionDto>>> SetEmotions(
        int sessionId, [FromBody] SetStoryEmotionsRequestDto request, CancellationToken ct)
    {
        var result = await _reflections.SetEmotionsAsync(ChildId, sessionId, request, ct);
        return Ok(ApiResponse<ChildStoryReflectionDto>.Ok(result));
    }

    /// <summary>Bé chọn cảnh thích nhất; đổi cảnh thì lời về cảnh cũ bị bỏ.</summary>
    [HttpPut("favorite-scene")]
    public async Task<ActionResult<ApiResponse<ChildStoryReflectionDto>>> SetFavoriteScene(
        int sessionId, [FromBody] SetFavoriteSceneRequestDto request, CancellationToken ct)
    {
        var result = await _reflections.SetFavoriteSceneAsync(ChildId, sessionId, request, ct);
        return Ok(ApiResponse<ChildStoryReflectionDto>.Ok(result));
    }

    /// <summary>Bé viết (≤ 200 ký tự) và/hoặc kể (≤ 3 MB, cần bố mẹ bật giọng nói) điều bé thích ở cảnh đã chọn.</summary>
    [HttpPost("note")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MemoryBufferThreshold = MaxRequestBytes, MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<ApiResponse<ChildStoryReflectionDto>>> SendNote(
        int sessionId, [FromForm] StoryReflectionNoteForm form, CancellationToken ct)
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

        var result = await _reflections.SendNoteAsync(ChildId, sessionId,
            new StoryReflectionNoteRequest { Text = form.Text, Audio = audio, ContentType = form.Audio?.ContentType }, ct);
        return Ok(ApiResponse<ChildStoryReflectionDto>.Ok(result, result.Message ?? string.Empty));
    }
}

public sealed class StoryReflectionNoteForm
{
    /// <summary>Chữ bé viết, tối đa 200 ký tự (không bắt buộc nếu có ghi âm).</summary>
    public string? Text { get; set; }
    /// <summary>Ghi âm (webm/ogg/wav/mp3/m4a/flac), tối đa 3 MB (không bắt buộc nếu có chữ).</summary>
    public IFormFile? Audio { get; set; }
}
