using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuthoringGuideManagement.DTOs;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.AuthoringGuideManagement.Services;

public sealed partial class AuthoringGuideManagementService
{
    public async Task<GuideDraftChangeDto> CreateDraftAsync(int actorUserId, CreateGuideDraftRequest request, CancellationToken ct = default)
    {
        var actor = await RequireActorAsync(actorUserId, ct);
        return await InTransactionAsync(async () =>
        {
            var catalog = await LoadCatalogTrackedAsync(ct);
            if (catalog.DraftVersionId is not null) throw new ConflictException("GUIDE_DRAFT_EXISTS");
            var source = await ResolveSourceAsync(catalog, request.SourceVersionId, ct);

            // Xoay token trước: UPDATE ... WHERE RevisionToken = <cũ> chạy đầu tiên nên người tạo thua cuộc nhận 409 thay vì lỗi unique index.
            catalog.RevisionToken = NewToken();
            uow.Repository<AuthoringGuideCatalog>().Update(catalog);
            await SaveAsync(ct);

            var existing = await uow.Repository<AuthoringGuideCatalogVersion>().FindAsync(x => x.CatalogId == catalog.Id && !x.IsDeleted, cancellationToken: ct);
            var versionNo = existing.Select(x => x.VersionNo).DefaultIfEmpty(0).Max() + 1;
            var draft = await uow.Repository<AuthoringGuideCatalogVersion>().AddAsync(new AuthoringGuideCatalogVersion
            {
                CatalogId = catalog.Id,
                VersionNo = versionNo,
                CatalogVersion = $"{catalog.AudienceRole.ToLowerInvariant()}-{catalog.Locale}-{versionNo}",
                Status = AuthoringGuideVersionStatus.Draft,
                CreatedByUserId = actor.Id
            }, ct);
            await SaveAsync(ct);

            var sourceItems = await _items.LoadAsync(source.Id, catalog.AudienceRole, ct);
            await _items.AddAsync(draft.Id, sourceItems, () => SaveAsync(ct), ct);

            catalog.DraftVersionId = draft.Id;
            uow.Repository<AuthoringGuideCatalog>().Update(catalog);
            await SaveAsync(ct);

            await audit.LogAsync(actor.Id, "AuthoringGuideDraftCreated", nameof(AuthoringGuideCatalogVersion), draft.Id, null,
                new { draft.VersionNo, draft.CatalogVersion, sourceVersionId = source.Id, sourceCatalogVersion = source.CatalogVersion }, ct);
            return new GuideDraftChangeDto(catalog.RevisionToken, ToSummary(draft, sourceItems.Length));
        }, ct);
    }

    /// <summary>Hủy bản nháp bằng xóa mềm (IsDeleted = true): kiểm token → ẩn bản nháp → xóa con trỏ → đổi token → audit.</summary>
    public async Task<GuideDraftDiscardDto> DiscardDraftAsync(int actorUserId, string? revisionToken, CancellationToken ct = default)
    {
        var actor = await RequireActorAsync(actorUserId, ct);
        return await InTransactionAsync(async () =>
        {
            var catalog = await LoadCatalogTrackedAsync(ct);
            RequireToken(catalog, revisionToken);
            var draft = await LoadDraftTrackedAsync(catalog, ct);
            var itemCount = await _items.CountAsync(draft.Id, ct);

            draft.IsDeleted = true;
            draft.UpdatedByUserId = actor.Id;
            catalog.DraftVersionId = null;
            catalog.RevisionToken = NewToken();
            uow.Repository<AuthoringGuideCatalogVersion>().Update(draft);
            uow.Repository<AuthoringGuideCatalog>().Update(catalog);
            await SaveAsync(ct);

            await audit.LogAsync(actor.Id, "AuthoringGuideDraftDiscarded", nameof(AuthoringGuideCatalogVersion), draft.Id,
                new { draft.VersionNo, draft.CatalogVersion, itemCount }, new { isDeleted = true }, ct);
            return new GuideDraftDiscardDto(catalog.RevisionToken, draft.Id, draft.CatalogVersion);
        }, ct);
    }

