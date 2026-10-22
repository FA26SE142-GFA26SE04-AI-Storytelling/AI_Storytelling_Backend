using System;
using System.Collections.Generic;

namespace StoryPlatform.Domain.Entities;

/// <summary>Bộ hướng dẫn sáng tác cho một đối tượng và ngôn ngữ (bản đầu: Parent + vi).</summary>
public class AuthoringGuideCatalog : BaseEntity
{
    public string Locale { get; set; } = "vi";
    public string AudienceRole { get; set; } = "Parent";

    /// <summary>Phiên bản Parent đang dùng.</summary>
    public int? PublishedVersionId { get; set; }

    /// <summary>Bản nháp chung đang mở (tối đa một).</summary>
    public int? DraftVersionId { get; set; }

    /// <summary>Đổi sau mỗi thay đổi; dùng để chặn sửa đồng thời.</summary>
    public string RevisionToken { get; set; } = Guid.NewGuid().ToString("N");

    public virtual ICollection<AuthoringGuideCatalogVersion> Versions { get; set; } = new List<AuthoringGuideCatalogVersion>();
}
