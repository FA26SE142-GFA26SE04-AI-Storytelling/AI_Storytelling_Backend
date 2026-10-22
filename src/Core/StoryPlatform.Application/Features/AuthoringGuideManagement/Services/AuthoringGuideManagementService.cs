using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.AuthoringGuideManagement.DTOs;
using StoryPlatform.Application.Features.AuthoringGuideManagement.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.AuthoringGuideManagement.Services;

public sealed partial class AuthoringGuideManagementService(IUnitOfWork uow, IAuditLogWriter audit) : IAuthoringGuideManagementService
{
    private static readonly JsonSerializerOptions Json = AuthoringGuideCatalogSeeder.Json;

    public async Task<GuideManagementOverviewDto> GetOverviewAsync(int actorUserId, CancellationToken ct = default)
    {
        await RequireActorAsync(actorUserId, ct);
        var catalog = await LoadCatalogAsync(ct);
        return new(catalog.Locale, catalog.AudienceRole, catalog.RevisionToken,
            await LoadDetailAsync(catalog.PublishedVersionId, ct), await LoadDetailAsync(catalog.DraftVersionId, ct));
    }

    public async Task<PagedResult<GuideVersionSummaryDto>> ListVersionsAsync(int actorUserId, int pageIndex, int pageSize,
        CancellationToken ct = default)
    {
        await RequireActorAsync(actorUserId, ct);
        if (pageIndex < 1 || pageSize is < 1 or > 100) throw new BadRequestException("PAGINATION_INVALID");
        var catalog = await LoadCatalogAsync(ct);
        var (rows, total) = await uow.Repository<AuthoringGuideCatalogVersion>().GetPagedAsync(pageIndex, pageSize,
            x => x.CatalogId == catalog.Id && !x.IsDeleted, q => q.OrderByDescending(x => x.VersionNo), cancellationToken: ct);
        return new(rows.Select(x => ToSummary(x, ReadContent(x).Items.Length)).ToList(), total, pageIndex, pageSize);
    }

    public async Task<GuideVersionDetailDto> GetVersionAsync(int actorUserId, int versionId, CancellationToken ct = default)
    {
        await RequireActorAsync(actorUserId, ct);
        var catalog = await LoadCatalogAsync(ct);
        var version = await uow.Repository<AuthoringGuideCatalogVersion>().FirstOrDefaultAsync(
            x => x.Id == versionId && x.CatalogId == catalog.Id && !x.IsDeleted, cancellationToken: ct)
            ?? throw new NotFoundException("Phiên bản hướng dẫn", versionId);
        return ToDetail(version);
    }

    // ---- dùng chung cho các phần partial ----

    private async Task<UserAccount> RequireActorAsync(int userId, CancellationToken ct)
    {
        var actor = await uow.Repository<UserAccount>().GetByIdAsync(userId, ct);
        if (actor is null || actor.IsDeleted || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.Moderator or UserRole.Administrator))
            throw new ForbiddenException("Chỉ Moderator hoặc Administrator đang hoạt động mới quản lý được hướng dẫn sáng tác.");
        return actor;
    }

    /// <summary>Bản không theo dõi, chỉ để đọc.</summary>
    private async Task<AuthoringGuideCatalog> LoadCatalogAsync(CancellationToken ct) =>
        await uow.Repository<AuthoringGuideCatalog>().FirstOrDefaultAsync(
            x => x.Locale == AuthoringGuideCatalogSeeder.DefaultLocale && x.AudienceRole == AuthoringGuideCatalogSeeder.DefaultAudience,
            cancellationToken: ct)
        ?? throw new ConflictException("GUIDE_CATALOG_UNAVAILABLE");

    /// <summary>Bản đang theo dõi: thay đổi sẽ được ghi, và EF dùng giá trị token cũ trong mệnh đề WHERE.</summary>
    private async Task<AuthoringGuideCatalog> LoadCatalogTrackedAsync(CancellationToken ct)
    {
        var plain = await LoadCatalogAsync(ct);
        return await uow.Repository<AuthoringGuideCatalog>().GetByIdAsync(plain.Id, ct)
               ?? throw new ConflictException("GUIDE_CATALOG_UNAVAILABLE");
    }

    private async Task<AuthoringGuideCatalogVersion> LoadDraftTrackedAsync(AuthoringGuideCatalog catalog, CancellationToken ct)
    {
        var id = catalog.DraftVersionId ?? throw new NotFoundException("GUIDE_DRAFT_NOT_FOUND");
        var draft = await uow.Repository<AuthoringGuideCatalogVersion>().GetByIdAsync(id, ct);
        if (draft is null || draft.IsDeleted || draft.Status != AuthoringGuideVersionStatus.Draft)
            throw new NotFoundException("GUIDE_DRAFT_NOT_FOUND");
        return draft;
    }

    private static void RequireToken(AuthoringGuideCatalog catalog, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new BadRequestException("GUIDE_REVISION_TOKEN_REQUIRED");
        if (token != catalog.RevisionToken) throw new ConflictException("GUIDE_REVISION_CONFLICT");
    }

    private static string NewToken() => Guid.NewGuid().ToString("N");

    /// <summary>Lưu thay đổi; xung đột token do DB phát hiện được quy về cùng mã GUIDE_REVISION_CONFLICT.</summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try { await uow.SaveChangesAsync(ct); }
        catch (ConflictException) { throw new ConflictException("GUIDE_REVISION_CONFLICT"); }
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await uow.BeginTransactionAsync(ct);
        try
        {
            var result = await work();
            await uow.CommitTransactionAsync(ct);
            return result;
        }
        catch
        {
            await uow.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<GuideVersionDetailDto?> LoadDetailAsync(int? versionId, CancellationToken ct)
    {
        if (versionId is null) return null;
        var id = versionId.Value;
        var version = await uow.Repository<AuthoringGuideCatalogVersion>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken: ct);
        return version is null ? null : ToDetail(version);
    }

    private static AuthoringGuideCatalogContent ReadContent(AuthoringGuideCatalogVersion version) =>
        JsonSerializer.Deserialize<AuthoringGuideCatalogContent>(version.ContentJson, Json) ?? new(1, []);

    private static string WriteContent(AuthoringGuideCatalogContent content) => JsonSerializer.Serialize(content, Json);

    private static GuideVersionSummaryDto ToSummary(AuthoringGuideCatalogVersion v, int itemCount) =>
        new(v.Id, v.VersionNo, v.CatalogVersion, v.Status.ToString(), itemCount, v.CreatedByUserId, v.CreatedAt,
            v.UpdatedByUserId, v.UpdatedAt, v.PublishedByUserId, v.PublishedAt);

    private static GuideVersionDetailDto ToDetail(AuthoringGuideCatalogVersion v)
    {
        var content = ReadContent(v);
        return new(ToSummary(v, content.Items.Length), content.SchemaVersion, content.Items);
    }
}
