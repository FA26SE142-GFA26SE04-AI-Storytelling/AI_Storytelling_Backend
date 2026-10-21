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
                ContentJson = source.ContentJson,
                CreatedByUserId = actor.Id
            }, ct);
            await SaveAsync(ct);

            catalog.DraftVersionId = draft.Id;
            uow.Repository<AuthoringGuideCatalog>().Update(catalog);
            await SaveAsync(ct);

            await audit.LogAsync(actor.Id, "AuthoringGuideDraftCreated", nameof(AuthoringGuideCatalogVersion), draft.Id, null,
                new { draft.VersionNo, draft.CatalogVersion, sourceVersionId = source.Id, sourceCatalogVersion = source.CatalogVersion }, ct);
            return new GuideDraftChangeDto(catalog.RevisionToken, ToSummary(draft, ReadContent(draft).Items.Length));
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
            var itemCount = ReadContent(draft).Items.Length;

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

    public Task<GuideDraftChangeDto> UpsertItemAsync(int actorUserId, string guideId, UpsertGuideItemRequest request, CancellationToken ct = default) =>
        MutateDraftAsync(actorUserId, request.RevisionToken, "AuthoringGuideItemUpserted", content =>
        {
            var item = new AuthoringGuide(guideId, "Parent", request.Purposes ?? [], request.SupportGroup ?? "", request.ItemType ?? "",
                request.Title ?? "", request.Instruction ?? "", request.Example, request.TemplateText,
                request.PlaceholderDefinitions ?? [], request.Order);
            AuthoringGuideCatalogValidator.ValidateItem(item);
            var existing = content.Items.FirstOrDefault(x => x.Id == guideId);
            var items = existing is null
                ? content.Items.Append(item).ToArray()
                : content.Items.Select(x => x.Id == guideId ? item : x).ToArray();
            return (content with { Items = items }, existing, item);
        }, ct);

    public Task<GuideDraftChangeDto> RemoveItemAsync(int actorUserId, string guideId, string? revisionToken, CancellationToken ct = default) =>
        MutateDraftAsync(actorUserId, revisionToken, "AuthoringGuideItemRemoved", content =>
        {
            var existing = content.Items.FirstOrDefault(x => x.Id == guideId) ?? throw new NotFoundException("GUIDE_ITEM_NOT_FOUND");
            return (content with { Items = content.Items.Where(x => x.Id != guideId).ToArray() }, existing, null);
        }, ct);

    /// <summary>Sửa nội dung bản nháp trong một transaction: kiểm token → đổi nội dung → đổi token → audit.</summary>
    private async Task<GuideDraftChangeDto> MutateDraftAsync(int actorUserId, string? revisionToken, string action,
        Func<AuthoringGuideCatalogContent, (AuthoringGuideCatalogContent Content, object? Before, object? After)> change, CancellationToken ct)
    {
        var actor = await RequireActorAsync(actorUserId, ct);
        return await InTransactionAsync(async () =>
        {
            var catalog = await LoadCatalogTrackedAsync(ct);
            RequireToken(catalog, revisionToken);
            var draft = await LoadDraftTrackedAsync(catalog, ct);

            var (content, before, after) = change(ReadContent(draft));
            draft.ContentJson = WriteContent(content);
            draft.UpdatedByUserId = actor.Id;
            catalog.RevisionToken = NewToken();
            uow.Repository<AuthoringGuideCatalogVersion>().Update(draft);
            uow.Repository<AuthoringGuideCatalog>().Update(catalog);
            await SaveAsync(ct);

            await audit.LogAsync(actor.Id, action, nameof(AuthoringGuideCatalogVersion), draft.Id, before, after, ct);
            return new GuideDraftChangeDto(catalog.RevisionToken, ToSummary(draft, content.Items.Length));
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
