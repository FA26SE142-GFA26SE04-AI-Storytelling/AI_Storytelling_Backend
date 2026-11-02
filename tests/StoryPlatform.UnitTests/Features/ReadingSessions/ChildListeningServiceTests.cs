using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ChildListeningServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly ChildListeningService _sut;
    private readonly ReadingSession _session;
    private readonly StoryScene[] _scenes;

    public ChildListeningServiceTests()
    {
        _sut = new ChildListeningService(_h.UnitOfWork.Object);
        _session = _h.AddSession();
        _scenes = Enumerable.Range(0, 3).Select(i =>
        {
            var scene = _h.AddScene(i, $"Scene {i}");
            scene.TextRangeStart = i * 20;
            scene.TextRangeEnd = i * 20 + 20;
            return scene;
        }).ToArray();

        Add(1, ListeningInteractionType.Checkpoint, 0, x => { x.Kind = CheckpointKind.Emotion; x.Prompt = "Thỏ thấy sao?"; });
        Option(10, 1, 1, "Sợ", isMatch: true, feedback: "Ừ, Thỏ sợ.");
        Option(11, 1, 2, "Vui", feedback: "Cũng có thể!");
        Add(2, ListeningInteractionType.FillBlank, 1, x =>
        {
            x.SourceQuote = "Thỏ gặp bác Gấu."; x.HiddenWord = "bác Gấu"; x.Hint = "Con vật to.";
        });
        Option(20, 2, 1, "bác Gấu");
        Option(21, 2, 2, "Gấu");
        Add(3, ListeningInteractionType.RolePlay, 1, x => x.SourceStart = 25);
        Add(4, ListeningInteractionType.EventOrdering, 2);
        Option(41, 4, 1, "Thỏ lạc", correctOrder: 1, sourceStart: 5);
        Option(42, 4, 2, "Gấu giúp", correctOrder: 2, sourceStart: 25);
        Option(43, 4, 3, "Về nhà", correctOrder: 3, sourceStart: 45);
        foreach (var interaction in _h.ListeningInteractions) _h.AddListeningMedia(interaction);
    }

    private void Add(int id, ListeningInteractionType type, int sceneIndex, Action<ListeningInteraction>? setup = null)
    {
        var interaction = new ListeningInteraction
        {
            Id = id, StoryVersionId = ReadingHarness.Version, Type = type,
            AnchorOffset = _scenes[sceneIndex].TextRangeEnd + id, StorySceneId = _scenes[sceneIndex].Id,
            MatchFeedback = "Giỏi quá!", NoMatchFeedback = "Mình nghe lại nhé."
        };
        setup?.Invoke(interaction);
        _h.ListeningInteractions.Add(interaction);
    }

    private void Option(int id, int interactionId, int order, string text, bool isMatch = false,
        string? feedback = null, int? correctOrder = null, int? sourceStart = null) =>
        _h.ListeningOptions.Add(new ListeningInteractionOption
        {
            Id = id, ListeningInteractionId = interactionId, SortOrder = order, Text = text, Feedback = feedback,
            IsMatch = isMatch, CorrectOrder = correctOrder, SourceStart = sourceStart
        });

    private Task<ListeningAttemptResultDto> Submit(int interactionId, SubmitListeningAttemptRequestDto request,
        int? sessionId = null, int childId = ReadingHarness.Child) =>
        _sut.SubmitAttemptAsync(childId, sessionId ?? _session.Id, interactionId, request);

    private Task<ListeningAttemptResultDto> Order(params int[] ids) =>
        Submit(4, new SubmitListeningAttemptRequestDto { OrderedOptionIds = ids.ToList() });

    [Fact]
    public async Task Checkpoint_ReturnsCardFeedback_SavesOnce_AndRepeatsTheSameResult()
    {
        var first = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 });

        Assert.Equal("Ừ, Thỏ sợ.", first.Feedback);
        Assert.Equal("listen-1-10-feedback.mp3", first.FeedbackAudioUrl);
        Assert.Null(first.Matched);
        var saved = Assert.Single(_h.ListeningAttempts);
        Assert.Equal((10, true, ReadingHarness.Child, _session.Id),
            (saved.SelectedOptionId, saved.IsMatch, saved.ChildProfileId, saved.ReadingSessionId));

        var again = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 11 });
        Assert.Equal("Ừ, Thỏ sợ.", again.Feedback);
        Assert.Single(_h.ListeningAttempts);
    }

    [Fact]
    public async Task Checkpoint_WithCardOfAnotherInteractionOrNoCard_IsBadRequest()
    {
        Assert.Equal(ChildListeningService.AnswerInvalidCode, (await Assert.ThrowsAsync<BadRequestException>(() =>
            Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 41 }))).Message);
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(1, new SubmitListeningAttemptRequestDto()));
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task FillBlank_MatchesAnyAcceptedAnswerIgnoringCaseAndSpaces()
    {
        var result = await Submit(2, new SubmitListeningAttemptRequestDto { Answer = "  BÁC   gấu " });

        Assert.True(result.Matched);
        Assert.Equal("Giỏi quá!", result.Feedback);
        Assert.Equal("listen-2-i-match.mp3", result.FeedbackAudioUrl);
        Assert.Null(result.SuggestedAnswer);
        Assert.Equal("BÁC   gấu", _h.ListeningAttempts.Single().AnswerText);
    }

    [Fact]
    public async Task FillBlank_NotMatched_GivesHintFeedbackAndSuggestedAnswer()
    {
        var result = await Submit(2, new SubmitListeningAttemptRequestDto { Answer = "chim sẻ" });

        Assert.False(result.Matched);
        Assert.Equal("Mình nghe lại nhé.", result.Feedback);
        Assert.Equal("listen-2-i-noMatch.mp3", result.FeedbackAudioUrl);
        Assert.Equal("bác Gấu", result.SuggestedAnswer);
        Assert.False(_h.ListeningAttempts.Single().IsMatch);
    }

    [Fact]
    public async Task FillBlank_BlankOrTooLongAnswer_IsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(2, new SubmitListeningAttemptRequestDto { Answer = "   " }));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            Submit(2, new SubmitListeningAttemptRequestDto { Answer = new string('a', ChildListeningService.MaxAnswerLength + 1) }));
    }

    [Fact]
    public async Task RolePlay_ReturnsPraise()
    {
        var result = await Submit(3, new SubmitListeningAttemptRequestDto());

        Assert.Equal("Giỏi quá!", result.Feedback);
        Assert.Equal("listen-3-i-match.mp3", result.FeedbackAudioUrl);
        Assert.Null(_h.ListeningAttempts.Single().IsMatch);
    }

    [Fact]
    public async Task EventOrdering_Correct_IsMatchedWithoutReplay()
    {
        var result = await Order(41, 42, 43);

        Assert.True(result.Matched);
        Assert.Equal("Giỏi quá!", result.Feedback);
        Assert.Empty(result.ReplaySceneIndexes);
        Assert.False(result.Revealed);
        Assert.Null(result.CorrectOrderOptionIds);
        Assert.Equal("[41,42,43]", _h.ListeningAttempts.Single().OrderedOptionIdsJson);
    }

    [Fact]
    public async Task EventOrdering_Wrong_ReplaysOnlyScenesOfMisplacedCards_InStoryOrder()
    {
        var result = await Order(42, 41, 43);

        Assert.False(result.Matched);
        Assert.Equal("Mình nghe lại nhé.", result.Feedback);
        Assert.Equal("listen-4-i-noMatch.mp3", result.FeedbackAudioUrl);
        Assert.Equal(new[] { 0, 1 }, result.ReplaySceneIndexes);
        Assert.False(result.Revealed);
        Assert.Null(result.CorrectOrderOptionIds);
    }

    [Fact]
    public async Task EventOrdering_ThirdWrongInSession_RevealsTheCorrectOrder_AndLaterAttemptsStayRevealed()
    {
        Assert.False((await Order(43, 42, 41)).Revealed);
        Assert.False((await Order(42, 41, 43)).Revealed);

        var third = await Order(41, 43, 42);
        Assert.False(third.Matched);
        Assert.True(third.Revealed);
        Assert.Equal(new[] { 41, 42, 43 }, third.CorrectOrderOptionIds);
        Assert.Empty(third.ReplaySceneIndexes);
        Assert.Equal("Mình nghe lại nhé.", third.Feedback);

        var fourth = await Order(41, 42, 43);
        Assert.True(fourth.Matched);
        Assert.True(fourth.Revealed);
        Assert.Equal(4, _h.ListeningAttempts.Count);
    }

    [Fact]
    public async Task EventOrdering_WrongCountStartsAgainInANewSession()
    {
        await Order(43, 42, 41);
        await Order(43, 42, 41);
        await Order(43, 42, 41);
        var next = _h.AddSession();

        var result = await Submit(4, new SubmitListeningAttemptRequestDto { OrderedOptionIds = [43, 42, 41] }, next.Id);

        Assert.False(result.Revealed);
        Assert.Equal(new[] { 0, 2 }, result.ReplaySceneIndexes);
    }

    [Theory]
    [InlineData(new[] { 41, 42 })]
    [InlineData(new[] { 41, 41, 43 })]
    [InlineData(new[] { 41, 42, 10 })]
    public async Task EventOrdering_MustSendEachCardOnce(int[] ids)
    {
        Assert.Equal(ChildListeningService.AnswerInvalidCode,
            (await Assert.ThrowsAsync<BadRequestException>(() => Order(ids))).Message);
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task SessionOfAnotherChild_IsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 }, childId: ReadingHarness.OtherChild));

    [Fact]
    public async Task CompletedSession_IsClosed()
    {
        _session.Status = SessionStatus.Completed;
        Assert.Equal(ChildListeningService.SessionClosedCode, (await Assert.ThrowsAsync<ConflictException>(() =>
            Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 }))).Message);
    }

    [Fact]
    public async Task InterruptedSession_IsForbidden()
    {
        _session.ForceExitRequestedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<ForbiddenException>(() => Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 }));
    }

    [Fact]
    public async Task InteractionOfAnotherVersion_OrWithoutMedia_OrUnbound_IsNotFound()
    {
        _h.ListeningInteractions.Single(x => x.Id == 1).StoryVersionId = 999;
        await Assert.ThrowsAsync<NotFoundException>(() => Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 }));

        _h.Assets.Single(x => x.ListeningInteractionId == 3).Status = MediaStatus.ManualReview;
        await Assert.ThrowsAsync<NotFoundException>(() => Submit(3, new SubmitListeningAttemptRequestDto()));

        _h.ListeningInteractions.Single(x => x.Id == 2).StorySceneId = null;
        await Assert.ThrowsAsync<NotFoundException>(() => Submit(2, new SubmitListeningAttemptRequestDto { Answer = "Gấu" }));
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task Answering_DoesNotTouchRewards()
    {
        await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 });
        await Order(41, 42, 43);

        Assert.Empty(_h.Achievements);
        Assert.Empty(_h.Badges);
        Assert.Empty(_h.Attempts);
    }
}
