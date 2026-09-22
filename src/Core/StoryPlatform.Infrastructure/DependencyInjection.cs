using System.IO;
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
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Infrastructure.AI;
using StoryPlatform.Infrastructure.BackgroundServices;
using StoryPlatform.Infrastructure.Caching;
using StoryPlatform.Infrastructure.Communication;
using StoryPlatform.Infrastructure.Export;
using StoryPlatform.Infrastructure.Payments;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using StoryPlatform.Infrastructure.Security;
using StoryPlatform.Infrastructure.Storage;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;

namespace StoryPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = ConnectionStringHelper.GetConnectionString(configuration);

        services.AddRedisCache(configuration);

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

        services.Configure<MediaGenerationOptions>(configuration.GetSection(MediaGenerationOptions.SectionName));
        var mediaOptions = new MediaGenerationOptions();
        configuration.GetSection(MediaGenerationOptions.SectionName).Bind(mediaOptions);
        services.AddSingleton(mediaOptions);

        // Gemini / Vertex AI options
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<ImageGenerationOptions>(configuration.GetSection(ImageGenerationOptions.SectionName));
        services.Configure<TtsServiceOptions>(configuration.GetSection(TtsServiceOptions.SectionName));
        services.Configure<MediaEvaluationOptions>(configuration.GetSection(MediaEvaluationOptions.SectionName));
        services.Configure<VertexOptions>(configuration.GetSection(VertexOptions.SectionName));

        // Vertex AI token provider (singleton, shared across all Vertex-authenticated HttpClients)
        services.AddSingleton<VertexTokenProvider>();

        // TextToSpeech client — uses Application Default Credentials by default, or an explicit
        // credentials file. When Vertex AI is enabled for media, the same service-account key
        // can be shared via VertexOptions.CredentialsPath.
        services.AddSingleton<Google.Cloud.TextToSpeech.V1Beta1.TextToSpeechClient>(sp =>
        {
            var ttsOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TtsServiceOptions>>().Value;
            var vertexOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<VertexOptions>>().Value;
            var builder = new Google.Cloud.TextToSpeech.V1Beta1.TextToSpeechClientBuilder();

            string? credPath = null;
            if (!string.IsNullOrWhiteSpace(ttsOpts.CredentialsFilePath) && File.Exists(ttsOpts.CredentialsFilePath))
                credPath = ttsOpts.CredentialsFilePath;
            else if (!string.IsNullOrWhiteSpace(vertexOpts.CredentialsPath) && File.Exists(vertexOpts.CredentialsPath))
                credPath = vertexOpts.CredentialsPath;

            if (credPath is not null)
                builder.CredentialsPath = credPath;
            return builder.Build();
        });

        // Gemini REST providers — registered via HttpClient factory.
        // When Vertex AI is enabled, the VertexAuthDelegatingHandler injects a fresh
        // OAuth2 Bearer token on every request; otherwise x-goog-api-key is used per-provider.
        services.AddHttpClient<GeminiImageGenerationProvider>(client => client.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(services =>
            {
                var tokenProvider = services.GetRequiredService<VertexTokenProvider>();
                return new VertexAuthDelegatingHandler(tokenProvider) { InnerHandler = new HttpClientHandler() };
            });
        services.AddHttpClient<GeminiMediaAlignmentEvaluator>(client => client.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(services =>
            {
                var tokenProvider = services.GetRequiredService<VertexTokenProvider>();
                return new VertexAuthDelegatingHandler(tokenProvider) { InnerHandler = new HttpClientHandler() };
            });
        services.AddHttpClient<GeminiMediaSafetyEvaluator>(client => client.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(services =>
            {
                var tokenProvider = services.GetRequiredService<VertexTokenProvider>();
                return new VertexAuthDelegatingHandler(tokenProvider) { InnerHandler = new HttpClientHandler() };
            });
        services.AddHttpClient<GeminiSemanticSceneSegmentationProvider>(client => client.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(services =>
            {
                var tokenProvider = services.GetRequiredService<VertexTokenProvider>();
                return new VertexAuthDelegatingHandler(tokenProvider) { InnerHandler = new HttpClientHandler() };
            });
        services.AddHttpClient<GeminiMediaContextExtractor>(client => client.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(services =>
            {
                var tokenProvider = services.GetRequiredService<VertexTokenProvider>();
                return new VertexAuthDelegatingHandler(tokenProvider) { InnerHandler = new HttpClientHandler() };
            });

        // Application-layer interfaces → Infrastructure implementations
        services.AddSingleton<IImageGenerationProvider>(sp => sp.GetRequiredService<GeminiImageGenerationProvider>());
        services.AddSingleton<GoogleCloudTtsProvider>();
        services.AddSingleton<ITtsProvider>(sp => sp.GetRequiredService<GoogleCloudTtsProvider>());
        services.AddSingleton<IMediaAlignmentEvaluator>(sp => sp.GetRequiredService<GeminiMediaAlignmentEvaluator>());
        services.AddSingleton<IMediaSafetyEvaluator>(sp => sp.GetRequiredService<GeminiMediaSafetyEvaluator>());
        services.AddSingleton<ISemanticSceneSegmentationProvider>(sp => sp.GetRequiredService<GeminiSemanticSceneSegmentationProvider>());
        services.AddSingleton<ParagraphSceneSegmentationProvider>();
        services.AddSingleton<IParagraphSceneSegmentationProvider>(sp => sp.GetRequiredService<ParagraphSceneSegmentationProvider>());
        services.AddSingleton<IMediaContextExtractor>(sp => sp.GetRequiredService<GeminiMediaContextExtractor>());
        services.AddSingleton<IAudioQualityGate, BinaryAudioQualityGate>();
        services.AddSingleton<IMediaGenerationJobFailureFinalizer, MediaGenerationJobFailureFinalizer>();
        services.AddHostedService<MediaGenerationWorker>();

        var storageOptions = new SupabaseStorageOptions();
        configuration.GetSection(SupabaseStorageOptions.SectionName).Bind(storageOptions);
        storageOptions.Url = configuration["SUPABASE_URL"] ?? storageOptions.Url;
        storageOptions.SecretKey = configuration["SUPABASE_SECRET_KEY"] ?? storageOptions.SecretKey;
        storageOptions.Bucket = configuration["SUPABASE_MEDIA_BUCKET"] ?? storageOptions.Bucket;
        services.AddSingleton(storageOptions);
        services.AddTransient<SupabaseStorageHttpClientHandler>();
        services.AddHttpClient<SupabaseMediaStorage>(client =>
                client.Timeout = TimeSpan.FromMinutes(2))
            .AddHttpMessageHandler<SupabaseStorageHttpClientHandler>();
        services.AddScoped<IMediaStorage>(provider => provider.GetRequiredService<SupabaseMediaStorage>());

        services.Configure<SePayOptions>(configuration.GetSection(SePayOptions.SectionName));
        services.AddSingleton<ISePayQrUrlBuilder, SePayQrUrlBuilder>();
        services.AddSingleton<ISePayWebhookAuthenticator, SePayWebhookAuthenticator>();
        services.Configure<PaymentExpirySweepWorkerOptions>(
            configuration.GetSection(PaymentExpirySweepWorkerOptions.SectionName));
        services.AddHostedService<PaymentExpirySweepWorker>();

        return services;
    }
}
