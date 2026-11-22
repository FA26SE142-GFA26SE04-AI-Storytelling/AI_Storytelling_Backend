using StoryPlatform.Application.Features.ContentCategories.DTOs;

namespace StoryPlatform.Application.Features.ContentCategories.Interfaces;

public interface IContentCategoryService
{
    Task<ContentCategoryDto> CreateAsync(
        int currentUserId, CreateContentCategoryRequestDto request, CancellationToken cancellationToken = default);

    Task<ContentCategoryDto> UpdateAsync(
        int contentCategoryId, UpdateContentCategoryRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Xoá hẳn danh mục; danh mục còn được tham chiếu thì ném ConflictException.</summary>
    Task DeleteAsync(int contentCategoryId, CancellationToken cancellationToken = default);

    /// <summary>includeInactive = false thì danh mục đã tắt coi như không tồn tại (NotFound).</summary>
    Task<ContentCategoryDto> GetByIdAsync(
        int contentCategoryId, bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>includeInactive = false chỉ trả danh mục đang hoạt động.</summary>
    Task<List<ContentCategoryDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);
}
