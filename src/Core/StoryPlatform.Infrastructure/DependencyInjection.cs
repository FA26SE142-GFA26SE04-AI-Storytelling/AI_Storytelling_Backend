using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Export;
using StoryPlatform.Application.Abstractions.Payments;
using StoryPlatform.Application.Features.Outline.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Infrastructure.AI;
using StoryPlatform.Infrastructure.BackgroundServices;
using StoryPlatform.Infrastructure.Communication;
using StoryPlatform.Infrastructure.Export;
using StoryPlatform.Infrastructure.Payments;
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

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<IWorkbookExportBuilder, ClosedXmlWorkbookExportBuilder>();
        services.AddSingleton<IArchiveExportBuilder, ZipCsvArchiveExportBuilder>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        services.AddHttpClient<IEmailSender, ResendEmailSender>();

        services.Configure<AIServiceOptions>(configuration.GetSection(AIServiceOptions.SectionName));
        services.AddHttpClient<IAIStoryGenerationClient, AIStoryGenerationClient>();
        services.Configure<OutlineWorkerOptions>(configuration.GetSection(OutlineWorkerOptions.SectionName));
        services.AddSingleton<IOutlineJobFailureFinalizer, OutlineJobFailureFinalizer>();
        services.AddHostedService<OutlineGenerationWorker>();
        services.Configure<ContentGenerationWorkerOptions>(configuration.GetSection(ContentGenerationWorkerOptions.SectionName));
        var contentOptions = new ContentGenerationOptions();
        configuration.GetSection(ContentGenerationOptions.SectionName).Bind(contentOptions);
        services.AddSingleton(contentOptions);
        services.AddSingleton<IContentGenerationJobFailureFinalizer, ContentGenerationJobFailureFinalizer>();
        services.AddHostedService<ContentGenerationWorker>();

        services.Configure<SePayOptions>(configuration.GetSection(SePayOptions.SectionName));
        services.AddSingleton<ISePayQrUrlBuilder, SePayQrUrlBuilder>();
        services.AddSingleton<ISePayWebhookAuthenticator, SePayWebhookAuthenticator>();
        services.Configure<PaymentExpirySweepWorkerOptions>(
            configuration.GetSection(PaymentExpirySweepWorkerOptions.SectionName));
        services.AddHostedService<PaymentExpirySweepWorker>();

        return services;
    }
}
