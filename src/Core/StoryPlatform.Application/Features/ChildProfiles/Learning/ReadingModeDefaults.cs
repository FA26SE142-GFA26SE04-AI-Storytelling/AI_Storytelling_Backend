using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Learning;

public static class ReadingModeDefaults
{
    /// <summary>Age_6_8 → nghe là chính; Age_9_12 → tự đọc.</summary>
    public static ReadingMode ForAgeBand(AgeBand ageBand) =>
        ageBand == AgeBand.Age_6_8 ? ReadingMode.ListenFirst : ReadingMode.ReadFirst;
}
