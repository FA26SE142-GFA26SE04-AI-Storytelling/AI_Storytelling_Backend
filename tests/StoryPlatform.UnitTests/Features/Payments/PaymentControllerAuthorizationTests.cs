using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Payments;

public class PaymentControllerAuthorizationTests
{
    [Theory]
    [InlineData(UserRole.Parent, true)]
    [InlineData(UserRole.Teacher, true)]
    [InlineData(UserRole.Administrator, true)]
    public async Task AuthenticatedActions_AnyRole(UserRole role, bool expected)
    {
        foreach (var actionName in new[]
                 {
                     nameof(PaymentController.ListActivePlans),
                     nameof(PaymentController.CreateTransaction)
                 })
        {
            Assert.Equal(expected, await IsAuthorizedAsync(actionName, role, authenticated: true));
        }
    }

    [Fact]
    public async Task AuthenticatedActions_Unauthenticated_Denied()
    {
        foreach (var actionName in new[]
                 {
                     nameof(PaymentController.ListActivePlans),
                     nameof(PaymentController.CreateTransaction)
                 })
        {
            Assert.False(await IsAuthorizedAsync(actionName, UserRole.Parent, authenticated: false));
        }
    }

    [Theory]
    [InlineData(UserRole.Administrator, true)]
    [InlineData(UserRole.Parent, false)]
    [InlineData(UserRole.Teacher, false)]
    public async Task AdminActions_OnlyAdministrator(UserRole role, bool expected)
    {
        foreach (var actionName in new[]
                 {
                     nameof(PaymentController.ListMismatched),
                     nameof(PaymentController.MarkPaidManually)
                 })
        {
            Assert.Equal(expected, await IsAuthorizedAsync(actionName, role, authenticated: true));
        }
    }

    [Fact]
    public void SePayWebhook_AllowsAnonymous()
    {
        var method = typeof(PaymentController).GetMethod(nameof(PaymentController.SePayWebhook))!;
        Assert.NotEmpty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    private static async Task<bool> IsAuthorizedAsync(string actionName, UserRole role, bool authenticated)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var identity = authenticated
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role.ToString()) }, "Bearer")
            : new ClaimsIdentity();
        var user = new ClaimsPrincipal(identity);
        var method = typeof(PaymentController).GetMethod(actionName)!;
        var attributes = typeof(PaymentController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<IAuthorizeData>()
            .Concat(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<IAuthorizeData>());
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, attributes);

        Assert.NotNull(policy);
        return (await authorization.AuthorizeAsync(user, null, policy!)).Succeeded;
    }
}
