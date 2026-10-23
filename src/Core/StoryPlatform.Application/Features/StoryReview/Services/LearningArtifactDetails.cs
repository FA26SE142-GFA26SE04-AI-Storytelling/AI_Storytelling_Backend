using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Domain.Entities;
using QuizItemDto = StoryPlatform.Application.Features.StoryReview.DTOs.QuizItemDto;
using VocabularyItemDto = StoryPlatform.Application.Features.StoryReview.DTOs.VocabularyItemDto;

namespace StoryPlatform.Application.Features.StoryReview.Services;

internal static class LearningArtifactDetails
{
    public static void Apply(StoryVocabulary entity, VocabularyItemDto input, string content)
    {
        var changed = !string.Equals(entity.Term, input.Term.Trim(), StringComparison.OrdinalIgnoreCase);
        if (input.SourceQuote is not null)
        {
            var anchor = Anchor(content, input.SourceQuote);
            if (!LearningArtifactGrounding.ContainsTerm(input.SourceQuote, input.Term)) throw new BadRequestException("VOCABULARY_EVIDENCE_MISMATCH");
            entity.SourceQuote = input.SourceQuote; entity.SourceStart = anchor.Start; entity.SourceEnd = anchor.End;
        }
        else if (changed) { entity.SourceQuote = null; entity.SourceStart = entity.SourceEnd = null; }
        if (input.Example is not null) { Anchor(content, input.Example); entity.Example = input.Example; }
        else if (changed) entity.Example = null;
    }
    public static void Apply(QuizItem entity, QuizItemDto input, string content)
    {
        var changed = !string.Equals(entity.Type.ToString(), input.Type, StringComparison.OrdinalIgnoreCase) ||
            entity.Question != input.Question.Trim() || entity.CorrectAnswer != input.CorrectAnswer?.Trim() ||
            entity.Choices != (input.Choices is null ? null : JsonSerializer.Serialize(input.Choices));
        if (input.SourceQuote is not null)
        {
            var anchor = Anchor(content, input.SourceQuote);
            entity.SourceQuote = input.SourceQuote; entity.SourceStart = anchor.Start; entity.SourceEnd = anchor.End;
        }
        else if (changed) { entity.SourceQuote = null; entity.SourceStart = entity.SourceEnd = null; }
        entity.Explanation = input.Explanation ?? (changed ? null : entity.Explanation);
        entity.Hint = input.Hint ?? (changed ? null : entity.Hint);
        entity.LearningGoal = input.LearningGoal ?? (changed ? null : entity.LearningGoal);
        entity.OptionFeedbackJson = input.OptionFeedback is not null ? JsonSerializer.Serialize(input.OptionFeedback) : changed ? null : entity.OptionFeedbackJson;
        if (input.OptionFeedback is not null && (input.Choices?.Count ?? 0) != input.OptionFeedback.Count)
            throw new BadRequestException("QUIZ_OPTION_FEEDBACK_INVALID");
    }
    private static (int Start, int End) Anchor(string content, string quote)
    {
        try { return LearningArtifactGrounding.Anchor(content, quote); }
        catch (JsonException) { throw new BadRequestException("LEARNING_EVIDENCE_NOT_IN_STORY"); }
    }
}
