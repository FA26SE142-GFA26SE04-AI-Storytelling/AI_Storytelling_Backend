namespace StoryPlatform.Application.Features.MediaGeneration;

/// <summary>
/// Signals a provider availability failure for which the worker should pause before reclaiming work.
/// </summary>
public sealed class TransientMediaGenerationException : Exception
{
    public string ErrorCode { get; }

    public TransientMediaGenerationException(string errorCode, Exception? innerException = null)
        : base(errorCode, innerException)
    {
        ErrorCode = errorCode;
    }
}

