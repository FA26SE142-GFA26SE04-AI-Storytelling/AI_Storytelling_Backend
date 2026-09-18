# Kế hoạch triển khai Supabase cho Media

**Dự án:** AI Storytelling - Phase 5 Media Generation
**Phiên bản:** 1.1 — 2026-09-15 (Đã cập nhật theo architecture review)
**Trạng thái:** Planning

---

## Mục lục

1. [Tổng quan kiến trúc](#1-tổng-quan-kiến-trúc)
2. [Storage Design](#2-storage-design)
3. [Backend Integration](#3-backend-integration)
4. [Provider Refactor](#4-provider-refactor)
5. [Migration Plan](#5-migration-plan)
6. [Testing & Rollback](#6-testing--rollback)

---

## 1. Tổng quan kiến trúc

### 1.1 Quyết định đã chốt

| Quyết định | Giá trị |
|------------|----------|
| Bucket | **1 private bucket** `story-media` |
| URL Strategy | Lưu `StoragePath` → sinh **signed URL** khi reader cần |
| HTTP Client | **Typed HttpClient** qua Supabase Storage REST API |
| Realtime | **P2/Optional** - không trong đường găng MVP |
| Edge Functions | **P2/Optional** - không trong đường găng MVP |

### 1.2 High-level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    ASP.NET Core Backend                          │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐ │
│  │ MediaGeneration │  │   AI Providers  │  │ MediaStorage    │ │
│  │    Service     │  │  (Refactored)   │  │ (HttpClient)    │ │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │                      │
                              ▼                      ▼
                    ┌─────────────────┐     ┌─────────────────┐
                    │  Gemini API     │     │ Supabase Storage│
                    │ (Bytes+MIME)   │     │ (Private Bucket)│
                    └─────────────────┘     └─────────────────┘
```

### 1.3 Media Flow (MVP)

```
1. Worker poll job
       │
       ▼
2. AI Provider GenerateAsync() → returns Stream/Bytes + MIME
       │
       ▼
3. MediaStorage.UploadAsync(path, stream, mimeType) → returns StoragePath
       │
       ▼
4. Save StoragePath to MediaAsset.Url
       │
       ▼
5. [P2] Frontend request signed URL when reader opens story
```

---

## 2. Storage Design

### 2.1 Bucket Configuration

```sql
-- Chỉ 1 bucket private
INSERT INTO storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
VALUES 
  ('story-media', 'story-media', false, 104857600, ARRAY[
    'image/jpeg', 'image/png', 'image/webp',
    'audio/mpeg', 'audio/wav', 'audio/ogg'
  ]);

-- Policy: chỉ service role được upload
CREATE POLICY "Service upload" ON storage.objects
  FOR INSERT WITH CHECK (bucket_id = 'story-media');

-- Policy: service role được delete (cho cleanup)
CREATE POLICY "Service delete" ON storage.objects
  FOR DELETE USING (bucket_id = 'story-media');

-- KHÔNG tạo public read policy - bucket private
```

### 2.2 StoragePath Convention

```
story-media/
  {storyId}/
    cover.{ext}
    scene-{sceneIndex}.{ext}
    audio-{sceneIndex}.{ext}
```

| File | StoragePath | MIME |
|------|-------------|------|
| Cover | `123/cover.jpg` | `image/jpeg` |
| Scene illustration | `123/scene-1.webp` | `image/webp` |
| Scene audio | `123/audio-1.mp3` | `audio/mpeg` |

### 2.3 Signed URL Generation

```csharp
// Khi reader cần đọc media
public async Task<string> GetSignedUrlAsync(string storagePath, TimeSpan expiry)
{
    // Supabase Storage REST API: POST /storage/v1/object/sign/{path}
    var response = await _httpClient.PostAsJsonAsync(
        $"/storage/v1/object/sign/{storagePath}",
        new { expiresIn = (int)expiry.TotalSeconds });
    
    return $"{_baseUrl}{response.SignedUrl}";
}
```

### 2.4 Delete Strategy (SAFE)

```csharp
// KHÔNG dùng prefix delete - nguy hiểm với versioning/retry
// Chỉ delete exact path khi MediaAsset bị xóa

public async Task DeleteMediaAsync(string storagePath)
{
    // Exact path delete
    await _httpClient.DeleteAsync($"/storage/v1/object/{storagePath}");
}

// Lifecycle cleanup: worker chạy định kỳ xóa orphan files
```

---

## 3. Backend Integration

### 3.1 IMediaStorage Interface (Tổng quát)

```csharp
namespace StoryPlatform.Application.Features.MediaStorage.Interfaces;

public interface IMediaStorage
{
    /// <summary>
    /// Upload file lên Supabase Storage
    /// </summary>
    /// <param name="storagePath">Path trong bucket (VD: "123/cover.jpg")</param>
    /// <param name="content">File content</param>
    /// <param name="mimeType">Content type</param>
    /// <param name="cancellationToken"></param>
    /// <returns>StoragePath đã lưu</returns>
    Task<string> UploadAsync(
        string storagePath, 
        Stream content, 
        string mimeType, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sinh signed URL tạm thời để đọc file
    /// </summary>
    Task<string> GetSignedUrlAsync(string storagePath, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>
    /// Xóa file theo exact path
    /// </summary>
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}
```

### 3.2 SupabaseStorage Implementation (Typed HttpClient)

```csharp
namespace StoryPlatform.Infrastructure.Storage;

public sealed class SupabaseStorageOptions
{
    public const string SectionName = "Supabase:Storage";
    public string Url { get; set; } = string.Empty;
    public string ServiceKey { get; set; } = string.Empty;  // sb_secret_...
}

public sealed class SupabaseStorageHttpClientHandler : DelegatingHandler
{
    private readonly string _serviceKey;

    public SupabaseStorageHttpClientHandler(string serviceKey)
    {
        _serviceKey = serviceKey;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);
        return await base.SendAsync(request, cancellationToken);
    }
}

public sealed class SupabaseMediaStorage : IMediaStorage
{
    private readonly HttpClient _httpClient;
    private readonly string _bucket = "story-media";
    private readonly ILogger<SupabaseMediaStorage> _logger;

    public SupabaseMediaStorage(
        HttpClient httpClient,
        ILogger<SupabaseMediaStorage> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        string storagePath,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var url = $"/storage/v1/object/{_bucket}/{storagePath}";
        
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Uploaded media to {Path}", storagePath);
        return storagePath;
    }

    public async Task<string> GetSignedUrlAsync(
        string storagePath, 
        TimeSpan expiry, 
        CancellationToken cancellationToken = default)
    {
        var url = $"/storage/v1/object/sign/{_bucket}/{storagePath}";
        
        var response = await _httpClient.PostAsJsonAsync(
            url,
            new { expiresIn = (int)expiry.TotalSeconds },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<SignedUrlResponse>(cancellationToken);
        return $"{_httpClient.BaseAddress}{result!.Url.TrimStart('/')}";
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var url = $"/storage/v1/object/{_bucket}/{storagePath}";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);
        
        // 404 = đã xóa hoặc không tồn tại → OK
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }
}

internal record SignedUrlResponse(string url);
```

### 3.3 Dependency Injection (Scoped, not Singleton)

```csharp
// Trong DependencyInjection.cs

services.AddHttpClient<SupabaseMediaStorage>(client =>
{
    var options = configuration.GetSection(SupabaseStorageOptions.SectionName);
    client.BaseAddress = new Uri(options.GetValue<string>("Url") + "/storage/v1");
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    var options = configuration.GetSection(SupabaseStorageOptions.SectionName)
        .Get<SupabaseStorageOptions>()!;
    return new SupabaseStorageHttpClientHandler(options.ServiceKey);
});

services.AddScoped<IMediaStorage, SupabaseMediaStorage>();
```

### 3.4 Configuration

```json
{
  "Supabase": {
    "Storage": {
      "Url": "https://xxxxxxxxxxxx.supabase.co",
      "ServiceKey": "sb_secret_xxxxxxxxxxxx..."
    }
  }
}
```

---

## 4. Provider Refactor

### 4.1 Current Contract (cần thay đổi)

```csharp
// Hiện tại - trả URL
public interface IImageGenerationProvider
{
    Task<GeneratedIllustration> GenerateAsync(SceneSpecification spec, CancellationToken ct);
}

public record GeneratedIllustration(string Url);  // Temporary URL
```

### 4.2 New Contract (Bytes + MIME)

```csharp
// Refactored - trả stream/bytes trực tiếp
public interface IImageGenerationProvider
{
    Task<GeneratedMedia> GenerateAsync(SceneSpecification spec, CancellationToken ct);
}

public interface ITtsProvider
{
    Task<GeneratedMedia> GenerateAsync(string text, CancellationToken ct);
}

public sealed record GeneratedMedia(
    Stream Content,
    string MimeType,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public GeneratedMedia(byte[] bytes, string mimeType, Dictionary<string, string>? metadata = null)
        : this(new MemoryStream(bytes), mimeType, metadata) { }

    public string SuggestedExtension => MimeType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "audio/mpeg" => ".mp3",
        "audio/wav" => ".wav",
        "audio/ogg" => ".ogg",
        _ => ".bin"
    };
}
```

### 4.3 MediaGenerationService Integration

```csharp
public class MediaGenerationService
{
    private readonly IMediaStorage _mediaStorage;

    private async Task EnsureIllustrationAsync(...)
    {
        // Generate - nhận bytes + mime trực tiếp
        var illustration = await _imageProvider.GenerateAsync(specification, ct);
        
        // Upload lên Supabase
        var storagePath = $"{job.StoryId}/scene-{scene.SceneIndex}{illustration.SuggestedExtension}";
        await _mediaStorage.UploadAsync(
            storagePath, 
            illustration.Content, 
            illustration.MimeType, 
            ct);
        
        // Lưu path (không phải URL)
        asset.Url = storagePath;
    }
}
```

---

## 5. Migration Plan

### Phase 1: Storage Setup (Day 1)
- [ ] Tạo Supabase project
- [ ] Tạo bucket `story-media` (private)
- [ ] Configure storage policies
- [ ] Test upload/delete với curl

### Phase 2: Infrastructure (Day 1-2)
- [ ] Cài đặt Microsoft.Extensions.Http (đã có)
- [ ] Tạo `SupabaseStorageHttpClientHandler`
- [ ] Tạo `SupabaseMediaStorage` class
- [ ] Tạo `IMediaStorage` interface
- [ ] Configure DI

### Phase 3: Provider Refactor (Day 2-3)
- [ ] Thêm `GeneratedMedia` record
- [ ] Update `IImageGenerationProvider`
- [ ] Update `ITtsProvider`
- [ ] Update existing provider implementations
- [ ] Update `MediaGenerationService`

### Phase 4: Integration & Testing (Day 3-4)
- [ ] Unit tests cho SupabaseMediaStorage
- [ ] Integration tests
- [ ] Test signed URL generation
- [ ] Test signed URL expiry

### Phase 5: Cleanup (Day 4)
- [ ] Remove old temporary URL handling
- [ ] Remove DownloadBytesAsync (không cần nữa)
- [ ] Update documentation

---

## 6. Testing & Rollback

### 6.1 Unit Tests

```csharp
[Fact]
public async Task UploadAsync_ValidContent_ReturnsStoragePath()
{
    // Arrange
    var handler = new MockHttpMessageHandler(json("{}"));
    var storage = new SupabaseMediaStorage(
        new HttpClient(handler) { BaseAddress = new Uri("https://test.supabase.co/storage/v1") },
        Mock.Of<ILogger<SupabaseMediaStorage>>());

    using var content = new MemoryStream(new byte[] { 1, 2, 3 });

    // Act
    var result = await storage.UploadAsync("123/cover.jpg", content, "image/jpeg");

    // Assert
    Assert.Equal("123/cover.jpg", result);
}

[Fact]
public async Task GetSignedUrlAsync_ReturnsSignedUrl()
{
    // Arrange
    var json = """{"url": "/storage/v1/object/sign/story-media/123/cover.jpg?token=abc"}""";
    var handler = new MockHttpMessageHandler(json);
    var storage = new SupabaseMediaStorage(
        new HttpClient(handler) { BaseAddress = new Uri("https://test.supabase.co") },
        Mock.Of<ILogger<SupabaseMediaStorage>>());

    // Act
    var result = await storage.GetSignedUrlAsync("123/cover.jpg", TimeSpan.FromMinutes(5));

    // Assert
    Assert.Contains("token=abc", result);
}
```

### 6.2 Rollback Plan

| Failure | Rollback |
|---------|----------|
| Supabase Storage down | Disable media worker, stories remain Approved |
| Upload fails | Retry với exponential backoff |
| Signed URL fails | Log error, return 503 |
| Migration issues | Revert code, Supabase bucket vẫn tồn tại |

### 6.3 What NOT to do

- ❌ Không dùng prefix delete (`{storyId}/`)
- ❌ Không tạo public read policy
- ❌ Không dùng full Supabase NuGet package
- ❌ Không implement Realtime trong MVP
- ❌ Không implement Edge Functions trong MVP
- ❌ Không lưu public URL vào database

---

## 7. Files to Create/Modify

### Create
- `src/Core/StoryPlatform.Application/Features/MediaStorage/Interfaces/IMediaStorage.cs`
- `src/Core/StoryPlatform.Application/Features/MediaStorage/Models/GeneratedMedia.cs`
- `src/Core/StoryPlatform.Infrastructure/Storage/SupabaseMediaStorage.cs`
- `src/Core/StoryPlatform.Infrastructure/Storage/SupabaseStorageHttpClientHandler.cs`

### Modify
- `src/Core/StoryPlatform.Application/Features/MediaGeneration/Interfaces/IImageGenerationProvider.cs`
- `src/Core/StoryPlatform.Application/Features/MediaGeneration/Interfaces/ITtsProvider.cs`
- `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs`
- `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs`
- `src/Core/StoryPlatform.Api/appsettings.json`

---

**Document Status:** Updated v1.1 - Phù hợp với architecture review
**Reviewed:** 2026-09-15
**Next Steps:** Bắt đầu Phase 1 (Storage Setup)
