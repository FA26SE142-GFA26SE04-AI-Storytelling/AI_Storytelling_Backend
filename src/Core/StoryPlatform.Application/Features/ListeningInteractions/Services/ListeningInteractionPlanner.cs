using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>
/// Kế hoạch tương tác lúc nghe theo số chỗ trống (số đoạn - 1) và nhóm tuổi; không ngẫu nhiên.
/// Thứ tự lấp chỗ: Checkpoint x2, EventOrdering, RolePlay (6-8) hoặc FillBlank (9-10), PicturePrediction, thêm Checkpoint tới trần.
/// </summary>
public static class ListeningInteractionPlanner
{
    public static ListeningInteractionPlan Create(AgeBand ageBand, IReadOnlyList<StoryTextBlock> blocks)
    {
        var slots = Math.Max(0, blocks.Count - 1);
        if (slots == 0) return ListeningInteractionPlan.Empty;
        var dialogue = FindDialogueBlockIds(blocks);
        // Truyện 2 đoạn: chỉ 1 điểm dừng (ngoại lệ đã chốt 31/10/2026).
        if (slots == 1) return new(slots, [new(ListeningInteractionType.Checkpoint, 1)], dialogue);

        var cap = ageBand == AgeBand.Age_6_8 ? 3 : 4; // Checkpoint + PicturePrediction
        var checkpoints = 2;
        var remaining = slots - checkpoints;
        var middle = new List<ListeningPlanItem>();

        if (remaining > 0 && blocks.Count >= 4)
        {
            middle.Add(new(ListeningInteractionType.EventOrdering, 1));
            remaining--;
        }
        if (remaining > 0 && ageBand == AgeBand.Age_6_8 && dialogue.Count > 0)
        {
            middle.Add(new(ListeningInteractionType.RolePlay, 1));
            remaining--;
        }
        else if (remaining > 0 && ageBand == AgeBand.Age_9_10)
        {
            middle.Add(new(ListeningInteractionType.FillBlank, 1));
            remaining--;
        }
        var picture = 0;
        if (remaining > 0 && checkpoints + 1 <= cap)
        {
            picture = 1;
            remaining--;
        }
        while (remaining > 0 && checkpoints + picture < cap)
        {
            checkpoints++;
            remaining--;
        }

        var items = new List<ListeningPlanItem> { new(ListeningInteractionType.Checkpoint, checkpoints) };
        items.AddRange(middle);
        if (picture > 0) items.Add(new(ListeningInteractionType.PicturePrediction, picture));
        return new(slots, items, dialogue);
    }

    /// <summary>Đoạn có câu thoại (ngoặc kép hoặc dòng mở đầu bằng gạch ngang), trừ đoạn cuối vì không được neo.</summary>
    public static IReadOnlyList<string> FindDialogueBlockIds(IReadOnlyList<StoryTextBlock> blocks) =>
        blocks.Take(Math.Max(0, blocks.Count - 1)).Where(b => HasDialogue(b.Text)).Select(b => b.BlockId).ToArray();

    private static bool HasDialogue(string text) =>
        text.IndexOfAny(['"', '“', '”']) >= 0 ||
        text.Split('\n').Select(line => line.TrimStart()).Any(line => line.Length > 0 && line[0] is '-' or '–' or '—');
}
