namespace StoryPlatform.Application.Features.MediaGeneration;

public enum MediaProviderPrivacyReason
{
    EmailDetected = 1,
    PhoneNumberDetected = 2,
    CredentialOrTokenDetected = 3,
    PrivateKeyDetected = 4,
    UrlWithCredentialDetected = 5,
    InputExceedsScanBudget = 6
}

/// <summary>
/// Permanent fail-closed boundary exception raised when recognizable PII or credentials
/// are detected in outbound media provider text before HTTP dispatch.
/// Never includes matched text, raw prompt content, or secrets in its message.
/// </summary>
public sealed class MediaProviderPrivacyException : Exception
{
    public const string DefaultErrorCode = "MEDIA_PROVIDER_PRIVACY_REVIEW_REQUIRED";

    public string ErrorCode => DefaultErrorCode;
    public MediaProviderPrivacyReason Reason { get; }

    public MediaProviderPrivacyException(MediaProviderPrivacyReason reason)
        : base($"{DefaultErrorCode}: {reason}")
    {
        Reason = reason;
    }
}
