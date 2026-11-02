# Tóm tắt cuối truyện gửi bố mẹ — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cuối truyện, trẻ gửi cho bố mẹ một bản ghi âm tóm tắt và/hoặc các cảnh còn thắc mắc (đánh cờ); chủ hồ sơ nghe/xem lại và xóa được.

**Architecture:** Một bảng `story_summaries` (1 dòng cho mỗi phiên đọc) và một bảng `story_summary_flags` (các cảnh trẻ đánh cờ). File ghi âm lưu qua `IMediaStorage` hiện có (Supabase), phụ huynh nghe bằng signed URL có hạn. Trẻ gửi qua controller mới dưới policy `ChildSession`; phụ huynh xem/xóa qua controller mới role `User`, chỉ chủ hồ sơ (`IChildProfileAccessGuard.EnsureOwnerAsync`). Không dùng AI.

**Tech Stack:** .NET 10, EF Core 8.0.13 + Npgsql, xUnit + Moq, Supabase Storage (`IMediaStorage`).

**Spec:** Drive — "Luồng 3 — Danh sách tương tác của trẻ" (22/10/2026), mục 6 "Tóm tắt cuối truyện"; quyết định của người dùng ngày 02/11/2026 (ghi ở Global Constraints).

## Global Constraints

- Cả hai cách (ghi âm, đánh cờ) đều tùy chọn; không ảnh hưởng EXP, quiz, Hold Mode; không ghi telemetry lỗi khi bỏ qua (bỏ qua = không gọi API).
- Không dùng AI để phân tích bản ghi âm hay cờ.
- File ghi âm **lưu cho tới khi phụ huynh xóa** (không tự hết hạn).
- **Chỉ chủ hồ sơ** (`ChildProfile.OwnerUserId`) xem/nghe/xóa.
- Phụ huynh **xóa được** (xóa file trong storage + xóa mềm dòng dữ liệu).
- **Không** đổi consent / `ConsentPolicy.CurrentVersion` (quyết định của người dùng).
- Thông báo cho chủ hồ sơ khi con gửi: `NotificationType.StorySummarySent` (nhóm `children`).
- Ghi âm chỉ cho phép khi `SafetyPolicy.VoiceAnswersEnabled = true` (cùng công tắc micro với luyện đọc); đánh cờ luôn được.
- Mỗi phiên đọc: gửi ghi âm tối đa 1 lần, gửi cờ tối đa 1 lần (gửi lại → 409).
- Ghi âm tối đa 3 000 000 byte; định dạng theo `ReadAloudPolicy.AudioContentTypes`.
- Đánh cờ 1–5 cảnh, mỗi cảnh phải thuộc `StoryVersionId` của phiên.
- Phiên phải thuộc trẻ, không bị ngắt (`ForceExited` / `ForceExitRequestedAt`), trạng thái đang đọc hoặc `Completed`.
- Lệnh build/test: `dotnet build StoryPlatform.slnx`, `dotnet test StoryPlatform.slnx`.

---

## File Structure

| File | Trách nhiệm | Task |
|---|---|---|
| Create `src/Core/StoryPlatform.Domain/Entities/StorySummary.cs` | Entity bản tóm tắt của một phiên đọc | 1 |
| Create `src/Core/StoryPlatform.Domain/Entities/StorySummaryFlag.cs` | Entity một cảnh được đánh cờ | 1 |
| Modify `src/Core/StoryPlatform.Domain/Enums/NotificationType.cs` | Thêm `StorySummarySent = 31` | 1 |
| Modify `src/Core/StoryPlatform.Application/Features/Notifications/NotificationGroups.cs` | Xếp `StorySummarySent` vào nhóm `children` | 1 |
| Create `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/StorySummaryConfiguration.cs` | Bảng `story_summaries`, `story_summary_flags`, index | 1 |
| Modify `src/Core/StoryPlatform.Infrastructure/Persistence/ApplicationDbContext.cs` | 2 `DbSet` | 1 |
| Create migration `<timestamp>_AddStorySummaries` (+ Designer) | Tạo 2 bảng | 1 |
| Create `tests/StoryPlatform.UnitTests/StorySummaryModelTests.cs` | Test model EF | 1 |
| Create `src/Core/StoryPlatform.Application/Features/ReadingSessions/StorySummaryPolicy.cs` | Hằng số, thông điệp, đuôi file | 2 |
| Create `src/Core/StoryPlatform.Application/Features/ReadingSessions/DTOs/StorySummaryDtos.cs` | DTO trẻ + phụ huynh | 2, 3 |
| Create `.../ReadingSessions/Interfaces/IChildStorySummaryService.cs`, `.../Services/ChildStorySummaryService.cs` | Trẻ gửi ghi âm / cờ | 2 |
| Create `src/Core/StoryPlatform.Api/Controllers/ChildStorySummaryController.cs` | API phía trẻ | 2 |
| Create `.../ReadingSessions/Interfaces/IParentStorySummaryService.cs`, `.../Services/ParentStorySummaryService.cs` | Phụ huynh xem / xóa / dọn khi xóa tài khoản | 3, 4 |
| Create `src/Core/StoryPlatform.Api/Controllers/ParentStorySummaryController.cs` | API phía phụ huynh | 3 |
| Modify `src/Core/StoryPlatform.Application/DependencyInjection.cs` | Đăng ký 2 service | 2, 3 |
| Modify `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ReadingHarness.cs` | Thêm list `Summaries`, `SummaryFlags` | 2 |
| Create `tests/.../ReadingSessions/ChildStorySummaryServiceTests.cs`, `ParentStorySummaryServiceTests.cs` | Test service | 2, 3 |
| Modify `src/Core/StoryPlatform.Application/Features/Auth/Services/AccountDeletionService.cs` + test | Xóa file ghi âm khi xóa tài khoản | 4 |
| Modify `docs/API_FRONTEND_ACTOR_FLOWS.md` | Tài liệu 4 endpoint mới | 3 |

---

### Task 1: Mô hình dữ liệu và loại thông báo

**Việc này làm gì (nói dễ hiểu):**
Tạo chỗ lưu cho tính năng: mỗi lần con đọc xong một truyện có thể có một "bản tóm tắt" gồm file ghi âm (nếu con nói) và danh sách cảnh con còn thắc mắc. Thêm một loại thông báo để báo cho bố mẹ khi con gửi. Chưa có API nào ở task này.

**Cần thêm / còn thiếu:**
- Đã có: `ReadingSession`, `StoryScene`, `ChildProfile`, `Story`; `NotificationType` (giá trị cuối `StoryWishExpiring = 30`); `NotificationGroups.For`; cột `notifications.Type` dạng chuỗi tối đa 40 ký tự; test tự kiểm độ dài (`Notification_type_column_fits_every_enum_name`) và chuỗi migration (`MigrationChainSchemaTests`).
- Cần thêm: entity `StorySummary`, `StorySummaryFlag`; bảng `story_summaries` (unique 1 dòng còn sống / phiên), `story_summary_flags` (unique cảnh trong một bản); `NotificationType.StorySummarySent = 31` thuộc nhóm `children`; migration `AddStorySummaries`.
- Còn thiếu / ngoài phạm vi: cập nhật `Database/ai_storytelling_platform.dbml` (tài liệu thiết kế) — làm ở Task 3 cùng tài liệu.

