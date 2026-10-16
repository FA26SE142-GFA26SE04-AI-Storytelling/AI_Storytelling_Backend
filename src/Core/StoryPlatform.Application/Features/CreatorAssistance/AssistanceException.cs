using StoryPlatform.Application.Common.Exceptions;

namespace StoryPlatform.Application.Features.CreatorAssistance;

public sealed class AssistanceException(string code, int statusCode = 409) : AppException(code, statusCode)
{
    public string Code { get; } = code;
}
