using StoryPlatform.Application.Features.ContentReports.Interfaces;

namespace StoryPlatform.Application.Features.ContentReports.Services;

public class StoryArchiveConsequences : IStoryArchiveConsequences
{
    private readonly IReadOnlyList<IStoryArchiveHandler> _handlers;

    public StoryArchiveConsequences(IEnumerable<IStoryArchiveHandler> handlers)
    {
        _handlers = handlers.ToList();
    }

    public async Task ApplyAsync(
        StoryArchiveContext context, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
        {
            await handler.HandleAsync(context, cancellationToken);
        }
    }
}
