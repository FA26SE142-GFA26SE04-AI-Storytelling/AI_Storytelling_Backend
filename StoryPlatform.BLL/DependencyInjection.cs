using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.BLL.Common.Security;
using StoryPlatform.BLL.Modules.Auth.Interfaces;
using StoryPlatform.BLL.Modules.Auth.Services;
using StoryPlatform.BLL.Modules.Story.Interfaces;
using StoryPlatform.BLL.Modules.Story.Services;

namespace StoryPlatform.BLL;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessLogicLayer(this IServiceCollection services, IConfiguration configuration)
    {
        // Cấu hình JWT Options
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        // Cấu hình Common Security
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // Đăng ký các Module Services trong tầng BLL
        // 1. Module Auth
        services.AddScoped<IAuthService, AuthService>();

        // 2. Module Story
        services.AddScoped<IStoryService, StoryService>();

        return services;
    }
}
