using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.UnitTests.Authorization;

/// <summary>
/// Tính ai được gọi từng action bằng cách chạy authorization thật cho mỗi loại principal.
/// Dùng để chứng minh việc đổi cách khai báo quyền không làm đổi quyền của ai.
/// </summary>
public static class AuthorizationMatrix
{
    public static readonly string[] Roles = ["Administrator", "ContentCreator", "CreatorApplicant", "Moderator", "User"];

    public static IServiceProvider BuildServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    public static ClaimsPrincipal Adult(string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, role), new Claim("token_version", "1")],
        "TestAuth"));

    public static readonly ClaimsPrincipal Child = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "42"), new Claim("token_type", "child"),
         new Claim(ChildSession.SessionClaimType, "session-abc")],
        "TestAuth"));

    public static async Task<SortedDictionary<string, string[]>> ComputeAsync(IServiceProvider services)
    {
        var policyProvider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var matrix = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (controller, action) in ControllerActions())
        {
            var allowed = await AllowedAsync(services, policyProvider, controller, action);
            foreach (var verb in action.GetCustomAttributes<HttpMethodAttribute>(true))
            {
                foreach (var method in verb.HttpMethods)
                {
                    var key = $"{controller.Name}.{action.Name}#{method} {verb.Template}";
                    matrix[key] = allowed;
                }
            }
        }
        return matrix;
    }

    public static async Task<bool> IsAllowedAsync(IServiceProvider services, Type controller, string actionName,
        ClaimsPrincipal principal)
    {
        var action = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .First(m => m.Name == actionName && m.GetCustomAttributes<HttpMethodAttribute>(true).Any());
        if (IsAnonymous(controller, action)) return true;
        var policy = await AuthorizationPolicy.CombineAsync(
            services.GetRequiredService<IAuthorizationPolicyProvider>(), AuthorizeData(controller, action));
        if (policy is null) return true;
        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, null, policy);
        return result.Succeeded;
    }

    private static async Task<string[]> AllowedAsync(IServiceProvider services, IAuthorizationPolicyProvider provider,
        Type controller, MethodInfo action)
    {
        if (IsAnonymous(controller, action))
            return ["anonymous", "child", .. Roles];
        var data = AuthorizeData(controller, action);
        if (data.Count == 0)
            return ["anonymous", "child", .. Roles];
        var policy = await AuthorizationPolicy.CombineAsync(provider, data);
        var allowed = new List<string>();
        using var scope = services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        if ((await authorization.AuthorizeAsync(Child, null, policy!)).Succeeded) allowed.Add("child");
        foreach (var role in Roles)
        {
            if ((await authorization.AuthorizeAsync(Adult(role), null, policy!)).Succeeded) allowed.Add(role);
        }
        return allowed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static bool IsAnonymous(Type controller, MethodInfo action) =>
        controller.IsDefined(typeof(AllowAnonymousAttribute), true) || action.IsDefined(typeof(AllowAnonymousAttribute), true);

    private static List<IAuthorizeData> AuthorizeData(Type controller, MethodInfo action) =>
        controller.GetCustomAttributes(true).OfType<IAuthorizeData>()
            .Concat(action.GetCustomAttributes(true).OfType<IAuthorizeData>())
            .ToList();

    private static IEnumerable<(Type Controller, MethodInfo Action)> ControllerActions() =>
        typeof(BaseApiController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(true).Any())
                .Select(method => (type, method)));
}
