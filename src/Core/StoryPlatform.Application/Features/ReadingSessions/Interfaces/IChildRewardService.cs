using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildRewardService
{
    /// <summary>Gọi sau khi đã đặt phiên Completed, TRƯỚC SaveChanges của người gọi. Không tự lưu.</summary>
    Task<RewardOutcome> OnStoryCompletedAsync(ReadingSession session, bool isRetake, CancellationToken ct = default);

    /// <summary>Gọi sau khi đã thêm QuizAttempt, TRƯỚC SaveChanges của người gọi. Không tự lưu.</summary>
    Task<RewardOutcome> OnQuizAnsweredAsync(ReadingSession session, bool firstTimeCorrect, bool perfectRunJustCompleted, CancellationToken ct = default);

    Task<ChildRewardsDto> GetSummaryAsync(int childProfileId, CancellationToken ct = default);
}
