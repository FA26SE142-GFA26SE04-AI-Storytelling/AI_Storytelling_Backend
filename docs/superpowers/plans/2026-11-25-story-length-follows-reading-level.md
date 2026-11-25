# Độ dài truyện theo mức đọc, phụ huynh chỉnh được, AI viết theo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Con số "Độ dài truyện" ở tab An toàn tự theo mức đọc của bé, phụ huynh chỉnh tay được, và pipeline AI viết truyện đúng độ dài đó (±10%).

**Architecture:** Thêm cột nullable `SafetyPolicy.StoryLengthTarget` (null = theo mức đọc). Một lớp tĩnh `StoryLengthRules` ở backend là nơi duy nhất quy đổi (mức đọc, số tự chỉnh, trần cũ) thành (độ dài đích, trần) cho AI; `AIStoryInputService` dùng nó nên đổi mức đọc là độ dài đổi theo ngay, không cần ghi chéo giữa các service. Frontend thay thanh "Độ dài tối đa" bằng một control "Theo mức đọc / Tự chỉnh" ở tab An toàn và bước tạo hồ sơ; công thức hiển thị phản chiếu `StoryLengthRules`.

**Tech Stack:** .NET 10 / EF Core (Postgres) / xUnit + Moq; Next.js + TypeScript + Vitest.

**Spec:** Thiết kế đã được người dùng duyệt trong chat (phiên 2026-11-25). Tóm tắt ở mục "Thiết kế đã duyệt" ngay bên dưới; không có file spec riêng.

## Thiết kế đã duyệt

- Ý nghĩa mới: giá trị "Độ dài truyện" là **độ dài đích** AI viết, không còn chỉ là trần.
- Hai chế độ ở tab An toàn: **Theo mức đọc** (mặc định, `StoryLengthTarget = null`) và **Tự chỉnh** (lưu số từ phụ huynh chọn).
- Chỉ **một thanh** cho phụ huynh. Trần `MaxStoryLength` do hệ thống tự suy ra: khi lưu số tự chỉnh, trần tự nâng lên `max(trần cũ, ⌈1,1 × số từ⌉)`; trần không bao giờ bị hạ xuống. Các chức năng khác đang dùng `MaxStoryLength` (tự viết, lộ trình, truyện có sẵn) không bị đụng.
- Mức mặc định theo mức đọc **giữ nguyên hai bậc** (900 cho bé 6–8 tuổi mức 1–3, 1500 cho bé 9–10 tuổi hoặc mức 4–5). Không đổi mức 1–5 (người dùng sẽ cân nhắc sau).
- Bé hiện có (chưa chỉnh gì) hành xử như hiện nay. Riêng giới hạn cũ thấp hơn mặc định (ví dụ 700 < 900) vẫn được tôn trọng: coi là độ dài đích và trần, và từ nay AI viết 700 từ thay vì báo lỗi 400 khi `TargetLength = 0`.
- **Cảnh báo khi tự chỉnh dài hơn mức phù hợp** (chỉ frontend, không chặn lưu): hai bậc so với độ dài mặc định theo mức đọc của bé (900 hoặc 1500). Trên 110% mặc định → "Dài hơn mức phù hợp với bé X tuổi: bé khó tập trung, AI cần nhiều thời gian hơn để tạo". Trên 150% mặc định → "Rất dài": bé dễ mất tập trung, AI mất lâu hơn đáng kể để tạo, bạn cũng mất nhiều thời gian hơn để duyệt. Hai ngưỡng là hằng số trong `guardian.ts`, đổi một chỗ. Không cảnh báo khi chọn ngắn hơn mặc định.
- Bước duyệt của người, cổng an toàn, kiểm tra chất lượng: **không đổi**.

## Global Constraints

- Mọi chuỗi giao diện bằng tiếng Việt có dấu đầy đủ; mã định danh và commit message bằng tiếng Anh.
- Không đổi hợp đồng API theo hướng phá vỡ: chỉ **thêm** trường nullable `storyLengthTarget` vào request/response của SafetyPolicy.
- Không động vào các file không thuộc phạm vi đang nằm trong working tree: `Database/Seed-Database.ps1`, `tests/StoryPlatform.UnitTests/SeedDataAlignmentTests.cs`, `docs/api/ket-qua-doc-phu-huynh-api-test-guide.html`, `Database/Seed/80_*.sql`–`83_*.sql`, các file trong `docs/superpowers/` không phải plan này.
- Không tự commit/push; **không** thêm dòng `Co-Authored-By` vào commit (người dùng sẽ tự yêu cầu commit).
- Không in connection string, API key hay nội dung `appsettings.*.json`.
- **Không chạy `dotnet ef database update` lên DB dev khi chưa hỏi người dùng.**
- Lệnh test backend (Visual Studio giữ khóa DLL): `dotnet test tests/StoryPlatform.UnitTests -o C:\Users\Admin\AppData\Local\Temp\claude\8cbeb7c727623cd2\bae9f3b0-e2c6-4df6-b0c4-54d099255750\scratchpad\ut`. Khoảng 16 test tìm file seed/snapshot theo đường dẫn repo sẽ đỏ vì lý do môi trường; chỉ cần không có test nào đỏ **thêm** so với trước khi sửa.
- Frontend (`E:\Capstone\AI_Storytelling_Frontend\src`): chạy `npx tsc --noEmit -p .`, `npx eslint <file>`, `npx vitest run <đường dẫn>`.
- Backend chạy nhánh `feat/interactions`; frontend nhánh `fix/interactions` (hai repo riêng).

---

### Task 1: Cột `StoryLengthTarget` và lưu qua API Safety Policy

**Việc này làm gì (nói dễ hiểu):**
Thêm chỗ để lưu "độ dài truyện mà phụ huynh muốn" cho từng bé. Để trống nghĩa là "cứ theo mức đọc của bé". Khi phụ huynh chọn một số từ riêng, hệ thống lưu số đó và tự nâng giới hạn tối đa lên đủ rộng để AI có thể viết đúng số đó (cộng thêm 10%). Giới hạn cũ không bao giờ bị hạ xuống nên không ảnh hưởng các chức năng khác.

**Cần thêm / còn thiếu:**
- Đã có: bảng/entity `SafetyPolicy` với `MaxStoryLength`; endpoint lưu/đọc chính sách an toàn (`SafetyPolicyService`); DTO request/response; audit log khi lưu.
- Cần thêm: cột nullable `StoryLengthTarget` (kèm migration); trường `storyLengthTarget` trong request (khoảng 100–9000, null = theo mức đọc) và trong response; quy tắc tự nâng trần khi lưu số tự chỉnh; ghi `StoryLengthTarget` vào audit snapshot; lớp tĩnh `StoryLengthRules` (dùng lại ở Task 2).
- Còn thiếu / ngoài phạm vi: việc AI thực sự viết theo số này nằm ở Task 2; giao diện nằm ở Task 3–5; không đổi mức mặc định theo từng mức đọc 1–5.

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/ChildProfiles/Safety/StoryLengthRules.cs` — file mới, cần thiết vì cả `SafetyPolicyService` (Task 1) và `AIStoryInputService` (Task 2) phải dùng chung đúng một công thức; đặt trong service nào cũng tạo phụ thuộc chéo giữa hai feature. Chứa một lớp tĩnh với 3 hàm thuần.
- Create (do `dotnet ef` sinh): `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddSafetyPolicyStoryLengthTarget.cs` và `<timestamp>_AddSafetyPolicyStoryLengthTarget.Designer.cs`.
- Modify: `src/Core/StoryPlatform.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (do `dotnet ef` cập nhật).
- Modify: `src/Core/StoryPlatform.Domain/Entities/SafetyPolicy.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ChildProfiles/Safety/DTOs/SafetyPolicyDtos.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ChildProfiles/Safety/Services/SafetyPolicyService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ChildProfiles/Safety/SafetyPolicyServiceTests.cs`
- Test (tạo mới, cần thiết để kiểm công thức thuần mà không dựng service): `tests/StoryPlatform.UnitTests/Features/ChildProfiles/Safety/StoryLengthRulesTests.cs`

