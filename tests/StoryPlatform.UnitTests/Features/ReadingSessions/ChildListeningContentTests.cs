using System.Text.Json;
using Moq;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ChildListeningContentTests
{
    private readonly ReadingHarness _h = new();
    private readonly ChildReadingService _sut;
    private readonly ReadingSession _session;
    private readonly StoryScene[] _scenes;

    public ChildListeningContentTests()
    {
        _sut = new ChildReadingService(_h.UnitOfWork.Object, new Mock<IChildRoadmapEligibility>().Object,
            new Mock<IChildRoadmapProgressRecorder>().Object, new Mock<IChildRewardService>().Object);
        _session = _h.AddSession();
        // 3 scene liền nhau: [0,20), [20,40), [40,60).
        _scenes = Enumerable.Range(0, 3).Select(i =>
        {
            var scene = _h.AddScene(i, $"Scene {i}");
            scene.TextRangeStart = i * 20;
            scene.TextRangeEnd = i * 20 + 20;
            return scene;
        }).ToArray();
    }

    private ListeningInteraction Add(int id, ListeningInteractionType type, int sceneIndex, Action<ListeningInteraction>? setup = null)
    {
        var interaction = new ListeningInteraction
        {
            Id = id, StoryVersionId = ReadingHarness.Version, Type = type,
            AnchorOffset = _scenes[sceneIndex].TextRangeEnd, StorySceneId = _scenes[sceneIndex].Id,
            MatchFeedback = "Giỏi quá!", NoMatchFeedback = "Mình nghe lại nhé."
        };
        setup?.Invoke(interaction);
        _h.ListeningInteractions.Add(interaction);
        return interaction;
    }

    private void Option(int id, int interactionId, int order, string text, bool isMatch = false,
        int? correctOrder = null, int? sourceStart = null, string? feedback = "Ý hay đó!") =>
        _h.ListeningOptions.Add(new ListeningInteractionOption
        {
            Id = id, ListeningInteractionId = interactionId, SortOrder = order, Text = text, Feedback = feedback,
            IsMatch = isMatch, CorrectOrder = correctOrder, SourceStart = sourceStart, SourceQuote = $"câu {id}",
            ImageDescription = $"hình {id}"
        });

    private ListeningInteraction Checkpoint()
    {
        var interaction = Add(1, ListeningInteractionType.Checkpoint, 0, x =>
        {
            x.Kind = CheckpointKind.Emotion;
            x.Prompt = "Thỏ thấy thế nào?";
            x.Explanation = "Trời tối nên Thỏ sợ.";
        });
        Option(10, 1, 1, "Sợ", isMatch: true, feedback: "Ừ, Thỏ sợ.");
        Option(11, 1, 2, "Vui");
        _h.AddListeningMedia(interaction);
        return interaction;
    }

    private Task<ReadingContentDto> Content() => _sut.GetContentAsync(ReadingHarness.Child, _session.Id);

    // Giữ nguyên chữ có dấu; encoder mặc định đổi sang \uXXXX làm phép kiểm "không chứa" luôn đạt.
    private static string Json(object? value) => JsonSerializer.Serialize(value,
        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    [Fact]
    public async Task Checkpoint_IsOnItsSceneWithAudioAndCards_ButNoAnswers()
    {
        Checkpoint();

        var content = await Content();

        var item = content.Scenes[0].Interaction!;
        Assert.Equal(1, item.Id);
        Assert.Equal("Checkpoint", item.Type);
        Assert.Equal("Emotion", item.Kind);
        Assert.Equal("Thỏ thấy thế nào?", item.Prompt);
        Assert.Equal(new[] { "noMatch", "prompt" }, item.AudioUrls.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "Sợ", "Vui" }, item.Options.Select(x => x.Text));
        Assert.Equal("Ừ, Thỏ sợ.", item.Options[0].Feedback);
        Assert.Equal("listen-1-10-feedback.mp3", item.Options[0].FeedbackAudioUrl);
        Assert.Equal("listen-1-10-text.mp3", item.Options[0].AudioUrl);
        Assert.Null(content.Scenes[1].Interaction);
        Assert.False(content.Scenes[0].AttemptedInSession);
        Assert.False(content.ListenOnly);
        var json = Json(content);
        Assert.Contains("Thỏ thấy thế nào?", json);
        Assert.DoesNotContain("IsMatch", json);
        Assert.DoesNotContain("Explanation", json);
        Assert.DoesNotContain("Trời tối nên Thỏ sợ", json);
    }

    [Fact]
    public async Task InteractionWithMissingMediaOrWithoutScene_IsNotReturned()
    {
        Checkpoint();
        _h.Assets.Single(x => x.ListeningInteractionId == 1 && x.AudioPartKey == "prompt").Status = MediaStatus.ManualReview;
        var unbound = Add(2, ListeningInteractionType.RolePlay, 1, x => x.StorySceneId = null);
        _h.AddListeningMedia(unbound);

        var content = await Content();

        Assert.All(content.Scenes, scene => Assert.Null(scene.Interaction));
    }

    [Fact]
    public async Task FillBlank_ShowsMaskedSentenceAndHint_WithoutAcceptedAnswers()
    {
        var interaction = Add(3, ListeningInteractionType.FillBlank, 0, x =>
        {
            x.SourceQuote = "Thỏ gặp Bác Gấu trong rừng.";
            x.HiddenWord = "bác Gấu";
            x.Hint = "Một con vật to lớn.";
        });
        Option(30, 3, 1, "bác Gấu");
        Option(31, 3, 2, "gấu");
        _h.AddListeningMedia(interaction);

        var item = (await Content()).Scenes[0].Interaction!;

        Assert.Equal("Thỏ gặp ____ trong rừng.", item.MaskedSentence);
        Assert.Equal("Một con vật to lớn.", item.Hint);
        Assert.Empty(item.Options);
        var json = Json(item);
        Assert.Contains("Thỏ gặp ____ trong rừng.", json);
        Assert.DoesNotContain("Gấu", json);
    }

    [Fact]
    public async Task RolePlay_UsesAudioOfTheSegmentHoldingTheLine()
    {
        var interaction = Add(4, ListeningInteractionType.RolePlay, 1, x =>
        {
            x.CharacterName = "Thỏ";
            x.SourceQuote = "Có ai không?";
            x.SourceStart = 32;
        });
        _h.AddListeningMedia(interaction);
        var first = _h.AddSegment(_scenes[1], 1, "Thỏ nghe.");
        first.StartOffset = 0; first.EndOffset = 10;
        var second = _h.AddSegment(_scenes[1], 2, "Có ai không?");
        second.StartOffset = 10; second.EndOffset = 20;
        _h.AddAudio(first, "seg-1.mp3");
        _h.AddAudio(second, "seg-2.mp3");

        var item = (await Content()).Scenes[1].Interaction!;

        Assert.Equal("seg-2.mp3", item.ModelAudioUrl);
        Assert.Equal("Có ai không?", item.ModelSentence);
        Assert.Equal("Thỏ", item.CharacterName);
        Assert.Equal(new[] { "match" }, item.AudioUrls.Keys);
    }

    [Fact]
    public async Task RolePlayWithoutSegmentAudio_IsNotReturned()
    {
        var interaction = Add(4, ListeningInteractionType.RolePlay, 1, x => x.SourceStart = 32);
        _h.AddListeningMedia(interaction);

        Assert.Null((await Content()).Scenes[1].Interaction);
    }

    [Fact]
    public async Task PicturePrediction_HasImagesAndRevealScene()
    {
        var interaction = Add(5, ListeningInteractionType.PicturePrediction, 0, x =>
        {
            x.Prompt = "Ai đến?";
            x.WaitFeedback = "Nghe tiếp nhé!";
            x.RevealOffset = 40;
        });
        Option(50, 5, 1, "Bác Gấu", isMatch: true);
        Option(51, 5, 2, "Chim Sẻ");
        _h.AddListeningMedia(interaction);

        var item = (await Content()).Scenes[0].Interaction!;

        Assert.Equal(1, item.RevealSceneIndex);
        Assert.Equal("Nghe tiếp nhé!", item.WaitFeedback);
        Assert.Equal(new[] { "picture-50.png", "picture-51.png" }, item.Options.Select(x => x.ImageUrl));
        Assert.Equal(new[] { "prompt", "wait" }, item.AudioUrls.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EventOrdering_CardsAreShuffledStablyAndNeverInTheRightOrder()
    {
        var interaction = Add(6, ListeningInteractionType.EventOrdering, 2);
        Option(61, 6, 1, "Thỏ lạc", correctOrder: 1, sourceStart: 5, feedback: null);
        Option(62, 6, 2, "Gấu giúp", correctOrder: 2, sourceStart: 25, feedback: null);
        Option(63, 6, 3, "Về nhà", correctOrder: 3, sourceStart: 45, feedback: null);
        _h.AddListeningMedia(interaction);

        for (var sessionId = 1; sessionId <= 20; sessionId++)
        {
            _session.Id = sessionId;
            var first = (await Content()).Scenes[2].Interaction!.Options.Select(x => x.Id).ToList();
            var again = (await Content()).Scenes[2].Interaction!.Options.Select(x => x.Id).ToList();
            Assert.Equal(first, again);
            Assert.NotEqual(new[] { 61, 62, 63 }, first);
            Assert.Equal(new[] { 61, 62, 63 }, first.Order());
        }
        var json = Json((await Content()).Scenes[2].Interaction);
        Assert.Contains("Thỏ lạc", json);
        Assert.DoesNotContain("CorrectOrder", json);
        Assert.DoesNotContain("câu 61", json);
    }

    [Fact]
    public async Task AttemptedInSession_CountsOnlyThisSession_AndEventOrderingNeedsMatchOrThreeWrong()
    {
        Checkpoint();
        var ordering = Add(6, ListeningInteractionType.EventOrdering, 2);
        Option(61, 6, 1, "A", correctOrder: 1, sourceStart: 5, feedback: null);
        Option(62, 6, 2, "B", correctOrder: 2, sourceStart: 25, feedback: null);
        _h.AddListeningMedia(ordering);
        void Attempt(int sessionId, int interactionId, bool? isMatch) => _h.ListeningAttempts.Add(new ListeningInteractionAttempt
        {
            Id = _h.ListeningAttempts.Count + 1, ReadingSessionId = sessionId, ChildProfileId = ReadingHarness.Child,
            ListeningInteractionId = interactionId, IsMatch = isMatch
        });
        Attempt(99, 1, true);
        Attempt(_session.Id, 6, false);
        Attempt(_session.Id, 6, false);

        var before = await Content();
        Assert.False(before.Scenes[0].AttemptedInSession);
        Assert.False(before.Scenes[2].AttemptedInSession);

        Attempt(_session.Id, 1, true);
        Attempt(_session.Id, 6, false);
        var after = await Content();
        Assert.True(after.Scenes[0].AttemptedInSession);
        Assert.True(after.Scenes[2].AttemptedInSession);
    }

    [Fact]
    public async Task ListenOnly_ComesFromTheStoryProgress()
    {
        Checkpoint();
        _h.Progress.Add(new ReadingProgress
        {
            Id = 1, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story, ListenOnly = true
        });

        var content = await Content();

        Assert.True(content.ListenOnly);
        Assert.NotNull(content.Scenes[0].Interaction);
    }

    [Fact]
    public void Mask_ReplacesFirstMatchIgnoringCase_AndKeepsSentenceWhenWordIsMissing()
    {
        Assert.Equal("Thỏ gặp ____ và bác Gấu.", ChildListeningContent.Mask("Thỏ gặp Bác Gấu và bác Gấu.", "bác gấu"));
        Assert.Equal("Thỏ chạy.", ChildListeningContent.Mask("Thỏ chạy.", "Gấu"));
    }
    [Fact]
    public async Task SetListenOnly_SavesPerStory_KeepsOtherFlags_AndShowsInContent()
    {
        _h.Progress.Add(new ReadingProgress
        {
            Id = 1, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story, IsBookmarked = true
        });

        var flags = await _sut.SetListenOnlyAsync(ReadingHarness.Child, _session.Id, true);

        Assert.True(flags.ListenOnly);
        Assert.True(flags.IsBookmarked);
        Assert.True(_h.Progress.Single().ListenOnly);
        Assert.True((await Content()).ListenOnly);
        Assert.Empty(_h.Telemetry);

        Assert.False((await _sut.SetListenOnlyAsync(ReadingHarness.Child, _session.Id, false)).ListenOnly);
    }

    [Fact]
    public async Task SetListenOnly_CreatesProgressForANewStory()
    {
        var flags = await _sut.SetListenOnlyAsync(ReadingHarness.Child, _session.Id, true);

        Assert.True(flags.ListenOnly);
        var progress = Assert.Single(_h.Progress);
        Assert.Equal(ReadingHarness.Story, progress.StoryId);
    }

    [Fact]
    public async Task SetListenOnly_OnAnotherChildsSession_IsNotFound() =>
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.NotFoundException>(() =>
            _sut.SetListenOnlyAsync(ReadingHarness.OtherChild, _session.Id, true));
}
