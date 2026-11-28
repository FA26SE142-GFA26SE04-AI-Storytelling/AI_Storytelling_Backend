using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

/// <summary>Nộp câu trả lời của bé 8 tuổi: ba loại trắc nghiệm, nối nguyên nhân và chọn nhân vật đọc luân phiên.</summary>
public sealed class ChildListeningServiceAge8Tests
{
    private readonly ReadingHarness _h = new();
    private readonly ChildListeningService _sut;
    private readonly ReadingSession _session;
    private readonly StoryScene[] _scenes;

    public ChildListeningServiceAge8Tests()
    {
        _sut = new ChildListeningService(_h.UnitOfWork.Object);
        _session = _h.AddSession();
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 8;
        _scenes = Enumerable.Range(0, 3).Select(i =>
        {
            var scene = _h.AddScene(i, $"Scene {i}");
            scene.TextRangeStart = i * 20;
            scene.TextRangeEnd = i * 20 + 20;
            return scene;
        }).ToArray();
    }

    private ListeningInteraction Add(int id, T type, int sceneIndex, Action<ListeningInteraction>? setup = null)
    {
        var interaction = new ListeningInteraction
        {
            Id = id, StoryVersionId = ReadingHarness.Version, TargetAge = 8, Type = type, AnchorOffset = _scenes[sceneIndex].TextRangeEnd,
            StorySceneId = _scenes[sceneIndex].Id, MatchFeedback = "Giỏi quá!"
        };
        setup?.Invoke(interaction);
        _h.ListeningInteractions.Add(interaction);
        return interaction;
    }

    private void Option(int id, int interactionId, int order, string text, bool isMatch = false, ListeningOptionRole role = ListeningOptionRole.None,
        int? correctOrder = null, string? speaker = null, string? emotion = null, string? feedback = null) =>
        _h.ListeningOptions.Add(new ListeningInteractionOption
        {
            Id = id, ListeningInteractionId = interactionId, SortOrder = order, Text = text, IsMatch = isMatch, Role = role,
            CorrectOrder = correctOrder, Speaker = speaker, Emotion = emotion, Feedback = feedback
        });

    private Task<ListeningAttemptResultDto> Submit(int interactionId, SubmitListeningAttemptRequestDto request) =>
        _sut.SubmitAttemptAsync(ReadingHarness.Child, _session.Id, interactionId, request);

    // ---- Suy luận từ chi tiết ----
    private void Inference()
    {
        var interaction = Add(91, T.InferenceQuestion, 1, x =>
        {
            x.Prompt = "Vì sao Thỏ trốn?"; x.SourceQuote = "Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú."; x.Explanation = "Vì Thỏ run và nghe tiếng sói hú.";
        });
        Option(910, 91, 1, "Vì Thỏ sợ", true);
        Option(911, 91, 2, "Vì Thỏ buồn ngủ");
        Option(912, 91, 3, "Vì Thỏ đang chơi");
        Option(913, 91, 4, "run lẩy bẩy", role: ListeningOptionRole.Clue);
        Option(914, 91, 5, "nghe tiếng sói hú", role: ListeningOptionRole.Clue);
        Option(915, 91, 6, "Thỏ", role: ListeningOptionRole.Clue);
        _h.AddListeningMedia(interaction);
    }

    [Fact]
    public async Task Inference_RightFirstTime_PraisesExplainsAndShowsTheClues()
    {
        Inference();
        var result = await Submit(91, new() { OptionId = 910 });
        Assert.True(result.Matched);
        Assert.True(result.Completed);
        Assert.False(result.CanRetry);
        Assert.Equal("Giỏi quá!", result.Feedback);
        Assert.Equal("Vì Thỏ run và nghe tiếng sói hú.", result.Explanation);
        Assert.Equal(3, result.ClueSpans!.Count);
        Assert.All(result.ClueSpans!, span => Assert.True(span.End > span.Start));
        Assert.Null(result.CorrectOptionId);
    }

