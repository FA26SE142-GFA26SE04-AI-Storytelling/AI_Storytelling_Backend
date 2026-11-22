using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using StoryPlatform.Api.Extensions;

namespace StoryPlatform.Api.Authorization;

/// <summary>
/// Dựng policy "perm:&lt;mã&gt;" theo yêu cầu: người lớn đã đăng nhập (không phải token trẻ) và có permission.
/// Mọi policy khác (DefaultPolicy, ChildSession) giữ nguyên qua provider mặc định.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    public const string Prefix = "perm:";

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.HasClaim("token_type", ServiceExtensions.ChildTokenType))
            .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
