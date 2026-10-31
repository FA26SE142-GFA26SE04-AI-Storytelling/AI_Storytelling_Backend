using System.Collections.Generic;

namespace StoryPlatform.Domain.Entities;

/// <summary>Một mục hướng dẫn trong một phiên bản; mục của bản Published không được sửa.</summary>
public class AuthoringGuideItem : BaseEntity
{
    public int CatalogVersionId { get; set; }

    /// <summary>Mã ổn định qua các phiên bản, là <c>id</c> trong API (ví dụ <c>opening-1</c>).</summary>
    public string GuideKey { get; set; } = string.Empty;

    /// <summary><c>question</c> hoặc <c>template</c>.</summary>
    public string ItemType { get; set; } = string.Empty;

    public string SupportGroup { get; set; } = string.Empty;
    public List<string> Purposes { get; set; } = new();
    public string Title { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public string? Example { get; set; }

    /// <summary>Câu mẫu có chỗ trống <c>{{key}}</c>; chỉ có ở mục <c>template</c>.</summary>
    public string? TemplateText { get; set; }

    public int Order { get; set; }
}
