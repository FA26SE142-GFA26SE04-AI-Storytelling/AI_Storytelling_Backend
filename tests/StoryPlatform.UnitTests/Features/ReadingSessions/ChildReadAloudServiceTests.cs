using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
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
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ChildReadAloudService _sut;

    public ChildReadAloudServiceTests()
    {
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, string _, CancellationToken _) => path);
        _sut = new ChildReadAloudService(_h.UnitOfWork.Object, _recognizer.Object, _storage.Object,
            NullLogger<ChildReadAloudService>.Instance);
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
        _storage.VerifyNoOtherCalls();
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
        _storage.VerifyNoOtherCalls();
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
    private ListeningInteraction Practice(string text = "Thỏ con ra vườn chơi.")
    {
        var interaction = new ListeningInteraction
        {
            Id = 70, StoryVersionId = ReadingHarness.Version, Type = ListeningInteractionType.ReadAloud,
            AnchorOffset = 100, SourceQuote = text, MatchFeedback = "Con đọc to rõ lắm!"
        };
        interaction.StorySceneId = _h.AddScene(0, text).Id;
        _h.ListeningInteractions.Add(interaction);
        _h.AddListeningMedia(interaction);
        return interaction;
    }

    [Fact]
    public async Task CheckPracticeAsync_ScoresAgainstThePassage_AndLogsInteractionIdOnly()
    {
        VoiceOn();
        var session = _h.AddSession();
        var practice = Practice();
        Hears(("thỏ", 0.9), ("con", 0.9), ("ra", 0.9), ("vườn", 0.9), ("chơi", 0.9));

        var result = await _sut.CheckPracticeAsync(ReadingHarness.Child, session.Id, practice.Id, Audio());

        Assert.Equal((5, 0, 0, 5), (result.GoodCount, result.RetryCount, result.SkippedCount, result.TotalCount));
        Assert.Equal(practice.Id, result.InteractionId);
        Assert.Null(result.SegmentId);
        Assert.Equal("Em đọc hay quá!", result.Message);
        var log = Assert.Single(_h.Telemetry, t => t.EventType == TelemetryEventType.AsrWordResult);
        Assert.Contains($"\"interactionId\":{practice.Id}", log.EventPayload);
        Assert.DoesNotContain("segmentId", log.EventPayload);
    }

    [Fact]
    public async Task CheckPracticeAsync_OtherTypeOrOtherVersion_IsNotFound_WithoutCallingGoogle()
    {
        VoiceOn();
        var session = _h.AddSession();
        var practice = Practice();
        practice.Type = ListeningInteractionType.RolePlay;
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CheckPracticeAsync(ReadingHarness.Child, session.Id, practice.Id, Audio()));

        practice.Type = ListeningInteractionType.ReadAloud;
        practice.StoryVersionId = ReadingHarness.Version + 1;
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CheckPracticeAsync(ReadingHarness.Child, session.Id, practice.Id, Audio()));
        _recognizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CheckPracticeAsync_VoiceNotEnabled_IsForbidden()
    {
        VoiceOn(enabled: false);
        var session = _h.AddSession();
        var practice = Practice();

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.CheckPracticeAsync(ReadingHarness.Child, session.Id, practice.Id, Audio()));

        Assert.Equal(ReadAloudPolicy.VoiceOffMessage, error.Message);
        _recognizer.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("other-package")]
    [InlineData("unbound")]
    [InlineData("deleted-scene")]
    [InlineData("other-scene-version")]
    [InlineData("missing-passage-audio")]
    [InlineData("missing-praise-audio")]
    public async Task CheckPracticeAsync_UnavailablePassage_IsHiddenBeforeRecognition(string reason)
    {
        VoiceOn();
        var session = _h.AddSession();
        var practice = Practice();
        switch (reason)
        {
            case "deleted": practice.IsDeleted = true; break;
            case "other-package": practice.ParentStoryPackageId = 99; break;
            case "unbound": practice.StorySceneId = null; break;
            case "deleted-scene": _h.Scenes.Single().IsDeleted = true; break;
            case "other-scene-version": _h.Scenes.Single().StoryVersionId++; break;
            case "missing-passage-audio": _h.Assets.Single(x => x.AudioPartKey == "text").IsCurrent = false; break;
            case "missing-praise-audio": _h.Assets.Single(x => x.AudioPartKey == "match").Status = MediaStatus.ManualReview; break;
        }

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CheckPracticeAsync(ReadingHarness.Child, session.Id, practice.Id, Audio()));

        _recognizer.VerifyNoOtherCalls();
        _storage.VerifyNoOtherCalls();
        Assert.Empty(_h.Telemetry);
    }

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task CheckPracticeAsync_EndedSession_IsConflict(SessionStatus status)
    {
        VoiceOn();
        var session = _h.AddSession(status);
        var practice = Practice();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.CheckPracticeAsync(ReadingHarness.Child, session.Id, practice.Id, Audio()));

        _recognizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CheckPracticeAsync_InterruptedSession_IsForbidden()
    {
        VoiceOn();
        var session = _h.AddSession();
        session.ForceExitRequestedAt = DateTime.UtcNow;
        var practice = Practice();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.CheckPracticeAsync(ReadingHarness.Child, session.Id, practice.Id, Audio()));

        _recognizer.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, "audio/webm", ReadAloudPolicy.EmptyAudioMessage)]
    [InlineData(ReadAloudPolicy.MaxAudioBytes + 1, "audio/webm", ReadAloudPolicy.AudioTooLargeMessage)]
    [InlineData(10, "video/mp4", ReadAloudPolicy.UnsupportedAudioMessage)]
    public async Task CheckPracticeAsync_InvalidAudio_IsRejectedBeforeRecognition(int bytes, string contentType, string message)
    {
        VoiceOn();
        var session = _h.AddSession();
        var practice = Practice();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CheckPracticeAsync(ReadingHarness.Child, session.Id, practice.Id, Audio(bytes, contentType)));

        Assert.Equal(message, error.Message);
        _recognizer.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task CheckAsync_SaveFails_DeletesNewFile_AndPreservesOldFile()
    {
        VoiceOn();
        var (session, segment) = Reading();
        Hears(("thỏ", 0.9));
        await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());
        var oldPath = Assert.Single(_h.ReadAloudRecordings).AudioStoragePath;
        _h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("save failed"));

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio()));

        _storage.Verify(s => s.DeleteAsync(It.Is<string>(path => path != oldPath), CancellationToken.None), Times.Once);
        _storage.Verify(s => s.DeleteAsync(oldPath, It.IsAny<CancellationToken>()), Times.Never);
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckAsync_LocksTheSession_BeforeLookingUpTheRecording()
    {
        VoiceOn();
        var (session, segment) = Reading();
        Hears(("thỏ", 0.9));

        await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());

        // Bé bấm hai lần cùng lúc: request sau phải chờ khóa của phiên rồi mới tìm bản ghi,
        // nên không thêm trùng dòng và không để lại file không dòng nào trỏ tới.
        var calls = _h.UnitOfWork.Invocations.Select(call => call.Method).ToList();
        var begin = calls.FindIndex(method => method.Name == nameof(IUnitOfWork.BeginTransactionAsync));
        var locked = calls.FindIndex(method => method.Name == nameof(IUnitOfWork.AcquireTransactionLockAsync));
        var lookup = calls.FindIndex(method => method.Name == nameof(IUnitOfWork.Repository)
                                               && method.GetGenericArguments()[0] == typeof(ReadAloudRecording));
        var commit = calls.FindIndex(method => method.Name == nameof(IUnitOfWork.CommitTransactionAsync));
        Assert.True(begin >= 0 && begin < locked && locked < lookup && lookup < commit,
            $"begin={begin}, lock={locked}, lookup={lookup}, commit={commit}");
        _h.UnitOfWork.Verify(u => u.AcquireTransactionLockAsync(session.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_h.ReadAloudRecordings);
    }

    [Fact]
    public async Task CheckAsync_StagingFails_DeletesUploadedFile()
    {
        VoiceOn();
        var (session, segment) = Reading();
        Hears(("thỏ", 0.9));
        _h.UnitOfWork.Setup(u => u.Repository<ReadAloudRecording>()).Throws(new IOException("query failed"));

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio()));

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_OldFileDeletionFails_StillReturnsScore()
    {
        VoiceOn();
        var (session, segment) = Reading();
        Hears(("thỏ", 0.9));
        await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());
        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("storage down"));

        var result = await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());

        Assert.Equal(1, result.GoodCount);
        Assert.Single(_h.ReadAloudRecordings);
    }

    [Fact]
    public async Task CheckAsync_UploadFailsOnRetry_PreservesPreviousRecording()
    {
        VoiceOn();
        var (session, segment) = Reading();
        Hears(("thỏ", 0.9));
        await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());
        var oldPath = Assert.Single(_h.ReadAloudRecordings).AudioStoragePath;
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("storage down"));

        await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());

        Assert.Equal(oldPath, Assert.Single(_h.ReadAloudRecordings).AudioStoragePath);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_KeepsRecording_WithScores()
    {
        VoiceOn();
        var (session, segment) = Reading();
        Hears(("thỏ", 0.9), ("còn", 0.9), ("ra", 0.9), ("vườn", 0.9));

        await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());

        var recording = Assert.Single(_h.ReadAloudRecordings);
        Assert.Equal((session.Id, ReadingHarness.Child, (int?)segment.Id, (int?)null),
            (recording.ReadingSessionId, recording.ChildProfileId, recording.StorySegmentId, recording.ListeningInteractionId));
        Assert.Equal((3, 1, 0, 4), (recording.GoodCount, recording.RetryCount, recording.SkippedCount, recording.TotalCount));
        Assert.StartsWith($"read-aloud/{ReadingHarness.Child}/{session.Id}/segment-{segment.Id}/", recording.AudioStoragePath);
        Assert.EndsWith(".webm", recording.AudioStoragePath);
        Assert.Equal(("audio/webm", 10), (recording.AudioContentType, recording.AudioBytes));
        _storage.Verify(s => s.UploadAsync(recording.AudioStoragePath, It.IsAny<Stream>(), "audio/webm", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckAsync_ReadAgain_ReplacesRecording_AndDeletesOldFile()
    {
        VoiceOn();
        var (session, segment) = Reading("Thỏ con.");
        Hears(("thỏ", 0.9));
        await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());
        var oldPath = Assert.Single(_h.ReadAloudRecordings).AudioStoragePath;
        Hears(("thỏ", 0.9), ("con", 0.9));

        await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio(20));

        var recording = Assert.Single(_h.ReadAloudRecordings);
        Assert.NotEqual(oldPath, recording.AudioStoragePath);
        Assert.Equal((2, 20), (recording.GoodCount, recording.AudioBytes));
        _storage.Verify(s => s.DeleteAsync(oldPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckAsync_UploadFails_StillReturnsScore_AndKeepsNoRecording()
    {
        VoiceOn();
        var (session, segment) = Reading("Thỏ con.");
        Hears(("thỏ", 0.9), ("con", 0.9));
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("storage down"));

        var result = await _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio());

        Assert.Equal(2, result.GoodCount);
        Assert.Empty(_h.ReadAloudRecordings);
        Assert.Single(_h.Telemetry, t => t.EventType == TelemetryEventType.AsrWordResult);
    }

    [Fact]
    public async Task CheckAsync_RecognitionFails_UploadsNothing()
    {
        VoiceOn();
        var (session, segment) = Reading();
        _recognizer.Setup(r => r.RecognizeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SpeechRecognitionUnavailableException());

        await Assert.ThrowsAsync<SpeechRecognitionUnavailableException>(() =>
            _sut.CheckAsync(ReadingHarness.Child, session.Id, segment.Id, Audio()));

        _storage.VerifyNoOtherCalls();
        Assert.Empty(_h.ReadAloudRecordings);
    }

    [Fact]
    public async Task CheckPracticeAsync_KeepsRecording_ForThePassage()
    {
        VoiceOn();
        var session = _h.AddSession();
        var practice = Practice();
        Hears(("thỏ", 0.9), ("con", 0.9), ("ra", 0.9), ("vườn", 0.9), ("chơi", 0.9));

        await _sut.CheckPracticeAsync(ReadingHarness.Child, session.Id, practice.Id, Audio());

        var recording = Assert.Single(_h.ReadAloudRecordings);
        Assert.Equal(((int?)null, (int?)practice.Id), (recording.StorySegmentId, recording.ListeningInteractionId));
        Assert.StartsWith($"read-aloud/{ReadingHarness.Child}/{session.Id}/practice-{practice.Id}/", recording.AudioStoragePath);
    }
}