**Files:**
- Create: `src/Core/StoryPlatform.Domain/Entities/StorySummary.cs`
- Create: `src/Core/StoryPlatform.Domain/Entities/StorySummaryFlag.cs`
- Modify: `src/Core/StoryPlatform.Domain/Enums/NotificationType.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Notifications/NotificationGroups.cs:32-33` (dòng `StoryWishExpiring` hiện ở 33)
- Create: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/StorySummaryConfiguration.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/Persistence/ApplicationDbContext.cs` (cạnh `DbSet<StoryWish>` dòng 29)
- Create: `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddStorySummaries.cs` + `.Designer.cs` (do `dotnet ef` sinh)
- Test: `tests/StoryPlatform.UnitTests/StorySummaryModelTests.cs` (mới), `tests/StoryPlatform.UnitTests/Features/Notifications/NotificationGroupsTests.cs`

**Interfaces:**
- Consumes: không.
- Produces:
  - `StoryPlatform.Domain.Entities.StorySummary` với các property: `int ReadingSessionId`, `int ChildProfileId`, `int StoryId`, `int? StoryVersionId`, `string? AudioStoragePath`, `string? AudioContentType`, `int? AudioBytes`, `DateTime? RecordingSentAt`, `DateTime? FlagsSentAt` (+ `BaseEntity`).
  - `StoryPlatform.Domain.Entities.StorySummaryFlag` với `int StorySummaryId`, `int StorySceneId` (+ `BaseEntity`).
  - `NotificationType.StorySummarySent = 31`.

- [ ] **Step 1: Viết test thất bại**

Create `tests/StoryPlatform.UnitTests/StorySummaryModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class StorySummaryModelTests
{
    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    [Fact]
    public void Story_summary_maps_to_its_table_with_one_live_row_per_reading_session()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(StorySummary))!;

        Assert.Equal("story_summaries", entity.GetTableName());
        var index = Assert.Single(entity.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "ReadingSessionId" }));
        Assert.True(index.IsUnique);
        Assert.Equal("\"IsDeleted\" = false", index.GetFilter());
        Assert.Equal(500, entity.FindProperty(nameof(StorySummary.AudioStoragePath))!.GetMaxLength());
    }

    [Fact]
    public void Story_summary_flag_is_unique_per_summary_and_scene()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(StorySummaryFlag))!;

        Assert.Equal("story_summary_flags", entity.GetTableName());
        var index = Assert.Single(entity.GetIndexes(), x => x.IsUnique);
        Assert.Equal(new[] { "StorySummaryId", "StorySceneId" }, index.Properties.Select(p => p.Name));
    }
}
```

Trong `tests/StoryPlatform.UnitTests/Features/Notifications/NotificationGroupsTests.cs`, ngay sau dòng `[InlineData(NotificationType.StoryWishExpiring, "children")]` thêm:

```csharp
    [InlineData(NotificationType.StorySummarySent, "children")]
```

- [ ] **Step 2: Chạy test để xác nhận thất bại**

Run: `dotnet build tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`
Expected: FAIL biên dịch: `The type or namespace name 'StorySummary' could not be found` và `'NotificationType' does not contain a definition for 'StorySummarySent'`.

- [ ] **Step 3: Tạo entity**

`src/Core/StoryPlatform.Domain/Entities/StorySummary.cs`:

```csharp
namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Tóm tắt cuối truyện trẻ gửi bố mẹ (Luồng 3 — Danh sách tương tác của trẻ, mục 6). Một dòng cho mỗi phiên đọc;
/// có bản ghi âm và/hoặc các cảnh trẻ đánh cờ. File lưu tới khi chủ hồ sơ xóa. Không phân tích bằng AI.
/// </summary>
public class StorySummary : BaseEntity
{
    public int ReadingSessionId { get; set; }
    public virtual ReadingSession? ReadingSession { get; set; }

    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int? StoryVersionId { get; set; }

    /// <summary>Đường dẫn trong storage; null khi chưa ghi âm hoặc đã xóa.</summary>
    public string? AudioStoragePath { get; set; }
    public string? AudioContentType { get; set; }
    public int? AudioBytes { get; set; }
    public DateTime? RecordingSentAt { get; set; }

    public DateTime? FlagsSentAt { get; set; }
}
```

`src/Core/StoryPlatform.Domain/Entities/StorySummaryFlag.cs`:

```csharp
namespace StoryPlatform.Domain.Entities;

/// <summary>Một cảnh trẻ đánh cờ "còn thắc mắc" trong bản tóm tắt cuối truyện.</summary>
public class StorySummaryFlag : BaseEntity
{
    public int StorySummaryId { get; set; }
    public virtual StorySummary? StorySummary { get; set; }

    public int StorySceneId { get; set; }
    public virtual StoryScene? StoryScene { get; set; }
}
```

- [ ] **Step 4: Thêm loại thông báo và nhóm**

`NotificationType.cs`, sau `StoryWishExpiring = 30`:

```csharp
    StoryWishExpiring = 30,
    /// <summary>Trẻ gửi tóm tắt cuối truyện (ghi âm hoặc đánh cờ) cho chủ hồ sơ.</summary>
    StorySummarySent = 31
```

`NotificationGroups.cs` dòng 32-33, đổi thành:

```csharp
            or NotificationType.StoryWishSubmitted
            or NotificationType.StoryWishExpiring
            or NotificationType.StorySummarySent => Children,
```

- [ ] **Step 5: Cấu hình EF**

`src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/StorySummaryConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StorySummaryConfiguration : IEntityTypeConfiguration<StorySummary>
{
    public void Configure(EntityTypeBuilder<StorySummary> builder)
    {
        builder.ToTable("story_summaries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AudioStoragePath).HasMaxLength(500);
        builder.Property(x => x.AudioContentType).HasMaxLength(50);

        builder.HasOne(x => x.ReadingSession).WithMany().HasForeignKey(x => x.ReadingSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ChildProfile).WithMany().HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Story).WithMany().HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ReadingSessionId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_story_summaries_one_per_session");
        builder.HasIndex(x => new { x.ChildProfileId, x.CreatedAt })
            .HasDatabaseName("IX_story_summaries_child_created");
    }
}

public class StorySummaryFlagConfiguration : IEntityTypeConfiguration<StorySummaryFlag>
{
    public void Configure(EntityTypeBuilder<StorySummaryFlag> builder)
    {
        builder.ToTable("story_summary_flags");
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.StorySummary).WithMany().HasForeignKey(x => x.StorySummaryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.StoryScene).WithMany().HasForeignKey(x => x.StorySceneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StorySummaryId, x.StorySceneId })
            .IsUnique()
            .HasDatabaseName("UX_story_summary_flags_summary_scene");
    }
}
```

`ApplicationDbContext.cs`, ngay sau `public DbSet<StoryWish> StoryWishes => Set<StoryWish>();`:

```csharp
    public DbSet<StorySummary> StorySummaries => Set<StorySummary>();
    public DbSet<StorySummaryFlag> StorySummaryFlags => Set<StorySummaryFlag>();
```

- [ ] **Step 6: Chạy test để xác nhận đạt**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~StorySummaryModelTests|FullyQualifiedName~NotificationGroupsTests"`
Expected: PASS.

- [ ] **Step 7: Sinh migration**

Run (cài công cụ nếu chưa có):
```bash
dotnet tool install --global dotnet-ef --version 8.0.13 || true
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef migrations add AddStorySummaries \
  --project src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj \
  --startup-project src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj
```
Expected: migration chỉ có `CreateTable story_summaries`, `CreateTable story_summary_flags` và các `CreateIndex` tương ứng; không đụng bảng khác. Nếu `Up` có thao tác trên bảng khác thì dừng và báo cáo.

- [ ] **Step 8: Kiểm tra chuỗi migration và model**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~MigrationChainSchemaTests" && dotnet test tests/StoryPlatform.IntegrationTests/StoryPlatform.IntegrationTests.csproj --filter "FullyQualifiedName~Notification_type_column_fits_every_enum_name"`
Expected: PASS.

---

