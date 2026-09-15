using StoryPlatform.Application.Features.ContentCategories.DTOs;

namespace StoryPlatform.Application.Features.ContentCategories.Interfaces;

public interface IContentCategoryService
{
    Task<ContentCategoryDto> CreateAsync(
        int currentUserId, CreateContentCategoryRequestDto request, CancellationToken cancellationToken = default);

    Task<ContentCategoryDto> UpdateAsync(
        int contentCategoryId, UpdateContentCategoryRequestDto request, CancellationToken cancellationToken = default);

    Task DeleteAsync(int contentCategoryId, CancellationToken cancellationToken = default);
    Task<ContentCategoryDto> GetByIdAsync(int contentCategoryId, CancellationToken cancellationToken = default);
    Task<List<ContentCategoryDto>> ListAsync(CancellationToken cancellationToken = default);
}
