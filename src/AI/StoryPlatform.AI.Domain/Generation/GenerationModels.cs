namespace StoryPlatform.AI.Domain.Generation;

public sealed record GenerationContext(
    string RequestId,
    string AgeBand,
    string ReadingLevel,
    string VocabularyLevel,
    string Language);

public sealed record LlmGenerationResult(
    string Content,
    string ModelProvider,
    string Model,
    string ModelVersion,
    int InputTokens,
    int OutputTokens,
    long LatencyMs);
