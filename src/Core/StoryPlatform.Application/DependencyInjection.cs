using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Interfaces;
using StoryPlatform.Application.Features.AIStoryInput.Services;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Application.Features.Stories.Services;

namespace StoryPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IStoryService, StoryService>();
        services.AddScoped<IAIStoryInputService, AIStoryInputService>();
        services.AddSingleton<IInputGuardrail, RuleBasedInputGuardrail>();

        return services;
    }
}
