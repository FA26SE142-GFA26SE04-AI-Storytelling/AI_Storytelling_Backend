using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation;

public static class GrayZoneGate
{
    public static bool IsBlocked(Story story, StoryVersion version) =>
        version.ModerationZone == ModerationZone.HardBlock ||
        story.Scope == StoryScope.Parent && version.ModerationZone == ModerationZone.GrayZone && !version.GrayZoneCleared;

    public static void EnsureCanContinue(Story story, StoryVersion version)
    {
        if (IsBlocked(story, version)) throw new ConflictException("SAFETY_MODERATION_REQUIRED");
    }
}
