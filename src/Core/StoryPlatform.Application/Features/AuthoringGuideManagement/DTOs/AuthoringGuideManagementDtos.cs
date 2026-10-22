using StoryPlatform.Application.Features.ParentAuthoring;

namespace StoryPlatform.Application.Features.AuthoringGuideManagement.DTOs;

public sealed record GuideVersionSummaryDto(int VersionId, int VersionNo, string CatalogVersion, string Status, int ItemCount,
    int? CreatedByUserId, DateTime CreatedAt, int? UpdatedByUserId, DateTime? UpdatedAt, int? PublishedByUserId, DateTime? PublishedAt);

public sealed record GuideVersionDetailDto(GuideVersionSummaryDto Version, int SchemaVersion, AuthoringGuide[] Items);

public sealed record GuideManagementOverviewDto(string Locale, string AudienceRole, string RevisionToken,
    GuideVersionDetailDto? Published, GuideVersionDetailDto? Draft);

public sealed record CreateGuideDraftRequest(int? SourceVersionId);

/// <summary>Nội dung một mục; <c>id</c> lấy từ URL và <c>audienceRole</c> luôn là Parent do server đặt.</summary>
public sealed record UpsertGuideItemRequest(string? RevisionToken, string[]? Purposes, string? SupportGroup, string? ItemType,
    string? Title, string? Instruction, string? Example, string? TemplateText, string[]? PlaceholderDefinitions, int Order);

/// <summary>Có <c>GuideId</c> → điền thử mẫu bằng <c>Values</c>; không có → liệt kê theo bộ lọc như Parent sẽ thấy.</summary>
public sealed record PreviewGuideDraftRequest(string? RevisionToken, string? Purpose, string? Group, int PageIndex = 1,
    int PageSize = 10, string? GuideId = null, Dictionary<string, string>? Values = null);

public sealed record PublishGuideDraftRequest(string? RevisionToken);

public sealed record GuideDraftChangeDto(string RevisionToken, GuideVersionSummaryDto Draft);

public sealed record GuideDraftPreviewDto(string RevisionToken, GuidePage? Page, RenderGuideResult? Render);

public sealed record GuidePublishResultDto(string RevisionToken, GuideVersionSummaryDto Published);

public sealed record GuideDraftDiscardDto(string RevisionToken, int DiscardedVersionId, string DiscardedCatalogVersion);
