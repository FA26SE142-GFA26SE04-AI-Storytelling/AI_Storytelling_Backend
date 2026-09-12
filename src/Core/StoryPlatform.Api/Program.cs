using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Api.Middleware;
using StoryPlatform.Application;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Controllers và JSON options
builder.Services.AddControllers();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Dữ liệu đầu vào không hợp lệ." : error.ErrorMessage)
            .ToList();
        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
            ApiResponse<object>.Fail("Dữ liệu đầu vào không hợp lệ.", errors));
    };
});

// 2. Cấu hình Swagger với JWT Bearer
builder.Services.AddSwaggerWithJwt();

// 3. Cấu hình CORS
builder.Services.AddCorsPolicy();

// 4. Cấu hình Authentication & JWT Token
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// 5. Đăng ký Application use cases và Infrastructure adapters
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Pipeline xử lý HTTP Request
// Bắt ngoại lệ tập trung toàn ứng dụng
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger UI
if (app.Environment.IsDevelopment() || true)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "StoryPlatform API v1");
        c.RoutePrefix = string.Empty; // Hiển thị Swagger ngay tại trang gốc "/"
    });
}

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
