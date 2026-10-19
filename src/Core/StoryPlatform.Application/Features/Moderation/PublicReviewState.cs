using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StoryPlatform.Application.Features.Moderation;

public sealed record GrayZoneFlag(string Id, string Kind, string Name, string? Reason);

public sealed record ResolvedFlag(string FlagId, string Reason, int ByUserId, DateTime At);

/// <summary>Đọc và ghi trạng thái duyệt trong các cột jsonb của moderation_items.</summary>
public static class PublicReviewState
{
    public static HashSet<int> GetViewed(string? reviewProgress, string partType)
    {
        var result = new HashSet<int>();
        if (ParseOrNew(reviewProgress)["viewed"] is JsonObject viewed && viewed[partType] is JsonArray ids)
        {
            foreach (var id in ids)
            {
                var value = ReadInt(id);
                if (value.HasValue)
                {
                    result.Add(value.Value);
                }
            }
        }

        return result;
    }

    public static string AddViewed(string? reviewProgress, string partType, int partId)
    {
        var root = ParseOrNew(reviewProgress);
        var viewed = root["viewed"] as JsonObject;
        if (viewed == null)
        {
            viewed = new JsonObject();
            root["viewed"] = viewed;
        }

        var ids = viewed[partType] as JsonArray;
        if (ids == null)
        {
            ids = new JsonArray();
            viewed[partType] = ids;
        }

        if (!ids.Any(node => ReadInt(node) == partId))
        {
            ids.Add(partId);
        }

        return root.ToJsonString();
    }

    public static List<GrayZoneFlag> ParseVersionFlags(string? grayZoneFlags)
    {
        var root = ParseOrNew(grayZoneFlags);
        var reason = ReadString(root["reason"]);
        var flags = new List<GrayZoneFlag>();
        AddFlagNames(flags, root["sections"], "section", reason);
        AddFlagNames(flags, root["artifacts"], "artifact", reason);
        return flags;
    }

    public static List<ResolvedFlag> GetResolvedFlags(string? itemFlags)
    {
        var list = new List<ResolvedFlag>();
        if (ParseOrNew(itemFlags)["resolved"] is not JsonArray array)
        {
            return list;
        }

        foreach (var node in array)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            var flagId = ReadString(entry["flagId"]);
            if (flagId == null)
            {
                continue;
            }

            var at = DateTime.TryParse(ReadString(entry["at"]), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed) ? parsed : default;
            list.Add(new ResolvedFlag(flagId, ReadString(entry["reason"]) ?? string.Empty,
                ReadInt(entry["byUserId"]) ?? 0, at));
        }

        return list;
    }

    public static string AddResolvedFlag(string? itemFlags, ResolvedFlag flag)
    {
        var root = ParseOrNew(itemFlags);
        var resolved = root["resolved"] as JsonArray;
        if (resolved == null)
        {
            resolved = new JsonArray();
            root["resolved"] = resolved;
        }

        resolved.Add(new JsonObject
        {
            ["flagId"] = flag.FlagId,
            ["reason"] = flag.Reason,
            ["byUserId"] = flag.ByUserId,
            ["at"] = flag.At.ToString("O", CultureInfo.InvariantCulture)
        });
        return root.ToJsonString();
    }

    public static Dictionary<string, bool> GetChecklist(string? checklist)
    {
        var result = new Dictionary<string, bool>();
        var root = ParseOrNew(checklist);
        foreach (var key in PublicReviewRequirements.ChecklistKeys)
        {
            if (root[key] is JsonValue value && value.TryGetValue<bool>(out var passed))
            {
                result[key] = passed;
            }
        }

        return result;
    }

    public static string MergeChecklist(string? checklist, IReadOnlyDictionary<string, bool> changes)
    {
        var root = ParseOrNew(checklist);
        foreach (var (key, passed) in changes)
        {
            root[key] = JsonValue.Create(passed);
        }

        return root.ToJsonString();
    }

    private static void AddFlagNames(List<GrayZoneFlag> flags, JsonNode? node, string kind, string? reason)
    {
        if (node is not JsonArray names)
        {
            return;
        }

        foreach (var item in names)
        {
            var name = ReadString(item);
            if (!string.IsNullOrWhiteSpace(name))
            {
                flags.Add(new GrayZoneFlag($"{kind}:{name}", kind, name, reason));
            }
        }
    }

    internal static JsonObject ParseOrNew(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    internal static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static int? ReadInt(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;
}
