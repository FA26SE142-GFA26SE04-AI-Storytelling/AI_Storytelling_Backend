using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>Gắn mỗi tương tác vào scene kết thúc tại đoạn neo.</summary>
public static class ListeningInteractionSceneBinder
{
    public static IReadOnlyDictionary<int, int> Bind(
        IReadOnlyList<StoryScene> scenes, IReadOnlyList<ListeningInteraction> interactions)
    {
        var result = new Dictionary<int, int>();
        foreach (var interaction in interactions.Where(x => !x.IsDeleted))
            result[interaction.Id] = scenes.FirstOrDefault(x => x.TextRangeEnd == interaction.AnchorOffset)?.Id
                ?? throw new InvalidOperationException("INTERACTION_SCENE_UNRESOLVED");
        return result;
    }
}
