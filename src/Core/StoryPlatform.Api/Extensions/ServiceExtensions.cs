using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using System.Security.Claims;

namespace StoryPlatform.Api.Extensions;

public static class ServiceExtensions
{
    private const int MinimumJwtSecretKeyLength = 32;

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var secretKey = configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey)
            || secretKey.Length < MinimumJwtSecretKeyLength)
        {
            throw new InvalidOperationException(
                $"'JwtSettings:SecretKey' phải có ít nhất {MinimumJwtSecretKeyLength} ký tự và không được để trống.");
        }

        var issuer = GetRequiredJwtSetting(configuration, "Issuer");
        var audience = GetRequiredJwtSetting(configuration, "Audience");
        ValidatePositiveJwtInteger(configuration, "ExpiryMinutes");
        ValidatePositiveJwtInteger(configuration, "RefreshTokenExpiryDays");
        ValidatePositiveJwtInteger(configuration, "ChildTokenExpiryMinutes");

        var key = Encoding.UTF8.GetBytes(secretKey);

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
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var tokenType = context.Principal?.FindFirst("token_type")?.Value;
                    var subjectClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    if (tokenType == "child")
                    {
                        if (!int.TryParse(subjectClaim, out var childProfileId))
                        {
                            context.Fail("Token không hợp lệ.");
                            return;
                        }

                        var childUnitOfWork = context.HttpContext.RequestServices
                            .GetRequiredService<IUnitOfWork>();
                        var childProfile = await childUnitOfWork.Repository<ChildProfile>()
                            .FirstOrDefaultAsync(
                                profile => profile.Id == childProfileId,
                                cancellationToken: context.HttpContext.RequestAborted);
                        if (childProfile == null
                            || childProfile.IsDeleted
                            || childProfile.Status == ChildProfileStatus.Archived)
                        {
                            context.Fail("Phiên của trẻ không còn hợp lệ.");
                        }

                        return;
                    }

                    var versionClaim = context.Principal?.FindFirst("token_version")?.Value;
                    var userIdClaim = subjectClaim;
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

    private static string GetRequiredJwtSetting(IConfiguration configuration, string settingName)
    {
        var key = $"JwtSettings:{settingName}";
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Thiếu cấu hình bắt buộc '{key}'.");
        }

        return value;
    }

    private static void ValidatePositiveJwtInteger(IConfiguration configuration, string settingName)
    {
        var key = $"JwtSettings:{settingName}";
        if (!int.TryParse(configuration[key], out var value) || value <= 0)
        {
            throw new InvalidOperationException($"'{key}' phải là số nguyên dương.");
        }
    }

    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.CustomSchemaIds(type => type.FullName?.Replace("+", "."));

            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "StoryPlatform API (AI Storytelling Platform)",
                Version = "v1"
            });

            // DTO names may repeat across feature namespaces. Use the qualified type
            // name so each OpenAPI component receives a stable, unique schema ID.
            c.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace('+', '.'));

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
