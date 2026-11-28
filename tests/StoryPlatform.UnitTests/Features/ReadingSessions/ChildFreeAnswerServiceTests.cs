using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ChildFreeAnswerServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ChildFreeAnswerService _sut;
    private readonly ReadingSession _session;

    public ChildFreeAnswerServiceTests()
    {
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((string path, Stream _, string _, CancellationToken _) => path);
        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _sut = new ChildFreeAnswerService(_h.UnitOfWork.Object, _storage.Object,
            NullLogger<ChildFreeAnswerService>.Instance);
        _session = _h.AddSession();
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 9;
        var scene = _h.AddScene(0, "Scene");
        _h.ListeningInteractions.Add(new ListeningInteraction
        {
            Id = 91, StoryVersionId = ReadingHarness.Version, StorySceneId = scene.Id,
            TargetAge = 9, Type = ListeningInteractionType.WhatIf
        });
        _h.Policies.Add(new SafetyPolicy { Id = 1, ChildProfileId = ReadingHarness.Child, VoiceAnswersEnabled = true });
    }

    private static ReadAloudRequest Audio(byte[]? bytes = null, string contentType = "audio/webm") => new()
        { Audio = bytes ?? [1, 2, 3], ContentType = contentType };

    [Fact]
    public async Task SavesOneRecording_AndReplacesItsFileOnSecondUpload()
    {
        Assert.True(await _sut.SaveAsync(ReadingHarness.Child, _session.Id, 91, Audio()));
        var old = Assert.Single(_h.ReadAloudRecordings).AudioStoragePath;
        Assert.True(await _sut.SaveAsync(ReadingHarness.Child, _session.Id, 91, Audio([4, 5])));

        var current = Assert.Single(_h.ReadAloudRecordings);
        Assert.NotEqual(old, current.AudioStoragePath);
        Assert.Equal(0, current.TotalCount);
        _storage.Verify(s => s.DeleteAsync(old!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VoicePolicyAndAudioFormatAreCheckedBeforeUpload()
    {
        _h.Policies.Single().VoiceAnswersEnabled = false;
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SaveAsync(ReadingHarness.Child, _session.Id, 91, Audio()));
        _h.Policies.Single().VoiceAnswersEnabled = true;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveAsync(ReadingHarness.Child, _session.Id, 91, Audio([])));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveAsync(ReadingHarness.Child, _session.Id, 91,
            Audio(contentType: "text/plain")));
        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WrongAgeOrInteractionTypeCannotSaveAudio()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 8;
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SaveAsync(ReadingHarness.Child, _session.Id, 91, Audio()));
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 9;
        _h.ListeningInteractions.Single().Type = ListeningInteractionType.StoryEditor;
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SaveAsync(ReadingHarness.Child, _session.Id, 91, Audio()));
    }

    [Fact]
    public async Task SessionOfAnotherChild_OrAnEndedSession_CannotSaveAudio()
    {
        var other = _h.AddSession(childId: ReadingHarness.OtherChild);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SaveAsync(ReadingHarness.Child, other.Id, 91, Audio()));
        var ended = _h.AddSession(SessionStatus.Completed);
        await Assert.ThrowsAsync<ConflictException>(() => _sut.SaveAsync(ReadingHarness.Child, ended.Id, 91, Audio()));
        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InteractionOfAnotherStoryVersion_OrOversizedAudio_IsRejected()
    {
        _h.ListeningInteractions.Single().StoryVersionId = ReadingHarness.Version + 1;
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SaveAsync(ReadingHarness.Child, _session.Id, 91, Audio()));
        _h.ListeningInteractions.Single().StoryVersionId = ReadingHarness.Version;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveAsync(ReadingHarness.Child, _session.Id, 91,
            Audio(new byte[ReadAloudPolicy.MaxAudioBytes + 1])));
    }
}
