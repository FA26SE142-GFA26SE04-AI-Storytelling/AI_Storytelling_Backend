using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class MediaGenerationServiceTests
{
    // Mỗi test dùng Id audio riêng: thời gian chờ thử lại lưu theo Id nên dùng chung sẽ lẫn giữa các test.
    private sealed class FakeWordAligner(params string[] words) : IAudioWordAligner
    {
        public int Calls;
        public Task<IReadOnlyList<TimedWord>> RecognizeWordsAsync(byte[] audio, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<TimedWord>>(words.Select((w, i) => new TimedWord(w, i * 0.5, i * 0.5 + 0.4)).ToList());
        }
    }

    private static MediaAsset SeedNarration(StoryReviewServiceTests.FakeUnitOfWork uow, int id, string text = "Thỏ con đi lạc",
        string provider = "GeminiTTS", string? timings = null, int? packageId = null)
    {
        uow.Seed(new StorySegment { Id = id, StorySceneId = 100, SegmentOrder = 1, TextContent = text });
        var asset = new MediaAsset
        {
            Id = id, StoryVersionId = 10, StorySegmentId = id, Type = MediaType.TtsAudio, Status = MediaStatus.Ready,
            ValidationStatus = ValidationStatus.Passed, IsCurrent = true, Url = $"audio-{id}.wav", Provider = provider,
            WordTimings = timings, ParentStoryPackageId = packageId
        };
        uow.Seed(asset);
        return asset;
    }

    [Fact]
    public async Task WordTimingBackfill_AddsTimingsToGeminiNarration()
    {
        var uow = Seed();
        var asset = SeedNarration(uow, 7001);
        var aligner = new FakeWordAligner("thỏ", "con", "đi", "lạc");
        var service = Create(uow, storage: new ReadableStorage(), aligner: aligner);

        Assert.True(await service.BackfillWordTimingsAsync());

        Assert.NotNull(asset.WordTimings);
        Assert.Equal("Thỏ", System.Text.Json.JsonDocument.Parse(asset.WordTimings!).RootElement[0].GetProperty("word").GetString());
        Assert.Equal(1, aligner.Calls);
        Assert.False(await service.BackfillWordTimingsAsync());   // đã có mốc: hết việc
    }

    [Fact]
    public async Task WordTimingBackfill_WithoutAligner_DoesNothing()
    {
        var uow = Seed();
        var asset = SeedNarration(uow, 7002);
        var service = Create(uow, storage: new ReadableStorage());

        Assert.False(await service.BackfillWordTimingsAsync());
        Assert.Null(asset.WordTimings);
    }

    [Fact]
    public async Task WordTimingBackfill_SkipsNonGeminiAlreadyTimedAndPackageAudio()
    {
        var uow = Seed();
        SeedNarration(uow, 7003, provider: "GoogleCloud");
        SeedNarration(uow, 7004, timings: "[{\"word\":\"x\",\"start\":0,\"end\":1}]");
        SeedNarration(uow, 7005, packageId: 3);
        var aligner = new FakeWordAligner("thỏ");
        var service = Create(uow, storage: new ReadableStorage(), aligner: aligner);

        Assert.False(await service.BackfillWordTimingsAsync());
        Assert.Equal(0, aligner.Calls);
    }

    [Fact]
    public async Task WordTimingBackfill_PoorAlignment_LeavesAudioUntouchedAndRetriesOnlyAfterTheCooldown()
    {
        var uow = Seed();
        var asset = SeedNarration(uow, 7006, "Thỏ con đi lạc vào rừng");
        var aligner = new FakeWordAligner("xe", "buýt");
        var service = Create(uow, storage: new ReadableStorage(), aligner: aligner);

        Assert.True(await service.BackfillWordTimingsAsync());
        Assert.Null(asset.WordTimings);
        Assert.False(await service.BackfillWordTimingsAsync());   // chưa hết thời gian chờ: không gọi dồn STT
        Assert.Equal(1, aligner.Calls);
    }

    [Fact]
    public async Task WordTimingBackfill_ListeningHighlightPart_UsesThePartText_ButOtherPartsAreSkipped()
    {
        var uow = Seed();
        uow.Seed(new ListeningInteraction
        {
            Id = 7007, StoryVersionId = 10, TargetAge = 6, Type = ListeningInteractionType.Checkpoint, Kind = CheckpointKind.Literal,
            AnchorOffset = FirstBlockEnd, Prompt = "Ai đi lạc vào rừng", StorySceneId = 600
        });
        var prompt = new MediaAsset
        {
            Id = 7008, StoryVersionId = 10, ListeningInteractionId = 7007, AudioPartKey = "prompt", Type = MediaType.ListeningAudio,
            Status = MediaStatus.Ready, IsCurrent = true, Url = "prompt.wav", Provider = "GeminiTTS"
        };
        var feedback = new MediaAsset
        {
            Id = 7009, StoryVersionId = 10, ListeningInteractionId = 7007, AudioPartKey = "match", Type = MediaType.ListeningAudio,
            Status = MediaStatus.Ready, IsCurrent = true, Url = "match.wav", Provider = "GeminiTTS"
        };
        uow.Seed(prompt);
        uow.Seed(feedback);
        var aligner = new FakeWordAligner("ai", "đi", "lạc", "vào", "rừng");
        var service = Create(uow, storage: new ReadableStorage(), aligner: aligner);

        Assert.True(await service.BackfillWordTimingsAsync());
        Assert.NotNull(prompt.WordTimings);
        Assert.False(await service.BackfillWordTimingsAsync());
        Assert.Null(feedback.WordTimings);
        Assert.Equal(1, aligner.Calls);
    }
    [Fact]
    public async Task WordTimingBackfill_AlternateReadingLine_UsesTheLineTextOfThatOption()
    {
        var uow = Seed();
        uow.Seed(new ListeningInteraction { Id = 7011, StoryVersionId = 10, TargetAge = 8, Type = ListeningInteractionType.AlternateReading,
            AnchorOffset = FirstBlockEnd, StorySceneId = 600, MatchFeedback = "Con đọc hay quá!" });
        uow.Seed(new ListeningInteractionOption { Id = 7012, ListeningInteractionId = 7011, SortOrder = 1, Text = "Cậu đang làm gì đấy?", CorrectOrder = 1, Speaker = "Thỏ" });
        uow.Seed(new ListeningInteractionOption { Id = 7013, ListeningInteractionId = 7011, SortOrder = 2, Text = "Mình đang tìm hạt dẻ.", CorrectOrder = 2, Speaker = "Sóc" });
        MediaAsset Line(int id, int option, string part) => new()
        {
            Id = id, StoryVersionId = 10, ListeningInteractionId = 7011, ListeningInteractionOptionId = option, AudioPartKey = part,
            Type = MediaType.ListeningAudio, Status = MediaStatus.Ready, IsCurrent = true, Url = $"line-{id}.wav", Provider = "GeminiTTS"
        };
        var line = Line(7014, 7013, "text");
        var firstLine = Line(7015, 7012, "text");
        var firstLineTimed = firstLine;
        firstLineTimed.WordTimings = "[{\"word\":\"Cậu\",\"start\":0,\"end\":0.3}]";   // đã có mốc: bỏ qua
        var feedback = Line(7016, 7013, "feedback");                                      // không cần mốc: bỏ qua
        uow.Seed(line); uow.Seed(firstLineTimed); uow.Seed(feedback);
        var aligner = new FakeWordAligner("mình", "đang", "tìm", "hạt", "dẻ");
        var service = Create(uow, storage: new ReadableStorage(), aligner: aligner);

        Assert.True(await service.BackfillWordTimingsAsync());
        Assert.NotNull(line.WordTimings);
        Assert.Equal("Mình", System.Text.Json.JsonDocument.Parse(line.WordTimings!).RootElement[0].GetProperty("word").GetString());
        Assert.Null(feedback.WordTimings);
        Assert.False(await service.BackfillWordTimingsAsync());
        Assert.Equal(1, aligner.Calls);
    }
}
