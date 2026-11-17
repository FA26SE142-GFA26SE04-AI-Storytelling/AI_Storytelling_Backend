using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ParentStoryReflectionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonOwner_IsRejectedBeforeReadingPrivateMedia(bool delete)
    {
        var session = _h.AddSession(SessionStatus.Completed);
        Reflection(session, text: "Private", audio: "private.webm");
        _guard.Setup(g => g.EnsureOwnerAsync(ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Not owner"));

        if (delete)
            await Assert.ThrowsAsync<ForbiddenException>(() => _sut.DeleteNoteAsync(Parent, ReadingHarness.Child, session.Id));
        else
            await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetAsync(Parent, ReadingHarness.Child, session.Id));

        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_SignsTheSavedIllustration_InsteadOfUsingANewerPicture()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var scene = _h.AddScene(0, "Scene");
        var saved = _h.AddIllustration(scene, "saved.png");
        _h.AddIllustration(scene, "new.png");
        Reflection(session, scene.Id, saved.Id);
        var signer = new ChildMediaUrlSigner(_storage.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChildMediaUrlSigner>.Instance);
        var sut = new ParentStoryReflectionService(_h.UnitOfWork.Object, _guard.Object, _storage.Object, signer);

        var result = await sut.GetAsync(Parent, ReadingHarness.Child, session.Id);

        Assert.Equal("https://signed.test/saved.png", result.FavoriteScene!.IllustrationUrl);
        _storage.Verify(s => s.GetSignedUrlAsync("new.png", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_DeletedScene_IsNotReturned()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var scene = _h.AddScene(0, "Scene");
        scene.IsDeleted = true;
        Reflection(session, scene.Id);

        var result = await _sut.GetAsync(Parent, ReadingHarness.Child, session.Id);

        Assert.Null(result.FavoriteScene);
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteNoteAsync_StorageFails_DoesNotClearNote()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var row = Reflection(session, text: "Previous", audio: "old.webm");
        _storage.Setup(s => s.DeleteAsync("old.webm", It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("storage down"));

        await Assert.ThrowsAsync<IOException>(() => _sut.DeleteNoteAsync(Parent, ReadingHarness.Child, session.Id));

        Assert.Equal("Previous", row.NoteText);
        Assert.Equal("old.webm", row.NoteAudioStoragePath);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private const int Parent = 7;
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ParentStoryReflectionService _sut;

    public ParentStoryReflectionServiceTests()
    {
        _storage.Setup(s => s.GetSignedUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, TimeSpan _, CancellationToken _) => $"https://signed.test/{path}");
        _sut = new ParentStoryReflectionService(_h.UnitOfWork.Object, _guard.Object, _storage.Object);
    }

    private StoryReflection Reflection(ReadingSession session, int? sceneId = null, int? assetId = null,
        string? text = null, string? audio = null, params string[] emotions)
    {
        var row = new StoryReflection
        {
            Id = _h.Reflections.Count + 1, ReadingSessionId = session.Id, ChildProfileId = session.ChildProfileId,
            StoryId = session.StoryId, StoryVersionId = session.StoryVersionId, Emotions = emotions.ToList(),
            FavoriteSceneId = sceneId, FavoriteMediaAssetId = assetId, NoteText = text, NoteAudioStoragePath = audio,
            NoteSentAt = text != null || audio != null ? DateTime.UtcNow : null
        };
        _h.Reflections.Add(row);
        return row;
    }

    [Fact]
    public async Task GetAsync_ReturnsEmotionsSceneNoteAndSignedAudio()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var scene = _h.AddScene(2, "Mèo tìm được nhà.");
        var picture = _h.AddIllustration(scene, "https://cdn.test/scene-2.png");
        Reflection(session, scene.Id, picture.Id, "Con thích mèo", "note.webm", "Happy", "Touched");

        var result = await _sut.GetAsync(Parent, ReadingHarness.Child, session.Id);

        Assert.Equal(new[] { "Vui", "Cảm động" }, result.Emotions.Select(e => e.Label));
        Assert.Equal((scene.Id, 2, "Mèo tìm được nhà."), (result.FavoriteScene!.SceneId, result.FavoriteScene.SceneIndex, result.FavoriteScene.SceneText));
        Assert.Equal("https://cdn.test/scene-2.png", result.FavoriteScene.IllustrationUrl);
        Assert.Equal("Con thích mèo", result.NoteText);
        Assert.Equal("https://signed.test/note.webm", result.NoteAudioUrl);
        _storage.Verify(s => s.GetSignedUrlAsync("note.webm", StoryReflectionPolicy.ListenUrlLifetime, It.IsAny<CancellationToken>()), Times.Once);
        _guard.Verify(g => g.EnsureOwnerAsync(ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_NoReflectionYet_ReturnsEmpty()
    {
        var session = _h.AddSession(SessionStatus.Completed);

        var result = await _sut.GetAsync(Parent, ReadingHarness.Child, session.Id);

        Assert.Empty(result.Emotions);
        Assert.Null(result.FavoriteScene);
        Assert.Null(result.NoteAudioUrl);
    }

    [Fact]
    public async Task GetAsync_SessionOfAnotherChild_IsNotFound()
    {
        var session = _h.AddSession(childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Parent, ReadingHarness.Child, session.Id));
    }

    [Fact]
    public async Task DeleteNoteAsync_RemovesFileAndText_KeepsEmotionsAndScene()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var row = Reflection(session, 5, null, "Con thích mèo", "note.webm", "Happy");

        await _sut.DeleteNoteAsync(Parent, ReadingHarness.Child, session.Id);

        Assert.Null(row.NoteText);
        Assert.Null(row.NoteAudioStoragePath);
        Assert.Equal(5, row.FavoriteSceneId);
        Assert.Equal(new[] { "Happy" }, row.Emotions);
        _storage.Verify(s => s.DeleteAsync("note.webm", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteNoteAsync_NothingToDelete_IsNotFound()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        Reflection(session, 5, null, null, null, "Happy");

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteNoteAsync(Parent, ReadingHarness.Child, session.Id));
    }

    [Fact]
    public async Task PurgeChildAsync_DeletesFilesOfThatChildOnly()
    {
        var mine = _h.AddSession(SessionStatus.Completed);
        var theirs = _h.AddSession(SessionStatus.Completed, childId: ReadingHarness.OtherChild);
        Reflection(mine, 1, null, null, "mine.webm");
        Reflection(theirs, 1, null, null, "theirs.webm");

        await _sut.PurgeChildAsync(ReadingHarness.Child);

        _storage.Verify(s => s.DeleteAsync("mine.webm", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("theirs.webm", It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(_h.Reflections.Single(r => r.ReadingSessionId == mine.Id).IsDeleted);
    }
}
