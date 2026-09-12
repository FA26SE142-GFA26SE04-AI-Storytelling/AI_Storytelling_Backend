# Auth (Đăng ký / Đăng nhập) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hoàn thiện vòng đời xác thực tài khoản (đăng ký, đăng nhập, làm mới token, đăng xuất, quên/đặt lại mật khẩu) trên nền `AuthController`/`AuthService` đã có sẵn một phần trong `StoryPlatform.Api`/`StoryPlatform.Application`.

**Architecture:** Giữ nguyên kiến trúc Clean Architecture hiện có (`Api` → `Application` → `Domain`, `Infrastructure` cài đặt các abstraction của `Application`). Bổ sung 2 cột lưu Refresh Token trực tiếp trên `UserAccount` (mô hình 1 phiên đăng nhập hiệu lực/tài khoản — đơn giản, đúng quy mô hiện tại của dự án, tránh tạo bảng mới ngoài phạm vi DBML). Quên/đặt lại mật khẩu tái sử dụng 2 cột `ResetTokenHash`/`ResetTokenExpiresAt` đã có sẵn trên `UserAccount` (không cần đổi schema). Thêm 1 abstraction `IEmailSender` (Application) + cài đặt log-only (Infrastructure) làm chỗ cắm email thật sau này.

**Tech Stack:** .NET 10 (ASP.NET Core Web API), EF Core + Npgsql (PostgreSQL), BCrypt.Net (băm mật khẩu), JWT Bearer (access token), SHA-256 (băm refresh token & reset token để tra cứu theo giá trị), xUnit + Moq (unit test).

**Spec:** Không có spec/brainstorm doc riêng cho tính năng này — yêu cầu là hoàn thiện phần Đăng ký/Đăng nhập của Luồng 1 ("Profile & Supervision Setup") trong DBML `ai_storytelling_platform` đã có trong dự án (bảng `user_accounts`, enum `account_status` với các giá trị `Registered/EmailVerified/LoggedIn/PasswordResetPending/LoggedOut/Suspended`). Plan này bám theo code hiện trạng đã khảo sát trực tiếp trong repo (xem "Hiện trạng" bên dưới) thay vì một tài liệu spec riêng.

## Hiện trạng (đã khảo sát trước khi viết plan)

Đã có sẵn và **không cần đụng vào**, chỉ tái sử dụng:
- `src/Core/StoryPlatform.Api/Controllers/AuthController.cs` — có `POST /api/v1/auth/register`, `POST /api/v1/auth/login`, `GET /api/v1/auth/me`.
- `src/Core/StoryPlatform.Application/Features/Auth/{DTOs,Interfaces,Services}/*` — `IAuthService`, `AuthService`, `LoginRequestDto`, `RegisterRequestDto`, `UserProfileDto`, `AuthResponseDto`.
- `src/Core/StoryPlatform.Application/Abstractions/Security/{IPasswordHasher,IJwtTokenGenerator}.cs` + cài đặt ở `Infrastructure/Security/{PasswordHasher,JwtTokenGenerator,JwtOptions}.cs`.
- `src/Core/StoryPlatform.Domain/Entities/UserAccount.cs` đã có sẵn 2 cột `ResetTokenHash`/`ResetTokenExpiresAt` (chưa được dùng ở đâu cả).
- `JwtOptions.RefreshTokenExpiryDays` (mặc định 7) đã tồn tại trong `Infrastructure/Security/JwtOptions.cs` nhưng chưa được dùng ở đâu.
- `IJwtTokenGenerator.GenerateRefreshToken()` đã sinh được chuỗi refresh token ngẫu nhiên, nhưng **hiện tại token này không được lưu ở đâu cả** → API trả về nhưng không có cách nào dùng lại được. Đây là lỗ hổng chính cần vá trong plan này.
- `StoryPlatform.UnitTests` (xUnit) tồn tại nhưng **chưa có gói Moq** và **chưa có test nào cho Auth**.

**Chưa có, sẽ được thêm trong plan này:** làm mới token (`refresh-token`), đăng xuất (`logout`), quên mật khẩu (`forgot-password`), đặt lại mật khẩu (`reset-password`).

**Ngoài phạm vi plan này (không làm):** xác thực email (`EmailVerified`), đăng nhập bằng OAuth/Google, khoá tài khoản tự động sau N lần sai mật khẩu, đa phiên đăng nhập song song (mô hình hiện tại chỉ giữ 1 refresh token hiệu lực/tài khoản — đăng nhập ở thiết bị mới sẽ vô hiệu hoá refresh token của thiết bị cũ).

## Global Constraints

- Namespace enum Domain hiện tại là `StoryPlatform.Domain.Enums` (không phải `StoryPlatform.Domain.Entities.Enums`) — dùng đúng namespace này ở mọi file mới.
- Không tạo bảng CSDL mới ngoài DBML đã duyệt — refresh token lưu trực tiếp trên `user_accounts`.
- Băm refresh token / reset token bằng SHA-256 (không dùng BCrypt) vì cần tra cứu bằng giá trị băm y hệt (deterministic) — BCrypt sinh salt ngẫu nhiên mỗi lần nên không tra cứu được theo hash.
- Endpoint quên mật khẩu **không được tiết lộ email có tồn tại trong hệ thống hay không** (luôn trả về cùng 1 thông điệp thành công chung chung) — tránh dò quét email (user enumeration).
- Mọi response API vẫn theo đúng khuôn `ApiResponse<T>` qua `BaseApiController.HandleResult`.
- Build: `dotnet build StoryPlatform.sln`. Test: `dotnet test StoryPlatform.sln`.
- Không log/in ra token thật, mật khẩu, hay connection string dưới dạng plain text trong bất kỳ đoạn code production nào ngoài `LoggingEmailSender` (nơi log token là hành vi **thay thế tạm thời cho gửi email thật**, sẽ nêu rõ trong code bằng comment).

