using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Interfaces;

public sealed record StoryArchiveContext(
    Story Story, ArchivedReason Reason, int ActorUserId, DateTime Now, int? ReporterUserId = null);

public interface IStoryArchiveHandler
{
    Task HandleAsync(StoryArchiveContext context, CancellationToken cancellationToken = default);
}

public interface IStoryArchiveConsequences
{
    Task ApplyAsync(StoryArchiveContext context, CancellationToken cancellationToken = default);
}
