namespace StoryPlatform.Application.Features.MediaGeneration;

/// <summary>
/// Signals a configuration or deterministic input failure that will not be healed by an immediate retry.
/// </summary>
public sealed class PermanentMediaGenerationException : Exception
{
    public string ErrorCode { get; }

    public PermanentMediaGenerationException(string errorCode, string? message = null)
        : base(message ?? errorCode)
    {
        ErrorCode = errorCode;
    }
}
