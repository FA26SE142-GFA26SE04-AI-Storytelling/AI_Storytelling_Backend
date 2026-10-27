namespace StoryPlatform.AI.Domain.Generation;

/// <summary>
/// The provider stopped generating because it reached the configured output-token ceiling. Retrying the same
/// request with the same ceiling produces the same truncation, so this is deliberately not a retryable failure:
/// it is a capacity signal that must reach the operator instead of being burned as another attempt.
/// </summary>
public sealed class LlmOutputTruncatedException(string message) : Exception(message);
