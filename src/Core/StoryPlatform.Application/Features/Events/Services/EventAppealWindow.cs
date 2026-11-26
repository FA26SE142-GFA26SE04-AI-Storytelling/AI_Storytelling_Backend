using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

internal static class EventAppealWindow
{
    public const int Hours = 48;

    public static DateTime? DeadlineFor(EventParticipation? participation) =>
        participation?.Status == EventParticipationStatus.VerificationFailed
            ? participation.VerifiedAt?.AddHours(Hours)
            : null;

    public static async Task<DateTime?> GetLatestOpenDeadlineAsync(IUnitOfWork uow, int eventId, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddHours(-Hours);
        var failed = await uow.Repository<EventParticipation>().FindAsync(
            p => p.EventId == eventId && p.Status == EventParticipationStatus.VerificationFailed &&
                 p.VerifiedAt.HasValue && p.VerifiedAt.Value > cutoff, cancellationToken: ct);
        if (failed == null || failed.Count == 0)
            return null;

        var ids = failed.Select(p => p.Id).ToList();
        var resolved = await uow.Repository<EventAppeal>().FindAsync(
            a => a.EventId == eventId && ids.Contains(a.ParticipationId) &&
                (a.Status == EventAppealStatus.Accepted || a.Status == EventAppealStatus.Rejected), cancellationToken: ct);
        var rejected = await uow.Repository<EventSubmission>().FindAsync(
            s => s.EventId == eventId && ids.Contains(s.ParticipationId) &&
                s.Status == EventSubmissionStatus.Rejected, cancellationToken: ct);
        var resolvedOrIneligible = (resolved ?? Array.Empty<EventAppeal>()).Select(a => a.ParticipationId)
            .Concat((rejected ?? Array.Empty<EventSubmission>()).Select(s => s.ParticipationId)).ToHashSet();
        return failed.Where(p => !resolvedOrIneligible.Contains(p.Id)).Select(DeadlineFor).Max();
    }

    public static async Task EnsureNoOpenWindowAsync(IUnitOfWork uow, int eventId, DateTime now, CancellationToken ct)
    {
        var latestDeadline = await GetLatestOpenDeadlineAsync(uow, eventId, now, ct);
        if (latestDeadline is { } deadline)
            throw new ConflictException($"Còn hồ sơ đang trong hạn khiếu nại đến {deadline + VietnamTime.Offset:HH:mm dd/MM/yyyy} (giờ Việt Nam); chưa thể chốt kết quả.");
    }
}
