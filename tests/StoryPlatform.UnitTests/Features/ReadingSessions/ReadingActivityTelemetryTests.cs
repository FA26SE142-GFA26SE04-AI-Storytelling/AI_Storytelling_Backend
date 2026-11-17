using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ReadingActivityTelemetryTests
{
    private static TelemetryLog Asr(int id, string payload) => new()
    {
        Id = id, ReadingSessionId = 1, EventType = TelemetryEventType.AsrWordResult, EventPayload = payload,
        OccurredAt = new DateTime(2026, 11, 16, 8, 0, id, DateTimeKind.Utc)
    };

    [Fact]
    public void ReadAloudPractice_UsesInteractionRowsOnly_WithLatestAndBest()
    {
        TelemetryLog[] logs =
        [
            Asr(1, "{\"segmentId\":7,\"total\":10,\"good\":10,\"retry\":0,\"skipped\":0}"),
            Asr(2, "{\"interactionId\":70,\"total\":8,\"good\":6,\"retry\":2,\"skipped\":0}"),
            Asr(3, "{\"interactionId\":70,\"total\":8,\"good\":4,\"retry\":3,\"skipped\":1}"),
            Asr(4, "khong phai json")
        ];

        var practice = ReadingActivityTelemetry.ReadAloudPractice(logs)!;

        Assert.Equal((2, 8, 4), (practice.Attempts, practice.TotalWords, practice.LatestGoodWords));
        Assert.Equal(50m, practice.LatestGoodPercent);
        Assert.Equal(75m, practice.BestGoodPercent);
        // Dòng luyện đọc không lẫn vào đọc to theo đoạn.
        Assert.Equal(1, ReadingActivityTelemetry.ReadAloud(logs)!.Attempts);
    }

    [Fact]
    public void ReadAloudPractice_WithoutPracticeRows_IsNull() =>
        Assert.Null(ReadingActivityTelemetry.ReadAloudPractice(
            [Asr(1, "{\"segmentId\":7,\"total\":10,\"good\":10,\"retry\":0,\"skipped\":0}")]));
}
