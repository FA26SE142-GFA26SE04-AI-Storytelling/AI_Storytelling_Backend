using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;

namespace StoryPlatform.Application.Features.Events.Services;

internal sealed record EventCreatorRubric(string PrimaryCriterion, EventCreatorRubric.Criterion[] Criteria)
{
    internal sealed record Criterion(string Key, string Label, int Weight);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly EventCreatorRubric Default = new("creativity",
    [
        new("creativity", "Sáng tạo và độc đáo", 30),
        new("education", "Giá trị giáo dục", 30),
        new("structure", "Cấu trúc và mạch lạc", 20),
        new("media", "Hình ảnh và giọng đọc", 20)
    ]);

    public static EventCreatorRubric Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Default;
        try
        {
            var rubric = JsonSerializer.Deserialize<EventCreatorRubric>(value, Json);
            if (rubric?.Criteria is { Length: > 0 } criteria &&
                criteria.All(x => x is not null && !string.IsNullOrWhiteSpace(x.Key) &&
                    !string.IsNullOrWhiteSpace(x.Label) && x.Weight is > 0 and <= 100) &&
                criteria.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == criteria.Length &&
                criteria.Sum(x => (long)x.Weight) == 100 &&
                criteria.Any(x => x.Key == rubric.PrimaryCriterion))
                return rubric;
        }
        catch (JsonException) { }
        throw new BadRequestException("Rubric cần có tiêu chí chính và các tiêu chí duy nhất với tổng trọng số 100.");
    }

    public (decimal Total, decimal Primary)? Score(string value)
    {
        try
        {
            var scores = JsonSerializer.Deserialize<Dictionary<string, decimal>>(value, Json);
            if (scores is null || scores.Count != Criteria.Length ||
                !Criteria.All(x => scores.TryGetValue(x.Key, out var score) && score is >= 0 and <= 100))
                return null;
            return (Criteria.Sum(x => scores[x.Key] * x.Weight) / 100m, scores[PrimaryCriterion]);
        }
        catch (JsonException) { return null; }
    }
}
