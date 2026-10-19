using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;

public interface IRoadmapTemplateService
{
    Task<RoadmapDetailDto> CreateAsync(
        int actorUserId, SaveRoadmapRequestDto request, CancellationToken cancellationToken = default);

    Task<RoadmapDetailDto> UpdateAsync(
        int actorUserId, int roadmapId, SaveRoadmapRequestDto request,
        CancellationToken cancellationToken = default);

    Task<RoadmapDetailDto> GetAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default);

    Task<PagedResult<CandidateStoryDto>> ListCandidatesAsync(
        int actorUserId, CandidateQueryDto query, CancellationToken cancellationToken = default);

    Task<RoadmapDetailDto> SetItemsAsync(
        int actorUserId, int roadmapId, SetRoadmapItemsRequestDto request,
        CancellationToken cancellationToken = default);

    Task<RoadmapSubmitResultDto> SubmitAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default);

    Task<RoadmapSubmitResultDto> WithdrawAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default);

    Task<PagedResult<RoadmapListItemDto>> ListAsync(
        int actorUserId, RoadmapListQueryDto query, CancellationToken cancellationToken = default);
}
