namespace StoryPlatform.Application.Features.ChildProfiles.Safety;

/// <summary>
/// Quy đổi "độ dài truyện" của phụ huynh thành độ dài đích và trần dùng khi sinh truyện.
/// Công thức này được frontend phản chiếu ở <c>effectiveStoryLength</c> (src/app/lib/guardian.ts); đổi ở đâu thì đổi cả hai.
/// </summary>
public static class StoryLengthRules
{
    /// <summary>Độ dài nhỏ nhất phụ huynh được đặt.</summary>
    public const int MinimumTarget = 300;

    /// <summary>AI được viết tối đa 110% độ dài đích (khớp LearningGenerationProfile.ForAge).</summary>
    public const decimal UpperTolerance = 1.1m;

    public static int UpperBound(int target) => (int)Math.Ceiling(target * UpperTolerance);

    /// <summary>
    /// Trần lưu trong cài đặt an toàn. Có độ dài tự chỉnh thì trần tự nâng để không thấp hơn số từ AI được phép viết;
    /// không bao giờ hạ trần đang có.
    /// </summary>
    public static int CapFor(int? storyLengthTarget, int requestedCap) =>
        storyLengthTarget is > 0 ? Math.Max(requestedCap, UpperBound(storyLengthTarget.Value)) : requestedCap;

    /// <summary>
    /// Độ dài tự chỉnh chỉ được thấp hơn hoặc bằng mặc định theo mức đọc; trần là UpperBound(target).
    /// Giới hạn cũ thấp hơn mặc định được giữ nguyên làm cả độ dài đích và trần.
    /// </summary>
    /// <param name="storyLengthTarget">Số phụ huynh tự chỉnh; null = theo mức đọc.</param>
    /// <param name="storedCap"><c>SafetyPolicy.MaxStoryLength</c> đang lưu.</param>
    /// <param name="levelDefaultTarget">Độ dài mặc định theo nhóm tuổi và mức đọc (900 hoặc 1500).</param>
    public static (int Target, int Cap) ForGeneration(int? storyLengthTarget, int storedCap, int levelDefaultTarget)
    {
        if (storyLengthTarget is > 0)
        {
            var target = Math.Min(storyLengthTarget.Value, levelDefaultTarget);
            return (target, UpperBound(target));
        }

        // Giới hạn thấp hơn mặc định do phụ huynh đặt từ trước khi có "độ dài truyện": giữ nguyên làm độ dài đích và trần.
        if (storedCap < levelDefaultTarget)
        {
            return (storedCap, storedCap);
        }

        return (levelDefaultTarget, UpperBound(levelDefaultTarget));
    }
}
