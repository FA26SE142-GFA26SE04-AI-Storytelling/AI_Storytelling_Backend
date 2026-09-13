using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;

namespace StoryPlatform.Application.Features.ChildProfiles.ClassGroups.Interfaces;

public interface IClassGroupService
{
    Task<ClassGroupDto> CreateClassGroupAsync(
        int teacherUserId, CreateClassGroupRequestDto request,
        CancellationToken cancellationToken = default);

    Task AddChildToClassGroupAsync(
        int classGroupId, int childProfileId, int currentUserId,
        CancellationToken cancellationToken = default);

    Task<ClassGroupDto> GetClassGroupByIdAsync(
        int classGroupId, int currentUserId, CancellationToken cancellationToken = default);

    Task<List<ClassGroupDto>> ListMyClassGroupsAsync(
        int teacherUserId, CancellationToken cancellationToken = default);

    Task<ClassGroupDto> UpdateClassGroupAsync(
        int classGroupId, int currentUserId, UpdateClassGroupRequestDto request,
        CancellationToken cancellationToken = default);

    Task ArchiveClassGroupAsync(
        int classGroupId, int currentUserId, CancellationToken cancellationToken = default);

    Task RemoveChildFromClassGroupAsync(
        int classGroupId, int childProfileId, int currentUserId,
        CancellationToken cancellationToken = default);

    Task<List<ChildProfileDto>> ListClassGroupMembersAsync(
        int classGroupId, int currentUserId, CancellationToken cancellationToken = default);
}
