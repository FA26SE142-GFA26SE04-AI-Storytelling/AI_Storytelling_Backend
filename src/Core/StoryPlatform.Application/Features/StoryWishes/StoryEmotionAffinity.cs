using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryWishes;

/// <summary>Cảm xúc bé thích và bé né; hai tập không giao nhau.</summary>
public sealed record EmotionPreference(IReadOnlySet<StoryEmotion> Liked, IReadOnlySet<StoryEmotion> Avoided)
{
    public static readonly EmotionPreference None = new(new HashSet<StoryEmotion>(), new HashSet<StoryEmotion>());
}

/// <summary>
/// Điểm cảm xúc cho gợi ý theo điều ước: hồ sơ cảm xúc của truyện (tổng hợp cảm nhận của các bé đã đọc),
/// sở thích cảm xúc của bé (cảm xúc + mức thích của chính bé). Tính toán thuần, không đọc DB, không AI.
/// </summary>
public static class StoryEmotionAffinity
{
    /// <summary>Cảm xúc truyện hay gây ra; rỗng khi chưa đủ <see cref="StoryWishSuggestionPolicy.MinStoryReflections"/> lượt.</summary>
    public static IReadOnlySet<StoryEmotion> StoryTags(IEnumerable<IReadOnlyCollection<string>> reflectionEmotions)
    {
        var rows = reflectionEmotions.Select(Parse).Where(set => set.Count > 0).ToList();
        if (rows.Count < StoryWishSuggestionPolicy.MinStoryReflections)
        {
            return new HashSet<StoryEmotion>();
        }

        return rows.SelectMany(set => set)
            .GroupBy(emotion => emotion)
            .Where(group => group.Count() * 100 >= StoryWishSuggestionPolicy.EmotionTagSharePercent * rows.Count)
            .Select(group => group.Key)
            .ToHashSet();
    }

    /// <summary>Sở thích từ các lần đọc xong, mới nhất trước; chỉ lấy lần có cảm xúc hợp lệ, tối đa 10 lần.</summary>
    public static EmotionPreference ChildPreference(
        IEnumerable<(StoryFeeling Feeling, IReadOnlyCollection<string> Emotions)> newestFirst)
    {
        var net = new Dictionary<StoryEmotion, int>();
        var reads = newestFirst
            .Select(read => (read.Feeling, Emotions: Parse(read.Emotions)))
            .Where(read => read.Emotions.Count > 0)
            .Take(StoryWishSuggestionPolicy.RecentReadsForEmotion);
        foreach (var (feeling, emotions) in reads)
        {
            var delta = feeling switch
            {
                StoryFeeling.VeryLike or StoryFeeling.Like => 1,
                StoryFeeling.Dislike => -1,
                _ => 0
            };
            if (delta == 0)
            {
                continue;
            }

            foreach (var emotion in emotions)
            {
                net[emotion] = net.GetValueOrDefault(emotion) + delta;
            }
        }

        return new EmotionPreference(
            net.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToHashSet(),
            net.Where(pair => pair.Value < 0).Select(pair => pair.Key).ToHashSet());
    }

    /// <summary>Điểm xếp hạng: né thắng thích; không hồ sơ hoặc không sở thích → 0.</summary>
    public static int Points(IReadOnlySet<StoryEmotion> storyTags, EmotionPreference preference)
    {
        if (storyTags.Overlaps(preference.Avoided))
        {
            return StoryWishSuggestionPolicy.AvoidedEmotionPoints;
        }

        return storyTags.Overlaps(preference.Liked) ? StoryWishSuggestionPolicy.LikedEmotionPoints : 0;
    }

    private static HashSet<StoryEmotion> Parse(IReadOnlyCollection<string> codes) =>
        codes.Select(code => StoryEmotionLabels.TryParse(code, out var emotion) ? emotion : (StoryEmotion?)null)
            .Where(emotion => emotion.HasValue)
            .Select(emotion => emotion!.Value)
            .ToHashSet();
}
