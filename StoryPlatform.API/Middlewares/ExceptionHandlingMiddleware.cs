using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StoryPlatform.BLL.Common.Exceptions;
using StoryPlatform.BLL.Common.Models;

namespace StoryPlatform.API.Middlewares;

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
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        int statusCode;
        string message;

        switch (exception)
        {
            case AppException appEx:
                statusCode = appEx.StatusCode;
                message = appEx.Message;
                _logger.LogWarning(appEx, "Ngoại lệ nghiệp vụ [{StatusCode}]: {Message}", statusCode, message);
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

        var response = ApiResponse<object>.Fail(message);
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(response, jsonOptions);

        await context.Response.WriteAsync(json);
    }
}
