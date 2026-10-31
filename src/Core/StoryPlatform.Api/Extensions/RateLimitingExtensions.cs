using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using StoryPlatform.Application.Common.Models;

namespace StoryPlatform.Api.Extensions;

public static class RateLimitPolicies
{
    /// <summary>Đăng nhập, quên/đặt lại mật khẩu, xác thực và gửi lại OTP/lời mời: giới hạn theo IP và theo endpoint.</summary>
    public const string Auth = "auth";

    /// <summary>Endpoint kích hoạt gọi AI: giới hạn theo tài khoản (chưa xác thực thì theo IP).</summary>
    public const string Ai = "ai";
}

public static class RateLimitingExtensions
{
    public const string RejectedMessage = "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.";

    /// <summary>Rate limiter của ASP.NET Core cho DF06; hạn mức đọc từ "RateLimiting:Auth|Ai:PermitLimit|WindowSeconds".</summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = ReadWindow(configuration, "RateLimiting:Auth", defaultPermitLimit: 10, defaultWindowSeconds: 60);
        var ai = ReadWindow(configuration, "RateLimiting:Ai", defaultPermitLimit: 20, defaultWindowSeconds: 60);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse<object>.Fail(RejectedMessage), cancellationToken);
            };
            options.AddPolicy(RateLimitPolicies.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                $"{ClientIp(http)}|{http.Request.Path.Value?.ToLowerInvariant()}", _ => auth));
            options.AddPolicy(RateLimitPolicies.Ai, http =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    userId is null ? $"ip:{ClientIp(http)}" : $"user:{userId}", _ => ai);
            });
        });

        return services;
    }

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions ReadWindow(
        IConfiguration configuration, string section, int defaultPermitLimit, int defaultWindowSeconds)
    {
        var permitLimit = configuration.GetValue($"{section}:PermitLimit", defaultPermitLimit);
        var windowSeconds = configuration.GetValue($"{section}:WindowSeconds", defaultWindowSeconds);
        if (permitLimit <= 0 || windowSeconds <= 0)
        {
            throw new InvalidOperationException($"'{section}:PermitLimit' và '{section}:WindowSeconds' phải là số nguyên dương.");
        }

        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        };
    }
}
