using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>Plans independent listening interaction sets for each exact age.</summary>
public static class ListeningInteractionPlanner
{
    public static ListeningInteractionPlan Create(int targetAge, IReadOnlyList<StoryTextBlock> blocks) => targetAge switch
    {
        6 => CreateAge6(blocks) with { TargetAge = 6 },
        _ => ListeningInteractionPlan.Empty with { TargetAge = targetAge }
    };

    private static ListeningInteractionPlan CreateAge6(IReadOnlyList<StoryTextBlock> blocks)
    {
        var slots = Math.Max(0, blocks.Count - 1);
        if (slots == 0) return ListeningInteractionPlan.Empty;
        var dialogue = FindDialogueBlockIds(blocks);
        var hasDialogue = dialogue.Count > 0;
        var order = new List<ListeningInteractionType> { ListeningInteractionType.Checkpoint };
        if (hasDialogue) order.Add(ListeningInteractionType.RolePlay);
        order.Add(ListeningInteractionType.CharacterLetter);
        order.Add(ListeningInteractionType.PicturePrediction);
        if (hasDialogue) order.Add(ListeningInteractionType.WhoSaidIt);
        var chosen = order.Take(slots).ToList();
        var literals = 1 + (!hasDialogue && slots > chosen.Count ? 1 : 0);
        var items = chosen.Select(type => type == ListeningInteractionType.Checkpoint
            ? new ListeningPlanItem(type, literals) { Kind = CheckpointKind.Literal }
            : new ListeningPlanItem(type, 1)).ToArray();
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
        var middle = items.Where(x => !ListeningInteractionRules.IsEndOfStory(x.Type)).Sum(x => x.Count);
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
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.WhoSaidIt))
            assigned[item.Type] = [TakeFitting(b => blocks.Any(d => dialogue.Contains(d.BlockId) && d.EndOffset <= b.EndOffset))];
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.FillBlank))
            assigned[item.Type] = [TakeFitting(HasShortSentence)];
        foreach (var item in items.Where(x => x.Type is ListeningInteractionType.Checkpoint or ListeningInteractionType.CharacterLetter))
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
