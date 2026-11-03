using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.Application.Features.ContentGeneration.Quality;

/// <summary>The exact text saved in StoryVersion.Content and checked during review.</summary>
public static class CanonicalStoryContent
{
    public static string Render(StoryContentDto story) => string.Join("\n\n", story.StorySections.OrderBy(item => item.Order)
        .Select(item => string.IsNullOrWhiteSpace(item.Heading) ? item.Content.Trim() : $"{item.Heading.Trim()}\n{item.Content.Trim()}"));
}
