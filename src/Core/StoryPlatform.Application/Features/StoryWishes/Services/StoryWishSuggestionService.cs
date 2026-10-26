using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryWishes.Services;

public class StoryWishSuggestionService : IStoryWishSuggestionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildRoadmapEligibility _eligibility;
    private readonly ISupervisionAccessGuard _guard;

    public StoryWishSuggestionService(
        IUnitOfWork unitOfWork, IChildRoadmapEligibility eligibility, ISupervisionAccessGuard guard)
    {
        _unitOfWork = unitOfWork;
        _eligibility = eligibility;
        _guard = guard;
    }

    public async Task<int> ComputeAsync(StoryWish wish, CancellationToken ct = default)
    {
        if (wish.Kind != StoryWishKind.Wish || wish.CharacterCardId is not int character
            || wish.PlaceCardId is not int place)
        {
            return 0;
        }

        var wishId = wish.Id;
        var suggestions = _unitOfWork.Repository<StoryWishSuggestion>();
        if (await suggestions.ExistsAsync(row => row.StoryWishId == wishId && !row.IsDeleted, ct))
        {
            return 0;   // Danh sách cố định: không chạy lại.
        }

        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(wish.ChildProfileId, ct);
        if (child == null || child.IsDeleted)
        {
            return 0;
        }

        var challenge = wish.ChallengeCardId;
        var lesson = wish.LessonLeftToParent ? null : wish.LessonCardId;
        var cardIds = new List<int> { character, place };
        if (challenge is int c) cardIds.Add(c);
        if (lesson is int l) cardIds.Add(l);

        var matches = await _unitOfWork.Repository<StoryWishCardMatch>().FindAsync(
            match => cardIds.Contains(match.WishCardId) && !match.IsDeleted, cancellationToken: ct);
        var hitsByStory = matches.GroupBy(match => match.StoryId)
            .ToDictionary(group => group.Key, group => group.Select(match => match.WishCardId).ToHashSet());
        // Điều kiện hiển thị: phải khớp nhân vật hoặc nơi chốn.
        var candidateIds = hitsByStory
            .Where(pair => pair.Value.Contains(character) || pair.Value.Contains(place))
            .Select(pair => pair.Key).ToList();
        if (candidateIds.Count == 0)
        {
            return 0;
        }

        var childId = child.Id;
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            story => candidateIds.Contains(story.Id) && !story.IsDeleted, cancellationToken: ct);
        var community = (await _unitOfWork.Repository<ModerationItem>().FindAsync(
                item => item.ItemType == ModerationItemType.CommunitySubmission
                        && item.Status == ModerationItemStatus.Approved
                        && !item.IsDeleted
                        && item.StoryId.HasValue
                        && candidateIds.Contains(item.StoryId.Value),
                cancellationToken: ct))
            .GroupBy(item => item.StoryId!.Value)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Id));
        var finished = (await _unitOfWork.Repository<ReadingSession>().FindAsync(
                session => session.ChildProfileId == childId
                           && session.Status == SessionStatus.Completed
                           && candidateIds.Contains(session.StoryId)
                           && !session.IsDeleted,
                cancellationToken: ct))
            .Select(session => session.StoryId).ToHashSet();
        var learning = await _unitOfWork.Repository<LearningProfile>().FirstOrDefaultAsync(
            profile => profile.ChildProfileId == childId && !profile.IsDeleted, cancellationToken: ct);

        var scored = new List<(Story Story, int Score, int? SubmissionId, List<int> Matched)>();
        foreach (var story in stories)
        {
            int? submissionId = null;
            if (story.Scope == StoryScope.Parent && story.ChildProfileId != childId)
            {
                // Truyện riêng của bé khác chỉ mở được khi đã thành truyện Cộng đồng.
                if (!community.TryGetValue(story.Id, out var itemId))
                {
                    continue;
                }

                submissionId = itemId;
            }

            if (await _eligibility.EvaluateAsync(child, story, ct) != null)
            {
                continue;
            }

            var hits = hitsByStory[story.Id];
            var matched = new List<int>();
            var score = 0;
            if (hits.Contains(character)) { score += StoryWishSuggestionPolicy.CharacterPoints; matched.Add(character); }
            if (hits.Contains(place)) { score += StoryWishSuggestionPolicy.PlacePoints; matched.Add(place); }
            if (challenge is int ch && hits.Contains(ch)) { score += StoryWishSuggestionPolicy.ChallengePoints; matched.Add(ch); }
            if (lesson is int le && hits.Contains(le)) { score += StoryWishSuggestionPolicy.LessonPoints; matched.Add(le); }
            if (!finished.Contains(story.Id)) score += StoryWishSuggestionPolicy.NotFinishedPoints;
            if (learning != null && story.ReadingLevel is int level
                && level - learning.ReadingLevel >= StoryWishSuggestionPolicy.TooHardGap)
            {
                score += StoryWishSuggestionPolicy.TooHardPoints;
            }

            if (score >= StoryWishSuggestionPolicy.MinScore)
            {
                scored.Add((story, score, submissionId, matched));
            }
        }

        var top = scored
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Story.CreatedAt)
            .ThenByDescending(item => item.Story.Id)
            .Take(StoryWishSuggestionPolicy.MaxSuggestions)
            .ToList();
        for (var index = 0; index < top.Count; index++)
        {
            await suggestions.AddAsync(new StoryWishSuggestion
            {
                StoryWishId = wishId,
                StoryId = top[index].Story.Id,
                CommunitySubmissionId = top[index].SubmissionId,
                Rank = index + 1,
                Score = top[index].Score,
                MatchedCardIds = string.Join(',', top[index].Matched)
            }, ct);
        }

        return top.Count;
    }

    public async Task<List<WishSuggestionDto>> ForChildAsync(StoryWish wish, CancellationToken ct = default)
    {
        if (wish.Kind != StoryWishKind.Wish
            || (wish.Status != StoryWishStatus.Pending && wish.Status != StoryWishStatus.Accepted))
        {
            return new List<WishSuggestionDto>();
        }

        if (await HoldModeLookup.FindOpenAsync(_unitOfWork, wish.ChildProfileId, ct) != null)
        {
            return new List<WishSuggestionDto>();
        }

        var rows = await LoadAsync(wish.Id, ct);
        return rows.Select(row => Fill(new WishSuggestionDto(), row)).ToList();
    }

    public async Task<ParentWishSuggestionsDto> ForParentAsync(
        int parentUserId, int wishId, CancellationToken ct = default)
    {
        var wish = await _unitOfWork.Repository<StoryWish>().GetByIdAsync(wishId, ct);
        if (wish == null || wish.IsDeleted || wish.Status == StoryWishStatus.Draft)
        {
            throw new NotFoundException("Điều ước", wishId);
        }

        await _guard.EnsurePermissionAsync(wish.ChildProfileId, parentUserId, Permission.ApproveStory, ct);

        var rows = await LoadAsync(wish.Id, ct);
        var storyIds = rows.Select(row => row.Suggestion.StoryId).ToList();
        var childId = wish.ChildProfileId;
        var since = wish.SubmittedAt ?? wish.CreatedAt;
        var sessions = await _unitOfWork.Repository<ReadingSession>().FindAsync(
            session => session.ChildProfileId == childId
                       && storyIds.Contains(session.StoryId)
                       && session.StartedAt >= since
                       && !session.IsDeleted,
            cancellationToken: ct);

        var items = rows.Select(row =>
        {
            var mine = sessions.Where(session => session.StoryId == row.Suggestion.StoryId).ToList();
            var latestDone = mine.Where(session => session.Status == SessionStatus.Completed)
                .OrderByDescending(session => session.CompletedAt ?? session.StartedAt)
                .FirstOrDefault();
            var item = Fill(new ParentWishSuggestionDto(), row);
            item.Opened = mine.Count > 0;
            item.Completed = latestDone != null;
            if (latestDone?.Feeling is StoryFeeling feeling)
            {
                item.Feeling = feeling.ToString();
                item.FeelingLabel = StoryFeelingLabels.Label(feeling);
                if (feeling is StoryFeeling.VeryLike or StoryFeeling.Like)
                {
                    item.Hint = StoryWishSuggestionPolicy.LikedHint(item.Title, item.FeelingLabel);
                }
            }

            return item;
        }).ToList();

        return new ParentWishSuggestionsDto
        {
            WishId = wish.Id,
            Items = items,
            EmptyMessage = items.Count == 0 ? StoryWishSuggestionPolicy.NoSuggestionMessage : null
        };
    }

    private sealed record Row(StoryWishSuggestion Suggestion, Story Story, List<WishCard> Cards);

    /// <summary>Danh sách đã lưu theo thứ tự; bỏ truyện đã bị xóa / lưu trữ / không còn Ready kể từ lúc gợi ý.</summary>
    private async Task<List<Row>> LoadAsync(int wishId, CancellationToken ct)
    {
        var suggestions = (await _unitOfWork.Repository<StoryWishSuggestion>().FindAsync(
                row => row.StoryWishId == wishId && !row.IsDeleted, cancellationToken: ct))
            .OrderBy(row => row.Rank).ToList();
        if (suggestions.Count == 0)
        {
            return new List<Row>();
        }

        var storyIds = suggestions.Select(row => row.StoryId).ToList();
        var stories = (await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: ct)).ToDictionary(story => story.Id);
        var cardIds = suggestions.SelectMany(row => ParseIds(row.MatchedCardIds)).Distinct().ToList();
        var cards = (await _unitOfWork.Repository<WishCard>().FindAsync(
            card => cardIds.Contains(card.Id), cancellationToken: ct)).ToDictionary(card => card.Id);

        return suggestions
            .Where(row => stories.TryGetValue(row.StoryId, out var story)
                          && !story.IsDeleted
                          && story.ArchivedAt == null
                          && story.Status == StoryStatus.Ready)
            .Select(row => new Row(
                row,
                stories[row.StoryId],
                ParseIds(row.MatchedCardIds).Where(cards.ContainsKey).Select(id => cards[id]).ToList()))
            .ToList();
    }

    private static T Fill<T>(T dto, Row row) where T : WishSuggestionDto
    {
        dto.Rank = row.Suggestion.Rank;
        dto.StoryId = row.Suggestion.CommunitySubmissionId == null ? row.Suggestion.StoryId : null;
        dto.CommunitySubmissionId = row.Suggestion.CommunitySubmissionId;
        dto.Title = row.Story.Title ?? string.Empty;
        dto.CoverImageUrl = row.Story.CoverImageUrl;
        dto.MatchedCards = row.Cards.Select(card => new ChildWishCardDto
        {
            Id = card.Id,
            Group = card.Group.ToString(),
            Label = card.Label,
            ImageUrl = card.ImageUrl,
            AudioUrl = card.AudioUrl
        }).ToList();
        return dto;
    }

    private static IEnumerable<int> ParseIds(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var id) ? id : 0)
            .Where(id => id > 0);
}
