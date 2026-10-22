namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildStoryBrowseService
{
    Task<ChildStoryPageDto> SearchAsync(
        int childProfileId, ChildStoryQueryDto query, CancellationToken cancellationToken = default);
}
