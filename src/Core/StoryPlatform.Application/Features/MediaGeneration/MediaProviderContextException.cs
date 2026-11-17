namespace StoryPlatform.Application.Features.MediaGeneration;

public enum MediaProviderContextInvalidReason
{
    EmptyContextJson = 1,
    MalformedJson = 2,
    DuplicateProperty = 3,
    UnsupportedSchemaVersion = 4,
    MissingStoryFacts = 5,
    MissingTitle = 6,
    MissingCanonicalContent = 7,
    InvalidVisualDesign = 8,
    InvalidFieldType = 9,
    OutputContractViolation = 10
}

/// <summary>
/// Fail-closed boundary exception raised when an internal MediaContext cannot be projected
/// into a valid provider DTO. Never includes raw JSON or sensitive metadata in its message.
/// </summary>
public sealed class MediaProviderContextException : Exception
{
    public const string DefaultErrorCode = "MEDIA_PROVIDER_CONTEXT_INVALID";

    public string ErrorCode => DefaultErrorCode;
    public MediaProviderContextInvalidReason Reason { get; }

    public MediaProviderContextException(MediaProviderContextInvalidReason reason)
        : base($"{DefaultErrorCode}: {reason}")
    {
        Reason = reason;
    }
}
