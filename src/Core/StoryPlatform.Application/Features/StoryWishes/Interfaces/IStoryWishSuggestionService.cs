using StoryPlatform.Domain.Entities;
namespace StoryPlatform.Application.Features.StoryWishes.Interfaces;
public interface IStoryWishSuggestionService
{
    /// <summary>Chấm điểm và thêm tối đa 3 dòng gợi ý. KHÔNG tự lưu: người gọi SaveChanges trong giao dịch của mình.</summary>
    Task<int> ComputeAsync(StoryWish wish, CancellationToken ct = default);
    Task<List<WishSuggestionDto>> ForChildAsync(StoryWish wish, CancellationToken ct = default);
    Task<ParentWishSuggestionsDto> ForParentAsync(int parentUserId, int wishId, CancellationToken ct = default);
}
