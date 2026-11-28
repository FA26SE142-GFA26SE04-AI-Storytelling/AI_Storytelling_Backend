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
        7 => CreateAge7(blocks) with { TargetAge = 7 },
        8 => CreateAge8(blocks) with { TargetAge = 8 },
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
        return new(slots, AssignZones(items, blocks, dialogue, 6), dialogue);
    }

    private static ListeningInteractionPlan CreateAge7(IReadOnlyList<StoryTextBlock> blocks)
    {
        var slots = Math.Max(0, blocks.Count - 1);
        if (slots == 0) return ListeningInteractionPlan.Empty;
        ListeningInteractionType[] priority = [ListeningInteractionType.PictureSearch, ListeningInteractionType.TrueFalse,
            ListeningInteractionType.SentenceBuilder, ListeningInteractionType.FollowAction, ListeningInteractionType.EventOrdering];
        var items = priority.Take(slots).Select(type => new ListeningPlanItem(type, 1)).ToArray();
        var dialogue = FindDialogueBlockIds(blocks);
        return new(slots, AssignZones(items, blocks, dialogue, 7), dialogue);
    }

    private static ListeningInteractionPlan CreateAge8(IReadOnlyList<StoryTextBlock> blocks)
    {
        var slots = Math.Max(0, blocks.Count - 1);
        if (slots == 0) return ListeningInteractionPlan.Empty;
        var dialogue = FindDialogueBlockIds(blocks);
        // Không có thoại thì không đọc luân phiên được: thay bằng câu suy luận thứ hai.
        var order = new List<ListeningPlanItem>();
        if (dialogue.Count > 0) order.Add(new(ListeningInteractionType.AlternateReading, 1));
        order.Add(new(ListeningInteractionType.InferenceQuestion, dialogue.Count > 0 ? 1 : Math.Min(2, slots)));
        order.Add(new(ListeningInteractionType.SceneTitle, 1));
        order.Add(new(ListeningInteractionType.ThoughtBubble, 1));
        order.Add(new(ListeningInteractionType.CauseEffect, 1));
        var items = new List<ListeningPlanItem>();
        var used = 0;
        foreach (var item in order)
        {
            if (used + item.Count > slots) break;
            items.Add(item);
            used += item.Count;
        }
        return new(slots, AssignZones(items, blocks, dialogue, 8), dialogue);
    }

    /// <summary>
    /// Chia P1..P(S) (S = số chỗ trống) thành M vùng liền nhau gần bằng nhau, M = số tương tác giữa truyện:
    /// vùng k = đoạn thứ ⌊k·S/M⌋+1 tới ⌊(k+1)·S/M⌋. Loại kén chỗ chọn trước, ưu tiên vùng gần giữa truyện:
    /// RolePlay lấy vùng có thoại, FillBlank lấy vùng có câu đủ ngắn. Checkpoint lấy các vùng còn lại từ đầu truyện,
    /// PicturePrediction lấy vùng muộn nhất để đáp án gần cuối truyện.
    /// </summary>
    private static IReadOnlyList<ListeningPlanItem> AssignZones(
        IReadOnlyList<ListeningPlanItem> items, IReadOnlyList<StoryTextBlock> blocks, IReadOnlyList<string> dialogue, int targetAge)
    {
        var slots = blocks.Count - 1;
        var middle = items.Where(x => !ListeningInteractionRules.IsEndOfStory(x.Type, targetAge)).Sum(x => x.Count);
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
        IReadOnlyList<string> TakeLatest() => Claim(free[^1]);

        var assigned = new Dictionary<ListeningInteractionType, IReadOnlyList<IReadOnlyList<string>>>();
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.RolePlay))
            assigned[item.Type] = [TakeFitting(b => dialogue.Contains(b.BlockId))];
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.WhoSaidIt))
            assigned[item.Type] = [TakeFitting(b => blocks.Any(d => dialogue.Contains(d.BlockId) && d.EndOffset <= b.EndOffset))];
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.FillBlank))
            assigned[item.Type] = [TakeFitting(HasShortSentence)];
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.SentenceBuilder))
            assigned[item.Type] = [TakeFitting(HasShortSentence)];
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.EventOrdering && targetAge == 7))
            assigned[item.Type] = [TakeLatest()];
        foreach (var item in items.Where(x => x.Type is ListeningInteractionType.Checkpoint or ListeningInteractionType.CharacterLetter))
            assigned[item.Type] = Enumerable.Range(0, item.Count).Select(_ => TakeEarliest()).ToArray();
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.PicturePrediction))
            assigned[item.Type] = [TakeEarliest()];
        foreach (var item in items.Where(x => x.Type is ListeningInteractionType.PictureSearch or ListeningInteractionType.TrueFalse or ListeningInteractionType.FollowAction))
            assigned[item.Type] = [TakeEarliest()];
        // Tuổi 8: đọc luân phiên cần vùng có thoại; bong bóng suy nghĩ lấy vùng muộn nhất (cao trào cảm xúc); còn lại từ đầu truyện.
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.AlternateReading))
            assigned[item.Type] = [TakeFitting(b => dialogue.Contains(b.BlockId))];
        foreach (var item in items.Where(x => x.Type == ListeningInteractionType.ThoughtBubble))
            assigned[item.Type] = [TakeLatest()];
        foreach (var item in items.Where(x => x.Type is ListeningInteractionType.InferenceQuestion or ListeningInteractionType.CauseEffect
            or ListeningInteractionType.SceneTitle))
            assigned[item.Type] = Enumerable.Range(0, item.Count).Select(_ => TakeEarliest()).ToArray();

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
