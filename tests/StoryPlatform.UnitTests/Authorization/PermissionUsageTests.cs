using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Features.AccessControl;
using Xunit;

namespace StoryPlatform.UnitTests.Authorization;

public class PermissionUsageTests
{
    private static IEnumerable<AuthorizeAttribute> AllAuthorizeAttributes() =>
        typeof(BaseApiController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetCustomAttributes<AuthorizeAttribute>(false)
                .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>(false))));

    private static HashSet<string> UsedPermissions() =>
        AllAuthorizeAttributes().OfType<HasPermissionAttribute>().Select(a => a.Permission).ToHashSet();

    [Fact]
    public void No_controller_uses_role_lists()
    {
        Assert.DoesNotContain(AllAuthorizeAttributes(), attribute => attribute.Roles is not null);
    }

    [Fact]
    public void Every_used_permission_is_in_catalog()
    {
        Assert.All(UsedPermissions(), code => Assert.NotNull(PermissionCatalog.Find(code)));
    }

    [Fact]
    public void Every_catalog_permission_is_used()
    {
        var used = UsedPermissions();
        Assert.All(PermissionCatalog.All, p => Assert.Contains(p.Code, used));
    }
}
