using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Auth.Interfaces;
using StoryPlatform.Application.Auth.Services;
using StoryPlatform.Application.Stories.Interfaces;
using StoryPlatform.Application.Stories.Services;

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
