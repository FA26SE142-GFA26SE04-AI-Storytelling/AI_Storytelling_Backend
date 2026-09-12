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
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register(
        [FromBody] RegisterRequestDto request, 
        CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        return HandleResult(result, "Đăng ký tài khoản thành công.");
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
}
