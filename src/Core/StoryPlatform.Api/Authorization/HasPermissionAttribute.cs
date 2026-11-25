using Microsoft.AspNetCore.Authorization;

namespace StoryPlatform.Api.Authorization;

/// <summary>Endpoint yêu cầu một permission; role nào có permission đó do bảng phân quyền quyết định.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission)
    {
        Permission = permission;
        Policy = PermissionPolicyProvider.Prefix + permission;
    }

    public string Permission { get; }
}
