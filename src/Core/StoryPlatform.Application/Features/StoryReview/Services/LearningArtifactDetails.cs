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
            var (quote, anchor) = Anchor(content, input.SourceQuote);
            if (!LearningArtifactGrounding.ContainsTerm(quote, input.Term)) throw new BadRequestException("VOCABULARY_EVIDENCE_MISMATCH");
            entity.SourceQuote = quote; entity.SourceStart = anchor.Start; entity.SourceEnd = anchor.End;
        }
        else if (changed) { entity.SourceQuote = null; entity.SourceStart = entity.SourceEnd = null; }
        if (input.Example is not null) { entity.Example = Anchor(content, input.Example).Quote; }
        else if (changed) entity.Example = null;
    }
    public static void Apply(QuizItem entity, QuizItemDto input, string content)
    {
        var changed = !string.Equals(entity.Type.ToString(), input.Type, StringComparison.OrdinalIgnoreCase) ||
            entity.Question != input.Question.Trim() || entity.CorrectAnswer != input.CorrectAnswer?.Trim() ||
            entity.Choices != (input.Choices is null ? null : JsonSerializer.Serialize(input.Choices));
        if (input.SourceQuote is not null)
        {
            var (quote, anchor) = Anchor(content, input.SourceQuote);
            entity.SourceQuote = quote; entity.SourceStart = anchor.Start; entity.SourceEnd = anchor.End;
        }
        else if (changed) { entity.SourceQuote = null; entity.SourceStart = entity.SourceEnd = null; }
        entity.Explanation = input.Explanation ?? (changed ? null : entity.Explanation);
        entity.Hint = input.Hint ?? (changed ? null : entity.Hint);
        entity.LearningGoal = input.LearningGoal ?? (changed ? null : entity.LearningGoal);
        entity.OptionFeedbackJson = input.OptionFeedback is not null ? JsonSerializer.Serialize(input.OptionFeedback) : changed ? null : entity.OptionFeedbackJson;
        if (input.OptionFeedback is not null && (input.Choices?.Count ?? 0) != input.OptionFeedback.Count)
            throw new BadRequestException("QUIZ_OPTION_FEEDBACK_INVALID");
    }
    private static (string Quote, (int Start, int End) Anchor) Anchor(string content, string quote)
    {
        try
        {
            var range = LearningArtifactGrounding.Anchor(content, quote);
            return (content[range.Start..range.End], range);
        }
        catch (JsonException) { throw new BadRequestException("LEARNING_EVIDENCE_NOT_IN_STORY"); }
    }
}
