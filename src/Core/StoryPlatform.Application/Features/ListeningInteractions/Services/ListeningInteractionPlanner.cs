using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>
/// Kế hoạch tương tác lúc nghe theo số chỗ trống (số đoạn - 1) và nhóm tuổi; không ngẫu nhiên.
/// Thứ tự lấp chỗ: Checkpoint x2, RolePlay (6-8) hoặc FillBlank (9-10), PicturePrediction, thêm Checkpoint tới trần.
/// EventOrdering đặt sau đoạn cuối nên không chiếm chỗ trống, có khi truyện từ 3 đoạn.
/// Các tương tác giữa truyện được chia vùng (AssignZones) để rải đều, không để khúc dài không có tương tác.
/// </summary>
public static class ListeningInteractionPlanner
{
    public static ListeningInteractionPlan Create(AgeBand ageBand, IReadOnlyList<StoryTextBlock> blocks)
    {
        var slots = Math.Max(0, blocks.Count - 1);
        if (slots == 0) return ListeningInteractionPlan.Empty;
        var dialogue = FindDialogueBlockIds(blocks);
        // Truyện 2 đoạn: chỉ 1 điểm dừng (ngoại lệ đã chốt 31/10/2026).
        if (slots == 1) return new(slots, AssignZones([new(ListeningInteractionType.Checkpoint, 1)], blocks, dialogue), dialogue);

        var cap = ageBand == AgeBand.Age_6_8 ? 3 : 4; // Checkpoint + PicturePrediction
        var checkpoints = 2;
        var remaining = slots - checkpoints;
        var middle = new List<ListeningPlanItem>();

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
        // Đặt sau đoạn cuối, chạy sau cùng; cần 3 đoạn để 3 sự kiện ở 3 đoạn khác nhau.
        if (blocks.Count >= 3) items.Add(new(ListeningInteractionType.EventOrdering, 1));
        return new(slots, AssignZones(items, blocks, dialogue), dialogue);
    }

    /// <summary>
    /// Chia P1..P(S) (S = số chỗ trống) thành M vùng liền nhau gần bằng nhau, M = số tương tác giữa truyện:
    /// vùng k = đoạn thứ ⌊k·S/M⌋+1 tới ⌊(k+1)·S/M⌋. Loại kén chỗ chọn trước, ưu tiên vùng gần giữa truyện:
    /// RolePlay lấy vùng có thoại, FillBlank lấy vùng có câu đủ ngắn. Checkpoint lấy các vùng còn lại từ đầu truyện,
    /// PicturePrediction lấy vùng muộn nhất để đáp án gần cuối truyện.
    /// </summary>
    private static IReadOnlyList<ListeningPlanItem> AssignZones(
        IReadOnlyList<ListeningPlanItem> items, IReadOnlyList<StoryTextBlock> blocks, IReadOnlyList<string> dialogue)
    {
        var slots = blocks.Count - 1;
        var middle = items.Where(x => x.Type != ListeningInteractionType.EventOrdering).Sum(x => x.Count);
        var zones = Enumerable.Range(0, middle).Select(k => blocks
            .Skip(k * slots / middle).Take((k + 1) * slots / middle - k * slots / middle).ToArray()).ToArray();
        var free = Enumerable.Range(0, middle).ToList();
        var center = (middle - 1) / 2.0;

        IReadOnlyList<string> Claim(int zone)
        {
            free.Remove(zone);
            return zones[zone].Select(b => b.BlockId).ToArray();
        }
        // Không vùng nào hợp thì vẫn lấy vùng gần giữa nhất; rules sẽ báo lỗi nội dung như trước.
        IReadOnlyList<string> TakeFitting(Func<StoryTextBlock, bool> fits) => Claim(free
            .OrderBy(z => zones[z].Any(fits) ? 0 : 1).ThenBy(z => Math.Abs(z - center)).ThenBy(z => z).First());
        IReadOnlyList<string> TakeEarliest() => Claim(free[0]);

        var assigned = new Dictionary<ListeningInteractionType, IReadOnlyList<IReadOnlyList<string>>>();
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.RolePlay))
            assigned[item.Type] = [TakeFitting(b => dialogue.Contains(b.BlockId))];
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.FillBlank))
            assigned[item.Type] = [TakeFitting(HasShortSentence)];
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.Checkpoint))
            assigned[item.Type] = Enumerable.Range(0, item.Count).Select(_ => TakeEarliest()).ToArray();
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.PicturePrediction))
            assigned[item.Type] = [TakeEarliest()];

        return items.Select(x => assigned.TryGetValue(x.Type, out var z) ? x with { Zones = z } : x).ToArray();
    }

    private static bool HasShortSentence(StoryTextBlock block) =>
        block.Text.Split(['.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ListeningInteractionRules.CountWords)
            .Any(words => words is > 0 and <= ListeningInteractionRules.FillBlankMaxWords);

    /// <summary>Đoạn có câu thoại (ngoặc kép hoặc dòng mở đầu bằng gạch ngang), trừ đoạn cuối vì không được neo.</summary>
    public static IReadOnlyList<string> FindDialogueBlockIds(IReadOnlyList<StoryTextBlock> blocks) =>
        blocks.Take(Math.Max(0, blocks.Count - 1)).Where(b => HasDialogue(b.Text)).Select(b => b.BlockId).ToArray();

    private static bool HasDialogue(string text) =>
        text.IndexOfAny(['"', '“', '”']) >= 0 ||
        text.Split('\n').Select(line => line.TrimStart()).Any(line => line.Length > 0 && line[0] is '-' or '–' or '—');
}
