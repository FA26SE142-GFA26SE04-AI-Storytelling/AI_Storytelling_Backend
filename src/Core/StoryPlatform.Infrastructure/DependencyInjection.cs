using StoryPlatform.Application.Features.Moderation;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Abstractions.Communication;
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
using StoryPlatform.Infrastructure.Payments;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using StoryPlatform.Infrastructure.Security;
using StoryPlatform.Infrastructure.Storage;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.CreatorAuthoring;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDataProtection();
        services.AddOptions<StoryPlatform.Application.Features.CreatorAssistance.CreatorAssistanceOptions>()
            .Bind(configuration.GetSection(StoryPlatform.Application.Features.CreatorAssistance.CreatorAssistanceOptions.SectionName))
            .Validate(x => x.HasValidLimits(),
                "Creator assistance limits are invalid. Lease must include the 10-second completion budget and 5-second safety margin after timeout.").ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StoryPlatform.Application.Features.CreatorAssistance.CreatorAssistanceOptions>>().Value);
        services.AddScoped<StoryPlatform.Application.Features.CreatorAuthoring.Interfaces.IImportPreviewProtector,
            StoryPlatform.Infrastructure.Security.ImportPreviewProtector>();
        services.AddOptions<CreatorAuthoringOptions>()
            .Bind(configuration.GetSection(CreatorAuthoringOptions.SectionName))
            .Validate(x => x.MaximumContentCharacters is > 0 and <= 200_000 &&
                           x.MaximumImportBytes is > 0 and <= 5_000_000 &&
                           x.ImportPreviewLifetimeHours is > 0 and <= 168 &&
                           x.SupportedLanguages is { Length: > 0 } && x.SupportedAgeBands is { Length: > 0 },
                "Creator authoring limits must remain within the import safety limits.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CreatorAuthoringOptions>>().Value);
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
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClientAttemptLimiter, InMemoryClientAttemptLimiter>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        services.AddHttpClient<IEmailSender, ResendEmailSender>();

        services.Configure<AIServiceOptions>(options =>
        {
            configuration.GetSection(AIServiceOptions.SectionName).Bind(options);
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = configuration["GEMINI_API_KEY"]
                              ?? configuration["AI:Gemini:ApiKey"]
                              ?? configuration["AI__Gemini__ApiKey"]
                              ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                              ?? string.Empty;
            }
        });
        services.AddHttpClient<GeminiDirectClient>();
        services.AddHttpClient<IAIStoryGenerationClient, AIStoryGenerationClient>();
        services.AddHttpClient<IModelModerationProvider, VertexGrayZoneModerationProvider>();
        services.AddHttpClient<StoryPlatform.Application.Features.CreatorAssistance.Interfaces.ICreatorAssistanceAiClient, CreatorAssistanceAiClient>();
        services.Configure<OutlineWorkerOptions>(configuration.GetSection(OutlineWorkerOptions.SectionName));
        services.AddSingleton<IOutlineJobFailureFinalizer, OutlineJobFailureFinalizer>();
        services.AddHostedService<OutlineGenerationWorker>();
        services.Configure<ContentGenerationWorkerOptions>(configuration.GetSection(ContentGenerationWorkerOptions.SectionName));
        var grayZoneOptions = new GrayZoneOptions();
        configuration.GetSection(GrayZoneOptions.SectionName).Bind(grayZoneOptions);
        services.AddSingleton(grayZoneOptions);
        var contentOptions = new ContentGenerationOptions();
        var learningOptions = new LearningBatchOptions();
        configuration.GetSection(LearningBatchOptions.SectionName).Bind(learningOptions);
        services.AddSingleton(learningOptions);
        services.AddSingleton<StoryPlatform.Application.Features.ContentGeneration.Interfaces.ILearningBatchRunner, LearningBatchRunner>();
        services.AddSingleton<StoryPlatform.Application.Features.ContentGeneration.Interfaces.ILearningRequestGate, LearningRequestGate>();
        services.AddScoped<StoryPlatform.Application.Features.ContentGeneration.Interfaces.ILearningBatchProcessor, StoryPlatform.Application.Features.ContentGeneration.Services.LearningBatchProcessor>();
        services.AddScoped<StoryPlatform.Application.Features.ContentGeneration.Interfaces.ILearningBatchService, StoryPlatform.Application.Features.ContentGeneration.Services.LearningBatchService>();
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
        var geminiOptions = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new();
        var imageOptions = configuration.GetSection(ImageGenerationOptions.SectionName).Get<ImageGenerationOptions>() ?? new();
        var ttsOptions = configuration.GetSection(TtsServiceOptions.SectionName).Get<TtsServiceOptions>() ?? new();
        var evaluationOptions = configuration.GetSection(MediaEvaluationOptions.SectionName).Get<MediaEvaluationOptions>() ?? new();
        services.AddOptions<VertexOptions>()
            .Bind(configuration.GetSection(VertexOptions.SectionName))
            .Validate(
                options => !options.UseVertex ||
                           (!string.IsNullOrWhiteSpace(options.ProjectId) && !string.IsNullOrWhiteSpace(options.Location)),
                "AI:Vertex requires ProjectId and Location when UseVertex=true.")
            .ValidateOnStart();

        // Vertex AI token provider (singleton, shared across all Vertex-authenticated HttpClients)
        services.AddSingleton<VertexTokenProvider>();
        services.AddTransient<VertexAuthDelegatingHandler>();
        services.AddTransient<MediaCircuitOpenDelegatingHandler>();
        services.AddSingleton<MediaRequestRateLimiter>();
        services.AddTransient<MediaRateLimitDelegatingHandler>();

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
            if (!string.IsNullOrWhiteSpace(vertexOpts.ProjectId))
                builder.QuotaProject = vertexOpts.ProjectId;
            return builder.Build();
        });

        // Gemini REST providers — registered via HttpClient factory.
        // When Vertex AI is enabled, the VertexAuthDelegatingHandler injects a fresh
        // OAuth2 Bearer token on every request; otherwise x-goog-api-key is used per-provider.
        ConfigureMediaHttpClient(
            services.AddHttpClient<GeminiImageGenerationProvider>(client => client.Timeout = TimeSpan.FromMinutes(2)),
            new MediaGenerationOptions { CircuitBreaker = mediaOptions.Images.CircuitBreaker }, 0, imageOptions.TransportRetryBaseDelayMs);
        ConfigureMediaHttpClient(
            services.AddHttpClient<GeminiTtsProvider>(client => client.Timeout = TimeSpan.FromMinutes(2)),
            new MediaGenerationOptions { CircuitBreaker = mediaOptions.Audio.CircuitBreaker }, 0, ttsOptions.TransportRetryBaseDelayMs);
        ConfigureMediaHttpClient(
            services.AddHttpClient<GeminiMediaAlignmentEvaluator>(client => client.Timeout = TimeSpan.FromMinutes(2)),
            mediaOptions, evaluationOptions.TransportRetryCount, evaluationOptions.TransportRetryBaseDelayMs);
        ConfigureMediaHttpClient(
            services.AddHttpClient<GeminiMediaSafetyEvaluator>(client => client.Timeout = TimeSpan.FromMinutes(2)),
            mediaOptions, evaluationOptions.TransportRetryCount, evaluationOptions.TransportRetryBaseDelayMs);
        ConfigureMediaHttpClient(
            services.AddHttpClient<GeminiSemanticSceneSegmentationProvider>(client => client.Timeout = TimeSpan.FromMinutes(2)),
            mediaOptions, geminiOptions.TransportRetryCount, geminiOptions.TransportRetryBaseDelayMs);
        ConfigureMediaHttpClient(
            services.AddHttpClient<GeminiIllustrationBeatPlanner>(client => client.Timeout = TimeSpan.FromMinutes(2)),
            mediaOptions, geminiOptions.TransportRetryCount, geminiOptions.TransportRetryBaseDelayMs);
        ConfigureMediaHttpClient(
            services.AddHttpClient<GeminiMediaContextExtractor>(client => client.Timeout = TimeSpan.FromMinutes(2)),
            mediaOptions, geminiOptions.TransportRetryCount, geminiOptions.TransportRetryBaseDelayMs);

        // Application-layer interfaces → Infrastructure implementations
        services.AddSingleton<IImageGenerationProvider>(sp => new GatedImageProvider(sp.GetRequiredService<GeminiImageGenerationProvider>(),
            new MediaBranchProviderGate(mediaOptions.Images, sp.GetRequiredService<ILoggerFactory>().CreateLogger("MediaProviderTiming"), "Image")));
        services.AddSingleton<GoogleCloudTtsProvider>();
        services.AddSingleton<ITtsProvider>(sp => new GatedTtsProvider(ttsOptions.Provider.Trim().ToUpperInvariant() switch
        {
            "GEMINI" or "GEMINITTS" => sp.GetRequiredService<GeminiTtsProvider>(),
            "GOOGLECLOUD" or "GOOGLECLOUDTTS" => sp.GetRequiredService<GoogleCloudTtsProvider>(),
            _ => throw new InvalidOperationException(
                $"Unsupported AI:TTS:Provider '{ttsOptions.Provider}'. Supported values: GeminiTTS, GoogleCloudTTS.")
        }, new MediaBranchProviderGate(mediaOptions.Audio, sp.GetRequiredService<ILoggerFactory>().CreateLogger("MediaProviderTiming"), "Audio")));
        services.AddOptions<SpeechToTextOptions>()
            .Bind(configuration.GetSection(SpeechToTextOptions.SectionName))
            .PostConfigure<Microsoft.Extensions.Options.IOptions<VertexOptions>>((stt, vertex) =>
            {
                if (string.IsNullOrWhiteSpace(stt.ProjectId)) stt.ProjectId = vertex.Value.ProjectId;
            });
        services.AddTransient<SpeechToTextAuthHandler>();
        services.AddHttpClient<GoogleSpeechToTextRecognizer>(client => client.Timeout = TimeSpan.FromSeconds(60))
            .AddHttpMessageHandler<SpeechToTextAuthHandler>();
        services.AddScoped<ISpeechRecognizer>(sp => sp.GetRequiredService<GoogleSpeechToTextRecognizer>());
        services.AddSingleton<IMediaAlignmentEvaluator>(sp => sp.GetRequiredService<GeminiMediaAlignmentEvaluator>());
        services.AddSingleton<IMediaSafetyEvaluator>(sp => sp.GetRequiredService<GeminiMediaSafetyEvaluator>());
        services.AddSingleton<ISemanticSceneSegmentationProvider>(sp => sp.GetRequiredService<GeminiSemanticSceneSegmentationProvider>());
        services.AddSingleton<IIllustrationBeatPlanner>(sp => sp.GetRequiredService<GeminiIllustrationBeatPlanner>());
        services.AddSingleton<ParagraphSceneSegmentationProvider>();
        services.AddSingleton<IParagraphSceneSegmentationProvider>(sp => sp.GetRequiredService<ParagraphSceneSegmentationProvider>());
        services.AddSingleton<IMediaContextExtractor>(sp => sp.GetRequiredService<GeminiMediaContextExtractor>());
        services.AddSingleton<IAudioQualityGate, BinaryAudioQualityGate>();
        services.AddSingleton<IMediaGenerationJobFailureFinalizer, MediaGenerationJobFailureFinalizer>();
        services.AddHostedService<MediaGenerationWorker>();
        services.AddHostedService<ImageGenerationWorker>();
        services.AddHostedService<TtsGenerationWorker>();

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
        services.Configure<NotificationRetentionWorkerOptions>(
            configuration.GetSection(NotificationRetentionWorkerOptions.SectionName));
        services.AddHostedService<NotificationRetentionWorker>();
        services.Configure<ModerationSweepWorkerOptions>(
            configuration.GetSection(ModerationSweepWorkerOptions.SectionName));
        services.AddHostedService<ModerationSweepWorker>();
        services.Configure<StoryWishSweepWorkerOptions>(
            configuration.GetSection(StoryWishSweepWorkerOptions.SectionName));
        services.AddHostedService<StoryWishSweepWorker>();

        return services;
    }

    private static void ConfigureMediaHttpClient(
        IHttpClientBuilder client,
        MediaGenerationOptions options,
        int retryCount,
        int retryBaseDelayMilliseconds)
    {
        client.AddHttpMessageHandler<MediaCircuitOpenDelegatingHandler>();
        client.AddMediaResiliencePipeline(options, retryCount, retryBaseDelayMilliseconds);
        client.AddHttpMessageHandler<VertexAuthDelegatingHandler>();
        client.AddHttpMessageHandler<MediaRateLimitDelegatingHandler>();
    }
}
