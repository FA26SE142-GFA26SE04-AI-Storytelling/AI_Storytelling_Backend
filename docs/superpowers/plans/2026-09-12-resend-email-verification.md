# Resend Email Integration + Email Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gửi email thật qua [Resend](https://resend.com) cho 2 luồng: quên mật khẩu (đã có sẵn logic, chỉ thiếu email thật) và xác thực email lúc đăng ký (luồng hoàn toàn mới) — đăng nhập bị chặn cho tới khi email được xác thực.

**Architecture:** Thay `LoggingEmailSender` (chỉ ghi log, dùng tạm) bằng `ResendEmailSender` — cài đặt thật của `IEmailSender` gọi REST API của Resend qua `HttpClient`, theo đúng pattern `HttpClient`-based client đã có sẵn trong dự án (`OpenAILlmClient`/`OpenAIOptions`). Thêm 2 cột `EmailVerificationTokenHash`/`EmailVerificationTokenExpiresAt` trên `UserAccount` (giống hệt pattern `ResetTokenHash`/`ResetTokenExpiresAt` đã có). `RegisterAsync` không còn cấp access/refresh token ngay — thay vào đó tạo + băm + lưu token xác thực rồi gửi email; người dùng phải gọi `POST /auth/verify-email` trước khi `POST /auth/login` thành công.

**Tech Stack:** .NET 10, EF Core + Npgsql, `HttpClient` (Resend REST API `https://api.resend.com/emails`), xUnit + Moq (đã có sẵn trong dự án).

**Spec:** Không có spec/brainstorm doc riêng — yêu cầu là của user trực tiếp trong hội thoại ("dùng Resend để gửi mail forget và register để xác thực người dùng", kèm quyết định rõ ràng: chặn đăng nhập cho tới khi xác thực email, dùng địa chỉ gửi thử `onboarding@resend.dev` của Resend cho tới khi có domain riêng). Plan này bám theo code hiện trạng đã khảo sát trực tiếp trong repo tại thời điểm viết plan (xem "Hiện trạng" bên dưới).

## Hiện trạng (đã khảo sát trực tiếp trước khi viết plan)

Đã có sẵn, **không cần đụng vào**, chỉ tái sử dụng:
- `IEmailSender` (`Abstractions/Communication/IEmailSender.cs`) hiện có đúng 1 method: `SendPasswordResetEmailAsync(string toEmail, string toName, string rawResetToken, CancellationToken)`.
- `AuthService.ForgotPasswordAsync`/`ResetPasswordAsync` đã hoàn thiện đầy đủ (kể cả bảo vệ `Suspended` không tự mở khoá) — **không sửa gì ở 2 method này**, chỉ tận dụng `_emailSender` đã được inject sẵn.
- `TokenHasher.Hash(string) -> string` (SHA-256 hex, `Application/Common/Security/TokenHasher.cs`) — dùng lại y hệt cho token xác thực email.
- `IJwtTokenGenerator.GenerateRefreshToken()` — bộ sinh chuỗi ngẫu nhiên an toàn 32-byte Base64, đã được `ForgotPasswordAsync` tái dùng làm reset-token; dùng lại lần nữa cho verification-token.
- `AccountStatus` enum đã có sẵn đúng 2 giá trị cần: `Registered = 1` (trạng thái ban đầu, CHƯA xác thực) và `EmailVerified = 2` (đã xác thực) — không cần đổi enum.
- `AuthServiceTests.cs` đã có sẵn field/constructor mock đầy đủ (`_emailSenderMock`, `_jwtGeneratorMock`, v.v.) — chỉ cần thêm test method, KHÔNG cần đổi field/constructor block.
- `ChangePasswordAsync`/`GetCurrentUserProfileAsync`/`RefreshTokenAsync`/`LogoutAsync` — không liên quan tới plan này, **không đụng vào**.
- `.gitignore` của dự án đã có sẵn dòng `appsettings*.json` — **toàn bộ file `appsettings.json`/`appsettings.Development.json` của `StoryPlatform.Api` không được commit lên git trong repo này** (đã xác minh bằng `git check-ignore`). Vì vậy API key Resend của user được ghi trực tiếp vào `src/Core/StoryPlatform.Api/appsettings.Development.json` (đã tạo sẵn, key thật đã nằm trong `ResendSettings:ApiKey`) — an toàn vì file này chưa từng và sẽ không bao giờ vào git history. Không có task nào trong plan này cần chạm lại vào giá trị đó, `IConfiguration` tự đọc khi chạy `dotnet run`/`dotnet test`.

