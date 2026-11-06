using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.Moderation;

public static class PublicStoryLabels
{
    public static async Task<List<int>> NormalizeAsync(IUnitOfWork unitOfWork, IEnumerable<int>? ids, CancellationToken ct)
    {
        var wanted = (ids ?? []).Distinct().OrderBy(id => id).ToList();
        if (wanted.Count == 0)
            throw new BadRequestException("Truyện phải có ít nhất một nhãn nội dung.");

        var categories = await unitOfWork.Repository<ContentCategory>().FindAsync(
            category => wanted.Contains(category.Id), cancellationToken: ct);
        var invalid = wanted.Where(id => !categories.Any(category => category.Id == id && category.IsActive)).ToList();
        if (invalid.Count > 0)
            throw new BadRequestException($"Nhãn không tồn tại hoặc không còn hoạt động: {string.Join(", ", invalid)}.");
        return wanted;
    }

    public static string Serialize(IReadOnlyCollection<int> ids) => JsonSerializer.Serialize(ids.OrderBy(id => id));

    public static List<int> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return (JsonSerializer.Deserialize<List<int>>(json) ?? []).Distinct().OrderBy(id => id).ToList(); }
        catch (JsonException) { return []; }
    }
}
