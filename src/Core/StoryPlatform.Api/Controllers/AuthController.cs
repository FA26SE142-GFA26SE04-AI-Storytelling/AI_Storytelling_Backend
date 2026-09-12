using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Interfaces;

namespace StoryPlatform.Api.Controllers;

public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
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
    public async Task<ActionResult<ApiResponse<object?>>> Logout(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        await _authService.LogoutAsync(userId, cancellationToken);
        return HandleResult<object?>(null, "Đăng xuất thành công.");
    }
}
