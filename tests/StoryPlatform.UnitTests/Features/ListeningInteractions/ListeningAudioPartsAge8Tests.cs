using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningAudioPartsAge8Tests
{
    private static ListeningInteractionOption O(int id, int interaction, string text, ListeningOptionRole role = ListeningOptionRole.None,
        int? order = null, string? feedback = null) =>
        new() { Id = id, ListeningInteractionId = interaction, SortOrder = id, Text = text, Role = role, CorrectOrder = order, Feedback = feedback };

    [Fact]
    public void AlternateReading_ReadsEveryLineAndTheFixedGuidance()
    {
        var interaction = new ListeningInteraction { Id = 1, Type = T.AlternateReading, TargetAge = 8, MatchFeedback = "Con đọc hay quá!" };
        var parts = ListeningAudioParts.For(interaction, [O(10, 1, "Cậu đang làm gì đấy?", order: 1), O(11, 1, "Mình đang tìm hạt dẻ.", order: 2)]);
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Instruction && p.Text.Contains("cùng đọc"));
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Match);
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Unclear);
        Assert.Equal([10, 11], parts.Where(p => p.OptionId is not null && p.PartKey == ListeningAudioParts.Text).Select(p => p.OptionId!.Value));
    }

    [Fact]
    public void Inference_ReadsPassageQuestionExplanationAndAnswers_ButNotTheClues()
    {
        var interaction = new ListeningInteraction { Id = 2, Type = T.InferenceQuestion, TargetAge = 8, SourceQuote = "Thỏ run. Thỏ sợ.",
            Prompt = "Vì sao Thỏ trốn?", Explanation = "Thỏ run nên Thỏ sợ.", MatchFeedback = "Giỏi quá!" };
        var parts = ListeningAudioParts.For(interaction, [O(20, 2, "Vì Thỏ sợ"), O(21, 2, "Vì Thỏ buồn ngủ"), O(22, 2, "run", ListeningOptionRole.Clue)]);
        Assert.Contains(parts, p => p.OptionId is null && p.PartKey == ListeningAudioParts.Text && p.Text == "Thỏ run. Thỏ sợ.");
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Prompt);
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Explanation);
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.RetryLookClose && p.Text == "Nhìn kỹ lại nhé.");
        Assert.Equal([20, 21], parts.Where(p => p.OptionId is not null).Select(p => p.OptionId!.Value));
    }

    [Fact]
    public void CauseEffect_ReadsEachCardAndAVyNenSentencePerEffect()
    {
        var interaction = new ListeningInteraction { Id = 3, Type = T.CauseEffect, TargetAge = 8, MatchFeedback = "Giỏi quá!" };
        var options = new[]
        {
            O(30, 3, "Thỏ trú dưới gốc cây", ListeningOptionRole.Item, 1), O(31, 3, "Sói bỏ đi", ListeningOptionRole.Item, 2),
            O(32, 3, "Trời mưa to", ListeningOptionRole.Target, 1), O(33, 3, "Mưa rơi rất to", ListeningOptionRole.Target, 2)
        };
        var parts = ListeningAudioParts.For(interaction, options);
        Assert.Equal(4, parts.Count(p => p.OptionId is not null && p.PartKey == ListeningAudioParts.Text));
        Assert.Equal("Vì trời mưa to nên thỏ trú dưới gốc cây.", parts.Single(p => p.OptionId == 30 && p.PartKey == ListeningAudioParts.Chain).Text);
        Assert.Equal("Vì mưa rơi rất to nên sói bỏ đi.", parts.Single(p => p.OptionId == 31 && p.PartKey == ListeningAudioParts.Chain).Text);
        Assert.DoesNotContain(parts, p => p.PartKey == ListeningAudioParts.Chain && p.OptionId is 32 or 33);
    }

    [Fact]
    public void SceneTitle_ReadsTitlesWithTheirFeedback_AndThoughtBubbleTheQuestionAndAFixedLine()
    {
        var title = new ListeningInteraction { Id = 4, Type = T.SceneTitle, TargetAge = 8, Explanation = "Cả cảnh.", MatchFeedback = "Giỏi quá!" };
        var parts = ListeningAudioParts.For(title, [O(40, 4, "Thỏ và Sóc làm bạn", feedback: "Nói về cả hai bạn.")]);
        Assert.Contains(parts, p => p.OptionId == 40 && p.PartKey == ListeningAudioParts.Feedback);
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Explanation);

        var bubble = new ListeningInteraction { Id = 5, Type = T.ThoughtBubble, TargetAge = 8, Prompt = "Thỏ đang nghĩ gì?", MatchFeedback = "Giỏi quá!" };
        var bubbleParts = ListeningAudioParts.For(bubble, [O(50, 5, "Mình sợ quá!")]);
        Assert.Contains(bubbleParts, p => p.PartKey == ListeningAudioParts.Prompt);
        Assert.Contains(bubbleParts, p => p.PartKey == ListeningAudioParts.RetryLook);
        Assert.Contains(bubbleParts, p => p.OptionId == 50 && p.PartKey == ListeningAudioParts.Text);
    }

    [Theory]
    [InlineData(T.AlternateReading, null, "text", true)]
    [InlineData(T.AlternateReading, 10, "text", true)]       // lượt thoại có mốc từng chữ
    [InlineData(T.AlternateReading, 10, "feedback", false)]
    [InlineData(T.InferenceQuestion, null, "text", true)]
    [InlineData(T.InferenceQuestion, null, "prompt", true)]
    [InlineData(T.InferenceQuestion, null, "explanation", false)]
    [InlineData(T.InferenceQuestion, 20, "text", false)]     // đáp án không cần mốc
    [InlineData(T.SentenceBuilder, null, "sentence:2", true)]
    [InlineData(T.SentenceBuilder, 10, "text", false)]
    [InlineData(T.CauseEffect, 30, "chain", false)]
    public void NeedsWordTimings_OnlyForTextTheChildFollowsByEye(T type, int? optionId, string partKey, bool expected) =>
        Assert.Equal(expected, ListeningAudioParts.NeedsWordTimings(type, optionId, partKey));

    [Fact]
    public void ThoughtBubble_NeedsAUsablePictureOfItsScene()
    {
        var interaction = new ListeningInteraction { Id = 6, Type = T.ThoughtBubble, TargetAge = 8, StorySceneId = 600, Prompt = "Thỏ đang nghĩ gì?", MatchFeedback = "Giỏi quá!" };
        var options = new[] { O(60, 6, "Mình sợ quá!") };
        var audio = ListeningAudioParts.For(interaction, options).Select((p, i) => new MediaAsset
        {
            Id = 100 + i, Type = MediaType.ListeningAudio, ListeningInteractionId = 6, ListeningInteractionOptionId = p.OptionId,
            AudioPartKey = p.PartKey, Status = MediaStatus.Ready, ValidationStatus = ValidationStatus.Passed, IsCurrent = true, Url = $"a{i}.mp3"
        }).ToList();
        Assert.Equal(["6:i:picture"], ListeningMediaRequirements.Missing(interaction, options, audio));

        audio.Add(new MediaAsset { Id = 200, Type = MediaType.Illustration, StorySceneId = 600, Status = MediaStatus.Ready,
            ValidationStatus = ValidationStatus.Passed, IsCurrent = true, Url = "scene.png" });
        Assert.Empty(ListeningMediaRequirements.Missing(interaction, options, audio));
    }
}
