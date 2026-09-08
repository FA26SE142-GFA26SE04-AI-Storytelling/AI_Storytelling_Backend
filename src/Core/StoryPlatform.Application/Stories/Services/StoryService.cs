using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Stories.DTOs;
using StoryPlatform.Application.Stories.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Entities.Enums;
using DomainStory = StoryPlatform.Domain.Entities.Story;

namespace StoryPlatform.Application.Stories.Services;

public class StoryService : IStoryService
{
    private readonly IUnitOfWork _unitOfWork;

    public StoryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<StoryDto>> GetStoriesAsync(StoryFilterRequestDto filter, CancellationToken cancellationToken = default)
    {
        // Xây dựng biểu thức điều kiện lọc động bằng Linq Expression
        Expression<Func<DomainStory, bool>> predicate = s =>
            (!filter.IsPublished.HasValue || s.IsPublished == filter.IsPublished.Value) &&
            (!filter.AuthorUserId.HasValue || s.AuthorUserId == filter.AuthorUserId.Value) &&
            (string.IsNullOrEmpty(filter.Genre) || s.Genre == filter.Genre) &&
            (string.IsNullOrEmpty(filter.AgeBand) || s.AgeBand == filter.AgeBand) &&
            (string.IsNullOrEmpty(filter.SearchTerm) || s.Title.Contains(filter.SearchTerm) || (s.Description != null && s.Description.Contains(filter.SearchTerm)));

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

        var dtos = items.Select(MapToStoryDto).ToList();

        return new PagedResult<StoryDto>(dtos, totalCount, filter.PageIndex, filter.PageSize);
    }

    public async Task<StoryDto> GetStoryByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var storyRepo = _unitOfWork.Repository<DomainStory>();
        var story = await storyRepo.FirstOrDefaultAsync(
            s => s.Id == id, 
            includeProperties: "Author", 
            cancellationToken: cancellationToken);

        if (story == null)
        {
            throw new NotFoundException("Câu chuyện", id);
        }

        return MapToStoryDto(story);
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
            s => s.Id == id, 
            includeProperties: "Author", 
            cancellationToken: cancellationToken);

        if (story == null)
        {
            throw new NotFoundException("Câu chuyện", id);
        }

        if (story.AuthorUserId != currentUserId)
        {
            throw new ForbiddenException("Bạn không có quyền chỉnh sửa câu chuyện của người khác.");
        }

        story.Title = request.Title.Trim();
        story.Description = request.Description?.Trim();
        story.Content = request.Content;
        story.CoverImageUrl = request.CoverImageUrl;
        story.Genre = request.Genre;
        story.MoralLesson = request.MoralLesson;
        story.AgeBand = request.AgeBand;
        story.Language = request.Language;

        storyRepo.Update(story);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToStoryDto(story);
    }

    public async Task<bool> DeleteStoryAsync(int id, int currentUserId, CancellationToken cancellationToken = default)
    {
        var storyRepo = _unitOfWork.Repository<DomainStory>();
        var story = await storyRepo.GetByIdAsync(id, cancellationToken);
        if (story == null)
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
        if (story == null)
        {
            throw new NotFoundException("Câu chuyện", id);
        }

        if (story.AuthorUserId != currentUserId)
        {
            throw new ForbiddenException("Bạn không có quyền phát hành câu chuyện này.");
        }

        story.IsPublished = true;
        story.Status = StoryStatus.Ready;
        storyRepo.Update(story);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static StoryDto MapToStoryDto(DomainStory story)
    {
        return new StoryDto
        {
            Id = story.Id,
            Title = story.Title,
            Description = story.Description,
            Content = story.Content,
            CoverImageUrl = story.CoverImageUrl,
            Genre = story.Genre,
            MoralLesson = story.MoralLesson,
            AgeBand = story.AgeBand,
            Language = story.Language,
            Source = story.Source.ToString(),
            Status = story.Status.ToString(),
            IsPublished = story.IsPublished,
            AuthorUserId = story.AuthorUserId,
            AuthorName = story.Author?.FullName ?? story.Author?.Username,
            CreatedAt = story.CreatedAt,
            UpdatedAt = story.UpdatedAt
        };
    }
}
