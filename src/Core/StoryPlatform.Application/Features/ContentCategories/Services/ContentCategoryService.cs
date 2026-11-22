using StoryPlatform.Application.Abstractions.Caching;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentCategories.DTOs;
using StoryPlatform.Application.Features.ContentCategories.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentCategories.Services;

public class ContentCategoryService : IContentCategoryService
{
    public const string ListCacheKey = "content-categories:list";
    public static string ItemCacheKey(int contentCategoryId) => $"content-categories:{contentCategoryId}";

    // Chỉ Admin sửa và mọi thay đổi đều xoá cache ngay; TTL chỉ là giới hạn trên nếu lệnh xoá cache bị lỗi.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppCache? _cache;

    public ContentCategoryService(IUnitOfWork unitOfWork, IAppCache? cache = null)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    private async Task InvalidateAsync(int? contentCategoryId, CancellationToken cancellationToken)
    {
        await _cache.RemoveIfConfiguredAsync(ListCacheKey, cancellationToken);
        if (contentCategoryId.HasValue)
        {
            await _cache.RemoveIfConfiguredAsync(ItemCacheKey(contentCategoryId.Value), cancellationToken);
        }
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
            Group = ParseGroup(request.Group),
            IsActive = request.IsActive,
            CreatedByAdminId = currentUserId,
            DefaultBlockedAgeBands = NormalizeAgeBands(request.DefaultBlockedAgeBands)
        };

        await repository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(null, cancellationToken);
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

        var group = request.Group == null ? category.Group : ParseGroup(request.Group);
        category.DisplayName = request.DisplayName.Trim();
        category.Group = group;
        category.IsActive = request.IsActive;
        if (request.DefaultBlockedAgeBands != null)
        {
            category.DefaultBlockedAgeBands = NormalizeAgeBands(request.DefaultBlockedAgeBands);
        }

        category.UpdatedAt = DateTime.UtcNow;
        repository.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(contentCategoryId, cancellationToken);
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
        await InvalidateAsync(contentCategoryId, cancellationToken);
    }

    public Task<ContentCategoryDto> GetByIdAsync(
        int contentCategoryId, CancellationToken cancellationToken = default) =>
        _cache.GetOrSetOrLoadAsync(ItemCacheKey(contentCategoryId), async ct =>
        {
            var category = await _unitOfWork.Repository<ContentCategory>().GetByIdAsync(contentCategoryId, ct);
            if (category == null)
            {
                throw new NotFoundException("Content Category", contentCategoryId);
            }

            return MapCategory(category);
        }, CacheTtl, cancellationToken);

    public Task<List<ContentCategoryDto>> ListAsync(CancellationToken cancellationToken = default) =>
        _cache.GetOrSetOrLoadAsync(ListCacheKey, async ct =>
        {
            var categories = await _unitOfWork.Repository<ContentCategory>().GetAllAsync(ct);
            return categories.Select(MapCategory).ToList();
        }, CacheTtl, cancellationToken);

    private static ContentCategoryDto MapCategory(ContentCategory category) => new()
    {
        Id = category.Id,
        Code = category.Code,
        DisplayName = category.DisplayName,
        Group = category.Group.ToString(),
        IsActive = category.IsActive,
        DefaultBlockedAgeBands = category.DefaultBlockedAgeBands.ToList()
    };

    private static ContentCategoryGroup ParseGroup(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || char.IsDigit(trimmed[0])
            || !Enum.TryParse<ContentCategoryGroup>(trimmed, ignoreCase: true, out var group)
            || !Enum.IsDefined(group))
        {
            throw new BadRequestException("Nhóm nhãn không hợp lệ.");
        }

        return group;
    }

    private static List<string> NormalizeAgeBands(IEnumerable<string>? values)
    {
        var result = new List<string>();
        foreach (var raw in values ?? Array.Empty<string>())
        {
            if (!Enum.TryParse<AgeBand>(raw?.Trim(), ignoreCase: false, out var band)
                || !Enum.IsDefined(band))
            {
                throw new BadRequestException($"Nhóm tuổi '{raw}' không hợp lệ.");
            }

            var name = band.ToString();
            if (!result.Contains(name))
            {
                result.Add(name);
            }
        }

        return result;
    }
}
