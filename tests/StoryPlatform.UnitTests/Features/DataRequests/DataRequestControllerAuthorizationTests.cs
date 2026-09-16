using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.DataRequests;

public class DataRequestControllerAuthorizationTests
{
    [Theory]
    [InlineData(UserRole.Parent, true)]
    [InlineData(UserRole.Teacher, false)]
    [InlineData(UserRole.Administrator, false)]
    public async Task CreateDataRequest_OnlyParent(UserRole role, bool expected)
    {
        Assert.Equal(expected, await IsAuthorizedAsync(nameof(DataRequestController.CreateDataRequest), role));
    }

    [Theory]
    [InlineData(UserRole.Administrator, true)]
    [InlineData(UserRole.Parent, false)]
    [InlineData(UserRole.Teacher, false)]
    public async Task AdminActions_OnlyAdministrator(UserRole role, bool expected)
    {
        foreach (var actionName in new[]
                 {
                     nameof(DataRequestController.ListPendingDataRequests),
                     nameof(DataRequestController.ResolveExport),
                     nameof(DataRequestController.ResolveDelete)
                 })
        {
            Assert.Equal(expected, await IsAuthorizedAsync(actionName, role));
        }
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
        var method = typeof(DataRequestController).GetMethod(actionName)!;
        var attributes = typeof(DataRequestController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<IAuthorizeData>()
            .Concat(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<IAuthorizeData>());
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, attributes);

        Assert.NotNull(policy);
        return (await authorization.AuthorizeAsync(user, null, policy!)).Succeeded;
    }
}