**Interfaces:**
- Produces: `StoryPlatform.Application.Features.ChildProfiles.Safety.StoryLengthRules` với
  - `public const decimal UpperTolerance = 1.1m;`
  - `public static int UpperBound(int target)` → `⌈target × 1,1⌉`
  - `public static int CapFor(int? storyLengthTarget, int requestedCap)`
  - `public static (int Target, int Cap) ForGeneration(int? storyLengthTarget, int storedCap, int levelDefaultTarget)`
- Produces: `SafetyPolicy.StoryLengthTarget` (`int?`), `SafetyPolicyDto.StoryLengthTarget` (`int?`), `SetSafetyPolicyRequestDto.StoryLengthTarget` (`int?`).

- [ ] **Step 1: Viết test thất bại cho `StoryLengthRules`**

Tạo `tests/StoryPlatform.UnitTests/Features/ChildProfiles/Safety/StoryLengthRulesTests.cs`:

```csharp
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Safety;

public class StoryLengthRulesTests
{
    [Theory]
    [InlineData(900, 990)]
    [InlineData(1500, 1650)]
    [InlineData(1200, 1320)]
    [InlineData(101, 112)] // 101 × 1,1 = 111,1 → làm tròn lên
    public void UpperBound_IsTenPercentAboveTargetRoundedUp(int target, int expected) =>
        Assert.Equal(expected, StoryLengthRules.UpperBound(target));

    [Fact]
    public void CapFor_WithCustomTarget_RaisesCapButNeverLowersIt()
    {
        Assert.Equal(1320, StoryLengthRules.CapFor(1200, 900));
        Assert.Equal(2000, StoryLengthRules.CapFor(1200, 2000));
    }

    [Fact]
    public void CapFor_WithoutTarget_KeepsRequestedCap() =>
        Assert.Equal(700, StoryLengthRules.CapFor(null, 700));

    [Fact]
    public void ForGeneration_CustomTarget_UsesTargetAndRaisedCap() =>
        Assert.Equal((1200, 1320), StoryLengthRules.ForGeneration(1200, 900, 900));

    [Theory]
    [InlineData(2000, 900, 900, 2000)]   // trần rộng: giữ trần
    [InlineData(900, 900, 900, 990)]     // trần bằng mặc định: nâng lên 110%
    [InlineData(1500, 1500, 1500, 1650)] // bé 9–10 tuổi hoặc mức 4–5
    public void ForGeneration_FollowsLevelDefaultWhenNoTarget(int storedCap, int levelDefault, int expectedTarget, int expectedCap) =>
        Assert.Equal((expectedTarget, expectedCap), StoryLengthRules.ForGeneration(null, storedCap, levelDefault));

    [Fact]
    public void ForGeneration_LegacyCapBelowDefault_IsHonouredAsTargetAndCap() =>
        Assert.Equal((700, 700), StoryLengthRules.ForGeneration(null, 700, 900));
}
```

- [ ] **Step 2: Chạy để thấy thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests -o C:\Users\Admin\AppData\Local\Temp\claude\8cbeb7c727623cd2\bae9f3b0-e2c6-4df6-b0c4-54d099255750\scratchpad\ut --filter "FullyQualifiedName~StoryLengthRulesTests"`
Expected: lỗi biên dịch `The type or namespace name 'StoryLengthRules' could not be found`.

- [ ] **Step 3: Tạo `StoryLengthRules`**

Tạo `src/Core/StoryPlatform.Application/Features/ChildProfiles/Safety/StoryLengthRules.cs`:

```csharp
namespace StoryPlatform.Application.Features.ChildProfiles.Safety;

/// <summary>
/// Quy đổi "độ dài truyện" của phụ huynh thành độ dài đích và trần dùng khi sinh truyện.
/// Công thức này được frontend phản chiếu ở <c>effectiveStoryLength</c> (src/app/lib/guardian.ts); đổi ở đâu thì đổi cả hai.
/// </summary>
public static class StoryLengthRules
{
    /// <summary>AI được viết tối đa 110% độ dài đích (khớp LearningGenerationProfile.ForAge).</summary>
    public const decimal UpperTolerance = 1.1m;

    public static int UpperBound(int target) => (int)Math.Ceiling(target * UpperTolerance);

    /// <summary>
    /// Trần lưu trong cài đặt an toàn. Có độ dài tự chỉnh thì trần tự nâng để không thấp hơn số từ AI được phép viết;
    /// không bao giờ hạ trần đang có.
    /// </summary>
    public static int CapFor(int? storyLengthTarget, int requestedCap) =>
        storyLengthTarget is > 0 ? Math.Max(requestedCap, UpperBound(storyLengthTarget.Value)) : requestedCap;

