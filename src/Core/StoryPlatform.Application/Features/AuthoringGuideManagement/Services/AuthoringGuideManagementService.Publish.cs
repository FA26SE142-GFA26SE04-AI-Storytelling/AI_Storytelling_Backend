using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuthoringGuideManagement.DTOs;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.AuthoringGuideManagement.Services;

public sealed partial class AuthoringGuideManagementService
{
    public async Task<GuideDraftPreviewDto> PreviewAsync(int actorUserId, PreviewGuideDraftRequest request, CancellationToken ct = default)
    {
        await RequireActorAsync(actorUserId, ct);
        var catalog = await LoadCatalogTrackedAsync(ct);
        RequireToken(catalog, request.RevisionToken);
        var draft = await LoadDraftTrackedAsync(catalog, ct);
        var items = ReadContent(draft).Items;
        AuthoringGuideCatalogValidator.Validate(items);

        if (!string.IsNullOrWhiteSpace(request.GuideId))
        {
            var guide = items.FirstOrDefault(x => x.Id == request.GuideId) ?? throw new NotFoundException("Guide", request.GuideId);
            return new(catalog.RevisionToken, null,
                new RenderGuideResult(draft.CatalogVersion, guide.Id, AuthoringGuideCatalogQuery.RenderText(guide, request.Values)));
        }

        AuthoringGuideCatalogQuery.ValidatePaging(request.Purpose, request.PageIndex, request.PageSize);
        return new(catalog.RevisionToken,
            AuthoringGuideCatalogQuery.Page(draft.CatalogVersion, catalog.Locale, request.Purpose, request.Group,
                request.PageIndex, request.PageSize, items), null);
    }

    public async Task<GuidePublishResultDto> PublishAsync(int actorUserId, PublishGuideDraftRequest request, CancellationToken ct = default)
    {
        var actor = await RequireActorAsync(actorUserId, ct);
        return await InTransactionAsync(async () =>
        {
            var catalog = await LoadCatalogTrackedAsync(ct);
            RequireToken(catalog, request.RevisionToken);
            var draft = await LoadDraftTrackedAsync(catalog, ct);

            var items = ReadContent(draft).Items;
            if (items.Length == 0) throw new BadRequestException("GUIDE_CATALOG_EMPTY");
            AuthoringGuideCatalogValidator.Validate(items);

            var previousId = catalog.PublishedVersionId;
            var previousVersion = previousId is null ? null : (await uow.Repository<AuthoringGuideCatalogVersion>().GetByIdAsync(previousId.Value, ct))?.CatalogVersion;

            draft.Status = AuthoringGuideVersionStatus.Published;
            draft.PublishedAt = DateTime.UtcNow;
            draft.PublishedByUserId = actor.Id;
            draft.UpdatedByUserId = actor.Id;
            catalog.PublishedVersionId = draft.Id;
            catalog.DraftVersionId = null;
            catalog.RevisionToken = NewToken();
            uow.Repository<AuthoringGuideCatalogVersion>().Update(draft);
            uow.Repository<AuthoringGuideCatalog>().Update(catalog);
            await SaveAsync(ct);

            await audit.LogAsync(actor.Id, "AuthoringGuideCatalogPublished", nameof(AuthoringGuideCatalogVersion), draft.Id,
                new { publishedVersionId = previousId, catalogVersion = previousVersion },
                new { publishedVersionId = draft.Id, catalogVersion = draft.CatalogVersion, draft.VersionNo, itemCount = items.Length }, ct);
            return new GuidePublishResultDto(catalog.RevisionToken, ToSummary(draft, items.Length));
        }, ct);
    }
}