**Sẽ được thêm mới trong plan này:**
- `EmailVerificationTokenHash`/`EmailVerificationTokenExpiresAt` trên `UserAccount` (2 cột mới, 1 migration).
- `IEmailSender.SendEmailVerificationEmailAsync(...)` (method thứ 2 trên interface đã có).
- `ResendOptions` + `ResendEmailSender` (thay thế hoàn toàn `LoggingEmailSender`, class này sẽ bị XOÁ vì không còn cần thiết — không giữ lại làm fallback, tránh 2 cài đặt `IEmailSender` cùng tồn tại gây hiểu nhầm cái nào đang chạy thật).
- `IAuthService.VerifyEmailAsync(...)` + `VerifyEmailRequestDto` + `POST /auth/verify-email`.
- Sửa `RegisterAsync`: đổi chữ ký trả về từ `Task<AuthResponseDto>` sang `Task` (không cấp token ngay khi đăng ký nữa).
- Sửa `LoginAsync`: chặn đăng nhập nếu `Status == AccountStatus.Registered` (chưa xác thực email).

**Ngoài phạm vi plan này (không làm):** endpoint "gửi lại email xác thực" khi token hết hạn (24h) — nếu hết hạn, người dùng hiện tại phải liên hệ hỗ trợ hoặc đăng ký lại; đây là hạn chế đã biết, ghi rõ ở cuối plan. Không thêm link xác thực dạng URL (dự án chưa có cấu hình frontend base URL) — email chỉ chứa mã token dạng text, người dùng/frontend tự POST vào `/auth/verify-email`, đúng quy ước JSON-body-only đã dùng cho `reset-password`.

## Global Constraints

- Namespace enum Domain: `StoryPlatform.Domain.Enums`. Namespace Entity: `StoryPlatform.Domain.Entities`.
- Băm token (verification cũng như reset) bằng `TokenHasher.Hash` (SHA-256, deterministic) — không dùng BCrypt, không bao giờ lưu token gốc vào CSDL.
- Email verification token hết hạn sau **24 giờ** (khác 1 giờ của reset-password token — đăng ký xong người dùng có thể chưa mở email ngay).
- `ResendEmailSender` phải ném `InvalidOperationException` nếu `ResendSettings:ApiKey` rỗng (giống hệt cách `OpenAILlmClient` ném lỗi khi thiếu `AI:OpenAI:ApiKey`) — không được âm thầm bỏ qua việc gửi email thất bại.
- Response API luôn qua `ApiResponse<T>` bằng `BaseApiController.HandleResult`.
- Build: `dotnet build StoryPlatform.sln`. Test: `dotnet test StoryPlatform.sln`.
- Không bao giờ in/log/hardcode giá trị thật của `ResendSettings:ApiKey` vào code hay chính plan/report file — chỉ đọc qua `IOptions<ResendOptions>` được bind từ configuration. `appsettings.Development.json` là nơi hợp lệ để chứa giá trị thật CHỈ TRONG REPO NÀY vì đã được `.gitignore` chặn hoàn toàn (`appsettings*.json`) — xác minh lại bằng `git check-ignore -v <path>` trước khi áp dụng thói quen này sang repo khác.

---

## File Structure

| File | Trạng thái | Trách nhiệm |
|---|---|---|
| `src/Core/StoryPlatform.Domain/Entities/UserAccount.cs` | Modify | Thêm 2 cột `EmailVerificationTokenHash`, `EmailVerificationTokenExpiresAt` |
| `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs` | Modify | Khai báo `HasMaxLength` cho cột mới |
| `src/Core/StoryPlatform.Infrastructure/Migrations/*` | Create (qua `dotnet ef migrations add`) | Migration EF Core cho 2 cột mới |
| `src/Core/StoryPlatform.Application/Abstractions/Communication/IEmailSender.cs` | Modify | Thêm method `SendEmailVerificationEmailAsync` |
| `src/Core/StoryPlatform.Infrastructure/Communication/LoggingEmailSender.cs` | **Delete** | Bị thay thế hoàn toàn bởi `ResendEmailSender` — không còn tác dụng, giữ lại sẽ gây nhầm lẫn 2 cài đặt `IEmailSender` |
| `src/Core/StoryPlatform.Infrastructure/Communication/ResendOptions.cs` | Create | Cấu hình `ApiKey`/`Endpoint`/`FromEmail`/`FromName` |
| `src/Core/StoryPlatform.Infrastructure/Communication/ResendEmailSender.cs` | Create | Cài đặt `IEmailSender` gọi REST API Resend qua `HttpClient` |
| `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs` | Modify | Bỏ đăng ký `LoggingEmailSender`, đăng ký `ResendOptions` + `ResendEmailSender` |
| `src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs` | Modify | Thêm `VerifyEmailRequestDto` |
| `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs` | Modify | Đổi chữ ký `RegisterAsync`, thêm `VerifyEmailAsync` |
| `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs` | Modify | Sửa `RegisterAsync`, `LoginAsync`; thêm `VerifyEmailAsync` |
| `src/Core/StoryPlatform.Api/Controllers/AuthController.cs` | Modify | Sửa action `Register`, thêm action `VerifyEmail` |
| `src/Core/StoryPlatform.Api/appsettings.Development.json` | Đã tạo sẵn (git-ignored, không cần task xử lý lại) | Chứa `ResendSettings:ApiKey` thật + `FromEmail`/`FromName` |
| `src/Core/StoryPlatform.Api/StoryPlatform.Api.http` | Modify | Thêm request mẫu `register`/`verify-email` |
| `tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj` | Modify | Thêm `ProjectReference` tới `StoryPlatform.Infrastructure` (để test `ResendEmailSender` trực tiếp) |
| `tests/StoryPlatform.UnitTests/Infrastructure/Communication/ResendEmailSenderTests.cs` | Create | Test `ResendEmailSender` bằng `HttpMessageHandler` giả lập |
| `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs` | Modify | Sửa test `RegisterAsync`, thêm test `LoginAsync` (chặn chưa xác thực), thêm test `VerifyEmailAsync` |