    /// <summary>Độ dài đích và trần dùng khi sinh truyện cho một bé.</summary>
    /// <param name="storyLengthTarget">Số phụ huynh tự chỉnh; null = theo mức đọc.</param>
    /// <param name="storedCap"><c>SafetyPolicy.MaxStoryLength</c> đang lưu.</param>
    /// <param name="levelDefaultTarget">Độ dài mặc định theo nhóm tuổi và mức đọc (900 hoặc 1500).</param>
    public static (int Target, int Cap) ForGeneration(int? storyLengthTarget, int storedCap, int levelDefaultTarget)
    {
        if (storyLengthTarget is > 0)
        {
            return (storyLengthTarget.Value, CapFor(storyLengthTarget, storedCap));
        }

        // Giới hạn thấp hơn mặc định do phụ huynh đặt từ trước khi có "độ dài truyện": giữ nguyên làm độ dài đích và trần.
        if (storedCap < levelDefaultTarget)
        {
            return (storedCap, storedCap);
        }

        return (levelDefaultTarget, Math.Max(storedCap, UpperBound(levelDefaultTarget)));
    }
}
```

- [ ] **Step 4: Chạy để thấy đạt**

Run: cùng lệnh Step 2.
Expected: PASS (tất cả test trong `StoryLengthRulesTests`).

- [ ] **Step 5: Viết test thất bại cho `SafetyPolicyService`**

Thêm vào `SafetyPolicyServiceTests.cs`, ngay sau test `SetSafetyPolicyAsync_NewPolicy_CreatesCategoriesAndConsentTimestamp` (dùng lại `AllowPermission()` và `ValidRequest()` có sẵn):

```csharp
    [Fact]
    public async Task SetSafetyPolicyAsync_CustomStoryLength_SavesTargetAndRaisesCapToUpperBound()
    {
        AllowPermission();
        _policyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync((SafetyPolicy?)null);
        SafetyPolicy? added = null;
        _policyRepo.Setup(r => r.AddAsync(It.IsAny<SafetyPolicy>(), It.IsAny<CancellationToken>()))
            .Callback<SafetyPolicy, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((SafetyPolicy value, CancellationToken _) => value);
        var request = ValidRequest();
        request.MaxStoryLength = 900;
        request.StoryLengthTarget = 1200;

        var result = await _sut.SetSafetyPolicyAsync(1, 2, request);

        Assert.Equal(1200, added!.StoryLengthTarget);
        Assert.Equal(1320, added.MaxStoryLength);
        Assert.Equal(1200, result.StoryLengthTarget);
        Assert.Equal(1320, result.MaxStoryLength);
        Assert.Contains("\"StoryLengthTarget\":1200", _audits.Single(a => a.Action == "SET_SAFETY_POLICY").After);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_NoStoryLength_StoresNullAndKeepsRequestedCap()
    {
        AllowPermission();
        _policyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync((SafetyPolicy?)null);
        SafetyPolicy? added = null;
        _policyRepo.Setup(r => r.AddAsync(It.IsAny<SafetyPolicy>(), It.IsAny<CancellationToken>()))
            .Callback<SafetyPolicy, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((SafetyPolicy value, CancellationToken _) => value);

        var result = await _sut.SetSafetyPolicyAsync(1, 2, ValidRequest());

        Assert.Null(added!.StoryLengthTarget);
        Assert.Equal(1500, added.MaxStoryLength);
        Assert.Null(result.StoryLengthTarget);
    }

    [Fact]
    public async Task SetSafetyPolicyAsync_SwitchBackToLevel_ClearsTargetAndDoesNotLowerCap()
    {
        AllowPermission();
        var policy = new SafetyPolicy { Id = 5, ChildProfileId = 1, StoryLengthTarget = 1200, MaxStoryLength = 1320 };
        _policyRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        _categoryRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SafetyPolicyCategory, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(new List<SafetyPolicyCategory>());
        var request = ValidRequest();
        request.MaxStoryLength = 1320;
        request.StoryLengthTarget = null;

        var result = await _sut.SetSafetyPolicyAsync(1, 2, request);

        Assert.Null(policy.StoryLengthTarget);
        Assert.Equal(1320, policy.MaxStoryLength);
        Assert.Null(result.StoryLengthTarget);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(9001)]
    public async Task SetSafetyPolicyAsync_StoryLengthOutOfRange_ThrowsBadRequest(int target)
    {
        AllowPermission();
        var request = ValidRequest();
        request.StoryLengthTarget = target;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetSafetyPolicyAsync(1, 2, request));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 6: Chạy để thấy thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests -o C:\Users\Admin\AppData\Local\Temp\claude\8cbeb7c727623cd2\bae9f3b0-e2c6-4df6-b0c4-54d099255750\scratchpad\ut --filter "FullyQualifiedName~SafetyPolicyServiceTests"`
Expected: lỗi biên dịch `'SetSafetyPolicyRequestDto' does not contain a definition for 'StoryLengthTarget'`.

- [ ] **Step 7: Thêm thuộc tính vào entity và DTO**

Trong `SafetyPolicy.cs`, ngay sau dòng `public int MaxStoryLength { get; set; }`:

```csharp
    /// <summary>
    /// Độ dài truyện (số từ) phụ huynh tự chỉnh; NULL = theo mức đọc của bé (900 hoặc 1500). AI viết khoảng ±10% quanh số này.
    /// </summary>
    public int? StoryLengthTarget { get; set; }
```

Trong `SafetyPolicyDtos.cs`, class `SafetyPolicyDto`, sau `public int MaxStoryLength { get; set; }`:

```csharp
    public int? StoryLengthTarget { get; set; }
```

Trong `SetSafetyPolicyRequestDto`, sau thuộc tính `MaxStoryLength` (kèm dòng `[Range]` của nó):

```csharp
    /// <summary>Độ dài truyện phụ huynh tự chỉnh; null = theo mức đọc. Khi có giá trị, trần MaxStoryLength tự nâng lên ít nhất 110% số này.</summary>
    [Range(100, 9000, ErrorMessage = "Độ dài truyện mong muốn phải từ 100 đến 9000 từ.")]
    public int? StoryLengthTarget { get; set; }
```

- [ ] **Step 8: Cập nhật `SafetyPolicyService`**

Trong `ApplyRequest` (đầu hàm), thay dòng `policy.MaxStoryLength = request.MaxStoryLength;` bằng:

```csharp
        policy.StoryLengthTarget = request.StoryLengthTarget;
        policy.MaxStoryLength = StoryLengthRules.CapFor(request.StoryLengthTarget, request.MaxStoryLength);
```

Trong `Snapshot(...)`, ngay sau `policy.MaxStoryLength,` thêm:

```csharp
        policy.StoryLengthTarget,
```

Trong `MapToDto(...)`, ngay sau `MaxStoryLength = policy.MaxStoryLength,` thêm:

```csharp
        StoryLengthTarget = policy.StoryLengthTarget,
```

Trong `ValidateRequest(...)`, ngay sau khối kiểm `MaxStoryLength`:

```csharp
        if (request.StoryLengthTarget is < 100 or > 9000)
        {
            throw new BadRequestException(
                "Độ dài truyện mong muốn phải từ 100 đến 9000 từ.");
        }
```

(`StoryLengthRules` cùng namespace `StoryPlatform.Application.Features.ChildProfiles.Safety` đã được `using` ở đầu file.)

- [ ] **Step 9: Chạy để thấy đạt**

Run: cùng lệnh Step 6.
Expected: PASS toàn bộ `SafetyPolicyServiceTests` (các test cũ vẫn đạt vì `StoryLengthTarget` mặc định null).

- [ ] **Step 10: Sinh migration (chưa áp lên DB)**

Run (từ thư mục gốc backend): `dotnet ef migrations add AddSafetyPolicyStoryLengthTarget -p src/Core/StoryPlatform.Infrastructure -s src/Core/StoryPlatform.Api`
Nếu báo lỗi sao chép DLL vì Visual Studio giữ khóa: dừng debug/đóng Visual Studio rồi chạy lại.
Expected: tạo 2 file migration mới và cập nhật `ApplicationDbContextModelSnapshot.cs`.
Kiểm tra bằng mắt file `*_AddSafetyPolicyStoryLengthTarget.cs`: phần `Up` **chỉ** có một `migrationBuilder.AddColumn<int>(name: "StoryLengthTarget", table: <bảng của SafetyPolicy>, nullable: true)` và `Down` chỉ có `DropColumn` tương ứng. Nếu có thêm thao tác nào khác (do model đang lệch với snapshot vì thay đổi chưa commit của người khác) thì **dừng và báo người dùng**, không tự xóa dòng.
**Không** chạy `dotnet ef database update`.

---

### Task 2: AI sinh truyện dùng độ dài của phụ huynh

**Việc này làm gì (nói dễ hiểu):**
Làm cho phần tạo truyện thật sự dùng con số của phụ huynh. Nếu phụ huynh tự chỉnh thì AI viết đúng số đó; nếu để "theo mức đọc" thì AI viết 900 hoặc 1500 từ tùy mức đọc hiện tại của bé, và khi phụ huynh đổi mức đọc thì lần tạo truyện sau tự dùng độ dài mới. Form tạo truyện cũng lấy số này làm mức "Vừa".

**Cần thêm / còn thiếu:**
- Đã có: `AIStoryInputService` dựng ngữ cảnh sinh truyện từ `SafetyPolicy` và `LearningProfile`; `LearningBatchOptions.Profile(age, requested, readingLevel)` quy đổi ra số từ (±10%); đoạn kiểm tra độ dài đích không vượt trần.
- Cần thêm: dùng `StoryLengthRules.ForGeneration` để tính (độ dài đích, trần) khi dựng ngữ cảnh; `RecommendedTargetLength` và trường hợp `TargetLength = 0` lấy theo độ dài đích đó; sửa test cũ `ParentAgeDefaultNeverRaisesExistingChildPolicyLimit` cho khớp hành vi mới (giới hạn cũ thấp hơn mặc định vẫn không bị nâng, nhưng nay sinh truyện đúng bằng giới hạn thay vì báo lỗi 400).
- Còn thiếu / ngoài phạm vi: luồng Creator (nhà sáng tạo) không đổi; các chức năng khác dùng `MaxStoryLength` (lộ trình, tự viết) vẫn dùng số đã lưu, không dùng công thức mới (chênh tối đa 10% ở chế độ "Theo mức đọc", chấp nhận được).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/AIStoryInput/Services/AIStoryInputService.cs` (hàm `SubmitAsync` quanh dòng 117, `ResolveContextAsync` quanh dòng 772–808, `ToContextDto` quanh dòng 952, record `EffectiveContext` quanh dòng 992)
- Test: `tests/StoryPlatform.UnitTests/AIStoryInputServiceTests.cs`

**Interfaces:**
- Consumes: `StoryLengthRules.ForGeneration(int? storyLengthTarget, int storedCap, int levelDefaultTarget)` và `SafetyPolicy.StoryLengthTarget` từ Task 1; `LearningBatchOptions.Profile(string age, int requested = 0, int? readingLevel = null)` (có sẵn).
- Produces: `EffectiveContext.DefaultTargetLength` (`int?`, tham số cuối của record, mặc định null nên luồng Creator không phải đổi).

- [ ] **Step 1: Viết test thất bại (và sửa test cũ)**

Trong `AIStoryInputServiceTests.cs`, **thay** test `ParentAgeDefaultNeverRaisesExistingChildPolicyLimit` (khoảng dòng 176–183) bằng các test sau. `CreateEligibleUnitOfWork()` có sẵn: bé 6–8 tuổi, mức đọc 2, `MaxStoryLength = 700`.

```csharp
    [Fact]
    public async Task LegacyLimitBelowLevelDefault_IsHonouredAsTargetAndCap()
    {
        var store = CreateEligibleUnitOfWork(); // trần 700 < mặc định 900
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));

        var context = await service.GetContextAsync(1, 1);
        Assert.Equal(700, context.RecommendedTargetLength);
        Assert.Equal(700, context.MaximumLength);

        await service.SubmitAsync(1, ValidRequest(targetLength: 0));
        Assert.Equal(700, StoredInput(store).TargetLength);
    }

    [Fact]
    public async Task CustomStoryLength_IsTheRecommendedAndDefaultTarget()
    {
        var store = CreateEligibleUnitOfWork();
        var policy = store.Items<SafetyPolicy>().Single();
        policy.StoryLengthTarget = 1200;
        policy.MaxStoryLength = 1320;
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));

        var context = await service.GetContextAsync(1, 1);
        Assert.Equal(1200, context.RecommendedTargetLength);
        Assert.Equal(1320, context.MaximumLength);

        await service.SubmitAsync(1, ValidRequest(targetLength: 0));
        Assert.Equal(1200, StoredInput(store).TargetLength);
    }

    [Theory]
    [InlineData(2, 900)]
    [InlineData(4, 1500)]
    public async Task WithoutCustomLength_TargetFollowsReadingLevel(int readingLevel, int words)
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<LearningProfile>().Single().ReadingLevel = readingLevel;
        store.Items<SafetyPolicy>().Single().MaxStoryLength = 2000;
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));

        var context = await service.GetContextAsync(1, 1);
        Assert.Equal(words, context.RecommendedTargetLength);
        Assert.Equal(2000, context.MaximumLength);

        await service.SubmitAsync(1, ValidRequest(targetLength: 0));
        Assert.Equal(words, StoredInput(store).TargetLength);
    }

    [Fact]
    public async Task WithoutCustomLength_CapCoversUpperBoundOfLevelDefault()
    {
        var store = CreateEligibleUnitOfWork();
        store.Items<SafetyPolicy>().Single().MaxStoryLength = 900; // bằng mặc định, chưa đủ 110%
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));

        var context = await service.GetContextAsync(1, 1);

        Assert.Equal(900, context.RecommendedTargetLength);
        Assert.Equal(990, context.MaximumLength);
    }
```

Thêm helper cùng lớp (đặt cạnh `ValidRequest`):

```csharp
    private static AcceptedAIStoryInputSnapshot StoredInput(FakeUnitOfWork store) =>
        JsonSerializer.Deserialize<AcceptedAIStoryInputSnapshot>(
            Assert.Single(store.Items<StoryGenerationRequest>()).AcceptedInputJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
```

- [ ] **Step 2: Chạy để thấy thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests -o C:\Users\Admin\AppData\Local\Temp\claude\8cbeb7c727623cd2\bae9f3b0-e2c6-4df6-b0c4-54d099255750\scratchpad\ut --filter "FullyQualifiedName~AIStoryInputServiceTests"`
Expected: các test mới FAIL (ví dụ `LegacyLimit...` lỗi `BadRequestException` vì `TargetLength` 900 > trần 700; `WithoutCustomLength_...` sai số).

- [ ] **Step 3: Sửa `AIStoryInputService`**

(a) Trong `ResolveContextAsync`, ngay sau dòng `var ageBand = child.AgeBand == AgeBand.Age_6_8 ? "6-8" : "9-10";` thêm:

```csharp
        var storyLength = StoryLengthRules.ForGeneration(
            safety.StoryLengthTarget,
            safety.MaxStoryLength,
            _learningOptions.Profile(ageBand, readingLevel: learning.ReadingLevel).TargetWords);
```

(b) Trong lệnh `new AIStoryInputContextSnapshot(...)` ngay bên dưới, đổi đối số thứ sáu từ `maximumLength,` thành `storyLength.Cap,`.

(c) Trong `return new EffectiveContext(...)`, đổi `maximumLength,` thành `storyLength.Cap,` và thêm đối số cuối `storyLength.Target` sau `restrictedTerms`:

```csharp
            blockedTerms,
            restrictedTerms,
            storyLength.Target);
