using System;

namespace StoryPlatform.Application.Features.ExistingStories.Helpers;

/// <summary>
/// Sinh idempotency key nhất quán cho các mutation trên Existing Story.
/// </summary>
internal static class ExistingStoryIdempotencyKeys
{
    /// <summary>
    /// Key cho import lần đầu. Nếu client không truyền, dùng user+child+content-hash.
    /// </summary>
    public static string ForImport(int userId, int childProfileId, string content)
    {
        var hash = ShortHash(content);
        return $"existing:import:u{userId}:c{childProfileId}:{hash}";
    }

    /// <summary>
    /// Key cho một stable version -> artifact handoff.
    /// </summary>
    public static string ForArtifactHandoff(int storyId, int storyVersionId, int operation)
        => $"existing:p3:{storyId}:v{storyVersionId}:op{operation}";

    /// <summary>
    /// Key cho adapt/manual edit để đảm bảo idempotent version creation.
    /// </summary>
    public static string ForVersionMutation(int storyId, int baseVersionId, string suffix)
        => $"existing:v:{storyId}:b{baseVersionId}:{suffix}";

    private static string ShortHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return "empty";
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