### Task 2: Trẻ gửi ghi âm tóm tắt và đánh cờ thắc mắc

**Việc này làm gì (nói dễ hiểu):**
Ở màn hình cuối truyện, trẻ có thể bấm micro kể lại truyện rồi gửi, hoặc chọn những cảnh còn thắc mắc rồi gửi. Hệ thống cất file ghi âm, lưu các cảnh được chọn, báo "Đã gửi cho bố mẹ" và gửi thông báo cho chủ hồ sơ. Mỗi phiên đọc chỉ gửi được một lần cho mỗi cách. Nếu bố mẹ chưa bật micro thì trẻ chỉ đánh cờ được.

**Cần thêm / còn thiếu:**
- Đã có: kiểm tra phiên kiểu luyện đọc (`ChildReadAloudService.cs` — chủ phiên, bị ngắt, đang đọc); `ReadAloudPolicy` (định dạng âm thanh, thông điệp micro tắt); `IMediaStorage.UploadAsync`; `INotificationService.CreateAsync`; claim `ChildId` trong controller trẻ; `ReadingHarness` cho test.
- Cần thêm: `StorySummaryPolicy`; DTO; `IChildStorySummaryService` + `ChildStorySummaryService`; `ChildStorySummaryController` với 2 endpoint:
  - `POST /api/v1/child/reading/sessions/{sessionId}/summary/recording` (multipart, field `audio`)
  - `POST /api/v1/child/reading/sessions/{sessionId}/summary/flags` (JSON `{ "sceneIds": [..] }`)
- Còn thiếu / ngoài phạm vi: giao diện cuối truyện (chọn 3–5 ảnh cảnh làm thẻ gợi nhớ) thuộc frontend; frontend lấy ảnh cảnh từ `GET sessions/{id}/content` hiện có. Nút Bỏ qua = không gọi API.

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/ReadingSessions/StorySummaryPolicy.cs`
- Create: `src/Core/StoryPlatform.Application/Features/ReadingSessions/DTOs/StorySummaryDtos.cs`
- Create: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Interfaces/IChildStorySummaryService.cs`
- Create: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildStorySummaryService.cs`
- Create: `src/Core/StoryPlatform.Api/Controllers/ChildStorySummaryController.cs`
- Modify: `src/Core/StoryPlatform.Application/DependencyInjection.cs:150`
- Modify: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ReadingHarness.cs:47,85`
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildStorySummaryServiceTests.cs`

**Interfaces:**
- Consumes: `StorySummary`, `StorySummaryFlag`, `NotificationType.StorySummarySent` (Task 1).
- Produces:
  - `Task<StorySummarySentDto> IChildStorySummaryService.SendRecordingAsync(int childProfileId, int sessionId, StorySummaryRecordingRequest request, CancellationToken ct = default)`
  - `Task<StorySummarySentDto> IChildStorySummaryService.SendFlagsAsync(int childProfileId, int sessionId, StorySummaryFlagsRequestDto request, CancellationToken ct = default)`
  - `StorySummaryPolicy.MaxAudioBytes`, `StorySummaryPolicy.MaxFlags`, `StorySummaryPolicy.ListenUrlLifetime`, `StorySummaryPolicy.Extension(string contentType)`.

- [ ] **Step 1: Mở rộng harness**

`ReadingHarness.cs`: sau dòng `public readonly List<UserAccount> Users = new();` thêm:

```csharp
    public readonly List<StorySummary> Summaries = new();
    public readonly List<StorySummaryFlag> SummaryFlags = new();
```

Trong constructor, dòng `Back(StoryWishes); Back(Users);` đổi thành:

```csharp
        Back(StoryWishes); Back(Users); Back(Summaries); Back(SummaryFlags);
```

- [ ] **Step 2: Viết test thất bại**

Create `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildStorySummaryServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.DTOs;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ChildStorySummaryServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly ChildStorySummaryService _sut;

    public ChildStorySummaryServiceTests()
    {
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, string _, CancellationToken _) => path);
        _notifications.Setup(n => n.CreateAsync(It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationDto());
        _sut = new ChildStorySummaryService(_h.UnitOfWork.Object, _storage.Object, _notifications.Object,
            NullLogger<ChildStorySummaryService>.Instance);
    }

    private void VoiceOn(bool enabled = true) => _h.Policies.Add(new SafetyPolicy
    {
        Id = _h.Policies.Count + 1, ChildProfileId = ReadingHarness.Child, VoiceAnswersEnabled = enabled
    });

    private static StorySummaryRecordingRequest Audio(int bytes = 10, string? contentType = "audio/webm;codecs=opus") =>
        new() { Audio = new byte[bytes], ContentType = contentType };

    [Fact]
    public async Task SendRecordingAsync_StoresAudio_SavesSummary_AndNotifiesOwner()
    {
        VoiceOn();
        var session = _h.AddSession(SessionStatus.Completed);

        var result = await _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio());

        Assert.Equal(StorySummaryPolicy.SentMessage, result.Message);
        var summary = Assert.Single(_h.Summaries);
        Assert.Equal(session.Id, summary.ReadingSessionId);
        Assert.Equal("audio/webm", summary.AudioContentType);
        Assert.Equal(10, summary.AudioBytes);
        Assert.NotNull(summary.RecordingSentAt);
        Assert.StartsWith($"story-summaries/{ReadingHarness.Child}/{session.Id}/", summary.AudioStoragePath);
        Assert.EndsWith(".webm", summary.AudioStoragePath);
        _storage.Verify(s => s.UploadAsync(summary.AudioStoragePath!, It.IsAny<Stream>(), "audio/webm", It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(7, NotificationType.StorySummarySent,
            It.Is<string?>(p => p!.Contains("\"kind\":\"recording\"")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendRecordingAsync_WhenVoiceOff_IsForbidden_AndUploadsNothing()
    {
        VoiceOn(false);
        var session = _h.AddSession();

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));

        Assert.Empty(_h.Summaries);
        _storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, "audio/webm")]
    [InlineData(StorySummaryPolicy.MaxAudioBytes + 1, "audio/webm")]
    [InlineData(10, "video/mp4")]
    public async Task SendRecordingAsync_RejectsBadAudio(int bytes, string contentType)
    {
        VoiceOn();
        var session = _h.AddSession();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio(bytes, contentType)));

        Assert.Empty(_h.Summaries);
    }

    [Fact]
    public async Task SendRecordingAsync_Twice_ReturnsConflict()
    {
        VoiceOn();
        var session = _h.AddSession();
        await _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio());

        await Assert.ThrowsAsync<ConflictException>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));

        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(SessionStatus.ForceExited)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task SendRecordingAsync_OnInterruptedOrAbandonedSession_IsRejected(SessionStatus status)
    {
        VoiceOn();
        var session = _h.AddSession(status);

        await Assert.ThrowsAnyAsync<Exception>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));

        Assert.Empty(_h.Summaries);
    }

    [Fact]
    public async Task SendRecordingAsync_OtherChildsSession_IsNotFound()
    {
        VoiceOn();
        var session = _h.AddSession(childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio()));
    }

    [Fact]
    public async Task SendFlagsAsync_SavesDistinctScenes_AndNotifies()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var first = _h.AddScene(0, "Thỏ ra vườn.");
        var second = _h.AddScene(1, "Thỏ gặp rùa.");

        var result = await _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = [first.Id, second.Id, first.Id] });

        Assert.Equal(StorySummaryPolicy.SentMessage, result.Message);
        var summary = Assert.Single(_h.Summaries);
        Assert.NotNull(summary.FlagsSentAt);
        Assert.Equal(new[] { first.Id, second.Id }, _h.SummaryFlags.Select(f => f.StorySceneId).OrderBy(id => id));
        Assert.All(_h.SummaryFlags, f => Assert.Equal(summary.Id, f.StorySummaryId));
        _notifications.Verify(n => n.CreateAsync(7, NotificationType.StorySummarySent,
            It.Is<string?>(p => p!.Contains("\"kind\":\"flags\"")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendFlagsAsync_AfterRecording_ReusesTheSameSummary()
    {
        VoiceOn();
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        await _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio());

        await _sut.SendFlagsAsync(ReadingHarness.Child, session.Id, new StorySummaryFlagsRequestDto { SceneIds = [scene.Id] });

        var summary = Assert.Single(_h.Summaries);
        Assert.NotNull(summary.RecordingSentAt);
        Assert.NotNull(summary.FlagsSentAt);
    }

    [Fact]
    public async Task SendFlagsAsync_RejectsEmpty_TooMany_AndScenesFromAnotherVersion()
    {
        var session = _h.AddSession();
        var scenes = Enumerable.Range(0, StorySummaryPolicy.MaxFlags + 1).Select(i => _h.AddScene(i, $"Cảnh {i}.")).ToList();
        var foreign = _h.AddScene(0, "Truyện khác.", versionId: 999);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = [] }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = scenes.Select(s => s.Id).ToList() }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = [foreign.Id] }));

        Assert.Empty(_h.Summaries);
        Assert.Empty(_h.SummaryFlags);
    }

    [Fact]
    public async Task SendFlagsAsync_Twice_ReturnsConflict()
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        await _sut.SendFlagsAsync(ReadingHarness.Child, session.Id, new StorySummaryFlagsRequestDto { SceneIds = [scene.Id] });

        await Assert.ThrowsAsync<ConflictException>(() => _sut.SendFlagsAsync(ReadingHarness.Child, session.Id,
            new StorySummaryFlagsRequestDto { SceneIds = [scene.Id] }));

        Assert.Single(_h.SummaryFlags);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotFailTheChild()
    {
        VoiceOn();
        var session = _h.AddSession();
        _notifications.Setup(n => n.CreateAsync(It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));

        var result = await _sut.SendRecordingAsync(ReadingHarness.Child, session.Id, Audio());

        Assert.Equal(StorySummaryPolicy.SentMessage, result.Message);
        Assert.Single(_h.Summaries);
    }
}
```

- [ ] **Step 3: Chạy test để xác nhận thất bại**

Run: `dotnet build tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`
Expected: FAIL biên dịch: `The type or namespace name 'ChildStorySummaryService' could not be found`, `'StorySummaryPolicy' could not be found`.

- [ ] **Step 4: Policy**

`src/Core/StoryPlatform.Application/Features/ReadingSessions/StorySummaryPolicy.cs`:

```csharp
namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Tóm tắt cuối truyện gửi bố mẹ (Luồng 3 — Danh sách tương tác của trẻ, mục 6). Không dùng AI, không chấm.</summary>
public static class StorySummaryPolicy
{
    /// <summary>Khoảng 2–3 phút giọng nói dạng webm/opus.</summary>
    public const int MaxAudioBytes = 3_000_000;
    public const int MaxFlags = 5;
    public static readonly TimeSpan ListenUrlLifetime = TimeSpan.FromMinutes(15);