---

### Task 1: Thêm cột lưu Email Verification Token vào `user_accounts` + migration

**Files:**
- Modify: `src/Core/StoryPlatform.Domain/Entities/UserAccount.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs`
- Create: migration mới trong `src/Core/StoryPlatform.Infrastructure/Migrations/` (sinh tự động bởi EF CLI)

**Interfaces:**
- Produces: `UserAccount.EmailVerificationTokenHash` (`string?`), `UserAccount.EmailVerificationTokenExpiresAt` (`DateTime?`) — Task 3 (verify) và Task 4 (register) dùng 2 property này.

- [x] **Step 1: Thêm 2 property mới vào `UserAccount`**

Sửa `src/Core/StoryPlatform.Domain/Entities/UserAccount.cs`. Tìm khối:

```csharp
    public string? ResetTokenHash { get; set; }
    public DateTime? ResetTokenExpiresAt { get; set; }
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
```

Thay bằng:

```csharp
    public string? ResetTokenHash { get; set; }
    public DateTime? ResetTokenExpiresAt { get; set; }
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public string? EmailVerificationTokenHash { get; set; }
    public DateTime? EmailVerificationTokenExpiresAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
```

- [x] **Step 2: Khai báo ràng buộc cột mới trong EF configuration**

Sửa `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs`. Tìm khối:

```csharp
        builder.Property(u => u.RefreshTokenHash)
            .HasMaxLength(64); // SHA-256 hex = 64 ký tự
```

Thay bằng:

```csharp
        builder.Property(u => u.RefreshTokenHash)
            .HasMaxLength(64); // SHA-256 hex = 64 ký tự

        builder.Property(u => u.EmailVerificationTokenHash)
            .HasMaxLength(64); // SHA-256 hex = 64 ký tự
```

- [x] **Step 3: Build để chắc chắn không lỗi biên dịch**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Sinh migration EF Core**

Run (từ thư mục `E:\Capstone\AI_Storytelling_Backend`):

```bash
dotnet ef migrations add AddEmailVerificationTokenToUserAccount --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api
```

Expected: sinh ra file `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddEmailVerificationTokenToUserAccount.cs` chỉ chứa `AddColumn` cho 2 cột mới trên bảng `user_accounts` (không đụng bảng nào khác). Mở file migration ra đọc lại để xác nhận đúng như vậy.

- [ ] **Step 5: Áp dụng migration vào CSDL cục bộ (nếu Postgres đang chạy)**

Run:

```bash
dotnet ef database update --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api
```

Nếu không có CSDL cục bộ sẵn sàng, ghi chú lại và bỏ qua bước này — không coi là blocker.

---

### Task 2: `ResendEmailSender` — cài đặt `IEmailSender` gọi Resend thật (thay `LoggingEmailSender`)

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Abstractions/Communication/IEmailSender.cs`
- Delete: `src/Core/StoryPlatform.Infrastructure/Communication/LoggingEmailSender.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/Communication/ResendOptions.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/Communication/ResendEmailSender.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs`
- Modify: `tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`
- Test: `tests/StoryPlatform.UnitTests/Infrastructure/Communication/ResendEmailSenderTests.cs`

**Interfaces:**
- Produces: `IEmailSender.SendEmailVerificationEmailAsync(string toEmail, string toName, string rawVerificationToken, CancellationToken) -> Task` — Task 4 (`RegisterAsync`) gọi method này.
- Produces: `ResendEmailSender : IEmailSender` implement CẢ HAI method của interface (`SendPasswordResetEmailAsync` đã có từ trước + method mới) — DI sẽ đăng ký class này thay `LoggingEmailSender`.

- [x] **Step 1: Thêm `ProjectReference` tới Infrastructure trong test project**

Sửa `tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`, thêm dòng sau vào `<ItemGroup>` chứa các `ProjectReference` hiện có:

```xml
    <ProjectReference Include="..\..\src\Core\StoryPlatform.Infrastructure\StoryPlatform.Infrastructure.csproj" />
