using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Api.Controllers;

[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/manual-stories/{confirmationId:int}/assets")]
public sealed class ChildPackageAssetsController(ParentStoryPackageReader reader, IUnitOfWork uow, IMediaStorage storage) : ControllerBase
{
    [HttpGet("{assetId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(int confirmationId, int assetId, CancellationToken ct)
    {
        var childId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var value) ? value : throw new UnauthorizedAccessException();
        var authorized = await reader.AuthorizeAsync(childId, confirmationId, ct);
        var manifest = JsonSerializer.Deserialize<ParentPackageManifest>(authorized.Package.ManifestJson, ParentPackageContext.Json)!;
        if (!manifest.MediaAssetIds.Contains(assetId)) throw new NotFoundException("MediaAsset", assetId);
        var asset = await uow.Repository<MediaAsset>().GetByIdAsync(assetId, ct) ?? throw new NotFoundException("MediaAsset", assetId);
        var binary = await storage.ReadAsync(asset.Url!, ct);
        // Revalidate after I/O: revocation cannot finish a newly authorized response unnoticed.
        await reader.AuthorizeAsync(childId, confirmationId, ct);
        return File(binary.Content, binary.MimeType, enableRangeProcessing: true);
    }
}