    public const string SentMessage = "Đã gửi cho bố mẹ";
    public const string EmptyAudioMessage = "Em bấm micro rồi kể lại nhé.";
    public const string AudioTooLargeMessage = "Đoạn ghi âm dài quá, em kể ngắn hơn nhé.";
    public const string AlreadyRecordedMessage = "Em đã gửi bản ghi âm cho bố mẹ rồi.";
    public const string NoFlagsMessage = "Em chọn ít nhất một chỗ nhé.";
    public const string TooManyFlagsMessage = "Em chọn tối đa 5 chỗ nhé.";
    public const string SceneNotInStoryMessage = "Chỗ em chọn không thuộc truyện này.";
    public const string AlreadyFlaggedMessage = "Em đã gửi những chỗ thắc mắc cho bố mẹ rồi.";

    public static string Extension(string contentType) => contentType switch
    {
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        "audio/wav" or "audio/x-wav" or "audio/wave" => ".wav",
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/mp4" or "audio/x-m4a" => ".m4a",
        "audio/flac" => ".flac",
        _ => ".bin"
    };
}
```

- [ ] **Step 5: DTO**

`src/Core/StoryPlatform.Application/Features/ReadingSessions/DTOs/StorySummaryDtos.cs`:

```csharp
namespace StoryPlatform.Application.Features.ReadingSessions.DTOs;

public sealed class StorySummaryRecordingRequest
{
    public byte[] Audio { get; init; } = [];
    public string? ContentType { get; init; }
}

public sealed class StorySummaryFlagsRequestDto
{
    public List<int>? SceneIds { get; set; }
}

