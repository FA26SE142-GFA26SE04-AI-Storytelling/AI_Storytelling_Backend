using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Features.ExistingStories.DTOs;

namespace StoryPlatform.Application.Features.ExistingStories.Interfaces;

/// <summary>
/// Đánh giá một StoryVersion theo:
///   - Hard safety (terms bị block từ SafetyPolicy)
///   - Profile fit (ReadingLevel vs VocabularyLevel vs AgeBand vs Length)
/// Decision:
///   - Suitable: nội dung phù hợp -> có thể handoff artifact ngay.
///   - AdaptRecommended: phù hợp có điều kiện -> cần supervisor chọn 1 trong 3.
///   - Blocked: có vi phạm an toàn cứng -> KHÔNG được KeepOriginal/Approval.
/// </summary>
public interface IExistingStoryEvaluationService
{
    Task<ExistingStoryEvaluationDto> EvaluateAsync(
        int userId,
        int storyId,
        int storyVersionId,
        CancellationToken cancellationToken = default);

    Task<ExistingStoryEvaluationDto> GetLatestAsync(
        int userId,
        int storyId,
        int storyVersionId,
        CancellationToken cancellationToken = default);
}
