using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

/// <summary>Truyện đang được dùng ở đâu (thẻ ước, điều ước đang chờ, lộ trình). Chỉ đọc, chỉ trả con số cho điều ước/lộ trình.</summary>
public class StoryUsageImpactReader : IStoryUsageImpactReader
{
    private readonly IUnitOfWork _unitOfWork;

    public StoryUsageImpactReader(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<StoryImpactDto?> ReadAsync(int storyId, bool checkRepublish, CancellationToken cancellationToken = default)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            return null;
        }

        var wishCards = await ReadWishCardsAsync(story, cancellationToken);
        return new StoryImpactDto
        {
            WishCards = wishCards,
            PendingWishCount = await CountPendingWishesAsync(storyId, cancellationToken),
            ActiveRoadmapItemCount = await _unitOfWork.Repository<ChildRoadmapItem>().CountAsync(
                item => item.StoryId == storyId && !item.IsDeleted
                        && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open),
                cancellationToken),
            // Creator chỉ tạo được bản sửa khi truyện đã gỡ, nên "từng công khai" + đang duyệt = xin công khai lại.
            WishCardsNeedReview = checkRepublish && story.PublishedAt != null && wishCards.Count > 0
        };
    }

    private async Task<List<StoryImpactWishCardDto>> ReadWishCardsAsync(Story story, CancellationToken cancellationToken)
    {
        var storyId = story.Id;
        var matches = await _unitOfWork.Repository<StoryWishCardMatch>().FindAsync(
            match => match.StoryId == storyId && !match.IsDeleted, cancellationToken: cancellationToken);
        if (matches.Count == 0)
        {
            return new List<StoryImpactWishCardDto>();
        }

        var cardIds = matches.Select(match => match.WishCardId).Distinct().ToList();
        var cards = (await _unitOfWork.Repository<WishCard>().FindAsync(
            card => cardIds.Contains(card.Id), cancellationToken: cancellationToken)).ToDictionary(card => card.Id);

        var otherMatches = await _unitOfWork.Repository<StoryWishCardMatch>().FindAsync(
            match => cardIds.Contains(match.WishCardId) && match.StoryId != storyId && !match.IsDeleted,
            cancellationToken: cancellationToken);
        var otherStoryIds = otherMatches.Select(match => match.StoryId).Distinct().ToList();
        var ageBand = story.AgeBand;
        var published = (await _unitOfWork.Repository<Story>().FindAsync(
                other => otherStoryIds.Contains(other.Id)
                         && !other.IsDeleted
                         && other.Scope == StoryScope.Creator
                         && other.IsPublished
                         && other.Status == StoryStatus.Ready
                         && other.ArchivedAt == null
                         && other.AgeBand == ageBand,
                cancellationToken: cancellationToken))
            .Select(other => other.Id).ToHashSet();

        return matches
            .Where(match => cards.ContainsKey(match.WishCardId))
            .GroupBy(match => match.WishCardId)
            .Select(group =>
            {
                var card = cards[group.Key];
                return new StoryImpactWishCardDto
                {
                    WishCardId = card.Id,
                    Group = card.Group.ToString(),
                    Label = card.Label,
                    Source = group.First().Source.ToString(),
                    IsActive = card.IsActive && !card.IsDeleted,
                    OtherPublishedStoryCount = otherMatches
                        .Where(other => other.WishCardId == card.Id && published.Contains(other.StoryId))
                        .Select(other => other.StoryId).Distinct().Count()
                };
            })
            .OrderBy(dto => cards[dto.WishCardId].Group)
            .ThenBy(dto => cards[dto.WishCardId].SortOrder)
            .ThenBy(dto => dto.WishCardId)
            .ToList();
    }

    private async Task<int> CountPendingWishesAsync(int storyId, CancellationToken cancellationToken)
    {
        var wishIds = (await _unitOfWork.Repository<StoryWishSuggestion>().FindAsync(
                row => row.StoryId == storyId && !row.IsDeleted && row.CommunitySubmissionId == null,
                cancellationToken: cancellationToken))
            .Select(row => row.StoryWishId).Distinct().ToList();
        if (wishIds.Count == 0)
        {
            return 0;
        }

        return await _unitOfWork.Repository<StoryWish>().CountAsync(
            wish => wishIds.Contains(wish.Id) && !wish.IsDeleted
                    && (wish.Status == StoryWishStatus.Pending || wish.Status == StoryWishStatus.Accepted),
            cancellationToken);
    }
}
