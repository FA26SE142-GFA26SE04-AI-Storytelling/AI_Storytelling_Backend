namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

public interface IChildRoadmapService
{
    Task<AddRoadmapStoryResultDto> AddStoryAsync(
        int actorUserId, AddRoadmapStoryRequestDto request, CancellationToken cancellationToken = default);

    Task<WishRoadmapResultDto> AddForWishAsync(
        int actorUserId, int childProfileId, int storyId, CancellationToken cancellationToken = default);

    Task<ChildRoadmapDto> RemoveAsync(
        int actorUserId, int childProfileId, int itemId, CancellationToken cancellationToken = default);

    Task<ChildRoadmapDto> ReorderAsync(
        int actorUserId, int childProfileId, ReorderRoadmapRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ChildRoadmapDto> ClearAsync(
        int actorUserId, int childProfileId, CancellationToken cancellationToken = default);

    Task<ChildRoadmapDto> GetAsync(
        int actorUserId, int childProfileId, CancellationToken cancellationToken = default);
}
