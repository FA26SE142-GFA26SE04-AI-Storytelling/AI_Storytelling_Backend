namespace StoryPlatform.Application.Features.ChildProfiles.Safety;

/// <summary>
/// Phiên bản điều khoản đồng thuận (consent) hiện hành — Luồng 1 Bước 1.4.
/// Do server quyết định; client không được gửi lên.
/// </summary>
public static class ConsentPolicy
{
    public const int CurrentVersion = 1;

    /// <summary>Consent đã cũ khi phiên bản từng đồng ý nhỏ hơn bản hiện hành.</summary>
    public static bool IsOutdated(int consentPolicyVersion) => consentPolicyVersion < CurrentVersion;
}
