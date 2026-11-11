using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.TokenQuota;

public class TokenQuotaControllerAuthorizationTests
{
    [Theory]
    [InlineData(UserRole.User, true)]
    [InlineData(UserRole.Administrator, false)]
    [InlineData(UserRole.ContentCreator, false)]
    public async Task GetMyQuota_RequiresParentRole(UserRole role, bool expected)
    {
        Assert.Equal(expected, await IsAuthorizedAsync(nameof(TokenQuotaController.GetMyQuota), role));
    }

    [Theory]
    [InlineData(UserRole.Administrator, true)]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.Moderator, false)]
    public async Task AdminActions_OnlyAdministrator(UserRole role, bool expected)
    {
        foreach (var actionName in new[]
                 {
                     nameof(TokenQuotaController.Adjust),
                     nameof(TokenQuotaController.ListAdjustments),
                     nameof(TokenQuotaController.GetUserQuota)
                 })
        {
            Assert.Equal(expected, await IsAuthorizedAsync(actionName, role));
        }
    }

    [Theory]
    [InlineData(UserRole.ContentCreator, true)]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.Administrator, false)]
    public async Task GetCreatorQuota_RequiresContentCreatorRole(UserRole role, bool expected)
    {
        Assert.Equal(expected, await IsAuthorizedAsync(nameof(TokenQuotaController.GetCreatorQuota), role));
    }

    [Theory]
    [InlineData("SetConfig")]
    [InlineData("ListConfigs")]
    public void ConfigActions_AreRemoved(string actionName)
    {
        Assert.Null(typeof(TokenQuotaController).GetMethod(actionName));
    }

    [Theory]
    [InlineData(UserRole.Administrator, true)]
    [InlineData(UserRole.User, true)]
    public async Task GetChildStatus_AllowsAnyAuthenticatedRole(UserRole role, bool expected)
    {
        Assert.Equal(expected, await IsAuthorizedAsync(nameof(TokenQuotaController.GetChildStatus), role));
    }

    private static async Task<bool> IsAuthorizedAsync(string actionName, UserRole role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, role.ToString()) }, "Bearer"));
        var method = typeof(TokenQuotaController).GetMethod(actionName)!;
        var attributes = typeof(TokenQuotaController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<IAuthorizeData>()
            .Concat(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<IAuthorizeData>());
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, attributes);

        Assert.NotNull(policy);
        return (await authorization.AuthorizeAsync(user, null, policy!)).Succeeded;
    }
}
