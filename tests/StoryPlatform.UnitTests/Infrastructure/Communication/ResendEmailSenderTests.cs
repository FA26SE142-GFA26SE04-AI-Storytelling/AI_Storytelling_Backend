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
