using System.Text.Json;
using Moq;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

/// <summary>Nội dung tương tác của bé 8 tuổi: đúng bộ, không lộ đáp án, ẩn khi thiếu media.</summary>
public sealed class ChildListeningContentAge8Tests
{
    private readonly ReadingHarness _h = new();
    private readonly ChildReadingService _sut;
    private readonly ReadingSession _session;
    private readonly StoryScene[] _scenes;

    public ChildListeningContentAge8Tests()
    {
        _sut = new ChildReadingService(_h.UnitOfWork.Object, new Mock<IChildRoadmapEligibility>().Object,
            new Mock<IChildRoadmapProgressRecorder>().Object, new Mock<IChildRewardService>().Object);
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
            Id = id, StoryVersionId = ReadingHarness.Version, TargetAge = 8, Type = type,
            AnchorOffset = _scenes[sceneIndex].TextRangeEnd, StorySceneId = _scenes[sceneIndex].Id, MatchFeedback = "Giỏi quá!"
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
            CorrectOrder = correctOrder, Speaker = speaker, Emotion = emotion, Feedback = feedback, SourceQuote = $"câu {id}"
        });

    private Task<ReadingContentDto> Content() => _sut.GetContentAsync(ReadingHarness.Child, _session.Id);

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    // ---- Đọc luân phiên ----
    private ListeningInteraction Reading(bool withAudio = true)
    {
        var interaction = Add(81, T.AlternateReading, 0);
        Option(810, 81, 1, "Cậu đang làm gì đấy?", correctOrder: 1, speaker: "Thỏ");
        Option(811, 81, 2, "Mình đang tìm hạt dẻ.", correctOrder: 2, speaker: "Sóc");
        Option(812, 81, 3, "Mình giúp cậu nhé!", correctOrder: 3, speaker: "Thỏ");
        if (withAudio) _h.AddListeningMedia(interaction);
        return interaction;
    }

    [Fact]
    public async Task AlternateReading_ShowsTwoCharactersAndTheLinesInStoryOrder()
    {
        Reading();
        var dto = (await Content()).Scenes[0].Interaction!;
        Assert.Equal("AlternateReading", dto.Type);
        Assert.Equal(["Thỏ", "Sóc"], dto.Characters);
        Assert.Equal([810, 811, 812], dto.Lines!.Select(l => l.Id));
        Assert.Equal(["Thỏ", "Sóc", "Thỏ"], dto.Lines!.Select(l => l.Speaker));
        Assert.All(dto.Lines!, line => Assert.NotNull(line.AudioUrl));
        Assert.Equal(2, dto.MaxVoiceRetries);
        Assert.Equal(8, dto.TargetAge);
        Assert.NotNull(dto.Instruction);
    }

    [Fact]
    public async Task AlternateReading_LineWordTimings_AreSentWhenAvailable()
    {
        Reading();
        _h.Assets.Single(x => x.ListeningInteractionOptionId == 811 && x.AudioPartKey == ListeningAudioParts.Text).WordTimings = "[{\"word\":\"Mình\",\"start\":0,\"end\":0.3}]";
        var lines = (await Content()).Scenes[0].Interaction!.Lines!;
        Assert.Null(lines[0].WordTimings);
        Assert.Contains("Mình", lines[1].WordTimings);
    }

    [Fact]
    public async Task AlternateReading_WithoutAnyLineAudio_IsHidden()
    {
        var interaction = Reading();
        _h.Assets.RemoveAll(x => x.ListeningInteractionOptionId == 811 && x.AudioPartKey == ListeningAudioParts.Text);
        Assert.Null((await Content()).Scenes[0].Interaction);
        Assert.NotNull(interaction);
    }

    // ---- Suy luận từ chi tiết ----
    private ListeningInteraction Inference(string passage = "Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú.")
    {
        var interaction = Add(82, T.InferenceQuestion, 1, x =>
        {
            x.Prompt = "Vì sao Thỏ trốn?"; x.SourceQuote = passage; x.Explanation = "Vì Thỏ run và nghe tiếng sói hú.";
        });
        Option(820, 82, 1, "Vì Thỏ sợ", true);
        Option(821, 82, 2, "Vì Thỏ buồn ngủ");
        Option(822, 82, 3, "Vì Thỏ đang chơi");
        Option(823, 82, 4, "run lẩy bẩy", role: ListeningOptionRole.Clue);
        Option(824, 82, 5, "nghe tiếng sói hú", role: ListeningOptionRole.Clue);
        Option(825, 82, 6, "Thỏ", role: ListeningOptionRole.Clue);
        _h.AddListeningMedia(interaction);
        return interaction;
    }

    [Fact]
    public async Task Inference_ShowsPassageAndThreeAnswers_ButNeverCluesOrTheExplanation()
    {
        Inference();
        var dto = (await Content()).Scenes[1].Interaction!;
        Assert.Equal("InferenceQuestion", dto.Type);
        Assert.Equal("Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú.", dto.Passage);
        Assert.Equal(3, dto.Options.Count);
        Assert.Equal(2, dto.MaxChoices);
        Assert.NotNull(dto.AudioUrls.GetValueOrDefault(ListeningAudioParts.Text));
        Assert.NotNull(dto.AudioUrls.GetValueOrDefault(ListeningAudioParts.Prompt));
        var json = Json(dto);
        Assert.DoesNotContain("run lẩy bẩy\"", json.Replace("Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú.", ""));
        Assert.DoesNotContain("Vì Thỏ run và nghe", json);
        Assert.DoesNotContain("isMatch", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inference_WhenAClueIsNoLongerInThePassage_IsHidden()
    {
        Inference("Thỏ run lẩy bẩy. Thỏ nghe tiếng chuông.");
        Assert.Null((await Content()).Scenes[1].Interaction);
    }

    // ---- Nối nguyên nhân và kết quả ----
    private ListeningInteraction CauseEffect()
    {
        var interaction = Add(83, T.CauseEffect, 2);
        Option(830, 83, 1, "Thỏ trú dưới gốc cây", role: ListeningOptionRole.Item, correctOrder: 1);
        Option(831, 83, 2, "Thỏ nấp sau gốc cây", role: ListeningOptionRole.Item, correctOrder: 2);
        Option(832, 83, 3, "Sói bỏ đi", role: ListeningOptionRole.Item, correctOrder: 3);
        Option(833, 83, 4, "Trời mưa to", role: ListeningOptionRole.Target, correctOrder: 1);
        Option(834, 83, 5, "Thỏ nghe tiếng sói hú", role: ListeningOptionRole.Target, correctOrder: 2);
        Option(835, 83, 6, "Mưa rơi rất to", role: ListeningOptionRole.Target, correctOrder: 3);
        _h.AddListeningMedia(interaction);
        return interaction;
    }

    [Fact]
    public async Task CauseEffect_ShowsShuffledEffectsAndCauses_WithoutPairNumbers()
    {
        CauseEffect();
        var first = (await Content()).Scenes[2].Interaction!;
        var again = (await Content()).Scenes[2].Interaction!;
        Assert.Equal("CauseEffect", first.Type);
        Assert.Equal([830, 831, 832], first.Effects!.Select(x => x.Id).Order());
        Assert.Equal([833, 834, 835], first.Causes!.Select(x => x.Id).Order());
        Assert.Equal(first.Effects!.Select(x => x.Id), again.Effects!.Select(x => x.Id));   // xáo cố định theo phiên
        Assert.All(first.Effects!.Concat(first.Causes!), card => Assert.NotNull(card.AudioUrl));
        Assert.Equal(2, first.MaxChoices);
        var json = Json(first);
        Assert.DoesNotContain("correctOrder", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pair", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CauseEffect_IsAttemptedOnlyWhenEveryEffectIsDone()
    {
        var interaction = CauseEffect();
        var options = _h.ListeningOptions.Where(x => x.ListeningInteractionId == 83).ToList();
        ListeningInteractionAttempt Try(int effect, bool match) => new() { ListeningInteractionId = 83, SelectedOptionId = effect, IsMatch = match };

        Assert.False(ChildListeningContent.IsAttempted(interaction, [Try(830, true), Try(831, true)], options));
        Assert.True(ChildListeningContent.IsAttempted(interaction, [Try(830, true), Try(831, true), Try(832, true)], options));
        // Thẻ sai hai lần cũng coi là xong (hệ thống nối sẵn).
        Assert.True(ChildListeningContent.IsAttempted(interaction, [Try(830, true), Try(831, true), Try(832, false), Try(832, false)], options));
        Assert.False(ChildListeningContent.IsAttempted(interaction, [Try(830, true), Try(831, true), Try(832, false)], options));
    }

    // ---- Đặt tên cho một cảnh ----
    private ListeningInteraction SceneTitle()
    {
        var interaction = Add(84, T.SceneTitle, 1, x => x.Explanation = "Tiêu đề này nói về cả cảnh.");
        Option(840, 84, 1, "Thỏ và Sóc làm bạn", true, feedback: "Tiêu đề này nói về cả hai bạn.");
        Option(841, 84, 2, "Sóc cười vui vẻ", feedback: "Tiêu đề này chỉ nói về một chi tiết.");
        Option(842, 84, 3, "Tiếng sói hú xa xa", feedback: "Tiêu đề này chỉ nói về một chi tiết.");
        _h.AddListeningMedia(interaction);
        return interaction;
    }

    [Fact]
    public async Task SceneTitle_ShowsThreeTitles_WithoutFeedbackAnswerOrExplanation()
    {
        SceneTitle();
        var picture = _h.AddIllustration(_scenes[1], "scene-1.png");
        picture.ValidationStatus = ValidationStatus.Passed;
        var dto = (await Content()).Scenes[1].Interaction!;
        Assert.Equal("SceneTitle", dto.Type);
        Assert.Equal(3, dto.Options.Count);
        Assert.All(dto.Options, option => Assert.Null(option.Feedback));
        Assert.Equal("scene-1.png", dto.SceneImageUrl);
        var json = Json(dto);
        Assert.DoesNotContain("chỉ nói về một chi tiết", json);
        Assert.DoesNotContain("nói về cả cảnh", json);
    }

    [Fact]
    public async Task SceneTitle_IsShownEvenWithoutAPicture()
    {
        SceneTitle();
        var dto = (await Content()).Scenes[1].Interaction!;
        Assert.Null(dto.SceneImageUrl);
        Assert.Equal(3, dto.Options.Count);
    }

    // ---- Bong bóng suy nghĩ ----
    private ListeningInteraction ThoughtBubble(bool picture = true)
    {
        var interaction = Add(85, T.ThoughtBubble, 0, x =>
        {
            x.Prompt = "Thỏ đang nghĩ gì?"; x.CharacterName = "Thỏ"; x.SourceStart = 5; x.SourceQuote = "Thỏ run lẩy bẩy.";
        });
        Option(850, 85, 1, "Mình sợ quá, phải nấp thôi!", true, emotion: "lo lắng");
        Option(851, 85, 2, "Hôm nay vui quá!", emotion: "tự hào");
        Option(852, 85, 3, "Mình muốn ăn cà rốt.", emotion: "háo hức");
        if (picture)
        {
            var asset = _h.AddIllustration(_scenes[0], "scene-0.png");
            asset.ValidationStatus = ValidationStatus.Passed;
        }
        _h.AddListeningMedia(interaction);
        return interaction;
    }

    [Fact]
    public async Task ThoughtBubble_ShowsThePictureAndThreeBubbles_WithoutEmotionsOrTheAnswer()
    {
        ThoughtBubble();
        var dto = (await Content()).Scenes[0].Interaction!;
        Assert.Equal("ThoughtBubble", dto.Type);
        Assert.Equal("Thỏ", dto.CharacterName);
        Assert.Equal("scene-0.png", dto.SceneImageUrl);
        Assert.Equal(3, dto.Options.Count);
        var json = Json(dto);
        foreach (var emotion in new[] { "lo lắng", "tự hào", "háo hức" }) Assert.DoesNotContain(emotion, json);
        Assert.DoesNotContain("isMatch", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ThoughtBubble_WithoutAUsablePicture_IsHidden()
    {
        ThoughtBubble(picture: false);
        Assert.Null((await Content()).Scenes[0].Interaction);
    }

    // ---- Phân tuổi ----
    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Age8Sets_AreNotShownToOtherAges(int age)
    {
        Reading();
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = age;
        Assert.Null((await Content()).Scenes[0].Interaction);
    }
}