---

## File Structure

| File | Trạng thái | Trách nhiệm |
|---|---|---|
| `src/Core/StoryPlatform.Domain/Entities/UserAccount.cs` | Modify | Thêm 2 cột `RefreshTokenHash`, `RefreshTokenExpiresAt` |
| `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs` | Modify | Khai báo `HasMaxLength` cho cột mới |
| `src/Core/StoryPlatform.Infrastructure/Migrations/*` | Create (qua `dotnet ef migrations add`) | Migration EF Core cho 2 cột mới |
| `src/Core/StoryPlatform.Application/Common/Security/TokenHasher.cs` | Create | Hàm băm SHA-256 dùng chung cho refresh token & reset token (tránh lặp code giữa các Task 3/4/6/7) |
| `src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs` | Modify | Thêm `RefreshTokenRequestDto`, `ForgotPasswordRequestDto`, `ResetPasswordRequestDto` |
| `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs` | Modify | Thêm 4 method mới |
| `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs` | Modify | Cài đặt logic refresh/logout/forgot/reset |
| `src/Core/StoryPlatform.Application/Abstractions/Communication/IEmailSender.cs` | Create | Abstraction gửi email đặt lại mật khẩu |
| `src/Core/StoryPlatform.Infrastructure/Communication/LoggingEmailSender.cs` | Create | Cài đặt tạm thời: ghi log thay vì gửi email thật |
| `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs` | Modify | Đăng ký `IEmailSender` |
| `src/Core/StoryPlatform.Api/Controllers/AuthController.cs` | Modify | Thêm 4 action mới |
| `src/Core/StoryPlatform.Api/StoryPlatform.Api.http` | Modify | Thêm request mẫu để test tay |
| `tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj` | Modify | Thêm gói `Moq` |
| `tests/StoryPlatform.UnitTests/Common/TokenHasherTests.cs` | Create | Test cho `TokenHasher` |
| `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs` | Create | Test cho toàn bộ `AuthService` (login/register/refresh/logout/forgot/reset) |

---

### Task 1: Thêm cột lưu Refresh Token vào `user_accounts` + migration

**Files:**
- Modify: `src/Core/StoryPlatform.Domain/Entities/UserAccount.cs:20-22`
- Modify: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs:37-39`
- Create: migration mới trong `src/Core/StoryPlatform.Infrastructure/Migrations/` (sinh tự động bởi EF CLI, không tự viết tay)

**Interfaces:**
- Produces: `UserAccount.RefreshTokenHash` (`string?`), `UserAccount.RefreshTokenExpiresAt` (`DateTime?`) — Task 3, 4, 5 dùng 2 property này.

- [ ] **Step 1: Thêm 2 property mới vào `UserAccount`**

Sửa `src/Core/StoryPlatform.Domain/Entities/UserAccount.cs`, chèn ngay sau dòng `public DateTime? ResetTokenExpiresAt { get; set; }`:

```csharp
    public string? ResetTokenHash { get; set; }
    public DateTime? ResetTokenExpiresAt { get; set; }
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
```

(chỉ 2 dòng `RefreshToken...` là mới, phần còn lại giữ nguyên để định vị chỗ chèn)

- [ ] **Step 2: Khai báo ràng buộc cột mới trong EF configuration**

Sửa `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs`, chèn ngay sau khối:

```csharp
        builder.Property(u => u.ResetTokenHash)
            .HasMaxLength(255);
```

thêm:

```csharp
        builder.Property(u => u.RefreshTokenHash)
            .HasMaxLength(64); // SHA-256 hex = 64 ký tự
```

- [ ] **Step 3: Build để chắc chắn không lỗi biên dịch**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Sinh migration EF Core**

Run (từ thư mục `E:\Capstone\AI_Storytelling_Backend`):

```bash
dotnet ef migrations add AddRefreshTokenToUserAccount --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api
```

Expected: sinh ra file `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddRefreshTokenToUserAccount.cs` chỉ chứa `AddColumn` cho 2 cột `RefreshTokenHash`/`RefreshTokenExpiresAt` trên bảng `user_accounts` (không đụng bảng nào khác). Mở file migration ra đọc lại để xác nhận đúng như vậy trước khi qua bước tiếp theo.

- [ ] **Step 5: Áp dụng migration vào CSDL cục bộ**

Run:

```bash
dotnet ef database update --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api
```

Expected: `Done.`, không có lỗi. Nếu chưa có CSDL cục bộ sẵn sàng (Postgres chưa chạy / connection string chưa cấu hình), ghi chú lại và bỏ qua bước này — migration vẫn được commit, executor môi trường khác sẽ tự áp dụng.

- [ ] **Step 6: Commit**

```bash
git add src/Core/StoryPlatform.Domain/Entities/UserAccount.cs src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs src/Core/StoryPlatform.Infrastructure/Migrations/
git commit -m "feat(auth): add refresh token storage columns to user_accounts"
```

---

### Task 2: Thêm gói Moq + tiện ích `TokenHasher` (băm SHA-256 dùng chung)

**Files:**
- Modify: `tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`
- Create: `src/Core/StoryPlatform.Application/Common/Security/TokenHasher.cs`
- Test: `tests/StoryPlatform.UnitTests/Common/TokenHasherTests.cs`

**Interfaces:**
- Produces: `TokenHasher.Hash(string rawToken) -> string` (chuỗi hex 64 ký tự, lowercase) — Task 3 (refresh token), Task 4 (xác thực refresh token), Task 6 (reset token), Task 7 (xác thực reset token) đều gọi hàm này.

- [ ] **Step 1: Thêm gói Moq vào test project**

Sửa `tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`, thêm dòng sau vào trong `<ItemGroup>` chứa các `PackageReference` hiện có:

```xml
    <PackageReference Include="Moq" Version="4.20.72" />
