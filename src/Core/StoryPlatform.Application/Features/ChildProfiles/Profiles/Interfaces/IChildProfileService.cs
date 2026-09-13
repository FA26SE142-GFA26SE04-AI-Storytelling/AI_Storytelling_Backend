using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;

namespace StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;

public interface IChildProfileService
{
    Task<ChildProfileDto> CreateChildProfileAsync(
        int ownerUserId,
        CreateChildProfileRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ChildProfileDto> ActivateChildProfileAsync(
        int childProfileId, int currentUserId,
        CancellationToken cancellationToken = default);

    Task<List<ChildProfileDto>> ListMyChildProfilesAsync(
        int ownerUserId, CancellationToken cancellationToken = default);

    Task<ChildProfileDto> GetChildProfileByIdAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task<ChildProfileDto> UpdateChildProfileAsync(
        int childProfileId, int currentUserId, UpdateChildProfileRequestDto request,
        CancellationToken cancellationToken = default);

    Task ArchiveChildProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);
}
