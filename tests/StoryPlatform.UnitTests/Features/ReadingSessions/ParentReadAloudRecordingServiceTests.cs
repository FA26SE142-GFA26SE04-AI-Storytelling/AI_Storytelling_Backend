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

public class ParentReadAloudRecordingServiceTests
{
    private const int Parent = 7;
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ParentReadAloudRecordingService _sut;

    public ParentReadAloudRecordingServiceTests()
    {
        _storage.Setup(s => s.GetSignedUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, TimeSpan _, CancellationToken _) => $"https://signed.test/{path}");
        _sut = new ParentReadAloudRecordingService(_h.UnitOfWork.Object, _guard.Object, _storage.Object);
    }

    private ReadAloudRecording Recording(ReadingSession session, int? segmentId, int? interactionId, string path, int good = 3, int total = 4)
    {
        var row = new ReadAloudRecording
        {
            Id = _h.ReadAloudRecordings.Count + 1, ReadingSessionId = session.Id, ChildProfileId = session.ChildProfileId,
            StorySegmentId = segmentId, ListeningInteractionId = interactionId, AudioStoragePath = path,
            AudioContentType = "audio/webm", AudioBytes = 10, GoodCount = good, RetryCount = total - good,
            TotalCount = total, RecordedAt = DateTime.UtcNow
        };
        _h.ReadAloudRecordings.Add(row);
        return row;
    }

    [Fact]
    public async Task ListAsync_ExcludesDeletedRows_AndOtherSessions()
    {
        var session = _h.AddSession();
        var other = _h.AddSession();
        Recording(session, 1, null, "deleted.webm").IsDeleted = true;
        Recording(other, 1, null, "other.webm");

        var result = await _sut.ListAsync(Parent, ReadingHarness.Child, session.Id);

        Assert.Empty(result.Recordings);
        _storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonOwner_IsRejectedBeforeStorage(bool delete)
    {
        var session = _h.AddSession();
        var row = Recording(session, 1, null, "private.webm");
        _guard.Setup(g => g.EnsureOwnerAsync(ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Bạn không có quyền với hồ sơ trẻ này."));

        if (delete)
            await Assert.ThrowsAsync<ForbiddenException>(() => _sut.DeleteAsync(Parent, ReadingHarness.Child, session.Id, row.Id));
        else
            await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(Parent, ReadingHarness.Child, session.Id));

        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAsync_StorageFails_DoesNotHideRecording()
    {
        var session = _h.AddSession();
        var row = Recording(session, 1, null, "private.webm");
        _storage.Setup(s => s.DeleteAsync(row.AudioStoragePath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("storage down"));

        await Assert.ThrowsAsync<IOException>(() => _sut.DeleteAsync(Parent, ReadingHarness.Child, session.Id, row.Id));

        Assert.False(row.IsDeleted);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_ReturnsSegmentsInPageOrder_ThenPractice_WithSignedUrls()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var page2 = _h.AddScene(1, "Trang hai.");
        var page1 = _h.AddScene(0, "Trang một.");
        var late = _h.AddSegment(page2, 1, "Trang hai.");
        var early = _h.AddSegment(page1, 1, "Trang một.");
        _h.ListeningInteractions.Add(new ListeningInteraction
        {
            Id = 70, StoryVersionId = ReadingHarness.Version, Type = ListeningInteractionType.ReadAloud, SourceQuote = "Đoạn luyện."
        });
        Recording(session, null, 70, "p.webm", 2, 2);
        Recording(session, late.Id, null, "b.webm");
        Recording(session, early.Id, null, "a.webm");

        var result = await _sut.ListAsync(Parent, ReadingHarness.Child, session.Id);

        Assert.Equal(new[] { "Trang một.", "Trang hai.", "Đoạn luyện." }, result.Recordings.Select(r => r.Text));
        Assert.Equal(new[] { "Segment", "Segment", "Practice" }, result.Recordings.Select(r => r.Kind));
        Assert.Equal("https://signed.test/a.webm", result.Recordings[0].AudioUrl);
        Assert.Equal(75m, result.Recordings[0].GoodPercent);
        Assert.Equal(0, result.Recordings[0].SceneIndex);
        _storage.Verify(s => s.GetSignedUrlAsync("a.webm", ReadAloudPolicy.ListenUrlLifetime, It.IsAny<CancellationToken>()), Times.Once);
        _guard.Verify(g => g.EnsureOwnerAsync(ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_SessionOfAnotherChild_IsNotFound()
    {
        var session = _h.AddSession(childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListAsync(Parent, ReadingHarness.Child, session.Id));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_AndHidesRow()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var segment = _h.AddSegment(_h.AddScene(0, "Thỏ."), 1, "Thỏ.");
        var row = Recording(session, segment.Id, null, "a.webm");

        await _sut.DeleteAsync(Parent, ReadingHarness.Child, session.Id, row.Id);

        Assert.True(row.IsDeleted);
        _storage.Verify(s => s.DeleteAsync("a.webm", It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_RecordingOfAnotherSession_IsNotFound()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var other = _h.AddSession(SessionStatus.Completed);
        var row = Recording(other, 1, null, "a.webm");

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(Parent, ReadingHarness.Child, session.Id, row.Id));
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PurgeChildAsync_DeletesEveryFileOfTheChild_Only()
    {
        var mine = _h.AddSession();
        var theirs = _h.AddSession(childId: ReadingHarness.OtherChild);
        Recording(mine, 1, null, "mine.webm");
        Recording(theirs, 1, null, "theirs.webm");

        await _sut.PurgeChildAsync(ReadingHarness.Child);

        _storage.Verify(s => s.DeleteAsync("mine.webm", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("theirs.webm", It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(_h.ReadAloudRecordings.Single(r => r.AudioStoragePath == "mine.webm").IsDeleted);
    }
}