    public Task<GuideDraftChangeDto> UpsertItemAsync(int actorUserId, string guideId, UpsertGuideItemRequest request, CancellationToken ct = default)
    {
        var item = new AuthoringGuide(guideId, "Parent", request.Purposes ?? [], request.SupportGroup ?? "", request.ItemType ?? "",
            request.Title ?? "", request.Instruction ?? "", request.Example, request.TemplateText,
            request.PlaceholderDefinitions ?? [], request.Order);
        return MutateDraftAsync(actorUserId, request.RevisionToken, "AuthoringGuideItemUpserted",
            () => AuthoringGuideCatalogValidator.ValidateItem(item, requireLabels: true),
            async (catalog, draft) =>
            {
                var before = await _items.UpsertAsync(draft.Id, catalog.AudienceRole, item, () => SaveAsync(ct), ct);
                return ((object?)before, (object?)item);
            }, ct);
    }

    public Task<GuideDraftChangeDto> RemoveItemAsync(int actorUserId, string guideId, string? revisionToken, CancellationToken ct = default) =>
        MutateDraftAsync(actorUserId, revisionToken, "AuthoringGuideItemRemoved", () => { },
            async (catalog, draft) =>
            {
                var removed = await _items.RemoveAsync(draft.Id, catalog.AudienceRole, guideId, ct)
                              ?? throw new NotFoundException("GUIDE_ITEM_NOT_FOUND");
                return ((object?)removed, null);
            }, ct);

    /// <summary>Sửa bản nháp trong một transaction: kiểm token → kiểm nội dung → đổi token → ghi mục → audit.</summary>
    private async Task<GuideDraftChangeDto> MutateDraftAsync(int actorUserId, string? revisionToken, string action, Action validate,
        Func<AuthoringGuideCatalog, AuthoringGuideCatalogVersion, Task<(object? Before, object? After)>> change, CancellationToken ct)
    {
        var actor = await RequireActorAsync(actorUserId, ct);
        return await InTransactionAsync(async () =>
        {
            var catalog = await LoadCatalogTrackedAsync(ct);
            RequireToken(catalog, revisionToken);
            var draft = await LoadDraftTrackedAsync(catalog, ct);
            validate();

            // Đổi token trước khi ghi mục: lần lưu đầu chạy UPDATE ... WHERE RevisionToken = <cũ>, người thua nhận 409 thay vì lỗi unique index.
            catalog.RevisionToken = NewToken();
            draft.UpdatedByUserId = actor.Id;
            uow.Repository<AuthoringGuideCatalog>().Update(catalog);
            uow.Repository<AuthoringGuideCatalogVersion>().Update(draft);

            var (before, after) = await change(catalog, draft);
            await SaveAsync(ct);

            await audit.LogAsync(actor.Id, action, nameof(AuthoringGuideCatalogVersion), draft.Id, before, after, ct);
            return new GuideDraftChangeDto(catalog.RevisionToken, ToSummary(draft, await _items.CountAsync(draft.Id, ct)));
        }, ct);
    }

    private async Task<AuthoringGuideCatalogVersion> ResolveSourceAsync(AuthoringGuideCatalog catalog, int? sourceVersionId, CancellationToken ct)
    {
        var id = sourceVersionId ?? catalog.PublishedVersionId ?? throw new ConflictException("GUIDE_CATALOG_UNAVAILABLE");
        var catalogId = catalog.Id;
        var source = await uow.Repository<AuthoringGuideCatalogVersion>().FirstOrDefaultAsync(
            x => x.Id == id && x.CatalogId == catalogId && !x.IsDeleted, cancellationToken: ct)
            ?? throw new NotFoundException("Phiên bản hướng dẫn", id);
        if (source.Status != AuthoringGuideVersionStatus.Published) throw new BadRequestException("GUIDE_SOURCE_VERSION_INVALID");
        return source;
    }
}
