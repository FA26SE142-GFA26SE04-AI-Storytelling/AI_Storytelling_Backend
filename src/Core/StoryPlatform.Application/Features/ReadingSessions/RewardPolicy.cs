using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class RewardPolicy
{
    public const int StoryCompletedExp = 10;
    public const int QuizItemCorrectExp = 2;
    /// <summary>Mỗi từ mới lưu vào Sổ tay (chỉ lần đầu lưu từ đó).</summary>
    public const int WordCollectedExp = 1;
    public const int WordCollectorCount = 50;
    public const int BookwormStoryCount = 5;
    public const int ShortStreakDays = 3;
    public const int LongStreakDays = 7;

    /// <summary>Việt Nam là UTC+7 quanh năm (không có giờ mùa hè).</summary>
    public static readonly TimeSpan LocalOffset = TimeSpan.FromHours(7);

    public static DateOnly LocalToday(TimeProvider clock) =>
        DateOnly.FromDateTime((clock.GetUtcNow() + LocalOffset).DateTime);

    /// <summary>
    /// Chuỗi ngày đọc còn hiệu lực: giữ nguyên nếu lần đọc cuối là hôm nay hoặc hôm qua, ngược lại là 0.
    /// Dùng chung cho màn thưởng và màn thói quen để hai nơi luôn cho cùng một con số.
    /// </summary>
    public static int LiveStreakDays(Achievement? achievement, DateOnly today)
    {
        var last = achievement?.LastStreakDate;
        var alive = last.HasValue && (last.Value == today || last.Value == today.AddDays(-1));
        return alive ? achievement!.StreakDays : 0;
    }
}
