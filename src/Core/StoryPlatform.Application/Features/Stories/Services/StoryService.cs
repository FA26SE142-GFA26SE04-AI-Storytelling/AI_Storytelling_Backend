using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Stories.DTOs;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using DomainStory = StoryPlatform.Domain.Entities.Story;

namespace StoryPlatform.Application.Features.Stories.Services;

public class StoryService : IStoryService
{
    public const string OrderByViews = "views";
    public const string OrderByFavorites = "favorites";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IStoryCoverResolver? _covers;

    public StoryService(IUnitOfWork unitOfWork, IStoryCoverResolver? covers = null)
    {
        _unitOfWork = unitOfWork;
        _covers = covers;
    }

    /// <summary>Đổi tham chiếu ảnh bìa (scene:...) của các truyện thành link đã ký; không có bộ giải mã thì giữ nguyên.</summary>
    private async Task<Func<string?, string?>?> CoverResolverAsync(IEnumerable<DomainStory> stories, CancellationToken cancellationToken) =>
        _covers is null ? null : await _covers.ResolveAsync(stories.Select(story => story.CoverImageUrl), cancellationToken);

    public async Task<PagedResult<StoryDto>> GetStoriesAsync(StoryFilterRequestDto filter, CancellationToken cancellationToken = default)
    {
        // Xây dựng biểu thức điều kiện lọc động bằng Linq Expression
        Expression<Func<DomainStory, bool>> predicate = s =>
            s.Scope == StoryScope.Parent && (s.Source != StorySource.Manual || s.CreateOperationId == null || s.CreateOperationId == "") &&
            (!filter.IsPublished.HasValue || s.IsPublished == filter.IsPublished.Value) &&
            (!filter.AuthorUserId.HasValue || s.AuthorUserId == filter.AuthorUserId.Value) &&
            (string.IsNullOrEmpty(filter.Genre) || s.Genre == filter.Genre) &&
            (string.IsNullOrEmpty(filter.AgeBand) || s.AgeBand == filter.AgeBand) &&
            (string.IsNullOrEmpty(filter.SearchTerm) || s.Title != null && s.Title.Contains(filter.SearchTerm) || s.Description != null && s.Description.Contains(filter.SearchTerm));

        var storyRepo = _unitOfWork.Repository<DomainStory>();

        Func<IQueryable<DomainStory>, IOrderedQueryable<DomainStory>> orderBy = query =>
            filter.IsDescending ? query.OrderByDescending(s => s.CreatedAt) : query.OrderBy(s => s.CreatedAt);

        var (items, totalCount) = await storyRepo.GetPagedAsync(
            pageIndex: filter.PageIndex,
            pageSize: filter.PageSize,
            filter: predicate,
            orderBy: orderBy,
            includeProperties: "Author",
            cancellationToken: cancellationToken);

        var cover = await CoverResolverAsync(items, cancellationToken);
        var dtos = items.Select(story => MapToStoryDto(story, cover)).ToList();

        return new PagedResult<StoryDto>(dtos, totalCount, filter.PageIndex, filter.PageSize);
    }

    public async Task<PagedResult<CommunityStoryDto>> GetPublishedCreatorStoriesAsync(StoryFilterRequestDto filter, CancellationToken cancellationToken = default)
    {
        // Thư viện cộng đồng: chỉ truyện Creator đã được duyệt và công khai. Không bao giờ trả bản nháp hay truyện đã lưu trữ.
        Expression<Func<DomainStory, bool>> predicate = s =>
            !s.IsDeleted && s.Scope == StoryScope.Creator && s.IsPublished && s.Status == StoryStatus.Ready && s.ArchivedAt == null &&
            (!filter.AuthorUserId.HasValue || s.AuthorUserId == filter.AuthorUserId.Value) &&
            (string.IsNullOrEmpty(filter.Genre) || s.Genre == filter.Genre) &&
            (string.IsNullOrEmpty(filter.AgeBand) || s.AgeBand == filter.AgeBand) &&
            (string.IsNullOrEmpty(filter.SearchTerm) || (s.Title != null && s.Title.Contains(filter.SearchTerm)) || (s.Description != null && s.Description.Contains(filter.SearchTerm)));

        var (items, totalCount) = await _unitOfWork.Repository<DomainStory>().GetPagedAsync(
            pageIndex: filter.PageIndex,
            pageSize: filter.PageSize,
            filter: predicate,
            orderBy: CommunityOrder(filter),
            includeProperties: "Author",
            cancellationToken: cancellationToken);

        var storyIds = items.Select(story => story.Id).ToList();
        var views = (await _unitOfWork.Repository<ReadingSession>().FindAsync(
                session => storyIds.Contains(session.StoryId), cancellationToken: cancellationToken))
            .GroupBy(session => session.StoryId).ToDictionary(group => group.Key, group => group.Count());
        var favorites = (await _unitOfWork.Repository<ReadingProgress>().FindAsync(
                progress => progress.IsFavorited && storyIds.Contains(progress.StoryId), cancellationToken: cancellationToken))
            .GroupBy(progress => progress.StoryId).ToDictionary(group => group.Key, group => group.Count());

        var cover = await CoverResolverAsync(items, cancellationToken);
        var dtos = items.Select(story =>
        {
            var dto = Fill(new CommunityStoryDto(), story, cover);
            dto.ViewCount = views.GetValueOrDefault(story.Id);
            dto.FavoriteCount = favorites.GetValueOrDefault(story.Id);
            return dto;
        }).ToList();
        return new PagedResult<CommunityStoryDto>(dtos, totalCount, filter.PageIndex, filter.PageSize);
    }

