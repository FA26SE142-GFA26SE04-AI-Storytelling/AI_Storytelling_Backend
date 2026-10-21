using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;

public interface IRoadmapReviewService
{
    Task<RoadmapReviewPackageDto> GetPackageAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    Task<List<RoadmapChecklistEntryDto>> SaveChecklistAsync(
        int actorUserId, int itemId, SaveRoadmapChecklistRequestDto request,
        CancellationToken cancellationToken = default);

    Task<RoadmapReviewDecisionResultDto> ApproveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default);

    Task<RoadmapReviewDecisionResultDto> RequestChangesAsync(
        int actorUserId, int itemId, RequestRoadmapChangesDto request,
        CancellationToken cancellationToken = default);

    Task<RoadmapReviewDecisionResultDto> RejectAsync(
        int actorUserId, int itemId, RejectRoadmapRequestDto request,
        CancellationToken cancellationToken = default);
}