```

Run: `dotnet restore tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`
Expected: restore thành công.

- [x] **Step 2: Viết test thất bại cho `ResendEmailSender`**

Tạo `tests/StoryPlatform.UnitTests/Infrastructure/Communication/ResendEmailSenderTests.cs`:

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Infrastructure.Communication;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Communication;

public class ResendEmailSenderTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public FakeHttpMessageHandler(HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _statusCode = statusCode;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : null;

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("{\"id\":\"test-email-id\"}")
            };
        }
    }

    private static ResendEmailSender CreateSender(FakeHttpMessageHandler handler, string apiKey = "test-api-key")
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new ResendOptions
        {
            ApiKey = apiKey,
            FromEmail = "onboarding@resend.dev",
            FromName = "Test Sender"
        });

        return new ResendEmailSender(httpClient, options, NullLogger<ResendEmailSender>.Instance);
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_ValidRequest_PostsCorrectRequestToResendApi()
    {
        var handler = new FakeHttpMessageHandler();
        var sender = CreateSender(handler);

        await sender.SendPasswordResetEmailAsync("user@example.com", "Nguyen Van A", "raw-reset-token-123");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://api.resend.com/emails", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-api-key", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Contains("raw-reset-token-123", handler.LastRequestBody);
        Assert.Contains("user@example.com", handler.LastRequestBody);
    }

    [Fact]
    public async Task SendEmailVerificationEmailAsync_ValidRequest_PostsCorrectRequestToResendApi()
    {
        var handler = new FakeHttpMessageHandler();
        var sender = CreateSender(handler);

        await sender.SendEmailVerificationEmailAsync("user@example.com", "Nguyen Van A", "raw-verification-token-456");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("raw-verification-token-456", handler.LastRequestBody);
        Assert.Contains("user@example.com", handler.LastRequestBody);
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_MissingApiKey_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler();
        var sender = CreateSender(handler, apiKey: "");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendPasswordResetEmailAsync("user@example.com", "Nguyen Van A", "raw-token"));
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_ResendApiReturnsError_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.BadRequest);
        var sender = CreateSender(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendPasswordResetEmailAsync("user@example.com", "Nguyen Van A", "raw-token"));
    }
}
```

- [x] **Step 3: Chạy test, xác nhận thất bại vì `ResendOptions`/`ResendEmailSender` chưa tồn tại**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter ResendEmailSenderTests`
Expected: FAIL — lỗi biên dịch (không tìm thấy `StoryPlatform.Infrastructure.Communication.ResendOptions`/`ResendEmailSender`).

- [x] **Step 4: Mở rộng `IEmailSender`**

Sửa `src/Core/StoryPlatform.Application/Abstractions/Communication/IEmailSender.cs` thành:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace StoryPlatform.Application.Abstractions.Communication;

public interface IEmailSender
{
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string rawResetToken, CancellationToken cancellationToken = default);
    Task SendEmailVerificationEmailAsync(string toEmail, string toName, string rawVerificationToken, CancellationToken cancellationToken = default);
}
```

- [x] **Step 5: Xoá `LoggingEmailSender.cs`**

Xoá file `src/Core/StoryPlatform.Infrastructure/Communication/LoggingEmailSender.cs` (`git rm` hoặc xoá thủ công) — bị thay thế hoàn toàn bởi `ResendEmailSender` ở bước sau, không còn được đăng ký ở đâu.

- [x] **Step 6: Tạo `ResendOptions`**

Tạo `src/Core/StoryPlatform.Infrastructure/Communication/ResendOptions.cs`:

```csharp
namespace StoryPlatform.Infrastructure.Communication;

/// <summary>
/// Cấu hình cho ResendEmailSender — đọc từ configuration section "ResendSettings".
/// ApiKey đặt trong appsettings.Development.json (dev, file này đã bị .gitignore chặn
/// hoàn toàn trong repo này) hoặc biến môi trường ResendSettings__ApiKey (production).
/// </summary>
public sealed class ResendOptions
{
    public const string SectionName = "ResendSettings";
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.resend.com/emails";
    public string FromEmail { get; set; } = "onboarding@resend.dev";
    public string FromName { get; set; } = "AI Storytelling Platform";
}
```

- [x] **Step 7: Tạo `ResendEmailSender`**

Tạo `src/Core/StoryPlatform.Infrastructure/Communication/ResendEmailSender.cs`:

```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Abstractions.Communication;

namespace StoryPlatform.Infrastructure.Communication;

/// <summary>
/// Cài đặt IEmailSender gửi email thật qua dịch vụ Resend (https://resend.com/docs/api-reference/emails/send-email).
/// Thay thế hoàn toàn LoggingEmailSender (đã xoá) — đây là cài đặt production, không phải placeholder.
/// </summary>
public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient httpClient, IOptions<ResendOptions> options, ILogger<ResendEmailSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string toName, string rawResetToken, CancellationToken cancellationToken = default)
    {
        const string subject = "Yêu cầu đặt lại mật khẩu";
        var html = $"""
            <p>Chào {toName},</p>
            <p>Bạn vừa yêu cầu đặt lại mật khẩu cho tài khoản AI Storytelling Platform. Mã đặt lại mật khẩu của bạn là:</p>
            <p style="font-size:22px;font-weight:bold;letter-spacing:2px;">{rawResetToken}</p>
            <p>Mã này có hiệu lực trong 1 giờ. Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.</p>
            """;

        return SendAsync(toEmail, subject, html, cancellationToken);
    }

    public Task SendEmailVerificationEmailAsync(string toEmail, string toName, string rawVerificationToken, CancellationToken cancellationToken = default)
    {
        const string subject = "Xác thực địa chỉ email của bạn";
        var html = $"""
            <p>Chào {toName},</p>
            <p>Cảm ơn bạn đã đăng ký tài khoản AI Storytelling Platform. Mã xác thực email của bạn là:</p>
            <p style="font-size:22px;font-weight:bold;letter-spacing:2px;">{rawVerificationToken}</p>
            <p>Mã này có hiệu lực trong 24 giờ. Vui lòng xác thực email trước khi đăng nhập.</p>
            """;

        return SendAsync(toEmail, subject, html, cancellationToken);
    }

    private async Task SendAsync(string toEmail, string subject, string html, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("ResendSettings:ApiKey (hoặc biến môi trường ResendSettings__ApiKey) chưa được cấu hình.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            from = $"{_options.FromName} <{_options.FromEmail}>",
            to = new[] { toEmail },
            subject,
            html
        });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Resend API trả về lỗi {StatusCode} khi gửi email tới {ToEmail}: {Body}",
                (int)response.StatusCode, toEmail, body);
            throw new InvalidOperationException($"Gửi email qua Resend thất bại (HTTP {(int)response.StatusCode}).");
        }
    }
}
```

- [x] **Step 8: Đăng ký DI — bỏ `LoggingEmailSender`, thêm `ResendEmailSender`**

Sửa `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs`. Tìm dòng:

```csharp
        services.AddScoped<IEmailSender, LoggingEmailSender>();
```

Thay bằng:

```csharp
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        services.AddHttpClient<IEmailSender, ResendEmailSender>();
```

- [x] **Step 9: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter ResendEmailSenderTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [x] **Step 10: Build toàn bộ solution**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)` — xác nhận không còn nơi nào tham chiếu `LoggingEmailSender` đã xoá.

---

### Task 3: API xác thực email — `POST /api/v1/auth/verify-email`

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`
- Modify: `src/Core/StoryPlatform.Api/Controllers/AuthController.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `TokenHasher.Hash` (đã có), `UserAccount.EmailVerificationTokenHash`/`EmailVerificationTokenExpiresAt` (Task 1).
- Produces: `IAuthService.VerifyEmailAsync(VerifyEmailRequestDto, CancellationToken) -> Task`.

- [x] **Step 1: Viết test thất bại**

Thêm vào cuối class `AuthServiceTests` (trước dấu `}` cuối file):

```csharp
    [Fact]
    public async Task VerifyEmailAsync_ValidToken_SetsStatusToEmailVerifiedAndClearsToken()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = TokenHasher.Hash("valid-raw-verification-token");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(12);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await _sut.VerifyEmailAsync(new VerifyEmailRequestDto { Email = user.Email, Token = "valid-raw-verification-token" });

        Assert.Equal(AccountStatus.EmailVerified, user.Status);
        Assert.Null(user.EmailVerificationTokenHash);
        Assert.Null(user.EmailVerificationTokenExpiresAt);
    }

    [Fact]
    public async Task VerifyEmailAsync_InvalidToken_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = TokenHasher.Hash("correct-token");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(12);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.VerifyEmailAsync(new VerifyEmailRequestDto { Email = user.Email, Token = "wrong-token" }));
    }

    [Fact]
    public async Task VerifyEmailAsync_ExpiredToken_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = TokenHasher.Hash("expired-token");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(-1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.VerifyEmailAsync(new VerifyEmailRequestDto { Email = user.Email, Token = "expired-token" }));
    }
```

- [x] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: FAIL — lỗi biên dịch (chưa có `VerifyEmailRequestDto`/`VerifyEmailAsync`).

- [x] **Step 3: Thêm DTO**

Thêm vào cuối `src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs`:

```csharp
public class VerifyEmailRequestDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã xác thực không được để trống.")]
    public string Token { get; set; } = string.Empty;
}
```

- [x] **Step 4: Thêm method vào interface**

Sửa `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs`, thêm dòng:

```csharp
    Task VerifyEmailAsync(VerifyEmailRequestDto request, CancellationToken cancellationToken = default);