```

Run: `dotnet restore tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`
Expected: restore thành công, không lỗi version conflict.

- [ ] **Step 2: Viết test cho `TokenHasher` (thất bại trước vì class chưa tồn tại)**

Tạo `tests/StoryPlatform.UnitTests/Common/TokenHasherTests.cs`:

```csharp
using StoryPlatform.Application.Common.Security;
using Xunit;

namespace StoryPlatform.UnitTests.Common;

public class TokenHasherTests
{
    [Fact]
    public void Hash_SameInput_ReturnsSameHash()
    {
        var hash1 = TokenHasher.Hash("abc123");
        var hash2 = TokenHasher.Hash("abc123");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_DifferentInput_ReturnsDifferentHash()
    {
        var hash1 = TokenHasher.Hash("abc123");
        var hash2 = TokenHasher.Hash("xyz789");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_ReturnsLowercase64CharHexString()
    {
        var hash = TokenHasher.Hash("sample-token");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }
}
```

- [ ] **Step 3: Chạy test, xác nhận thất bại vì thiếu class**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter TokenHasherTests`
Expected: FAIL (lỗi biên dịch — không tìm thấy `StoryPlatform.Application.Common.Security.TokenHasher`)

- [ ] **Step 4: Cài đặt `TokenHasher`**

Tạo `src/Core/StoryPlatform.Application/Common/Security/TokenHasher.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace StoryPlatform.Application.Common.Security;

/// <summary>
/// Băm một chuỗi token bất kỳ (refresh token, reset-password token) bằng SHA-256
/// để lưu trong CSDL dưới dạng không thể đảo ngược. Dùng SHA-256 (không dùng BCrypt)
/// vì các luồng refresh-token/reset-password cần TRA CỨU theo đúng giá trị băm
/// (deterministic) — BCrypt sinh salt ngẫu nhiên mỗi lần gọi nên không tra cứu được.
/// </summary>
public static class TokenHasher
{
    public static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }
        return builder.ToString();
    }
}
```

- [ ] **Step 5: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter TokenHasherTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj tests/StoryPlatform.UnitTests/Common/TokenHasherTests.cs src/Core/StoryPlatform.Application/Common/Security/TokenHasher.cs
git commit -m "test(auth): add Moq and TokenHasher SHA-256 utility"
```

---

### Task 3: Lưu Refresh Token thật khi Đăng nhập / Đăng ký

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs` (tạo mới ở task này, sẽ được bổ sung tiếp ở Task 4-7)

**Interfaces:**
- Consumes: `TokenHasher.Hash(string) -> string` (Task 2), `IUnitOfWork.Repository<UserAccount>()`, `IJwtTokenGenerator.{GenerateAccessToken,GenerateRefreshToken,GetExpirationDate,ExpiresInSeconds}`, `IPasswordHasher.{HashPassword,VerifyPassword}` (đã có sẵn).
- Produces: `AuthService.GenerateAuthResponseAsync(UserAccount, CancellationToken) -> Task<AuthResponseDto>` (thay thế method đồng bộ `GenerateAuthResponse` cũ) — Task 4 (refresh) cũng gọi lại đúng method này để xoay vòng token.

- [ ] **Step 1: Viết test thất bại cho việc Login lưu refresh token**

Tạo `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs`:

```csharp
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Auth;

public class AuthServiceTests
{
    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGeneratorMock = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>()).Returns(_userRepoMock.Object);
        _jwtGeneratorMock.Setup(j => j.GenerateAccessToken(It.IsAny<UserAccount>())).Returns("fake-access-token");
        _jwtGeneratorMock.Setup(j => j.GenerateRefreshToken()).Returns("fake-refresh-token-raw");
        _jwtGeneratorMock.Setup(j => j.GetExpirationDate()).Returns(DateTime.UtcNow.AddMinutes(120));
        _jwtGeneratorMock.Setup(j => j.ExpiresInSeconds).Returns(7200L);

        _sut = new AuthService(_unitOfWorkMock.Object, _passwordHasherMock.Object, _jwtGeneratorMock.Object);
    }

    private static UserAccount CreateUser(string password = "hashed-password") => new()
    {
        Id = 1,
        Username = "parent_demo1",
        Email = "parent1@example.com",
        PasswordHash = password,
        FullName = "Tran Thi Hoa",
        Role = UserRole.Parent,
        Status = AccountStatus.EmailVerified
    };

    [Fact]
    public async Task LoginAsync_ValidCredentials_StoresHashedRefreshTokenOnUser()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("Demo@123", user.PasswordHash)).Returns(true);

        var result = await _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" });

        Assert.Equal("fake-access-token", result.AccessToken);
        Assert.Equal("fake-refresh-token-raw", result.RefreshToken);
        Assert.NotNull(user.RefreshTokenHash);
        Assert.NotEqual("fake-refresh-token-raw", user.RefreshTokenHash);
        Assert.True(user.RefreshTokenExpiresAt > DateTime.UtcNow);
        _userRepoMock.Verify(r => r.Update(user), Times.AtLeastOnce);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsBadRequestException()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "WrongPass" }));
    }

    [Fact]
    public async Task LoginAsync_SuspendedAccount_ThrowsForbiddenException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Suspended;
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" }));
    }

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
            Password = "Demo@123"
        };

        var result = await _sut.RegisterAsync(request);

        Assert.NotNull(addedUser);
        Assert.Equal("fake-refresh-token-raw", result.RefreshToken);
        Assert.NotNull(addedUser!.RefreshTokenHash);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: FAIL — lỗi biên dịch vì `AuthResponseDto` chưa có `RefreshToken` (thực ra đã có, giữ nguyên) nhưng `user.RefreshTokenHash` chưa tồn tại khi Task 1 chưa merge, hoặc test `LoginAsync_ValidCredentials_StoresHashedRefreshTokenOnUser` FAIL vì `user.RefreshTokenHash` vẫn `null` (do `AuthService` chưa gán). Đảm bảo Task 1 đã hoàn tất trước khi chạy Task này.

- [ ] **Step 3: Sửa `AuthService` để lưu refresh token đã băm**

Trong `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`, thêm using:

```csharp
using StoryPlatform.Application.Common.Security;
```

Thay toàn bộ method `LoginAsync` hiện tại:

```csharp
    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();

        // Cho phép đăng nhập bằng cả Email hoặc Username
        var normalizedIdentifier = request.Identifier.Trim().ToLowerInvariant();
        var user = await userRepo.FirstOrDefaultAsync(
            u => u.Email.ToLower() == normalizedIdentifier || u.Username.ToLower() == normalizedIdentifier,
            cancellationToken: cancellationToken);

        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new BadRequestException("Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        if (user.Status == AccountStatus.Suspended)
        {
            throw new ForbiddenException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        user.Status = AccountStatus.LoggedIn;
        user.LastLoginAt = DateTime.UtcNow;

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }
```

Thay toàn bộ method `RegisterAsync` hiện tại (chỉ đổi dòng cuối):

```csharp
        await userRepo.AddAsync(newUser, cancellationToken);

        return await GenerateAuthResponseAsync(newUser, cancellationToken);
```

(giữ nguyên phần validate email/username trùng và khởi tạo `newUser` phía trên — chỉ thay 2 dòng cuối cùng của method, bỏ dòng `await _unitOfWork.SaveChangesAsync(cancellationToken);` cũ vì `GenerateAuthResponseAsync` sẽ lưu 1 lần duy nhất)

Thay method đồng bộ `GenerateAuthResponse` bằng bản `async` mới:

```csharp
    private async Task<AuthResponseDto> GenerateAuthResponseAsync(UserAccount user, CancellationToken cancellationToken)
    {
        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user);
        var rawRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        user.RefreshTokenHash = TokenHasher.Hash(rawRefreshToken);
        user.RefreshTokenExpiresAt = _jwtTokenGenerator.GetExpirationDate();

        // Chỉ gọi Update() cho tài khoản ĐÃ tồn tại (Id != 0, được fetch bằng
        // FirstOrDefaultAsync AsNoTracking() nên cần Attach lại thủ công).
        // Với tài khoản MỚI (RegisterAsync, Id == 0), entity đã được AddAsync() tracking
        // sẵn ở trạng thái Added — gọi Update() lúc này sẽ đổi nhầm trạng thái thành
        // Modified và khiến EF Core phát UPDATE thay vì INSERT, gây lỗi khi lưu.
        if (user.Id != 0)
        {
            _unitOfWork.Repository<UserAccount>().Update(user);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            TokenType = "Bearer",
            ExpiresInSeconds = _jwtTokenGenerator.ExpiresInSeconds,
            User = MapToUserProfileDto(user)
        };
    }
```

- [ ] **Step 4: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Build toàn bộ solution để chắc chắn không có nơi nào khác gọi `GenerateAuthResponse` (bản đồng bộ cũ)**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs
git commit -m "feat(auth): persist hashed refresh token on login and register"
```

---

### Task 4: API làm mới Access Token — `POST /api/v1/auth/refresh-token`

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`
- Modify: `src/Core/StoryPlatform.Api/Controllers/AuthController.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `TokenHasher.Hash` (Task 2), `GenerateAuthResponseAsync` (Task 3).
- Produces: `IAuthService.RefreshTokenAsync(RefreshTokenRequestDto, CancellationToken) -> Task<AuthResponseDto>`.

- [ ] **Step 1: Thêm test thất bại cho refresh token**

Thêm vào cuối class `AuthServiceTests` (trước dấu `}` cuối file):

```csharp
    [Fact]
    public async Task RefreshTokenAsync_ValidToken_RotatesRefreshTokenAndReturnsNewAccessToken()
    {
        var user = CreateUser();
        user.RefreshTokenHash = TokenHasher.Hash("old-raw-refresh-token");
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "old-raw-refresh-token" });

        Assert.Equal("fake-access-token", result.AccessToken);
        Assert.Equal("fake-refresh-token-raw", result.RefreshToken);
        Assert.Equal(TokenHasher.Hash("fake-refresh-token-raw"), user.RefreshTokenHash);
    }

    [Fact]
    public async Task RefreshTokenAsync_UnknownToken_ThrowsUnauthorizedException()
    {
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "unknown-token" }));
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ThrowsUnauthorizedException()
    {
        var user = CreateUser();
        user.RefreshTokenHash = TokenHasher.Hash("expired-raw-token");
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(-1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "expired-raw-token" }));
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại (chưa có `RefreshTokenAsync`/`RefreshTokenRequestDto`)**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: FAIL — lỗi biên dịch.

- [ ] **Step 3: Thêm DTO**

Thêm vào cuối `src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs`:

```csharp
public class RefreshTokenRequestDto
{
    [Required(ErrorMessage = "Refresh token không được để trống.")]
    public string RefreshToken { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Thêm method vào interface**

Sửa `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs`, thêm dòng:

```csharp
    Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Cài đặt `RefreshTokenAsync` trong `AuthService`**

Thêm using:

```csharp
using StoryPlatform.Application.Common.Exceptions; // đã có sẵn, chỉ cần đảm bảo UnauthorizedException nằm trong này
```

Thêm method (đặt sau `LoginAsync`):

```csharp
    public async Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default)
    {
        var hashedToken = TokenHasher.Hash(request.RefreshToken);
        var userRepo = _unitOfWork.Repository<UserAccount>();

        var user = await userRepo.FirstOrDefaultAsync(
            u => u.RefreshTokenHash == hashedToken,
            cancellationToken: cancellationToken);

        if (user == null || user.RefreshTokenExpiresAt == null || user.RefreshTokenExpiresAt < DateTime.UtcNow)
        {
            throw new UnauthorizedException("Refresh token không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.");
        }

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }
```

- [ ] **Step 6: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 7: Thêm endpoint controller**

Thêm vào `src/Core/StoryPlatform.Api/Controllers/AuthController.cs`, sau action `Login`:

```csharp
    /// <summary>
    /// Làm mới Access Token bằng Refresh Token còn hiệu lực (xoay vòng: refresh token cũ sẽ bị vô hiệu hoá)
    /// </summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> RefreshToken(
        [FromBody] RefreshTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshTokenAsync(request, cancellationToken);
        return HandleResult(result, "Làm mới token thành công.");
    }
```

- [ ] **Step 8: Build toàn bộ**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 9: Commit**

```bash
git add src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs src/Core/StoryPlatform.Api/Controllers/AuthController.cs tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs
git commit -m "feat(auth): add refresh-token endpoint with rotation"
```

---

### Task 5: API Đăng xuất — `POST /api/v1/auth/logout`

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`
- Modify: `src/Core/StoryPlatform.Api/Controllers/AuthController.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs`

**Interfaces:**
- Produces: `IAuthService.LogoutAsync(int userId, CancellationToken) -> Task`.

- [ ] **Step 1: Thêm test thất bại**

Thêm vào cuối class `AuthServiceTests`:

```csharp
    [Fact]
    public async Task LogoutAsync_ClearsRefreshTokenAndSetsLoggedOutStatus()
    {
        var user = CreateUser();
        user.RefreshTokenHash = "some-hash";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);
        user.Status = AccountStatus.LoggedIn;

        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await _sut.LogoutAsync(1);

        Assert.Null(user.RefreshTokenHash);
        Assert.Null(user.RefreshTokenExpiresAt);
        Assert.Equal(AccountStatus.LoggedOut, user.Status);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_UnknownUserId_ThrowsNotFoundException()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.LogoutAsync(999));
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: FAIL — lỗi biên dịch (chưa có `LogoutAsync`).

- [ ] **Step 3: Thêm method vào interface**

Sửa `IAuthService.cs`, thêm:

```csharp
    Task LogoutAsync(int userId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Cài đặt trong `AuthService`**

```csharp
    public async Task LogoutAsync(int userId, CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException("Tài khoản", userId);
        }

        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        user.Status = AccountStatus.LoggedOut;
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
```

- [ ] **Step 5: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: `Passed! - Failed: 0, Passed: 9`

- [ ] **Step 6: Thêm endpoint controller (yêu cầu đã đăng nhập)**

Thêm vào `AuthController.cs`, sau action `GetProfile`:

```csharp
    /// <summary>
    /// Đăng xuất — vô hiệu hoá refresh token hiện tại của tài khoản đang đăng nhập
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object?>>> Logout(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        await _authService.LogoutAsync(userId, cancellationToken);
        return HandleResult<object?>(null, "Đăng xuất thành công.");
    }
```

- [ ] **Step 7: Build toàn bộ**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs src/Core/StoryPlatform.Api/Controllers/AuthController.cs tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs
git commit -m "feat(auth): add logout endpoint that revokes refresh token"
```

---

### Task 6: Quên mật khẩu — `POST /api/v1/auth/forgot-password`

**Files:**
- Create: `src/Core/StoryPlatform.Application/Abstractions/Communication/IEmailSender.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/Communication/LoggingEmailSender.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`
- Modify: `src/Core/StoryPlatform.Api/Controllers/AuthController.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs`

**Vì sao cần file mới `IEmailSender`/`LoggingEmailSender`:** dự án chưa có hạ tầng gửi email nào. Quên mật khẩu bắt buộc phải gửi token cho người dùng qua kênh ngoài băng (out-of-band), nếu không sẽ không thể test được luồng end-to-end. Tách abstraction ở `Application` + cài đặt "chỉ log" ở `Infrastructure` đúng theo pattern `IPasswordHasher`/`IJwtTokenGenerator` đã có sẵn trong dự án — sau này thay `LoggingEmailSender` bằng SMTP/SendGrid thật mà không đổi `AuthService`.

**Interfaces:**
- Consumes: `TokenHasher.Hash` (Task 2).
- Produces: `IEmailSender.SendPasswordResetEmailAsync(string toEmail, string toName, string rawResetToken, CancellationToken) -> Task`, `IAuthService.ForgotPasswordAsync(ForgotPasswordRequestDto, CancellationToken) -> Task`.

- [ ] **Step 1: Viết test thất bại**

Thêm vào đầu class `AuthServiceTests` field mới và constructor cập nhật — **thay thế** field/constructor hiện tại:

```csharp
    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGeneratorMock = new();
    private readonly Mock<IEmailSender> _emailSenderMock = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>()).Returns(_userRepoMock.Object);
        _jwtGeneratorMock.Setup(j => j.GenerateAccessToken(It.IsAny<UserAccount>())).Returns("fake-access-token");
        _jwtGeneratorMock.Setup(j => j.GenerateRefreshToken()).Returns("fake-refresh-token-raw");
        _jwtGeneratorMock.Setup(j => j.GetExpirationDate()).Returns(DateTime.UtcNow.AddMinutes(120));
        _jwtGeneratorMock.Setup(j => j.ExpiresInSeconds).Returns(7200L);

        _sut = new AuthService(_unitOfWorkMock.Object, _passwordHasherMock.Object, _jwtGeneratorMock.Object, _emailSenderMock.Object);
    }
```

Thêm using `using StoryPlatform.Application.Abstractions.Communication;` ở đầu file test.

Thêm test vào cuối class:

```csharp
    [Fact]
    public async Task ForgotPasswordAsync_ExistingEmail_StoresHashedResetTokenAndSendsEmail()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await _sut.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "parent1@example.com" });

        Assert.NotNull(user.ResetTokenHash);
        Assert.True(user.ResetTokenExpiresAt > DateTime.UtcNow);
        _emailSenderMock.Verify(e => e.SendPasswordResetEmailAsync(
            user.Email, user.FullName, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_UnknownEmail_DoesNotThrowAndDoesNotSendEmail()
    {
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        await _sut.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "khong-ton-tai@example.com" });

        _emailSenderMock.Verify(e => e.SendPasswordResetEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: FAIL — lỗi biên dịch (`IEmailSender`, `ForgotPasswordRequestDto`, `ForgotPasswordAsync`, constructor `AuthService` 4 tham số chưa tồn tại).

- [ ] **Step 3: Tạo abstraction `IEmailSender`**

Tạo `src/Core/StoryPlatform.Application/Abstractions/Communication/IEmailSender.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace StoryPlatform.Application.Abstractions.Communication;

public interface IEmailSender
{
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string rawResetToken, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Tạo cài đặt tạm thời `LoggingEmailSender`**

Tạo `src/Core/StoryPlatform.Infrastructure/Communication/LoggingEmailSender.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Communication;

namespace StoryPlatform.Infrastructure.Communication;

/// <summary>
/// Cài đặt tạm thời của IEmailSender: dự án chưa tích hợp dịch vụ gửi email thật
/// (SMTP/SendGrid/...). Thay vì gửi email, ghi log lại nội dung để dev/QA lấy token
/// test thủ công. THAY THẾ class này bằng cài đặt gửi email thật trước khi lên production.
/// </summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string toName, string rawResetToken, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[DEV-ONLY] Email đặt lại mật khẩu cho {ToName} <{ToEmail}>. Reset token (chỉ dùng để test, KHÔNG log trong môi trường production thật): {RawResetToken}",
            toName, toEmail, rawResetToken);

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: Đăng ký DI**

Sửa `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs`, thêm using:

```csharp
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Infrastructure.Communication;
```

Thêm dòng đăng ký ngay sau `services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();`:

```csharp
        services.AddScoped<IEmailSender, LoggingEmailSender>();
```

- [ ] **Step 6: Thêm DTO**

Thêm vào `AuthDtos.cs`:

```csharp
public class ForgotPasswordRequestDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;
}
```

- [ ] **Step 7: Thêm method vào interface**

Thêm vào `IAuthService.cs`:

```csharp
    Task ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken = default);
```

- [ ] **Step 8: Cập nhật constructor và cài đặt `AuthService`**

Sửa constructor `AuthService`, thêm tham số `IEmailSender`:

```csharp
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailSender _emailSender;

    public AuthService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailSender = emailSender;
    }
```

Thêm using `using StoryPlatform.Application.Abstractions.Communication;` đầu file.

Thêm method:

```csharp
    public async Task ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken: cancellationToken);

        // Không tiết lộ email có tồn tại hay không — âm thầm bỏ qua nếu không tìm thấy.
        if (user == null)
        {
            return;
        }

        var rawResetToken = _jwtTokenGenerator.GenerateRefreshToken(); // tái dùng bộ sinh chuỗi ngẫu nhiên an toàn sẵn có
        user.ResetTokenHash = TokenHasher.Hash(rawResetToken);
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
        user.Status = AccountStatus.PasswordResetPending;
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _emailSender.SendPasswordResetEmailAsync(user.Email, user.FullName, rawResetToken, cancellationToken);
    }
```

- [ ] **Step 9: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: `Passed! - Failed: 0, Passed: 11`

- [ ] **Step 10: Thêm endpoint controller**

Thêm vào `AuthController.cs`, sau action `Login`:

```csharp
    /// <summary>
    /// Yêu cầu đặt lại mật khẩu — nếu email tồn tại, hệ thống sẽ gửi token đặt lại mật khẩu.
    /// Luôn trả về cùng 1 thông điệp bất kể email có tồn tại hay không (tránh dò quét email).
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> ForgotPassword(
        [FromBody] ForgotPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        await _authService.ForgotPasswordAsync(request, cancellationToken);
        return HandleResult<object?>(null, "Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi.");
    }
```

- [ ] **Step 11: Build toàn bộ**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 12: Commit**

```bash
git add src/Core/StoryPlatform.Application/Abstractions/Communication/IEmailSender.cs src/Core/StoryPlatform.Infrastructure/Communication/LoggingEmailSender.cs src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs src/Core/StoryPlatform.Api/Controllers/AuthController.cs tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs
git commit -m "feat(auth): add forgot-password flow with pluggable email sender"
```

---

### Task 7: Đặt lại mật khẩu — `POST /api/v1/auth/reset-password`

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs`
- Modify: `src/Core/StoryPlatform.Api/Controllers/AuthController.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `TokenHasher.Hash` (Task 2).
- Produces: `IAuthService.ResetPasswordAsync(ResetPasswordRequestDto, CancellationToken) -> Task`.

- [ ] **Step 1: Viết test thất bại**

Thêm vào cuối class `AuthServiceTests`:

```csharp
    [Fact]
    public async Task ResetPasswordAsync_ValidToken_UpdatesPasswordAndClearsTokensAndSessions()
    {
        var user = CreateUser();
        user.ResetTokenHash = TokenHasher.Hash("valid-raw-reset-token");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);
        user.RefreshTokenHash = "some-old-refresh-hash";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.HashPassword("NewPass@123")).Returns("new-hashed-password");

        await _sut.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email,
            ResetToken = "valid-raw-reset-token",
            NewPassword = "NewPass@123"
        });

        Assert.Equal("new-hashed-password", user.PasswordHash);
        Assert.Null(user.ResetTokenHash);
        Assert.Null(user.ResetTokenExpiresAt);
        Assert.Null(user.RefreshTokenHash);
        Assert.Null(user.RefreshTokenExpiresAt);
        Assert.Equal(AccountStatus.LoggedOut, user.Status);
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidToken_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.ResetTokenHash = TokenHasher.Hash("correct-token");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email,
            ResetToken = "wrong-token",
            NewPassword = "NewPass@123"
        }));
    }

    [Fact]
    public async Task ResetPasswordAsync_ExpiredToken_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.ResetTokenHash = TokenHasher.Hash("expired-token");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(-5);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email,
            ResetToken = "expired-token",
            NewPassword = "NewPass@123"
        }));
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: FAIL — lỗi biên dịch.

- [ ] **Step 3: Thêm DTO**

Thêm vào `AuthDtos.cs`:

```csharp
public class ResetPasswordRequestDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Reset token không được để trống.")]
    public string ResetToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự.")]
    public string NewPassword { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Thêm method vào interface**

Thêm vào `IAuthService.cs`:

```csharp
    Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Cài đặt trong `AuthService`**

```csharp
    public async Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken: cancellationToken);

        var hashedIncomingToken = TokenHasher.Hash(request.ResetToken);
        if (user == null || user.ResetTokenHash != hashedIncomingToken)
        {
            throw new BadRequestException("Token đặt lại mật khẩu không hợp lệ.");
        }

        if (user.ResetTokenExpiresAt == null || user.ResetTokenExpiresAt < DateTime.UtcNow)
        {
            throw new BadRequestException("Token đặt lại mật khẩu đã hết hạn. Vui lòng yêu cầu lại.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.ResetTokenHash = null;
        user.ResetTokenExpiresAt = null;
        // Vô hiệu hoá phiên đăng nhập hiện tại — buộc đăng nhập lại bằng mật khẩu mới ở mọi thiết bị.
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        user.Status = AccountStatus.LoggedOut;
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
```

- [ ] **Step 6: Chạy lại test, xác nhận pass**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter AuthServiceTests`
Expected: `Passed! - Failed: 0, Passed: 14`

- [ ] **Step 7: Thêm endpoint controller**

Thêm vào `AuthController.cs`, sau action `ForgotPassword`:

```csharp
    /// <summary>
    /// Đặt lại mật khẩu bằng token nhận được từ bước Quên mật khẩu
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> ResetPassword(
        [FromBody] ResetPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        await _authService.ResetPasswordAsync(request, cancellationToken);
        return HandleResult<object?>(null, "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại.");
    }
```

- [ ] **Step 8: Build toàn bộ**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 9: Commit**

```bash
git add src/Core/StoryPlatform.Application/Features/Auth/DTOs/AuthDtos.cs src/Core/StoryPlatform.Application/Features/Auth/Interfaces/IAuthService.cs src/Core/StoryPlatform.Application/Features/Auth/Services/AuthService.cs src/Core/StoryPlatform.Api/Controllers/AuthController.cs tests/StoryPlatform.UnitTests/Features/Auth/AuthServiceTests.cs
git commit -m "feat(auth): add reset-password endpoint and revoke sessions on reset"
```

---

### Task 8: Kiểm thử thủ công đầu-cuối + hoàn thiện

**Files:**
- Modify: `src/Core/StoryPlatform.Api/StoryPlatform.Api.http`

**Interfaces:**
- Consumes: toàn bộ 7 endpoint đã hoàn thành ở Task 3-7 (`register`, `login`, `refresh-token`, `logout`, `forgot-password`, `reset-password`, `me`).

- [ ] **Step 1: Thêm request mẫu vào file `.http` để test tay qua REST Client / Visual Studio**

Thêm vào cuối `src/Core/StoryPlatform.Api/StoryPlatform.Api.http`:

```http

### Đăng ký tài khoản mới
POST {{StoryPlatform_Api_HostAddress}}/api/v1/auth/register
Content-Type: application/json

{
  "username": "parent_test_new",
  "email": "parent_test_new@example.com",
  "fullName": "Nguyen Van Test",
  "password": "Demo@123"
}

### Đăng nhập
POST {{StoryPlatform_Api_HostAddress}}/api/v1/auth/login
Content-Type: application/json

{
  "identifier": "parent_test_new@example.com",
  "password": "Demo@123"
}

### Làm mới Access Token (thay {{refreshToken}} bằng giá trị refreshToken nhận được từ Login ở trên)
POST {{StoryPlatform_Api_HostAddress}}/api/v1/auth/refresh-token
Content-Type: application/json

{
  "refreshToken": "{{refreshToken}}"
}

### Lấy thông tin tài khoản đang đăng nhập (thay {{accessToken}} bằng accessToken nhận được từ Login)
GET {{StoryPlatform_Api_HostAddress}}/api/v1/auth/me
Authorization: Bearer {{accessToken}}

### Đăng xuất (thay {{accessToken}})
POST {{StoryPlatform_Api_HostAddress}}/api/v1/auth/logout
Authorization: Bearer {{accessToken}}

### Quên mật khẩu (kiểm tra log của API để lấy resetToken vì chưa có email server thật)
POST {{StoryPlatform_Api_HostAddress}}/api/v1/auth/forgot-password
Content-Type: application/json

{
  "email": "parent_test_new@example.com"
}

### Đặt lại mật khẩu (thay {{resetToken}} bằng giá trị lấy được từ log ở bước trên)
POST {{StoryPlatform_Api_HostAddress}}/api/v1/auth/reset-password
Content-Type: application/json

{
  "email": "parent_test_new@example.com",
  "resetToken": "{{resetToken}}",
  "newPassword": "NewDemo@456"
}
```

- [ ] **Step 2: Chạy toàn bộ test suite của solution**

Run: `dotnet test StoryPlatform.sln`
Expected: tất cả test PASS, bao gồm `AuthServiceTests` (14 test) và `TokenHasherTests` (3 test) và các test có sẵn khác (`PageRequestTests`).

- [ ] **Step 3: Build toàn bộ solution lần cuối**

Run: `dotnet build StoryPlatform.sln`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: (Nếu có Postgres cục bộ đang chạy) Chạy thử API và thực hiện tay toàn bộ chuỗi request trong file `.http`**

Run: `dotnet run --project src/Core/StoryPlatform.Api`

Mở `src/Core/StoryPlatform.Api/StoryPlatform.Api.http` trong VS/VS Code, thực hiện lần lượt từng request theo đúng thứ tự: `register` → `login` → `refresh-token` → `me` → `logout` → thử gọi lại `me` (phải bị 401 vì access token cũ vẫn còn hạn theo JWT nhưng có thể vẫn pass do access token không bị thu hồi tức thời — đây là giới hạn đã biết của JWT stateless, ghi chú lại) → `forgot-password` → xem log console lấy `resetToken` → `reset-password` → `login` lại bằng mật khẩu mới để xác nhận đổi thành công.

- [ ] **Step 5: Commit**

```bash
git add src/Core/StoryPlatform.Api/StoryPlatform.Api.http
git commit -m "test(auth): add manual .http smoke test requests for full auth flow"
```

---

## Ghi chú / giới hạn đã biết (nêu rõ để user quyết định có cần xử lý tiếp không)

0. **[Đã vá trong quá trình triển khai]** Bản kế hoạch gốc ở Task 3 vô tình tái dùng
   `GetExpirationDate()` (hàm dành cho access token) để đặt luôn hạn cho refresh token,
   khiến refresh token hết hạn cùng lúc với access token — vô hiệu hoá `RefreshTokenExpiryDays`.
   Whole-branch review cuối đã phát hiện và vá bằng `GetRefreshTokenExpirationDate()` riêng.
   Cùng đợt vá, đã bổ sung: `Suspended` giờ là trạng thái chặn tuyệt đối — không còn bị
   `forgot-password`/`reset-password`/`logout` vô tình ghi đè, và `refresh-token` cũng từ
   chối tài khoản đang bị khoá. Xem `.superpowers/sdd/2026-09-12-auth-login-register/progress.md`
   cho toàn bộ chi tiết review + fix.
1. **Chưa có index trên `RefreshTokenHash`** (cột được tra cứu ở mọi lần gọi
   `refresh-token`) — chấp nhận được ở quy mô capstone hiện tại, nhưng sẽ cần một
   migration bổ sung `HasIndex` nếu lưu lượng tăng.
2. **Access token không bị thu hồi ngay khi logout/reset password.** JWT access token là stateless — sau khi logout, access token cũ vẫn hợp lệ cho tới khi hết hạn (mặc định 120 phút, theo `JwtOptions.ExpiryMinutes`) vì hệ thống không có blacklist token. Chỉ có refresh token (dùng để lấy access token mới) bị thu hồi ngay lập tức. Đây là đánh đổi phổ biến khi dùng JWT thuần không có revocation store — nếu cần thu hồi tức thời, sẽ cần thêm bảng blacklist/token version, nằm ngoài phạm vi plan này.
3. **`LoggingEmailSender` chỉ ghi log, chưa gửi email thật.** Cần thay bằng tích hợp SMTP/SendGrid thật trước khi đưa vào môi trường có người dùng thật — đã comment rõ trong code, và nay có thêm 1 `LogWarning` lúc khởi tạo để nhắc thay thế trước khi lên production.
4. **Mô hình 1 refresh token/tài khoản** (không phải 1 refresh token/thiết bị) — đăng nhập ở thiết bị mới sẽ tự động đăng xuất thiết bị cũ. Nếu cần hỗ trợ nhiều thiết bị đồng thời, cần một bảng `refresh_tokens` riêng (nằm ngoài DBML hiện tại, cần bàn thêm trước khi làm).
