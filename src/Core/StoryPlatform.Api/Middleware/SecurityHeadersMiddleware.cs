namespace StoryPlatform.Api.Middleware;

/// <summary>
/// Security headers cơ bản cho mọi response, kể cả lỗi và 429 (DF06). Gắn qua OnStarting để
/// middleware phía sau có xóa/ghi lại response thì header vẫn còn.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });

        return next(context);
    }
}
