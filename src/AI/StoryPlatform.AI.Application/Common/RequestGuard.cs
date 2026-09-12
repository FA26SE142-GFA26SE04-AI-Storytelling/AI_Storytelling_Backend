using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;

namespace StoryPlatform.AI.Application.Common;

internal static class RequestGuard
{
    public static void Validate(GenerateOutlineRequest request)
    {
        Validate(request.RequestId, request.StoryParameters, request.Constraints);
        ValidateProfileContext(request.AgeBand, request.ReadingLevel, request.VocabularyLevel, request.Language);
        ValidateSource(request.Source, "ai");
    }

    public static void Validate(GenerateStoryRequest request)
    {
        Validate(request.RequestId, request.StoryParameters, request.Constraints);
        ValidateProfileContext(request.AgeBand, request.ReadingLevel, request.VocabularyLevel, request.Language);
        ValidateSource(request.Source, "ai");

        if (string.IsNullOrWhiteSpace(request.ApprovedOutlineReference))
        {
            throw new ArgumentException("approvedOutlineReference is required for content expansion.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Outline.Opening) ||
            string.IsNullOrWhiteSpace(request.Outline.Development) ||
            string.IsNullOrWhiteSpace(request.Outline.Ending))
        {
            throw new ArgumentException("An approved three-part outline is required.", nameof(request));
        }
    }

    public static void Validate(string requestId, StoryParametersDto? parameters, GenerationConstraintsDto constraints)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("requestId is required.", nameof(requestId));
        }

        if (constraints.MaximumWords is < 100 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(constraints.MaximumWords), "maximumWords must be between 100 and 10000.");
        }

        if (parameters is not null && parameters.RequestedLength > constraints.MaximumWords)
        {
            throw new ArgumentException("requestedLength cannot exceed maximumWords.", nameof(parameters));
        }

        var blockedTopics = constraints.BlockedTopics
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Select(topic => topic.Trim())
            .ToArray();

        if (parameters is not null && blockedTopics.Any(topic => ContainsBlockedTerm(parameters, topic)))
        {
            throw new ArgumentException("The requested topic is blocked by the supplied generation constraints.", nameof(parameters));
        }
    }

    private static bool ContainsBlockedTerm(StoryParametersDto parameters, string topic) =>
        parameters.Topic.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
        parameters.Setting.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
        parameters.Lesson.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
        parameters.Characters.Any(character => character.Contains(topic, StringComparison.OrdinalIgnoreCase));

    private static void ValidateProfileContext(string ageBand, string readingLevel, string vocabularyLevel, string language)
    {
        if (string.IsNullOrWhiteSpace(ageBand))
        {
            throw new ArgumentException("ageBand is required.", nameof(ageBand));
        }

        if (string.IsNullOrWhiteSpace(readingLevel))
        {
            throw new ArgumentException("readingLevel is required.", nameof(readingLevel));
        }

        if (string.IsNullOrWhiteSpace(vocabularyLevel))
        {
            throw new ArgumentException("vocabularyLevel is required.", nameof(vocabularyLevel));
        }

        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("language is required.", nameof(language));
        }
    }

    private static void ValidateSource(string source, string expected)
    {
        if (!string.Equals(source, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"source must be '{expected}' for this operation.", nameof(source));
        }
    }
}
