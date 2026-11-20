using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StoryPlatform.AI.Api.Diagnostics;

/// <summary>Observes bound DTOs without consuming or buffering HTTP request/response streams.</summary>
public sealed class AiRequestLoggingFilter : IAsyncActionFilter, IOrderedFilter
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AiRequestLoggingFilter> _logger;

    public AiRequestLoggingFilter(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<AiRequestLoggingFilter> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public int Order => -3000;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var trace = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        var limit = Math.Clamp(_configuration.GetValue("AI:Diagnostics:MaxPayloadCharacters", 8192), 256, 32768);
        var arguments = context.ActionArguments.Where(x => x.Value is not CancellationToken)
            .ToDictionary(x => x.Key, x => x.Value);
        var requestId = arguments.Values.Select(value => value?.GetType().GetProperty("RequestId")?.GetValue(value))
            .OfType<string>().FirstOrDefault();
        var safeRequestId = SafePreview(() => AiPayloadPreview.Format(requestId, 256));
        using var scope = _logger.BeginScope(new Dictionary<string, object>
            { ["AiTraceId"] = trace, ["AiRequestId"] = safeRequestId });
        var payloads = _environment.IsDevelopment() && _configuration.GetValue("AI:Diagnostics:LogPayloads", false);
        var timer = Stopwatch.StartNew();
        _logger.LogInformation("AI request received. Method={Method} Path={Path} TraceId={TraceId} RequestId={RequestId}",
            context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value, trace, safeRequestId);
        if (payloads)
        {
            _logger.LogInformation("AI request payload (allowlisted, redacted, bounded). TraceId={TraceId} Payload={Payload}",
                trace, SafePreview(() => AiPayloadPreview.FormatAllowlisted(arguments, limit)));
        }
        ActionExecutedContext executed;
        try { executed = await next(); }
        catch (Exception exception)
        {
            _logger.LogWarning("AI action interrupted. TraceId={TraceId} ElapsedMs={ElapsedMs} ErrorType={ErrorType}",
                trace, timer.ElapsedMilliseconds, exception.GetType().Name);
            throw;
        }
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            _logger.LogWarning("AI action failed; response will be mapped by exception middleware. TraceId={TraceId} ElapsedMs={ElapsedMs} ErrorType={ErrorType}",
                trace, timer.ElapsedMilliseconds, executed.Exception.GetType().Name);
            return;
        }
        var result = executed.Result as ObjectResult;
        var status = result?.StatusCode ?? (executed.Result as StatusCodeResult)?.StatusCode ?? context.HttpContext.Response.StatusCode;
        _logger.LogInformation("AI action response. TraceId={TraceId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
            trace, status, timer.ElapsedMilliseconds);
        if (payloads && result is not null)
        {
            _logger.LogInformation("AI response payload (allowlisted, redacted, bounded). TraceId={TraceId} Payload={Payload}",
                trace, SafePreview(() => AiPayloadPreview.FormatAllowlisted(result.Value, limit)));
        }
    }

    private static string SafePreview(Func<string> format)
    {
        try { return format(); }
        catch { return "[PAYLOAD_UNAVAILABLE]"; }
    }
}