```

(biến `maximumLength` ở đầu hàm vẫn giữ để kiểm `<= 0` như cũ.)

(d) Record `EffectiveContext` (cuối file): thêm tham số cuối có mặc định:

```csharp
        IReadOnlyList<string> RestrictedTerms,
        int? DefaultTargetLength = null);
```

(e) Trong `SubmitAsync`, thay dòng `var profile = _learningOptions.Profile(context.AgeBand, normalized.TargetLength, context.ReadingLevel);` bằng:

```csharp
        var profile = _learningOptions.Profile(context.AgeBand,
            normalized.TargetLength > 0 ? normalized.TargetLength : context.DefaultTargetLength ?? 0, context.ReadingLevel);
```

(f) Trong `ToContextDto`, thay dòng `RecommendedTargetLength = ...` bằng:

```csharp
        RecommendedTargetLength = _learningOptions.Profile(context.AgeBand, context.DefaultTargetLength ?? 0, context.ReadingLevel).TargetWords,
```

Thêm `using StoryPlatform.Application.Features.ChildProfiles.Safety;` ở đầu file nếu chưa có.

- [ ] **Step 4: Chạy để thấy đạt**

Run: cùng lệnh Step 2.
Expected: PASS toàn bộ `AIStoryInputServiceTests`.

- [ ] **Step 5: Chạy cả bộ unit test backend để tìm hồi quy**

Run: `dotnet test tests/StoryPlatform.UnitTests -o C:\Users\Admin\AppData\Local\Temp\claude\8cbeb7c727623cd2\bae9f3b0-e2c6-4df6-b0c4-54d099255750\scratchpad\ut`
Expected: không có test nào đỏ ngoài nhóm ~16 test môi trường (tìm file seed/snapshot theo đường dẫn repo). Nếu một test dùng `MaxStoryLength < 900` với `TargetLength = 0` đổi kết quả (ví dụ trong `AIStoryInputServiceWishTests`), cập nhật kỳ vọng theo hành vi mới đã nêu ở Task này, không sửa logic.

---

### Task 3: Kiểu dữ liệu và hàm tính độ dài hiển thị ở frontend

**Việc này làm gì (nói dễ hiểu):**
Dạy frontend biết thêm trường "độ dài truyện phụ huynh chọn" và cách tính con số nên hiện cho phụ huynh: nếu phụ huynh đã chọn riêng thì hiện số đó, nếu chưa thì hiện theo mức đọc của bé (900 hoặc 1500). Hàm này giống hệt công thức ở backend nên màn hình luôn khớp với số AI thật sự viết. Thêm luôn hàm tạo lời cảnh báo khi phụ huynh chọn dài hơn mức phù hợp với tuổi của bé (kèm nhắc thời gian tạo truyện sẽ lâu hơn).

**Cần thêm / còn thiếu:**
- Đã có: `storyLengthFor(ageBand, level)` trong `guardian.ts` (900/1500 và khoảng ±10%); `AGE_BANDS` (nhãn "6–8 tuổi"); kiểu `SafetyPolicy` và `SetSafetyPolicyRequest`; `MAX_STORY_LENGTH_RANGE`.
- Cần thêm: trường `storyLengthTarget` ở hai kiểu và ở hai interface hợp đồng; hàm `effectiveStoryLength`; hàm `storyLengthAdvice` (hai bậc cảnh báo); test cho cả hai.
- Còn thiếu / ngoài phạm vi: giao diện dùng hai hàm này nằm ở Task 4–5. Form tạo truyện (chọn Ngắn/Vừa/Dài khi viết từng truyện) chưa có cảnh báo này; nếu muốn thì làm riêng sau.

**Files:**
- Modify: `E:\Capstone\AI_Storytelling_Frontend\src\app\types\childProfile.ts`
- Modify: `E:\Capstone\AI_Storytelling_Frontend\src\app\types\backendContracts.ts` (interface `SafetyPolicyDto` quanh dòng 1786 và `SetSafetyPolicyRequestDto` quanh dòng 1878)
- Modify: `E:\Capstone\AI_Storytelling_Frontend\src\app\lib\guardian.ts`
- Test: `E:\Capstone\AI_Storytelling_Frontend\src\tests\unit\guardian\guardian.test.ts`

**Interfaces:**
- Produces: `effectiveStoryLength(ageBand: string | undefined, level: number, storyLengthTarget: number | null | undefined, maxStoryLength: number): { value: number; custom: boolean }`
- Produces: `storyLengthAdvice(ageBand: string | undefined, level: number, value: number): { severity: "long" | "very-long"; text: string } | null` (null = không cần cảnh báo).
- Produces: `STORY_LENGTH_LONG_RATIO = 1.1`, `STORY_LENGTH_VERY_LONG_RATIO = 1.5` (hằng số ngưỡng, so với độ dài mặc định theo mức đọc).
- Produces: `SafetyPolicy.storyLengthTarget?: number | null`, `SetSafetyPolicyRequest.storyLengthTarget?: number | null`.

- [ ] **Step 1: Viết test thất bại**

Trong `guardian.test.ts`, đổi dòng import đầu thành `import { effectiveStoryLength, groupCategories, passwordProblem, presetBlocked, storyLengthAdvice } from '@/app/lib/guardian';` rồi thêm cuối file:

```ts
describe('effectiveStoryLength', () => {
  it('theo mức đọc khi chưa tự chỉnh: 900 cho bé 6–8 tuổi mức 1–3, 1500 cho mức 4–5 hoặc 9–10 tuổi', () => {
    expect(effectiveStoryLength('Age_6_8', 2, null, 2000)).toEqual({ value: 900, custom: false });
    expect(effectiveStoryLength('Age_6_8', 4, undefined, 2000)).toEqual({ value: 1500, custom: false });
    expect(effectiveStoryLength('Age_9_10', 1, null, 2000)).toEqual({ value: 1500, custom: false });
  });

  it('ưu tiên số phụ huynh tự chỉnh', () => {
    expect(effectiveStoryLength('Age_6_8', 2, 1200, 1320)).toEqual({ value: 1200, custom: true });
  });

  it('giới hạn cũ thấp hơn mặc định được coi là tự chỉnh bằng đúng giới hạn đó (khớp backend StoryLengthRules)', () => {
    expect(effectiveStoryLength('Age_6_8', 2, null, 700)).toEqual({ value: 700, custom: true });
  });

  it('trần bằng hoặc cao hơn mặc định thì vẫn là theo mức đọc', () => {
    expect(effectiveStoryLength('Age_6_8', 2, null, 900)).toEqual({ value: 900, custom: false });
  });
});

