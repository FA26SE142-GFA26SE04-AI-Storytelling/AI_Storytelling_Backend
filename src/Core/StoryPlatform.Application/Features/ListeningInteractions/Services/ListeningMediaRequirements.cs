using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>Media bắt buộc của tương tác; readiness và API trẻ dùng chung.</summary>
public static class ListeningMediaRequirements
{
    public static IReadOnlyList<string> Missing(ListeningInteraction interaction,
        IReadOnlyList<ListeningInteractionOption> options, IReadOnlyList<MediaAsset> assets)
    {
        var missing = new List<string>();
        foreach (var part in ListeningAudioParts.For(interaction, options).Where(x => x.Required))
            if (FindAudio(assets, interaction.Id, part.OptionId, part.PartKey) is null)
                missing.Add($"{interaction.Id}:{(part.OptionId is int optionId ? $"o{optionId}" : "i")}:{part.PartKey}");
        foreach (var option in ImageOptions(interaction, options))
            if (FindImage(assets, option.Id) is null)
                missing.Add($"{interaction.Id}:o{option.Id}:image");
        return missing;
    }

    /// <summary>Loại có tranh gắn với đáp án: Đoán tranh, Chạm vào nhân vật, Chữ cái (chân dung) và điểm dừng nghĩa đen tuổi 6.</summary>
    public static bool HasOptionImages(ListeningInteraction interaction) =>
        interaction.Type is ListeningInteractionType.PicturePrediction or ListeningInteractionType.WhoSaidIt
            or ListeningInteractionType.CharacterLetter ||
        interaction is { Type: ListeningInteractionType.Checkpoint, Kind: CheckpointKind.Literal };

    public static IEnumerable<ListeningInteractionOption> ImageOptions(ListeningInteraction interaction,
        IReadOnlyList<ListeningInteractionOption> options) => options
        .Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted && HasOptionImages(interaction) &&
            (interaction.Type != ListeningInteractionType.CharacterLetter || x.IsMatch))
        .OrderBy(x => x.SortOrder);

    public static MediaAsset? FindAudio(IReadOnlyList<MediaAsset> assets, int interactionId, int? optionId, string partKey) =>
        NewestUsable(assets.Where(asset => asset.Type == MediaType.ListeningAudio && asset.ListeningInteractionId == interactionId &&
            asset.ListeningInteractionOptionId == optionId && asset.AudioPartKey == partKey));

    public static MediaAsset? FindImage(IReadOnlyList<MediaAsset> assets, int optionId) =>
        NewestUsable(assets.Where(asset => asset.Type == MediaType.ListeningImage && asset.ListeningInteractionOptionId == optionId));

    internal static bool IsUsable(MediaAsset asset) =>
        asset.IsCurrent && !asset.IsDeleted && asset.Status == MediaStatus.Ready &&
        asset.ValidationStatus == ValidationStatus.Passed && !string.IsNullOrWhiteSpace(asset.Url);

    private static MediaAsset? NewestUsable(IEnumerable<MediaAsset> assets) =>
        assets.Where(IsUsable).OrderByDescending(asset => asset.CompletedAt ?? asset.CreatedAt)
            .ThenByDescending(asset => asset.Id).FirstOrDefault();
}
