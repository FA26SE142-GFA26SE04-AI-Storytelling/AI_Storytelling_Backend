using StoryPlatform.Api.Extensions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentCategories;

public class ContentCategoryControllerAuthorizationTests
{
    [Theory]
    [InlineData(UserRole.User, true)]
    [InlineData(UserRole.Moderator, true)]
    [InlineData(UserRole.Administrator, true)]
    public async Task GetActions_AllowAnyAuthenticatedRole(UserRole role, bool expected)
    {
        foreach (var actionName in new[]
                 {
                     nameof(ContentCategoryController.GetContentCategory),
                     nameof(ContentCategoryController.ListContentCategories)
                 })
        {
            Assert.Equal(expected, await IsAuthorizedAsync(actionName, role));
        }
    }

    [Theory]
    [InlineData(UserRole.Moderator, true)]
    [InlineData(UserRole.Administrator, false)]
    [InlineData(UserRole.User, false)]
    public async Task WriteActions_RequireModeratorOnly(UserRole role, bool expected)
    {
        foreach (var actionName in new[]
                 {
                     nameof(ContentCategoryController.CreateContentCategory),
                     nameof(ContentCategoryController.UpdateContentCategory),
                     nameof(ContentCategoryController.DeleteContentCategory)
                 })
        {
            Assert.Equal(expected, await IsAuthorizedAsync(actionName, role));
        }
    }

    private static async Task<bool> IsAuthorizedAsync(string actionName, UserRole role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppAuthorization();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, role.ToString()) }, "Bearer"));
        var method = typeof(ContentCategoryController).GetMethod(actionName)!;
        var attributes = typeof(ContentCategoryController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<IAuthorizeData>()
            .Concat(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<IAuthorizeData>());
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, attributes);

        Assert.NotNull(policy);
        return (await authorization.AuthorizeAsync(user, null, policy!)).Succeeded;
    }
}
