using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public class ReadingSessionInterruptService : IReadingSessionInterruptService
{
    private readonly IUnitOfWork _unitOfWork;

    public ReadingSessionInterruptService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<int> RequestStoryInterruptAsync(
        int storyId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var postReadSince = now - ReadingSessionInterruptPolicy.PostReadWindow;
        var repository = _unitOfWork.Repository<ReadingSession>();
        var candidates = await repository.FindAsync(
            session => session.StoryId == storyId
                       && session.ForceExitRequestedAt == null
                       && (session.Status == SessionStatus.Started
                           || session.Status == SessionStatus.Reading
                           || session.Status == SessionStatus.Activity
                           || (session.Status == SessionStatus.Completed
                               && session.CompletedAt != null
                               && session.CompletedAt >= postReadSince)),
            cancellationToken: cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var candidate in candidates)
        {
            var session = await repository.GetByIdAsync(candidate.Id, cancellationToken);
            if (session == null || session.IsDeleted || session.ForceExitRequestedAt != null)
            {
                continue;
            }

            session.ForceExitRequestedAt = now;
            repository.Update(session);
            count += 1;
        }

        if (count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return count;
    }

    public async Task<ForceExitSignalDto> CheckAsync(
        int childProfileId, int sessionId, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<ReadingSession>();
        var session = await repository.GetByIdAsync(sessionId, cancellationToken);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
        {
            throw new NotFoundException("Phiên đọc", sessionId);
        }

        if (session.ForceExitRequestedAt == null)
        {
            return new ForceExitSignalDto
            {
                ShouldExit = false,
                SessionStatus = session.Status.ToString()
            };
        }

        if (session.Status is SessionStatus.Started or SessionStatus.Reading)
        {
            session.Status = SessionStatus.ForceExited;
            session.CompletedAt = DateTime.UtcNow;
            repository.Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ForceExitSignalDto
        {
            ShouldExit = true,
            ReturnTo = ReadingSessionInterruptPolicy.ReturnToJourneyMap,
            Message = ReadingSessionInterruptPolicy.ChildMessage,
            SessionStatus = session.Status.ToString()
        };
    }
}
