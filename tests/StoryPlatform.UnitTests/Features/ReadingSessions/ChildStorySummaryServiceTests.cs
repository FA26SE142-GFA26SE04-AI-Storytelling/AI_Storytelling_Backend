using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ChildStorySummaryServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly ChildStorySummaryService _sut;

    public ChildStorySummaryServiceTests()
    {
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, string _, CancellationToken _) => path);
        _notifications.Setup(n => n.CreateAsync(It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationDto());
        _sut = new ChildStorySummaryService(_h.UnitOfWork.Object, _storage.Object, _notifications.Object,
            NullLogger<ChildStorySummaryService>.Instance);
    }

    private void VoiceOn(bool enabled = true) => _h.Policies.Add(new SafetyPolicy
    {
        Id = _h.Policies.Count + 1, ChildProfileId = ReadingHarness.Child, VoiceAnswersEnabled = enabled
    });

    private static StorySummaryRecordingRequest Audio(int bytes = 10, string? contentType = "audio/webm;codecs=opus") =>
        new() { Audio = new byte[bytes], ContentType = contentType };

    [Fact]
    public async Task SendRecordingAsync_StoresAudio_SavesSummary_AndNotifiesOwner()
    {
        VoiceOn();
        var session = _h.AddSession(SessionStatus.Completed);

        var result = await _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio());

        Assert.Equal(StorySummaryPolicy.SentMessage, result.Message);
        var summary = Assert.Single(_h.Summaries);
        Assert.Equal(summary.Id, result.SummaryId);
        Assert.Equal(session.Id, summary.ReadingSessionId);
        Assert.Equal(ReadingHarness.Story, summary.StoryId);
        Assert.Equal("audio/webm", summary.AudioContentType);
        Assert.Equal(10, summary.AudioBytes);
        Assert.NotNull(summary.RecordingSentAt);
        Assert.StartsWith($"story-summaries/{ReadingHarness.Child}/{session.Id}/", summary.AudioStoragePath);
        Assert.EndsWith(".webm", summary.AudioStoragePath);
        _storage.Verify(s => s.UploadAsync(summary.AudioStoragePath!, It.IsAny<Stream>(), "audio/webm", It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(7, NotificationType.StorySummarySent,
            It.Is<string?>(p => p!.Contains("\"kind\":\"recording\"")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendRecordingAsync_WhenVoiceOff_IsForbidden_AndUploadsNothing()
    {
        VoiceOn(false);
        var session = _h.AddSession();

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));

        Assert.Empty(_h.Summaries);
        _storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, "audio/webm")]
    [InlineData(StorySummaryPolicy.MaxAudioBytes + 1, "audio/webm")]
    [InlineData(10, "video/mp4")]
    public async Task SendRecordingAsync_RejectsBadAudio(int bytes, string contentType)
    {
        VoiceOn();
        var session = _h.AddSession();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio(bytes, contentType)));

        Assert.Empty(_h.Summaries);
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendRecordingAsync_Twice_ReturnsConflict()
    {
        VoiceOn();
        var session = _h.AddSession();
        await _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio());

        await Assert.ThrowsAsync<ConflictException>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));

        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendRecordingAsync_OnForceExitedSession_IsForbidden()
    {
        VoiceOn();
        var session = _h.AddSession(SessionStatus.ForceExited);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));

        Assert.Empty(_h.Summaries);
    }

    [Fact]
    public async Task SendRecordingAsync_OnAbandonedSession_IsConflict()
    {
        VoiceOn();
        var session = _h.AddSession(SessionStatus.Abandoned);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));

        Assert.Empty(_h.Summaries);
    }

    [Fact]
    public async Task SendRecordingAsync_OtherChildsSession_IsNotFound()
    {
        VoiceOn();
        var session = _h.AddSession(childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));
    }

    [Fact]
    public async Task SendRecordingAsync_WhenSavingFails_DeletesTheUploadedFile()
    {
        VoiceOn();
        var session = _h.AddSession();
        _h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));

        _storage.Verify(s => s.DeleteAsync(It.Is<string>(p => p.StartsWith("story-summaries/")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendFlagsAsync_SavesDistinctScenes_AndNotifies()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var first = _h.AddScene(0, "Thỏ ra vườn.");
        var second = _h.AddScene(1, "Thỏ gặp rùa.");

        var result = await _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = [first.Id, second.Id, first.Id] });

        Assert.Equal(StorySummaryPolicy.SentMessage, result.Message);
        var summary = Assert.Single(_h.Summaries);
        Assert.NotNull(summary.FlagsSentAt);
        Assert.Null(summary.AudioStoragePath);
        Assert.Equal(new[] { first.Id, second.Id }, _h.SummaryFlags.Select(f => f.StorySceneId).OrderBy(id => id));
        Assert.All(_h.SummaryFlags, f => Assert.Equal(summary.Id, f.StorySummaryId));
        _notifications.Verify(n => n.CreateAsync(7, NotificationType.StorySummarySent,
            It.Is<string?>(p => p!.Contains("\"kind\":\"flags\"")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendFlagsAsync_DoesNotNeedVoice()
    {
        VoiceOn(false);
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");

        await _sut.SendFlagsAsync(ReadingHarness.Child, session.Id, new StorySummaryFlagsRequestDto { SceneIds = [scene.Id] });

        Assert.Single(_h.SummaryFlags);
    }

    [Fact]
    public async Task SendFlagsAsync_AfterRecording_ReusesTheSameSummary()
    {
        VoiceOn();
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        await _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio());

        await _sut.SendFlagsAsync(ReadingHarness.Child, session.Id, new StorySummaryFlagsRequestDto { SceneIds = [scene.Id] });

        var summary = Assert.Single(_h.Summaries);
        Assert.NotNull(summary.RecordingSentAt);
        Assert.NotNull(summary.FlagsSentAt);
    }

    [Fact]
    public async Task SendFlagsAsync_RejectsEmpty_TooMany_AndScenesFromAnotherVersion()
    {
        var session = _h.AddSession();
        var scenes = Enumerable.Range(0, StorySummaryPolicy.MaxFlags + 1).Select(i => _h.AddScene(i, $"Cảnh {i}.")).ToList();
        var foreign = _h.AddScene(0, "Truyện khác.", versionId: 999);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = [] }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = scenes.Select(s => s.Id).ToList() }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = [foreign.Id] }));

        Assert.Empty(_h.Summaries);
        Assert.Empty(_h.SummaryFlags);
    }

    [Fact]
    public async Task SendFlagsAsync_Twice_ReturnsConflict()
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        await _sut.SendFlagsAsync(ReadingHarness.Child, session.Id, new StorySummaryFlagsRequestDto { SceneIds = [scene.Id] });

        await Assert.ThrowsAsync<ConflictException>(() => _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = [scene.Id] }));

        Assert.Single(_h.SummaryFlags);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotFailTheChild()
    {
        VoiceOn();
        var session = _h.AddSession();
        _notifications.Setup(n => n.CreateAsync(It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));

        var result = await _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio());

        Assert.Equal(StorySummaryPolicy.SentMessage, result.Message);
        Assert.Single(_h.Summaries);
    }
}
