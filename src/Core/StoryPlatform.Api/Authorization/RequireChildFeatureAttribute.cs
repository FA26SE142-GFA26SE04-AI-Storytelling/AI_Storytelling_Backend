using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;

namespace StoryPlatform.Api.Authorization;

/// <summary>
/// API của bé chỉ chạy khi phụ huynh đang bật chức năng <see cref="Feature"/> cho bé (quyền của bé).
/// Chạy sau authorization ChildSession; tắt thì trả 403 CHILD_FEATURE_DISABLED.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireChildFeatureAttribute : TypeFilterAttribute
{
    public RequireChildFeatureAttribute(string feature) : base(typeof(RequireChildFeatureFilter))
    {
        Feature = feature;
        Arguments = [feature];
    }

    public string Feature { get; }
}

public sealed class RequireChildFeatureFilter(IChildFeatureService features, string feature) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var claim = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!int.TryParse(claim, out var childProfileId))
            throw new UnauthorizedAccessException();

        if (!await features.IsEnabledAsync(childProfileId, feature, context.HttpContext.RequestAborted))
            throw new ChildFeatureDisabledException();

        await next();
    }
}
