using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public partial class ParentReadingFeedbackService
{
    /// <summary>Telemetry đọc to / EXP / lưu từ của các phiên, nhóm theo ReadingSessionId.</summary>
    private async Task<Dictionary<int, List<TelemetryLog>>> LoadActivityLogsAsync(
        List<int> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0)
        {
            return new Dictionary<int, List<TelemetryLog>>();
        }

        return (await _unitOfWork.Repository<TelemetryLog>().FindAsync(
                log => sessionIds.Contains(log.ReadingSessionId)
                       && !log.IsDeleted
                       && (log.EventType == TelemetryEventType.AsrWordResult
                           || log.EventType == TelemetryEventType.RewardGranted
                           || log.EventType == TelemetryEventType.VocabularyCollected),
                cancellationToken: ct))
            .GroupBy(log => log.ReadingSessionId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    /// <summary>Chữ của từ vựng theo Id (kể cả từ đã bị gỡ khỏi truyện, vì bé vẫn đã lưu).</summary>
    private async Task<Dictionary<int, string>> LoadTermsAsync(List<int> vocabularyIds, CancellationToken ct)
    {
        if (vocabularyIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        return (await _unitOfWork.Repository<StoryVocabulary>().FindAsync(
                word => vocabularyIds.Contains(word.Id), cancellationToken: ct))
            .ToDictionary(word => word.Id, word => word.Term);
    }

    /// <summary>Bản kể lại chưa xóa của các phiên (nếu một phiên có nhiều bản thì lấy bản mới nhất).</summary>
    private async Task<Dictionary<int, ParentRetellBriefDto>> LoadRetellsAsync(
        List<int> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0)
        {
            return new Dictionary<int, ParentRetellBriefDto>();
        }

        var summaries = await _unitOfWork.Repository<StorySummary>().FindAsync(
            summary => sessionIds.Contains(summary.ReadingSessionId) && !summary.IsDeleted, cancellationToken: ct);
        if (summaries.Count == 0)
        {
            return new Dictionary<int, ParentRetellBriefDto>();
        }

        var summaryIds = summaries.Select(summary => summary.Id).ToList();
        var flags = (await _unitOfWork.Repository<StorySummaryFlag>().FindAsync(
                flag => summaryIds.Contains(flag.StorySummaryId) && !flag.IsDeleted, cancellationToken: ct))
            .GroupBy(flag => flag.StorySummaryId)
            .ToDictionary(group => group.Key, group => (Total: group.Count(), Open: group.Count(flag => flag.AnsweredAt == null)));

        return summaries
            .GroupBy(summary => summary.ReadingSessionId)
            .ToDictionary(group => group.Key, group =>
            {
                var latest = group.OrderByDescending(summary => summary.CreatedAt)
                    .ThenByDescending(summary => summary.Id).First();
                return new ParentRetellBriefDto
                {
                    SummaryId = latest.Id,
                    CreatedAt = latest.CreatedAt,
                    HasRecording = latest.AudioStoragePath != null,
                    FlagCount = flags.GetValueOrDefault(latest.Id).Total,
                    UnansweredCount = flags.GetValueOrDefault(latest.Id).Open
                };
            });
    }
}
