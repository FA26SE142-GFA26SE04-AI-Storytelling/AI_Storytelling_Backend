using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildRewardService
{
    /// <summary>Gọi sau khi đã đặt phiên Completed, TRƯỚC SaveChanges của người gọi. Không tự lưu.</summary>
    Task<RewardOutcome> OnStoryCompletedAsync(ReadingSession session, bool isRetake, CancellationToken ct = default);

    /// <summary>Gọi sau khi đã thêm QuizAttempt, TRƯỚC SaveChanges của người gọi. Không tự lưu.</summary>
    Task<RewardOutcome> OnQuizAnsweredAsync(ReadingSession session, bool firstTimeCorrect, bool perfectRunJustCompleted, CancellationToken ct = default);

    /// <summary>Gọi sau khi đã thêm dòng Sổ tay mới, TRƯỚC SaveChanges của người gọi. Không tự lưu.</summary>
    Task<RewardOutcome> OnWordCollectedAsync(ReadingSession session, int wordsInNotebook, CancellationToken ct = default);

    /// <summary>Gọi khi trẻ vừa hoàn thành truyện cuối của lộ trình (cùng lúc gửi roadmap_completed). Không tự lưu.</summary>
    Task<RewardOutcome> OnRoadmapFinishedAsync(ReadingSession session, CancellationToken ct = default);

    Task<ChildRewardsDto> GetSummaryAsync(int childProfileId, CancellationToken ct = default);

    /// <summary>Đổ toàn bộ EXP chưa tưới vào Cây tri thức, tối đa tới mốc ra hoa. Tự lưu.</summary>
    Task<WaterKnowledgeTreeResultDto> WaterKnowledgeTreeAsync(int childProfileId, CancellationToken ct = default);
}
