namespace StoryPlatform.Application.Features.AIStoryInput.Models;

/// <summary>
/// Creative input persisted only after the input guardrail returns Allow.
/// </summary>
public sealed record AcceptedAIStoryInputSnapshot(
    string Topic,
    string? Genre,
    string CharacterMode,
    IReadOnlyList<string> Characters,
    string SettingMode,
    string? Setting,
    string Lesson,
    string VocabularyLevel,
    string Language,
    int TargetLength);

/// <summary>
/// Server-resolved profile and policy values used to validate the accepted input.
/// </summary>
public sealed record AIStoryInputContextSnapshot(
    int ChildProfileId,
    string AgeBand,
    int ReadingLevel,
    string VocabularyLevel,
    string Language,
    int MaximumLength,
    string RequiredApprovalMode,
    IReadOnlyList<string> Interests,
    IReadOnlyList<string> AllowedCategoryCodes,
    IReadOnlyList<string> RestrictedCategoryCodes,
    IReadOnlyList<string> BlockedCategoryCodes);