    /// <summary>Truyện công khai của người gọi; bỏ qua tác giả trong query string.</summary>
    public Task<PagedResult<CommunityStoryDto>> GetMyPublishedCreatorStoriesAsync(
        int creatorUserId, StoryFilterRequestDto filter, CancellationToken cancellationToken = default)
    {
        filter.AuthorUserId = creatorUserId;
        return GetPublishedCreatorStoriesAsync(filter, cancellationToken);
    }

    private Func<IQueryable<DomainStory>, IOrderedQueryable<DomainStory>> CommunityOrder(StoryFilterRequestDto filter)
    {
        var sessions = _unitOfWork.Repository<ReadingSession>().Query();
        var favorites = _unitOfWork.Repository<ReadingProgress>().Query();
        var descending = filter.IsDescending;
        switch ((filter.OrderBy ?? string.Empty).Trim().ToLowerInvariant())
        {
            case OrderByViews:
                return query => ThenByCreatedAtAndId(descending
                    ? query.OrderByDescending(s => sessions.Count(session => session.StoryId == s.Id))
                    : query.OrderBy(s => sessions.Count(session => session.StoryId == s.Id)), descending);
            case OrderByFavorites:
                return query => ThenByCreatedAtAndId(descending
                    ? query.OrderByDescending(s => favorites.Count(progress => progress.IsFavorited && progress.StoryId == s.Id))
                    : query.OrderBy(s => favorites.Count(progress => progress.IsFavorited && progress.StoryId == s.Id)), descending);
            default:
                return query => descending ? query.OrderByDescending(s => s.CreatedAt) : query.OrderBy(s => s.CreatedAt);
        }
    }

    private static IOrderedQueryable<DomainStory> ThenByCreatedAtAndId(IOrderedQueryable<DomainStory> ordered, bool descending) =>
        descending
            ? ordered.ThenByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            : ordered.ThenBy(s => s.CreatedAt).ThenBy(s => s.Id);

    public async Task<StoryDto> GetStoryByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var storyRepo = _unitOfWork.Repository<DomainStory>();
        var story = await storyRepo.FirstOrDefaultAsync(
            s => s.Id == id && s.Scope == StoryScope.Parent && (s.Source != StorySource.Manual || s.CreateOperationId == null || s.CreateOperationId == ""),
            includeProperties: "Author", 
            cancellationToken: cancellationToken);

