using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;

namespace StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;

public interface ILearningProfileService
{
    Task<LearningProfileDto> SetLearningProfileAsync(
        int childProfileId, int currentUserId, SetLearningProfileRequestDto request,
        CancellationToken cancellationToken = default);

    Task<LearningProfileDto> GetLearningProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task DeleteLearningProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);
}