```

- [x] **Step 5: Cài đặt `VerifyEmailAsync` trong `AuthService`**

Thêm method (đặt sau `ForgotPasswordAsync`, trước `ResetPasswordAsync`):

```csharp
    public async Task VerifyEmailAsync(VerifyEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken: cancellationToken);

        var hashedIncomingToken = TokenHasher.Hash(request.Token);
        if (user == null || user.EmailVerificationTokenHash != hashedIncomingToken)
        {
            throw new BadRequestException("Mã xác thực email không hợp lệ.");
        }

        if (user.EmailVerificationTokenExpiresAt == null || user.EmailVerificationTokenExpiresAt < DateTime.UtcNow)
        {
            throw new BadRequestException("Mã xác thực email đã hết hạn.");
        }

        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAt = null;
        // Chỉ nâng cấp trạng thái Registered -> EmailVerified. Không hạ cấp một tài khoản
        // đã LoggedIn/LoggedOut/PasswordResetPending, và không mở khoá Suspended qua đường này.
        if (user.Status == AccountStatus.Registered)
        {
            user.Status = AccountStatus.EmailVerified;
        }
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
```

- [x] **Step 6: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: tất cả test trong file pass (bao gồm 3 test mới).

- [x] **Step 7: Thêm endpoint controller**

Thêm vào `src/Core/StoryPlatform.Api/Controllers/AuthController.cs`, sau action `Login`:

```csharp
    /// <summary>
    /// Xác thực email bằng mã đã gửi lúc đăng ký — bắt buộc trước khi đăng nhập lần đầu.
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> VerifyEmail(
        [FromBody] VerifyEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        await _authService.VerifyEmailAsync(request, cancellationToken);
        return HandleResult<object?>(null, "Xác thực email thành công. Bạn có thể đăng nhập ngay bây giờ.");
    }
```

- [x] **Step 8: Build toàn bộ**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

---

### Task 4: Đăng ký không cấp token ngay — sinh + lưu + gửi mã xác thực email

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`
- Modify: `src/Core/StoryPlatform.Api/Controllers/AuthController.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `IEmailSender.SendEmailVerificationEmailAsync` (Task 2), `TokenHasher.Hash` (đã có).
- Produces: `IAuthService.RegisterAsync` đổi chữ ký từ `Task<AuthResponseDto>` thành `Task` — **đây là breaking change so với hành vi cũ**, không còn caller nào khác gọi `RegisterAsync` ngoài `AuthController.Register` (đã xác nhận bằng cách tìm kiếm trong toàn bộ `src/`).

- [x] **Step 1: Sửa test `RegisterAsync` hiện có**

Trong `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs`, tìm toàn bộ method:

```csharp
    [Fact]
    public async Task RegisterAsync_NewEmail_CreatesUserWithHashedRefreshToken()
    {
        _userRepoMock.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>())).Returns("hashed-password");
        UserAccount? addedUser = null;
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccount, CancellationToken>((u, _) => addedUser = u)
            .ReturnsAsync((UserAccount u, CancellationToken _) => u);

        var request = new RegisterRequestDto
        {
            Username = "parent_new",
            Email = "new@example.com",
            FullName = "Nguoi Dung Moi",
            Password = "Demo@123",
            ConfirmPassword = "Demo@123"
        };

        var result = await _sut.RegisterAsync(request);

        Assert.NotNull(addedUser);
        Assert.Equal("fake-refresh-token-raw", result.RefreshToken);
        Assert.NotNull(addedUser!.RefreshTokenHash);
        // Tài khoản mới (Id == 0) đang ở trạng thái Added của EF — không được gọi Update().
        _userRepoMock.Verify(r => r.Update(It.IsAny<UserAccount>()), Times.Never);
    }
```

Thay TOÀN BỘ bằng:

```csharp
    [Fact]
    public async Task RegisterAsync_NewEmail_CreatesUserAndSendsVerificationEmail()
    {
        _userRepoMock.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>())).Returns("hashed-password");
        UserAccount? addedUser = null;
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccount, CancellationToken>((u, _) => addedUser = u)
            .ReturnsAsync((UserAccount u, CancellationToken _) => u);

        var request = new RegisterRequestDto
        {
            Username = "parent_new",
            Email = "new@example.com",
            FullName = "Nguoi Dung Moi",
            Password = "Demo@123",
            ConfirmPassword = "Demo@123"
        };

        await _sut.RegisterAsync(request);

        Assert.NotNull(addedUser);
        Assert.Equal(AccountStatus.Registered, addedUser!.Status);
        Assert.Equal(TokenHasher.Hash("fake-refresh-token-raw"), addedUser.EmailVerificationTokenHash);
        Assert.True(addedUser.EmailVerificationTokenExpiresAt > DateTime.UtcNow.AddHours(23));
        // Đăng ký không còn cấp phiên đăng nhập ngay — phải xác thực email trước.
        Assert.Null(addedUser.RefreshTokenHash);
        _emailSenderMock.Verify(e => e.SendEmailVerificationEmailAsync(
            "new@example.com", "Nguoi Dung Moi", "fake-refresh-token-raw", It.IsAny<CancellationToken>()), Times.Once);
        // Tài khoản mới (Id == 0) đang ở trạng thái Added của EF — không được gọi Update().
        _userRepoMock.Verify(r => r.Update(It.IsAny<UserAccount>()), Times.Never);
    }
