using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Infrastructure.Storage;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class S3MediaStorageTests
{
    private static readonly byte[] ValidWebp = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 1, 2];
    private static readonly DateTime FixedNow = new(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UploadAsync_PutsValidatedObjectWithContentTypeAndReturnsKey()
    {
        var s3 = new Mock<IAmazonS3>();
        PutObjectRequest? captured = null;
        byte[]? body = null;
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((r, _) =>
            {
                captured = r;
                using var copy = new MemoryStream();
                r.InputStream.CopyTo(copy);
                body = copy.ToArray();
            })
            .ReturnsAsync(new PutObjectResponse());

        var key = await CreateStorage(s3.Object).UploadAsync("/123\\scene-1.webp", new MemoryStream(ValidWebp), "image/webp");

        Assert.Equal("123/scene-1.webp", key);
        Assert.NotNull(captured);
        Assert.Equal("story-media-test", captured.BucketName);
        Assert.Equal("123/scene-1.webp", captured.Key);
        Assert.Equal("image/webp", captured.ContentType);
        Assert.Equal(ValidWebp, body);
    }

    [Fact]
    public async Task UploadAsync_RejectsMismatchedBinaryBeforeCallingS3()
    {
        var s3 = new Mock<IAmazonS3>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateStorage(s3.Object).UploadAsync("1/a.png", new MemoryStream(ValidWebp), "image/png"));
    }

    [Theory]
    [InlineData("https://foreign.example/image.webp")]
    [InlineData("../private.webp")]
    public async Task ReadAsync_RejectsForeignUrlsAndTraversalBeforeCallingS3(string path)
    {
        var s3 = new Mock<IAmazonS3>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ArgumentException>(() => CreateStorage(s3.Object).ReadAsync(path));
    }

    [Fact]
    public async Task ReadAsync_ReturnsBytesAndMimeAfterValidation()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync("story-media-test", "123/image.webp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ObjectResponse(ValidWebp, "image/webp"));

        var result = await CreateStorage(s3.Object).ReadAsync("123/image.webp");

        Assert.Equal(ValidWebp, result.Content);
        Assert.Equal("image/webp", result.MimeType);
    }

    [Fact]
    public async Task ReadAsync_RejectsUnsupportedStoredContentType()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ObjectResponse(ValidWebp, "text/html"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateStorage(s3.Object).ReadAsync("1/a.webp"));
        Assert.Equal("UNSUPPORTED_MEDIA_TYPE", ex.Message);
    }

    [Fact]
    public async Task GetSignedUrlAsync_ReturnsCloudFrontCannedSignedUrlVerifiableWithPublicKey()
    {
        using var rsa = RSA.Create(2048);
        var storage = CreateStorage(new Mock<IAmazonS3>(MockBehavior.Strict).Object, rsa.ExportRSAPrivateKeyPem());

        var url = await storage.GetSignedUrlAsync("123/my image.webp", TimeSpan.FromHours(2));

        const string resource = "https://media.example.net/123/my%20image.webp";
        var expires = new DateTimeOffset(FixedNow.AddHours(2)).ToUnixTimeSeconds();
        Assert.StartsWith($"{resource}?Expires={expires}&Signature=", url);
        Assert.EndsWith("&Key-Pair-Id=K2TESTKEY", url);

        var signature = url.Split("&Signature=")[1].Split("&Key-Pair-Id=")[0];
        var raw = Convert.FromBase64String(signature.Replace('-', '+').Replace('_', '=').Replace('~', '/'));
        var policy = "{\"Statement\":[{\"Resource\":\"" + resource +
                     "\",\"Condition\":{\"DateLessThan\":{\"AWS:EpochTime\":" + expires + "}}}]}";
        Assert.True(rsa.VerifyData(Encoding.UTF8.GetBytes(policy), raw, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task GetSignedUrlAsync_AcceptsPemWithEscapedNewlines()
    {
        using var rsa = RSA.Create(2048);
        var escaped = rsa.ExportRSAPrivateKeyPem().Replace("\r", "").Replace("\n", "\\n");
        var url = await CreateStorage(new Mock<IAmazonS3>().Object, escaped).GetSignedUrlAsync("1/a.webp", TimeSpan.FromMinutes(10));
        Assert.Contains("&Signature=", url);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8 * 24 * 60)]
    public async Task GetSignedUrlAsync_RejectsOutOfRangeExpiry(int minutes)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateStorage(new Mock<IAmazonS3>().Object).GetSignedUrlAsync("1/a.webp", TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public async Task GetSignedUrlAsync_WithoutCdnConfigurationFailsClosed()
    {
        var storage = new S3MediaStorage(new Mock<IAmazonS3>().Object,
            new S3StorageOptions { BucketName = "story-media-test" }, NullLogger<S3MediaStorage>.Instance);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetSignedUrlAsync("1/a.webp", TimeSpan.FromMinutes(10)));
        Assert.Equal("S3_STORAGE_NOT_CONFIGURED", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_DeletesNormalizedKeyAndIgnoresNotFound()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.DeleteObjectAsync("story-media-test", "1/a.webp", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("gone") { StatusCode = HttpStatusCode.NotFound });

        await CreateStorage(s3.Object).DeleteAsync("/1/a.webp");

        s3.Verify(x => x.DeleteObjectAsync("story-media-test", "1/a.webp", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task S3Upload_AmazonClientException_IsTransient()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonClientException("network"));

        var ex = await Assert.ThrowsAsync<TransientMediaGenerationException>(() =>
            CreateStorage(s3.Object).UploadAsync("1/a.webp", new MemoryStream(ValidWebp), "image/webp"));

        Assert.Equal("MEDIA_STORAGE_UPLOAD_UNAVAILABLE", ex.ErrorCode);
    }

    [Fact]
    public async Task S3Upload_BucketNotConfigured_IsPermanent()
    {
        var storage = new S3MediaStorage(new Mock<IAmazonS3>().Object,
            new S3StorageOptions(), NullLogger<S3MediaStorage>.Instance);

        var ex = await Assert.ThrowsAsync<PermanentMediaGenerationException>(() =>
            storage.UploadAsync("1/a.webp", new MemoryStream(ValidWebp), "image/webp"));

        Assert.Equal("MEDIA_STORAGE_UPLOAD_NOT_CONFIGURED", ex.ErrorCode);
    }

    [Fact]
    public async Task S3Upload_Forbidden403_KeepsCurrentMapping()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("forbidden") { StatusCode = HttpStatusCode.Forbidden });

        var ex = await Assert.ThrowsAsync<PermanentMediaGenerationException>(() =>
            CreateStorage(s3.Object).UploadAsync("1/a.webp", new MemoryStream(ValidWebp), "image/webp"));

        Assert.Equal("MEDIA_STORAGE_UPLOAD_ACCESS_DENIED", ex.ErrorCode);
    }

    private static S3MediaStorage CreateStorage(IAmazonS3 s3, string? privateKeyPem = null)
    {
        privateKeyPem ??= DefaultKey.Value;
        return new S3MediaStorage(s3, new S3StorageOptions
        {
            BucketName = "story-media-test",
            CdnDomain = "media.example.net",
            CdnKeyPairId = "K2TESTKEY",
            CdnPrivateKey = privateKeyPem
        }, NullLogger<S3MediaStorage>.Instance, () => FixedNow);
    }

    private static readonly Lazy<string> DefaultKey = new(() =>
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    });

    private static GetObjectResponse ObjectResponse(byte[] bytes, string contentType)
    {
        var response = new GetObjectResponse { ResponseStream = new MemoryStream(bytes), ContentLength = bytes.Length };
        response.Headers.ContentType = contentType;
        return response;
    }
}
