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

        byte[] validWebp = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x01, 0x02];
        using var content = new MemoryStream(validWebp);
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
        Assert.Equal(validWebp, capturedBody);
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

    [Fact]
    public async Task UploadAsync_RejectsMismatchedMagicBytesForDeclaredMime()
    {
        var storage = CreateStorage(_ => throw new InvalidOperationException("HTTP should not be called"));

        // Content is invalid / dummy bytes, declared MIME is image/png
        using var dummyContent = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.UploadAsync(
            "123/image.png", dummyContent, "image/png"));

        // Content is valid WebP, but declared MIME is audio/wav
        byte[] webpBytes = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 0x01, 0x02];
        using var webpContent = new MemoryStream(webpBytes);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.UploadAsync(
            "123/audio.wav", webpContent, "audio/wav"));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D }, "image/png")]
    // JPEG: SOI + APP0 marker + minimal JFIF-style padding so the validator accepts it.
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 }, "image/jpeg")]
    public async Task UploadAsync_AcceptsValidImageBytesForMatchingMime(byte[] payload, string mime)
    {
        var storage = CreateStorage(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}")));

        using var content = new MemoryStream(payload);
        var result = await storage.UploadAsync("123/img.bin", content, mime);

        Assert.Equal("123/img.bin", result);
    }

    [Theory]
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, "audio/mpeg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 }, "audio/wav")]
    public async Task UploadAsync_AcceptsValidAudioBytesForMatchingMime(byte[] payload, string mime)
    {
        var storage = CreateStorage(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}")));

        using var content = new MemoryStream(payload);
        var result = await storage.UploadAsync("123/audio.bin", content, mime);

        Assert.Equal("123/audio.bin", result);
    }

    [Fact]
    public async Task UploadAsync_RejectsGarbageBytesDeclaredAsAudio()
    {
        var storage = CreateStorage(_ => throw new InvalidOperationException("HTTP should not be called"));

        using var garbage = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.UploadAsync("123/audio.mp3", garbage, "audio/mpeg"));
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
