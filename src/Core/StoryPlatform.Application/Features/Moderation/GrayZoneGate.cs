using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation;

public static class GrayZoneGate
{
    public static bool IsBlocked(Story story, StoryVersion version) =>
        version.Moderation.ModerationZone == ModerationZone.HardBlock ||
        story.Scope == StoryScope.Parent && version.Moderation.ModerationZone == ModerationZone.GrayZone && !version.Moderation.GrayZoneCleared;

    public static void EnsureCanContinue(Story story, StoryVersion version)
    {
        if (IsBlocked(story, version)) throw new ConflictException("SAFETY_MODERATION_REQUIRED");
    }
}
