using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Abstractions.Communication;
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
            FromName = "Test Sender",
            StaffActivationUrl = "https://app.example.com/staff-activation"
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
    public async Task SendAccountProvisionedEmailAsync_ValidRequest_PostsCorrectRequestToResendApi()
    {
        var handler = new FakeHttpMessageHandler();
        var sender = CreateSender(handler);

        await sender.SendAccountProvisionedEmailAsync(
            "admin1@example.com", "Le Thi B", "Hieu Truong", "Quản trị viên (Administrator)", "raw-set-password-token-789");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://api.resend.com/emails", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("raw-set-password-token-789", handler.LastRequestBody);
        Assert.Contains("admin1@example.com", handler.LastRequestBody);
        Assert.Contains("Hieu Truong", handler.LastRequestBody);
    }

    [Fact]
    public async Task SendStaffInviteEmailAsync_ValidRequest_PostsActivationLinkAnd72HourNotice()
    {
        var handler = new FakeHttpMessageHandler();
        var sender = CreateSender(handler);

        await sender.SendStaffInviteEmailAsync(
            "mod@example.com", "Nguyen Thi Lan", "Admin Demo",
            "Kiểm duyệt viên (Moderator)", "raw-invite-token-xyz");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("raw-invite-token-xyz", handler.LastRequestBody);
        Assert.Contains("mod@example.com", handler.LastRequestBody);
        using var payload = JsonDocument.Parse(handler.LastRequestBody!);
        var html = payload.RootElement.GetProperty("html").GetString();
        Assert.Contains("72 giờ", html);
        Assert.Contains("href=\"https://app.example.com/staff-activation#email=mod%40example.com&amp;inviteToken=raw-invite-token-xyz\"", html);
        Assert.DoesNotContain(">raw-invite-token-xyz<", html);
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
    public async Task SendAccountSecurityChangedEmailAsync_SendsToRecipient()
    {
        var handler = new FakeHttpMessageHandler();
        var sender = CreateSender(handler);

        await sender.SendAccountSecurityChangedEmailAsync(
            "owner@example.com", "An", AccountSecurityChange.PasswordChanged);

        Assert.NotNull(handler.LastRequest);
        Assert.Contains("owner@example.com", handler.LastRequestBody);
        Assert.Contains("AI Storytelling Platform", handler.LastRequestBody);
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