describe('storyLengthAdvice', () => {
  it('không cảnh báo khi bằng, ngắn hơn hoặc dài không quá 110% mức mặc định', () => {
    expect(storyLengthAdvice('Age_6_8', 2, 600)).toBeNull();
    expect(storyLengthAdvice('Age_6_8', 2, 900)).toBeNull();
    expect(storyLengthAdvice('Age_6_8', 2, 990)).toBeNull();
  });

  it('cảnh báo dài hơn mức phù hợp, nêu nhóm tuổi của bé và việc tạo truyện lâu hơn', () => {
    const advice = storyLengthAdvice('Age_6_8', 2, 1200);
    expect(advice?.severity).toBe('long');
    expect(advice?.text).toContain('6–8 tuổi');
    expect(advice?.text).toMatch(/thời gian/);
  });

  it('cảnh báo rất dài khi vượt 150% mức mặc định', () => {
    const advice = storyLengthAdvice('Age_6_8', 2, 1400);
    expect(advice?.severity).toBe('very-long');
    expect(advice?.text).toMatch(/Rất dài/);
    expect(advice?.text).toMatch(/duyệt/);
  });

  it('so với mức mặc định của bé 9–10 tuổi (1500), không phải 900', () => {
    expect(storyLengthAdvice('Age_9_10', 3, 1500)).toBeNull();
    expect(storyLengthAdvice('Age_9_10', 3, 1700)?.severity).toBe('long');
    expect(storyLengthAdvice('Age_9_10', 3, 2300)?.severity).toBe('very-long');
  });
});
```

- [ ] **Step 2: Chạy để thấy thất bại**

Run (trong `E:\Capstone\AI_Storytelling_Frontend\src`): `npx vitest run tests/unit/guardian`
Expected: FAIL — `effectiveStoryLength is not a function` (hoặc lỗi import).

- [ ] **Step 3: Thêm kiểu và hàm**

`childProfile.ts`, trong `SetSafetyPolicyRequest`, ngay sau dòng `maxStoryLength: number; // 100 to 20000`:

```ts
  /** Độ dài truyện phụ huynh tự chỉnh (100–9000 từ); null/bỏ trống = theo mức đọc của bé. */
  storyLengthTarget?: number | null;
```

`childProfile.ts`, trong `SafetyPolicy`, ngay sau `maxStoryLength: number;`:

```ts
  /** Độ dài truyện phụ huynh tự chỉnh; null = theo mức đọc của bé. */
  storyLengthTarget?: number | null;
```

`backendContracts.ts`: trong `SafetyPolicyDto`, sau dòng `'maxStoryLength': number;` thêm `'storyLengthTarget'?: number | null;`; trong `SetSafetyPolicyRequestDto`, sau dòng `'maxStoryLength': number;` thêm `'storyLengthTarget'?: number | null;`.

`guardian.ts`, ngay sau hàm `storyLengthFor` (và trước khối `MAX_STORY_LENGTH_RANGE`):

