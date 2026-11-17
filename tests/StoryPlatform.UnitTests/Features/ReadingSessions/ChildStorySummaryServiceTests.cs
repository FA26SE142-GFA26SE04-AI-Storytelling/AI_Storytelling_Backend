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

    private static StorySummaryQuestionRequest Ask(int sceneId, int? beatOrder = null, string? text = null,
        int audioBytes = 0, string? contentType = "audio/webm") =>
        new() { SceneId = sceneId, BeatOrder = beatOrder, Text = text, Audio = new byte[audioBytes], ContentType = audioBytes > 0 ? contentType : null };

    [Fact]
    public async Task AskAsync_AboutAPicture_StoresTheExactAssetTheChildSaw_AndNotifies()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var scene = _h.AddScene(0, "Thỏ ra vườn. Thỏ gặp rùa.");
        _h.AddBeatIllustration(scene, 1, 0, 12, "old-1.png", current: false);
        var seen = _h.AddBeatIllustration(scene, 1, 0, 12, "pic-1.png");
        var beat = _h.Beats.Single();

        var result = await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, beatOrder: 1, text: "  Sao thỏ buồn?  "));

        var question = Assert.Single(_h.SummaryFlags);
        Assert.Equal((scene.Id, beat.Id, seen.Id, "Sao thỏ buồn?"),
            (question.StorySceneId, question.IllustrationBeatId, question.MediaAssetId, question.QuestionText));
        Assert.Null(question.AudioStoragePath);
        Assert.Equal((question.StorySummaryId, question.Id), (result.SummaryId, result.QuestionId));
        Assert.NotNull(Assert.Single(_h.Summaries).FlagsSentAt);
        _notifications.Verify(n => n.CreateAsync(7, NotificationType.StorySummarySent,
            It.Is<string?>(p => p!.Contains("\"kind\":\"question\"")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AskAsync_IgnoresIllustrationsOfFamilyPackages()
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        var packageOnly = _h.AddBeatIllustration(scene, 1, 0, 5, "package.png");
        packageOnly.ParentStoryPackageId = 77;

        // Phiên truyện thường không thấy tranh của gói → cảnh coi như không có tranh, chọn beat 1 là sai.
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, beatOrder: 1)));
        var ok = await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id));

        Assert.Null(Assert.Single(_h.SummaryFlags).MediaAssetId);
        Assert.True(ok.QuestionId > 0);
    }

    [Fact]
    public async Task AskAsync_SceneWithoutBeats_UsesTheSceneIllustration()
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        var picture = _h.AddIllustration(scene, "scene.png");

        await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id));

        var question = Assert.Single(_h.SummaryFlags);
        Assert.Null(question.IllustrationBeatId);
        Assert.Equal(picture.Id, question.MediaAssetId);
    }

    [Fact]
    public async Task AskAsync_SceneWithPictures_RequiresChoosingOne()
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        _h.AddBeatIllustration(scene, 1, 0, 5, "pic-1.png");

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id)));

        Assert.Equal(StorySummaryPolicy.ChoosePictureMessage, error.Message);
        Assert.Empty(_h.SummaryFlags);
    }

    [Fact]
    public async Task AskAsync_WithVoice_UploadsUnderQuestions_WhenVoiceOn()
    {
        VoiceOn();
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");

        await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, audioBytes: 20));

        var question = Assert.Single(_h.SummaryFlags);
        Assert.StartsWith($"story-summaries/{ReadingHarness.Child}/{session.Id}/questions/", question.AudioStoragePath);
        Assert.EndsWith(".webm", question.AudioStoragePath);
        Assert.Equal(("audio/webm", 20), (question.AudioContentType, question.AudioBytes));
    }

    [Fact]
    public async Task AskAsync_VoiceOff_RejectsAudio_ButAcceptsText()
    {
        VoiceOn(false);
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, audioBytes: 20)));
        await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, text: "Rùa là gì?"));

        Assert.Equal("Rùa là gì?", Assert.Single(_h.SummaryFlags).QuestionText);
        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AskAsync_RejectsTooLongText_ForeignScene_AndUnknownPicture()
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        var foreign = _h.AddScene(0, "Truyện khác.", versionId: 999);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AskAsync(ReadingHarness.Child, session.Id,
            Ask(scene.Id, text: new string('a', StorySummaryPolicy.MaxQuestionTextLength + 1))));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(foreign.Id)));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, beatOrder: 3)));

        Assert.Empty(_h.SummaryFlags);
    }

    [Fact]
    public async Task AskAsync_SamePictureTwice_IsConflict_AndSixthQuestionIsRejected()
    {
        var session = _h.AddSession();
        var scenes = Enumerable.Range(0, StorySummaryPolicy.MaxFlags + 1).Select(i => _h.AddScene(i, $"Cảnh {i}.")).ToList();
        await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scenes[0].Id));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scenes[0].Id)));
        foreach (var scene in scenes.Skip(1).Take(StorySummaryPolicy.MaxFlags - 1))
            await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id));
        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scenes[^1].Id)));

        Assert.Equal(StorySummaryPolicy.TooManyQuestionsMessage, error.Message);
        Assert.Equal(StorySummaryPolicy.MaxFlags, _h.SummaryFlags.Count);
        Assert.Single(_h.Summaries);
    }

    [Fact]
    public async Task AskAsync_AfterRecording_ReusesTheSameSummary()
    {
        VoiceOn();
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        await _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio());

        await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id));

        var summary = Assert.Single(_h.Summaries);
        Assert.NotNull(summary.RecordingSentAt);
        Assert.NotNull(summary.FlagsSentAt);
    }

    [Fact]
    public async Task AskAsync_WhenSavingFails_DeletesTheUploadedQuestionAudio()
    {
        VoiceOn();
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        _h.UnitOfWork.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, audioBytes: 20)));

        _storage.Verify(s => s.DeleteAsync(It.Is<string>(p => p.Contains("/questions/")), It.IsAny<CancellationToken>()), Times.Once);
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

    [Fact]
    public async Task AskAsync_TwoPicturesInOneScene_AreSeparateQuestions_AndWhitespaceIsOptional()
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn. Thỏ gặp rùa.");
        _h.AddBeatIllustration(scene, 1, 0, 12, "first.png");
        _h.AddBeatIllustration(scene, 2, 13, 25, "second.png");
        await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, 1, "   "));
        await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, 2, new string('a', 200)));
        Assert.Equal(2, _h.SummaryFlags.Count);
        Assert.Null(_h.SummaryFlags[0].QuestionText);
        Assert.Equal(200, _h.SummaryFlags[1].QuestionText!.Length);
        Assert.Single(_h.Summaries);
    }

    [Fact]
    public async Task AskAsync_OtherChildsSession_IsNotFound()
    {
        var session = _h.AddSession(childId: ReadingHarness.OtherChild);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(1)));
        Assert.Empty(_h.SummaryFlags);
    }

    [Theory]
    [InlineData(StorySummaryPolicy.MaxAudioBytes + 1, "audio/webm")]
    [InlineData(10, "video/mp4")]
    public async Task AskAsync_InvalidAudio_IsRejectedBeforeUploading(int bytes, string type)
    {
        VoiceOn();
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, audioBytes: bytes, contentType: type)));
        Assert.Empty(_h.SummaryFlags);
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AskAsync_TransactionStartupFailure_DeletesUploadedAudio()
    {
        VoiceOn();
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        _h.UnitOfWork.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id, audioBytes: 10)));
        _storage.Verify(x => x.DeleteAsync(It.Is<string>(p => p.Contains("/questions/")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IllustrationLookup_FrozenManifestAllowsReusedBaseMedia_AndUsesNewestAsset()
    {
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        var old = _h.AddBeatIllustration(scene, 1, 0, 12, "old.png", current: true);
        var newest = _h.AddBeatIllustration(scene, 1, 0, 12, "frozen.png", current: false);
        newest.CompletedAt = DateTime.UtcNow.AddMinutes(1);
        _h.AddBeatIllustration(scene, 1, 0, 12, "unlisted.png");
        var assets = await StorySummaryIllustrations.LoadAsync(_h.UnitOfWork.Object, ReadingHarness.Version,
            77, [old.Id, newest.Id], [scene.Id], default);
        Assert.Equal(2, assets.Count);
        Assert.Equal(newest.Id, StorySummaryIllustrations.ForBeat(assets, old.IllustrationBeatId!.Value,
            preferCurrent: false)!.Id);
        Assert.Equal(old.Id, StorySummaryIllustrations.ForBeat(assets, old.IllustrationBeatId.Value)!.Id);
    }

    [Fact]
    public async Task AskAsync_SceneWhoseSelectedImageHasNoUrl_DoesNotSubstituteAnOlderImage()
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        _h.AddIllustration(scene, "old.png");
        var newest = _h.AddIllustration(scene, "new.png");
        newest.Url = null;
        newest.CompletedAt = DateTime.UtcNow.AddMinutes(1);
        await _sut.AskAsync(ReadingHarness.Child, session.Id, Ask(scene.Id));
        Assert.Null(Assert.Single(_h.SummaryFlags).MediaAssetId);
    }
}
