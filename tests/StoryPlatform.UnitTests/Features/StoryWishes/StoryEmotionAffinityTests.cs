using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class StoryEmotionAffinityTests
{
    private static IReadOnlyCollection<string> E(params string[] codes) => codes;

    private static (StoryFeeling Feeling, IReadOnlyCollection<string> Emotions) Read(StoryFeeling feeling, params string[] codes) =>
        (feeling, codes);

    // ---- StoryTags ----

    [Fact]
    public void StoryTags_FewerThanThreeReflections_HasNoProfile()
    {
        Assert.Empty(StoryEmotionAffinity.StoryTags(new[] { E("Scared"), E("Scared") }));
    }

    [Fact]
    public void StoryTags_KeepsEmotionsChosenInAtLeastFortyPercent()
    {
        // 5 lượt: Scared 2/5 = 40% → gắn; Happy, Sad, Touched, Funny 1/5 = 20% → không
        var tags = StoryEmotionAffinity.StoryTags(new[]
        {
            E("Scared", "Happy"), E("Scared"), E("Sad"), E("Touched"), E("Funny")
        });

        Assert.Equal(new[] { StoryEmotion.Scared }, tags.OrderBy(x => x));
    }

    [Fact]
    public void StoryTags_BelowFortyPercent_IsNotATag()
    {
        // 3 lượt, mỗi cảm xúc 1/3 ≈ 33%
        Assert.Empty(StoryEmotionAffinity.StoryTags(new[] { E("Sad"), E("Happy"), E("Funny") }));
    }

    [Fact]
    public void StoryTags_IgnoresUnknownCodes_EmptyRows_AndDuplicatesInARow()
    {
        // Dòng rỗng và dòng chỉ có mã lạ không tính vào tổng → tổng = 3.
        // "happy" + "Happy" trong cùng một dòng tính 1 → Happy 2/3 ≈ 67% gắn; Funny 1/3 không.
        var tags = StoryEmotionAffinity.StoryTags(new[]
        {
            E("happy", "Happy"), E("Happy"), E("Funny"), E(), E("Angry")
        });

        Assert.Equal(new[] { StoryEmotion.Happy }, tags.OrderBy(x => x));
    }

    // ---- ChildPreference ----

    [Fact]
    public void ChildPreference_LikedAndDislikedReadsDecideTheSide()
    {
        var preference = StoryEmotionAffinity.ChildPreference(new[]
        {
            Read(StoryFeeling.VeryLike, "Funny", "Happy"),
            Read(StoryFeeling.Like, "Funny"),
            Read(StoryFeeling.Dislike, "Scared", "Happy"),
            Read(StoryFeeling.Neutral, "Sad")
        });

        // Funny +2 → thích; Happy +1 −1 = 0 → trung lập; Scared −1 → né; Sad chỉ ở Neutral → trung lập.
        Assert.Equal(new[] { StoryEmotion.Funny }, preference.Liked.OrderBy(x => x));
        Assert.Equal(new[] { StoryEmotion.Scared }, preference.Avoided.OrderBy(x => x));
    }

    [Fact]
    public void ChildPreference_OnlyTheTenNewestReadsCount()
    {
        // 10 lần gần nhất đều Neutral; lần thứ 11 (cũ nhất) VeryLike không được tính.
        var reads = Enumerable.Range(0, 10).Select(_ => Read(StoryFeeling.Neutral, "Happy"))
            .Append(Read(StoryFeeling.VeryLike, "Happy"));

        var preference = StoryEmotionAffinity.ChildPreference(reads);

        Assert.Empty(preference.Liked);
        Assert.Empty(preference.Avoided);
    }

    [Fact]
    public void ChildPreference_ReadsWithoutAValidEmotion_DoNotTakeASlot()
    {
        var reads = Enumerable.Range(0, 10).Select(_ => Read(StoryFeeling.Like, "Angry"))
            .Append(Read(StoryFeeling.Like, "Touched"));

        Assert.Equal(new[] { StoryEmotion.Touched }, StoryEmotionAffinity.ChildPreference(reads).Liked.OrderBy(x => x));
    }

    [Fact]
    public void ChildPreference_NoReads_IsNone()
    {
        var preference = StoryEmotionAffinity.ChildPreference(Array.Empty<(StoryFeeling, IReadOnlyCollection<string>)>());

        Assert.Empty(preference.Liked);
        Assert.Empty(preference.Avoided);
    }

    // ---- Points ----

    [Theory]
    [InlineData(new[] { StoryEmotion.Scared }, -2)]
    [InlineData(new[] { StoryEmotion.Funny }, 1)]
    [InlineData(new[] { StoryEmotion.Funny, StoryEmotion.Scared }, -2)]
    [InlineData(new[] { StoryEmotion.Sad }, 0)]
    [InlineData(new StoryEmotion[0], 0)]
    public void Points_AvoidedBeatsLiked(StoryEmotion[] tags, int expected)
    {
        var preference = new EmotionPreference(
            new HashSet<StoryEmotion> { StoryEmotion.Funny },
            new HashSet<StoryEmotion> { StoryEmotion.Scared });

        Assert.Equal(expected, StoryEmotionAffinity.Points(tags.ToHashSet(), preference));
    }

    [Fact]
    public void Points_WithoutPreference_IsZero()
    {
        Assert.Equal(0, StoryEmotionAffinity.Points(
            new HashSet<StoryEmotion> { StoryEmotion.Scared }, EmotionPreference.None));
    }
}
