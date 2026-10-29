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

    public Task SendEmailVerificationEmailAsync(string toEmail, string toName, string otp, CancellationToken cancellationToken = default)
    {
        const string subject = "Xác thực địa chỉ email của bạn";
        var html = $"""
            <p>Chào {toName},</p>
            <p>Cảm ơn bạn đã đăng ký tài khoản AI Storytelling Platform. Mã xác thực (OTP) email của bạn là:</p>
            <p style="font-size:22px;font-weight:bold;letter-spacing:4px;">{otp}</p>
            <p>Mã có hiệu lực trong 10 phút. Không chia sẻ mã này với bất kỳ ai. Vui lòng xác thực email trước khi đăng nhập.</p>
            """;

        return SendAsync(toEmail, subject, html, cancellationToken);
    }

    public Task SendEmailChangeOtpEmailAsync(
        string toEmail, string toName, string otp,
        CancellationToken cancellationToken = default)
    {
        const string subject = "Xác thực địa chỉ email mới";
        var html = $"""
            <p>Chào {toName},</p>
            <p>Bạn vừa yêu cầu đổi email của tài khoản AI Storytelling Platform sang địa chỉ này. Mã xác thực (OTP) của bạn là:</p>
            <p style="font-size:22px;font-weight:bold;letter-spacing:4px;">{otp}</p>
            <p>Mã có hiệu lực trong 10 phút. Không chia sẻ mã này với bất kỳ ai. Nếu bạn không yêu cầu, vui lòng bỏ qua email này.</p>
            """;

        return SendAsync(toEmail, subject, html, cancellationToken);
    }

    public Task SendAccountSecurityChangedEmailAsync(
        string toEmail, string toName, AccountSecurityChange change,
        CancellationToken cancellationToken = default)
    {
        const string subject = "Thông báo bảo mật tài khoản";
        var description = change switch
        {
            AccountSecurityChange.PasswordChanged => "mật khẩu của tài khoản vừa được thay đổi",
            AccountSecurityChange.EmailChanged => "địa chỉ email của tài khoản vừa được thay đổi",
            AccountSecurityChange.AccountDeleted => "tài khoản của bạn vừa được xóa",
            _ => "tài khoản của bạn vừa có thay đổi về bảo mật"
        };
        var html = $"""
            <p>Chào {toName},</p>
            <p>Chúng tôi thông báo {description} trên AI Storytelling Platform.</p>
            <p>Nếu không phải bạn thực hiện, vui lòng liên hệ quản trị viên ngay.</p>
            """;

        return SendAsync(toEmail, subject, html, cancellationToken);
    }

    public Task SendDataRequestCompletedEmailAsync(
        string toEmail, string toName, string requestType, string result,
        string? reason, CancellationToken cancellationToken = default)
    {
        const string subject = "Kết quả yêu cầu dữ liệu của bạn";
        var typeLabel = requestType == "delete" ? "xóa dữ liệu" : "xuất dữ liệu";
        var body = result == "rejected"
            ? $"<p>Yêu cầu {typeLabel} của bạn đã bị từ chối.</p><p>Lý do: {reason}</p>"
            : $"<p>Yêu cầu {typeLabel} của bạn đã được xử lý xong.</p>";
        var html = $"""
            <p>Chào {toName},</p>
            {body}
            <p>Đây là thông báo từ AI Storytelling Platform.</p>
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

    public Task SendStaffInviteEmailAsync(
        string toEmail, string toName, string inviterName, string roleLabel, string rawInviteToken,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(_options.StaffActivationUrl, UriKind.Absolute, out var activationUri)
            || (activationUri.Scheme != Uri.UriSchemeHttps
                && !(activationUri.Scheme == Uri.UriSchemeHttp && activationUri.IsLoopback))
            || !string.IsNullOrEmpty(activationUri.Fragment))
        {
            throw new InvalidOperationException(
                "ResendSettings:StaffActivationUrl phải là URL trang kích hoạt frontend dùng HTTPS (hoặc HTTP localhost khi phát triển).");
        }

        var activationLink = $"{activationUri.AbsoluteUri}#email={Uri.EscapeDataString(toEmail)}&inviteToken={Uri.EscapeDataString(rawInviteToken)}";
        const string subject = "Lời mời tham gia AI Storytelling Platform";
        var html = $"""
            <p>Chào {System.Net.WebUtility.HtmlEncode(toName)},</p>
            <p>{System.Net.WebUtility.HtmlEncode(inviterName)} mời bạn tham gia AI Storytelling Platform với vai trò <strong>{System.Net.WebUtility.HtmlEncode(roleLabel)}</strong>.</p>
            <p>Nhấn nút bên dưới để đặt mật khẩu và xác nhận cam kết bảo mật dữ liệu trẻ em và quy tắc kiểm duyệt:</p>
            <p><a href="{System.Net.WebUtility.HtmlEncode(activationLink)}" style="display:inline-block;padding:12px 20px;background:#2563eb;color:#fff;text-decoration:none;border-radius:6px;">Kích hoạt tài khoản</a></p>
            <p>Liên kết chỉ có hiệu lực trong 72 giờ và chỉ dùng được một lần. Nếu bạn không mong đợi email này, vui lòng bỏ qua.</p>
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
