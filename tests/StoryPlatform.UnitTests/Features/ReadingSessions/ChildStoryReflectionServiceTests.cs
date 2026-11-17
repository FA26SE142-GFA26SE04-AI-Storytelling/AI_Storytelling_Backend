using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ChildStoryReflectionServiceTests
{
    [Fact]
    public async Task SendNoteAsync_ExactLimits_AreAccepted()
    {
        VoiceOn();
        var (session, _) = Chosen();

        await _sut.SendNoteAsync(ReadingHarness.Child, session.Id,
            Note(new string('a', StoryReflectionPolicy.MaxNoteTextLength), StorySummaryPolicy.MaxAudioBytes));

        var row = Assert.Single(_h.Reflections);
        Assert.Equal(200, row.NoteText!.Length);
        Assert.Equal(StorySummaryPolicy.MaxAudioBytes, row.NoteAudioBytes);
    }

    [Fact]
    public async Task SendNoteAsync_UploadFailure_PreservesPreviousNote()
    {
        VoiceOn();
        var (session, _) = Chosen();
        var row = Assert.Single(_h.Reflections);
        row.NoteText = "Previous note";
        row.NoteAudioStoragePath = "old.webm";
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("storage down"));

        await Assert.ThrowsAsync<IOException>(() => _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note("New note", 10)));

        Assert.Equal("Previous note", row.NoteText);
        Assert.Equal("old.webm", row.NoteAudioStoragePath);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetFavoriteSceneAsync_SaveFails_DoesNotDeletePreviousAudio()
    {
        var (session, _) = Chosen();
        var next = _h.AddScene(1, "Another scene");
        Assert.Single(_h.Reflections).NoteAudioStoragePath = "old.webm";
        _h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("db down"));

        await Assert.ThrowsAsync<IOException>(() => _sut.SetFavoriteSceneAsync(ReadingHarness.Child, session.Id,
            new SetFavoriteSceneRequestDto { SceneId = next.Id }));

        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetFavoriteSceneAsync_DoesNotUseAnotherPackagesIllustration()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var scene = _h.AddScene(0, "Scene");
        var original = _h.AddIllustration(scene, "original.png");
        var other = _h.AddIllustration(scene, "other-package.png");
        other.ParentStoryPackageId = 99;

        await _sut.SetFavoriteSceneAsync(ReadingHarness.Child, session.Id, new SetFavoriteSceneRequestDto { SceneId = scene.Id });

        Assert.Equal(original.Id, Assert.Single(_h.Reflections).FavoriteMediaAssetId);
    }

    [Theory]
    [InlineData("emotions")]
    [InlineData("scene")]
    [InlineData("note")]
    public async Task EveryWrite_RequiresACompletedSession(string operation)
    {
        var (session, scene) = Chosen();
        session.Status = SessionStatus.Reading;
        Func<Task> action = operation switch
        {
            "emotions" => () => _sut.SetEmotionsAsync(ReadingHarness.Child, session.Id, Emotions("Happy")),
            "scene" => () => _sut.SetFavoriteSceneAsync(ReadingHarness.Child, session.Id, new SetFavoriteSceneRequestDto { SceneId = scene.Id }),
            _ => () => _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note("Note"))
        };

        await Assert.ThrowsAsync<ConflictException>(action);
        _storage.VerifyNoOtherCalls();
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private readonly ReadingHarness _h = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ChildStoryReflectionService _sut;

    public ChildStoryReflectionServiceTests()
    {
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, string _, CancellationToken _) => path);
        _sut = new ChildStoryReflectionService(_h.UnitOfWork.Object, _storage.Object,
            NullLogger<ChildStoryReflectionService>.Instance);
    }

    private void VoiceOn(bool enabled = true) => _h.Policies.Add(new SafetyPolicy
    {
        Id = _h.Policies.Count + 1, ChildProfileId = ReadingHarness.Child, VoiceAnswersEnabled = enabled
    });

    private static SetStoryEmotionsRequestDto Emotions(params string[] codes) => new() { Emotions = codes.ToList() };

    [Fact]
    public async Task SetEmotionsAsync_SavesOneOrTwo_WithLabels()
    {
        var session = _h.AddSession(SessionStatus.Completed);

        var result = await _sut.SetEmotionsAsync(ReadingHarness.Child, session.Id, Emotions("happy", "Touched"));

        Assert.Equal(new[] { "Happy", "Touched" }, result.Emotions.Select(e => e.Code));
        Assert.Equal(new[] { "Vui", "Cảm động" }, result.Emotions.Select(e => e.Label));
        var row = Assert.Single(_h.Reflections);
        Assert.Equal((session.Id, ReadingHarness.Child, ReadingHarness.Story, (int?)ReadingHarness.Version),
            (row.ReadingSessionId, row.ChildProfileId, row.StoryId, row.StoryVersionId));
        Assert.Equal(new[] { "Happy", "Touched" }, row.Emotions);
    }

    [Fact]
    public async Task SetEmotionsAsync_Again_ReplacesTheList()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        await _sut.SetEmotionsAsync(ReadingHarness.Child, session.Id, Emotions("Happy", "Touched"));

        await _sut.SetEmotionsAsync(ReadingHarness.Child, session.Id, Emotions("Scared"));

        Assert.Equal(new[] { "Scared" }, Assert.Single(_h.Reflections).Emotions);
    }

    [Theory]
    [InlineData(new string[0], StoryReflectionPolicy.ChooseEmotionMessage)]
    [InlineData(new[] { "Angry" }, StoryReflectionPolicy.ChooseEmotionMessage)]
    [InlineData(new[] { "1" }, StoryReflectionPolicy.ChooseEmotionMessage)]
    [InlineData(new[] { "Happy", "happy" }, StoryReflectionPolicy.ChooseEmotionMessage)]
    [InlineData(new[] { "Happy", "Sad", "Funny" }, StoryReflectionPolicy.TooManyEmotionsMessage)]
    public async Task SetEmotionsAsync_InvalidChoice_IsBadRequest(string[] codes, string message)
    {
        var session = _h.AddSession(SessionStatus.Completed);

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetEmotionsAsync(ReadingHarness.Child, session.Id, Emotions(codes)));

        Assert.Equal(message, error.Message);
        Assert.Empty(_h.Reflections);
    }

    [Fact]
    public async Task SetEmotionsAsync_NullList_IsBadRequest()
    {
        var session = _h.AddSession(SessionStatus.Completed);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetEmotionsAsync(ReadingHarness.Child, session.Id, new SetStoryEmotionsRequestDto()));
    }

    [Fact]
    public async Task SetEmotionsAsync_BeforeFinishing_IsConflict()
    {
        var session = _h.AddSession(SessionStatus.Reading);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.SetEmotionsAsync(ReadingHarness.Child, session.Id, Emotions("Happy")));

        Assert.Equal(ChildReadingPolicy.FeelingAfterReadingMessage, error.Message);
    }

    [Fact]
    public async Task SetEmotionsAsync_InterruptedOrOtherChild_IsRejected()
    {
        var interrupted = _h.AddSession(SessionStatus.Completed);
        interrupted.ForceExitRequestedAt = DateTime.UtcNow;
        var other = _h.AddSession(SessionStatus.Completed, childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SetEmotionsAsync(ReadingHarness.Child, interrupted.Id, Emotions("Happy")));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.SetEmotionsAsync(ReadingHarness.Child, other.Id, Emotions("Happy")));
    }

    [Fact]
    public async Task SetFavoriteSceneAsync_SavesSceneAndThePictureTheChildSaw()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var scene = _h.AddScene(2, "Mèo tìm được nhà.");
        var picture = _h.AddIllustration(scene, "scene-2.png");

        var result = await _sut.SetFavoriteSceneAsync(ReadingHarness.Child, session.Id, new SetFavoriteSceneRequestDto { SceneId = scene.Id });

        Assert.Equal(scene.Id, result.FavoriteSceneId);
        var row = Assert.Single(_h.Reflections);
        Assert.Equal((scene.Id, picture.Id), (row.FavoriteSceneId!.Value, row.FavoriteMediaAssetId!.Value));
    }

    [Fact]
    public async Task SetFavoriteSceneAsync_SceneOfAnotherVersion_IsBadRequest()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var scene = _h.AddScene(0, "Chuyện khác.", versionId: 999);

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetFavoriteSceneAsync(ReadingHarness.Child, session.Id, new SetFavoriteSceneRequestDto { SceneId = scene.Id }));

        Assert.Equal(StoryReflectionPolicy.SceneNotInStoryMessage, error.Message);
    }

    [Fact]
    public async Task SetFavoriteSceneAsync_ChangingScene_DropsOldNote_AndDeletesItsFile()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var first = _h.AddScene(0, "Một.");
        var second = _h.AddScene(1, "Hai.");
        _h.Reflections.Add(new StoryReflection
        {
            Id = 1, ReadingSessionId = session.Id, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story,
            StoryVersionId = ReadingHarness.Version, FavoriteSceneId = first.Id, NoteText = "Con thích mèo",
            NoteAudioStoragePath = "old.webm", NoteAudioContentType = "audio/webm", NoteAudioBytes = 10, NoteSentAt = DateTime.UtcNow
        });

        await _sut.SetFavoriteSceneAsync(ReadingHarness.Child, session.Id, new SetFavoriteSceneRequestDto { SceneId = second.Id });

        var row = Assert.Single(_h.Reflections);
        Assert.Equal(second.Id, row.FavoriteSceneId);
        Assert.Null(row.NoteText);
        Assert.Null(row.NoteAudioStoragePath);
        Assert.Null(row.NoteSentAt);
        _storage.Verify(s => s.DeleteAsync("old.webm", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetFavoriteSceneAsync_SameSceneAgain_KeepsNote()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var scene = _h.AddScene(0, "Một.");
        _h.Reflections.Add(new StoryReflection
        {
            Id = 1, ReadingSessionId = session.Id, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story,
            FavoriteSceneId = scene.Id, NoteText = "Con thích mèo"
        });

        await _sut.SetFavoriteSceneAsync(ReadingHarness.Child, session.Id, new SetFavoriteSceneRequestDto { SceneId = scene.Id });

        Assert.Equal("Con thích mèo", Assert.Single(_h.Reflections).NoteText);
        _storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAsync_ReportsWhetherTheMicCanBeShown(bool voice)
    {
        VoiceOn(voice);
        var session = _h.AddSession(SessionStatus.Completed);

        var result = await _sut.GetAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(voice, result.CanRecord);
        Assert.Empty(result.Emotions);
        Assert.Null(result.FavoriteSceneId);
    }
    private (ReadingSession Session, StoryScene Scene) Chosen()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var scene = _h.AddScene(0, "Mèo tìm được nhà.");
        _h.Reflections.Add(new StoryReflection
        {
            Id = 1, ReadingSessionId = session.Id, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story,
            StoryVersionId = ReadingHarness.Version, FavoriteSceneId = scene.Id
        });
        return (session, scene);
    }

    private static StoryReflectionNoteRequest Note(string? text = null, int audioBytes = 0, string? contentType = "audio/webm;codecs=opus") =>
        new() { Text = text, Audio = new byte[audioBytes], ContentType = audioBytes == 0 ? null : contentType };

    [Fact]
    public async Task SendNoteAsync_TextOnly_WorksEvenWhenVoiceIsOff()
    {
        VoiceOn(false);
        var (session, _) = Chosen();

        var result = await _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note("  Con thích mèo con  "));

        var row = Assert.Single(_h.Reflections);
        Assert.Equal("Con thích mèo con", row.NoteText);
        Assert.Null(row.NoteAudioStoragePath);
        Assert.NotNull(row.NoteSentAt);
        Assert.Equal(StoryReflectionPolicy.NoteSentMessage, result.Message);
        Assert.False(result.CanRecord);
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendNoteAsync_WithAudio_UploadsUnderTheChildSessionFolder()
    {
        VoiceOn();
        var (session, _) = Chosen();

        var result = await _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note("Mèo đáng yêu", 10));

        var row = Assert.Single(_h.Reflections);
        Assert.StartsWith($"story-reflections/{ReadingHarness.Child}/{session.Id}/", row.NoteAudioStoragePath);
        Assert.EndsWith(".webm", row.NoteAudioStoragePath);
        Assert.Equal(("audio/webm", 10), (row.NoteAudioContentType, row.NoteAudioBytes!.Value));
        Assert.True(result.HasNoteRecording);
        _storage.Verify(s => s.UploadAsync(row.NoteAudioStoragePath!, It.IsAny<Stream>(), "audio/webm", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendNoteAsync_Again_ReplacesNote_AndDeletesOldFile()
    {
        VoiceOn();
        var (session, _) = Chosen();
        await _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note(null, 10));
        var oldPath = Assert.Single(_h.Reflections).NoteAudioStoragePath!;

        await _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note("Chỉ viết thôi"));

        var row = Assert.Single(_h.Reflections);
        Assert.Equal("Chỉ viết thôi", row.NoteText);
        Assert.Null(row.NoteAudioStoragePath);
        _storage.Verify(s => s.DeleteAsync(oldPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendNoteAsync_AudioWhenVoiceOff_IsForbidden_AndUploadsNothing()
    {
        VoiceOn(false);
        var (session, _) = Chosen();

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note("Mèo", 10)));

        Assert.Equal(ReadAloudPolicy.VoiceOffMessage, error.Message);
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendNoteAsync_BeforeChoosingScene_IsBadRequest()
    {
        var session = _h.AddSession(SessionStatus.Completed);

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note("Mèo")));

        Assert.Equal(StoryReflectionPolicy.ChooseSceneFirstMessage, error.Message);
    }

    [Theory]
    [InlineData(null, 0, null, StoryReflectionPolicy.EmptyNoteMessage)]
    [InlineData("   ", 0, null, StoryReflectionPolicy.EmptyNoteMessage)]
    [InlineData(null, StorySummaryPolicy.MaxAudioBytes + 1, "audio/webm", StorySummaryPolicy.AudioTooLargeMessage)]
    [InlineData(null, 10, "video/mp4", ReadAloudPolicy.UnsupportedAudioMessage)]
    public async Task SendNoteAsync_InvalidNote_IsBadRequest(string? text, int bytes, string? contentType, string message)
    {
        VoiceOn();
        var (session, _) = Chosen();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note(text, bytes, contentType)));

        Assert.Equal(message, error.Message);
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendNoteAsync_TextTooLong_IsBadRequest()
    {
        var (session, _) = Chosen();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note(new string('a', StoryReflectionPolicy.MaxNoteTextLength + 1))));

        Assert.Equal(StoryReflectionPolicy.NoteTooLongMessage, error.Message);
    }

    [Fact]
    public async Task SendNoteAsync_SaveFails_DeletesTheNewFile()
    {
        VoiceOn();
        var (session, _) = Chosen();
        _h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note(null, 10)));

        _storage.Verify(s => s.DeleteAsync(It.Is<string>(p => p.StartsWith("story-reflections/")), It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("emotions")]
    [InlineData("scene")]
    [InlineData("note")]
    public async Task EveryWrite_LocksTheSession_BeforeReadingTheReflection(string operation)
    {
        VoiceOn();
        var (session, scene) = Chosen();
        var next = _h.AddScene(1, "Hai.");
        _h.UnitOfWork.Invocations.Clear();

        await (operation switch
        {
            "emotions" => _sut.SetEmotionsAsync(ReadingHarness.Child, session.Id, Emotions("Happy")),
            "scene" => _sut.SetFavoriteSceneAsync(ReadingHarness.Child, session.Id, new SetFavoriteSceneRequestDto { SceneId = next.Id }),
            _ => _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note("Mèo", 10))
        });

        // Bé bấm hai lần cùng lúc: request sau chờ khóa của phiên rồi mới đọc bản cảm nhận,
        // nên không thêm trùng dòng và không để file ghi âm của lần "thua" nằm lại không dòng nào trỏ tới.
        var calls = _h.UnitOfWork.Invocations.Select(call => call.Method).ToList();
        var begin = calls.FindIndex(method => method.Name == nameof(IUnitOfWork.BeginTransactionAsync));
        var locked = calls.FindIndex(method => method.Name == nameof(IUnitOfWork.AcquireTransactionLockAsync));
        var commit = calls.FindIndex(method => method.Name == nameof(IUnitOfWork.CommitTransactionAsync));
        Assert.True(begin >= 0 && begin < locked && locked < commit, $"begin={begin}, lock={locked}, commit={commit}");
        Assert.Contains(calls.Select((method, index) => (method, index)), call =>
            call.method.Name == nameof(IUnitOfWork.Repository) && call.method.GetGenericArguments()[0] == typeof(StoryReflection)
            && call.index > locked && call.index < commit);
        _h.UnitOfWork.Verify(u => u.AcquireTransactionLockAsync(session.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendNoteAsync_SceneChangedWhileUploading_IsConflict_AndDeletesTheNewFile()
    {
        VoiceOn();
        var (session, _) = Chosen();
        var next = _h.AddScene(1, "Hai.");
        var row = Assert.Single(_h.Reflections);
        string? uploaded = null;
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, string, CancellationToken>((path, _, _, _) =>
            {
                uploaded = path;
                row.FavoriteSceneId = next.Id; // request "đổi cảnh" chạy xong trong lúc file đang upload
            })
            .ReturnsAsync((string path, Stream _, string _, CancellationToken _) => path);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.SendNoteAsync(ReadingHarness.Child, session.Id, Note("Mèo", 10)));

        Assert.Equal(StoryReflectionPolicy.SceneChangedMessage, error.Message);
        Assert.Null(row.NoteText);
        Assert.Null(row.NoteAudioStoragePath);
        _storage.Verify(s => s.DeleteAsync(uploaded!, It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
