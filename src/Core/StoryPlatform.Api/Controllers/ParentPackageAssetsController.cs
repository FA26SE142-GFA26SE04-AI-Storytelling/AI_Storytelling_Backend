using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Api.Controllers;

[Authorize(Roles = "User")]
[Route("api/v1/parent/stories/{storyId:int}/packages/{packageId:int}/assets")]
public sealed class ParentPackageAssetsController(ParentStoryPackageService packages, IUnitOfWork uow, IMediaStorage storage) : BaseApiController
{
    [HttpGet("{assetId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(int storyId, int packageId, int assetId, CancellationToken ct)
    {
        var preview = await packages.PreviewAsync(GetCurrentUserId(), storyId, packageId, ct);
        if (!preview.Manifest.MediaAssetIds.Contains(assetId)) throw new NotFoundException("MediaAsset", assetId);
        var asset = await uow.Repository<MediaAsset>().GetByIdAsync(assetId, ct) ?? throw new NotFoundException("MediaAsset", assetId);
        var binary = await storage.ReadAsync(asset.Url!, ct);
        await packages.PreviewAsync(GetCurrentUserId(), storyId, packageId, ct);
        return File(binary.Content, binary.MimeType, enableRangeProcessing: true);
    }
}
