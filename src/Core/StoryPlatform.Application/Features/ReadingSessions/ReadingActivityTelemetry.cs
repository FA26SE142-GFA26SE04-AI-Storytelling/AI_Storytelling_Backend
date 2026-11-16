using System.Text.Json;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>
/// Đọc số liệu từ TelemetryLog của một phiên cho phía phụ huynh: đọc to (AsrWordResult), EXP (RewardGranted),
/// từ đã lưu (VocabularyCollected). Payload hỏng hoặc thiếu trường thì bỏ qua dòng đó.
/// </summary>
public static class ReadingActivityTelemetry
{
    /// <summary>Mỗi đoạn lấy lần đọc cuối; null nếu không có lần đọc to hợp lệ nào.</summary>
    public static ParentReadAloudSummaryDto? ReadAloud(IEnumerable<TelemetryLog> logs)
    {
        var results = new List<(int SegmentId, int Total, int Good, int Retry)>();
        foreach (var log in Ordered(logs, TelemetryEventType.AsrWordResult))
        {
            if (TryRead(log, out var root)
                && TryInt(root, "segmentId", out var segmentId)
                && TryInt(root, "total", out var total)
                && TryInt(root, "good", out var good)
                && TryInt(root, "retry", out var retry))
            {
                results.Add((segmentId, total, good, retry));
            }
        }

        if (results.Count == 0)
        {
            return null;
        }

        var latest = results.GroupBy(result => result.SegmentId).Select(group => group.Last()).ToList();
        var goodWords = latest.Sum(result => result.Good);
        var totalWords = latest.Sum(result => result.Total);
        return new ParentReadAloudSummaryDto
        {
            Attempts = results.Count,
            SegmentsPracticed = latest.Count,
            GoodWords = goodWords,
            RetryWords = latest.Sum(result => result.Retry),
            TotalWords = totalWords,
            GoodPercent = QuizRunCalculator.Percent(goodWords, totalWords)
        };
    }

    /// <summary>Luyện đọc to sau truyện (payload có interactionId); null nếu bé chưa luyện.</summary>
    public static ParentReadAloudPracticeDto? ReadAloudPractice(IEnumerable<TelemetryLog> logs)
    {
        var results = new List<(int Total, int Good)>();
        foreach (var log in Ordered(logs, TelemetryEventType.AsrWordResult))
        {
            if (TryRead(log, out var root)
                && TryInt(root, "interactionId", out _)
                && TryInt(root, "total", out var total)
                && TryInt(root, "good", out var good))
            {
                results.Add((total, good));
            }
        }

        if (results.Count == 0)
        {
            return null;
        }

        var latest = results[^1];
        return new ParentReadAloudPracticeDto
        {
            Attempts = results.Count,
            TotalWords = latest.Total,
            LatestGoodWords = latest.Good,
            LatestGoodPercent = QuizRunCalculator.Percent(latest.Good, latest.Total),
            BestGoodPercent = results.Max(result => QuizRunCalculator.Percent(result.Good, result.Total))
        };
    }

    /// <summary>Tổng EXP đã ghi nhận cho phiên (chỉ các dòng exp &gt; 0).</summary>
    public static int Exp(IEnumerable<TelemetryLog> logs) =>
        Ordered(logs, TelemetryEventType.RewardGranted)
            .Sum(log => TryRead(log, out var root) && TryInt(root, "exp", out var exp) && exp > 0 ? exp : 0);

    /// <summary>Id từ vựng bé lưu lần đầu trong phiên, theo thứ tự thời gian, không trùng.</summary>
    public static List<int> CollectedVocabularyIds(IEnumerable<TelemetryLog> logs) =>
        Ordered(logs, TelemetryEventType.VocabularyCollected)
            .Select(log => TryRead(log, out var root) && TryInt(root, "vocabularyId", out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    private static IEnumerable<TelemetryLog> Ordered(IEnumerable<TelemetryLog> logs, TelemetryEventType type) =>
        logs.Where(log => log.EventType == type && !log.IsDeleted)
            .OrderBy(log => log.OccurredAt)
            .ThenBy(log => log.Id);

    private static bool TryRead(TelemetryLog log, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(log.EventPayload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(log.EventPayload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out value);
    }
}
