using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ChildNotebookServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly ChildNotebookService _sut;

    public ChildNotebookServiceTests()
    {
        _sut = new ChildNotebookService(_h.UnitOfWork.Object);
    }

    [Fact]
    public async Task GetDefinitionAsync_ReturnsMeaning_AndLogsOpen()
    {
        var session = _h.AddSession();
        _h.AddVocabulary(1, "cà rốt", "củ màu cam");

        var result = await _sut.GetDefinitionAsync(ReadingHarness.Child, session.Id, 1);

        Assert.Equal("củ màu cam", result.Definition);
        Assert.False(result.IsCollected);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.VocabularyOpened);
    }

    [Fact]
    public async Task GetDefinitionAsync_ReturnsPronunciationAudio_WhenReady()
    {
        var session = _h.AddSession();
        var word = _h.AddVocabulary(1, "cà rốt", "củ màu cam");
        _h.AddVocabularyAudio(word, "10/versions/50/v1/vocab-1.mp3");

        var result = await _sut.GetDefinitionAsync(ReadingHarness.Child, session.Id, 1);

        Assert.Equal("10/versions/50/v1/vocab-1.mp3", result.AudioUrl);
    }

    [Fact]
    public async Task GetDefinitionAsync_WordFromAnotherStoryVersion_IsNotFound()
    {
        var session = _h.AddSession();
        _h.Vocabulary.Add(new StoryVocabulary { Id = 7, StoryVersionId = 999, Term = "lạ", Definition = "x" });

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetDefinitionAsync(ReadingHarness.Child, session.Id, 7));
    }

    [Fact]
    public async Task CollectAsync_SavesOnce_EvenWhenCalledTwice()
    {
        var session = _h.AddSession();
        _h.AddVocabulary(1, "cà rốt");

        var first = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);
        var second = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);

        Assert.Single(_h.Notebook);
        Assert.Equal(first.EntryId, second.EntryId);
        Assert.Single(_h.Telemetry, t => t.EventType == TelemetryEventType.VocabularyCollected);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyMyWords_WithFirstSceneContainingTheTerm()
    {
        var session = _h.AddSession();
        _h.AddScene(0, "Thỏ ra vườn.");
        _h.AddScene(1, "Thỏ thấy cà rốt to.");
        _h.AddVocabulary(1, "cà rốt");
        _h.AddVocabulary(2, "vườn");
        await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);
        await _sut.CollectAsync(ReadingHarness.Child, session.Id, 2);
        _h.Notebook.Add(new VocabularyNotebookEntry
            { Id = 50, ChildProfileId = ReadingHarness.OtherChild, StoryVocabularyId = 1, CollectedAt = DateTime.UtcNow });

        var list = await _sut.ListAsync(ReadingHarness.Child);

        Assert.Equal(2, list.Count);
        Assert.Equal(1, list.Single(e => e.Term == "cà rốt").SceneIndex);
        Assert.Equal(0, list.Single(e => e.Term == "vườn").SceneIndex);
        Assert.All(list, e => Assert.Equal("Truyện 10", e.StoryTitle));
    }

    [Fact]
    public async Task ListAsync_CarriesPronunciationAudio_AndNullWhenMissing()
    {
        var session = _h.AddSession();
        _h.AddScene(0, "Thỏ thấy cà rốt trong vườn.");
        var carrot = _h.AddVocabulary(1, "cà rốt");
        _h.AddVocabulary(2, "vườn");
        _h.AddVocabularyAudio(carrot, "10/versions/50/v1/vocab-1.mp3");
        await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);
        await _sut.CollectAsync(ReadingHarness.Child, session.Id, 2);

        var list = await _sut.ListAsync(ReadingHarness.Child);

        Assert.Equal("10/versions/50/v1/vocab-1.mp3", list.Single(e => e.Term == "cà rốt").AudioUrl);
        Assert.Null(list.Single(e => e.Term == "vườn").AudioUrl);
    }

    [Fact]
    public async Task RemoveAsync_DeletesMyEntry_ButNotSomeoneElses()
    {
        var session = _h.AddSession();
        _h.AddVocabulary(1, "cà rốt");
        var mine = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);
        _h.Notebook.Add(new VocabularyNotebookEntry
            { Id = 50, ChildProfileId = ReadingHarness.OtherChild, StoryVocabularyId = 1, CollectedAt = DateTime.UtcNow });

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RemoveAsync(ReadingHarness.Child, 50));
        await _sut.RemoveAsync(ReadingHarness.Child, mine.EntryId);

        Assert.Equal(new[] { 50 }, _h.Notebook.Select(e => e.Id));
    }
}
