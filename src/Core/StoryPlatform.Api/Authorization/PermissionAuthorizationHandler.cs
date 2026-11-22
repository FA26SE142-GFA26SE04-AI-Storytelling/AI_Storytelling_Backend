using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using StoryPlatform.Application.Features.AccessControl.Interfaces;

namespace StoryPlatform.Api.Authorization;

/// <summary>Đọc role trong token và hỏi <see cref="IPermissionService"/>; thiếu role thì không cấp quyền.</summary>
public sealed class PermissionAuthorizationHandler(IPermissionService permissions)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(role)) return;

        if (await permissions.RoleHasAsync(role, requirement.Permission))
            context.Succeed(requirement);
    }
}
