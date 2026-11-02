using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public sealed record ListeningAudioPart(int? OptionId, string PartKey, string Text);

/// <summary>Các câu cần đọc của tương tác; bỏ qua trường rỗng và option đã xoá.</summary>
public static class ListeningAudioParts
{
    public const string Prompt = "prompt", Wait = "wait", Match = "match", NoMatch = "noMatch",
        Hint = "hint", Text = "text", Feedback = "feedback";

    public static IReadOnlyList<ListeningAudioPart> For(
        ListeningInteraction interaction, IReadOnlyList<ListeningInteractionOption> options)
    {
        var parts = new List<ListeningAudioPart>();
        void Add(int? optionId, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) parts.Add(new ListeningAudioPart(optionId, key, value));
        }

        switch (interaction.Type)
        {
            case ListeningInteractionType.Checkpoint:
                Add(null, Prompt, interaction.Prompt);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                break;
            case ListeningInteractionType.RolePlay:
                Add(null, Match, interaction.MatchFeedback);
                break;
            case ListeningInteractionType.FillBlank:
                Add(null, Hint, interaction.Hint);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                break;
            case ListeningInteractionType.EventOrdering:
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                break;
            case ListeningInteractionType.PicturePrediction:
                Add(null, Prompt, interaction.Prompt);
                Add(null, Wait, interaction.WaitFeedback);
                break;
        }
        // RolePlay dùng audio segment; đáp án FillBlank không cần đọc riêng.
        if (interaction.Type is ListeningInteractionType.RolePlay or ListeningInteractionType.FillBlank) return parts;

        var readsFeedback = interaction.Type is ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction;
        foreach (var option in options.Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted)
                     .OrderBy(x => x.SortOrder))
        {
            Add(option.Id, Text, option.Text);
            if (readsFeedback) Add(option.Id, Feedback, option.Feedback);
        }
        return parts;
    }
}
