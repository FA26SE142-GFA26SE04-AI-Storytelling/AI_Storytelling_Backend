using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.DTOs;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;

namespace StoryPlatform.Api.Controllers;

public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;
    private readonly IAuditLogQueryService _auditLogQueryService;

    public AuthController(IAuthService authService, IAuditLogQueryService auditLogQueryService)
    {
        _authService = authService;
        _auditLogQueryService = auditLogQueryService;
    }

    /// <summary>
    /// Đăng ký tài khoản người dùng mới
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> Register(
        [FromBody] RegisterRequestDto request, 
        CancellationToken cancellationToken)
    {
        await _authService.RegisterAsync(request, cancellationToken);
        return HandleResult<object?>(null, "Đăng ký thành công. Vui lòng kiểm tra email để lấy mã xác thực trước khi đăng nhập.");
    }

    /// <summary>
    /// Giáo viên tạo tài khoản Phụ huynh. Phụ huynh dùng mã gửi qua email với endpoint
    /// reset-password để thiết lập mật khẩu đăng nhập lần đầu.
    /// </summary>
    [HttpPost("parents")]
    [Authorize(Roles = "Teacher")]
    public async Task<ActionResult<ApiResponse<CreatedAccountDto>>> CreateParentAccount(
        [FromBody] CreateParentAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.CreateParentAccountAsync(
            GetCurrentUserId(), request, cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<CreatedAccountDto>.Ok(result, "Tạo tài khoản phụ huynh thành công."));
    }

    /// <summary>
    /// Đăng nhập hệ thống (hỗ trợ cả Email hoặc Username)
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login(
        [FromBody] LoginRequestDto request, 
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        return HandleResult(result, "Đăng nhập thành công.");
    }

    /// <summary>
    /// Bước 2 của đăng nhập Administrator: xác thực mã TOTP (Mục 9 — MFA bắt buộc cho Administrator).
    /// </summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> VerifyMfa(
        [FromBody] VerifyMfaRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.VerifyMfaAsync(request, cancellationToken);
        return HandleResult(result, "Xác thực MFA thành công.");
    }

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

    /// <summary>
    /// Gửi lại mã xác thực email mới và vô hiệu hóa mã cũ.
    /// </summary>
    [HttpPost("resend-verification-email")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object?>>> ResendVerificationEmail(
        [FromBody] ResendVerificationEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        await _authService.ResendVerificationEmailAsync(request, cancellationToken);
        return HandleResult<object?>(
            null, "Nếu email tồn tại và chưa xác thực, mã xác thực mới đã được gửi.");
    }

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

    /// <summary>
    /// Đổi mật khẩu cho tài khoản đang đăng nhập
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object?>>> ChangePassword(
        [FromBody] ChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        await _authService.ChangePasswordAsync(userId, request, cancellationToken);
        return HandleResult<object?>(null, "Đổi mật khẩu thành công. Vui lòng đăng nhập lại.");
    }

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

    /// <summary>
    /// Lấy thông tin hồ sơ của tài khoản đang đăng nhập
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> GetProfile(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var profile = await _authService.GetCurrentUserProfileAsync(userId, cancellationToken);
        return HandleResult(profile, "Lấy thông tin tài khoản thành công.");
    }

    /// <summary>
    /// Đăng xuất — vô hiệu hoá refresh token hiện tại của tài khoản đang đăng nhập
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object?>>> Logout(
        [FromBody] LogoutRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        await _authService.LogoutAsync(userId, request, cancellationToken);
        return HandleResult<object?>(null, "Đăng xuất thành công.");
    }

    /// <summary>
    /// Đăng xuất khỏi mọi thiết bị và thu hồi toàn bộ token của tài khoản.
    /// </summary>
    [HttpPost("logout-all-devices")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object?>>> LogoutAllDevices(CancellationToken cancellationToken)
    {
        await _authService.LogoutAllDevicesAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Đã đăng xuất khỏi mọi thiết bị.");
    }

    /// <summary>
    /// Danh sách refresh-token session còn hiệu lực của tài khoản hiện tại.
    /// </summary>
    [HttpGet("sessions")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<SessionDto>>>> ListSessions(
        CancellationToken cancellationToken)
    {
        var result = await _authService.ListSessionsAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách phiên đăng nhập thành công.");
    }

    /// <summary>
    /// Thu hồi refresh token của một phiên thuộc tài khoản hiện tại.
    /// Access token đã cấp cho phiên đó vẫn có hiệu lực đến khi hết hạn.
    /// </summary>
    [HttpDelete("sessions/{id:int}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object?>>> RevokeSession(
        int id, CancellationToken cancellationToken)
    {
        await _authService.RevokeSessionAsync(GetCurrentUserId(), id, cancellationToken);
        return HandleResult<object?>(null, "Thu hồi phiên đăng nhập thành công.");
    }

    /// <summary>
    /// Lịch sử hoạt động do tài khoản hiện tại thực hiện.
    /// </summary>
    [HttpGet("me/audit-log")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> GetMyAuditLog(
        [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var result = await _auditLogQueryService.GetMyAuditLogAsync(
            GetCurrentUserId(), pageRequest, cancellationToken);
        return HandleResult(result, "Lấy lịch sử hoạt động thành công.");
    }
}
