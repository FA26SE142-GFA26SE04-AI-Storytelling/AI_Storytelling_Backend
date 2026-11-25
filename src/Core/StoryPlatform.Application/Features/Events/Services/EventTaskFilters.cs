using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.Events.Services;

internal sealed record EventTaskFilters(int MinReadSeconds = 0, int[]? StoryIds = null, int[]? CategoryIds = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static ILookup<int, int> NoCategories { get; } = Array.Empty<StoryCategory>().ToLookup(x => x.StoryId, x => x.ContentCategoryId);

    public static EventTaskFilters Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new();
        try
        {
            var filters = JsonSerializer.Deserialize<EventTaskFilters>(value, Json);
            if (filters is not null && filters.MinReadSeconds >= 0 &&
                filters.StoryIds?.Any(id => id <= 0) != true && filters.CategoryIds?.Any(id => id <= 0) != true)
                return filters;
        }
        catch (JsonException) { }
        throw new BadRequestException("Bộ lọc nhiệm vụ không hợp lệ: minReadSeconds phải >= 0 và ID truyện/thể loại phải dương.");
    }

    public bool Includes(int storyId, ILookup<int, int> categories) =>
        (StoryIds is null || StoryIds.Contains(storyId)) &&
        (CategoryIds is null || categories[storyId].Any(CategoryIds.Contains));

    public static async Task<ILookup<int, int>> LoadCategoriesAsync(IUnitOfWork uow, List<int> storyIds, CancellationToken ct)
    {
        var categories = await uow.Repository<StoryCategory>().FindAsync(
            x => storyIds.Contains(x.StoryId) && !x.IsDeleted, cancellationToken: ct);
        return categories.ToLookup(x => x.StoryId, x => x.ContentCategoryId);
    }
}
