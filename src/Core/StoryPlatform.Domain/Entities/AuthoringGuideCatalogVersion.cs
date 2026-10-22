using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>Nội dung một phiên bản của bộ hướng dẫn. Phiên bản Published không được sửa hoặc xóa.</summary>
public class AuthoringGuideCatalogVersion : BaseEntity
{
    public int CatalogId { get; set; }
    public virtual AuthoringGuideCatalog? Catalog { get; set; }

    public int VersionNo { get; set; }

    /// <summary>Mã phiên bản hiển thị cho client, ví dụ <c>parent-vi-1</c>; không tái sử dụng.</summary>
    public string CatalogVersion { get; set; } = string.Empty;

    public AuthoringGuideVersionStatus Status { get; set; } = AuthoringGuideVersionStatus.Draft;

    /// <summary>JSON <c>{ "schemaVersion": 1, "items": [...] }</c>.</summary>
    public string ContentJson { get; set; } = "{}";

    public int? CreatedByUserId { get; set; }
    public int? UpdatedByUserId { get; set; }
    public int? PublishedByUserId { get; set; }
    public DateTime? PublishedAt { get; set; }
}
