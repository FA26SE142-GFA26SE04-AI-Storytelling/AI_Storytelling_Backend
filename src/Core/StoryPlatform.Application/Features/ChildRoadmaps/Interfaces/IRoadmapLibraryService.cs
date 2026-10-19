using StoryPlatform.Application.Common.Models;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

public interface IRoadmapLibraryService
{
    Task<PagedResult<RoadmapLibraryItemDto>> ListAsync(
        int actorUserId, RoadmapLibraryQueryDto query, CancellationToken cancellationToken = default);

    Task<RoadmapLibraryDetailDto> GetAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default);

    Task<RoadmapCopyPlanDto> PreviewAsync(
        int actorUserId, int roadmapId, RoadmapCopyRequestDto request,
        CancellationToken cancellationToken = default);

    Task<RoadmapCopyPlanDto> CopyAsync(
        int actorUserId, int roadmapId, RoadmapCopyRequestDto request,
        CancellationToken cancellationToken = default);
}
