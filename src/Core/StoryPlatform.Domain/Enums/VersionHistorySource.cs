namespace StoryPlatform.Domain.Enums;

/// <summary>Nguồn thay đổi trong child_profile_version_history.</summary>
public enum VersionHistorySource
{
    /// <summary>Owner / người có approve_reading_level sửa trực tiếp.</summary>
    Manual = 1,

    /// <summary>Áp dụng một Recommendation của Luồng 6.</summary>
    Recommendation = 2
}
