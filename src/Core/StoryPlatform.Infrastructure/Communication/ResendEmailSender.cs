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

    public Task SendSupervisionInvitationEmailAsync(string toEmail, string inviterName, string invitationCode, CancellationToken cancellationToken = default)
    {
        const string subject = "Lời mời giám sát hồ sơ trẻ";
        var html = $"""
            <p>Xin chào,</p>
            <p>{inviterName} vừa mời bạn trở thành người giám sát bổ sung cho một hồ sơ trẻ trên AI Storytelling Platform. Mã mời của bạn là:</p>
            <p style="font-size:22px;font-weight:bold;letter-spacing:2px;">{invitationCode}</p>
            <p>Vui lòng đăng nhập và dùng mã này để chấp nhận lời mời. Nếu bạn không mong đợi email này, vui lòng bỏ qua.</p>
            """;

        return SendAsync(toEmail, subject, html, cancellationToken);
    }

    public Task SendAccountProvisionedEmailAsync(
        string toEmail, string toName, string creatorName, string roleLabel, string rawSetPasswordToken,
        CancellationToken cancellationToken = default)
    {
        const string subject = "Tài khoản của bạn đã được tạo trên AI Storytelling Platform";
        var html = $"""
            <p>Chào {toName},</p>
            <p>{creatorName} vừa tạo cho bạn một tài khoản với vai trò <strong>{roleLabel}</strong> trên AI Storytelling Platform.</p>
            <p>Vui lòng dùng mã bên dưới tại trang "Đặt lại mật khẩu" để thiết lập mật khẩu đăng nhập lần đầu:</p>
            <p style="font-size:22px;font-weight:bold;letter-spacing:2px;">{rawSetPasswordToken}</p>
            <p>Mã này có hiệu lực trong 7 ngày. Nếu bạn không mong đợi email này, vui lòng bỏ qua.</p>
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
