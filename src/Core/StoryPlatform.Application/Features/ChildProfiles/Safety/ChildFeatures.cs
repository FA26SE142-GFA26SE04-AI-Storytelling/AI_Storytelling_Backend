using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildProfiles.Safety;

public sealed record ChildFeatureDefinition(string Code, string Group, string Description);

/// <summary>
/// Quyền của bé: chức năng phụ huynh bật/tắt cho từng bé, lưu trên <see cref="SafetyPolicy"/>.
/// Chức năng cốt lõi (đọc truyện, lộ trình, truyện gia đình) không có mã và luôn bật.
/// </summary>
public static class ChildFeatures
{
    public const string LearningGroup = "Học tập";
    public const string ExtraGroup = "Mở rộng";

    public const string Quiz = "child.quiz";
    public const string Notebook = "child.notebook";
    public const string LibraryBrowse = "child.library.browse";
    public const string Wishes = "child.wishes";
    public const string Voice = "child.voice";
    public const string Reflection = "child.reflection";
    public const string Rewards = "child.rewards";

    public static readonly IReadOnlyList<ChildFeatureDefinition> All =
    [
        new(Quiz, LearningGroup, "Câu đố sau truyện"),
        new(Notebook, LearningGroup, "Sổ từ vựng"),
        new(LibraryBrowse, ExtraGroup, "Tìm truyện trong thư viện"),
        new(Wishes, ExtraGroup, "Gửi điều ước"),
        new(Voice, ExtraGroup, "Đọc to và trả lời bằng giọng nói"),
        new(Reflection, ExtraGroup, "Cảm nhận sau truyện"),
        new(Rewards, ExtraGroup, "Phần thưởng và cây kiến thức"),
    ];

    /// <summary>Bé chưa có cài đặt an toàn thì mọi chức năng bật (giữ hành vi cũ).</summary>
    public static bool IsEnabled(SafetyPolicy? policy, string code)
    {
        if (All.All(f => f.Code != code)) throw new ArgumentException($"Unknown child feature '{code}'.", nameof(code));
        if (policy is null) return true;
        return code switch
        {
            Quiz => policy.QuizEnabled,
            Notebook => policy.NotebookEnabled,
            LibraryBrowse => policy.LibraryBrowseEnabled,
            Wishes => policy.StoryWishesEnabled,
            Voice => policy.VoiceAnswersEnabled,
            Reflection => policy.ReflectionEnabled,
            _ => policy.RewardsEnabled,
        };
    }
}

/// <summary>Bé gọi chức năng phụ huynh đã tắt.</summary>
public sealed class ChildFeatureDisabledException()
    : AppException("Bố mẹ chưa bật chức năng này.", 403, "CHILD_FEATURE_DISABLED");
