using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ChildListeningServiceTests
{
    [Fact]
    public async Task Age7PictureSearch_RetriesThenRevealsHotspot()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        Add(7, ListeningInteractionType.PictureSearch, 0, x => { x.TargetAge = 7; x.SourceStart = 5;
            x.Prompt = "Tìm Thỏ con"; x.TargetLabel = "Thỏ con"; });
        var picture = _h.AddIllustration(_scenes[0], "scene.png");
        picture.ValidationStatus = ValidationStatus.Passed;
        _h.ListeningInteractions.Single(x => x.Id == 7).HotspotJson =
            new HotspotBox(picture.Id, 0.4, 0.5, 0.2, 0.2).ToJson();
        _h.AddListeningMedia(_h.ListeningInteractions.Single(x => x.Id == 7));
        var first = await Submit(7, new() { X = 0.1, Y = 0.1 });
        Assert.True(first.CanRetry);
        Assert.Null(first.RevealHotspot);
        var second = await Submit(7, new() { X = 0.1, Y = 0.1 });
        Assert.True(second.Revealed);
        Assert.True(second.Completed);
        Assert.Equal(0.4, second.RevealHotspot!.X);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    [Fact]
    public async Task Age7FollowAction_ChecksItemAndTarget()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        Add(8, ListeningInteractionType.FollowAction, 0, x => { x.TargetAge = 7; x.Prompt = "Bé giúp Thỏ nhé"; });
        Option(80, 8, 1, "bác Gấu", isMatch: true);
        Option(81, 8, 2, "bụi cây");
        Option(82, 8, 3, "cửa hang");
        _h.ListeningOptions.Single(x => x.Id == 80).Role = ListeningOptionRole.Item;
        _h.ListeningOptions.Single(x => x.Id == 81).Role = ListeningOptionRole.Item;
        _h.ListeningOptions.Single(x => x.Id == 82).Role = ListeningOptionRole.Target;
        _h.AddListeningMedia(_h.ListeningInteractions.Single(x => x.Id == 8));
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(8, new() { OptionId = 80, TargetOptionId = 81 }));
        var result = await Submit(8, new() { OptionId = 80, TargetOptionId = 82 });
        Assert.True(result.Matched);
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task Age7TrueFalse_RequiresDisplayedOrderAndCompletesBothStatements()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        Add(6, ListeningInteractionType.TrueFalse, 0, x => x.TargetAge = 7);
        Option(60, 6, 1, "Thỏ nghe tiếng động", isMatch: true);
        Option(61, 6, 2, "Thỏ ngủ trong hang");
        var interaction = _h.ListeningInteractions.Single(x => x.Id == 6);
        _h.AddListeningMedia(interaction);
        var displayed = ChildListeningContent.Build(_session.Id, 7, _scenes, [], [interaction], _h.ListeningOptions,
            _h.Assets, []).Values.Single().Interaction.Statements!;
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(6, new() { OptionId = displayed[1].Id, Truth = true }));
        var first = _h.ListeningOptions.Single(x => x.Id == displayed[0].Id);
        var second = _h.ListeningOptions.Single(x => x.Id == displayed[1].Id);
        var result1 = await Submit(6, new() { OptionId = first.Id, Truth = first.IsMatch });
        Assert.False(result1.Completed);
        var result2 = await Submit(6, new() { OptionId = second.Id, Truth = second.IsMatch });
        Assert.True(result2.Completed);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    [Fact]
    public async Task Age7SentenceBuilder_AcceptsAlternateOrderAndReturnsItsAudio()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        Add(9, ListeningInteractionType.SentenceBuilder, 0, x =>
        { x.TargetAge = 7; x.SourceQuote = "Bác Gấu dẫn Thỏ về hang."; x.AcceptedOrdersJson = "[[1,2,3],[2,1,3]]"; });
        Option(90, 9, 1, "Bác Gấu", correctOrder: 1);
        Option(91, 9, 2, "dẫn Thỏ", correctOrder: 2);
        Option(92, 9, 3, "về hang", correctOrder: 3);
        _h.AddListeningMedia(_h.ListeningInteractions.Single(x => x.Id == 9));
        var result = await Submit(9, new() { OrderedOptionIds = [91, 90, 92] });
        Assert.True(result.Matched);
        Assert.False(result.Completed);
        Assert.Contains("sentence:2", result.SentenceAudioUrl);
    }

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
            Id = id, StoryVersionId = ReadingHarness.Version, TargetAge = 6, Type = type,
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

    // ---- Tuổi 7: các nhánh còn lại của từng loại ----
    private (MediaAsset Picture, ListeningInteraction Interaction) PictureSearchSet()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        Add(7, ListeningInteractionType.PictureSearch, 0, x => { x.TargetAge = 7; x.SourceStart = 5;
            x.Prompt = "Tìm Thỏ con"; x.TargetLabel = "Thỏ con"; });
        var picture = _h.AddIllustration(_scenes[0], "scene.png");
        picture.ValidationStatus = ValidationStatus.Passed;
        var interaction = _h.ListeningInteractions.Single(x => x.Id == 7);
        interaction.HotspotJson = new HotspotBox(picture.Id, 0.4, 0.5, 0.2, 0.2).ToJson();
        _h.AddListeningMedia(interaction);
        return (picture, interaction);
    }

    [Fact]
    public async Task Age7PictureSearch_TapInsideBox_MatchesAndCompletes()
    {
        PictureSearchSet();
        var result = await Submit(7, new() { X = 0.5, Y = 0.6 });
        Assert.True(result.Matched);
        Assert.True(result.Completed);
        Assert.False(result.CanRetry);
        Assert.Null(result.RevealHotspot);
        Assert.Equal("Giỏi quá!", result.Feedback);
        Assert.Equal("{\"x\":0.5,\"y\":0.6}", Assert.Single(_h.ListeningAttempts).AnswerText);
    }

    [Fact]
    public async Task Age7PictureSearch_TapJustOutsideBox_UsesTouchPadding()
    {
        PictureSearchSet();
        Assert.True((await Submit(7, new() { X = 0.37, Y = 0.48 })).Matched);
    }

    [Fact]
    public async Task Age7PictureSearch_MissThenHit_CompletesOnSecondTap()
    {
        PictureSearchSet();
        var first = await Submit(7, new() { X = 0.1, Y = 0.1 });
        Assert.Equal("Nhìn kỹ lại nhé.", first.Feedback);
        var second = await Submit(7, new() { X = 0.5, Y = 0.6 });
        Assert.True(second.Matched);
        Assert.False(second.Revealed);
        Assert.True(second.Completed);
    }

    [Theory]
    [InlineData(1.4, 0.5)]
    [InlineData(-0.1, 0.5)]
    [InlineData(0.5, double.NaN)]
    public async Task Age7PictureSearch_TapOutsideTheImage_IsBadRequest(double x, double y)
    {
        PictureSearchSet();
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(7, new() { X = x, Y = y }));
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task Age7PictureSearch_MissingCoordinates_IsBadRequest()
    {
        PictureSearchSet();
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(7, new() { X = 0.5 }));
    }

    [Fact]
    public async Task Age7PictureSearch_WithoutHotspot_IsHiddenFromTheChild()
    {
        var (_, interaction) = PictureSearchSet();
        interaction.HotspotJson = null;
        await Assert.ThrowsAsync<NotFoundException>(() => Submit(7, new() { X = 0.5, Y = 0.6 }));
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task Age7PictureSearch_ResendAfterFinish_ReturnsLastResultWithoutNewRow()
    {
        PictureSearchSet();
        await Submit(7, new() { X = 0.1, Y = 0.1 });
        var second = await Submit(7, new() { X = 0.1, Y = 0.1 });
        var third = await Submit(7, new() { X = 0.5, Y = 0.6 });
        Assert.True(third.Revealed);
        Assert.Equal(second.RevealHotspot!.X, third.RevealHotspot!.X);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    private List<ListeningInteractionOption> TrueFalseSet()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        Add(6, ListeningInteractionType.TrueFalse, 0, x => x.TargetAge = 7);
        Option(60, 6, 1, "Thỏ nghe tiếng động", isMatch: true);
        Option(61, 6, 2, "Thỏ ngủ trong hang");
        var interaction = _h.ListeningInteractions.Single(x => x.Id == 6);
        _h.AddListeningMedia(interaction);
        // Thứ tự bé thấy trên màn hình (xáo cố định theo phiên); bé phải trả lời đúng thứ tự này.
        var shown = ChildListeningContent.Build(_session.Id, 7, _scenes, [], [interaction], _h.ListeningOptions,
            _h.Assets, []).Values.Single().Interaction.Statements!;
        return shown.Select(card => _h.ListeningOptions.Single(x => x.Id == card.Id)).ToList();
    }

    private Task<ListeningAttemptResultDto> Answer(ListeningInteractionOption statement, bool truth) =>
        Submit(6, new() { OptionId = statement.Id, Truth = truth });

    [Fact]
    public async Task Age7TrueFalse_WrongFirst_AllowsRetryWithoutRevealing()
    {
        var shown = TrueFalseSet();
        var result = await Answer(shown[0], !shown[0].IsMatch);
        Assert.False(result.Matched);
        Assert.True(result.CanRetry);
        Assert.False(result.Revealed);
        Assert.False(result.Completed);
        Assert.Null(result.CorrectTruth);
        Assert.Equal("Mình nghe lại nhé.", result.Feedback);
        Assert.Equal(shown[0].Id, result.OptionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Age7TrueFalse_SecondWrong_SaysTheAnswerOfThatStatement(bool statementIsTheTrueOne)
    {
        var shown = TrueFalseSet();
        var statement = shown.First(x => x.IsMatch == statementIsTheTrueOne);
        foreach (var earlier in shown.TakeWhile(x => x.Id != statement.Id)) await Answer(earlier, earlier.IsMatch);
        await Answer(statement, !statement.IsMatch);
        var result = await Answer(statement, !statement.IsMatch);
        Assert.True(result.Revealed);
        Assert.False(result.CanRetry);
        Assert.Equal(statement.IsMatch, result.CorrectTruth);
        var expected = statement.IsMatch ? ListeningAudioParts.TruthYes : ListeningAudioParts.TruthNo;
        Assert.Equal(ListeningAudioParts.FixedLine(expected), result.Feedback);
        Assert.Contains(expected, result.FeedbackAudioUrl);
        Assert.DoesNotContain("sai", result.Feedback!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chưa đúng", result.Feedback!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Age7TrueFalse_Reveal_PointsAtTheEvidenceInThePicture(bool trueStatement)
    {
        var shown = TrueFalseSet();
        var picture = _h.AddIllustration(_scenes[0], "evidence.png");
        picture.ValidationStatus = ValidationStatus.Passed;
        var statement = shown.First(x => x.IsMatch == trueStatement);
        statement.HotspotJson = new HotspotBox(picture.Id, 0.1, 0.2, 0.3, 0.4).ToJson();
        foreach (var earlier in shown.TakeWhile(x => x.Id != statement.Id)) await Answer(earlier, earlier.IsMatch);
        await Answer(statement, !statement.IsMatch);
        var result = await Answer(statement, !statement.IsMatch);
        Assert.Equal((0.1, 0.2, 0.3, 0.4), (result.RevealHotspot!.X, result.RevealHotspot.Y, result.RevealHotspot.W, result.RevealHotspot.H));
        Assert.Equal("evidence.png", result.RevealImageUrl);
    }

    [Fact]
    public async Task Age7TrueFalse_Reveal_WithoutEvidence_OnlySaysTheAnswer()
    {
        var shown = TrueFalseSet();
        await Answer(shown[0], !shown[0].IsMatch);
        var result = await Answer(shown[0], !shown[0].IsMatch);
        Assert.True(result.Revealed);
        Assert.Null(result.RevealHotspot);
        Assert.Null(result.RevealImageUrl);
        Assert.NotNull(result.CorrectTruth);
    }

    [Fact]
    public async Task Age7TrueFalse_Reveal_IgnoresEvidenceOnARetiredPicture()
    {
        var shown = TrueFalseSet();
        var picture = _h.AddIllustration(_scenes[0], "old.png");
        picture.ValidationStatus = ValidationStatus.Passed;
        picture.IsCurrent = false;
        shown[0].HotspotJson = new HotspotBox(picture.Id, 0.1, 0.2, 0.3, 0.4).ToJson();
        await Answer(shown[0], !shown[0].IsMatch);
        var result = await Answer(shown[0], !shown[0].IsMatch);
        Assert.True(result.Revealed);
        Assert.Null(result.RevealHotspot);
        Assert.Null(result.RevealImageUrl);
    }

    [Fact]
    public async Task Age7TrueFalse_RightThenRight_CompletesAndResendAddsNoRow()
    {
        var shown = TrueFalseSet();
        var first = await Answer(shown[0], shown[0].IsMatch);
        Assert.True(first.Matched);
        Assert.False(first.Completed);
        var second = await Answer(shown[1], shown[1].IsMatch);
        Assert.True(second.Completed);
        var again = await Answer(shown[0], !shown[0].IsMatch);
        Assert.True(again.Completed);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    [Fact]
    public async Task Age7TrueFalse_MissingTruth_IsBadRequest()
    {
        var shown = TrueFalseSet();
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(6, new() { OptionId = shown[0].Id }));
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task Age7FollowAction_WrongItem_RetriesOnceThenRevealsTheItem()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        Add(8, ListeningInteractionType.FollowAction, 0, x => { x.TargetAge = 7; x.Prompt = "Bé giúp Thỏ nhé"; });
        Option(80, 8, 1, "bác Gấu", isMatch: true);
        Option(81, 8, 2, "bụi cây");
        Option(82, 8, 3, "cửa hang");
        _h.ListeningOptions.Single(x => x.Id == 80).Role = ListeningOptionRole.Item;
        _h.ListeningOptions.Single(x => x.Id == 81).Role = ListeningOptionRole.Item;
        _h.ListeningOptions.Single(x => x.Id == 82).Role = ListeningOptionRole.Target;
        _h.AddListeningMedia(_h.ListeningInteractions.Single(x => x.Id == 8));
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(8, new() { OptionId = 82, TargetOptionId = 82 }));
        Assert.Empty(_h.ListeningAttempts);
        var first = await Submit(8, new() { OptionId = 81, TargetOptionId = 82 });
        Assert.True(first.CanRetry);
        Assert.Equal("Bé thử lại nhé.", first.Feedback);
        Assert.Null(first.CorrectOptionId);
        var second = await Submit(8, new() { OptionId = 81, TargetOptionId = 82 });
        Assert.True(second.Revealed);
        Assert.Equal(80, second.CorrectOptionId);
        Assert.True(second.Completed);
    }

    private void SentenceBuilderSet()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        Add(9, ListeningInteractionType.SentenceBuilder, 0, x =>
        { x.TargetAge = 7; x.SourceQuote = "Bác Gấu dẫn Thỏ về hang."; x.AcceptedOrdersJson = "[[1,2,3],[2,1,3]]"; });
        Option(90, 9, 1, "Bác Gấu", correctOrder: 1);
        Option(91, 9, 2, "dẫn Thỏ", correctOrder: 2);
        Option(92, 9, 3, "về hang", correctOrder: 3);
        _h.AddListeningMedia(_h.ListeningInteractions.Single(x => x.Id == 9));
    }

    [Fact]
    public async Task Age7SentenceBuilder_WrongOrder_RetriesOnceWithTheHearLine()
    {
        SentenceBuilderSet();
        var first = await Submit(9, new() { OrderedOptionIds = [92, 91, 90] });
        Assert.False(first.Matched);
        Assert.True(first.CanRetry);
        Assert.Equal("Nghe lạ quá, mình thử lại nhé.", first.Feedback);
        Assert.Null(first.CorrectOrderOptionIds);
        Assert.Null(first.SentenceAudioUrl);
    }

    [Fact]
    public async Task Age7SentenceBuilder_SecondWrong_RevealsTheStoryOrderAndItsAudio()
    {
        SentenceBuilderSet();
        await Submit(9, new() { OrderedOptionIds = [92, 91, 90] });
        var second = await Submit(9, new() { OrderedOptionIds = [92, 90, 91] });
        Assert.True(second.Revealed);
        Assert.True(second.Completed);
        Assert.Equal([90, 91, 92], second.CorrectOrderOptionIds);
        Assert.EndsWith("-sentence.mp3", second.SentenceAudioUrl);
        var again = await Submit(9, new() { OrderedOptionIds = [90, 91, 92] });
        Assert.True(again.Revealed);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    [Fact]
    public async Task Age7SentenceBuilder_StoryOrder_UsesTheFirstSentenceAudio()
    {
        SentenceBuilderSet();
        var result = await Submit(9, new() { OrderedOptionIds = [90, 91, 92] });
        Assert.True(result.Matched);
        Assert.EndsWith("-sentence.mp3", result.SentenceAudioUrl);
    }

    [Theory]
    [InlineData(90, 90, 92)]
    [InlineData(90, 91)]
    [InlineData(90, 91, 92, 93)]
    [InlineData(90, 91, 999)]
    public async Task Age7SentenceBuilder_InvalidCards_IsBadRequest(params int[] ids)
    {
        SentenceBuilderSet();
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(9, new() { OrderedOptionIds = ids.ToList() }));
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task Age7EventOrdering_RevealsAfterTheSecondWrongOrder()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        _h.ListeningInteractions.Single(x => x.Id == 4).TargetAge = 7;
        var first = await Order(42, 41, 43);
        Assert.False(first.Revealed);
        var second = await Order(43, 42, 41);
        Assert.True(second.Revealed);
        Assert.Equal([41, 42, 43], second.CorrectOrderOptionIds);
        await Order(41, 42, 43);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    [Fact]
    public async Task Age6EventOrdering_StillNeedsThreeWrongOrdersToReveal()
    {
        await Order(42, 41, 43);
        var second = await Order(43, 42, 41);
        Assert.False(second.Revealed);
        var third = await Order(42, 41, 43);
        Assert.True(third.Revealed);
    }

    [Fact]
    public async Task Literal_RetriesOnce_RevealsAndRepeatsLastResult()
    {
        _h.ListeningInteractions.Single(x => x.Id == 1).Kind = CheckpointKind.Literal;
        _h.AddListeningMedia(_h.ListeningInteractions.Single(x => x.Id == 1));
        var first = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 11 });
        Assert.True(first.CanRetry);
        Assert.False(first.Revealed);
        Assert.Null(first.CorrectOptionId);
        Assert.Equal("Bé nhìn lại tranh nhé.", first.Feedback);
        var second = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 11 });
        Assert.True(second.Revealed);
        Assert.False(second.CanRetry);
        Assert.Equal(10, second.CorrectOptionId);
        var third = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 });
        Assert.Equal(second.CorrectOptionId, third.CorrectOptionId);
        Assert.False(third.Matched);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(7)]
    public async Task OtherAgeOrUntaggedInteraction_IsNotFound(int? age)
    {
        _h.ListeningInteractions.Single(x => x.Id == 1).TargetAge = age;
        await Assert.ThrowsAsync<NotFoundException>(() => Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 }));
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task Age6Picture_RecordsUngradedChoice()
    {
        var interaction = _h.ListeningInteractions.Single(x => x.Id == 1);
        interaction.Type = ListeningInteractionType.PicturePrediction;
        interaction.Kind = null;
        _h.AddListeningMedia(interaction);
        var result = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 });
        Assert.Null(result.Matched);
        Assert.Null(Assert.Single(_h.ListeningAttempts).IsMatch);
    }

    [Fact]
    public async Task CharacterLetter_StepsRequireChoiceAndSaveToLatestAttempt()
    {
        var interaction = _h.ListeningInteractions.Single(x => x.Id == 1);
        interaction.Type = ListeningInteractionType.CharacterLetter;
        interaction.Kind = null;
        interaction.CharacterName = "Thỏ";
        interaction.BlankIndex = 0;
        _h.ListeningOptions.Single(x => x.Id == 10).Text = "th";
        _h.ListeningOptions.Single(x => x.Id == 11).Text = "b";
        _h.AddListeningMedia(interaction);
        var steps = new ListeningStepsRequestDto { Sound = "tried", Trace = "skipped" };
        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.SaveStepsAsync(ReadingHarness.Child, _session.Id, 1, steps));
        Assert.Equal("LISTENING_STEPS_BEFORE_ANSWER", error.Message);
        var first = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 11 });
        Assert.True(first.CanRetry);
        Assert.Equal("Mình chọn lại nhé.", first.Feedback);
        var second = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 11 });
        Assert.True(second.Revealed);
        Assert.Equal(10, second.CorrectOptionId);
        Assert.Equal("Chữ còn thiếu là th.", second.Feedback);
        Assert.Equal("listen-1-10-sound.mp3", second.FeedbackAudioUrl);
        await _sut.SaveStepsAsync(ReadingHarness.Child, _session.Id, 1, steps);
        Assert.Null(_h.ListeningAttempts[0].StepResultsJson);
        Assert.Equal("{\"sound\":\"tried\",\"trace\":\"skipped\"}", _h.ListeningAttempts[1].StepResultsJson);
        steps.Sound = "good";
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveStepsAsync(ReadingHarness.Child, _session.Id, 1, steps));
    }

    [Fact]
    public async Task WhoSaidIt_FirstRightFinishes_WithoutSecondAttempt()
    {
        var interaction = _h.ListeningInteractions.Single(x => x.Id == 1);
        interaction.Type = ListeningInteractionType.WhoSaidIt;
        interaction.SourceQuote = "Xin chào bạn!";
        _h.AddListeningMedia(interaction);
        var first = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 10 });
        Assert.True(first.Matched);
        Assert.False(first.CanRetry);
        Assert.Equal(interaction.MatchFeedback, first.Feedback);
        var repeat = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 11 });
        Assert.True(repeat.Matched);
        Assert.Single(_h.ListeningAttempts);
    }

    [Fact]
    public async Task WhoSaidIt_SecondNonMatchPlaysCorrectSpeakerName()
    {
        var interaction = _h.ListeningInteractions.Single(x => x.Id == 1);
        interaction.Type = ListeningInteractionType.WhoSaidIt;
        interaction.SourceQuote = "Xin chào bạn!";
        _h.AddListeningMedia(interaction);
        await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 11 });
        var second = await Submit(1, new SubmitListeningAttemptRequestDto { OptionId = 11 });
        Assert.True(second.Revealed);
        Assert.Equal(10, second.CorrectOptionId);
        Assert.Equal("listen-1-10-text.mp3", second.FeedbackAudioUrl);
    }

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

        _h.Assets.Single(x => x.ListeningInteractionId == 3 && x.AudioPartKey == "match").Status = MediaStatus.ManualReview;
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RepeatedAnswer_ReturnsSignedFeedbackAudio_LikeTheFirstAnswer(int interactionId)
    {
        var sut = new ChildListeningService(_h.UnitOfWork.Object, new PrefixSigner());
        var request = interactionId == 1
            ? new SubmitListeningAttemptRequestDto { OptionId = 10 }
            : new SubmitListeningAttemptRequestDto { Answer = "bác Gấu" };

        var first = await sut.SubmitAttemptAsync(ReadingHarness.Child, _session.Id, interactionId, request);
        var repeat = await sut.SubmitAttemptAsync(ReadingHarness.Child, _session.Id, interactionId, request);

        Assert.StartsWith("signed:", first.FeedbackAudioUrl);
        Assert.Equal(first.FeedbackAudioUrl, repeat.FeedbackAudioUrl);
        Assert.Single(_h.ListeningAttempts);
    }

    private sealed class PrefixSigner : Application.Features.ReadingSessions.Interfaces.IChildMediaUrlSigner
    {
        public Task<Func<string?, string?>> SignAsync(IEnumerable<string?> paths, CancellationToken cancellationToken = default) =>
            Task.FromResult<Func<string?, string?>>(path => path is null ? null : "signed:" + path);
    }
}
