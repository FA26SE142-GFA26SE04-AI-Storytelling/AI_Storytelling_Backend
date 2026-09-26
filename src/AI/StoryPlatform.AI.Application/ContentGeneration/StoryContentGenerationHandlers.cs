using System.Text.Json;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.Common;
using StoryPlatform.AI.Domain.Enums;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Application.ContentGeneration;

public sealed class GenerateStoryContentHandler
{
    private readonly StoryContentGenerationExecutor _executor;
    private readonly IPromptTemplateProvider _prompts;

    public GenerateStoryContentHandler(StoryContentGenerationExecutor executor, IPromptTemplateProvider prompts)
    {
        _executor = executor;
        _prompts = prompts;
    }

    public async Task<GenerateStoryContentResponse> HandleAsync(GenerateStoryContentRequest request, CancellationToken cancellationToken = default)
    {
        ValidateContentRequest(request);
        var template = SnapshotPromptResolver.Resolve(request.Snapshot, PromptType.StoryContent,
            request.Language, request.AgeBand, _prompts);
        var minimumSections = request.Snapshot is null ? 1 : Math.Clamp(request.Snapshot.Config.MinStorySections, 2, 10);
        var maximumSections = request.Snapshot is null ? int.MaxValue : Math.Clamp(request.Snapshot.Config.MaxStorySections, 3, 20);
        if (minimumSections > maximumSections) throw new ArgumentException("Invalid pinned section limits.", nameof(request));
        var prompt = PromptComposer.Compose(template, request);
        if (request.Snapshot is not null)
            prompt += $"\nReturn {minimumSections} to {maximumSections} non-empty story sections.";
        var generated = await _executor.ExecuteAsync(
            prompt, "story_content", GenerationSchemas.StoryContent,
            json => DeserializeStory(json, minimumSections, maximumSections), cancellationToken);
        return new GenerateStoryContentResponse
        {
            RequestId = request.RequestId,
            GenerationId = Guid.NewGuid().ToString("N"),
            Story = generated.Value,
            Metadata = generated.Generation.ToMetadata(template.Version) with { AttemptCount = generated.AttemptCount }
        };
    }

    internal static void ValidateContentRequest(GenerateStoryContentRequest request)
    {
        RequestGuard.Validate(request.RequestId, request.StoryParameters, request.Constraints);
        if (string.IsNullOrWhiteSpace(request.AgeBand) || string.IsNullOrWhiteSpace(request.ReadingLevel) ||
            string.IsNullOrWhiteSpace(request.VocabularyLevel) || string.IsNullOrWhiteSpace(request.Language) ||
            string.IsNullOrWhiteSpace(request.ApprovedOutlineReference) ||
            string.IsNullOrWhiteSpace(request.Outline.Opening) ||
            string.IsNullOrWhiteSpace(request.Outline.Development) ||
            string.IsNullOrWhiteSpace(request.Outline.Ending))
        {
            throw new ArgumentException("A complete approved outline and profile context are required.", nameof(request));
        }
    }

    internal static void ValidateStory(StoryContentDto story)
    {
        if (string.IsNullOrWhiteSpace(story.Title) || string.IsNullOrWhiteSpace(story.Lesson) ||
            story.StorySections.Count == 0 || story.StorySections.Any(item => item.Order <= 0 || string.IsNullOrWhiteSpace(item.Content)) ||
            story.StorySections.Select(item => item.Order).Distinct().Count() != story.StorySections.Count)
        {
            throw new JsonException("Generated story content is incomplete.");
        }
    }

    private static StoryContentDto DeserializeStory(string json, int minimumSections, int maximumSections)
    {
        var story = JsonSerializer.Deserialize<StoryContentDto>(json, JsonDefaults.Options)
                    ?? throw new JsonException("The LLM returned an empty story payload.");
        ValidateStory(story);
        if (story.StorySections.Count < minimumSections || story.StorySections.Count > maximumSections)
            throw new JsonException("Generated story section count is outside pinned limits.");
        return EnsureDescription(story);
    }

    internal static StoryContentDto EnsureDescription(StoryContentDto story)
    {
        var source = string.IsNullOrWhiteSpace(story.Description)
            ? story.StorySections.OrderBy(item => item.Order).First().Content
            : story.Description;
        var normalized = string.Join(' ', source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 300)
        {
            var cut = normalized.LastIndexOf(' ', 299);
            normalized = normalized[..(cut >= 120 ? cut : 299)].TrimEnd() + "…";
        }

        return story with { Description = normalized };
    }
}

public sealed class RefineStoryContentHandler
{
    private readonly StoryContentGenerationExecutor _executor;
    private readonly IPromptTemplateProvider _prompts;

    public RefineStoryContentHandler(StoryContentGenerationExecutor executor, IPromptTemplateProvider prompts)
    {
        _executor = executor;
        _prompts = prompts;
    }

