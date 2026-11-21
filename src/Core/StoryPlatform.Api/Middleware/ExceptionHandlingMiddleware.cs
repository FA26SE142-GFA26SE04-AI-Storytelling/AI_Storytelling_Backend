using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;

namespace StoryPlatform.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Request aborted by caller. TraceId={TraceId}", System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.RequestAborted.IsCancellationRequested || context.Response.HasStarted) return;
        context.Response.ContentType = "application/json";

        int statusCode;
        string message;

        switch (exception)
        {
            case AppException appEx:
                statusCode = appEx.StatusCode;
                // Log giữ mã gốc; người dùng nhận câu tiếng Việt thân thiện.
                _logger.LogWarning(appEx, "Ngoại lệ nghiệp vụ [{StatusCode}]: {Message}", statusCode, appEx.Message);
                message = UserFacingMessages.Translate(appEx.Message);
                break;

            case UnauthorizedAccessException:
                statusCode = (int)HttpStatusCode.Unauthorized;
                message = "Phiên làm việc không hợp lệ hoặc đã hết hạn.";
                _logger.LogWarning(exception, "Lỗi phân quyền 401: {Message}", message);
                break;

            default:
                statusCode = (int)HttpStatusCode.InternalServerError;
                message = "Đã xảy ra lỗi nội bộ trên máy chủ. Vui lòng thử lại sau.";
                _logger.LogError(exception, "Lỗi không xác định 500: {Message}", exception.Message);
                break;
        }

        context.Response.StatusCode = statusCode;

        List<string>? errors = exception switch
        {
            StoryPlatform.Application.Features.CreatorAssistance.AssistanceException assistance => [assistance.Code],
            AppException { ErrorCode: not null } appExWithCode => [appExWithCode.ErrorCode],
            _ => null
        };
        var response = ApiResponse<object>.Fail(message, errors);
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(response, jsonOptions);

        await context.Response.WriteAsync(json);
    }
}
