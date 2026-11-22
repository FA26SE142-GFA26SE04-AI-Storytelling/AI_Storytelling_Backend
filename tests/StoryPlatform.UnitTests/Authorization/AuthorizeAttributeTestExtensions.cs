using Microsoft.AspNetCore.Authorization;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.UnitTests;

public static class AuthorizeAttributeTestExtensions
{
    /// <summary>
    /// Role được cấp bởi attribute: <c>Roles</c> nếu có, còn <c>[HasPermission]</c> thì các role giữ permission đó
    /// trong bản gán mặc định, nối bằng dấu phẩy theo thứ tự User, ContentCreator, CreatorApplicant, Moderator, Administrator.
    /// </summary>
    public static string? GrantedRoles(this AuthorizeAttribute attribute)
    {
        if (attribute.Roles is not null) return attribute.Roles;
        if (attribute is not HasPermissionAttribute permission) return null;
        var roles = RoleCodes.All.Where(role => DefaultRolePermissions.Map[role].Contains(permission.Permission));
        return string.Join(",", roles);
    }
}
