using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

public class ReadingSessionArchiveHandler : IStoryArchiveHandler
{
    private readonly IReadingSessionInterruptService _interruptService;

    public ReadingSessionArchiveHandler(IReadingSessionInterruptService interruptService)
    {
        _interruptService = interruptService;
    }

    public async Task HandleAsync(
        StoryArchiveContext context, CancellationToken cancellationToken = default)
    {
        if (context.Reason == ArchivedReason.SafetyConcern)
        {
            await _interruptService.RequestStoryInterruptAsync(context.Story.Id, cancellationToken);
        }
    }
}
