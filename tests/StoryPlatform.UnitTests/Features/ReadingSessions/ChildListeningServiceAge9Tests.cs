using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ChildListeningServiceAge9Tests
{
    private readonly ReadingHarness _h = new();
    private readonly ChildListeningService _sut;
    private readonly ReadingSession _session;
    private readonly StoryScene _scene;

    public ChildListeningServiceAge9Tests()
    {
        _sut = new ChildListeningService(_h.UnitOfWork.Object);
        _session = _h.AddSession();
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 9;
        _scene = _h.AddScene(0, "Story scene");
        _scene.TextRangeStart = 0;
        _scene.TextRangeEnd = 1000;
    }

    private ListeningInteraction Add(int id, T type, Action<ListeningInteraction>? setup = null)
    {
        var interaction = new ListeningInteraction
        {
            Id = id, StoryVersionId = ReadingHarness.Version, StorySceneId = _scene.Id,
            TargetAge = 9, Type = type, AnchorOffset = 100, MatchFeedback = "Giỏi lắm!"
        };
        setup?.Invoke(interaction);
        _h.ListeningInteractions.Add(interaction);
        return interaction;
    }

    private void Option(int id, int interactionId, int order, string text, bool match = false,
        ListeningOptionRole role = ListeningOptionRole.None, string? feedback = null, string? counterpoint = null) =>
        _h.ListeningOptions.Add(new ListeningInteractionOption
        {
            Id = id, ListeningInteractionId = interactionId, SortOrder = order, Text = text,
            IsMatch = match, Role = role, Feedback = feedback, Counterpoint = counterpoint
        });

    private Task<ListeningAttemptResultDto> Submit(int id, SubmitListeningAttemptRequestDto request) =>
        _sut.SubmitAttemptAsync(ReadingHarness.Child, _session.Id, id, request);

    [Fact]
    public async Task Trial_ReturnsBothSidesWithAudio_AndDoesNotRecordAReplay()
    {
        var interaction = Add(91, T.CharacterTrial, x => x.Prompt = "Mai chọn ở lại giúp bạn.");
        Option(911, 91, 1, "Bạn cần giúp", feedback: "Mai giúp được bạn.", counterpoint: "Mai có thể bị ướt.");
        _h.AddListeningMedia(interaction);

        var first = await Submit(91, new() { Verdict = "reasonable", ReasonOptionId = 911 });
        var replay = await Submit(91, new() { Verdict = "notYet", ReasonOptionId = 911 });

        Assert.True(first.Completed);
        Assert.Equal("Mai giúp được bạn.", replay.Positive);
        Assert.Equal("Mai có thể bị ướt.", replay.Limitation);
        Assert.NotNull(replay.PositiveAudioUrl);
        Assert.NotNull(replay.LimitationAudioUrl);
        Assert.Single(_h.ListeningAttempts);
    }

    [Fact]
    public async Task WrongDetail_RequiresBothSentenceAndReplacement_ThenReveals()
    {
        var interaction = Add(92, T.FindWrongDetail, x =>
        {
            x.Prompt = "Mai ra vườn. Nam cầm chiếc ô.";
            x.HiddenWord = "chiếc ô";
            x.SourceQuote = "Mai ra vườn. Nam cầm bình nước.";
            x.SourceStart = 0; x.SourceEnd = 33;
            x.Explanation = "Nam cầm bình nước.";
        });
        Option(921, 92, 1, "bình nước", true);
        Option(922, 92, 2, "cái xô");
        Option(923, 92, 3, "chiếc ô");
        _h.AddListeningMedia(interaction);

        var first = await Submit(92, new() { SentenceIndex = 0, OptionId = 921 });
        Assert.True(first.CanRetry);
        Assert.False(first.SentenceCorrect);
        Assert.Null(first.CorrectOptionId);
        var second = await Submit(92, new() { SentenceIndex = 1, OptionId = 922 });
        Assert.True(second.Revealed);
        Assert.Equal(1, second.CorrectSentenceIndex);
        Assert.Equal(921, second.CorrectOptionId);
        Assert.NotNull(second.SourceSpan);
    }

    [Fact]
    public async Task Editor_GradesMainSentences_AndRejectsForeignOption()
    {
        var interaction = Add(93, T.StoryEditor, x => x.SourceQuote = "Một. Hai. Ba. Bốn.");
        Option(931, 93, 1, "Một.");
        Option(932, 93, 2, "Hai.", true, feedback: "Ý chính thứ nhất.");
        Option(933, 93, 3, "Ba.", true, feedback: "Ý chính thứ hai.");
        Option(934, 93, 4, "Bốn.", true, feedback: "Ý chính thứ ba.");
        _h.AddListeningMedia(interaction);

        await Assert.ThrowsAsync<BadRequestException>(() => Submit(93, new() { KeptOptionIds = [932, 999] }));
        var result = await Submit(93, new() { KeptOptionIds = [932, 933, 934] });
        Assert.True(result.Matched);
        Assert.Equal([932, 933, 934], result.ModelKeptOptionIds);
        Assert.Equal(3, result.ModelSentenceAudioUrls!.Count);
    }

    [Fact]
    public async Task RelationshipMap_KeepsCorrectEdgesFixedDuringRetry()
    {
        var interaction = Add(94, T.RelationshipMap, x => x.RelationsJson =
            """{"edges":[{"from":0,"to":1,"accepted":[0],"evidence":"A giúp B.","explanation":"A giúp B."},{"from":1,"to":2,"accepted":[1],"evidence":"B bảo vệ C.","explanation":"B bảo vệ C."},{"from":2,"to":0,"accepted":[2],"evidence":"C tin A.","explanation":"C tin A."}]}""");
        Option(941, 94, 1, "A", role: ListeningOptionRole.Node);
        Option(942, 94, 2, "B", role: ListeningOptionRole.Node);
        Option(943, 94, 3, "C", role: ListeningOptionRole.Node);
        Option(944, 94, 4, "giúp đỡ", role: ListeningOptionRole.Card);
        Option(945, 94, 5, "bảo vệ", role: ListeningOptionRole.Card);
        Option(946, 94, 6, "tin tưởng", role: ListeningOptionRole.Card);
        Option(947, 94, 7, "cản trở", role: ListeningOptionRole.Card);
        _h.AddListeningMedia(interaction);

        var first = await Submit(94, new() { Placements = [new(0, 944), new(1, 947), new(2, 946)] });
        Assert.True(first.CanRetry);
        Assert.Equal([true, false, true], first.EdgeResults!.Select(x => x.Matched));
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(94, new()
            { Placements = [new(0, 947), new(1, 945), new(2, 946)] }));
        var second = await Submit(94, new() { Placements = [new(0, 944), new(1, 945), new(2, 946)] });
        Assert.True(second.Completed);
        Assert.True(second.Matched);
        Assert.Equal(2, _h.ListeningAttempts.Count);
    }

    [Fact]
    public async Task WhatIf_AllowsOneTextAnswer_WithoutScoring()
    {
        var interaction = Add(95, T.WhatIf, x => x.Prompt = "Nếu Mai rời đi thì sao?");
        Option(951, 95, 1, "Hoa bị ướt", feedback: "Mưa có thể làm hoa ướt.");
        Option(952, 95, 2, "Nam che hoa", feedback: "Nam có thể giúp.");
        _h.AddListeningMedia(interaction);

        var result = await Submit(95, new() { FreeText = "Nam có thể che hoa." });
        Assert.True(result.Completed);
        Assert.Null(result.Matched);
        Assert.NotNull(result.FeedbackAudioUrl);
        Assert.Single(_h.ListeningAttempts);
    }

    [Fact]
    public async Task WhatIf_RejectsTwoAnswersAtOnce_AndTooLongText()
    {
        var interaction = Add(96, T.WhatIf, x => x.Prompt = "Nếu Mai rời đi thì sao?");
        Option(961, 96, 1, "Hoa bị ướt", feedback: "Mưa làm hoa ướt.");
        _h.AddListeningMedia(interaction);

        await Assert.ThrowsAsync<BadRequestException>(() => Submit(96, new() { OptionId = 961, FreeText = "Cả hai." }));
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(96, new() { FreeText = new string('a', ChildListeningService.MaxAnswerLength + 1) }));
        await Assert.ThrowsAsync<BadRequestException>(() => Submit(96, new()));
        Assert.Empty(_h.ListeningAttempts);
    }

    [Fact]
    public async Task WhatIf_VoiceAnswer_NeedsTheUploadedRecordingFirst()
    {
        var interaction = Add(97, T.WhatIf, x => x.Prompt = "Nếu Mai rời đi thì sao?");
        Option(971, 97, 1, "Hoa bị ướt", feedback: "Mưa làm hoa ướt.");
        _h.AddListeningMedia(interaction);

        await Assert.ThrowsAsync<ConflictException>(() => Submit(97, new() { FreeVoice = true }));
        _h.ReadAloudRecordings.Add(new ReadAloudRecording
        {
            Id = 1, ReadingSessionId = _session.Id, ChildProfileId = ReadingHarness.Child, ListeningInteractionId = 97,
            AudioStoragePath = "free.webm", AudioContentType = "audio/webm", AudioBytes = 3, TotalCount = 0
        });
        var result = await Submit(97, new() { FreeVoice = true });
        Assert.True(result.Completed);
    }
}
