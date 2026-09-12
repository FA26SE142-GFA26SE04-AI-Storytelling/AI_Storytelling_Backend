using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Application.Features.Stories.Services;

namespace StoryPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IStoryService, StoryService>();

        return services;
    }
}