```ts
/**
 * Độ dài truyện hiển thị ở tab An toàn. Phản chiếu StoryLengthRules.ForGeneration ở backend:
 * số phụ huynh tự chỉnh nếu có; chưa có thì theo mức đọc, trừ khi giới hạn cũ thấp hơn mặc định (phụ huynh đã đặt từ trước)
 * thì coi giới hạn đó là số tự chỉnh.
 */
export const effectiveStoryLength = (
  ageBand: string | undefined,
  level: number,
  storyLengthTarget: number | null | undefined,
  maxStoryLength: number,
): { value: number; custom: boolean } => {
  if (typeof storyLengthTarget === "number" && storyLengthTarget > 0) return { value: storyLengthTarget, custom: true };
  const byLevel = storyLengthFor(ageBand, level).target;
  if (maxStoryLength > 0 && maxStoryLength < byLevel) return { value: maxStoryLength, custom: true };
  return { value: byLevel, custom: false };
};

/** Ngưỡng cảnh báo so với độ dài mặc định theo mức đọc: trên 110% là "dài", trên 150% là "rất dài". */
export const STORY_LENGTH_LONG_RATIO = 1.1;
export const STORY_LENGTH_VERY_LONG_RATIO = 1.5;

/**
 * Cảnh báo khi phụ huynh tự chỉnh dài hơn mức phù hợp với tuổi và mức đọc của bé. Không chặn lưu; null = không cần cảnh báo.
 * Truyện dài hơn thì bé khó tập trung, AI mất nhiều thời gian hơn để tạo và phụ huynh mất nhiều thời gian hơn để duyệt.
 */
export const storyLengthAdvice = (
  ageBand: string | undefined,
  level: number,
  value: number,
): { severity: "long" | "very-long"; text: string } | null => {
  const recommended = storyLengthFor(ageBand, level).target;
  const who = AGE_BANDS.find((a) => a.id === ageBand)?.label;
  const forWho = who ? `bé ${who}` : "bé";
  const about = recommended.toLocaleString("vi-VN");
  if (value > recommended * STORY_LENGTH_VERY_LONG_RATIO) {
    return {
      severity: "very-long",
      text: `Rất dài so với mức phù hợp với ${forWho} (khoảng ${about} từ). Bé dễ mất tập trung, AI sẽ mất lâu hơn đáng kể để tạo truyện và bạn cũng cần nhiều thời gian hơn để duyệt.`,
    };
  }
  if (value > recommended * STORY_LENGTH_LONG_RATIO) {
    return {
      severity: "long",
      text: `Dài hơn mức phù hợp với ${forWho} (khoảng ${about} từ). Bé có thể khó tập trung đọc hết, và AI cần nhiều thời gian hơn để tạo truyện.`,
    };
  }
  return null;
};
```

Lưu ý: `AGE_BANDS` được khai báo ở đầu `guardian.ts` (trước hàm này) nên dùng được ngay.

- [ ] **Step 4: Chạy để thấy đạt**

Run: `npx vitest run tests/unit/guardian` rồi `npx tsc --noEmit -p .`
Expected: test PASS; `tsc` không lỗi.

---

### Task 4: Control "Độ dài truyện" ở tab An toàn

**Việc này làm gì (nói dễ hiểu):**
Thay thanh "Độ dài tối đa của truyện" ở tab An toàn bằng mục "Độ dài truyện" có hai nút: "Theo mức đọc" (số tự đổi khi đổi mức đọc ở tab Học tập) và "Tự chỉnh" (kéo thanh để chọn số từ riêng). Có dòng giải thích AI sẽ viết khoảng bao nhiêu từ. Tab vẫn tự lưu như cũ.

**Cần thêm / còn thiếu:**
- Đã có: `SafetyWorkspace.tsx` với thanh trượt `len`, cơ chế tự lưu (so sánh `settingsKey`), các lớp CSS `th-val-pill`, `th-range`, `seg`.
- Cần thêm: tải mức đọc của bé (hồ sơ học tập) cùng lúc với chính sách; hai trạng thái `lenCustom`, `level`; control mới; gửi `storyLengthTarget` và trần khi lưu; hộp cảnh báo (vàng) khi tự chỉnh dài hơn mức phù hợp với tuổi và mức đọc của bé, nhắc thời gian tạo truyện sẽ lâu hơn (dùng `storyLengthAdvice` của Task 3).
- Còn thiếu / ngoài phạm vi: không thêm CSS mới nếu các lớp có sẵn đủ dùng; nếu giao diện lệch sẽ chỉnh ở Task 6 khi xem trình duyệt.

**Files:**
- Modify: `E:\Capstone\AI_Storytelling_Frontend\src\app\features\safety\SafetyWorkspace.tsx`

**Interfaces:**
- Consumes: `effectiveStoryLength`, `storyLengthFor`, `defaultReadingLevel`, `MAX_STORY_LENGTH_RANGE` từ `@/app/lib/guardian` (Task 3); `childProfileService.getLearningProfile(childId)` (có sẵn, trả `ApiResponse<LearningProfile>` với `readingLevel: number`); `SafetyPolicy.storyLengthTarget`, `SetSafetyPolicyRequest.storyLengthTarget` (Task 3).
- Produces: hành vi: gửi `storyLengthTarget = len` khi `lenCustom`, ngược lại `null`.

- [ ] **Step 1: Sửa import**

Đổi dòng `import { fmtDate, presetBlocked } from "@/app/lib/guardian";` thành:

```tsx
import { MAX_STORY_LENGTH_RANGE, defaultReadingLevel, effectiveStoryLength, fmtDate, presetBlocked, storyLengthAdvice, storyLengthFor } from "@/app/lib/guardian";
```

Trong dòng import `lucide-react` hiện có (`import { Clock, FileCheck, Inbox, Target, ShieldCheck, Sparkles, Mic, Trash2 } from "lucide-react";`), thêm `TriangleAlert` vào danh sách.

- [ ] **Step 2: Thêm trạng thái**

Ngay sau `const [len, setLen] = useState(2000);` thêm:

```tsx
  // Độ dài truyện: false = theo mức đọc (số tự đổi), true = phụ huynh tự chỉnh `len`.
  const [lenCustom, setLenCustom] = useState(false);
  const [level, setLevel] = useState(1);
```

- [ ] **Step 3: Cho `apply` nhận mức đọc**

Đổi chữ ký `const apply = useCallback((pol: SafetyPolicy | null, active: ContentCategory[], ageBand: string) => {` thành:

```tsx
  const apply = useCallback((pol: SafetyPolicy | null, active: ContentCategory[], ageBand: string, lvl: number) => {
```

Trong nhánh `if (pol) {`, thay `setLen(pol.maxStoryLength);` bằng:

```tsx
      const eff = effectiveStoryLength(ageBand, lvl, pol.storyLengthTarget, pol.maxStoryLength);
      setLenCustom(eff.custom);
      setLen(eff.value);
```

Trong nhánh `else {`, thay `setLen(2000);` bằng:

```tsx
      setLenCustom(false);
      setLen(storyLengthFor(ageBand, lvl).target);
```

- [ ] **Step 4: Tải mức đọc cùng lúc**

Trong `useEffect` tải dữ liệu, thay khối `void Promise.all([...]).then(([c, p]) => { ... });` bằng:

```tsx
    void Promise.all([
      childProfileService.getContentCategories(),
      childProfileService.getSafetyPolicy(childId),
      childProfileService.getLearningProfile(childId),
    ]).then(([c, p, lp]) => {
      if (!alive) return;
      const active = (c.data ?? []).filter((x) => x.isActive);
      const lvl = lp.success && lp.data ? lp.data.readingLevel : defaultReadingLevel(childAge ?? "");
      setCats(active);
      setLevel(lvl);
      apply(p.success ? p.data : null, active, childAge ?? "", lvl);
      setLoading(false);
    });
```

- [ ] **Step 4b: Cập nhật ba chỗ gọi `apply` còn lại**

- Trong `save`: `if (res.data) apply(res.data, cats, child.ageBand);` → `if (res.data) apply(res.data, cats, child.ageBand, level);`
- Trong `reapplyPreset`: `if (res.success && res.data) apply(res.data, cats, child.ageBand);` → `... apply(res.data, cats, child.ageBand, level);`
- Trong `removePolicy`: `apply(null, cats, child.ageBand);` → `apply(null, cats, child.ageBand, level);`

- [ ] **Step 5: Đưa `lenCustom` vào khóa tự lưu và gửi khi lưu**

`const settingsKey = JSON.stringify([mode, len, gate, ...` → thêm `lenCustom` sau `len`:

```tsx
  const settingsKey = JSON.stringify([mode, len, lenCustom, gate, wishes, interactive, features, threshold, safetyTh, readTh, voice, rules, consent]);
```

