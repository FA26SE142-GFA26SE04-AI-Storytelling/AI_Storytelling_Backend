using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentCategories.DTOs;
using StoryPlatform.Application.Features.ContentCategories.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

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
            Group = ParseGroup(request.Group),
            IsActive = request.IsActive,
            CreatedByModeratorId = currentUserId,
            DefaultBlockedAgeBands = NormalizeAgeBands(request.DefaultBlockedAgeBands)
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

        var usages = await FindUsagesAsync(contentCategoryId, cancellationToken);
        if (usages.Count > 0)
        {
            throw new ConflictException(
                $"Không thể xoá Content Category đang được dùng trong: {string.Join(", ", usages)}. "
                + "Hãy tắt danh mục (IsActive = false) thay vì xoá.");
        }

        repository.Delete(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ContentCategoryDto> GetByIdAsync(
        int contentCategoryId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var category = await _unitOfWork.Repository<ContentCategory>()
            .GetByIdAsync(contentCategoryId, cancellationToken);
        if (category == null || (!category.IsActive && !includeInactive))
        {
            throw new NotFoundException("Content Category", contentCategoryId);
        }

        return MapCategory(category);
    }

    public async Task<List<ContentCategoryDto>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<ContentCategory>();
        var categories = includeInactive
            ? await repository.GetAllAsync(cancellationToken)
            : await repository.FindAsync(category => category.IsActive, cancellationToken: cancellationToken);
        return categories.Select(MapCategory).ToList();
    }

    /// <summary>
    /// Các nơi còn tham chiếu danh mục: bốn bảng có khoá ngoại (Restrict) và nhãn đề xuất
    /// lưu dạng jsonb trong hồ sơ gửi duyệt (không có khoá ngoại nên phải tự kiểm).
    /// </summary>
    private async Task<List<string>> FindUsagesAsync(int contentCategoryId, CancellationToken cancellationToken)
    {
        var usages = new List<string>();
        if (await _unitOfWork.Repository<StoryCategory>().ExistsAsync(
                link => link.ContentCategoryId == contentCategoryId, cancellationToken))
            usages.Add("truyện");
        if (await _unitOfWork.Repository<SafetyPolicyCategory>().ExistsAsync(
                link => link.ContentCategoryId == contentCategoryId, cancellationToken))
            usages.Add("chính sách an toàn của trẻ");
        if (await _unitOfWork.Repository<WishCard>().ExistsAsync(
                card => card.ContentCategoryId == contentCategoryId, cancellationToken))
            usages.Add("thẻ điều ước");
        if (await _unitOfWork.Repository<ReadingRoadmap>().ExistsAsync(
                roadmap => roadmap.MainCategoryId == contentCategoryId, cancellationToken))
            usages.Add("lộ trình đọc");

        var proposals = await _unitOfWork.Repository<ModerationItem>().FindAsync(
            item => item.ProposedContentCategoryIds != null, cancellationToken: cancellationToken);
        if (proposals.Any(item => PublicStoryLabels.Parse(item.ProposedContentCategoryIds).Contains(contentCategoryId)))
            usages.Add("hồ sơ gửi duyệt truyện");

        return usages;
    }

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
