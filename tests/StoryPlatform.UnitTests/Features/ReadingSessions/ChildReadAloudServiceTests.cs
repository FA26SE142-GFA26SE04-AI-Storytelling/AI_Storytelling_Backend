using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ChildReadAloudServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<ISpeechRecognizer> _recognizer = new(MockBehavior.Strict);
    private readonly ChildReadAloudService _sut;

    public ChildReadAloudServiceTests()
    {
        _sut = new ChildReadAloudService(_h.UnitOfWork.Object, _recognizer.Object);
    }

    private static ReadAloudRequest Audio(int bytes = 10, string? contentType = "audio/webm;codecs=opus") =>
        new() { Audio = new byte[bytes], ContentType = contentType };

    private void VoiceOn(bool enabled = true) => _h.Policies.Add(new SafetyPolicy
    {
        Id = _h.Policies.Count + 1, ChildProfileId = ReadingHarness.Child, VoiceAnswersEnabled = enabled
    });

    private (ReadingSession Session, StorySegment Segment) Reading(string text = "Thỏ con ra vườn.")
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, text);
        return (session, _h.AddSegment(scene, 1, text));
    }

    private void Hears(params (string Word, double? Confidence)[] words) =>
        _recognizer.Setup(r => r.RecognizeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(words.Select(word => new RecognizedWord(word.Word, word.Confidence)).ToList());

    [Fact]
    public async Task CheckAsync_ReturnsWordResults_AndLogsCountsOnly()
    {
        VoiceOn();
        var (session, segment) = Reading();
        Hears(("thỏ", 0.9), ("còn", 0.9), ("ra", 0.9), ("vườn", 0.9));

        var result = await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());

        Assert.Equal(new[] { "good", "retry", "good", "good" }, result.Words.Select(word => word.Status));
        Assert.Equal(ReadAloudHint.Tone, result.Words[1].Hint);
        Assert.Equal((3, 1, 0, 4), (result.GoodCount, result.RetryCount, result.SkippedCount, result.TotalCount));
        Assert.Equal("Em đọc hay quá! Thử đọc lại các chữ màu vàng nhé.", result.Message);
        Assert.Equal(segment.Id, result.SegmentId);
        var log = Assert.Single(_h.Telemetry, t => t.EventType == TelemetryEventType.AsrWordResult);
        Assert.Equal(session.Id, log.ReadingSessionId);
        Assert.Contains("\"good\":3", log.EventPayload);
        Assert.DoesNotContain("còn", log.EventPayload);
    }

    [Fact]
    public async Task CheckAsync_VoiceNotEnabled_IsForbidden_WithoutCallingGoogle()
    {
        VoiceOn(enabled: false);
        var (session, segment) = Reading();

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio()));

        Assert.Equal(ReadAloudPolicy.VoiceOffMessage, error.Message);
        _recognizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CheckAsync_NoSafetyPolicy_IsForbidden()
    {
        var (session, segment) = Reading();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio()));
    }

    [Fact]
    public async Task CheckAsync_AnotherChildsSession_IsNotFound()
    {
        VoiceOn();
        var session = _h.AddSession(childId: ReadingHarness.OtherChild);
        var segment = _h.AddSegment(_h.AddScene(0, "Thỏ con."), 1, "Thỏ con.");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio()));
    }

    [Fact]
    public async Task CheckAsync_SegmentOfAnotherStoryVersion_IsNotFound()
    {
        VoiceOn();
        var session = _h.AddSession();
        var otherScene = _h.AddScene(0, "Chuyện khác.", versionId: 999);
        var segment = _h.AddSegment(otherScene, 1, "Chuyện khác.");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio()));
    }

    [Fact]
    public async Task CheckAsync_FinishedSession_IsConflict()
    {
        VoiceOn();
        var (session, segment) = Reading();
        session.Status = SessionStatus.Completed;

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio()));
    }

    [Theory]
    [InlineData(0, "audio/webm", ReadAloudPolicy.EmptyAudioMessage)]
    [InlineData(ReadAloudPolicy.MaxAudioBytes + 1, "audio/webm", ReadAloudPolicy.AudioTooLargeMessage)]
    [InlineData(10, "video/mp4", ReadAloudPolicy.UnsupportedAudioMessage)]
    [InlineData(10, null, ReadAloudPolicy.UnsupportedAudioMessage)]
    public async Task CheckAsync_InvalidAudio_IsBadRequest(int bytes, string? contentType, string message)
    {
        VoiceOn();
        var (session, segment) = Reading();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio(bytes, contentType)));

        Assert.Equal(message, error.Message);
        _recognizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CheckAsync_GoogleUnavailable_PropagatesFriendlyError_AndLogsNothing()
    {
        VoiceOn();
        var (session, segment) = Reading();
        _recognizer.Setup(r => r.RecognizeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SpeechRecognitionUnavailableException());

        await Assert.ThrowsAsync<SpeechRecognitionUnavailableException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio()));

        Assert.DoesNotContain(_h.Telemetry, t => t.EventType == TelemetryEventType.AsrWordResult);
    }

    [Fact]
    public async Task CheckAsync_PerfectReading_PraisesTheChild()
    {
        VoiceOn();
        var (session, segment) = Reading("Thỏ con.");
        Hears(("thỏ", 0.9), ("con", 0.9));

        var result = await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());

        Assert.Equal("Em đọc hay quá!", result.Message);
    }
}