Trong `save`, thay dòng `maxStoryLength: len,` bằng (và khai báo `storyLen` ngay trước lệnh `const savePolicy = ...`):

```tsx
    const storyLen = lenCustom ? len : storyLengthFor(child.ageBand, level).target;
```

```tsx
      // Trần tự suy ra: không bao giờ thấp hơn 110% độ dài đang dùng, và không hạ trần đang lưu (backend cũng nâng như vậy).
      maxStoryLength: Math.max(policy?.maxStoryLength ?? 0, Math.ceil(storyLen * 1.1)),
      storyLengthTarget: lenCustom ? len : null,
```

- [ ] **Step 6: Thay control trong mục "Giới hạn và quyền"**

Sau `if (!child) return ...` và trước `const reapplyPreset`, thêm (hai hằng dẫn xuất, không phải hook):

```tsx
  const levelLen = storyLengthFor(child.ageBand, level);
  const shownLen = lenCustom ? len : levelLen.target;
  // Chỉ cảnh báo khi phụ huynh tự chỉnh dài hơn mức phù hợp; theo mức đọc thì không cần.
  const lenAdvice = lenCustom ? storyLengthAdvice(child.ageBand, level, len) : null;
```

Thay toàn bộ khối `<div className="field" style={{ margin: "10px 0 2px" }}> ... </div>` hiện có (thanh "Độ dài tối đa của truyện", từ dòng `<div className="field" style={{ margin: "10px 0 2px" }}>` đến `</div>` đóng ngay trước `<Toggle strong ...>`) bằng:

```tsx
              <div className="field" style={{ margin: "10px 0 2px" }}>
                <span className="lbl row" style={{ justifyContent: "space-between" }}>
                  <span id="story-len-label">Độ dài truyện</span>
                  <b className="th-val-pill">khoảng {shownLen.toLocaleString("vi-VN")} từ</b>
                </span>
                <div className="seg" role="group" aria-labelledby="story-len-label">
                  <button type="button" className={!lenCustom ? "on" : ""} aria-pressed={!lenCustom} disabled={!canEdit} onClick={() => setLenCustom(false)}>Theo mức đọc</button>
                  <button type="button" className={lenCustom ? "on" : ""} aria-pressed={lenCustom} disabled={!canEdit} onClick={() => { setLen(shownLen); setLenCustom(true); }}>Tự chỉnh</button>
                </div>
                <input
                  type="range"
                  className="range th-range"
                  disabled={!canEdit || !lenCustom}
                  min={MAX_STORY_LENGTH_RANGE.min}
                  max={MAX_STORY_LENGTH_RANGE.max}
                  step={MAX_STORY_LENGTH_RANGE.step}
                  value={shownLen}
                  style={{ "--pct": `${((shownLen - MAX_STORY_LENGTH_RANGE.min) / (MAX_STORY_LENGTH_RANGE.max - MAX_STORY_LENGTH_RANGE.min)) * 100}%` } as React.CSSProperties}
                  onChange={(e) => setLen(+e.target.value)}
                  aria-label="Độ dài truyện"
                />
                <span className="th-range-bounds" aria-hidden>
                  <small>{MAX_STORY_LENGTH_RANGE.min.toLocaleString("vi-VN")} từ</small>
                  <small>{MAX_STORY_LENGTH_RANGE.max.toLocaleString("vi-VN")} từ</small>
                </span>
                <small className="sub" aria-live="polite">
                  {lenCustom
                    ? `Bạn đang tự chỉnh: AI viết khoảng ${Math.ceil(len * 0.9).toLocaleString("vi-VN")}–${Math.floor(len * 1.1).toLocaleString("vi-VN")} từ.`
                    : `Theo mức đọc ${level}, tự đổi khi bạn đổi mức đọc ở tab Học tập. AI viết khoảng ${levelLen.min.toLocaleString("vi-VN")}–${levelLen.max.toLocaleString("vi-VN")} từ.`}
                </small>
                {lenCustom && lenAdvice && (
                  <p className="notice warn" data-inline-notice role="status">
                    <TriangleAlert className="ic" aria-hidden />
                    <span>{lenAdvice.text}</span>
                  </p>
                )}
              </div>
```

- [ ] **Step 7: Kiểm tra tĩnh**

Run (trong `E:\Capstone\AI_Storytelling_Frontend\src`): `npx tsc --noEmit -p .` và `npx eslint app/features/safety/SafetyWorkspace.tsx`
Expected: không lỗi.

---

### Task 5: Bước tạo hồ sơ bé và ghi chú ở tab Học tập

**Việc này làm gì (nói dễ hiểu):**
Làm cho màn tạo hồ sơ bé dùng cùng cách nói và cùng cách lưu với tab An toàn (bé mới mặc định "theo mức đọc", nếu phụ huynh kéo thanh thì lưu số riêng). Ở tab Học tập, thêm một dòng nhỏ báo rằng độ dài truyện ở khung mô tả mức đọc là mặc định và chỉnh lại được ở phần An toàn.

**Cần thêm / còn thiếu:**
- Đã có: bước 2 của `CreateChildWizard` đã gợi ý độ dài theo mức đọc (`suggestedLen`) và cho kéo (`customLen`); khung `LevelInfo` trong `LearningForm` đã hiện "Cả truyện khoảng N từ".
- Cần thêm: gửi `storyLengthTarget` khi tạo chính sách; đổi nhãn "Độ dài tối đa" thành "Độ dài truyện"; chỉnh câu cảnh báo khi thấp hơn gợi ý; thêm cảnh báo khi kéo dài hơn mức phù hợp với tuổi của bé (cùng `storyLengthAdvice` như tab An toàn); dòng ghi chú trong `LevelInfo`.
- Còn thiếu / ngoài phạm vi: không thêm test component (repo chưa có hạ tầng test cho wizard); kiểm tra bằng trình duyệt ở Task 6.

**Files:**
- Modify: `E:\Capstone\AI_Storytelling_Frontend\src\app\features\children\CreateChildWizard.tsx`
- Modify: `E:\Capstone\AI_Storytelling_Frontend\src\app\features\children\LearningForm.tsx`

**Interfaces:**
- Consumes: `SetSafetyPolicyRequest.storyLengthTarget` (Task 3).

- [ ] **Step 1: Gửi số tự chỉnh khi tạo chính sách**

Trong `CreateChildWizard.tsx`, trong lệnh `childProfileService.setSafetyPolicy(id, { ... })`, thay `maxStoryLength: maxLen,` bằng:

```tsx
          // Trần tự suy ra từ độ dài truyện (110%); backend cũng tự nâng nếu có số tự chỉnh.
          maxStoryLength: Math.ceil(maxLen * 1.1), storyLengthTarget: customLen ?? null,
```

(`customLen` là `number | undefined`; `?? null` đổi "chưa chỉnh" thành null = theo mức đọc.)

- [ ] **Step 2: Đổi nhãn và câu cảnh báo**

- `Độ dài tối đa của truyện <b style=...>` (dòng nhãn của thanh kéo) → `Độ dài truyện <b ...>` (giữ phần `<b>` hiện số từ).
- `aria-label="Độ dài tối đa của truyện"` → `aria-label="Độ dài truyện"`.
- Câu cảnh báo `Thấp hơn gợi ý: truyện AI tạo cho bé sẽ ngắn hơn, không quá {fmt(maxLen)} từ.` → `Thấp hơn gợi ý: truyện AI tạo cho bé sẽ ngắn hơn, khoảng {fmt(maxLen)} từ.`
- Ở bước tóm tắt: `<small>Độ dài tối đa</small>` → `<small>Độ dài truyện</small>`.

- [ ] **Step 2b: Cảnh báo khi kéo dài hơn mức phù hợp với tuổi**

