using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Interfaces;
using StoryPlatform.Application.Features.AIStoryInput.Services;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Application.Features.Stories.Services;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Application.Features.Outline.Guardrails;
using StoryPlatform.Application.Features.Outline.Interfaces;
using StoryPlatform.Application.Features.Outline.Services;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ContentGeneration.Services;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Services;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.Services;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Services;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Services;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;
using StoryPlatform.Application.Features.ContentCategories.Interfaces;
using StoryPlatform.Application.Features.ContentCategories.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Notifications.Services;
using StoryPlatform.Application.Features.Organizations.Interfaces;
using StoryPlatform.Application.Features.Organizations.Services;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.Services;
using StoryPlatform.Application.Features.Administration.Interfaces;
using StoryPlatform.Application.Features.Administration.Services;
using StoryPlatform.Application.Features.DataRequests.Interfaces;
using StoryPlatform.Application.Features.DataRequests.Services;
using StoryPlatform.Application.Features.BusinessReports.Interfaces;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Application.Features.Payments.Services;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;

namespace StoryPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IStoryService, StoryService>();
        services.AddScoped<IAIStoryInputService, AIStoryInputService>();
        services.AddScoped<IChildProfileService, ChildProfileService>();
        services.AddScoped<ISupervisionAccessGuard, SupervisionAccessGuard>();
        services.AddScoped<ILearningProfileService, LearningProfileService>();
        services.AddScoped<ISafetyPolicyService, SafetyPolicyService>();
        services.AddScoped<ISupervisionService, SupervisionService>();
        services.AddScoped<IClassGroupService, ClassGroupService>();
        services.AddScoped<IChildAccessCredentialService, ChildAccessCredentialService>();
        services.AddScoped<IContentCategoryService, ContentCategoryService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddScoped<IDataRequestService, DataRequestService>();
        services.AddScoped<IBusinessReportService, BusinessReportService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ITokenQuotaService, TokenQuotaService>();
        services.AddScoped<IPaymentExpirySweepService, PaymentExpirySweepService>();
        services.AddSingleton<IInputGuardrail, RuleBasedInputGuardrail>();
        services.AddScoped<OutlineService>();
        services.AddScoped<IOutlineService>(provider => provider.GetRequiredService<OutlineService>());
        services.AddScoped<IOutlineJobProcessor>(provider => provider.GetRequiredService<OutlineService>());
        services.AddSingleton<IOutlineReviewGuardrail, RuleBasedOutlineReviewGuardrail>();
        services.AddScoped<ContentGenerationService>();
        services.AddScoped<IContentGenerationService>(provider => provider.GetRequiredService<ContentGenerationService>());
        services.AddScoped<IContentGenerationJobProcessor>(provider => provider.GetRequiredService<ContentGenerationService>());
        services.AddSingleton<IContentQualityEvaluator, RuleBasedContentQualityEvaluator>();

        // Story Review
        services.AddSingleton<IProposalCache, InMemoryProposalCache>();
        services.AddScoped<IStoryReviewService, StoryReviewService>();
        services.AddScoped<IProposalService, ProposalService>();

        return services;
    }
}