public sealed class StorySummarySentDto
{
    public int SummaryId { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class ParentStorySummaryQueryDto
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class ParentStorySummaryItemDto
{
    public int SummaryId { get; init; }
    public int SessionId { get; init; }
    public int StoryId { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    /// <summary>Link nghe có hạn (15 phút); null khi con không ghi âm.</summary>
    public string? RecordingUrl { get; init; }
    public DateTime? RecordingSentAt { get; init; }
    public DateTime? FlagsSentAt { get; init; }
    public List<ParentStorySummaryFlagDto> Flags { get; init; } = new();
}

public sealed class ParentStorySummaryFlagDto
{
    public int SceneId { get; init; }
    public int SceneIndex { get; init; }
    public string SceneText { get; init; } = string.Empty;
    public string? IllustrationUrl { get; init; }
}
```

- [ ] **Step 6: Interface và service**

`src/Core/StoryPlatform.Application/Features/ReadingSessions/Interfaces/IChildStorySummaryService.cs`:

```csharp
using StoryPlatform.Application.Features.ReadingSessions.DTOs;

namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildStorySummaryService
{
    Task<StorySummarySentDto> SendRecordingAsync(
        int childProfileId, int sessionId, StorySummaryRecordingRequest request, CancellationToken ct = default);

    Task<StorySummarySentDto> SendFlagsAsync(
        int childProfileId, int sessionId, StorySummaryFlagsRequestDto request, CancellationToken ct = default);
}
```

`src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildStorySummaryService.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.DTOs;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>Trẻ gửi tóm tắt cuối truyện cho chủ hồ sơ: ghi âm và/hoặc đánh cờ cảnh còn thắc mắc. Không dùng AI.</summary>
public class ChildStorySummaryService : IChildStorySummaryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediaStorage _storage;
    private readonly INotificationService _notifications;
    private readonly ILogger<ChildStorySummaryService> _logger;

    public ChildStorySummaryService(IUnitOfWork unitOfWork, IMediaStorage storage,
        INotificationService notifications, ILogger<ChildStorySummaryService> logger)
    {
        _unitOfWork = unitOfWork;
        _storage = storage;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<StorySummarySentDto> SendRecordingAsync(
        int childProfileId, int sessionId, StorySummaryRecordingRequest request, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(childProfileId, sessionId, ct);
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childProfileId && !existing.IsDeleted, cancellationToken: ct);
        if (policy is not { VoiceAnswersEnabled: true })
            throw new ForbiddenException(ReadAloudPolicy.VoiceOffMessage);
        if (request.Audio.Length == 0)
            throw new BadRequestException(StorySummaryPolicy.EmptyAudioMessage);
        if (request.Audio.Length > StorySummaryPolicy.MaxAudioBytes)
            throw new BadRequestException(StorySummaryPolicy.AudioTooLargeMessage);
        if (!ReadAloudPolicy.IsSupportedContentType(request.ContentType))
            throw new BadRequestException(ReadAloudPolicy.UnsupportedAudioMessage);

        var summary = await FindOrNewAsync(session, ct);
        if (summary.AudioStoragePath != null)
            throw new ConflictException(StorySummaryPolicy.AlreadyRecordedMessage);

        var contentType = request.ContentType!.Split(';')[0].Trim().ToLowerInvariant();
        var path = $"story-summaries/{childProfileId}/{sessionId}/{Guid.NewGuid():N}{StorySummaryPolicy.Extension(contentType)}";
        using (var content = new MemoryStream(request.Audio))
        {
            await _storage.UploadAsync(path, content, contentType, ct);
        }

        summary.AudioStoragePath = path;
        summary.AudioContentType = contentType;
        summary.AudioBytes = request.Audio.Length;
        summary.RecordingSentAt = DateTime.UtcNow;
        try
        {
            await SaveSummaryAsync(summary, ct);
        }
        catch
        {
            await TryDeleteAsync(path);
            throw;
        }

        await NotifyOwnerAsync(session, summary, "recording", ct);
        return Sent(summary);
    }

    public async Task<StorySummarySentDto> SendFlagsAsync(
        int childProfileId, int sessionId, StorySummaryFlagsRequestDto request, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(childProfileId, sessionId, ct);
        var sceneIds = (request.SceneIds ?? new List<int>()).Distinct().ToList();
        if (sceneIds.Count == 0)
            throw new BadRequestException(StorySummaryPolicy.NoFlagsMessage);
        if (sceneIds.Count > StorySummaryPolicy.MaxFlags)
            throw new BadRequestException(StorySummaryPolicy.TooManyFlagsMessage);
        var versionId = session.StoryVersionId;
        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(
            scene => sceneIds.Contains(scene.Id) && scene.StoryVersionId == versionId && !scene.IsDeleted,
            cancellationToken: ct);
        if (scenes.Count != sceneIds.Count)
            throw new BadRequestException(StorySummaryPolicy.SceneNotInStoryMessage);

        var summary = await FindOrNewAsync(session, ct);
        if (summary.FlagsSentAt != null)
            throw new ConflictException(StorySummaryPolicy.AlreadyFlaggedMessage);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            summary.FlagsSentAt = DateTime.UtcNow;
            await SaveSummaryAsync(summary, ct);
            await _unitOfWork.Repository<StorySummaryFlag>().AddRangeAsync(
                sceneIds.Select(id => new StorySummaryFlag { StorySummaryId = summary.Id, StorySceneId = id }), ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await NotifyOwnerAsync(session, summary, "flags", ct);
        return Sent(summary);
    }

    private async Task<ReadingSession> LoadSessionAsync(int childProfileId, int sessionId, CancellationToken ct)
    {
        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
            throw new NotFoundException("Phiên đọc", sessionId);
        if (session.Status == SessionStatus.ForceExited || session.ForceExitRequestedAt != null)
            throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
        if (!ChildReadingPolicy.IsActive(session.Status) && session.Status != SessionStatus.Completed)
            throw new ConflictException(ReadAloudPolicy.SessionEndedMessage);
        return session;
    }

    private async Task<StorySummary> FindOrNewAsync(ReadingSession session, CancellationToken ct) =>
        await _unitOfWork.Repository<StorySummary>().FirstOrDefaultAsync(
            existing => existing.ReadingSessionId == session.Id && !existing.IsDeleted, cancellationToken: ct)
        ?? new StorySummary
        {
            ReadingSessionId = session.Id,
            ChildProfileId = session.ChildProfileId,
            StoryId = session.StoryId,
            StoryVersionId = session.StoryVersionId
        };

    private async Task SaveSummaryAsync(StorySummary summary, CancellationToken ct)
    {
        var repo = _unitOfWork.Repository<StorySummary>();
        if (summary.Id == 0)
        {
            await repo.AddAsync(summary, ct);
        }
        else
        {
            summary.UpdatedAt = DateTime.UtcNow;
            repo.Update(summary);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task NotifyOwnerAsync(ReadingSession session, StorySummary summary, string kind, CancellationToken ct)
    {
        try
        {
            var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(session.ChildProfileId, ct);
            if (child == null) return;
            var payload = JsonSerializer.Serialize(new
            {
                childProfileId = child.Id, summaryId = summary.Id, storyId = session.StoryId, kind
            });
            await _notifications.CreateAsync(child.OwnerUserId, NotificationType.StorySummarySent, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Story summary notification failed. SummaryId={SummaryId}, ErrorType={ErrorType}",
                summary.Id, ex.GetType().Name);
        }
    }

    private async Task TryDeleteAsync(string path)
    {
        try
        {
            await _storage.DeleteAsync(path, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Orphan story summary audio could not be deleted. ErrorType={ErrorType}", ex.GetType().Name);
        }
    }

    private static StorySummarySentDto Sent(StorySummary summary) =>
        new() { SummaryId = summary.Id, Message = StorySummaryPolicy.SentMessage };
}
```

`ReadingSessionInterruptPolicy` nằm trong namespace `StoryPlatform.Application.Features.ReadingSessions` (file `DTOs/ReadingSessionInterruptDtos.cs`), `ReadAloudPolicy` và `ChildReadingPolicy` cùng namespace — không cần `using` thêm.

- [ ] **Step 7: Đăng ký DI**

`src/Core/StoryPlatform.Application/DependencyInjection.cs`, sau dòng 150 `services.AddScoped<IChildReadAloudService, ChildReadAloudService>();`:

```csharp
        services.AddScoped<IChildStorySummaryService, ChildStorySummaryService>();
```

- [ ] **Step 8: Controller**

`src/Core/StoryPlatform.Api/Controllers/ChildStorySummaryController.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.DTOs;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Tóm tắt cuối truyện trẻ gửi bố mẹ (Luồng 3 — mục 6): ghi âm hoặc đánh cờ chỗ còn thắc mắc.</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/reading/sessions/{sessionId:int}/summary")]
public sealed class ChildStorySummaryController : ControllerBase
{
    private const int MaxRequestBytes = StorySummaryPolicy.MaxAudioBytes + 100_000;
    private readonly IChildStorySummaryService _summaries;

    public ChildStorySummaryController(IChildStorySummaryService summaries)
    {
        _summaries = summaries;
    }

    private int ChildId
    {
        get
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!int.TryParse(claim, out var id))
            {
                throw new UnauthorizedAccessException();
            }

            return id;
        }
    }

    [HttpPost("recording")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MemoryBufferThreshold = MaxRequestBytes, MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<ApiResponse<StorySummarySentDto>>> SendRecording(
        int sessionId, [FromForm] StorySummaryRecordingForm form, CancellationToken ct)
    {
        if (form.Audio is { Length: > StorySummaryPolicy.MaxAudioBytes })
        {
            throw new BadRequestException(StorySummaryPolicy.AudioTooLargeMessage);
        }

        byte[] audio = [];
        if (form.Audio is { Length: > 0 })
        {
            using var buffer = new MemoryStream();
            await form.Audio.CopyToAsync(buffer, ct);
            audio = buffer.ToArray();
        }

        var result = await _summaries.SendRecordingAsync(ChildId, sessionId,
            new StorySummaryRecordingRequest { Audio = audio, ContentType = form.Audio?.ContentType }, ct);
        return Ok(ApiResponse<StorySummarySentDto>.Ok(result, result.Message));
    }

    [HttpPost("flags")]
    public async Task<ActionResult<ApiResponse<StorySummarySentDto>>> SendFlags(
        int sessionId, [FromBody] StorySummaryFlagsRequestDto request, CancellationToken ct)
    {
        var result = await _summaries.SendFlagsAsync(ChildId, sessionId, request, ct);
        return Ok(ApiResponse<StorySummarySentDto>.Ok(result, result.Message));
    }
}

public sealed class StorySummaryRecordingForm
{
    /// <summary>Bản ghi âm tóm tắt (webm/ogg/wav/mp3/m4a/flac), tối đa 3 MB.</summary>
    public IFormFile? Audio { get; set; }
}
```

- [ ] **Step 9: Chạy test để xác nhận đạt**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~ChildStorySummaryServiceTests"`
Expected: PASS.

Run: `dotnet build StoryPlatform.slnx 2>&1 | grep -E " error |Build succeeded"`
Expected: `Build succeeded`.

---

### Task 3: Phụ huynh nghe, xem và xóa bản tóm tắt

**Việc này làm gì (nói dễ hiểu):**
Chủ hồ sơ mở danh sách các bản tóm tắt con đã gửi: mỗi bản cho biết truyện nào, gửi lúc nào, có link nghe ghi âm (link tự hết hạn sau 15 phút, mở lại danh sách là có link mới) và các cảnh con còn thắc mắc kèm ảnh, đoạn chữ. Chủ hồ sơ xóa được một bản: file ghi âm bị xóa khỏi kho lưu trữ, bản tóm tắt biến mất khỏi danh sách. Người khác (kể cả admin) không xem được qua API này.

**Cần thêm / còn thiếu:**
- Đã có: `IChildProfileAccessGuard.EnsureOwnerAsync`; mẫu danh sách phân trang `ParentReadingFeedbackService`; `IMediaStorage.GetSignedUrlAsync`, `DeleteAsync`; cách lấy ảnh cảnh (`MediaAsset` loại `Illustration`, `Ready`, theo `StorySceneId`) như `ChildReadingService.cs:236-256`.
- Cần thêm: `IParentStorySummaryService` + `ParentStorySummaryService` (`ListAsync`, `DeleteAsync`); `ParentStorySummaryController`:
  - `GET /api/v1/ParentStorySummary/{childProfileId}?pageIndex=&pageSize=`
  - `DELETE /api/v1/ParentStorySummary/{childProfileId}/{summaryId}`
  - Tài liệu API cho 4 endpoint (2 của trẻ, 2 của phụ huynh) và DBML.
- Còn thiếu / ngoài phạm vi: đánh dấu "đã nghe"; hiển thị trong thẻ "Con vừa đọc xong" và màn hình frontend — thuộc repo frontend (gọi API này).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/DTOs/StorySummaryDtos.cs` (đã có DTO phụ huynh từ Task 2)
- Create: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Interfaces/IParentStorySummaryService.cs`
- Create: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ParentStorySummaryService.cs`
- Create: `src/Core/StoryPlatform.Api/Controllers/ParentStorySummaryController.cs`
- Modify: `src/Core/StoryPlatform.Application/DependencyInjection.cs:146`
- Modify: `docs/API_FRONTEND_ACTOR_FLOWS.md`, `Database/ai_storytelling_platform.dbml`
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ParentStorySummaryServiceTests.cs`

**Interfaces:**
- Consumes: entity Task 1; DTO và `StorySummaryPolicy.ListenUrlLifetime` Task 2.
- Produces:
  - `Task<PagedResult<ParentStorySummaryItemDto>> IParentStorySummaryService.ListAsync(int parentUserId, int childProfileId, ParentStorySummaryQueryDto query, CancellationToken ct = default)`
  - `Task IParentStorySummaryService.DeleteAsync(int parentUserId, int childProfileId, int summaryId, CancellationToken ct = default)`
  - `Task IParentStorySummaryService.PurgeChildAsync(int childProfileId, CancellationToken ct = default)` (dùng ở Task 4).

- [ ] **Step 1: Viết test thất bại**

Create `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ParentStorySummaryServiceTests.cs`:

```csharp
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.DTOs;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ParentStorySummaryServiceTests
{
    private const int Parent = 7;
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ParentStorySummaryService _sut;

    public ParentStorySummaryServiceTests()
    {
        _storage.Setup(s => s.GetSignedUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, TimeSpan _, CancellationToken _) => $"https://signed/{path}");
        _sut = new ParentStorySummaryService(_h.UnitOfWork.Object, _guard.Object, _storage.Object);
    }

    private StorySummary AddSummary(string? audioPath = "story-summaries/100/1/a.webm", int childId = ReadingHarness.Child)
    {
        var session = _h.AddSession(SessionStatus.Completed, childId);
        var summary = new StorySummary
        {
            Id = _h.Summaries.Count + 1, ReadingSessionId = session.Id, ChildProfileId = childId,
            StoryId = ReadingHarness.Story, StoryVersionId = ReadingHarness.Version,
            AudioStoragePath = audioPath, RecordingSentAt = audioPath == null ? null : DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddMinutes(-_h.Summaries.Count)
        };
        _h.Summaries.Add(summary);
        return summary;
    }

    [Fact]
    public async Task ListAsync_ReturnsSignedRecordingUrl_AndFlaggedScenesWithIllustration()
    {
        var summary = AddSummary();
        var scene = _h.AddScene(2, "Thỏ gặp rùa.");
        _h.AddIllustration(scene, "https://img/scene-2.png");
        summary.FlagsSentAt = DateTime.UtcNow;
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = scene.Id });

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        var item = Assert.Single(page.Items);
        Assert.Equal("Truyện 10", item.Title);
        Assert.Equal("https://signed/story-summaries/100/1/a.webm", item.RecordingUrl);
        var flag = Assert.Single(item.Flags);
        Assert.Equal((scene.Id, 2, "Thỏ gặp rùa.", "https://img/scene-2.png"),
            (flag.SceneId, flag.SceneIndex, flag.SceneText, flag.IllustrationUrl));
        _guard.Verify(g => g.EnsureOwnerAsync(ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.GetSignedUrlAsync("story-summaries/100/1/a.webm", StorySummaryPolicy.ListenUrlLifetime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_HidesDeletedAndOtherChildren_AndHasNoUrlWithoutRecording()
    {
        AddSummary(audioPath: null);
        AddSummary().IsDeleted = true;
        AddSummary(childId: ReadingHarness.OtherChild);

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        var item = Assert.Single(page.Items);
        Assert.Null(item.RecordingUrl);
    }

    [Fact]
    public async Task ListAsync_NotOwner_PropagatesGuardFailure()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(ReadingHarness.Child, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("x"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(99, ReadingHarness.Child, new ParentStorySummaryQueryDto()));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_AndSoftDeletesSummaryAndFlags()
    {
        var summary = AddSummary();
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = 5 });

        await _sut.DeleteAsync(Parent, ReadingHarness.Child, summary.Id);

        _storage.Verify(s => s.DeleteAsync("story-summaries/100/1/a.webm", It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(summary.IsDeleted);
        Assert.Null(summary.AudioStoragePath);
        Assert.All(_h.SummaryFlags, f => Assert.True(f.IsDeleted));
    }

    [Fact]
    public async Task DeleteAsync_SummaryOfAnotherChild_IsNotFound()
    {
        var summary = AddSummary(childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(Parent, ReadingHarness.Child, summary.Id));

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(summary.IsDeleted);
    }

    [Fact]
    public async Task PurgeChildAsync_DeletesEveryRecordingOfTheChild()
    {
        var first = AddSummary("p/1.webm");
        var second = AddSummary("p/2.webm");
        var other = AddSummary("p/3.webm", ReadingHarness.OtherChild);

        await _sut.PurgeChildAsync(ReadingHarness.Child);

        _storage.Verify(s => s.DeleteAsync("p/1.webm", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("p/2.webm", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("p/3.webm", It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(first.IsDeleted && second.IsDeleted);
        Assert.Null(first.AudioStoragePath);
        Assert.False(other.IsDeleted);
    }
}
```

- [ ] **Step 2: Chạy test để xác nhận thất bại**

Run: `dotnet build tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`
Expected: FAIL biên dịch: `The type or namespace name 'ParentStorySummaryService' could not be found`.

- [ ] **Step 3: Interface và service**

`src/Core/StoryPlatform.Application/Features/ReadingSessions/Interfaces/IParentStorySummaryService.cs`:

```csharp
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions.DTOs;

namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IParentStorySummaryService
{
    Task<PagedResult<ParentStorySummaryItemDto>> ListAsync(
        int parentUserId, int childProfileId, ParentStorySummaryQueryDto query, CancellationToken ct = default);

    Task DeleteAsync(int parentUserId, int childProfileId, int summaryId, CancellationToken ct = default);

    /// <summary>Dọn mọi bản ghi âm của trẻ khi xóa tài khoản chủ hồ sơ (không kiểm quyền — gọi từ hệ thống).</summary>
    Task PurgeChildAsync(int childProfileId, CancellationToken ct = default);
}
```

`src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ParentStorySummaryService.cs`:

```csharp
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.DTOs;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

/// <summary>Chủ hồ sơ nghe / xem / xóa tóm tắt cuối truyện con gửi. File lưu tới khi chủ hồ sơ xóa.</summary>
public class ParentStorySummaryService : IParentStorySummaryService
{
    private const int MaxPageSize = 50;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _guard;
    private readonly IMediaStorage _storage;

    public ParentStorySummaryService(IUnitOfWork unitOfWork, IChildProfileAccessGuard guard, IMediaStorage storage)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
        _storage = storage;
    }

    public async Task<PagedResult<ParentStorySummaryItemDto>> ListAsync(
        int parentUserId, int childProfileId, ParentStorySummaryQueryDto query, CancellationToken ct = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var (summaries, total) = await _unitOfWork.Repository<StorySummary>().GetPagedAsync(
            pageIndex,
            pageSize,
            summary => summary.ChildProfileId == childProfileId && !summary.IsDeleted,
            orderBy: rows => rows.OrderByDescending(summary => summary.CreatedAt).ThenByDescending(summary => summary.Id),
            cancellationToken: ct);

        var summaryIds = summaries.Select(summary => summary.Id).ToList();
        var flags = await _unitOfWork.Repository<StorySummaryFlag>().FindAsync(
            flag => summaryIds.Contains(flag.StorySummaryId) && !flag.IsDeleted, cancellationToken: ct);
        var sceneIds = flags.Select(flag => flag.StorySceneId).Distinct().ToList();
        var scenes = (await _unitOfWork.Repository<StoryScene>().FindAsync(
            scene => sceneIds.Contains(scene.Id), cancellationToken: ct)).ToDictionary(scene => scene.Id);
        var illustrations = (await _unitOfWork.Repository<MediaAsset>().FindAsync(
                asset => asset.StorySceneId.HasValue && sceneIds.Contains(asset.StorySceneId.Value)
                         && asset.Type == MediaType.Illustration && asset.Status == MediaStatus.Ready && !asset.IsDeleted,
                cancellationToken: ct))
            .OrderByDescending(asset => asset.CompletedAt ?? asset.CreatedAt).ThenByDescending(asset => asset.Id)
            .GroupBy(asset => asset.StorySceneId!.Value)
            .ToDictionary(group => group.Key, group => group.First().Url);
        var versionIds = summaries.Where(summary => summary.StoryVersionId.HasValue)
            .Select(summary => summary.StoryVersionId!.Value).Distinct().ToList();
        var titles = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
            version => versionIds.Contains(version.Id), cancellationToken: ct)).ToDictionary(version => version.Id, version => version.Title);

        var items = new List<ParentStorySummaryItemDto>();
        foreach (var summary in summaries)
        {
            var url = summary.AudioStoragePath == null
                ? null
                : await _storage.GetSignedUrlAsync(summary.AudioStoragePath, StorySummaryPolicy.ListenUrlLifetime, ct);
            items.Add(new ParentStorySummaryItemDto
            {
                SummaryId = summary.Id,
                SessionId = summary.ReadingSessionId,
                StoryId = summary.StoryId,
                Title = summary.StoryVersionId.HasValue && titles.TryGetValue(summary.StoryVersionId.Value, out var title) ? title : string.Empty,
                CreatedAt = summary.CreatedAt,
                RecordingUrl = url,
                RecordingSentAt = summary.RecordingSentAt,
                FlagsSentAt = summary.FlagsSentAt,
                Flags = flags.Where(flag => flag.StorySummaryId == summary.Id && scenes.ContainsKey(flag.StorySceneId))
                    .Select(flag => scenes[flag.StorySceneId])
                    .OrderBy(scene => scene.SceneIndex)
                    .Select(scene => new ParentStorySummaryFlagDto
                    {
                        SceneId = scene.Id,
                        SceneIndex = scene.SceneIndex,
                        SceneText = scene.SceneText,
                        IllustrationUrl = illustrations.TryGetValue(scene.Id, out var image) ? image : null
                    }).ToList()
            });
        }

        return new PagedResult<ParentStorySummaryItemDto>(items, total, pageIndex, pageSize);
    }

    public async Task DeleteAsync(int parentUserId, int childProfileId, int summaryId, CancellationToken ct = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        var summary = await _unitOfWork.Repository<StorySummary>().GetByIdAsync(summaryId, ct);
        if (summary == null || summary.IsDeleted || summary.ChildProfileId != childProfileId)
            throw new NotFoundException("Bản tóm tắt", summaryId);

        await RemoveAsync(summary, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task PurgeChildAsync(int childProfileId, CancellationToken ct = default)
    {
        var summaries = await _unitOfWork.Repository<StorySummary>().FindAsync(
            summary => summary.ChildProfileId == childProfileId && !summary.IsDeleted, cancellationToken: ct);
        foreach (var summary in summaries)
        {
            await RemoveAsync(summary, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task RemoveAsync(StorySummary summary, CancellationToken ct)
    {
        if (summary.AudioStoragePath != null)
        {
            await _storage.DeleteAsync(summary.AudioStoragePath, ct);
        }

        var now = DateTime.UtcNow;
        var flagRepo = _unitOfWork.Repository<StorySummaryFlag>();
        var flags = await flagRepo.FindAsync(flag => flag.StorySummaryId == summary.Id && !flag.IsDeleted, cancellationToken: ct);
        foreach (var flag in flags)
        {
            flag.IsDeleted = true;
            flag.UpdatedAt = now;
            flagRepo.Update(flag);
        }

        summary.AudioStoragePath = null;
        summary.IsDeleted = true;
        summary.UpdatedAt = now;
        _unitOfWork.Repository<StorySummary>().Update(summary);
    }
}
```

- [ ] **Step 4: Đăng ký DI**

`DependencyInjection.cs`, sau dòng 146 `services.AddScoped<IParentReadingFeedbackService, ParentReadingFeedbackService>();`:

```csharp
        services.AddScoped<IParentStorySummaryService, ParentStorySummaryService>();
```

- [ ] **Step 5: Controller**

`src/Core/StoryPlatform.Api/Controllers/ParentStorySummaryController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions.DTOs;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Tóm tắt cuối truyện con gửi, phía chủ hồ sơ: nghe ghi âm, xem chỗ con thắc mắc, xóa.</summary>
[Authorize(Roles = "User")]
public class ParentStorySummaryController : BaseApiController
{
    private readonly IParentStorySummaryService _summaries;

    public ParentStorySummaryController(IParentStorySummaryService summaries)
    {
        _summaries = summaries;
    }

    [HttpGet("{childProfileId:int}")]
    public async Task<ActionResult<ApiResponse<PagedResult<ParentStorySummaryItemDto>>>> List(
        int childProfileId, [FromQuery] ParentStorySummaryQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _summaries.ListAsync(GetCurrentUserId(), childProfileId, query, cancellationToken);
        return HandleResult(result, "Lấy danh sách tóm tắt của con thành công.");
    }

    [HttpDelete("{childProfileId:int}/{summaryId:int}")]
    public async Task<ActionResult<ApiResponse<object?>>> Delete(
        int childProfileId, int summaryId, CancellationToken cancellationToken)
    {
        await _summaries.DeleteAsync(GetCurrentUserId(), childProfileId, summaryId, cancellationToken);
        return HandleResult<object?>(null, "Đã xóa bản tóm tắt.");
    }
}
```

- [ ] **Step 6: Chạy test để xác nhận đạt**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~ParentStorySummaryServiceTests"`
Expected: PASS.

- [ ] **Step 7: Tài liệu**

`docs/API_FRONTEND_ACTOR_FLOWS.md`: thêm mục mới cạnh nhóm `ParentReadingFeedback` / `ChildReading`, theo đúng khuôn các mục endpoint khác (Action, Quyền, Request, Response, Frontend = **CHƯA_GỌI**):
- `POST /api/v1/child/reading/sessions/{sessionId}/summary/recording` — ChildSession; multipart `audio` ≤ 3 MB; cần `voiceAnswersEnabled`; 409 khi gửi lần 2.
- `POST /api/v1/child/reading/sessions/{sessionId}/summary/flags` — ChildSession; body `{ "sceneIds": [..] }` 1–5 cảnh của truyện; 409 khi gửi lần 2.
- `GET /api/v1/ParentStorySummary/{childProfileId}` — User, chỉ chủ hồ sơ; `recordingUrl` hết hạn sau 15 phút.
- `DELETE /api/v1/ParentStorySummary/{childProfileId}/{summaryId}` — User, chỉ chủ hồ sơ; xóa file và bản tóm tắt.
Thêm DTO `StorySummarySentDto`, `StorySummaryFlagsRequestDto`, `ParentStorySummaryItemDto`, `ParentStorySummaryFlagDto` và giá trị `StorySummarySent` (31) vào bảng `NotificationType`.

`Database/ai_storytelling_platform.dbml`: thêm `Table story_summaries` và `Table story_summary_flags` với các cột như entity Task 1.

---

### Task 4: Xóa file ghi âm khi phụ huynh xóa tài khoản

**Việc này làm gì (nói dễ hiểu):**
Khi chủ hồ sơ xóa tài khoản, hồ sơ con bị ẩn danh. Giọng nói của con không được nằm lại trong kho lưu trữ, nên sau khi xóa tài khoản xong, hệ thống xóa luôn mọi file ghi âm tóm tắt của các con thuộc tài khoản đó. Nếu kho lưu trữ tạm lỗi, việc xóa tài khoản vẫn thành công và hệ thống ghi log để xử lý tay.

**Cần thêm / còn thiếu:**
- Đã có: `AccountDeletionService.DeleteAccountAsync` xóa hồ sơ trẻ trong transaction (`ChildProfileEraser`), ghi audit sau commit.
- Cần thêm: sau commit, gọi `IParentStorySummaryService.PurgeChildAsync` cho từng hồ sơ đã xóa; lỗi chỉ ghi log.
- Còn thiếu / ngoài phạm vi: job dọn lại file mồ côi khi purge lỗi (chỉ có log).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AccountDeletionService.cs:29-44,120`
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AccountDeletionServiceTests.cs:31,46-48`

**Interfaces:**
- Consumes: `IParentStorySummaryService.PurgeChildAsync(int childProfileId, CancellationToken ct)` (Task 3).
- Produces: constructor `AccountDeletionService(..., IAuditLogWriter auditLogWriter, IParentStorySummaryService storySummaries, ILogger<AccountDeletionService> logger)`.

- [ ] **Step 1: Viết test thất bại**

`AccountDeletionServiceTests.cs`: thêm field cạnh `_auditLogWriter` (dòng 31):

```csharp
    private readonly Mock<IParentStorySummaryService> _storySummaries = new();
```

Constructor `_sut` (dòng 46-48) đổi thành:

```csharp
        _sut = new AccountDeletionService(_unitOfWork.Object, _passwordHasher.Object, _eraser.Object,
            _childSessionRevoker.Object, _emailSender.Object, _auditLogWriter.Object, _storySummaries.Object,
            NullLogger<AccountDeletionService>.Instance);
```

Thêm `using StoryPlatform.Application.Features.ReadingSessions.Interfaces;` và hai test (cùng khuôn với `DeleteAccountAsync_Confirmed_ErasesEveryChildAndKeepsStories` ở dòng 72-86):

```csharp
    [Fact]
    public async Task DeleteAccountAsync_Confirmed_PurgesStorySummaryAudioOfEveryErasedChild()
    {
        SetupUser();
        SetupOwnedProfiles(
            new ChildProfile { Id = 5, OwnerUserId = 1, Nickname = "Bin", Status = ChildProfileStatus.Active },
            new ChildProfile { Id = 6, OwnerUserId = 1, Nickname = "Na", Status = ChildProfileStatus.Archived });

        await _sut.DeleteAccountAsync(1,
            new DeleteAccountRequestDto { CurrentPassword = "Pass@123", DeleteChildProfiles = true });

        _storySummaries.Verify(s => s.PurgeChildAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        _storySummaries.Verify(s => s.PurgeChildAsync(6, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_StillSucceeds_WhenStorySummaryPurgeFails()
    {
        var user = SetupUser();
        SetupOwnedProfiles(new ChildProfile { Id = 5, OwnerUserId = 1, Nickname = "Bin", Status = ChildProfileStatus.Active });
        _storySummaries.Setup(s => s.PurgeChildAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("storage down"));

        await _sut.DeleteAccountAsync(1,
            new DeleteAccountRequestDto { CurrentPassword = "Pass@123", DeleteChildProfiles = true });

        Assert.True(user.IsDeleted);
        _unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Chạy test để xác nhận thất bại**

Run: `dotnet build tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`
Expected: FAIL biên dịch: `'AccountDeletionService' does not contain a constructor that takes 8 arguments`.

- [ ] **Step 3: Sửa service**

`AccountDeletionService.cs`: thêm field `private readonly IParentStorySummaryService _storySummaries;`, thêm tham số `IParentStorySummaryService storySummaries` ngay trước `ILogger<AccountDeletionService> logger` trong constructor (dòng 29-36) và gán `_storySummaries = storySummaries;`. Thêm `using StoryPlatform.Application.Features.ReadingSessions.Interfaces;`.

Ngay sau khối `try { ... CommitTransactionAsync ... } catch { Rollback; throw; }` (trước vòng `foreach (var profile in erased)` ghi audit, ~dòng 120) chèn:

```csharp
        foreach (var profile in erased)
        {
            try
            {
                await _storySummaries.PurgeChildAsync(profile.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Story summary audio purge failed. ChildProfileId={ChildProfileId}, ErrorType={ErrorType}",
                    profile.Id, ex.GetType().Name);
            }
        }
```

- [ ] **Step 4: Chạy test để xác nhận đạt**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~AccountDeletionServiceTests"`
Expected: PASS.

- [ ] **Step 5: Chạy toàn bộ build và test**

Run: `dotnet build StoryPlatform.slnx && dotnet test StoryPlatform.slnx`
Expected: build 0 error; test 0 failed (số skipped giữ nguyên, 12 test Postgres).
