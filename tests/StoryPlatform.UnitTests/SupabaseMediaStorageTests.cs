using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.Infrastructure.Storage;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class SupabaseMediaStorageTests
{
    [Fact]
    public async Task UploadAsync_SendsPrivateBackendRequestAndReturnsStoragePath()
    {
        HttpRequestMessage? captured = null;
        byte[]? capturedBody = null;
        var storage = CreateStorage(async request =>
        {
            captured = request;
            capturedBody = await request.Content!.ReadAsByteArrayAsync();
            return JsonResponse(HttpStatusCode.OK, "{}");
        });

        using var content = new MemoryStream([1, 2, 3]);
        var result = await storage.UploadAsync(
            "123/scene-1.webp",
            content,
            "image/webp");

        Assert.Equal("123/scene-1.webp", result);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("https://project.supabase.co/storage/v1/object/story-media/123/scene-1.webp", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("test-secret", Assert.Single(captured.Headers.GetValues("apikey")));
        Assert.Equal("true", Assert.Single(captured.Headers.GetValues("x-upsert")));
        Assert.Equal("image/webp", captured.Content!.Headers.ContentType!.MediaType);
        Assert.Equal([1, 2, 3], capturedBody);
        Assert.Null(captured.Headers.Authorization);
        Assert.True(content.CanRead);
    }

    [Theory]
    [InlineData("/object/sign/story-media/123/scene-1.webp?token=abc")]
    [InlineData("https://project.supabase.co/storage/v1/object/sign/story-media/123/scene-1.webp?token=abc")]
    public async Task GetSignedUrlAsync_ReturnsAbsolutePrivateUrl(string returnedSignedUrl)
    {
        var storage = CreateStorage(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            $"{{\"signedURL\":\"{returnedSignedUrl}\"}}")));

        var result = await storage.GetSignedUrlAsync("123/scene-1.webp", TimeSpan.FromMinutes(5));

        Assert.Equal(
            "https://project.supabase.co/storage/v1/object/sign/story-media/123/scene-1.webp?token=abc",
            result);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_IsIdempotent()
    {
        var storage = CreateStorage(_ => Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{}")));

        await storage.DeleteAsync("123/audio-1.mp3");
    }

    [Fact]
    public async Task DeleteAsync_SendsOnlyTheExactObjectPath()
    {
        string? body = null;
        Uri? requestUri = null;
        var storage = CreateStorage(async request =>
        {
            requestUri = request.RequestUri;
            body = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        await storage.DeleteAsync("123/audio-1.mp3");

        Assert.Equal("https://project.supabase.co/storage/v1/object/story-media", requestUri!.AbsoluteUri);
        Assert.Contains("\"123/audio-1.mp3\"", body);
        Assert.DoesNotContain("123/\"", body);
    }

    [Theory]
    [InlineData("../other/file.png")]
    [InlineData("123/file.png?token=bad")]
    [InlineData("123/file.png#fragment")]
    public async Task Operations_RejectUnsafePaths(string path)
    {
        var storage = CreateStorage(_ => throw new InvalidOperationException("HTTP should not be called"));

        await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(path));
    }

    [Fact]
    public async Task UploadAsync_RejectsMimeOutsideBucketAllowList()
    {
        var storage = CreateStorage(_ => throw new InvalidOperationException("HTTP should not be called"));

        await Assert.ThrowsAsync<ArgumentException>(() => storage.UploadAsync(
            "123/file.svg", new MemoryStream([1]), "image/svg+xml"));
    }

    private static SupabaseMediaStorage CreateStorage(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        var options = new SupabaseStorageOptions
        {
            Url = "https://project.supabase.co",
            SecretKey = "test-secret",
            Bucket = "story-media"
        };
        var authHandler = new SupabaseStorageHttpClientHandler(options)
        {
            InnerHandler = new StubHandler(responder)
        };
        return new SupabaseMediaStorage(
            new HttpClient(authHandler),
            options,
            NullLogger<SupabaseMediaStorage>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
