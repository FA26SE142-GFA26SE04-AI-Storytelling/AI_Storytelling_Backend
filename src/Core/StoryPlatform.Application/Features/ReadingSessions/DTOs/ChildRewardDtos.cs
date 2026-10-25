namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Trẻ vừa được thưởng gì trong một thao tác.</summary>
public sealed class RewardOutcome
{
    public static readonly RewardOutcome None = new(0, Array.Empty<string>());

    public RewardOutcome(int expGained, IReadOnlyList<string> newBadgeCodes)
    {
        ExpGained = expGained;
        NewBadgeCodes = newBadgeCodes;
    }

    public int ExpGained { get; }
    public IReadOnlyList<string> NewBadgeCodes { get; }
}

public class ChildRewardsDto
{
    public int ExpTotal { get; set; }
    /// <summary>Streak đang còn hiệu lực (0 nếu trẻ đã bỏ lỡ từ hôm kia trở đi).</summary>
    public int StreakDays { get; set; }
    public List<ChildBadgeDto> Badges { get; set; } = new();
}

public class ChildBadgeDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Earned { get; set; }
    public DateTime? EarnedAt { get; set; }
}
