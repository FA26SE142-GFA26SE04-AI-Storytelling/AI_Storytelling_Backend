using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public static partial class ListeningInteractionRules
{
    public const int WhoSaidItMaxWords = 12;

    private static void ValidateCharacterLetter(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(item.CharacterName)) { errors.Add(new("SCHEMA_INVALID", index, "characterName")); return; }
        if (!AppearsBefore(context.Content, item.CharacterName, item.AnchorOffset))
            errors.Add(new("NOT_GROUNDED", index, "characterName"));
        var units = VietnameseLetterUnits.Split(item.CharacterName.Trim());
        if (item.BlankIndex is not { } blank || blank < 0 || blank >= units.Count || !VietnameseLetterUnits.IsBlankable(units[blank]))
        {
            errors.Add(new("LETTER_BLANK_INVALID", index, "blankIndex"));
            return;
        }
        if (item.Options.Count != 3 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Any(o => !VietnameseLetterUnits.BaseLetters.Contains(o.Text.Trim().ToLowerInvariant())))
            errors.Add(new("LETTER_BLANK_INVALID", index, "options"));
        var matches = item.Options.Where(o => o.IsMatch).ToArray();
        if (matches.Any(o => string.IsNullOrWhiteSpace(o.ImageDescription)))
            errors.Add(new("SCHEMA_INVALID", index, "options.imageDescription"));
        if (matches.Length != 1 || !string.Equals(matches[0].Text.Trim(), units[blank], StringComparison.OrdinalIgnoreCase))
            errors.Add(new("LETTER_BLANK_INVALID", index, "options"));
    }

    private static void ValidateWhoSaidIt(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        var line = item.SourceQuote;
        var dialogueBlock = context.Blocks.FirstOrDefault(b => context.DialogueBlockIds.Contains(b.BlockId) &&
            line is not null && b.Text.Contains(line, StringComparison.Ordinal) && b.EndOffset <= item.AnchorOffset);
        if (string.IsNullOrWhiteSpace(line) || dialogueBlock is null || !IsInsideQuotes(dialogueBlock.Text, line))
            errors.Add(new("SPEAKER_INVALID", index, "lineQuote"));
        if (line is not null && CountWords(line) > WhoSaidItMaxWords) errors.Add(new("LENGTH_LIMIT", index, "lineQuote"));
        if (item.Options.Count is < 2 or > 3 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Count(o => o.IsMatch) != 1) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
        {
            if (string.IsNullOrWhiteSpace(item.Options[j].ImageDescription))
                errors.Add(new("SCHEMA_INVALID", index, $"options[{j}].imageDescription"));
            if (!AppearsBefore(context.Content, item.Options[j].Text, item.AnchorOffset))
                errors.Add(new("SPEAKER_INVALID", index, $"options[{j}].text"));
        }
    }

    private static void ValidatePictureAge6(ListeningInteractionDraft item, int index, List<ListeningValidationError> errors)
    {
        if (item.Options.Count != 2 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Any(o => o.IsMatch)) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        if (item.RevealOffset is not null) errors.Add(new("SCHEMA_INVALID", index, "revealBlockId"));
        for (var j = 0; j < item.Options.Count; j++)
            if (string.IsNullOrWhiteSpace(item.Options[j].ImageDescription))
                errors.Add(new("SCHEMA_INVALID", index, $"options[{j}].imageDescription"));
    }

    private static bool AppearsBefore(string content, string term, int? anchor) =>
        anchor is { } a && content.AsSpan(0, Math.Clamp(a, 0, content.Length)).Contains(term.Trim().AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static bool IsInsideQuotes(string blockText, string line)
    {
        var start = blockText.IndexOf(line, StringComparison.Ordinal);
        var before = blockText[..start];
        var lastOpen = before.LastIndexOfAny(['"', '“']);
        var lineStart = before.LastIndexOf('\n') + 1;
        var dashLine = blockText[lineStart..].TrimStart() is { Length: > 0 } l && l[0] is '-' or '–' or '—';
        return dashLine || (lastOpen >= 0 && before.Count(c => c is '"' or '“' or '”') % 2 == 1);
    }
}
