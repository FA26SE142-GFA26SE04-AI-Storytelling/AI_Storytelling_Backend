using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StoryPlatform.AI.Api.Diagnostics;

/// <summary>Observes bound DTOs without consuming or buffering HTTP request/response streams.</summary>
public sealed class AiRequestLoggingFilter(IConfiguration configuration, IWebHostEnvironment environment,
    ILogger<AiRequestLoggingFilter> logger) : IAsyncActionFilter, IOrderedFilter
{
    public int Order => -3000;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var trace = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        var limit = Math.Clamp(configuration.GetValue("AI:Diagnostics:MaxPayloadCharacters", 8192), 256, 32768);
        var secrets = configuration.AsEnumerable().Where(x => x.Value is { Length: >= 4 } &&
                System.Text.RegularExpressions.Regex.IsMatch(x.Key, "password|secret|token|credential|key|connectionstring",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Select(x => x.Value!).Distinct().ToArray();
        var arguments = context.ActionArguments.Where(x => x.Value is not CancellationToken)
            .ToDictionary(x => x.Key, x => x.Value);
        var requestId = arguments.Values.Select(value => value?.GetType().GetProperty("RequestId")?.GetValue(value))
            .OfType<string>().FirstOrDefault();
        var safeRequestId = AiPayloadPreview.Format(requestId, 256, secrets);
        using var scope = logger.BeginScope(new Dictionary<string, object>
            { ["AiTraceId"] = trace, ["AiRequestId"] = safeRequestId });
        var payloads = environment.IsDevelopment() && configuration.GetValue("AI:Diagnostics:LogPayloads", false);
        var timer = Stopwatch.StartNew();
        logger.LogInformation("AI request received. Method={Method} Path={Path} TraceId={TraceId} RequestId={RequestId}",
            context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value, trace, safeRequestId);
        if (payloads) logger.LogInformation("AI request payload (redacted, bounded). TraceId={TraceId} Payload={Payload}",
            trace, AiPayloadPreview.Format(arguments, limit, secrets));
        ActionExecutedContext executed;
        try { executed = await next(); }
        catch (Exception exception)
        {
            logger.LogWarning("AI action interrupted. TraceId={TraceId} ElapsedMs={ElapsedMs} ErrorType={ErrorType}",
                trace, timer.ElapsedMilliseconds, exception.GetType().Name);
            throw;
        }
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            logger.LogWarning("AI action failed; response will be mapped by exception middleware. TraceId={TraceId} ElapsedMs={ElapsedMs} ErrorType={ErrorType}",
                trace, timer.ElapsedMilliseconds, executed.Exception.GetType().Name);
            return;
        }
        var result = executed.Result as ObjectResult;
        var status = result?.StatusCode ?? (executed.Result as StatusCodeResult)?.StatusCode ?? context.HttpContext.Response.StatusCode;
        logger.LogInformation("AI action response. TraceId={TraceId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
            trace, status, timer.ElapsedMilliseconds);
        if (payloads && result is not null)
            logger.LogInformation("AI response payload (redacted, bounded). TraceId={TraceId} Payload={Payload}",
                trace, AiPayloadPreview.Format(result.Value, limit, secrets));
    }
}
