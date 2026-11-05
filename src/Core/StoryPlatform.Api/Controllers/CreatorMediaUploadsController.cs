using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Api.Controllers;

[Authorize(Roles = "ContentCreator")]
[EnableRateLimiting(RateLimitPolicies.Ai)]
[Route("api/v1/creator/stories/{storyId:int}/media")]
public sealed class CreatorMediaUploadsController(IMediaGenerationService media) : BaseApiController
{
    [HttpPost("illustration-beats/{beatId:int}/upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024 + 65536)]
    [ProducesResponseType(typeof(ApiResponse<CreatorMediaCommandResult>), 202)]
    public Task<IActionResult> UploadImage(int storyId, int beatId, [FromForm] CreatorMediaUploadForm form, CancellationToken ct) => Upload(storyId, beatId, true, form, ct);

    [HttpPost("segments/{segmentId:int}/upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25 * 1024 * 1024 + 65536)]
    [ProducesResponseType(typeof(ApiResponse<CreatorMediaCommandResult>), 202)]
    public Task<IActionResult> UploadAudio(int storyId, int segmentId, [FromForm] CreatorMediaUploadForm form, CancellationToken ct) => Upload(storyId, segmentId, false, form, ct);

    [HttpPost("illustration-beats/{beatId:int}/generate")]
    [ProducesResponseType(typeof(ApiResponse<CreatorMediaCommandResult>), 202)]
    public async Task<IActionResult> GenerateImage(int storyId, int beatId, CreatorMediaCommand request, CancellationToken ct) =>
        Accepted(ApiResponse<CreatorMediaCommandResult>.Ok(await media.SelectCreatorMediaAsync(GetCurrentUserId(), storyId, beatId, true, request, null, null, ct)));

    [HttpPost("segments/{segmentId:int}/generate")]
    [ProducesResponseType(typeof(ApiResponse<CreatorMediaCommandResult>), 202)]
    public async Task<IActionResult> GenerateAudio(int storyId, int segmentId, CreatorMediaCommand request, CancellationToken ct) =>
        Accepted(ApiResponse<CreatorMediaCommandResult>.Ok(await media.SelectCreatorMediaAsync(GetCurrentUserId(), storyId, segmentId, false, request, null, null, ct)));

    private async Task<IActionResult> Upload(int story, int target, bool image, CreatorMediaUploadForm form, CancellationToken ct)
    {
        var limit = (image ? 10 : 25) * 1024 * 1024;
        if (form.File is null || form.File.Length <= 0 || form.File.Length > limit) throw new BadRequestException("MEDIA_FILE_SIZE_INVALID");
        await using var input = form.File.OpenReadStream();
        using var output = new MemoryStream();
        var buffer = new byte[81920]; int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        { if (output.Length + read > limit) throw new BadRequestException("MEDIA_FILE_SIZE_INVALID"); await output.WriteAsync(buffer.AsMemory(0, read), ct); }
        var command = new CreatorMediaCommand(form.OperationId, form.StoryVersionId, form.PackageRevision, form.ExpectedConfirmed);
        var result = await media.SelectCreatorMediaAsync(GetCurrentUserId(), story, target, image, command,
            new(command, form.SourceDeclaration, form.RightsDeclaration), new GeneratedMedia(output.ToArray(), form.File.ContentType), ct);
        return Accepted(ApiResponse<CreatorMediaCommandResult>.Ok(result));
    }
}

public sealed class CreatorMediaUploadForm
{
    [Required] public IFormFile File { get; set; } = null!;
    [Required, StringLength(100, MinimumLength = 1)] public string OperationId { get; set; } = "";
    [Range(1, int.MaxValue)] public int StoryVersionId { get; set; }
    [Range(1, int.MaxValue)] public int PackageRevision { get; set; }
    [Required] public bool? ExpectedConfirmed { get; set; }
    [Required, StringLength(2000)] public string SourceDeclaration { get; set; } = "";
    [Required, StringLength(2000)] public string RightsDeclaration { get; set; } = "";
}
