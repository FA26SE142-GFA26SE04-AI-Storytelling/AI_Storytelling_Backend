using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions;
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
        _sut = new ChildNotebookService(_h.UnitOfWork.Object, new ChildRewardService(_h.UnitOfWork.Object, TimeProvider.System));
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
    public async Task RemoveAsync_HidesMyEntry_ButNotSomeoneElses()
    {
        var session = _h.AddSession();
        _h.AddVocabulary(1, "cà rốt");
        var mine = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);
        _h.Notebook.Add(new VocabularyNotebookEntry
            { Id = 50, ChildProfileId = ReadingHarness.OtherChild, StoryVocabularyId = 1, CollectedAt = DateTime.UtcNow });

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RemoveAsync(ReadingHarness.Child, 50));
        await _sut.RemoveAsync(ReadingHarness.Child, mine.EntryId);

        Assert.Equal(2, _h.Notebook.Count);
        Assert.NotNull(_h.Notebook.Single(e => e.Id == mine.EntryId).RemovedAt);
        Assert.Null(_h.Notebook.Single(e => e.Id == 50).RemovedAt);
        Assert.Empty(await _sut.ListAsync(ReadingHarness.Child));
    }

    private Achievement? Row() => _h.Achievements.SingleOrDefault(a => a.ChildProfileId == ReadingHarness.Child);

    [Fact]
    public async Task CollectAsync_NewWord_GivesOneExp()
    {
        var session = _h.AddSession();
        _h.AddVocabulary(1, "cà rốt");

        var result = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);

        Assert.Equal(RewardPolicy.WordCollectedExp, result.ExpGained);
        Assert.Empty(result.NewBadges);
        Assert.Equal(1, Row()!.ExpTotal);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.RewardGranted);
    }

    [Fact]
    public async Task CollectAsync_SameWordTwice_GivesExpOnlyOnce()
    {
        var session = _h.AddSession();
        _h.AddVocabulary(1, "cà rốt");

        await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);
        var second = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);

        Assert.Equal(0, second.ExpGained);
        Assert.Equal(1, Row()!.ExpTotal);
    }

    [Fact]
    public async Task CollectAsync_WordRemovedEarlier_ShowsItAgain_WithoutExp()
    {
        var session = _h.AddSession();
        _h.AddVocabulary(1, "cà rốt");
        var first = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);
        await _sut.RemoveAsync(ReadingHarness.Child, first.EntryId);

        var again = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);

        Assert.Equal(first.EntryId, again.EntryId);
        Assert.Equal(0, again.ExpGained);
        Assert.Single(_h.Notebook);
        Assert.Null(_h.Notebook.Single().RemovedAt);
        Assert.Equal(1, Row()!.ExpTotal);
        Assert.Single(await _sut.ListAsync(ReadingHarness.Child));
    }

    [Fact]
    public async Task RemoveAsync_KeepsExp_AndAlreadyRemovedIsNotFound()
    {
        var session = _h.AddSession();
        _h.AddVocabulary(1, "cà rốt");
        var entry = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);

        await _sut.RemoveAsync(ReadingHarness.Child, entry.EntryId);

        Assert.Equal(1, Row()!.ExpTotal);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RemoveAsync(ReadingHarness.Child, entry.EntryId));
    }

    [Fact]
    public async Task GetDefinitionAsync_RemovedWord_IsNotCollected()
    {
        var session = _h.AddSession();
        _h.AddVocabulary(1, "cà rốt");
        var entry = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);
        await _sut.RemoveAsync(ReadingHarness.Child, entry.EntryId);

        var result = await _sut.GetDefinitionAsync(ReadingHarness.Child, session.Id, 1);

        Assert.False(result.IsCollected);
    }

    [Fact]
    public async Task CollectAsync_FiftiethVisibleWord_AwardsWordCollector_RemovedWordsDoNotCount()
    {
        var session = _h.AddSession();
        for (var id = 1; id <= 49; id++)
        {
            _h.Notebook.Add(new VocabularyNotebookEntry
                { Id = id, ChildProfileId = ReadingHarness.Child, StoryVocabularyId = 1000 + id, CollectedAt = DateTime.UtcNow });
        }

        _h.Notebook.Add(new VocabularyNotebookEntry
        {
            Id = 60, ChildProfileId = ReadingHarness.Child, StoryVocabularyId = 2000, CollectedAt = DateTime.UtcNow,
            RemovedAt = DateTime.UtcNow
        });
        _h.AddVocabulary(1, "cà rốt");
        _h.AddVocabulary(2, "vườn");

        var fiftieth = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 1);
        var fiftyFirst = await _sut.CollectAsync(ReadingHarness.Child, session.Id, 2);

        Assert.Equal(new[] { BadgeCatalog.WordCollector50 }, fiftieth.NewBadges);
        Assert.Empty(fiftyFirst.NewBadges);
        Assert.Single(_h.Badges, b => b.BadgeCode == BadgeCatalog.WordCollector50);
    }
}