    public async Task<RefineStoryContentResponse> HandleAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.Reasons.Count == 0)
        {
            throw new ArgumentException("A request id and refinement reasons are required.", nameof(request));
        }
        ValidateRefinementInput(request.Story);
        var template = SnapshotPromptResolver.Resolve(request.Snapshot, PromptType.StoryContentRefinement,
            request.Language, request.AgeBand, _prompts);
        var generated = await _executor.ExecuteAsync(
            PromptComposer.Compose(template, request), "refined_story_content", GenerationSchemas.StoryContent,
            json => DeserializeRefinedStory(json), cancellationToken);
        GenerateStoryContentHandler.ValidateStory(generated.Value);
        return new RefineStoryContentResponse
        {
            RequestId = request.RequestId,
            GenerationId = Guid.NewGuid().ToString("N"),
            Story = generated.Value,
            Metadata = generated.Generation.ToMetadata(template.Version, 1) with { AttemptCount = generated.AttemptCount }
        };
    }

    private static void ValidateRefinementInput(StoryContentDto story)
    {
        if (string.IsNullOrWhiteSpace(story.Title) ||
            story.StorySections.Count == 0 ||
            story.StorySections.Any(item => item.Order <= 0 || string.IsNullOrWhiteSpace(item.Content)) ||
            story.StorySections.Select(item => item.Order).Distinct().Count() != story.StorySections.Count)
        {
            throw new ArgumentException(
                "The story to refine requires a title and valid story sections.",
                nameof(story));
        }
    }

    private static StoryContentDto DeserializeRefinedStory(string json)
    {
        var story = JsonSerializer.Deserialize<StoryContentDto>(json, JsonDefaults.Options)
                    ?? throw new JsonException("The LLM returned an empty refined story payload.");
        GenerateStoryContentHandler.ValidateStory(story);
        return GenerateStoryContentHandler.EnsureDescription(story);
    }
}

public sealed class GenerateVocabularyHandler
{
    private readonly StoryContentGenerationExecutor _executor;
    private readonly IPromptTemplateProvider _prompts;
    public GenerateVocabularyHandler(StoryContentGenerationExecutor executor, IPromptTemplateProvider prompts) => (_executor, _prompts) = (executor, prompts);

    public async Task<GenerateVocabularyResponse> HandleAsync(GenerateVocabularyRequest request, CancellationToken cancellationToken = default)
    {
        ValidateArtifactRequest(request.RequestId, request.Story);
        var template = SnapshotPromptResolver.Resolve(request.Snapshot, PromptType.Vocabulary,
            request.Language, request.AgeBand, _prompts);
        var maxItems = request.Snapshot is null ? int.MaxValue : Math.Clamp(request.Snapshot.Config.MaxVocabularyItems, 5, 20);
        var prompt = PromptComposer.Compose(template, request);
        if (request.Snapshot is not null) prompt += $"\nReturn at most {maxItems} vocabulary items.";
        var generated = await _executor.ExecuteAsync(
            prompt, "story_vocabulary", GenerationSchemas.Vocabulary,
            json => DeserializeVocabulary(json, maxItems), cancellationToken);
        return new GenerateVocabularyResponse { RequestId = request.RequestId, Items = generated.Value.Items, Metadata = generated.Generation.ToMetadata(template.Version) with { AttemptCount = generated.AttemptCount } };
    }

    private sealed record VocabularyPayload(IReadOnlyList<GeneratedVocabularyItemDto> Items);
    private static VocabularyPayload DeserializeVocabulary(string json, int maxItems)
    {
        var value = JsonSerializer.Deserialize<VocabularyPayload>(json, JsonDefaults.Options)
            ?? throw new JsonException("The LLM returned an empty vocabulary payload.");
        if (value.Items is null || value.Items.Count > maxItems)
            throw new JsonException("Generated vocabulary exceeds the pinned item limit.");
        return value;
    }
    internal static void ValidateArtifactRequest(string requestId, StoryContentDto story)
    {
        if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("requestId is required.", nameof(requestId));
        if (string.IsNullOrWhiteSpace(story.Title) ||
            story.StorySections.Count == 0 || story.StorySections.Any(item => item.Order <= 0 || string.IsNullOrWhiteSpace(item.Content)) ||
            story.StorySections.Select(item => item.Order).Distinct().Count() != story.StorySections.Count)
        {
            throw new JsonException("Story content is incomplete.");
        }
    }
}

public sealed class GenerateQuizHandler
{
    private readonly StoryContentGenerationExecutor _executor;
    private readonly IPromptTemplateProvider _prompts;
    public GenerateQuizHandler(StoryContentGenerationExecutor executor, IPromptTemplateProvider prompts) => (_executor, _prompts) = (executor, prompts);

