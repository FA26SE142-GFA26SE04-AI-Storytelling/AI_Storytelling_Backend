namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Đối tượng mua gói (Luồng 2, 2f): mỗi vai trò chỉ thấy và chỉ mua được gói của mình.
/// </summary>
public enum PlanAudience
{
    /// <summary>Phụ huynh (role User).</summary>
    User = 1,
    /// <summary>Content Creator.</summary>
    Creator = 2
}
