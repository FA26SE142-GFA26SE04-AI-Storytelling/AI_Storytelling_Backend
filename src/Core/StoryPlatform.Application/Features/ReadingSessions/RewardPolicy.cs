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
}
