using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Common;

public static class StoryOutputModeContract
{
    public static StoryOutputMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "learning" => StoryOutputMode.Learning,
        "reading_media_only" => StoryOutputMode.ReadingMediaOnly,
        _ => throw new BadRequestException("outputMode phải là 'learning' hoặc 'reading_media_only'.")
    };

    public static string Format(StoryOutputMode value) => value switch
    {
        StoryOutputMode.Learning => "learning",
        StoryOutputMode.ReadingMediaOnly => "reading_media_only",
        _ => throw new BadRequestException("Story OutputMode không hợp lệ.")
    };
}