    [Fact]
    public async Task Inference_WrongFirst_ShowsTheCluesAndAllowsOneRetry_WithoutRevealingTheAnswer()
    {
        Inference();
        var first = await Submit(91, new() { OptionId = 911 });
        Assert.False(first.Matched);
        Assert.True(first.CanRetry);
        Assert.False(first.Completed);
        Assert.Equal(3, first.ClueSpans!.Count);
        Assert.Null(first.CorrectOptionId);
        Assert.Null(first.Explanation);
        Assert.Equal("Nhìn kỹ lại nhé.", first.Feedback);
        Assert.DoesNotContain("sai", first.Feedback!, StringComparison.OrdinalIgnoreCase);

        var second = await Submit(91, new() { OptionId = 910 });
        Assert.True(second.Matched);
        Assert.True(second.Completed);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    [Fact]
    public async Task Inference_WrongTwice_RevealsTheAnswerWithItsExplanation_AndResendAddsNoRow()
    {
        Inference();
        await Submit(91, new() { OptionId = 911 });
        var second = await Submit(91, new() { OptionId = 912 });
        Assert.True(second.Revealed);
        Assert.True(second.Completed);
        Assert.False(second.CanRetry);
        Assert.Equal(910, second.CorrectOptionId);
        Assert.Equal("Vì Thỏ run và nghe tiếng sói hú.", second.Explanation);

        var resent = await Submit(91, new() { OptionId = 910 });
        Assert.True(resent.Revealed);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    [Theory]
    [InlineData(913)]   // manh mối không phải đáp án
    [InlineData(999)]   // option không thuộc tương tác
    public async Task Inference_ClueOrForeignOption_IsBadRequest(int optionId)
    {
        Inference();
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(91, new() { OptionId = optionId }));
        Assert.Empty(_h.ListeningAttempts);
    }

    // ---- Đặt tên cho một cảnh ----
    private void SceneTitle()
    {
        var interaction = Add(92, T.SceneTitle, 1, x => x.Explanation = "Tiêu đề này nói về cả cảnh.");
        Option(920, 92, 1, "Thỏ và Sóc làm bạn", true, feedback: "Tiêu đề này nói về cả hai bạn.");
        Option(921, 92, 2, "Sóc cười vui vẻ", feedback: "Tiêu đề này chỉ nói về một chi tiết.");
        Option(922, 92, 3, "Tiếng sói hú", feedback: "Tiêu đề kia nói về tiếng sói.");
        _h.AddListeningMedia(interaction);
    }

    [Fact]
    public async Task SceneTitle_WrongTitle_SaysItOnlyTellsOneDetail_ThenRevealsWithTheExplanation()
    {
        SceneTitle();
        var first = await Submit(92, new() { OptionId = 921 });
        Assert.True(first.CanRetry);
        Assert.Equal("Tiêu đề này chỉ nói về một chi tiết.", first.Feedback);
        Assert.Null(first.Explanation);
        Assert.Null(first.ClueSpans);

        var second = await Submit(92, new() { OptionId = 922 });
        Assert.True(second.Revealed);
        Assert.Equal(920, second.CorrectOptionId);
        Assert.Equal("Tiêu đề này nói về cả cảnh.", second.Explanation);
        Assert.Equal("Tiêu đề này nói về cả hai bạn.", second.Feedback);
    }

    [Fact]
    public async Task SceneTitle_Right_PraisesAndExplains()
    {
        SceneTitle();
        var result = await Submit(92, new() { OptionId = 920 });
        Assert.True(result.Matched);
        Assert.Equal("Giỏi quá!", result.Feedback);
        Assert.Equal("Tiêu đề này nói về cả cảnh.", result.Explanation);
    }

    // ---- Bong bóng suy nghĩ ----
    private void ThoughtBubble()
    {
        var interaction = Add(93, T.ThoughtBubble, 0, x =>
        {
            x.Prompt = "Thỏ đang nghĩ gì?"; x.CharacterName = "Thỏ"; x.SourceStart = 5; x.SourceQuote = "Thỏ run lẩy bẩy.";
        });
        Option(930, 93, 1, "Mình sợ quá!", true, emotion: "lo lắng");
        Option(931, 93, 2, "Vui quá!", emotion: "vui");
        Option(932, 93, 3, "Mình đói!", emotion: "háo hức");
        _h.AddIllustration(_scenes[0], "scene-0.png").ValidationStatus = ValidationStatus.Passed;
        _h.AddListeningMedia(interaction);
    }

    [Fact]
    public async Task ThoughtBubble_WrongFirst_AsksToLookAtThePictureWithoutTheEmotion_RightShowsTheEmotion()
    {
        ThoughtBubble();
        var first = await Submit(93, new() { OptionId = 931 });
        Assert.True(first.CanRetry);
        Assert.Equal("Bé nhìn lại tranh nhé.", first.Feedback);
        Assert.Null(first.RevealEmotion);
        Assert.Null(first.CorrectOptionId);

        var second = await Submit(93, new() { OptionId = 930 });
        Assert.True(second.Matched);
        Assert.Equal("lo lắng", second.RevealEmotion);
    }

    [Fact]
    public async Task ThoughtBubble_WrongTwice_RevealsTheEmotionOfTheRightBubble()
    {
        ThoughtBubble();
        await Submit(93, new() { OptionId = 931 });
        var second = await Submit(93, new() { OptionId = 932 });
        Assert.True(second.Revealed);
        Assert.Equal(930, second.CorrectOptionId);
        Assert.Equal("lo lắng", second.RevealEmotion);
    }

    // ---- Nối nguyên nhân và kết quả ----
    private void CauseEffect()
    {
        var interaction = Add(94, T.CauseEffect, 2);
        Option(940, 94, 1, "Thỏ trú dưới gốc cây", role: ListeningOptionRole.Item, correctOrder: 1);
        Option(941, 94, 2, "Thỏ nấp sau gốc cây", role: ListeningOptionRole.Item, correctOrder: 2);
        Option(942, 94, 3, "Sói bỏ đi", role: ListeningOptionRole.Item, correctOrder: 3);
        Option(943, 94, 4, "Trời mưa to", role: ListeningOptionRole.Target, correctOrder: 1);
        Option(944, 94, 5, "Thỏ nghe tiếng sói hú", role: ListeningOptionRole.Target, correctOrder: 2);
        Option(945, 94, 6, "Mưa rơi rất to", role: ListeningOptionRole.Target, correctOrder: 3);
        _h.AddListeningMedia(interaction);
    }

    [Fact]
    public async Task CauseEffect_RightPair_IsMatchedButTheSetIsNotDoneYet()
    {
        CauseEffect();
        var result = await Submit(94, new() { OptionId = 940, TargetOptionId = 943 });
        Assert.True(result.Matched);
        Assert.False(result.Completed);
        Assert.Equal(943, result.MatchedCauseOptionId);
        Assert.Null(result.ChainSentences);
    }

    [Fact]
    public async Task CauseEffect_WrongPair_BouncesBackWithAGentleLine_AndTheSecondMissLinksTheRightCause()
    {
        CauseEffect();
        var first = await Submit(94, new() { OptionId = 940, TargetOptionId = 944 });
        Assert.False(first.Matched);
        Assert.True(first.CanRetry);
        Assert.Null(first.MatchedCauseOptionId);
        Assert.Equal("Bé thử lại nhé.", first.Feedback);

        var second = await Submit(94, new() { OptionId = 940, TargetOptionId = 945 });
        Assert.True(second.Revealed);
        Assert.False(second.CanRetry);
        Assert.Equal(943, second.MatchedCauseOptionId);
    }

    [Fact]
    public async Task CauseEffect_AllThreeDone_ReturnsTheThreeBecauseSoSentencesInStoryOrder()
    {
        CauseEffect();
        await Submit(94, new() { OptionId = 941, TargetOptionId = 944 });
        await Submit(94, new() { OptionId = 942, TargetOptionId = 944 });          // nối nhầm
        await Submit(94, new() { OptionId = 940, TargetOptionId = 943 });
        var last = await Submit(94, new() { OptionId = 942, TargetOptionId = 945 });
        Assert.True(last.Completed);
        Assert.Equal("Giỏi quá!", last.Feedback);
        Assert.Equal(
            ["Vì trời mưa to nên thỏ trú dưới gốc cây.", "Vì thỏ nghe tiếng sói hú nên thỏ nấp sau gốc cây.", "Vì mưa rơi rất to nên sói bỏ đi."],
            last.ChainSentences!.Select(x => x.Text));
        Assert.Equal([940, 941, 942], last.ChainSentences!.Select(x => x.EffectOptionId));
        Assert.All(last.ChainSentences!, sentence => Assert.NotNull(sentence.AudioUrl));
    }

    [Fact]
    public async Task CauseEffect_ResendingADoneCard_ReturnsTheOldResultWithoutANewRow()
    {
        CauseEffect();
        await Submit(94, new() { OptionId = 940, TargetOptionId = 943 });
        var again = await Submit(94, new() { OptionId = 940, TargetOptionId = 944 });
        Assert.True(again.Matched);
        Assert.Equal(943, again.MatchedCauseOptionId);
        Assert.Single(_h.ListeningAttempts);
    }

    [Theory]
    [InlineData(943, 940)]   // thẻ lí do làm thẻ kết quả
    [InlineData(940, 941)]   // thả vào thẻ kết quả khác
    [InlineData(940, null)]
    [InlineData(null, 943)]
    [InlineData(999, 943)]
    public async Task CauseEffect_WrongCardRoles_AreBadRequest(int? effect, int? cause)
    {
        CauseEffect();
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(94, new() { OptionId = effect, TargetOptionId = cause }));
        Assert.Empty(_h.ListeningAttempts);
    }