```

(method `RegisterAsync_PasswordMismatch_ThrowsBadRequestException` ngay bên dưới GIỮ NGUYÊN — nó throw trước khi chạm tới logic bị đổi)

- [x] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: FAIL — lỗi biên dịch (`RegisterAsync` vẫn trả `Task<AuthResponseDto>`, chưa có `EmailVerificationTokenHash` được set) hoặc assertion fail.

- [x] **Step 3: Đổi chữ ký trong interface**

Sửa `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs`. Tìm dòng:

```csharp
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
```

Thay bằng:

```csharp
    Task RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
```

- [x] **Step 4: Sửa `RegisterAsync` trong `AuthService`**

Tìm toàn bộ method `RegisterAsync` (từ `public async Task<AuthResponseDto> RegisterAsync` tới dấu `}` đóng method, ngay trước `public async Task<UserProfileDto> GetCurrentUserProfileAsync`), thay bằng:

```csharp
    public async Task RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.Password != request.ConfirmPassword)
        {
            throw new BadRequestException("Mật khẩu xác nhận không khớp.");
        }

        var userRepo = _unitOfWork.Repository<UserAccount>();

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailExists = await userRepo.ExistsAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            throw new BadRequestException($"Email '{request.Email}' đã được sử dụng trong hệ thống.");
        }

        var normalizedUsername = request.Username.Trim().ToLowerInvariant();
        var usernameExists = await userRepo.ExistsAsync(u => u.Username.ToLower() == normalizedUsername, cancellationToken);
        if (usernameExists)
        {
            throw new BadRequestException($"Tên đăng nhập '{request.Username}' đã tồn tại.");
        }

        var rawVerificationToken = _jwtTokenGenerator.GenerateRefreshToken(); // tái dùng bộ sinh chuỗi ngẫu nhiên an toàn sẵn có

        var newUser = new UserAccount
        {
            Username = request.Username.Trim(),
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Role = UserRole.Parent, // Mặc định người dùng đăng ký là phụ huynh
            Status = AccountStatus.Registered,
            EmailVerificationTokenHash = TokenHasher.Hash(rawVerificationToken),
            EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        await userRepo.AddAsync(newUser, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _emailSender.SendEmailVerificationEmailAsync(newUser.Email, newUser.FullName, rawVerificationToken, cancellationToken);
    }
```

- [x] **Step 5: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: tất cả test pass.

- [x] **Step 6: Sửa action `Register` trong controller**

Sửa `src/Core/StoryPlatform.Api/Controllers/AuthController.cs`. Tìm khối:

```csharp
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register(
        [FromBody] RegisterRequestDto request, 
        CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        return HandleResult(result, "Đăng ký tài khoản thành công.");
    }
```

Thay bằng:

```csharp
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> Register(
        [FromBody] RegisterRequestDto request, 
        CancellationToken cancellationToken)
    {
        await _authService.RegisterAsync(request, cancellationToken);
        return HandleResult<object?>(null, "Đăng ký thành công. Vui lòng kiểm tra email để lấy mã xác thực trước khi đăng nhập.");
    }
```

- [x] **Step 7: Build toàn bộ**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

---

### Task 5: Chặn đăng nhập khi email chưa xác thực

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs`

**Interfaces:**
- Không có interface mới — chỉ thêm 1 điều kiện chặn trong `LoginAsync` đã có.

- [x] **Step 1: Viết test thất bại**

Thêm vào cuối class `AuthServiceTests`:

```csharp
    [Fact]
    public async Task LoginAsync_UnverifiedAccount_ThrowsForbiddenException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" }));
    }
```

- [x] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: FAIL — `LoginAsync` hiện tại không ném lỗi cho `Status == Registered`, test không throw như mong đợi.

- [x] **Step 3: Thêm điều kiện chặn trong `LoginAsync`**

Trong `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`, tìm khối:

```csharp
        if (user.Status == AccountStatus.Suspended)
        {
            throw new ForbiddenException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        user.Status = AccountStatus.LoggedIn;
```

Thay bằng:

```csharp
        if (user.Status == AccountStatus.Suspended)
        {
            throw new ForbiddenException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        if (user.Status == AccountStatus.Registered)
        {
            throw new ForbiddenException("Vui lòng xác thực email trước khi đăng nhập. Kiểm tra hộp thư của bạn để lấy mã xác thực.");
        }

        user.Status = AccountStatus.LoggedIn;
```

- [x] **Step 4: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: tất cả test pass. (Lưu ý: `CreateUser()` helper mặc định đã dùng `Status = AccountStatus.EmailVerified` từ trước — các test `LoginAsync` khác không bị ảnh hưởng.)

- [x] **Step 5: Build toàn bộ**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

---

### Task 6: Test tay đầu-cuối, hoàn thiện

**Files:**
- Modify: `src/Core/StoryPlatform.Api/StoryPlatform.Api.http`

**Interfaces:**
- Consumes: toàn bộ endpoint đã hoàn thành ở Task 1-5 (`register`, `verify-email`, `login`, `forgot-password`, `reset-password`).

`src/Core/StoryPlatform.Api/appsettings.Development.json` đã được tạo sẵn với `ResendSettings:ApiKey` (giá trị thật) + `FromEmail`/`FromName` — file này bị `.gitignore` chặn hoàn toàn (`appsettings*.json`) nên an toàn khi chứa key thật, không cần task nào xử lý lại.

- [x] **Step 1: Thêm request mẫu vào file `.http`**

Thêm vào cuối `src/Core/StoryPlatform.Api/StoryPlatform.Api.http`:

```http

### Đăng ký tài khoản mới (không còn trả về token — kiểm tra email/log Resend để lấy mã xác thực)
POST {{StoryPlatform_Api_HostAddress}}/api/v1/auth/register
Content-Type: application/json

{
  "username": "parent_test_new",
  "email": "parent_test_new@example.com",
  "fullName": "Nguyen Van Test",
  "password": "Demo@123",
  "confirmPassword": "Demo@123"
}

### Xác thực email (thay {{verificationToken}} bằng mã nhận được qua email Resend)
POST {{StoryPlatform_Api_HostAddress}}/api/v1/auth/verify-email
Content-Type: application/json

{
  "email": "parent_test_new@example.com",
  "token": "{{verificationToken}}"
}

### Đăng nhập (chỉ thành công SAU KHI đã verify-email ở trên)
POST {{StoryPlatform_Api_HostAddress}}/api/v1/auth/login
Content-Type: application/json

{
  "identifier": "parent_test_new@example.com",
  "password": "Demo@123"
}
```

- [x] **Step 2: Chạy toàn bộ test suite của solution**

Run: `dotnet test StoryPlatform.sln`
Expected: tất cả test PASS (bao gồm `AuthServiceTests` với các test mới/sửa và `ResendEmailSenderTests` — 4 test mới).

- [x] **Step 3: Build toàn bộ solution lần cuối**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: (Nếu có Postgres cục bộ đang chạy) Chạy thử API và test tay toàn bộ chuỗi bằng file `.http`**

Run: `dotnet run --project src/Core/StoryPlatform.Api`

Thực hiện lần lượt: `register` (kiểm tra Resend Dashboard hoặc hộp thư email thật nhận mã xác thực — vì đang dùng `onboarding@resend.dev`, email chỉ gửi được tới đúng địa chỉ email của chủ tài khoản Resend cho tới khi verify domain riêng) → `verify-email` với mã nhận được → `login` (phải thành công) → thử `login` lại với 1 tài khoản MỚI đăng ký nhưng CHƯA verify-email (phải nhận lỗi 403 "Vui lòng xác thực email...").

---

## Ghi chú / giới hạn đã biết

1. **Chưa có endpoint "gửi lại mã xác thực".** Nếu mã xác thực email hết hạn sau 24 giờ mà người dùng chưa xác thực, tài khoản đó bị kẹt vĩnh viễn ở trạng thái `Registered` (không đăng nhập được, không tự lấy mã mới được) — cần đăng ký lại bằng email khác hoặc một admin flow xử lý thủ công. Đây là hạn chế đã biết, có thể bổ sung sau bằng 1 endpoint `POST /auth/resend-verification-email` nếu cần.
2. **`onboarding@resend.dev` chỉ gửi được tới email của chính chủ tài khoản Resend** cho tới khi verify 1 domain riêng trên Resend Dashboard — đủ dùng để dev/test nhưng KHÔNG dùng được cho người dùng thật. Khi có domain riêng, chỉ cần đổi `FromEmail`/`FromName` trong `appsettings.Development.json`/`appsettings.json` — không cần đổi code.
3. **Tài khoản demo/seed có sẵn với `Status = Registered`** (nếu `Database/Seed-Database.ps1` có seed dữ liệu như vậy) sẽ KHÔNG đăng nhập được nữa sau khi Task 5 hoàn tất, cho tới khi được set thủ công sang `EmailVerified` trong CSDL hoặc verify qua API. Không nằm trong phạm vi plan này để sửa seed script — nêu ra để người dùng biết trước khi test.
