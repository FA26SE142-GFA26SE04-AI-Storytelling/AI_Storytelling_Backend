using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

/// <summary>Đọc luân phiên (tuổi 8): chấm mềm từng lượt thoại của nhân vật bé đã chọn.</summary>
public sealed class ChildReadAloudServiceAge8Tests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<ISpeechRecognizer> _recognizer = new(MockBehavior.Strict);
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ChildReadAloudService _sut;
    private readonly ReadingSession _session;
    private readonly ListeningInteraction _reading;

    public ChildReadAloudServiceAge8Tests()
    {
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, string _, CancellationToken _) => path);
        _sut = new ChildReadAloudService(_h.UnitOfWork.Object, _recognizer.Object, _storage.Object,
            NullLogger<ChildReadAloudService>.Instance);
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 8;
        _session = _h.AddSession();
        _h.Policies.Add(new SafetyPolicy { Id = 1, ChildProfileId = ReadingHarness.Child, VoiceAnswersEnabled = true });
        var scene = _h.AddScene(0, "Thỏ hỏi.");
        _reading = new ListeningInteraction
        {
            Id = 80, StoryVersionId = ReadingHarness.Version, TargetAge = 8, Type = ListeningInteractionType.AlternateReading,
            AnchorOffset = 100, StorySceneId = scene.Id, MatchFeedback = "Con đọc hay quá!"
        };
        _h.ListeningInteractions.Add(_reading);
        Line(800, 1, "Cậu đang làm gì đấy?", "Thỏ");
        Line(801, 2, "Mình đang tìm hạt dẻ.", "Sóc");
        Line(802, 3, "Mình giúp cậu nhé!", "Thỏ");
        _h.AddListeningMedia(_reading);
    }

    private void Line(int id, int order, string text, string speaker) => _h.ListeningOptions.Add(new ListeningInteractionOption
    {
        Id = id, ListeningInteractionId = 80, SortOrder = order, CorrectOrder = order, Text = text, Speaker = speaker
    });

    private void Chose(string speaker) => _h.ListeningAttempts.Add(new ListeningInteractionAttempt
    {
        Id = _h.ListeningAttempts.Count + 1, ReadingSessionId = _session.Id, ChildProfileId = ReadingHarness.Child,
        ListeningInteractionId = 80, AnswerText = speaker, AttemptedAt = DateTime.UtcNow
    });

    private static ReadAloudRequest Audio(int? line) => new() { Audio = new byte[10], ContentType = "audio/webm", LineId = line };

    private void Hears(params string[] words) =>
        _recognizer.Setup(r => r.RecognizeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(words.Select(word => new RecognizedWord(word, 0.9)).ToList());

    private Task<ReadAloudResultDto> Read(int? line) => _sut.CheckPracticeAsync(ReadingHarness.Child, _session.Id, 80, Audio(line));

    [Fact]
    public async Task ReadingMyLine_IsScoredSoftlyAgainstThatLine_AndRecordedPerLine()
    {
        Chose("Thỏ");
        Hears("cậu", "đang", "làm", "gì", "đấy");
        var result = await Read(800);

        Assert.Equal(800, result.LineId);
        Assert.Equal(80, result.InteractionId);
        Assert.Equal((5, 0, 0, 5), (result.GoodCount, result.RetryCount, result.SkippedCount, result.TotalCount));
        Assert.Equal("Con đọc hay quá!", result.Message);
        var recording = Assert.Single(_h.ReadAloudRecordings);
        Assert.Equal(800, recording.ListeningInteractionOptionId);
        Assert.Contains("practice-80-line-800", recording.AudioStoragePath);
        var log = Assert.Single(_h.Telemetry, t => t.EventType == TelemetryEventType.AsrWordResult);
        Assert.Contains("\"optionId\":800", log.EventPayload);
        Assert.DoesNotContain("cậu", log.EventPayload);
    }

    [Fact]
    public async Task UnclearWords_GetTheSoftRetryMessage_NeverTheWordSai()
    {
        Chose("Thỏ");
        Hears("cậu", "đang");
        var result = await Read(800);
        Assert.True(result.RetryCount > 0);
        Assert.Equal("Mình nghe chưa rõ, mình đọc lại nhé.", result.Message);
        Assert.DoesNotContain("sai", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.Words, word => word.Status == "skipped");
    }

    [Fact]
    public async Task EachLineHasItsOwnThreeAttempts_AndItsOwnRecording()
    {
        Chose("Thỏ");
        Hears("cậu", "đang", "làm", "gì", "đấy");
        for (var attempt = 0; attempt < ChildReadAloudService.MaxPracticeAttempts; attempt++) await Read(800);

        var limit = await Assert.ThrowsAsync<ConflictException>(() => Read(800));
        Assert.Equal(ChildReadAloudService.VoiceRetryLimitCode, limit.Message);

        // Lượt thoại thứ hai của bé vẫn còn đủ 3 lần.
        Hears("mình", "giúp", "cậu", "nhé");
        await Read(802);
        Assert.Equal(2, _h.ReadAloudRecordings.Count);
        Assert.Equal(4, _h.Telemetry.Count(log => log.EventType == TelemetryEventType.AsrWordResult));
    }

    [Fact]
    public async Task BeforeChoosingACharacter_IsConflict_WithoutRecognitionOrUpload()
    {
        var error = await Assert.ThrowsAsync<ConflictException>(() => Read(800));
        Assert.Equal(ChildReadAloudService.SpeakerNotChosenCode, error.Message);
        _recognizer.VerifyNoOtherCalls();
        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_h.ReadAloudRecordings);
    }

    [Theory]
    [InlineData(801)]    // lời của nhân vật còn lại (hệ thống đọc)
    [InlineData(999)]    // lượt không thuộc tương tác
    [InlineData(null)]   // không nói lượt nào
    public async Task OtherCharactersLineUnknownLineOrNoLine_IsNotFound(int? line)
    {
        Chose("Thỏ");
        await Assert.ThrowsAsync<NotFoundException>(() => Read(line));
        _recognizer.VerifyNoOtherCalls();
        Assert.Empty(_h.ReadAloudRecordings);
    }

    [Fact]
    public async Task ChoosingTheOtherCharacter_ReadsTheOtherLines()
    {
        Chose("Sóc");
        Hears("mình", "đang", "tìm", "hạt", "dẻ");
        var result = await Read(801);
        Assert.Equal(801, result.LineId);
        await Assert.ThrowsAsync<NotFoundException>(() => Read(800));
    }

    [Fact]
    public async Task VoiceOff_IsForbidden_AndAChildOfAnotherAge_IsNotFound()
    {
        Chose("Thỏ");
        _h.Policies.Single().VoiceAnswersEnabled = false;
        await Assert.ThrowsAsync<ForbiddenException>(() => Read(800));
        _h.Policies.Single().VoiceAnswersEnabled = true;
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        await Assert.ThrowsAsync<NotFoundException>(() => Read(800));
        _recognizer.VerifyNoOtherCalls();
    }
}