    public async Task<GenerateQuizResponse> HandleAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default)
    {
        GenerateVocabularyHandler.ValidateArtifactRequest(request.RequestId, request.Story);
        if (request.Vocabulary.Count == 0) throw new ArgumentException("Validated vocabulary is required.", nameof(request));
        var template = SnapshotPromptResolver.Resolve(request.Snapshot, PromptType.Quiz,
            request.Language, request.AgeBand, _prompts);
        var maxItems = request.Snapshot is null ? int.MaxValue : Math.Clamp(request.Snapshot.Config.MaxQuizItems, 3, 15);
        var prompt = PromptComposer.Compose(template, request);
        if (request.Snapshot is not null) prompt += $"\nReturn at most {maxItems} quiz items.";
        var generated = await _executor.ExecuteAsync(
            prompt, "story_quiz", GenerationSchemas.Quiz,
            json => DeserializeQuiz(json, maxItems), cancellationToken);
        return new GenerateQuizResponse { RequestId = request.RequestId, Items = generated.Value.Items, Metadata = generated.Generation.ToMetadata(template.Version) with { AttemptCount = generated.AttemptCount } };
    }

    private sealed record QuizPayload(IReadOnlyList<QuizItemDto> Items);
    private static QuizPayload DeserializeQuiz(string json, int maxItems)
    {
        var value = JsonSerializer.Deserialize<QuizPayload>(json, JsonDefaults.Options)
            ?? throw new JsonException("The LLM returned an empty quiz payload.");
        if (value.Items is null || value.Items.Count > maxItems)
            throw new JsonException("Generated quiz exceeds the pinned item limit.");
        return value;
    }
}

public sealed class GenerateDiscussionHandler
{
    private readonly StoryContentGenerationExecutor _executor;
    private readonly IPromptTemplateProvider _prompts;
    public GenerateDiscussionHandler(StoryContentGenerationExecutor executor, IPromptTemplateProvider prompts) => (_executor, _prompts) = (executor, prompts);

    public async Task<GenerateDiscussionResponse> HandleAsync(GenerateDiscussionRequest request, CancellationToken cancellationToken = default)
    {
        GenerateVocabularyHandler.ValidateArtifactRequest(request.RequestId, request.Story);
        var template = SnapshotPromptResolver.Resolve(request.Snapshot, PromptType.Discussion,
            request.Language, request.AgeBand, _prompts);
        var generated = await _executor.ExecuteAsync(
            PromptComposer.Compose(template, request), "story_discussion", GenerationSchemas.Discussion,
            json => JsonSerializer.Deserialize<DiscussionPayload>(json, JsonDefaults.Options), cancellationToken);
        return new GenerateDiscussionResponse { RequestId = request.RequestId, Items = generated.Value.Items, Metadata = generated.Generation.ToMetadata(template.Version) with { AttemptCount = generated.AttemptCount } };
    }

    private sealed record DiscussionPayload(IReadOnlyList<DiscussionQuestionDto> Items);
}

public sealed class EvaluateContentSafetyHandler
{
    private readonly StoryContentGenerationExecutor _executor;
    private readonly IPromptTemplateProvider _prompts;
    public EvaluateContentSafetyHandler(StoryContentGenerationExecutor executor, IPromptTemplateProvider prompts) => (_executor, _prompts) = (executor, prompts);

    public async Task<EvaluateContentSafetyResponse> HandleAsync(
        EvaluateContentSafetyRequest request, CancellationToken cancellationToken = default)
    {
        GenerateVocabularyHandler.ValidateArtifactRequest(request.RequestId, request.Story);
        var template = SnapshotPromptResolver.Resolve(request.Snapshot, PromptType.ContentSafety,
            request.Language, request.AgeBand, _prompts);
        var generated = await _executor.ExecuteAsync(
            PromptComposer.Compose(template, request), "content_safety", GenerationSchemas.ContentSafety,
            json => JsonSerializer.Deserialize<SafetyPayload>(json, JsonDefaults.Options), cancellationToken);
        return new EvaluateContentSafetyResponse
        {
            RequestId = request.RequestId,
            IsAllowed = generated.Value.IsAllowed,
            CanRefine = generated.Value.IsAllowed || generated.Value.CanRefine,
            ReasonCode = string.IsNullOrWhiteSpace(generated.Value.ReasonCode)
                ? generated.Value.IsAllowed ? "CONTENT_SAFETY_ALLOWED" : "CONTENT_SAFETY_BLOCKED"
                : generated.Value.ReasonCode,
            Violations = generated.Value.Violations,
            Metadata = generated.Generation.ToMetadata(template.Version) with { AttemptCount = generated.AttemptCount }
        };
    }

    private sealed record SafetyPayload(bool IsAllowed, bool CanRefine, string ReasonCode, IReadOnlyList<string> Violations);
}
