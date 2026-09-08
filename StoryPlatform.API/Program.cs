using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StoryPlatform.API.Extensions;
using StoryPlatform.API.Middlewares;
using StoryPlatform.BLL;
using StoryPlatform.DAL;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Controllers và JSON options
builder.Services.AddControllers();

// 2. Cấu hình Swagger với JWT Bearer
builder.Services.AddSwaggerWithJwt();

// 3. Cấu hình CORS
builder.Services.AddCorsPolicy();

// 4. Cấu hình Authentication & JWT Token
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// 5. Đăng ký các tầng theo kiến trúc (thông qua tham chiếu bắc cầu: API -> BLL -> DAL)
// 5.1. Đăng ký Data Access Layer (DAL) - DbContext, Generic Repository, Unit of Work
builder.Services.AddDataAccessLayer(builder.Configuration);

// 5.2. Đăng ký Business Logic Layer (BLL) - Modules (Auth, Story...) & Security
builder.Services.AddBusinessLogicLayer(builder.Configuration);

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
