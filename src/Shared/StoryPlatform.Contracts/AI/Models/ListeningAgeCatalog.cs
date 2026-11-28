namespace StoryPlatform.Contracts.AI.Models;

/// <summary>Exact ages with a completed listening interaction set, shared by Core and AI.</summary>
public static class ListeningAgeCatalog
{
    public static IReadOnlyList<int> BuiltAges { get; } = Array.AsReadOnly(new[] { 6, 7, 8, 9 });

    /// <summary>Cảm xúc cho phép của bong bóng suy nghĩ (tuổi 8), dùng chung cho Core và dịch vụ AI.</summary>
    public static IReadOnlyList<string> ThoughtEmotions { get; } = Array.AsReadOnly(
        new[] { "vui", "buồn", "sợ", "ngạc nhiên", "giận", "lo lắng", "tự hào", "háo hức" });
}
