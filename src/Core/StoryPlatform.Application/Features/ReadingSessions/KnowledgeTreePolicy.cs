namespace StoryPlatform.Application.Features.ReadingSessions;

public sealed record KnowledgeTreeStageInfo(string Code, string Name, int MinExp, int? NextAt);

/// <summary>Mốc của Cây tri thức (WF-L3A UC-L3A-08 bước 4). Tính theo EXP đã tưới vào cây đang trồng.</summary>
public static class KnowledgeTreePolicy
{
    public const string Seed = "SEED";
    public const string Sprout = "SPROUT";
    public const string Sapling = "SAPLING";
    public const string Grown = "GROWN";
    public const string Flowering = "FLOWERING";

    public const int FlowerAt = 500;

    private static readonly IReadOnlyList<KnowledgeTreeStageInfo> Stages = new List<KnowledgeTreeStageInfo>
    {
        new(Seed, "Hạt", 0, 30),
        new(Sprout, "Mầm", 30, 100),
        new(Sapling, "Cây non", 100, 250),
        new(Grown, "Cây lớn", 250, FlowerAt),
        new(Flowering, "Cây ra hoa", FlowerAt, null)
    };

    public static KnowledgeTreeStageInfo StageOf(int currentTreeExp) =>
        Stages.Last(stage => currentTreeExp >= stage.MinExp);
}
