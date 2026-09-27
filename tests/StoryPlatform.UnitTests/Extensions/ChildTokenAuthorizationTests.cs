using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Api.Hubs;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Extensions;

public class ChildTokenAuthorizationTests
{
    private static readonly ClaimsPrincipal ChildPrincipal = new(new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim("token_type", "child"),
            new Claim(ChildSession.SessionClaimType, "session-abc")
        }, "TestAuth"));

    private static readonly ClaimsPrincipal ParentPrincipal = new(new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "Parent"),
            new Claim("token_version", "1")
        }, "TestAuth"));

    [Fact]
    public async Task ChildToken_IsRejectedByEveryNonChildEndpoint()
    {
        var (policyProvider, authorization) = BuildAuthorization();
        var violations = new List<string>();

        foreach (var (controller, action) in ControllerActions())
        {
            if (controller.IsDefined(typeof(AllowAnonymousAttribute), true)
                || action.IsDefined(typeof(AllowAnonymousAttribute), true))
            {
                continue;
            }

            var authorizeData = controller.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(action.GetCustomAttributes<AuthorizeAttribute>(true))
                .Cast<IAuthorizeData>()
                .ToList();
            if (authorizeData.Count == 0
                || authorizeData.Any(data => data.Policy == ServiceExtensions.ChildSessionPolicy))
            {
                continue;
            }

            var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);
            var result = await authorization.AuthorizeAsync(ChildPrincipal, null, policy!);
            if (result.Succeeded)
            {
                violations.Add($"{controller.Name}.{action.Name}");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public async Task ChildToken_IsRejectedByNotificationHub()
    {
        var (policyProvider, authorization) = BuildAuthorization();
        var policy = await AuthorizationPolicy.CombineAsync(
            policyProvider, typeof(NotificationHub).GetCustomAttributes<AuthorizeAttribute>(true));

        var result = await authorization.AuthorizeAsync(ChildPrincipal, null, policy!);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DefaultPolicy_AcceptsAdultToken()
    {
        var (policyProvider, authorization) = BuildAuthorization();

        var result = await authorization.AuthorizeAsync(
            ParentPrincipal, null, await policyProvider.GetDefaultPolicyAsync());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ChildSessionPolicy_AcceptsChildAndRejectsAdult()
    {
        var (_, authorization) = BuildAuthorization();

        var child = await authorization.AuthorizeAsync(
            ChildPrincipal, null, ServiceExtensions.ChildSessionPolicy);
        var adult = await authorization.AuthorizeAsync(
            ParentPrincipal, null, ServiceExtensions.ChildSessionPolicy);

        Assert.True(child.Succeeded);
        Assert.False(adult.Succeeded);
    }

    private static (IAuthorizationPolicyProvider, IAuthorizationService) BuildAuthorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppAuthorization();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IAuthorizationPolicyProvider>(),
            provider.GetRequiredService<IAuthorizationService>());
    }

    private static IEnumerable<(Type Controller, MethodInfo Action)> ControllerActions() =>
        typeof(BaseApiController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(true).Any())
                .Select(method => (type, method)));
}
