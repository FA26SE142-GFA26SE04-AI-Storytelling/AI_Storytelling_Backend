using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.StoryWishes;

namespace StoryPlatform.Application.Features.StoryWishes.Interfaces;

public interface IParentStoryWishService
{
    Task<IReadOnlyList<ParentWishDto>> ListAsync(int parentUserId, int childProfileId, CancellationToken ct = default);
    Task<ParentWishDetailDto> GetAsync(int parentUserId, int wishId, CancellationToken ct = default);
    Task<ParentWishGenerationFormDto> GetGenerationFormAsync(int parentUserId, int wishId, CancellationToken ct = default);
    Task<ParentWishCreateStoryResultDto> CreateStoryAsync(int parentUserId, int wishId, SubmitAIStoryInputRequestDto request, CancellationToken ct = default);
    Task<AIStoryInputProgressDto> GetGenerationProgressAsync(int parentUserId, int wishId, CancellationToken ct = default);
    Task<ParentWishDto> UseExistingAsync(int parentUserId, int wishId, UseExistingStoryRequestDto request, CancellationToken ct = default);
    Task<ParentWishDto> DeclineAsync(int parentUserId, int wishId, DeclineWishRequestDto request, CancellationToken ct = default);
}
