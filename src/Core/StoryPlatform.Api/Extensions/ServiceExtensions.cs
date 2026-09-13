using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StoryPlatform.Infrastructure.Security;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using System.Security.Claims;

namespace StoryPlatform.Api.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var key = Encoding.UTF8.GetBytes(jwtOptions.SecretKey);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var versionClaim = context.Principal?.FindFirst("token_version")?.Value;
                    var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (!int.TryParse(versionClaim, out var version) || !int.TryParse(userIdClaim, out var userId))
                    {
                        context.Fail("Token không hợp lệ.");
                        return;
                    }

                    var unitOfWork = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();
                    // Không tracking ở middleware: service có thể đọc và cập nhật lại tài khoản này.
                    var user = await unitOfWork.Repository<UserAccount>()
                        .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken: context.HttpContext.RequestAborted);
                    if (user == null || user.IsDeleted || user.TokenVersion != version
                        || user.Status == AccountStatus.Suspended || user.Status == AccountStatus.Registered
                        || context.Principal?.FindFirst(ClaimTypes.Role)?.Value != user.Role.ToString())
                    {
                        context.Fail("Token đã bị thu hồi hoặc tài khoản không còn quyền truy cập.");
                    }
                }
            };
        });

        return services;
    }

    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "StoryPlatform API (AI Storytelling Platform)",
                Version = "v1"
            });

            // Cấu hình JWT Bearer trong Swagger UI
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Nhập JWT Token của bạn: {token}"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    public static IServiceCollection AddCorsPolicy(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", builder =>
            {
                builder.AllowAnyOrigin()
                       .AllowAnyMethod()
                       .AllowAnyHeader();
            });
        });

        return services;
    }
}
