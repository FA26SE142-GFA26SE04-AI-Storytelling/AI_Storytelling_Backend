using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Interfaces;
using StoryPlatform.Application.Features.AIStoryInput.Services;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Application.Features.Stories.Services;
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

namespace StoryPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
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
        services.AddSingleton<IInputGuardrail, RuleBasedInputGuardrail>();

        return services;
    }
}
