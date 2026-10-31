using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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
    public const string ChildSessionPolicy = "ChildSession";
    public const string ChildTokenType = "child";

    public const string ForwardedHeadersKnownNetworksKey = "ForwardedHeaders:KnownNetworks";
    public const string ForwardedHeadersForwardLimitKey = "ForwardedHeaders:ForwardLimit";
    private const int DefaultForwardLimit = 2;

    /// <summary>
    /// Production: client → Cloudflare → ALB (trong VPC) → API (xem infra/aws-cdk). Chỉ tin X-Forwarded-For/Proto
    /// khi hop gửi tới nằm trong "ForwardedHeaders:KnownNetworks" (dải VPC của ALB + dải IP Cloudflare), tối đa
    /// "ForwardedHeaders:ForwardLimit" hop (mặc định 2: ALB rồi Cloudflare) để RemoteIpAddress là IP client thật,
    /// dùng cho rate limiter và khóa quét sai Easy Login. Không cấu hình dải nào (local/compose) thì bỏ qua header.
    /// </summary>
    public static IServiceCollection AddLoadBalancerForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var knownNetworks = ResolveForwardedHeadersKnownNetworks(configuration);
        var forwardLimit = configuration.GetValue(ForwardedHeadersForwardLimitKey, DefaultForwardLimit);
        if (forwardLimit <= 0)
        {
            throw new InvalidOperationException($"'{ForwardedHeadersForwardLimitKey}' phải là số nguyên dương.");
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            if (knownNetworks.Length == 0)
            {
                options.ForwardedHeaders = ForwardedHeaders.None;
                return;
            }

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = forwardLimit;
            foreach (var network in knownNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return services;
    }

    public static System.Net.IPNetwork[] ResolveForwardedHeadersKnownNetworks(IConfiguration configuration)
    {
        var configured = (configuration.GetSection(ForwardedHeadersKnownNetworksKey).Get<string[]>() ?? [])
            .Select(network => network.Trim())
            .Where(network => network.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return configured
            .Select(network => System.Net.IPNetwork.TryParse(network, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"'{ForwardedHeadersKnownNetworksKey}' chỉ nhận dải CIDR (ví dụ 10.0.0.0/16), không nhận '{network}'."))
            .ToArray();
    }

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

                    if (tokenType == ChildTokenType)
                    {
                        var sessionKey = context.Principal?.FindFirst(ChildSession.SessionClaimType)?.Value;
                        if (!int.TryParse(subjectClaim, out var childProfileId)
                            || string.IsNullOrWhiteSpace(sessionKey))
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
                        // Bước 3.0 — chỉ hồ sơ Active mới giữ được Child Session đang mở.
                        if (childProfile == null
                            || childProfile.IsDeleted
                            || childProfile.Status != ChildProfileStatus.Active)
                        {
                            context.Fail("Phiên của trẻ không còn hợp lệ.");
                            return;
                        }

                        // Bước 1.10 — idle-timeout 20 phút; ghi hoạt động tối đa 1 lần/phút.
                        var sessionRepository = childUnitOfWork.Repository<ChildSession>();
                        var session = await sessionRepository.FirstOrDefaultAsync(
                            value => value.SessionKey == sessionKey,
                            cancellationToken: context.HttpContext.RequestAborted);
                        var now = DateTime.UtcNow;
                        if (session == null
                            || session.ChildProfileId != childProfileId
                            || session.IsIdleExpired(now))
                        {
                            context.Fail("Phiên của trẻ đã hết hạn do không hoạt động. Vui lòng đăng nhập lại.");
                            return;
                        }

                        if (session.ShouldRecordActivity(now))
                        {
                            session.LastActivityAt = now;
                            session.UpdatedAt = now;
                            sessionRepository.Update(session);
                            await childUnitOfWork.SaveChangesAsync(context.HttpContext.RequestAborted);
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

    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Bước 3.0 — [Authorize] trơn chỉ dành cho người lớn: Child token không bao giờ lọt vào
            // Guardian Management Area/Admin Portal, kể cả khi client gọi nhầm route.
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim("token_type", ChildTokenType))
                .Build();
            options.AddPolicy(ChildSessionPolicy,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("token_type", ChildTokenType));
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

            // Query enum hiển thị tên (Pending, Medium...) thay vì số; model binder nhận cả tên lẫn số.
            c.ParameterFilter<QueryEnumAsStringParameterFilter>();

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

    private sealed class QueryEnumAsStringParameterFilter : Swashbuckle.AspNetCore.SwaggerGen.IParameterFilter
    {
        public void Apply(OpenApiParameter parameter, Swashbuckle.AspNetCore.SwaggerGen.ParameterFilterContext context)
        {
            if (parameter.In != ParameterLocation.Query)
            {
                return;
            }

            var type = context.ApiParameterDescription.Type;
            var enumType = type is null ? null : Nullable.GetUnderlyingType(type) ?? type;
            if (enumType is null || !enumType.IsEnum)
            {
                return;
            }

            parameter.Schema = new OpenApiSchema
            {
                Type = "string",
                Nullable = Nullable.GetUnderlyingType(type!) != null,
                Enum = Enum.GetNames(enumType)
                    .Select(name => (Microsoft.OpenApi.Any.IOpenApiAny)new Microsoft.OpenApi.Any.OpenApiString(name))
                    .ToList()
            };
        }
    }

    public const string CorsPolicyName = "WebFrontend";
    public const string CorsAllowedOriginsKey = "Cors:AllowedOrigins";
    private static readonly string[] DevelopmentCorsOrigins = ["http://localhost:3000", "http://127.0.0.1:3000"];

    /// <summary>
    /// Chỉ các origin trong "Cors:AllowedOrigins" được gọi API từ trình duyệt (DF06). Môi trường Development
    /// không cấu hình thì dùng web local; môi trường khác không cấu hình thì không cho origin nào (fail closed).
    /// Web gửi JWT qua header Authorization, không dùng cookie, nên không bật AllowCredentials.
    /// </summary>
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var origins = ResolveCorsOrigins(configuration, isDevelopment);
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, builder =>
            {
                if (origins.Length > 0)
                {
                    builder.WithOrigins(origins)
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                }
            });
        });

        return services;
    }

    public static string[] ResolveCorsOrigins(IConfiguration configuration, bool isDevelopment)
    {
        var configured = (configuration.GetSection(CorsAllowedOriginsKey).Get<string[]>() ?? [])
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Where(origin => origin.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var origin in configured)
        {
            if (origin == "*" || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/")
            {
                throw new InvalidOperationException(
                    $"'{CorsAllowedOriginsKey}' chỉ nhận origin dạng scheme://host[:port], không nhận '{origin}'.");
            }
        }

        return configured.Length == 0 && isDevelopment ? DevelopmentCorsOrigins : configured;
    }
}
