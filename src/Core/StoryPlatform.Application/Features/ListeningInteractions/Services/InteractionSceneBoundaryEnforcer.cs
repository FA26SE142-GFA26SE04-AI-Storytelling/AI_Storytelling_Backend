using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>Cắt scene sau các đoạn cần dừng để hiển thị tương tác.</summary>
public static class InteractionSceneBoundaryEnforcer
{
    public static IReadOnlyList<SceneSelection> Enforce(
        IReadOnlyList<StoryTextBlock> blocks,
        IReadOnlyList<SceneSelection> selections,
        IReadOnlyList<ListeningInteraction> interactions,
        IReadOnlyList<ListeningInteractionOption> options)
    {
        var active = interactions.Where(x => !x.IsDeleted).ToArray();
        if (active.Length == 0) return selections;

        var required = RequiredBoundaryBlockIds(blocks, active, options);
        var ordered = selections.OrderBy(x => x.SceneIndex).ToArray();
        // Đánh lại số sẽ che lỗi thứ tự của scene đầu vào.
        for (var i = 0; i < ordered.Length; i++)
            if (ordered[i].SceneIndex != i || ordered[i].BlockIds.Count == 0)
                throw new InvalidOperationException("INVALID_SCENE_ORDER");

        var result = new List<SceneSelection>(ordered.Length);
        foreach (var selection in ordered)
        {
            var part = new List<string>();
            foreach (var blockId in selection.BlockIds)
            {
                part.Add(blockId);
                if (!required.Contains(blockId)) continue;
                result.Add(new SceneSelection(result.Count, part.ToArray(), selection.Focus));
                part = [];
            }
            if (part.Count > 0) result.Add(new SceneSelection(result.Count, part.ToArray(), selection.Focus));
        }
        return result;
    }

    private static HashSet<string> RequiredBoundaryBlockIds(
        IReadOnlyList<StoryTextBlock> blocks,
        IReadOnlyList<ListeningInteraction> interactions,
        IReadOnlyList<ListeningInteractionOption> options)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var interaction in interactions)
        {
            required.Add(BlockEndingAt(blocks, interaction.AnchorOffset));
            if (interaction.Type == ListeningInteractionType.PicturePrediction)
                required.Add(BlockEndingAt(blocks, interaction.RevealOffset ?? throw Unresolved()));
            if (interaction.Type != ListeningInteractionType.EventOrdering) continue;
            // Mỗi sự kiện cần kết thúc một scene để có ảnh và vị trí nghe lại riêng.
            foreach (var option in options.Where(o => o.ListeningInteractionId == interaction.Id && !o.IsDeleted))
                required.Add(BlockContaining(blocks, option.SourceStart ?? throw Unresolved()));
        }
        return required;
    }

    private static string BlockEndingAt(IReadOnlyList<StoryTextBlock> blocks, int offset) =>
        blocks.FirstOrDefault(b => b.EndOffset == offset)?.BlockId ?? throw Unresolved();

    private static string BlockContaining(IReadOnlyList<StoryTextBlock> blocks, int offset) =>
        blocks.FirstOrDefault(b => b.StartOffset <= offset && offset < b.EndOffset)?.BlockId ?? throw Unresolved();

    private static InvalidOperationException Unresolved() => new("INTERACTION_BOUNDARY_UNRESOLVED");
}
