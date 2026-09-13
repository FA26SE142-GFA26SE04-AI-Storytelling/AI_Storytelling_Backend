using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Application.OutlineGeneration;

public sealed record OutlineSafetyResult(bool IsAllowed, string ReasonCode, string? FallbackMessage = null);

public interface IOutlineOutputGuardrail
{
    OutlineSafetyResult Validate(GenerateOutlineRequest request, GenerateOutlineResponse response);
}