        if (story == null || story.Scope != StoryScope.Parent || StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story))
        {
            throw new NotFoundException("Câu chuyện", id);
        }

        return MapToStoryDto(story, await CoverResolverAsync([story], cancellationToken));
    }

    public async Task<StoryDto> CreateStoryAsync(int authorUserId, CreateStoryRequestDto request, CancellationToken cancellationToken = default)
    {
        var author = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(authorUserId, cancellationToken);
        if (author == null)
        {
            throw new NotFoundException("Tác giả", authorUserId);
        }

        var newStory = new DomainStory
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Content = request.Content,
            CoverImageUrl = request.CoverImageUrl,
            Genre = request.Genre,
            MoralLesson = request.MoralLesson,
            AgeBand = request.AgeBand,
            Language = request.Language,
            Source = request.IsAiGenerated ? StorySource.Ai : StorySource.Manual,
            Status = StoryStatus.Draft,
            IsPublished = false,
            AuthorUserId = authorUserId
        };

        var storyRepo = _unitOfWork.Repository<DomainStory>();
        await storyRepo.AddAsync(newStory, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        newStory.Author = author;
        return MapToStoryDto(newStory);
    }

    public async Task<StoryDto> UpdateStoryAsync(int id, int currentUserId, UpdateStoryRequestDto request, CancellationToken cancellationToken = default)
    {
        var storyRepo = _unitOfWork.Repository<DomainStory>();
        var story = await storyRepo.FirstOrDefaultAsync(
            s => s.Id == id && s.Scope == StoryScope.Parent && (s.Source != StorySource.Manual || s.CreateOperationId == null || s.CreateOperationId == ""),
            includeProperties: "Author",
            cancellationToken: cancellationToken);

        if (story == null || story.Scope != StoryScope.Parent || StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story))
        {
            throw new NotFoundException("Câu chuyện", id);
        }

        if (story.AuthorUserId != currentUserId)
        {
            throw new ForbiddenException("Bạn không có quyền chỉnh sửa câu chuyện của người khác.");
        }

        // Chỉ cho phép cập nhật metadata (Title/Description/CoverImageUrl) và metadata an toàn.
        // Content/MoralLesson là canonical của StoryVersion hiện tại - KHÔNG được sửa trực tiếp.
        // Supervisor phải dùng Review API (tạo version mới) hoặc ExistingStories API.
        if (request.Content != null && story.Source == StorySource.Manual)
        {
            throw new BadRequestException(
                "Không thể cập nhật trực tiếp content của Story nhập tay. Hãy dùng API /existing/content để tạo version mới.");
        }
        if (request.MoralLesson != null && story.Source == StorySource.Manual)
        {
            throw new BadRequestException(
                "Không thể cập nhật trực tiếp MoralLesson của Story nhập tay. Hãy dùng API /existing/content.");
        }

        if (request.Title != null) story.Title = request.Title.Trim();
        if (request.Description != null) story.Description = request.Description.Trim();
        if (request.CoverImageUrl != null) story.CoverImageUrl = request.CoverImageUrl;
        if (request.Genre != null) story.Genre = request.Genre;
        if (request.Content != null && story.Source == StorySource.Ai) story.Content = request.Content;
        if (request.MoralLesson != null && story.Source == StorySource.Ai) story.MoralLesson = request.MoralLesson;
        if (request.AgeBand != null) story.AgeBand = request.AgeBand;
        if (request.Language != null) story.Language = request.Language;

        storyRepo.Update(story);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToStoryDto(story);
    }

    public async Task<bool> DeleteStoryAsync(int id, int currentUserId, CancellationToken cancellationToken = default)
    {
        var storyRepo = _unitOfWork.Repository<DomainStory>();
        var story = await storyRepo.GetByIdAsync(id, cancellationToken);
        if (story == null || story.Scope != StoryScope.Parent || StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story))
        {
            throw new NotFoundException("Câu chuyện", id);
        }

        if (story.AuthorUserId != currentUserId)
        {
            throw new ForbiddenException("Bạn không có quyền xóa câu chuyện này.");
        }

        // Soft delete
        story.IsDeleted = true;
        storyRepo.Update(story);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> PublishStoryAsync(int id, int currentUserId, CancellationToken cancellationToken = default)
    {
        var storyRepo = _unitOfWork.Repository<DomainStory>();
        var story = await storyRepo.GetByIdAsync(id, cancellationToken);
        if (story == null || story.Scope != StoryScope.Parent || StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story))
        {
            throw new NotFoundException("Câu chuyện", id);
        }

        if (story.AuthorUserId != currentUserId)
        {
            throw new ForbiddenException("Bạn không có quyền phát hành câu chuyện này.");
        }

        // Đóng lỗ hổng bypass Approval/Media:
        // Không một story nào (kể cả AI hay Manual) được publish nếu chưa qua Approval (Phase 4) và hoàn tất Media (Phase 5).
        // Story chỉ có thể publish khi đã đạt trạng thái Ready.
        if (story.Status != StoryStatus.Ready)
        {
            throw new ConflictException(
                $"CANNOT_BYPASS_APPROVAL_MEDIA: Không thể phát hành câu chuyện khi chưa hoàn tất phê duyệt và tạo Media (trạng thái hiện tại: {story.Status}).");
        }

        story.IsPublished = true;
        storyRepo.Update(story);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static StoryDto MapToStoryDto(DomainStory story, Func<string?, string?>? cover = null) =>
        Fill(new StoryDto(), story, cover);

    private static T Fill<T>(T dto, DomainStory story, Func<string?, string?>? cover) where T : StoryDto
    {
        dto.Id = story.Id;
        dto.Title = story.Title ?? string.Empty;
        dto.Description = story.Description;
        dto.Content = story.Content;
        dto.CoverImageUrl = cover is null ? story.CoverImageUrl : cover(story.CoverImageUrl);
        dto.Genre = story.Genre;
        dto.MoralLesson = story.MoralLesson;
        dto.AgeBand = story.AgeBand;
        dto.Language = story.Language;
        dto.Source = story.Source.ToString();
        dto.OutputMode = StoryPlatform.Application.Common.StoryOutputModeContract.Format(story.OutputMode);
        dto.Status = story.Status.ToString();
        dto.IsPublished = story.IsPublished;
        dto.AuthorUserId = story.AuthorUserId;
        dto.AuthorName = story.Author?.FullName ?? story.Author?.Username;
        dto.CreatedAt = story.CreatedAt;
        dto.UpdatedAt = story.UpdatedAt;
        return dto;
    }
}
