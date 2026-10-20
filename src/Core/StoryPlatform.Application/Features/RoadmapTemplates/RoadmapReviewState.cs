using System.Text.Json;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;

namespace StoryPlatform.Application.Features.RoadmapTemplates;

internal sealed record RoadmapReviewFeedback(
    string Decision,
    string? GeneralNote,
    string? Reason,
    List<RoadmapChangePartDto> Parts,
    List<RoadmapChecklistEntryDto> Checklist);

internal static class RoadmapReviewState
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private sealed record ProgressJson(List<RoadmapChecklistEntryDto>? Checklist);

    public static List<RoadmapChecklistEntryDto> ReadChecklist(string? json)
    {
        var saved = new List<RoadmapChecklistEntryDto>();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                saved = JsonSerializer.Deserialize<ProgressJson>(json, Options)?.Checklist ?? [];
            }
            catch (JsonException)
            {
                // Dữ liệu hỏng được coi như chưa chấm.
            }
        }

        return RoadmapReviewPolicy.CriterionCodes.Select(code =>
        {
            var entry = saved.FirstOrDefault(candidate => candidate.Code == code);
            return new RoadmapChecklistEntryDto
            {
                Code = code,
                Label = RoadmapReviewPolicy.CriterionLabel(code),
                Passed = entry?.Passed ?? false,
                Note = entry?.Note
            };
        }).ToList();
    }

    public static string WriteChecklist(IEnumerable<RoadmapChecklistEntryDto> entries) =>
        JsonSerializer.Serialize(new ProgressJson(entries.ToList()), Options);

    public static string WriteFeedback(RoadmapReviewFeedback feedback) =>
        JsonSerializer.Serialize(feedback, Options);

    public static RoadmapReviewFeedback? ReadFeedback(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RoadmapReviewFeedback>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
