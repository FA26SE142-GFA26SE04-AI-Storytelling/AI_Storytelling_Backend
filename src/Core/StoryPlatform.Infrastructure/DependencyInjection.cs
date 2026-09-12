using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Infrastructure.AI;
using StoryPlatform.Infrastructure.Communication;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using StoryPlatform.Infrastructure.Security;

namespace StoryPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = ConnectionStringHelper.GetConnectionString(configuration);

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (!string.IsNullOrEmpty(connectionString))
            {
                options.UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
            }
        });

        // Đăng ký Generic Repository và Unit of Work
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        services.AddHttpClient<IEmailSender, ResendEmailSender>();

        services.Configure<AIServiceOptions>(configuration.GetSection(AIServiceOptions.SectionName));
        services.AddHttpClient<IAIStoryGenerationClient, AIStoryGenerationClient>();

        return services;
    }
}
