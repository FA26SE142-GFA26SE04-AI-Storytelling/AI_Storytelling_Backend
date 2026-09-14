using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentCategories.DTOs;
using StoryPlatform.Application.Features.ContentCategories.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ContentCategories.Services;

public class ContentCategoryService : IContentCategoryService
{
    private readonly IUnitOfWork _unitOfWork;

    public ContentCategoryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ContentCategoryDto> CreateAsync(
        int currentUserId, CreateContentCategoryRequestDto request, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var repository = _unitOfWork.Repository<ContentCategory>();
        if (await repository.ExistsAsync(category => category.Code == code, cancellationToken))
        {
            throw new ConflictException($"Mã danh mục '{code}' đã tồn tại.");
        }

        var category = new ContentCategory
        {
            Code = code,
            DisplayName = request.DisplayName.Trim(),
            IsActive = request.IsActive,
            CreatedByAdminId = currentUserId
        };

        await repository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapCategory(category);
    }

    public async Task<ContentCategoryDto> UpdateAsync(
        int contentCategoryId, UpdateContentCategoryRequestDto request, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<ContentCategory>();
        var category = await repository.GetByIdAsync(contentCategoryId, cancellationToken);
        if (category == null)
        {
            throw new NotFoundException("Content Category", contentCategoryId);
        }

        category.DisplayName = request.DisplayName.Trim();
        category.IsActive = request.IsActive;
        category.UpdatedAt = DateTime.UtcNow;
        repository.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapCategory(category);
    }

    public async Task DeleteAsync(int contentCategoryId, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<ContentCategory>();
        var category = await repository.GetByIdAsync(contentCategoryId, cancellationToken);
        if (category == null)
        {
            throw new NotFoundException("Content Category", contentCategoryId);
        }

        repository.Delete(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ContentCategoryDto> GetByIdAsync(
        int contentCategoryId, CancellationToken cancellationToken = default)
    {
        var category = await _unitOfWork.Repository<ContentCategory>()
            .GetByIdAsync(contentCategoryId, cancellationToken);
        if (category == null)
        {
            throw new NotFoundException("Content Category", contentCategoryId);
        }

        return MapCategory(category);
    }

    public async Task<List<ContentCategoryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _unitOfWork.Repository<ContentCategory>().GetAllAsync(cancellationToken);
        return categories.Select(MapCategory).ToList();
    }

    private static ContentCategoryDto MapCategory(ContentCategory category) => new()
    {
        Id = category.Id,
        Code = category.Code,
        DisplayName = category.DisplayName,
        IsActive = category.IsActive
    };
}
