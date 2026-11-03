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
    public KnowledgeTreeDto KnowledgeTree { get; set; } = new();
}

public class KnowledgeTreeDto
{
    public string Stage { get; set; } = KnowledgeTreePolicy.Seed;
    public string StageName { get; set; } = string.Empty;
    /// <summary>EXP đã tưới vào cây đang trồng.</summary>
    public int CurrentTreeExp { get; set; }
    /// <summary>EXP cần có để lên mốc sau; null khi cây đã ra hoa.</summary>
    public int? NextStageAt { get; set; }
    public int FlowerAt { get; set; } = KnowledgeTreePolicy.FlowerAt;
    /// <summary>Số cây đã ra hoa trong vườn.</summary>
    public int FloweredTreeCount { get; set; }
    /// <summary>EXP chưa tưới, trẻ bấm "Tưới cây" để dùng.</summary>
    public int ExpToWater { get; set; }
}

public class WaterKnowledgeTreeResultDto
{
    public int ExpPoured { get; set; }
    /// <summary>Cây đã ra hoa được đưa vào vườn và trẻ vừa trồng hạt mới.</summary>
    public bool NewTreePlanted { get; set; }
    public string PreviousStage { get; set; } = KnowledgeTreePolicy.Seed;
    public KnowledgeTreeDto Tree { get; set; } = new();
}

public class ChildBadgeDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Earned { get; set; }
    public DateTime? EarnedAt { get; set; }
}