    // ---- Đọc luân phiên: chọn nhân vật ----
    private void Reading()
    {
        var interaction = Add(95, T.AlternateReading, 0);
        Option(950, 95, 1, "Cậu đang làm gì đấy?", correctOrder: 1, speaker: "Thỏ");
        Option(951, 95, 2, "Mình đang tìm hạt dẻ.", correctOrder: 2, speaker: "Sóc");
        _h.AddListeningMedia(interaction);
    }

    [Fact]
    public async Task AlternateReading_ChoosingACharacter_IsRecordedOnceWithoutGrading()
    {
        Reading();
        var result = await Submit(95, new() { Speaker = " sóc " });
        Assert.Equal("Sóc", result.Speaker);
        Assert.True(result.Completed);
        Assert.Null(result.Matched);
        var attempt = Assert.Single(_h.ListeningAttempts);
        Assert.Equal("Sóc", attempt.AnswerText);
        Assert.Null(attempt.IsMatch);

        var again = await Submit(95, new() { Speaker = "Thỏ" });   // không đổi nhân vật sau khi đã chọn
        Assert.Equal("Sóc", again.Speaker);
        Assert.Single(_h.ListeningAttempts);
    }

    [Theory]
    [InlineData("Gấu")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AlternateReading_UnknownCharacter_IsBadRequest(string? speaker)
    {
        Reading();
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(95, new() { Speaker = speaker }));
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task Age8Interactions_AreNotReachableByAChildOfAnotherAge()
    {
        Inference();
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        await Assert.ThrowsAsync<NotFoundException>(() => Submit(91, new() { OptionId = 910 }));
    }
}
