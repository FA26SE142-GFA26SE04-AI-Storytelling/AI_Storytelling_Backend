using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates;

public static class RoadmapTemplatePolicy
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MinReadingLevel = 1;
    public const int MaxReadingLevel = 10;
    public const int MinDays = 1;
    public const int MaxDays = 30;
}

public static class RoadmapAge
{
    public static string ToText(AgeBand ageBand) => ageBand == AgeBand.Age_6_8 ? "6-8" : "9-10";

    public static AgeBand? Parse(string? ageBand)
    {
        var trimmed = ageBand?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed switch
        {
            "6-8" => AgeBand.Age_6_8,
            "9-10" => AgeBand.Age_9_10,
            _ => throw new BadRequestException("Độ tuổi phải là 6-8 hoặc 9-10.")
        };
    }
}

public static class RoadmapEligibility
{
    public static bool IsEligible(Story? story) =>
        story != null
        && !story.IsDeleted
        && story.Scope == StoryScope.Creator
        && story.IsPublished
        && story.Status == StoryStatus.Ready
        && story.ArchivedAt == null;
}
