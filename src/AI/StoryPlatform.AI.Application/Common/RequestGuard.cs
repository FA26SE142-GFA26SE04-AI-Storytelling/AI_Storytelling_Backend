using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.AI.Application.Common;

internal static class RequestGuard
{
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

        if (parameters is not null && constraints.BlockedTopics.Any(topic =>
                parameters.Topic.Contains(topic, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The requested topic is blocked by the supplied generation constraints.", nameof(parameters));
        }
    }
}
