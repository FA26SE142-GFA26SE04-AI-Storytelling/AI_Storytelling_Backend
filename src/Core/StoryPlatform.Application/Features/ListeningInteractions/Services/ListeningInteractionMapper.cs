using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public sealed record ListeningMapResult(IReadOnlyList<ListeningInteractionDraft> Items, IReadOnlyList<ListeningValidationError> Errors);

/// <summary>
/// Đổi id đoạn và câu trích nguyên văn của AI thành offset trong Content; đổi qua lại draft và entity.
/// AI không bao giờ tự tính offset.
/// </summary>
public static class ListeningInteractionMapper
{
    public static ListeningMapResult Resolve(
        IReadOnlyList<ListeningInteractionDraft> items, string content, IReadOnlyList<StoryTextBlock> blocks)
    {
        var byId = blocks.ToDictionary(b => b.BlockId, StringComparer.Ordinal);
        var errors = new List<ListeningValidationError>();
        var resolved = new List<ListeningInteractionDraft>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!byId.TryGetValue(item.AnchorBlockId, out var anchor))
            {
                errors.Add(new("ANCHOR_INVALID", i, "afterBlockId"));
                resolved.Add(item);
                continue;
            }
            item = item with { AnchorOffset = anchor.EndOffset };
            switch (item.Type)
            {
                case ListeningInteractionType.Checkpoint:
                case ListeningInteractionType.PicturePrediction:
                    item = LocateBeforeAnchor(item, i, content, anchor.EndOffset, errors);
                    if (item.Type == ListeningInteractionType.PicturePrediction)
                    {
                        if (item.RevealBlockId is not null && byId.TryGetValue(item.RevealBlockId, out var reveal))
                            item = item with { RevealOffset = reveal.EndOffset };
                        else errors.Add(new("REVEAL_BLOCK_INVALID", i, "revealBlockId"));
                    }
                    break;
                case ListeningInteractionType.RolePlay:
                case ListeningInteractionType.FillBlank:
                    var field = item.Type == ListeningInteractionType.RolePlay ? "lineQuote" : "sentenceQuote";
                    var span = LocateIn(anchor, item.SourceQuote);
                    if (span is null) errors.Add(new("QUOTE_NOT_FOUND", i, field));
                    else item = item with { SourceStart = span.Value.Start, SourceEnd = span.Value.End };
                    break;
                case ListeningInteractionType.EventOrdering:
                    item = item with { Options = item.Options.Select((option, j) => LocateEvent(option, i, j, byId, errors)).ToArray() };
                    break;
            }
            resolved.Add(item);
        }
        return new(resolved, errors);
    }

    public static (ListeningInteraction Interaction, IReadOnlyList<ListeningInteractionOption> Options) ToEntities(
        ListeningInteractionDraft draft, int storyVersionId)
    {
        var interaction = new ListeningInteraction
        {
            StoryVersionId = storyVersionId,
            Type = draft.Type,
            AnchorOffset = draft.AnchorOffset ?? throw new InvalidOperationException("LISTENING_DRAFT_UNRESOLVED"),
            Kind = draft.Kind,
            Prompt = draft.Prompt,
            SourceQuote = draft.SourceQuote,
            SourceStart = draft.SourceStart,
            SourceEnd = draft.SourceEnd,
            CharacterName = draft.CharacterName,
            HiddenWord = draft.HiddenWord,
            Hint = draft.Hint,
            RevealOffset = draft.RevealOffset,
            Explanation = draft.Explanation,
            MatchFeedback = draft.MatchFeedback,
            NoMatchFeedback = draft.NoMatchFeedback,
            WaitFeedback = draft.WaitFeedback
        };
        var options = draft.Options.Select((o, index) => new ListeningInteractionOption
        {
            SortOrder = index + 1,
            Text = o.Text,
            Feedback = o.Feedback,
            IsMatch = o.IsMatch,
            CorrectOrder = o.CorrectOrder,
            SourceQuote = o.SourceQuote,
            SourceStart = o.SourceStart,
            SourceEnd = o.SourceEnd,
            ImageDescription = o.ImageDescription
        }).ToArray();
        return (interaction, options);
    }

    public static ListeningInteractionDraft ToDraft(
        ListeningInteraction interaction, IReadOnlyList<ListeningInteractionOption> options, IReadOnlyList<StoryTextBlock> blocks) => new()
    {
        Type = interaction.Type,
        AnchorBlockId = BlockIdEndingAt(blocks, interaction.AnchorOffset) ?? string.Empty,
        AnchorOffset = interaction.AnchorOffset,
        Kind = interaction.Kind,
        Prompt = interaction.Prompt,
        SourceQuote = interaction.SourceQuote,
        SourceStart = interaction.SourceStart,
        SourceEnd = interaction.SourceEnd,
        CharacterName = interaction.CharacterName,
        HiddenWord = interaction.HiddenWord,
        Hint = interaction.Hint,
        RevealBlockId = interaction.RevealOffset is { } reveal ? BlockIdEndingAt(blocks, reveal) : null,
        RevealOffset = interaction.RevealOffset,
        Explanation = interaction.Explanation,
        MatchFeedback = interaction.MatchFeedback,
        NoMatchFeedback = interaction.NoMatchFeedback,
        WaitFeedback = interaction.WaitFeedback,
        Options = options.OrderBy(o => o.SortOrder).Select(o => new ListeningOptionDraft
        {
            Text = o.Text,
            Feedback = o.Feedback,
            IsMatch = o.IsMatch,
            CorrectOrder = o.CorrectOrder,
            BlockId = o.SourceStart is { } start ? blocks.FirstOrDefault(b => b.StartOffset <= start && start < b.EndOffset)?.BlockId : null,
            SourceQuote = o.SourceQuote,
            SourceStart = o.SourceStart,
            SourceEnd = o.SourceEnd,
            ImageDescription = o.ImageDescription
        }).ToArray()
    };

    private static string? BlockIdEndingAt(IReadOnlyList<StoryTextBlock> blocks, int offset) =>
        blocks.FirstOrDefault(b => b.EndOffset == offset)?.BlockId;

    // Câu căn cứ phải nằm hẳn trong phần trẻ đã nghe [0, anchorEnd).
    private static ListeningInteractionDraft LocateBeforeAnchor(
        ListeningInteractionDraft item, int index, string content, int anchorEnd, List<ListeningValidationError> errors)
    {
        if (string.IsNullOrEmpty(item.SourceQuote))
        {
            errors.Add(new("QUOTE_NOT_FOUND", index, "sourceQuote"));
            return item;
        }
        var start = content[..anchorEnd].IndexOf(item.SourceQuote, StringComparison.Ordinal);
        if (start >= 0) return item with { SourceStart = start, SourceEnd = start + item.SourceQuote.Length };
        errors.Add(new(content.Contains(item.SourceQuote, StringComparison.Ordinal) ? "QUOTE_AFTER_ANCHOR" : "QUOTE_NOT_FOUND",
            index, "sourceQuote"));
        return item;
    }

    private static (int Start, int End)? LocateIn(StoryTextBlock block, string? quote)
    {
        if (string.IsNullOrEmpty(quote)) return null;
        var at = block.Text.IndexOf(quote, StringComparison.Ordinal);
        return at < 0 ? null : (block.StartOffset + at, block.StartOffset + at + quote.Length);
    }

    private static ListeningOptionDraft LocateEvent(ListeningOptionDraft option, int index, int eventIndex,
        IReadOnlyDictionary<string, StoryTextBlock> byId, List<ListeningValidationError> errors)
    {
        if (option.BlockId is null || !byId.TryGetValue(option.BlockId, out var block))
        {
            errors.Add(new("EVENT_ORDER_INVALID", index, $"events[{eventIndex}].blockId"));
            return option;
        }
        var span = LocateIn(block, option.SourceQuote);
        if (span is null)
        {
            errors.Add(new("QUOTE_NOT_FOUND", index, $"events[{eventIndex}].sourceQuote"));
            return option;
        }
        return option with { SourceStart = span.Value.Start, SourceEnd = span.Value.End };
    }
}
