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

public class ParentStorySummaryServiceTests
{
    private const int Parent = 7;
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ParentStorySummaryService _sut;

    public ParentStorySummaryServiceTests()
    {
        _storage.Setup(s => s.GetSignedUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, TimeSpan _, CancellationToken _) => $"https://signed/{path}");
        _sut = new ParentStorySummaryService(_h.UnitOfWork.Object, _guard.Object, _storage.Object);
    }

    private StorySummary AddSummary(string? audioPath = "story-summaries/100/1/a.webm", int childId = ReadingHarness.Child)
    {
        var session = _h.AddSession(SessionStatus.Completed, childId);
        var summary = new StorySummary
        {
            Id = _h.Summaries.Count + 1, ReadingSessionId = session.Id, ChildProfileId = childId,
            StoryId = ReadingHarness.Story, StoryVersionId = ReadingHarness.Version,
            AudioStoragePath = audioPath, RecordingSentAt = audioPath == null ? null : DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddMinutes(-_h.Summaries.Count)
        };
        _h.Summaries.Add(summary);
        return summary;
    }

    [Fact]
    public async Task ListAsync_ReturnsSignedRecordingUrl_AndFlaggedScenesWithIllustration()
    {
        var summary = AddSummary();
        var scene = _h.AddScene(2, "Thỏ gặp rùa.");
        _h.AddIllustration(scene, "https://img/scene-2.png");
        summary.FlagsSentAt = DateTime.UtcNow;
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = scene.Id });

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        var item = Assert.Single(page.Items);
        Assert.Equal("Truyện 10", item.Title);
        Assert.Equal("https://signed/story-summaries/100/1/a.webm", item.RecordingUrl);
        Assert.NotNull(item.FlagsSentAt);
        var flag = Assert.Single(item.Flags);
        Assert.Equal((scene.Id, 2, "Thỏ gặp rùa.", "https://img/scene-2.png"),
            (flag.SceneId, flag.SceneIndex, flag.SceneText, flag.IllustrationUrl));
        _guard.Verify(g => g.EnsureOwnerAsync(ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.GetSignedUrlAsync("story-summaries/100/1/a.webm", StorySummaryPolicy.ListenUrlLifetime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_HidesDeletedAndOtherChildren_AndHasNoUrlWithoutRecording()
    {
        AddSummary(audioPath: null);
        AddSummary().IsDeleted = true;
        AddSummary(childId: ReadingHarness.OtherChild);

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        var item = Assert.Single(page.Items);
        Assert.Null(item.RecordingUrl);
        _storage.Verify(s => s.GetSignedUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_NotOwner_PropagatesGuardFailure()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(ReadingHarness.Child, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("x"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(99, ReadingHarness.Child, new ParentStorySummaryQueryDto()));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_AndSoftDeletesSummaryAndFlags()
    {
        var summary = AddSummary();
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = 5 });

        await _sut.DeleteAsync(Parent, ReadingHarness.Child, summary.Id);

        _storage.Verify(s => s.DeleteAsync("story-summaries/100/1/a.webm", It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(summary.IsDeleted);
        Assert.Null(summary.AudioStoragePath);
        Assert.All(_h.SummaryFlags, f => Assert.True(f.IsDeleted));
    }

    [Fact]
    public async Task DeleteAsync_NotOwner_DeletesNothing()
    {
        var summary = AddSummary();
        _guard.Setup(g => g.EnsureOwnerAsync(ReadingHarness.Child, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("x"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.DeleteAsync(99, ReadingHarness.Child, summary.Id));

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(summary.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_SummaryOfAnotherChild_IsNotFound()
    {
        var summary = AddSummary(childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(Parent, ReadingHarness.Child, summary.Id));

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(summary.IsDeleted);
    }

    [Fact]
    public async Task PurgeChildAsync_DeletesEveryRecordingOfTheChild()
    {
        var first = AddSummary("p/1.webm");
        var second = AddSummary("p/2.webm");
        var other = AddSummary("p/3.webm", ReadingHarness.OtherChild);

        await _sut.PurgeChildAsync(ReadingHarness.Child);

        _storage.Verify(s => s.DeleteAsync("p/1.webm", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("p/2.webm", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("p/3.webm", It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(first.IsDeleted && second.IsDeleted);
        Assert.Null(first.AudioStoragePath);
        Assert.False(other.IsDeleted);
    }
}
