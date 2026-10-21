using System.Text.RegularExpressions;
using StoryPlatform.Application.Common.Exceptions;

namespace StoryPlatform.Application.Features.ParentAuthoring;

/// <summary>Luật hợp lệ của bộ hướng dẫn, dùng chung cho nạp dữ liệu, lưu bản nháp, xem trước và phát hành.</summary>
public static class AuthoringGuideCatalogValidator
{
    public static readonly string[] SupportGroups =
        ["starting_point", "opening", "events", "emotion", "dialogue", "transition", "ending"];

    public static readonly string[] ItemTypes = ["question", "template"];

    public static readonly Regex PlaceholderPattern = new(@"\{\{([a-z_]+)\}\}", RegexOptions.Compiled);

    public static void Validate(IReadOnlyCollection<AuthoringGuide> items)
    {
        if (items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new BadRequestException("GUIDE_ID_DUPLICATED");
        foreach (var item in items) ValidateItem(item);
    }

    public static void ValidateItem(AuthoringGuide item)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Title) ||
            string.IsNullOrWhiteSpace(item.Instruction))
            throw new BadRequestException("GUIDE_FIELD_REQUIRED");
        if (item.AudienceRole != "Parent") throw new BadRequestException("GUIDE_AUDIENCE_INVALID");

        var purposes = item.Purposes ?? [];
        if (purposes.Length == 0 || purposes.Any(p => !ParentManualAuthoringPolicy.Purposes.Contains(p)))
            throw new BadRequestException("GUIDE_PURPOSE_INVALID");
        if (!SupportGroups.Contains(item.SupportGroup)) throw new BadRequestException("GUIDE_GROUP_INVALID");
        if (!ItemTypes.Contains(item.ItemType)) throw new BadRequestException("GUIDE_ITEM_TYPE_INVALID");

        var declared = item.PlaceholderDefinitions ?? [];
        if (item.ItemType == "question")
        {
            if (item.TemplateText is not null || declared.Length > 0)
                throw new BadRequestException("GUIDE_QUESTION_HAS_TEMPLATE");
            return;
        }

        if (string.IsNullOrWhiteSpace(item.TemplateText)) throw new BadRequestException("GUIDE_TEMPLATE_REQUIRED");
        var used = PlaceholderPattern.Matches(item.TemplateText).Select(m => m.Groups[1].Value)
            .Distinct().Order(StringComparer.Ordinal);
        if (!used.SequenceEqual(declared.Distinct().Order(StringComparer.Ordinal)))
            throw new BadRequestException("GUIDE_PLACEHOLDERS_MISMATCH");
    }
}
