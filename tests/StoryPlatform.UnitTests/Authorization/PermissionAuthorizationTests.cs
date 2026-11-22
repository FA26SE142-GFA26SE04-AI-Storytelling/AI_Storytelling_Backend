using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Features.AccessControl;
using Xunit;

namespace StoryPlatform.UnitTests.Authorization;

public class PermissionAuthorizationTests
{
    private static readonly IServiceProvider Services =
        AuthorizationMatrix.BuildServices(services => services.AddAppAuthorization());

    private static async Task<bool> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, string permission)
    {
        var policy = await Services.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(new HasPermissionAttribute(permission).Policy!);
        using var scope = Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy!);
        return result.Succeeded;
    }

    [Theory]
    [InlineData("Moderator", Permissions.Review.GrayZone, true)]
    [InlineData("Administrator", Permissions.Review.GrayZone, true)]
    [InlineData("User", Permissions.Review.GrayZone, false)]
    [InlineData("CreatorApplicant", Permissions.Creator.ApplicationSubmit, true)]
    [InlineData("ContentCreator", Permissions.Creator.ApplicationSubmit, false)]
    public async Task Role_is_checked_against_permission(string role, string permission, bool expected)
    {
        Assert.Equal(expected, await AuthorizeAsync(AuthorizationMatrix.Adult(role), permission));
    }

    [Fact]
    public async Task Child_token_is_rejected()
    {
        Assert.False(await AuthorizeAsync(AuthorizationMatrix.Child, Permissions.Parent.StoryAuthor));
    }

    [Fact]
    public async Task Unknown_permission_is_denied()
    {
        Assert.False(await AuthorizeAsync(AuthorizationMatrix.Adult("Administrator"), "unknown.code"));
    }

    [Fact]
    public void Attribute_exposes_permission_and_policy_name()
    {
        var attribute = new HasPermissionAttribute(Permissions.Review.GrayZone);
        Assert.Equal(Permissions.Review.GrayZone, attribute.Permission);
        Assert.Equal("perm:review.gray_zone", attribute.Policy);
        Assert.Null(attribute.Roles);
    }

    [Fact]
    public async Task Other_policies_still_resolve()
    {
        var provider = Services.GetRequiredService<IAuthorizationPolicyProvider>();
        Assert.NotNull(await provider.GetPolicyAsync(ServiceExtensions.ChildSessionPolicy));
        Assert.NotNull(await provider.GetDefaultPolicyAsync());
        Assert.Null(await provider.GetPolicyAsync("does-not-exist"));
    }
}