Trong `CreateChildWizard.tsx`:
- Đổi dòng import guardian thành `import { AGE_BANDS, MAX_STORY_LENGTH_RANGE, defaultReadingLevel, defaultReadingMode, presetBlocked, storyLengthAdvice, storyLengthFor } from "@/app/lib/guardian";` và thêm `TriangleAlert` vào import `lucide-react` hiện có (file đã import `Check` từ `lucide-react`).
- Ngay sau dòng `const maxLen = customLen ?? suggestedLen;` thêm:

```tsx
  // Chỉ cảnh báo khi phụ huynh tự kéo dài hơn mức phù hợp với tuổi và mức đọc của bé.
  const lenAdvice = customLen !== undefined ? storyLengthAdvice(ageBand, learning.readingLevel, maxLen) : null;
```

- Ngay sau khối `{maxLen < suggestedLen && ( ... )}` (câu "Thấp hơn gợi ý...") thêm:

```tsx
              {lenAdvice && (
                <p className="notice warn" data-inline-notice role="status"><TriangleAlert className="ic" aria-hidden /><span>{lenAdvice.text}</span></p>
              )}
```

- [ ] **Step 3: Ghi chú trong `LevelInfo`**

Trong `LearningForm.tsx`, trong `LevelInfo`, ngay sau thẻ `</dl>` thêm:

```tsx
      <p className="sub">Đây là độ dài mặc định theo mức đọc. Bạn chỉnh lại được ở phần An toàn.</p>
```

- [ ] **Step 4: Kiểm tra tĩnh**

Run (trong `E:\Capstone\AI_Storytelling_Frontend\src`): `npx tsc --noEmit -p .` và `npx eslint app/features/children/CreateChildWizard.tsx app/features/children/LearningForm.tsx`
Expected: không lỗi.

---

### Task 6: Kiểm chứng toàn bộ và bàn giao

**Việc này làm gì (nói dễ hiểu):**
Chạy toàn bộ kiểm tra tự động và xem thật trên trình duyệt để chắc rằng đổi mức đọc thì độ dài ở tab An toàn đổi theo, tự chỉnh thì giữ nguyên, và AI nhận đúng số. Phần cập nhật cơ sở dữ liệu dev chỉ làm khi người dùng đồng ý.

**Cần thêm / còn thiếu:**
- Đã có: dev server frontend `http://localhost:3000`; tài khoản seed `parent_demo1` / mật khẩu demo `Demo@123`; bé "Bé An" (6–8 tuổi, mức đọc 2).
- Cần thêm: không có code mới; chỉ chạy kiểm tra và ghi lại giá trị gốc để khôi phục.
- Còn thiếu / ngoài phạm vi: tài liệu hướng dẫn test API dạng HTML (`docs/api/`) không nằm trong yêu cầu; nếu cần sẽ làm riêng theo template của dự án.

**Files:**
- Không sửa file mới ở task này (chỉ sửa nhỏ giao diện nếu kiểm tra phát hiện lệch).

- [ ] **Step 1: Kiểm tra tĩnh và test đầy đủ**

Backend: `dotnet build` (nếu VS khóa DLL, dùng `-o` như Global Constraints) rồi `dotnet test tests/StoryPlatform.UnitTests -o C:\Users\Admin\AppData\Local\Temp\claude\8cbeb7c727623cd2\bae9f3b0-e2c6-4df6-b0c4-54d099255750\scratchpad\ut`.
Frontend (trong `E:\Capstone\AI_Storytelling_Frontend\src`): `npx tsc --noEmit -p .`, `npx eslint app/features/safety app/features/children app/lib/guardian.ts tests/unit/guardian`, `npx vitest run`.
Expected: không lỗi mới (backend chỉ còn nhóm test môi trường đã biết); vitest toàn bộ đạt.

- [ ] **Step 2: Xin phép áp migration lên DB dev**

**Dừng và hỏi người dùng**: "Có chạy `dotnet ef database update -p src/Core/StoryPlatform.Infrastructure -s src/Core/StoryPlatform.Api` lên DB dev để thử trên trình duyệt không?" Chỉ chạy khi người dùng đồng ý. Chưa áp migration thì API dev sẽ lỗi khi đọc chính sách an toàn (thiếu cột), nên không thể thử trên trình duyệt; khi đó bỏ Step 3–4 và báo rõ là chưa kiểm chứng giao diện.

- [ ] **Step 3: Ghi giá trị gốc rồi thử trên trình duyệt (chỉ sau khi migration đã áp)**

Trước khi thử, ghi lại: mức đọc hiện tại của Bé An (tab Học tập), chế độ và số ở mục "Độ dài truyện" (tab An toàn). Tab An toàn **tự lưu**, nên mọi thao tác thử đều ghi vào DB dev và phải được khôi phục ở Step 4.
Đăng nhập `http://localhost:3000` bằng `parent_demo1`, mở `/app/children`, chọn "Bé An":
1. Tab An toàn → "Độ dài truyện": đang "Theo mức đọc", hiện khoảng 900 từ, thanh trượt bị khóa, dòng giải thích hiện 810–990.
2. Tab Học tập → chọn mức đọc 4 và bấm Lưu; quay lại tab An toàn: số đổi thành khoảng 1500 từ.
3. Đưa mức đọc về 2 (bé 6–8 tuổi, mặc định 900). Bấm "Tự chỉnh": ở 900 **không** có cảnh báo (thanh trượt nhảy theo bước 100 nên từ 1000 trở lên đã vượt 110% mặc định 990); kéo tới 1000 hoặc 1200: hiện cảnh báo vàng "Dài hơn mức phù hợp với bé 6–8 tuổi…" có nhắc AI cần nhiều thời gian hơn; kéo tới 1400 trở lên: cảnh báo đổi thành "Rất dài…" có nhắc thời gian duyệt. Dòng giải thích ở 1200 là 1080–1320. Chờ tự lưu; tải lại trang: vẫn "Tự chỉnh 1200" và cảnh báo vẫn hiện.
4. Đổi mức đọc ở tab Học tập sang 4 (mặc định 1500) rồi quay lại: số tự chỉnh vẫn 1200 (đã tự chỉnh) và cảnh báo biến mất vì 1200 nay thấp hơn mặc định mới; đổi lại mức 2 thì cảnh báo trở lại.
5. Bấm "Theo mức đọc": số trở về theo mức đọc và không còn cảnh báo.
6. Xem ở độ rộng mobile (375) và desktop; kiểm tra không lỗi console, bàn phím Tab/Enter dùng được hai nút, cảnh báo không làm thanh trượt nhảy vị trí khó chịu.
7. Mở form tạo truyện của bé: mức "Vừa" bằng độ dài đang chọn (nếu ở chế độ "Tự chỉnh 1200" thì "Vừa" = 1200).
8. Wizard tạo hồ sơ (nút "Thêm bé", bước 2 "Chủ đề"): kéo thanh "Độ dài truyện" quá 110% gợi ý thì hiện cùng cảnh báo; không bấm hoàn tất để khỏi tạo hồ sơ mới trong DB dev.

- [ ] **Step 4: Khôi phục dữ liệu dev**

Đưa mức đọc của Bé An về giá trị gốc đã ghi, và mục "Độ dài truyện" về "Theo mức đọc" (hoặc giá trị gốc nếu khác). Tải lại trang xác nhận.

- [ ] **Step 5: Kiểm tra git và báo cáo**

Run: `git status` và `git diff --stat` ở cả hai repo.
Expected: chỉ có các file ghi trong plan (cộng 2 file migration và snapshot); không đụng các file ngoài phạm vi trong Global Constraints. Báo cáo cuối: Created / Modified / Deleted / Tests / Notes, nêu rõ thay đổi API công khai (thêm trường nullable `storyLengthTarget`), migration mới, và hành vi mới: giới hạn cũ thấp hơn mặc định nay sinh truyện đúng bằng giới hạn thay vì báo lỗi 400.
