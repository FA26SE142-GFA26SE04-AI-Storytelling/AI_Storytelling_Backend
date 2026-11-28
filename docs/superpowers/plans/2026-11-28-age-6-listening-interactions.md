# Tương tác lúc nghe cho bé 6 tuổi — Implementation Plan

## Trạng thái thực thi — 28/11/2026

- [x] Triển khai mã và test cho Tasks 1–11: bộ riêng tuổi 6, AI, media, API trẻ, duyệt và phản hồi phụ huynh.
- [x] Tạo migration `20261128032700_AddAge6ListeningInteractions` sau khi người dùng duyệt riêng. Chưa áp migration vào database.
- [ ] Task 12: chưa tạo trang HTML theo lựa chọn của người dùng (chỉ duyệt tạo migration).
- [x] `dotnet build StoryPlatform.sln --no-restore -m:1 -nr:false`: thành công, 0 lỗi.
- [x] Toàn bộ `StoryPlatform.UnitTests`: 5.027/5.027 PASS (gồm kiểm tra migration/snapshot).
- [x] Toàn bộ `StoryPlatform.AI.UnitTests`: 149/149 PASS.
- [x] `StoryPlatform.AI.IntegrationTests --filter FullyQualifiedName~Listening`: 27/27 PASS.
- [x] `git diff --check`: không có lỗi khoảng trắng.
- Chưa chạy integration test cần database/seed/migration; chưa áp migration vào database.

Điều chỉnh khi đối chiếu mã thực tế:

- Index điểm dừng phải có `TargetAge`; index job phải có `OperationKey` chứa tuổi để các bộ không xung đột.
- Đoán tranh tuổi 6 không yêu cầu ranh giới scene cho reveal; CharacterLetter trả thêm `CharacterImageUrl` được ký.
- Sau hai lần chọn chưa khớp, WhoSaidIt/CharacterLetter phát audio tên/âm của đáp án đúng đã duyệt.
- Backend giới hạn RolePlay/CharacterLetter tuổi 6 ở ba lượt nói (lượt đầu và hai lần đọc lại), dựa trên telemetry của phiên; không thêm cột.
- Phiên bản đã xác nhận gói nội dung vẫn bất biến theo cơ chế hiện có; cần phiên bản mới để bổ sung tương tác cho các truyện đó.

---

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bé 6 tuổi nghe truyện sẽ gặp đúng 5 tương tác riêng của tuổi 6 (Điểm dừng nghĩa đen, Nói theo nhân vật có chấm mềm, Tên nhân vật và chữ cái, Đoán tiếp bằng tranh, Chạm vào nhân vật nói câu đó). Các tuổi khác không dùng chung bộ này.

**Architecture:** Mỗi `ListeningInteraction` có thêm `TargetAge` (6–10). Truyện vẫn sinh theo nhóm tuổi (`6-8`, `9-10`), nhưng Phase 4 sinh **một bộ riêng cho từng tuổi** trong nhóm (6-8 → bộ 6, 7, 8; 9-10 → bộ 9, 10). **Không còn bộ cũ**: planner theo nhóm tuổi bị bỏ, dòng cũ (`TargetAge = null`) không bao giờ tới trẻ. Plan này xây bộ tuổi 6; bộ 7, 8, 9, 10 theo tài liệu sẽ làm trong các plan kế tiếp, dùng cùng khung `TargetAge` này. Bé chỉ thấy dòng có `TargetAge` đúng tuổi mình. Planner, rules, AI contract, media, API trẻ, duyệt và báo cáo phụ huynh đều đọc `TargetAge`.

**Tech Stack:** .NET 8, EF Core (PostgreSQL), xUnit + Moq, service AI riêng ở `src/AI`, TTS/ASR có sẵn (`MediaGenerationService.ListeningMedia`, `ISpeechRecognizer`, `ReadAloudMatcher`).

**Spec:** `C:\Users\Admin\Downloads\Tương tác truyện cho trẻ 6–10 tuổi – Tớ Kể Chuyện.docx` (27/11/2026), mục "6 tuổi" và "Nguyên tắc chung".

## Global Constraints

- Mỗi tuổi có 5 tương tác, **không trùng nhau giữa các tuổi**; bé chỉ thấy bộ đúng tuổi của mình.
- Mỗi tương tác gắn với nội dung truyện, không thêm trò chơi phụ.
- Mọi tương tác dùng mic hoặc vẽ đều có nút "Bỏ qua" (bỏ qua = không có dòng attempt / bước ghi `skipped`).
- Không hiện điểm hay chữ "sai" cho bé; kết quả lưu cho phụ huynh xem. Luật `GRADING_WORD` giữ nguyên.
- Chạy trên web, thao tác bằng ngón tay hoặc chuột.
- Mọi nội dung cho trẻ do AI soạn nháp và **phải qua người duyệt** trước khi tới trẻ (ràng buộc cứng của dự án).
- Câu hệ thống cố định (không do AI sinh): "nhìn lại tranh nhé", "mình nghe chưa rõ, mình đọc lại nhé", "đi theo mũi tên nhé".
- Chọn lại: chỉ cho chọn lại **1 lần**; sai lần 2 thì hệ thống nói đáp án rồi chạy tiếp.
- Đọc lại từ vàng: tối đa **2 lần**.
- Chữ cái: chữ ghép (`ngh`, `ng`, `nh`, `ch`, `gh`, `gi`, `kh`, `ph`, `qu`, `th`, `tr`) tính một ô; chỉ khuyết chữ cái gốc, không dấu thanh/dấu phụ; mỗi lần khuyết 1 ô.
- Không còn bộ cũ theo nhóm tuổi: không sinh, không hiện cho trẻ, không tính vào readiness.

## Quyết định thiết kế đã chốt

| Câu hỏi | Chốt |
|---|---|
| Bộ tuổi 6 áp cho ai | Chỉ bé có tuổi 6. Hồ sơ cũ `Age == null` lấy tuổi nhỏ nhất của nhóm (6-8 → 6, 9-10 → 9), tức bộ dễ nhất của nhóm. |
| Bộ cũ | Bỏ (yêu cầu 28/11/2026). Planner theo nhóm tuổi bị xóa; dòng `TargetAge = null` đã có vẫn nằm trong DB nhưng bị lọc bỏ ở mọi chỗ trẻ đọc và ở readiness. Không xóa dữ liệu. |
| Tuổi chưa xây bộ (7–10, tới khi có plan riêng) | Phase 4 chỉ sinh bộ của các tuổi đã xây (`ListeningAgeSets.Built`). Bé 7–10 tuổi tạm **không có tương tác lúc nghe**; truyện vẫn xuất bản được. Mỗi plan tuổi sau chỉ cần thêm tuổi đó vào `Built` + nhánh planner/luật/prompt riêng. |
| Sinh | Truyện nhóm `6-8` sinh một bộ cho mỗi tuổi đã xây trong nhóm (hiện chỉ 6). Neo của các bộ độc lập (cùng một đoạn có thể có tương tác ở bộ 6 và bộ 7). |
| Truyện đã xuất bản | Mất tương tác cũ với trẻ (dòng `null` bị lọc). Muốn có bộ tuổi 6 thì người duyệt bấm sinh lại tương tác (luồng artifacts/generate có sẵn). |
| Code của loại cũ | Giữ enum và code theo loại (`EventOrdering`, `FillBlank`, `ReadAloud`, Checkpoint Prediction/Emotion/Opinion) vì bộ 7–10 có thể dùng lại (vd Sắp xếp 3 thẻ tranh của tuổi 7). Loại nào sau khi xong plan 7–10 không tuổi nào dùng thì dọn trong một plan riêng. |
| Map 5 tương tác vào type | Điểm dừng nghĩa đen = `Checkpoint` + `CheckpointKind.Literal` (mới). Nói theo nhân vật = `RolePlay` (thêm chấm mềm). Tên nhân vật và chữ cái = `CharacterLetter` (mới). Đoán tiếp bằng tranh = `PicturePrediction` biến thể tuổi 6 (2 tranh, không chấm, không reveal). Chạm vào nhân vật = `WhoSaidIt` (mới). |
| Tuổi 6 không có | `EventOrdering`, `ReadAloud`, `FillBlank`, Checkpoint Prediction/Emotion/Opinion. |
| Âm của chữ cái | Bảng âm cố định trong code (`b` → "bờ", `th` → "thờ"…), TTS đọc chữ trong bảng; không do AI sinh. |

## File Structure

- `src/Core/StoryPlatform.Domain/Enums/ListeningInteractionType.cs` — thêm `CharacterLetter = 7`, `WhoSaidIt = 8`.
- `src/Core/StoryPlatform.Domain/Enums/CheckpointKind.cs` — thêm `Literal = 4`.
- `src/Core/StoryPlatform.Domain/Entities/ListeningInteraction.cs` — thêm `TargetAge`, `BlankIndex`.
- `src/Core/StoryPlatform.Domain/Entities/ListeningInteractionAttempt.cs` — thêm `StepResultsJson`.
- `src/Core/StoryPlatform.Domain/Entities/ReadAloudRecording.cs` — thêm `WordStatusesJson`.
- `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddAge6ListeningInteractions.cs` — migration (EF sinh).
- `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningAgeSets.cs` — **mới**: tuổi nào có bộ riêng, bé tuổi X thấy bộ nào.
- `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/VietnameseLetterUnits.cs` — **mới**: tách tên thành ô chữ, bảng âm đọc.
- `.../ListeningInteractions/Services/ListeningInteractionPlanner.cs` — thêm nhánh tuổi 6.
- `.../ListeningInteractions/Services/ListeningInteractionRules.cs` (+ file `partial` mới `ListeningInteractionRules.Age6.cs`) — luật tuổi 6.
- `.../ListeningInteractions/Models/ListeningInteractionModels.cs` — `TargetAge`, `BlankIndex` trong draft/context/plan.
- `.../ListeningInteractions/Services/ListeningInteractionAiMapper.cs`, `ListeningInteractionMapper.cs`, `ListeningInteractionGenerator.cs` — đi theo tuổi đích.
- `src/Shared/StoryPlatform.Contracts/AI/Requests/GenerationRequests.cs` (+ response tương ứng) — `TargetAge`, trường mới.
- `src/AI/StoryPlatform.AI.Application/ContentGeneration/ListeningGenerationContract.cs`, `src/AI/StoryPlatform.AI.Infrastructure/PromptCatalog/PromptTemplateProvider.cs`, enum `PromptType` bên AI — prompt mới.
- `.../ContentGeneration/Services/ContentGenerationService.Listening.cs`, `.../ParentAuthoring/ParentStoryPackageProcessor.cs` — Phase 4 lặp theo bộ tuổi.
- `.../ListeningInteractions/Services/ListeningAudioParts.cs`, `ListeningMediaRequirements.cs`, `.../MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs` — audio/hình loại mới.
- `.../ReadingSessions/ChildListeningContent.cs`, `DTOs/ChildReadingDtos.cs`, `Services/ChildListeningService.cs`, `Services/ChildReadAloudService.cs` — API trẻ.
- `.../StoryReview/Services/StoryReviewService.Listening.cs`, `.../ReadingSessions/Services/ParentReadingFeedbackService.cs` — duyệt và phụ huynh.
- `docs/api/age-6-interactions-api-test-guide.html` — **mới** (Task 12, cần bạn duyệt trước khi tạo).

---

### Task 1: Mô hình dữ liệu — tuổi đích và cột mới

**Việc này làm gì (nói dễ hiểu):**
Mỗi tương tác được gắn "dành cho bé mấy tuổi". Nhờ vậy một truyện có thể chứa bộ riêng cho từng tuổi (6, 7, 8…), và bé chỉ thấy bộ của tuổi mình; tương tác cũ không gắn tuổi sẽ không còn tới trẻ. Task này cũng thêm chỗ lưu những thứ mới mà 5 tương tác tuổi 6 cần: vị trí chữ bị khuyết trong tên nhân vật, kết quả "đã thử / bỏ qua" của bước đọc âm và vẽ chữ, và kết quả từng từ khi bé nói theo.

**Cần thêm / còn thiếu:**
- Đã có: bảng `listening_interactions`, `listening_interaction_options`, `listening_interaction_attempts`, `read_aloud_recordings`; `ChildProfile.Age` (6–10, nullable).
- Cần thêm: enum `CharacterLetter`, `WhoSaidIt`, `CheckpointKind.Literal`; cột `TargetAge`, `BlankIndex`, `StepResultsJson`, `WordStatusesJson`; class `ListeningAgeSets` quyết định bé thấy bộ nào.
- Còn thiếu / ngoài phạm vi: bộ riêng cho 7/8/9/10 (plan sau; tới lúc đó bé tuổi này chưa có tương tác lúc nghe). Hồ sơ chưa có `Age` lấy tuổi nhỏ nhất của nhóm — muốn bắt phụ huynh nhập tuổi là việc của Luồng 1.

**Files:**
- Modify: `src/Core/StoryPlatform.Domain/Enums/ListeningInteractionType.cs`
- Modify: `src/Core/StoryPlatform.Domain/Enums/CheckpointKind.cs`
- Modify: `src/Core/StoryPlatform.Domain/Entities/ListeningInteraction.cs`
- Modify: `src/Core/StoryPlatform.Domain/Entities/ListeningInteractionAttempt.cs`
- Modify: `src/Core/StoryPlatform.Domain/Entities/ReadAloudRecording.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/ListeningInteractionConfiguration.cs` (index mới)
- Create: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningAgeSets.cs`
- Create (EF sinh): `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddAge6ListeningInteractions.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningAgeSetsTests.cs`

**Interfaces:**
- Produces:
  - `ListeningInteractionType.CharacterLetter = 7`, `ListeningInteractionType.WhoSaidIt = 8`, `CheckpointKind.Literal = 4`
  - `ListeningInteraction.TargetAge : int?`, `ListeningInteraction.BlankIndex : int?`
  - `ListeningInteractionAttempt.StepResultsJson : string?` (JSON `{"sound":"tried|skipped","trace":"tried|skipped"}`)
  - `ReadAloudRecording.WordStatusesJson : string?` (JSON mảng `["Good","Retry",...]`, không chứa lời bé nói)
  - `static class ListeningAgeSets { IReadOnlyList<int> Built; IReadOnlyList<int> TargetAgesFor(AgeBand band); int AgeFor(int? childAge, AgeBand band); }`

- [ ] **Step 1: Viết test hỏng cho `ListeningAgeSets`**

```csharp
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningAgeSetsTests
{
    [Fact]
    public void Band6To8_GeneratesOnlyBuiltAges() =>
        Assert.Equal([6], ListeningAgeSets.TargetAgesFor(AgeBand.Age_6_8));

    [Fact]
    public void Band9To10_GeneratesNothingUntilBuilt() =>
        Assert.Empty(ListeningAgeSets.TargetAgesFor(AgeBand.Age_9_10));

    [Theory]
    [InlineData(6, AgeBand.Age_6_8, 6)]
    [InlineData(7, AgeBand.Age_6_8, 7)]   // bộ riêng của 7 tuổi, không rơi về bộ khác
    [InlineData(10, AgeBand.Age_9_10, 10)]
    [InlineData(null, AgeBand.Age_6_8, 6)]
    [InlineData(null, AgeBand.Age_9_10, 9)]
    public void ChildAlwaysMapsToOwnAge(int? age, AgeBand band, int expected) =>
        Assert.Equal(expected, ListeningAgeSets.AgeFor(age, band));
}
```

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningAgeSetsTests`
Expected: FAIL — `ListeningAgeSets` chưa tồn tại.

- [ ] **Step 3: Thêm enum và cột**

`ListeningInteractionType.cs`:
```csharp
    ReadAloud = 6,
    /// <summary>6 tuổi: chọn chữ cái khuyết trong tên nhân vật, đọc âm, vẽ chữ.</summary>
    CharacterLetter = 7,
    /// <summary>6 tuổi: nghe câu thoại, chạm vào nhân vật đã nói.</summary>
    WhoSaidIt = 8
```

`CheckpointKind.cs`:
```csharp
    Opinion = 3,
    /// <summary>6 tuổi: câu hỏi nghĩa đen, đáp án có ngay trong tranh/đoạn vừa nghe.</summary>
    Literal = 4
```

`ListeningInteraction.cs` (sau `Type`):
```csharp
    /// <summary>Tuổi đích của bộ tương tác (6–10). Null chỉ có ở dòng tạo trước khi có bộ theo tuổi; dòng đó không tới trẻ.</summary>
    public int? TargetAge { get; set; }
    /// <summary>CharacterLetter: vị trí ô chữ bị khuyết trong CharacterName (tính theo VietnameseLetterUnits).</summary>
    public int? BlankIndex { get; set; }
```

`ListeningInteractionAttempt.cs`:
```csharp
    /// <summary>CharacterLetter: {"sound":"tried|skipped","trace":"tried|skipped"}. Không chấm, chỉ ghi đã thử hay bỏ qua.</summary>
    public string? StepResultsJson { get; set; }
```

`ReadAloudRecording.cs`:
```csharp
    /// <summary>Trạng thái từng từ theo thứ tự câu mẫu ("Good"/"Retry"/"Skipped"); cho phụ huynh xem. Không lưu lời bé nói.</summary>
    public string? WordStatusesJson { get; set; }
```

Trong `ListeningInteractionConfiguration.cs`, thêm index để API trẻ lọc nhanh theo bộ:
```csharp
builder.HasIndex(x => new { x.StoryVersionId, x.ParentStoryPackageId, x.TargetAge });
```

- [ ] **Step 4: Viết `ListeningAgeSets`**

```csharp
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>
/// Mỗi tuổi có bộ tương tác riêng, không dùng chung; không có bộ theo nhóm tuổi.
/// Built = các tuổi đã có planner/luật/prompt; mỗi plan tuổi mới thêm tuổi đó vào đây.
/// </summary>
public static class ListeningAgeSets
{
    public static IReadOnlyList<int> Built { get; } = [6];

    /// <summary>Các bộ Phase 4 sinh cho truyện thuộc nhóm tuổi này: mọi tuổi đã xây trong nhóm.</summary>
    public static IReadOnlyList<int> TargetAgesFor(AgeBand band) =>
        AgesIn(band).Where(Built.Contains).ToArray();

    /// <summary>Tuổi của bộ bé thấy: luôn đúng tuổi bé. Hồ sơ cũ chưa có tuổi lấy tuổi nhỏ nhất của nhóm.</summary>
    public static int AgeFor(int? childAge, AgeBand band) => childAge ?? AgesIn(band)[0];

    private static int[] AgesIn(AgeBand band) => band == AgeBand.Age_6_8 ? [6, 7, 8] : [9, 10];
}
```

- [ ] **Step 5: Chạy test, phải qua**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningAgeSetsTests`
Expected: PASS

- [ ] **Step 6: Tạo migration**

Run: `dotnet ef migrations add AddAge6ListeningInteractions --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api`
Expected: migration chỉ có `AddColumn` cho 4 cột nullable và `CreateIndex`; **không** có `DropColumn`/`DropTable`. Dòng cũ giữ `TargetAge = null`.

- [ ] **Step 7: Build**

Run: `dotnet build`
Expected: build thành công (switch trên enum chưa xử lý loại mới vẫn biên dịch được; các task sau bổ sung).

---

### Task 2: Tách tên nhân vật thành ô chữ và bảng âm đọc

**Việc này làm gì (nói dễ hiểu):**
Để làm trò "Tên nhân vật và chữ cái", hệ thống phải biết tên "Nhím" gồm các ô "Nh – i – m" (chữ ghép là một ô), ô nào được phép khuyết (chỉ chữ cái gốc, không có dấu), và mỗi chữ đọc là âm gì ("nhờ", "i", "mờ") để giọng mẫu đọc cho bé nghe.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionRules.CountWords`, chưa có gì về chữ cái.
- Cần thêm: `VietnameseLetterUnits.Split`, `IsBlankable`, `SoundOf`, danh sách chữ cái gốc dùng làm thẻ chọn.
- Còn thiếu / ngoài phạm vi: dấu thanh và chữ có dấu phụ (ă, â, ê, ô, ơ, ư, đ) — để giai đoạn sau theo spec. Thứ tự nét vẽ của chữ do frontend giữ (glyph cố định), backend không lưu.

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/VietnameseLetterUnits.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/VietnameseLetterUnitsTests.cs`

**Interfaces:**
- Produces: `static class VietnameseLetterUnits { IReadOnlyList<string> Split(string word); bool IsBlankable(string unit); string? SoundOf(string unit); IReadOnlyList<string> BaseLetters; }`

- [ ] **Step 1: Viết test hỏng**

```csharp
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class VietnameseLetterUnitsTests
{
    [Theory]
    [InlineData("Nhím", "Nh|í|m")]
    [InlineData("Thỏ", "Th|ỏ")]
    [InlineData("Nghé", "Ngh|é")]
    [InlineData("Gấu", "G|ấ|u")]
    [InlineData("Quạ", "Qu|ạ")]
    [InlineData("Gió", "Gi|ó")]
    [InlineData("Bống", "B|ố|ng")]
    public void Split_KeepsDigraphsAsOneUnit(string word, string expected) =>
        Assert.Equal(expected, string.Join("|", VietnameseLetterUnits.Split(word)));

    [Theory]
    [InlineData("Nh", true)]
    [InlineData("m", true)]
    [InlineData("a", true)]
    [InlineData("í", false)]   // có dấu thanh
    [InlineData("ấ", false)]   // dấu phụ + dấu thanh
    [InlineData("đ", false)]   // chữ có dấu phụ, để giai đoạn sau
    public void IsBlankable_OnlyBaseLetters(string unit, bool expected) =>
        Assert.Equal(expected, VietnameseLetterUnits.IsBlankable(unit));

    [Theory]
    [InlineData("b", "bờ")]
    [InlineData("Th", "thờ")]
    [InlineData("ngh", "ngờ")]
    [InlineData("a", "a")]
    public void SoundOf_ReturnsFixedSound(string unit, string expected) =>
        Assert.Equal(expected, VietnameseLetterUnits.SoundOf(unit));
}
```

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~VietnameseLetterUnitsTests`
Expected: FAIL — class chưa tồn tại.

- [ ] **Step 3: Viết `VietnameseLetterUnits`**

```csharp
using System.Globalization;
using System.Text;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>
/// Ô chữ của một từ tiếng Việt cho tương tác CharacterLetter: chữ ghép là một ô, chỉ khuyết chữ cái gốc không dấu.
/// Bảng âm cố định do hệ thống soạn (không do AI sinh) để TTS đọc âm của chữ.
/// </summary>
public static class VietnameseLetterUnits
{
    // Dài trước ngắn để "ngh" thắng "ng".
    private static readonly string[] Digraphs = ["ngh", "ng", "nh", "ch", "gh", "gi", "kh", "ph", "qu", "th", "tr"];

    private static readonly Dictionary<string, string> Sounds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "bờ", ["c"] = "cờ", ["d"] = "dờ", ["g"] = "gờ", ["h"] = "hờ", ["k"] = "ca", ["l"] = "lờ",
        ["m"] = "mờ", ["n"] = "nờ", ["p"] = "pờ", ["r"] = "rờ", ["s"] = "sờ", ["t"] = "tờ", ["v"] = "vờ", ["x"] = "xờ",
        ["a"] = "a", ["e"] = "e", ["i"] = "i", ["o"] = "o", ["u"] = "u", ["y"] = "i",
        ["ngh"] = "ngờ", ["ng"] = "ngờ", ["nh"] = "nhờ", ["ch"] = "chờ", ["gh"] = "gờ", ["gi"] = "giờ",
        ["kh"] = "khờ", ["ph"] = "phờ", ["qu"] = "quờ", ["th"] = "thờ", ["tr"] = "trờ"
    };

    /// <summary>Các ô được phép làm đáp án/thẻ nhiễu (chữ thường).</summary>
    public static IReadOnlyList<string> BaseLetters { get; } = Sounds.Keys.Select(k => k.ToLowerInvariant()).ToArray();

    public static IReadOnlyList<string> Split(string word)
    {
        var units = new List<string>();
        var i = 0;
        while (i < word.Length)
        {
            var digraph = Digraphs.FirstOrDefault(d => i + d.Length <= word.Length &&
                string.Equals(word.Substring(i, d.Length), d, StringComparison.OrdinalIgnoreCase) &&
                // "gi" chỉ là chữ ghép khi đứng trước nguyên âm khác ("Gió"), không phải "Gì".
                (d != "gi" || (i + 2 < word.Length && IsVowel(word[i + 2]))));
            var length = digraph?.Length ?? 1;
            units.Add(word.Substring(i, length));
            i += length;
        }
        return units;
    }

    public static bool IsBlankable(string unit) => Sounds.ContainsKey(unit) && unit.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');

    public static string? SoundOf(string unit) => Sounds.TryGetValue(unit, out var sound) ? sound : null;

    private static bool IsVowel(char c) =>
        "aeiouy".Contains(char.ToLowerInvariant(RemoveMarks(c)));

    private static char RemoveMarks(char c)
    {
        var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        return decomposed.First(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark);
    }
}
```

- [ ] **Step 4: Chạy test, phải qua**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~VietnameseLetterUnitsTests`
Expected: PASS

---

### Task 3: Planner bộ tuổi 6

**Việc này làm gì (nói dễ hiểu):**
Khi truyện có đủ chỗ dừng, bé 6 tuổi gặp mỗi tương tác trong 5 loại đúng một lần, rải đều trong truyện. Truyện ngắn thì ưu tiên loại quan trọng trước. Truyện không có câu thoại thì bỏ các loại cần thoại và thay bằng thêm một câu hỏi nghĩa đen. Bộ tuổi 6 không có tương tác sau đoạn cuối (không Sắp xếp, không Luyện đọc to).

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionPlanner.Create(AgeBand, blocks)` (bảng theo nhóm tuổi — sẽ **xóa**), `AssignZones`, `FindDialogueBlockIds`.
- Cần thêm: thay bằng `Create(int targetAge, blocks)`; `targetAge == 6` dùng `CreateAge6`, tuổi chưa xây trả `Empty`. Xóa nhánh theo nhóm tuổi và các test bảng 6-8 / 9-10 của nó. Thứ tự ưu tiên tuổi 6: Literal → RolePlay → CharacterLetter → PicturePrediction → WhoSaidIt. `ListeningPlanItem` thêm `Kind` để Checkpoint biết là Literal.
- Còn thiếu / ngoài phạm vi: CharacterLetter cần tên nhân vật có ô khuyết được — planner không biết tên, rules + AI lo (Task 4/5). Nếu AI không tìm được tên hợp lệ sau số lần thử, job báo `CHARACTERLETTER_VALIDATION_FAILED` như các loại khác.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Models/ListeningInteractionModels.cs` (`ListeningPlanItem`, `ListeningInteractionPlan`)
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionPlanner.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionPlannerTests.cs`

**Interfaces:**
- Consumes: `ListeningAgeSets` (Task 1).
- Produces:
  - `ListeningPlanItem(ListeningInteractionType Type, int Count) { CheckpointKind? Kind { get; init; } }`
  - `ListeningInteractionPlan(...) { int TargetAge { get; init; } }`
  - `ListeningInteractionPlanner.Create(int targetAge, IReadOnlyList<StoryTextBlock> blocks)` — overload `Create(AgeBand, blocks)` bị xóa.

- [ ] **Step 1: Thay test planner**

Xóa các test của bảng theo nhóm tuổi (`Age6To8_FollowsSpecTable`, `Age9To10_FollowsSpecTable`, `Age6To8_WithoutDialogue_GivesRolePlaySlotToPicture` và mọi test khác gọi `Create(AgeBand.…)`), giữ test của `AssignZones`/`FindDialogueBlockIds` nhưng đổi lời gọi sang `Create(6, …)` và sửa kỳ vọng theo bảng tuổi 6. Thêm:

```csharp
    private static string DescribeAge6(int blocks, bool dialogue = true) =>
        string.Join(",", ListeningInteractionPlanner.Create(6, Story(blocks, dialogue)).Items
            .Select(x => $"{x.Type}{(x.Kind is { } k ? ":" + k : "")}x{x.Count}"));

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "Checkpoint:Literalx1")]
    [InlineData(3, "Checkpoint:Literalx1,RolePlayx1")]
    [InlineData(4, "Checkpoint:Literalx1,RolePlayx1,CharacterLetterx1")]
    [InlineData(5, "Checkpoint:Literalx1,RolePlayx1,CharacterLetterx1,PicturePredictionx1")]
    [InlineData(6, "Checkpoint:Literalx1,RolePlayx1,CharacterLetterx1,PicturePredictionx1,WhoSaidItx1")]
    [InlineData(12, "Checkpoint:Literalx1,RolePlayx1,CharacterLetterx1,PicturePredictionx1,WhoSaidItx1")]
    public void Age6_FiveDistinctTypes(int blocks, string expected) =>
        Assert.Equal(expected, DescribeAge6(blocks));

    [Fact]
    public void Age6_WithoutDialogue_ReplacesDialogueTypesWithLiteral() =>
        Assert.Equal("Checkpoint:Literalx2,CharacterLetterx1,PicturePredictionx1", DescribeAge6(6, dialogue: false));

    [Fact]
    public void Age6_HasNoEndOfStoryItems() =>
        Assert.DoesNotContain(ListeningInteractionPlanner.Create(6, Story(9)).Items,
            x => ListeningInteractionRules.IsEndOfStory(x.Type));

    [Fact]
    public void Age6_PlanCarriesTargetAge() =>
        Assert.Equal(6, ListeningInteractionPlanner.Create(6, Story(6)).TargetAge);

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    public void UnbuiltAge_HasEmptyPlan(int age) =>
        Assert.Empty(ListeningInteractionPlanner.Create(age, Story(6)).Items);
```

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionPlannerTests`
Expected: FAIL — overload `Create(int, ...)` chưa có.

- [ ] **Step 3: Sửa model**

```csharp
public sealed record ListeningPlanItem(ListeningInteractionType Type, int Count)
{
    public IReadOnlyList<IReadOnlyList<string>> Zones { get; init; } = [];
    /// <summary>Kind bắt buộc của Checkpoint trong bộ tuổi này (tuổi 6: Literal); null = loại không phải Checkpoint.</summary>
    public CheckpointKind? Kind { get; init; }
}

public sealed record ListeningInteractionPlan(int Slots, IReadOnlyList<ListeningPlanItem> Items, IReadOnlyList<string> DialogueBlockIds)
{
    public static readonly ListeningInteractionPlan Empty = new(0, [], []);
    /// <summary>Tuổi của bộ (6–10).</summary>
    public int TargetAge { get; init; }
}
```

- [ ] **Step 4: Thay planner theo nhóm tuổi bằng planner theo tuổi**

Xóa thân `Create(AgeBand, blocks)` cũ (bảng Checkpoint/RolePlay/FillBlank/EventOrdering/ReadAloud theo nhóm tuổi) và sửa summary đầu class thành "Kế hoạch tương tác lúc nghe theo tuổi; mỗi tuổi một bộ riêng". Thay bằng:

```csharp
    /// <summary>Mỗi tuổi một bộ riêng (spec 27/11/2026). Tuổi chưa xây (chưa có trong ListeningAgeSets.Built) trả Empty.</summary>
    public static ListeningInteractionPlan Create(int targetAge, IReadOnlyList<StoryTextBlock> blocks) => targetAge switch
    {
        6 => CreateAge6(blocks) with { TargetAge = 6 },
        _ => ListeningInteractionPlan.Empty with { TargetAge = targetAge }
    };

    /// <summary>
    /// Bộ tuổi 6 (spec 27/11/2026): mỗi loại tối đa 1 lần, lấp chỗ theo thứ tự ưu tiên, không có mục sau đoạn cuối.
    /// Loại cần thoại (RolePlay, WhoSaidIt) bị bỏ khi truyện không có thoại; chỗ trống đó dành cho Literal thứ hai.
    /// </summary>
    private static ListeningInteractionPlan CreateAge6(IReadOnlyList<StoryTextBlock> blocks)
    {
        var slots = Math.Max(0, blocks.Count - 1);
        if (slots == 0) return ListeningInteractionPlan.Empty;
        var dialogue = FindDialogueBlockIds(blocks);
        var hasDialogue = dialogue.Count > 0;
        var order = new List<ListeningInteractionType> { ListeningInteractionType.Checkpoint };
        if (hasDialogue) order.Add(ListeningInteractionType.RolePlay);
        order.Add(ListeningInteractionType.CharacterLetter);
        order.Add(ListeningInteractionType.PicturePrediction);
        if (hasDialogue) order.Add(ListeningInteractionType.WhoSaidIt);

        var chosen = order.Take(slots).ToList();
        var literals = 1 + (!hasDialogue && slots > chosen.Count ? 1 : 0);
        var items = chosen.Select(type => type == ListeningInteractionType.Checkpoint
            ? new ListeningPlanItem(type, literals) { Kind = CheckpointKind.Literal }
            : new ListeningPlanItem(type, 1)).ToArray();
        return new(slots, AssignZones(items, blocks, dialogue), dialogue);
    }
```

Trong `AssignZones`, thêm `WhoSaidIt` vào nhóm "kén chỗ" như RolePlay, và `CharacterLetter` lấy vùng sớm (tên nhân vật đã xuất hiện):
```csharp
        foreach (var item in items.Where(x => x.Type is ListeningInteractionType.RolePlay or ListeningInteractionType.WhoSaidIt))
            assigned[item.Type] = [TakeFitting(b => dialogue.Contains(b.BlockId))];
        // ... FillBlank như cũ ...
        foreach (var item in items.Where(x => x.Type is ListeningInteractionType.Checkpoint or ListeningInteractionType.CharacterLetter))
            assigned[item.Type] = Enumerable.Range(0, item.Count).Select(_ => TakeEarliest()).ToArray();
```

- [ ] **Step 5: Chạy test, phải qua (đã xóa test bảng theo nhóm tuổi)**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionPlannerTests`
Expected: PASS

---

### Task 4: Luật kiểm tra bộ tuổi 6

**Việc này làm gì (nói dễ hiểu):**
AI soạn nháp có thể sai (câu hỏi không có trong truyện, đáp án trùng, tên nhân vật không có chữ khuyết được…). Luật này chặn bản nháp sai trước khi tới người duyệt, và cũng chạy lại khi người duyệt sửa tay. Mỗi loại tuổi 6 có luật riêng theo đúng spec: số đáp án, đúng một đáp án đúng, câu thoại có trong truyện, chữ khuyết là chữ cái gốc…

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionRules.Validate`, `ValidateText`, `CheckStale`, mã lỗi gửi lại AI qua `previousErrors`.
- Cần thêm:
  - Checkpoint `Literal`: 2–3 đáp án, đúng 1 đáp án khớp, câu trích không sau neo, không áp `KIND_LIMIT` (bộ tuổi 6 không cần Emotion/Opinion).
  - `CharacterLetter`: `CharacterName` có trong truyện trước neo; `BlankIndex` trỏ vào ô `IsBlankable`; đúng 3 thẻ, mỗi thẻ là một `BaseLetters`, không trùng, đúng 1 thẻ khớp với ô khuyết; `MatchFeedback` bắt buộc.
  - `WhoSaidIt`: `SourceQuote` là câu thoại có trong một đoạn thoại trước neo, ≤ 12 từ; 2–3 thẻ nhân vật (tên có trong truyện), đúng 1 khớp, mỗi thẻ có `ImageDescription`; `MatchFeedback` + `NoMatchFeedback` bắt buộc.
  - `PicturePrediction` khi `TargetAge == 6`: đúng 2 tranh, **không** thẻ nào `IsMatch`, không `RevealOffset`.
  - Mã lỗi mới: `LETTER_BLANK_INVALID`, `SPEAKER_INVALID`.
- Còn thiếu / ngoài phạm vi: kiểm tra "đáp án có thật trong tranh" (Literal) — không tự động được; người duyệt xác nhận.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Models/ListeningInteractionModels.cs` (`ListeningInteractionDraft.BlankIndex`, `ListeningRuleContext.TargetAge`)
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionRules.cs`
- Create: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionRules.Age6.cs` (partial)
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionRulesAge6Tests.cs`

**Interfaces:**
- Consumes: `VietnameseLetterUnits` (Task 2), `CheckpointKind.Literal`, `ListeningInteractionType.CharacterLetter/WhoSaidIt` (Task 1).
- Produces: `ListeningInteractionDraft.BlankIndex : int?`, `ListeningRuleContext.TargetAge : int?`; mã lỗi `LETTER_BLANK_INVALID`, `SPEAKER_INVALID`.

- [ ] **Step 1: Viết test hỏng**

Dùng truyện chung `ListeningTestStory` (P2 có thoại: Thỏ hỏi "Có ai ở đó không?"; P3 có "bác Gấu").

```csharp
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Enums;
using Xunit;
using S = StoryPlatform.UnitTests.Features.ListeningInteractions.ListeningTestStory;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionRulesAge6Tests
{
    private static ListeningRuleContext Ctx(T type, int count = 1) => S.Context(type, count) with { TargetAge = 6 };

    private static string[] Codes(ListeningInteractionDraft item, ListeningRuleContext ctx) =>
        ListeningInteractionRules.Validate([S.Resolve(item)], ctx).Select(e => e.Code).Distinct().ToArray();

    private static ListeningInteractionDraft Letter(string name = "Thỏ", int blank = 0, params (string Text, bool Match)[] options) => new()
    {
        Type = T.CharacterLetter, AnchorBlockId = "P1", CharacterName = name, BlankIndex = blank,
        MatchFeedback = "Bé giỏi quá!",
        Options = (options.Length > 0 ? options : [("th", true), ("b", false), ("m", false)])
            .Select((o, i) => new ListeningOptionDraft { Text = o.Text, IsMatch = o.Match, SortOrder = i }).ToArray()
    };

    [Fact]
    public void CharacterLetter_Valid() => Assert.Empty(Codes(Letter(), Ctx(T.CharacterLetter)));

    [Fact]
    public void CharacterLetter_BlankOnToneMarkedVowel_Rejected() =>
        Assert.Contains("LETTER_BLANK_INVALID", Codes(Letter(blank: 1, options: [("o", true), ("a", false), ("e", false)]), Ctx(T.CharacterLetter)));

    [Fact]
    public void CharacterLetter_MatchNotTheBlankedUnit_Rejected() =>
        Assert.Contains("LETTER_BLANK_INVALID", Codes(Letter(options: [("th", false), ("b", true), ("m", false)]), Ctx(T.CharacterLetter)));

    [Fact]
    public void CharacterLetter_NeedsExactlyThreeOptions() =>
        Assert.Contains("OPTION_COUNT_INVALID", Codes(Letter(options: [("th", true), ("b", false)]), Ctx(T.CharacterLetter)));

    [Fact]
    public void CharacterLetter_NameNotInStory_Rejected() =>
        Assert.Contains("NOT_GROUNDED", Codes(Letter(name: "Mèo"), Ctx(T.CharacterLetter)));

    // Neo P3: câu thoại ở P2, "bác Gấu" xuất hiện ở P3 — cả hai đều trước neo.
    private static ListeningInteractionDraft WhoSaid(string line = "Có ai ở đó không?", string anchor = "P3") => new()
    {
        Type = T.WhoSaidIt, AnchorBlockId = anchor, SourceQuote = line,
        MatchFeedback = "Đúng là bạn Thỏ rồi!", NoMatchFeedback = "Mình nghe lại nhé.",
        Options =
        [
            new ListeningOptionDraft { Text = "Thỏ", IsMatch = true, ImageDescription = "Chú thỏ trắng nhỏ", SortOrder = 0 },
            new ListeningOptionDraft { Text = "bác Gấu", ImageDescription = "Bác gấu nâu to", SortOrder = 1 }
        ]
    };

    [Fact]
    public void WhoSaidIt_Valid() => Assert.Empty(Codes(WhoSaid(), Ctx(T.WhoSaidIt)));

    [Fact]
    public void WhoSaidIt_LineNotDialogue_Rejected() =>
        Assert.Contains("SPEAKER_INVALID", Codes(WhoSaid(line: "Trời bắt đầu tối."), Ctx(T.WhoSaidIt)));

    [Fact]
    public void WhoSaidIt_MissingImage_Rejected()
    {
        var item = WhoSaid() with { Options = [WhoSaid().Options[0] with { ImageDescription = null }, WhoSaid().Options[1]] };
        Assert.Contains("SCHEMA_INVALID", Codes(item, Ctx(T.WhoSaidIt)));
    }

    [Fact]
    public void Literal_TwoOptionsOneMatch_Valid()
    {
        var item = new ListeningInteractionDraft
        {
            Type = T.Checkpoint, Kind = CheckpointKind.Literal, AnchorBlockId = "P1", Prompt = "Ai đi lạc vào rừng?",
            NoMatchFeedback = "Bé nhìn lại tranh nhé.",
            Options =
            [
                new ListeningOptionDraft { Text = "Thỏ con", IsMatch = true, Feedback = "Đúng là Thỏ con!", SortOrder = 0 },
                new ListeningOptionDraft { Text = "bác Gấu", Feedback = "Mình xem lại nhé.", SortOrder = 1 }
            ]
        };
        Assert.Empty(Codes(item, Ctx(T.Checkpoint)));
    }

    [Fact]
    public void Picture_Age6_NoMatchAllowed()
    {
        var item = new ListeningInteractionDraft
        {
            // Câu hỏi phải có từ bám truyện ("Thỏ") để qua NOT_GROUNDED.
            Type = T.PicturePrediction, AnchorBlockId = "P1", Prompt = "Theo bé, Thỏ sẽ gặp chuyện gì tiếp?",
            WaitFeedback = "Mình cùng nghe tiếp nhé!",
            Options =
            [
                new ListeningOptionDraft { Text = "Thỏ gặp bạn", ImageDescription = "Thỏ gặp gấu", Feedback = "Hay quá!", SortOrder = 0 },
                new ListeningOptionDraft { Text = "Thỏ ngủ", ImageDescription = "Thỏ ngủ dưới gốc cây", Feedback = "Hay quá!", SortOrder = 1 }
            ]
        };
        Assert.Empty(Codes(item, Ctx(T.PicturePrediction)));
        Assert.Contains("MATCH_RULE_INVALID", Codes(item with { Options = [item.Options[0] with { IsMatch = true }, item.Options[1]] }, Ctx(T.PicturePrediction)));
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = [.. item.Options, item.Options[0] with { Text = "Thỏ chạy" }] }, Ctx(T.PicturePrediction)));
    }
}
```

Nếu `ListeningTestStory` chưa có `Resolve` / `Context` dạng public static, mở rộng file đó (hàm đang dùng trong `ListeningInteractionRulesTests`) thay vì viết lại.

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionRulesAge6Tests`
Expected: FAIL — `BlankIndex`, `TargetAge` chưa có.

- [ ] **Step 3: Thêm trường model**

```csharp
// ListeningInteractionDraft
    /// <summary>CharacterLetter: vị trí ô khuyết trong CharacterName.</summary>
    public int? BlankIndex { get; init; }

// ListeningRuleContext
    /// <summary>Tuổi của bộ đang kiểm (6–10); luật riêng từng tuổi đọc trường này.</summary>
    public int TargetAge { get; init; }
```

- [ ] **Step 4: Viết `ListeningInteractionRules.Age6.cs`**

```csharp
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public static partial class ListeningInteractionRules
{
    public const int WhoSaidItMaxWords = 12;

    private static void ValidateCharacterLetter(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(item.CharacterName)) { errors.Add(new("SCHEMA_INVALID", index, "characterName")); return; }
        if (!AppearsBefore(context.Content, item.CharacterName, item.AnchorOffset))
            errors.Add(new("NOT_GROUNDED", index, "characterName"));
        var units = VietnameseLetterUnits.Split(item.CharacterName.Trim());
        if (item.BlankIndex is not { } blank || blank < 0 || blank >= units.Count || !VietnameseLetterUnits.IsBlankable(units[blank]))
        {
            errors.Add(new("LETTER_BLANK_INVALID", index, "blankIndex"));
            return;
        }
        if (item.Options.Count != 3 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Any(o => !VietnameseLetterUnits.BaseLetters.Contains(o.Text.Trim().ToLowerInvariant())))
            errors.Add(new("LETTER_BLANK_INVALID", index, "options"));
        var matches = item.Options.Where(o => o.IsMatch).ToArray();
        if (matches.Length != 1 || !string.Equals(matches[0].Text.Trim(), units[blank], StringComparison.OrdinalIgnoreCase))
            errors.Add(new("LETTER_BLANK_INVALID", index, "options"));
    }

    private static void ValidateWhoSaidIt(ListeningInteractionDraft item, int index, ListeningRuleContext context,
        List<ListeningValidationError> errors)
    {
        var line = item.SourceQuote;
        var dialogueBlock = context.Blocks.FirstOrDefault(b => context.DialogueBlockIds.Contains(b.BlockId) &&
            line is not null && b.Text.Contains(line, StringComparison.Ordinal) && b.EndOffset <= item.AnchorOffset);
        if (string.IsNullOrWhiteSpace(line) || dialogueBlock is null || !IsInsideQuotes(dialogueBlock.Text, line))
            errors.Add(new("SPEAKER_INVALID", index, "lineQuote"));
        if (line is not null && CountWords(line) > WhoSaidItMaxWords) errors.Add(new("LENGTH_LIMIT", index, "lineQuote"));
        if (item.Options.Count is < 2 or > 3 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Count(o => o.IsMatch) != 1) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
        {
            if (string.IsNullOrWhiteSpace(item.Options[j].ImageDescription))
                errors.Add(new("SCHEMA_INVALID", index, $"options[{j}].imageDescription"));
            if (!AppearsBefore(context.Content, item.Options[j].Text, item.AnchorOffset))
                errors.Add(new("SPEAKER_INVALID", index, $"options[{j}].text"));
        }
    }

    private static void ValidatePictureAge6(ListeningInteractionDraft item, int index, List<ListeningValidationError> errors)
    {
        if (item.Options.Count != 2 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Any(o => o.IsMatch)) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        if (item.RevealOffset is not null) errors.Add(new("SCHEMA_INVALID", index, "revealBlockId"));
        for (var j = 0; j < item.Options.Count; j++)
            if (string.IsNullOrWhiteSpace(item.Options[j].ImageDescription))
                errors.Add(new("SCHEMA_INVALID", index, $"options[{j}].imageDescription"));
    }

    private static bool AppearsBefore(string content, string term, int? anchor) =>
        anchor is { } a && content.AsSpan(0, Math.Min(a, content.Length)).Contains(term.Trim().AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static bool IsInsideQuotes(string blockText, string line)
    {
        var start = blockText.IndexOf(line, StringComparison.Ordinal);
        var before = blockText[..start];
        var lastOpen = before.LastIndexOfAny(['"', '“']);
        var lineStart = before.LastIndexOf('\n') + 1;
        var dashLine = blockText[lineStart..].TrimStart() is { Length: > 0 } l && l[0] is '-' or '–' or '—';
        return dashLine || (lastOpen >= 0 && before.Count(c => c is '"' or '“' or '”') % 2 == 1);
    }
}
```

- [ ] **Step 5: Nối vào `Validate`, `ValidateFields`, `RequiredFeedback`, `TextFields`**

Trong `Validate`:
```csharp
                case ListeningInteractionType.Checkpoint: ValidateCheckpoint(item, i, errors); break;
                case ListeningInteractionType.PicturePrediction when context.TargetAge == 6: ValidatePictureAge6(item, i, errors); break;
                case ListeningInteractionType.PicturePrediction: ValidatePicture(item, i, errors); break;
                case ListeningInteractionType.CharacterLetter: ValidateCharacterLetter(item, i, context, errors); break;
                case ListeningInteractionType.WhoSaidIt: ValidateWhoSaidIt(item, i, context, errors); break;
```
Bộ tuổi 6 bắt mọi Checkpoint phải `Literal`; khối `KIND_LIMIT` theo nhóm tuổi (đòi có Emotion/Opinion) chỉ còn áp cho tuổi khác 6, để plan tuổi 7–10 tự quyết khi xây:
```csharp
        if (context.Type == ListeningInteractionType.Checkpoint && context.TargetAge == 6)
        {
            if (items.Any(x => x.Kind != CheckpointKind.Literal)) errors.Add(new("KIND_LIMIT"));
        }
        else if (context.Type == ListeningInteractionType.Checkpoint) { /* khối KIND_LIMIT hiện có, không đổi */ }
```

`ValidateCheckpoint`: `Literal` dùng luật như Prediction (2–3 đáp án, đúng 1 khớp) — nhánh `countOk` hiện tại đã đúng vì chỉ Emotion là 4 đáp án; không cần sửa.

`ValidateFields` — cho phép trường theo loại mới:
```csharp
        Forbid(item.Prompt is not null && t is not (ListeningInteractionType.Checkpoint or ListeningInteractionType.PicturePrediction), "question");
        Forbid(item.CharacterName is not null && t is not (ListeningInteractionType.RolePlay or ListeningInteractionType.CharacterLetter), "characterName");
        Forbid(item.BlankIndex is not null && t != ListeningInteractionType.CharacterLetter, "blankIndex");
        Forbid(item.SourceQuote is not null && t is ListeningInteractionType.EventOrdering or ListeningInteractionType.CharacterLetter, "sourceQuote");
        Forbid(item.NoMatchFeedback is not null && t is ListeningInteractionType.RolePlay or ListeningInteractionType.PicturePrediction
            or ListeningInteractionType.ReadAloud or ListeningInteractionType.CharacterLetter, "noMatchFeedback");
        // MatchFeedback: giữ Forbid cũ (Checkpoint, PicturePrediction); CharacterLetter/WhoSaidIt được phép.
        // Option: ImageDescription cho phép thêm WhoSaidIt; Feedback cấm với CharacterLetter, WhoSaidIt.
            Forbid(o.ImageDescription is not null && t is not (ListeningInteractionType.PicturePrediction or ListeningInteractionType.WhoSaidIt), OptionField(item, j, "imageDescription"));
            Forbid(o.Feedback is not null && t is ListeningInteractionType.FillBlank or ListeningInteractionType.EventOrdering
                or ListeningInteractionType.CharacterLetter or ListeningInteractionType.WhoSaidIt, OptionField(item, j, "feedback"));
            Forbid(o.IsMatch && t is ListeningInteractionType.FillBlank or ListeningInteractionType.EventOrdering, OptionField(item, j, "isMatch"));
```

`RequiredFeedback`:
```csharp
            case ListeningInteractionType.CharacterLetter:
                yield return ("praiseFeedback", item.MatchFeedback);
                break;
            case ListeningInteractionType.WhoSaidIt:
                yield return ("matchFeedback", item.MatchFeedback);
                yield return ("retryFeedback", item.NoMatchFeedback);
                break;
            case ListeningInteractionType.PicturePrediction:
                yield return ("waitFeedback", item.WaitFeedback);
                for (var j = 0; j < item.Options.Count; j++) yield return ($"options[{j}].feedback", item.Options[j].Feedback);
                break;
```

Thẻ chữ cái ("b", "m") và tên nhân vật không chứa từ chấm điểm, `ValidateText` giữ nguyên. Kiểm `NOT_GROUNDED` chỉ chạy trên `Prompt`, CharacterLetter/WhoSaidIt không có `Prompt` nên dùng `AppearsBefore` ở trên.

- [ ] **Step 6: Chạy test tuổi 6 và toàn bộ test luật cũ**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionRules`
Expected: PASS. Test luật hiện có dựng `ListeningRuleContext` không gán `TargetAge` (mặc định 0, khác 6) nên vẫn chạy nhánh luật hiện có và không đổi kết quả.

---

### Task 5: Hợp đồng AI và prompt cho bộ tuổi 6

**Việc này làm gì (nói dễ hiểu):**
Service AI cần biết đang soạn cho bé mấy tuổi và soạn loại nào, để viết câu hỏi nghĩa đen ngắn gọn, chọn tên nhân vật và chữ khuyết, chọn câu thoại và các nhân vật để bé chạm, hay soạn 2 tranh đoán tiếp. Core gửi thêm tuổi đích, AI trả thêm trường mới, Core chuyển về bản nháp để kiểm luật.

**Cần thêm / còn thiếu:**
- Đã có: `GenerateListeningInteractionRequest` (có `AgeBand`, `InteractionType`, `Count`, `AllowedBlockIds`), `ListeningInteractionAiMapper`, `ListeningGenerationContract.Template(type)` bên AI, `PromptTemplateProvider`, `GetListeningPromptSnapshotAsync(type, ageBand, language)`.
- Cần thêm:
  - Request: `TargetAge : int?`, `CheckpointKind : string?` (bộ tuổi 6 gửi `"Literal"`).
  - Response item: `blankIndex : int?`, `lineQuote` (dùng lại `sourceQuote` cho WhoSaidIt), `speakers[]` (map vào `Options` với `text`, `isMatch`, `imageDescription`).
  - Prompt mới: `ListeningCharacterLetter`, `ListeningWhoSaidIt`, `ListeningCheckpointLiteral`, `ListeningPicturePredictionAge6` (snapshot key = type + `Age6`).
  - Gợi ý cho AI: danh sách chữ cái khuyết được (`VietnameseLetterUnits.BaseLetters`) và luật ô chữ ghép.
- Còn thiếu / ngoài phạm vi: chỉnh câu chữ prompt cho hay — teammate phụ trách prompt AI (`src/AI`) duyệt lại; plan chỉ chốt schema JSON và luật.

**Files:**
- Modify: `src/Shared/StoryPlatform.Contracts/AI/Requests/GenerationRequests.cs`
- Modify: response DTO của listening trong `src/Shared/StoryPlatform.Contracts/AI/Responses/` (file chứa `GenerateListeningInteractionResponse`)
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionAiMapper.cs`
- Modify: `src/AI/StoryPlatform.AI.Application/ContentGeneration/ListeningGenerationContract.cs`
- Modify: `src/AI/StoryPlatform.AI.Application/ContentGeneration/GenerateListeningInteractionHandler.cs`
- Modify: `src/AI/StoryPlatform.AI.Infrastructure/PromptCatalog/PromptTemplateProvider.cs` và enum `PromptType` bên AI
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionAiMapperTests.cs`, `tests/StoryPlatform.AI.IntegrationTests/ListeningHttpContractTests.cs`

**Interfaces:**
- Consumes: `ListeningPlanItem.Kind`, `ListeningInteractionPlan.TargetAge` (Task 3), `ListeningInteractionDraft.BlankIndex` (Task 4).
- Produces:
  - `GenerateListeningInteractionRequest.TargetAge : int?`, `.CheckpointKind : string?`, `.AllowedLetters : IReadOnlyList<string>`
  - Response item `BlankIndex : int?`, `Speakers : IReadOnlyList<ListeningSpeakerDto>` với `ListeningSpeakerDto(string Name, bool IsMatch, string ImageDescription)`
  - `ListeningInteractionAiMapper.BuildRequest(..., int? targetAge = null, CheckpointKind? kind = null)` (tham số thêm cuối; Core luôn truyền tuổi, mặc định chỉ để test cũ biên dịch)

- [ ] **Step 1: Test hỏng cho mapper**

```csharp
    [Fact]
    public void BuildRequest_Age6Checkpoint_SendsTargetAgeAndLiteralKind()
    {
        var request = ListeningInteractionAiMapper.BuildRequest("req", ListeningInteractionType.Checkpoint, 1, AgeBand.Age_6_8, "vi",
            "Thỏ con", "", ListeningTestStory.Blocks, [], [], [["P1"]], [], [], targetAge: 6, kind: CheckpointKind.Literal);
        Assert.Equal(6, request.TargetAge);
        Assert.Equal("Literal", request.CheckpointKind);
    }

    [Fact]
    public void BuildRequest_CharacterLetter_SendsAllowedLetters()
    {
        var request = ListeningInteractionAiMapper.BuildRequest("req", ListeningInteractionType.CharacterLetter, 1, AgeBand.Age_6_8, "vi",
            "Thỏ con", "", ListeningTestStory.Blocks, [], [], [["P1"]], [], [], targetAge: 6);
        Assert.Contains("th", request.AllowedLetters);
        Assert.DoesNotContain("đ", request.AllowedLetters);
    }

    [Fact]
    public void ToDrafts_WhoSaidIt_MapsSpeakersToOptions()
    {
        // response item: type WhoSaidIt, afterBlockId P2, sourceQuote "Có ai ở đó không?",
        // speakers [{name:"Thỏ",isMatch:true,imageDescription:"Chú thỏ"}, {name:"bác Gấu",isMatch:false,imageDescription:"Bác gấu"}]
        var drafts = ListeningInteractionAiMapper.ToDrafts(/* dùng helper dựng response có sẵn trong file test */);
        Assert.Equal(["Thỏ", "bác Gấu"], drafts[0].Options.Select(o => o.Text));
        Assert.True(drafts[0].Options[0].IsMatch);
    }

    [Fact]
    public void ToDrafts_CharacterLetter_MapsBlankIndex() { /* response item blankIndex = 0 → draft.BlankIndex == 0 */ }
```

Viết hai test `ToDrafts` theo đúng helper dựng response đang dùng trong `ListeningInteractionAiMapperTests` (đọc file trước, dùng lại helper thay vì viết mới).

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionAiMapperTests`
Expected: FAIL — tham số `targetAge` chưa có.

- [ ] **Step 3: Sửa contract**

```csharp
// GenerateListeningInteractionRequest
    /// <summary>Tuổi của bộ (6–10). AI chọn template theo tuổi này.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? TargetAge { get; init; }
    /// <summary>Checkpoint: kind bắt buộc của bộ ("Literal" với tuổi 6).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CheckpointKind { get; init; }
    /// <summary>CharacterLetter: các chữ cái gốc được phép khuyết/làm thẻ.</summary>
    public IReadOnlyList<string> AllowedLetters { get; init; } = [];
```

Response item thêm `int? BlankIndex` và `IReadOnlyList<ListeningSpeakerDto> Speakers` (mặc định `[]`); `ListeningSpeakerDto` đặt trong cùng file response.

- [ ] **Step 4: Sửa mapper Core**

`BuildRequest` thêm `int? targetAge = null, CheckpointKind? kind = null`, gán `TargetAge = targetAge`, `CheckpointKind = kind?.ToString()`, `AllowedLetters = type == ListeningInteractionType.CharacterLetter ? VietnameseLetterUnits.BaseLetters : []`.
`ToDrafts`: `BlankIndex = item.BlankIndex`; với `WhoSaidIt` dựng `Options` từ `Speakers` (`Text = s.Name, IsMatch = s.IsMatch, ImageDescription = s.ImageDescription, SortOrder = i`); `CharacterLetter` dùng `characterName` + `options[].text/isMatch` như Checkpoint.

- [ ] **Step 5: Prompt bên AI**

Trong `ListeningGenerationContract`, thêm template cho `CharacterLetter`, `WhoSaidIt`, và biến thể `Checkpoint`/`PicturePrediction` khi `TargetAge == 6`. Mỗi template phải nêu đúng luật của Task 4 (ví dụ CharacterLetter: "chọn một tên nhân vật đã xuất hiện trước afterBlockId; chia tên theo ô, chữ ghép ngh/ng/nh/ch/gh/gi/kh/ph/qu/th/tr là một ô; blankIndex chỉ vào ô thuộc allowedLetters; đúng 3 options, 1 isMatch là ô bị khuyết, 2 ô nhiễu khác nhau trong allowedLetters; praiseFeedback ngắn, không dùng từ 'sai'"). Đăng ký `PromptType.ListeningCharacterLetter`, `ListeningWhoSaidIt`, `ListeningCheckpointLiteral`, `ListeningPicturePredictionAge6` trong `PromptTemplateProvider`; `GenerateListeningInteractionHandler` chọn template theo `(InteractionType, TargetAge)`. Nếu `Database/Seed` có seed prompt template listening (tìm: `grep -rn "ListeningCheckpoint" Database/Seed`), thêm 4 dòng tương ứng theo đúng định dạng seed đang có.

Core: `GetListeningPromptSnapshotAsync` gửi `type` dạng `"CharacterLetter"`, `"Checkpoint:Age6"`, `"PicturePrediction:Age6"` khi `TargetAge == 6` để snapshot đúng template.

- [ ] **Step 6: Chạy test mapper và contract**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionAiMapperTests`
Run: `dotnet test tests/StoryPlatform.AI.IntegrationTests --filter FullyQualifiedName~ListeningHttpContractTests`
Expected: PASS. Thêm vào `ListeningHttpContractTests` một case serialize/deserialize request có `targetAge: 6` và response có `speakers`, `blankIndex`.

---

### Task 6: Phase 4 sinh từng bộ theo tuổi đích

**Việc này làm gì (nói dễ hiểu):**
Khi truyện được duyệt nội dung, job sinh tương tác chạy một lượt cho mỗi tuổi đã xây trong nhóm (hiện tại: nhóm 6-8 chỉ chạy bộ tuổi 6; nhóm 9-10 chưa có tuổi nào nên không sinh). Không còn sinh bộ theo nhóm tuổi. Các bộ không giành chỗ dừng của nhau. Mọi bản nháp vẫn vào hàng chờ duyệt như hiện tại.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionGenerator.CreateScope/LoadScopeAsync/GenerateAsync`, `LoadSavedAsync`, job ở `ContentGenerationService.Listening.cs`, `ParentStoryPackageProcessor.cs`, `LearningArtifactService.cs` (artifacts/generate), `ListeningJobState`.
- Cần thêm: `CreateScope(version, context, targetAge)`; `LoadSavedAsync` lọc theo `TargetAge`; neo bị chiếm chỉ tính trong cùng bộ; lưu `TargetAge`, `BlankIndex` khi ghi; job lặp `ListeningAgeSets.TargetAgesFor(band)`; `ListeningJobState.Fingerprint` có tuổi đích để hai bộ không dùng nhầm request đã ghim.
- Còn thiếu / ngoài phạm vi: số lần gọi AI tăng (thêm tối đa 5 loại cho nhóm 6-8). Không đổi quota (sinh tương tác không trừ lượt). Truyện đã xuất bản trước plan này **không** tự có bộ tuổi 6 — muốn có thì người duyệt bấm sinh lại tương tác (luồng artifacts/generate có sẵn).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionGenerator.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Models/ListeningInteractionModels.cs` (`ListeningScope.TargetAge`, `ListeningSavedItem.TargetAge`)
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Models/ListeningJobState.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionMapper.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ContentGeneration/Services/ContentGenerationService.Listening.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ParentAuthoring/ParentStoryPackageProcessor.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/StoryReview/Services/LearningArtifactService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionGeneratorTests.cs`, `ListeningWorkflowPersistenceTests.cs`, `ListeningInteractionMapperTests.cs`

**Interfaces:**
- Consumes: `ListeningAgeSets.TargetAgesFor` (Task 1), `Planner.Create(int?, AgeBand, blocks)` (Task 3), mapper Task 5.
- Produces: `ListeningInteractionGenerator.CreateScope(StoryVersion, AIStoryInputContextSnapshot, int targetAge)`; `ListeningScope.TargetAge : int`; `LoadScopeAsync(story, version, int targetAge, CancellationToken ct)`; `HasPlan(content, AgeBand band)` = có ít nhất 2 đoạn **và** nhóm có tuổi đã xây.

- [ ] **Step 1: Test hỏng**

Trong `ListeningInteractionGeneratorTests`:
```csharp
    [Fact]
    public async Task Age6Set_DoesNotTreatOtherRowsAsOccupied()
    {
        // Seed một Checkpoint cũ (TargetAge = null) neo ở P1 — dòng trước khi có bộ theo tuổi.
        // Gọi GenerateAsync với scope tuổi 6, type Checkpoint; AI mock trả Literal neo P1.
        // Kỳ vọng: không có ANCHOR_INVALID; request gửi AI có OccupiedBlockIds rỗng và TargetAge = 6.
    }

    [Fact]
    public async Task SaveAsync_WritesTargetAgeAndBlankIndex()
    {
        // GenerateAsync + SaveAsync cho CharacterLetter tuổi 6 → dòng ListeningInteraction có TargetAge = 6, BlankIndex = 0.
    }

    [Fact]
    public async Task RegenerateAge6Set_SoftDeletesOnlyAge6Rows()
    {
        // Có dòng tuổi 6 và một dòng TargetAge = 7 (giả lập bộ tuổi sau); sinh lại bộ 6 → chỉ dòng tuổi 6 cũ bị xóa mềm.
    }
```
Dựng dữ liệu bằng các helper đang có trong `ListeningInteractionGeneratorTests` / `ListeningTestStory` (in-memory UoW + mock `IAIStoryGenerationClient`); viết đầy đủ arrange/act/assert theo mẫu test sinh Checkpoint hiện có trong file.

Trong `ListeningWorkflowPersistenceTests`: job Phase 4 cho truyện `6-8` chỉ gọi AI cho `TargetAge = 6` (đúng 5 loại, không còn Checkpoint Prediction/Emotion, FillBlank, EventOrdering, ReadAloud); truyện `9-10` không gọi AI listening và job vẫn hoàn tất (không kẹt chờ tương tác). Sửa các test workflow hiện có đang kỳ vọng bảng theo nhóm tuổi cho khớp.

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ListeningInteractionGeneratorTests|FullyQualifiedName~ListeningWorkflowPersistenceTests"`
Expected: FAIL.

- [ ] **Step 3: Generator theo bộ**

```csharp
    public static ListeningScope CreateScope(StoryVersion version, AIStoryInputContextSnapshot context, int targetAge)
    {
        var blocks = BlockParser.Parse(version.Content!);
        return new(version, context, blocks, ListeningInteractionPlanner.Create(targetAge, blocks)) { TargetAge = targetAge };
    }

    public static bool HasPlan(string? content, AgeBand band) =>
        !string.IsNullOrEmpty(content) && BlockParser.Parse(content).Count >= 2 && ListeningAgeSets.TargetAgesFor(band).Count > 0;
```
Mọi chỗ đang gọi `HasPlan(content)` (tìm `grep -rn "HasPlan(" src/Core`) truyền thêm nhóm tuổi của truyện, để truyện 9-10 không chờ Phase 4 listening.
```csharp
```
- `LoadSavedAsync(versionId, ct, packageId, targetAge)` thêm điều kiện `x.TargetAge == targetAge`.
- `GenerateAsync`: `occupied` chỉ lấy từ cùng bộ; `RuleContext(...)` gán `TargetAge = scope.TargetAge`; `BuildRequest(..., targetAge: scope.TargetAge, kind: item.Kind)`.
- Kiểm `LISTENING_PLAN_INFEASIBLE` áp cho cả `WhoSaidIt` như `RolePlay`.
- Khi thay/xóa mềm bản nháp cũ của một loại, chỉ đụng dòng cùng `TargetAge`.
- `ListeningInteractionMapper` ghi `TargetAge = scope.TargetAge`, `BlankIndex = draft.BlankIndex` (và chiều ngược lại khi đọc dòng về draft).
- `ListeningJobState.Fingerprint(scope)` thêm `scope.TargetAge` vào chuỗi băm.

- [ ] **Step 4: Job lặp theo bộ**

Trong `ContentGenerationService.Listening.cs` và `ParentStoryPackageProcessor.cs`, chỗ đang `foreach (var item in scope.Plan.Items)`:
```csharp
foreach (var targetAge in ListeningAgeSets.TargetAgesFor(ToAgeBand(context.AgeBand)))
{
    var scope = ListeningInteractionGenerator.CreateScope(version, context, targetAge) with { ParentStoryPackageId = packageId };
    foreach (var item in scope.Plan.Items)
        await _listening.GenerateAndSaveAsync(scope, item.Type, $"{requestIdPrefix}-a{targetAge}", ct /* tham số còn lại giữ như hiện có */);
}
```
Giữ nguyên cách lưu trạng thái job/checkpoint hiện có; khóa trạng thái bền (`ListeningJobState`) đặt theo `(type, targetAge)`. `LearningArtifactService` (người duyệt bấm sinh lại) nhận thêm `targetAge` từ request (Task 11 thêm vào DTO), mặc định sinh lại mọi bộ.

- [ ] **Step 5: Chạy test**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractions`
Expected: PASS

---

### Task 7: Audio và hình cho loại mới

**Việc này làm gì (nói dễ hiểu):**
Bé 6 tuổi chưa đọc thạo nên mọi chữ trên màn hình đều cần giọng đọc: câu hỏi, từng đáp án, câu khen, câu gợi ý, âm của từng chữ cái, câu thoại để bé đoán ai nói. Trò "Chạm vào nhân vật" và "Tên nhân vật" cần hình nhân vật. Task này khai báo những audio/hình bắt buộc để hệ thống tạo media và chỉ hiện tương tác khi đủ.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningAudioParts.For`, `InstructionFor`, `ListeningMediaRequirements.Missing/FindAudio/FindImage`, TTS + sinh hình trong `MediaGenerationService.ListeningMedia.cs`, `MediaType.ListeningImage` theo option.
- Cần thêm:
  - Câu hướng dẫn cố định: CharacterLetter "Em hãy chọn chữ còn thiếu trong tên bạn {CharacterName} nhé!"; WhoSaidIt "Em hãy nghe rồi chạm vào bạn đã nói câu này nhé!".
  - Phần audio cố định (không AI): `retryLook` = "Bé nhìn lại tranh nhé." (Literal), `retryArrow` = "Đi theo mũi tên nhé." (CharacterLetter), `unclear` = "Mình nghe chưa rõ, mình đọc lại nhé." (RolePlay, CharacterLetter).
  - CharacterLetter: `name` (đọc tên), mỗi option `sound` = `VietnameseLetterUnits.SoundOf(text)`, `match`; hình nhân vật gắn option đúng (một hình, mô tả lấy từ hồ sơ nhân vật của truyện — xem ghi chú).
  - WhoSaidIt: `text` = câu thoại, `match`, `noMatch`, mỗi option `text` + hình.
  - PicturePrediction tuổi 6: như cũ (prompt, wait, text/feedback từng tranh, hình từng tranh).
  - Literal: như Checkpoint (prompt, noMatch, text/feedback từng đáp án) + `retryLook`.
- Còn thiếu / ngoài phạm vi: hình nhân vật giống tranh truyện — nếu dự án đã có ảnh/mô tả nhân vật cố định (character sheet) thì dùng; nếu chưa, mô tả do AI soạn (`ImageDescription`) và người duyệt kiểm. Thứ tự nét vẽ chữ do frontend giữ.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningAudioParts.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningMediaRequirements.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningAudioPartsTests.cs`, `ListeningMediaRequirementsTests.cs`, `tests/StoryPlatform.UnitTests/MediaGenerationServiceTests.Listening.cs`

**Interfaces:**
- Consumes: `VietnameseLetterUnits.SoundOf` (Task 2), `BlankIndex`, `TargetAge` (Task 1).
- Produces: hằng `ListeningAudioParts.Name = "name"`, `Sound = "sound"`, `RetryLook = "retryLook"`, `RetryArrow = "retryArrow"`, `Unclear = "unclear"`; `ListeningAudioParts.FixedLine(string partKey) : string`.

- [ ] **Step 1: Test hỏng**

```csharp
    [Fact]
    public void CharacterLetter_ReadsNameSoundsAndPraise()
    {
        var interaction = new ListeningInteraction { Id = 1, Type = ListeningInteractionType.CharacterLetter, TargetAge = 6,
            CharacterName = "Thỏ", BlankIndex = 0, MatchFeedback = "Giỏi quá!" };
        var options = new[] { ("th", true), ("b", false), ("m", false) }.Select((o, i) => new ListeningInteractionOption
            { Id = 10 + i, ListeningInteractionId = 1, Text = o.Item1, IsMatch = o.Item2, SortOrder = i }).ToArray();
        var parts = ListeningAudioParts.For(interaction, options);
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Name && p.Text == "Thỏ");
        Assert.Contains(parts, p => p.OptionId == 10 && p.PartKey == ListeningAudioParts.Sound && p.Text == "thờ");
        Assert.Contains(parts, p => p.OptionId == 11 && p.PartKey == ListeningAudioParts.Sound && p.Text == "bờ");
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.RetryArrow && p.Text == "Đi theo mũi tên nhé.");
        Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Unclear);
    }

    [Fact]
    public void WhoSaidIt_ReadsLineAndNames()
    {
        var interaction = new ListeningInteraction { Id = 2, Type = ListeningInteractionType.WhoSaidIt, TargetAge = 6,
            SourceQuote = "Có ai ở đó không?", MatchFeedback = "Đúng là Thỏ!", NoMatchFeedback = "Mình nghe lại nhé." };
        var options = new[] { new ListeningInteractionOption { Id = 20, ListeningInteractionId = 2, Text = "Thỏ", IsMatch = true },
                              new ListeningInteractionOption { Id = 21, ListeningInteractionId = 2, Text = "bác Gấu", SortOrder = 1 } };
        var keys = ListeningAudioParts.For(interaction, options).Select(p => $"{p.OptionId}:{p.PartKey}").ToArray();
        Assert.Equal([":instruction", ":text", ":match", ":noMatch", "20:text", "21:text"], keys);
    }

    [Fact]
    public void Literal_AddsFixedRetryLook() { /* Checkpoint Kind Literal → có part retryLook = "Bé nhìn lại tranh nhé." */ }
```

Trong `ListeningMediaRequirementsTests`: WhoSaidIt thiếu hình option → `Missing` có `"{id}:o{optionId}:image"`; CharacterLetter thiếu hình option đúng → có `":image"`; PicturePrediction giữ nguyên.

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ListeningAudioPartsTests|FullyQualifiedName~ListeningMediaRequirementsTests"`
Expected: FAIL.

- [ ] **Step 3: Sửa `ListeningAudioParts`**

```csharp
    public const string Name = "name", Sound = "sound", RetryLook = "retryLook", RetryArrow = "retryArrow", Unclear = "unclear";

    /// <summary>Câu hệ thống cố định (spec 27/11/2026), không do AI sinh.</summary>
    public static string FixedLine(string partKey) => partKey switch
    {
        RetryLook => "Bé nhìn lại tranh nhé.",
        RetryArrow => "Đi theo mũi tên nhé.",
        Unclear => "Mình nghe chưa rõ, mình đọc lại nhé.",
        _ => throw new ArgumentOutOfRangeException(nameof(partKey))
    };
```
`InstructionFor` thêm:
```csharp
        ListeningInteractionType.CharacterLetter => string.IsNullOrWhiteSpace(interaction.CharacterName)
            ? "Em hãy chọn chữ còn thiếu trong tên nhân vật nhé!"
            : $"Em hãy chọn chữ còn thiếu trong tên bạn {interaction.CharacterName.Trim()} nhé!",
        ListeningInteractionType.WhoSaidIt => "Em hãy nghe rồi chạm vào bạn đã nói câu này nhé!",
```
Trong `For`:
```csharp
            case ListeningInteractionType.Checkpoint:
                Add(null, Prompt, interaction.Prompt);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                if (interaction.Kind == CheckpointKind.Literal) Add(null, RetryLook, FixedLine(RetryLook));
                break;
            case ListeningInteractionType.RolePlay:
                Add(null, Match, interaction.MatchFeedback);
                if (interaction.TargetAge == 6) Add(null, Unclear, FixedLine(Unclear));
                break;
            case ListeningInteractionType.CharacterLetter:
                Add(null, Name, interaction.CharacterName);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, RetryArrow, FixedLine(RetryArrow));
                Add(null, Unclear, FixedLine(Unclear));
                foreach (var option in Live(options, interaction.Id))
                    Add(option.Id, Sound, VietnameseLetterUnits.SoundOf(option.Text.Trim()));
                return parts;
            case ListeningInteractionType.WhoSaidIt:
                Add(null, Text, interaction.SourceQuote);
                Add(null, Match, interaction.MatchFeedback);
                Add(null, NoMatch, interaction.NoMatchFeedback);
                break;
```
(`Live` = lọc option chưa xóa của interaction, sắp `SortOrder` — tách từ vòng `foreach` cuối hàm để dùng chung.) WhoSaidIt đi tiếp vòng cuối để đọc `text` từng tên; `readsFeedback` giữ nguyên (chỉ Checkpoint, PicturePrediction).

- [ ] **Step 4: Sửa `ListeningMediaRequirements.Missing`**

```csharp
        var imageOptions = interaction.Type switch
        {
            ListeningInteractionType.PicturePrediction or ListeningInteractionType.WhoSaidIt => LiveOptions(),
            ListeningInteractionType.CharacterLetter => LiveOptions().Where(o => o.IsMatch),
            _ => []
        };
        foreach (var option in imageOptions)
            if (FindImage(assets, option.Id) is null) missing.Add($"{interaction.Id}:o{option.Id}:image");
```
`MediaGenerationService.ListeningMedia.cs`: chỗ chọn option cần sinh hình mở rộng theo cùng switch; prompt hình CharacterLetter lấy `ImageDescription` của option đúng — AI ở Task 5 trả `imageDescription` cho option đúng của CharacterLetter (thêm vào luật Task 4: option đúng của CharacterLetter phải có `ImageDescription`, `SCHEMA_INVALID` nếu thiếu, và cho phép trường này ở `ValidateFields`).

- [ ] **Step 5: Chạy test**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ListeningAudioParts|FullyQualifiedName~ListeningMediaRequirements|FullyQualifiedName~MediaGenerationServiceTests"`
Expected: PASS

---

### Task 8: Bé nhận đúng bộ tương tác theo tuổi

**Việc này làm gì (nói dễ hiểu):**
Khi bé mở truyện, hệ thống nhìn tuổi trong hồ sơ: bé 6 tuổi chỉ nhận 5 tương tác tuổi 6, bé tuổi khác chỉ nhận bộ của tuổi mình (tuổi chưa xây thì tạm không có tương tác). Không bao giờ trộn bộ, và tương tác cũ không gắn tuổi không bao giờ hiện. Dữ liệu gửi xuống đủ để màn hình vẽ: tên có ô trống, các thẻ chữ, câu thoại, hình nhân vật, audio từng phần.

**Cần thêm / còn thiếu:**
- Đã có: `ChildListeningContent.Build(...)`, `ReadingInteractionDto`, `ChildListeningService.LoadAsync` (kiểm interaction thuộc session/version/package), lọc media.
- Cần thêm:
  - Lọc `interaction.TargetAge == ListeningAgeSets.AgeFor(child.Age, child.AgeBand)` ở mọi chỗ trẻ đọc tương tác (danh sách scene, nộp câu trả lời, luyện đọc, tóm tắt cuối truyện nếu có đọc ListeningInteraction). Dòng `TargetAge = null` và interaction của tuổi khác → 404 như interaction không thuộc phiên.
  - DTO: `MaskedName` (vd `"__ỏ"` → chuẩn `"_ỏ"` theo ô), `NameUnits` (mảng ô, ô khuyết là `null`), `LetterOptions` (id, text, audio âm), `SpeakerLine` + audio, `Speakers` (id, tên, hình, audio tên), `MaxChoices` (2 với Literal/WhoSaidIt/CharacterLetter), `Graded` (false với PicturePrediction tuổi 6), `FixedAudio` (`retryLook`, `retryArrow`, `unclear`).
  - Không gửi đáp án đúng xuống client trước khi bé chọn (giữ nguyên nguyên tắc hiện tại: `IsMatch` không có trong DTO).
- Còn thiếu / ngoài phạm vi: giao diện frontend (vẽ chữ theo nét mờ, nhảy mừng/lắc đầu) — repo frontend. Bước vẽ chữ không gửi nét lên backend.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/ChildListeningContent.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/DTOs/ChildReadingDtos.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildListeningService.cs` (`LoadAsync`)
- Modify: nơi gọi `ChildListeningContent.Build` (dịch vụ đọc truyện của trẻ — tìm bằng `grep -rn "ChildListeningContent.Build(" src/Core`)
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildReadAloudService.cs` (`CheckPracticeAsync` lọc bộ)
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildListeningContentTests.cs`, `ChildListeningServiceTests.cs`

**Interfaces:**
- Consumes: `ListeningAgeSets.AgeFor` (Task 1), audio parts Task 7.
- Produces: `ChildListeningContent.Build(int sessionId, int childAge, ...)` — thêm tham số `childAge`; DTO mới:
  ```csharp
  public class ReadingLetterOptionDto { public int Id { get; set; } public string Text { get; set; } = ""; public string? SoundAudioUrl { get; set; } }
  public class ReadingSpeakerDto { public int Id { get; set; } public string Name { get; set; } = ""; public string? ImageUrl { get; set; } public string? NameAudioUrl { get; set; } }
  // ReadingInteractionDto thêm:
  public IReadOnlyList<string?>? NameUnits { get; set; }
  public IReadOnlyList<ReadingLetterOptionDto>? LetterOptions { get; set; }
  public string? SpeakerLine { get; set; }
  public IReadOnlyList<ReadingSpeakerDto>? Speakers { get; set; }
  public int MaxChoices { get; set; } = 1;
  public bool Graded { get; set; } = true;
  public int? TargetAge { get; set; }
  ```

- [ ] **Step 1: Test hỏng**

`ChildListeningContentTests`:
```csharp
    [Fact]
    public void Build_Age6Child_GetsOnlyAge6Set() { /* scene có 1 Checkpoint TargetAge null + 1 Literal TargetAge 6 + 1 dòng TargetAge 7 → childAge 6 chỉ thấy Literal */ }

    [Fact]
    public void Build_Age7Child_GetsOnlyAge7Rows() { /* cùng dữ liệu, childAge 7 → chỉ thấy dòng TargetAge 7; không thấy dòng null */ }

    [Fact]
    public void Build_UnbuiltAge_NoInteractions() { /* chỉ có dòng null + tuổi 6, childAge 8 → không có tương tác nào */ }

    [Fact]
    public void Build_CharacterLetter_MasksBlankUnit()
    {
        // CharacterName "Thỏ", BlankIndex 0 → NameUnits == [null, "ỏ"], LetterOptions 3 thẻ có SoundAudioUrl, MaxChoices 2.
    }

    [Fact]
    public void Build_WhoSaidIt_ShowsLineAndSpeakersWithoutAnswer() { /* SpeakerLine, Speakers có ImageUrl; DTO không lộ IsMatch */ }

    [Fact]
    public void Build_Age6Picture_IsUngraded() { /* Graded == false, MaxChoices == 1 */ }
```
`ChildListeningServiceTests`: bé 6 tuổi nộp câu trả lời cho interaction `TargetAge = null` → `NotFoundException`; hồ sơ `Age = null`, nhóm 6-8 → được nộp interaction tuổi 6. Sửa các test hiện có đang seed interaction không có `TargetAge` (gán `TargetAge = 6` và hồ sơ `Age = 6`) để vẫn kiểm đúng hành vi cũ của từng loại.

Viết đủ arrange theo các helper dựng scene/interaction/asset đang có trong hai file test.

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ChildListeningContentTests|FullyQualifiedName~ChildListeningServiceTests"`
Expected: FAIL.

- [ ] **Step 3: Lọc bộ và dựng DTO**

- Nơi gọi `Build` đọc `ChildProfile` của phiên, truyền `ListeningAgeSets.AgeFor(profile.Age, profile.AgeBand)`.
- `Build` bỏ mọi interaction có `TargetAge != childAge` (gồm dòng `null`).
- `ChildListeningService.LoadAsync` và `ChildReadAloudService.CheckPracticeAsync` thêm điều kiện `interaction.TargetAge == ListeningAgeSets.AgeFor(profile.Age, profile.AgeBand)`, sai thì `NotFoundException` như hiện có.
- Dựng DTO:
  ```csharp
  case ListeningInteractionType.CharacterLetter:
      var units = VietnameseLetterUnits.Split(interaction.CharacterName!.Trim());
      dto.CharacterName = interaction.CharacterName;
      dto.NameUnits = units.Select((u, i) => i == interaction.BlankIndex ? null : u).ToArray();
      dto.LetterOptions = live.Select(o => new ReadingLetterOptionDto { Id = o.Id, Text = o.Text,
          SoundAudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, o.Id, ListeningAudioParts.Sound)?.Url }).ToArray();
      dto.MaxChoices = 2;
      break;
  case ListeningInteractionType.WhoSaidIt:
      dto.SpeakerLine = interaction.SourceQuote;
      dto.Speakers = live.Select(o => new ReadingSpeakerDto { Id = o.Id, Name = o.Text,
          ImageUrl = ListeningMediaRequirements.FindImage(assets, o.Id)?.Url,
          NameAudioUrl = ListeningMediaRequirements.FindAudio(assets, interaction.Id, o.Id, ListeningAudioParts.Text)?.Url }).ToArray();
      dto.MaxChoices = 2;
      break;
  ```
  Checkpoint `Literal` → `MaxChoices = 2`; PicturePrediction có `TargetAge == 6` → `Graded = false`. `AudioUrls` thêm các khóa `name`, `retryLook`, `retryArrow`, `unclear` khi có. `dto.TargetAge = interaction.TargetAge`.
- Cập nhật comment `Type` của `ReadingInteractionDto` liệt kê thêm `"CharacterLetter"`, `"WhoSaidIt"`; `Kind` thêm `"Literal"`.
- URL media vẫn ký qua `IChildMediaUrlSigner` như các trường hiện có.

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ChildListening|FullyQualifiedName~ChildReadAloud"`
Expected: PASS

---

### Task 9: Nộp câu trả lời — chọn lại 1 lần, đoán tranh không chấm, bước đọc âm/vẽ

**Việc này làm gì (nói dễ hiểu):**
Bé chọn sai lần đầu thì được gợi ý và chọn lại; sai lần hai thì hệ thống nói đáp án rồi truyện chạy tiếp. Với "Đoán tiếp bằng tranh" hệ thống chỉ ghi lại lựa chọn, không đúng không sai. Với "Tên nhân vật và chữ cái", sau khi chọn chữ, bé đọc âm và vẽ chữ; hai bước này chỉ ghi "đã thử" hoặc "bỏ qua".

**Cần thêm / còn thiếu:**
- Đã có: `SubmitAttemptAsync`, mỗi phiên ghi 1 lần với loại không phải EventOrdering, `Revealed` cho EventOrdering, `IsAttempted`.
- Cần thêm:
  - Literal, WhoSaidIt, CharacterLetter: tối đa 2 attempt mỗi phiên; attempt khớp hoặc attempt thứ 2 → kết thúc, trả `Revealed = true` + `CorrectOptionId` khi sai lần 2. Gửi lại sau khi kết thúc → trả kết quả cũ (như hiện tại).
  - Kết quả lần sai 1: Literal → `Feedback = FixedLine(RetryLook)`; WhoSaidIt → `NoMatchFeedback`; CharacterLetter → câu gợi ý cố định "Mình chọn lại nhé." (không có chữ "sai").
  - PicturePrediction tuổi 6: `IsMatch = null`, `Matched = null`, feedback của tranh đã chọn.
  - CharacterLetter bước sau: `PATCH .../listening/{interactionId}/steps` với `{ "sound": "tried|skipped", "trace": "tried|skipped" }` ghi `StepResultsJson` vào attempt cuối cùng của phiên.
  - `IsAttempted` cho loại 2 lượt: có attempt khớp hoặc ≥ 2 attempt.
- Còn thiếu / ngoài phạm vi: chấm bước đọc âm bằng ASR nằm ở Task 10 (chỉ để hiện xanh/vàng cho bé; kết quả lưu vẫn là "đã thử"). Hoạt hình nhảy mừng/lắc đầu — frontend.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildListeningService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/ChildListeningContent.cs` (`IsAttempted`, hằng `MaxChoiceAttempts = 2`)
- Modify: DTO `SubmitListeningAttemptRequestDto`, `ListeningAttemptResultDto` (thêm `CorrectOptionId : int?`, `CanRetry : bool`), `ListeningStepsRequestDto` (mới, cùng file DTO)
- Modify: `src/Core/StoryPlatform.Api/Controllers/ChildReadingController.cs` (endpoint steps)
- Modify: interface `IChildListeningService`
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildListeningServiceTests.cs`

**Interfaces:**
- Consumes: `ListeningAudioParts.FixedLine` (Task 7), bộ lọc tuổi (Task 8).
- Produces:
  - `IChildListeningService.SaveStepsAsync(int childProfileId, int sessionId, int interactionId, ListeningStepsRequestDto request, CancellationToken ct)`
  - `PATCH /api/v1/child/reading/sessions/{sessionId}/listening/{interactionId}/steps` (cùng prefix route với endpoint nộp attempt hiện có)
  - `ListeningAttemptResultDto.CorrectOptionId`, `.CanRetry`

- [ ] **Step 1: Test hỏng**

```csharp
    [Fact] public async Task Literal_FirstWrong_CanRetryWithLookHint() { /* CanRetry true, Feedback "Bé nhìn lại tranh nhé.", Revealed false */ }
    [Fact] public async Task Literal_SecondWrong_RevealsAnswer() { /* Revealed true, CorrectOptionId = option khớp, CanRetry false */ }
    [Fact] public async Task Literal_ThirdSubmit_ReturnsPreviousResult() { /* không thêm dòng attempt thứ 3 */ }
    [Fact] public async Task WhoSaidIt_FirstRight_Finishes() { /* Matched true, CanRetry false, Feedback = MatchFeedback */ }
    [Fact] public async Task CharacterLetter_SecondWrong_RevealsLetter() { /* CorrectOptionId = thẻ "th" */ }
    [Fact] public async Task Age6Picture_RecordsChoiceUngraded() { /* attempt.IsMatch null, result.Matched null */ }
    [Fact] public async Task Steps_SavedOnLastAttempt() { /* StepResultsJson == {"sound":"tried","trace":"skipped"} */ }
    [Fact] public async Task Steps_InvalidValue_BadRequest() { /* "sound":"good" → BadRequest LISTENING_ANSWER_INVALID */ }
    [Fact] public async Task Steps_BeforeChoosing_Conflict() { /* chưa có attempt → ConflictException */ }
```
Dựng dữ liệu bằng helper hiện có trong `ChildListeningServiceTests`; mỗi test phải có assert cụ thể như ghi chú.

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildListeningServiceTests`
Expected: FAIL.

- [ ] **Step 3: Sửa `AnswerAsync`**

```csharp
    private static bool AllowsRetry(ListeningInteraction i) =>
        i.Type is ListeningInteractionType.WhoSaidIt or ListeningInteractionType.CharacterLetter ||
        (i.Type == ListeningInteractionType.Checkpoint && i.Kind == CheckpointKind.Literal);

    // Đầu AnswerAsync, thay khối "mỗi phiên ghi một lần":
    var finished = AllowsRetry(interaction)
        ? context.Attempts.Any(a => a.IsMatch == true) || context.Attempts.Count >= ChildListeningContent.MaxChoiceAttempts
        : interaction.Type != ListeningInteractionType.EventOrdering && context.Attempts.Count > 0;
    if (finished) { /* trả kết quả của attempt cuối như hiện tại */ }
```
Trong `switch` ghi attempt, gộp `CharacterLetter`, `WhoSaidIt` vào nhánh chọn option; PicturePrediction tuổi 6 ghi `attempt.IsMatch = null`.

Trong `Result(...)`, cho loại 2 lượt:
```csharp
        var attemptsSoFar = context.Attempts.Count + 1;
        var matched = attempt.IsMatch == true;
        var revealed = !matched && attemptsSoFar >= ChildListeningContent.MaxChoiceAttempts;
        result.Matched = matched;
        result.CanRetry = !matched && !revealed;
        result.Revealed = revealed;
        result.CorrectOptionId = revealed ? context.Options.First(o => o.IsMatch).Id : null;
        result.Feedback = matched ? MatchText(interaction, option) : revealed ? RevealText(interaction) : RetryText(interaction);
```
- `MatchText`: Literal → `option.Feedback`; WhoSaidIt, CharacterLetter → `MatchFeedback`.
- `RetryText`: Literal → `FixedLine(RetryLook)`; WhoSaidIt → `NoMatchFeedback`; CharacterLetter → `"Mình chọn lại nhé."`.
- `RevealText`: Literal → `option đúng.Feedback`; WhoSaidIt → `$"Bạn {đúng.Text} đã nói câu này đó."`; CharacterLetter → `$"Chữ còn thiếu là {đúng.Text}."`.
- Audio đi kèm theo part tương ứng (`Feedback` của option, `Match`, `NoMatch`, `RetryLook`); câu cố định không có audio riêng (`RevealText` của WhoSaidIt/CharacterLetter) thì `FeedbackAudioUrl = null` và frontend phát audio tên/âm của option đúng (đã có trong DTO).

- [ ] **Step 4: Endpoint steps**

```csharp
    [HttpPatch("sessions/{sessionId:int}/listening/{interactionId:int}/steps")]
    public async Task<IActionResult> SaveSteps(int sessionId, int interactionId, [FromBody] ListeningStepsRequestDto request, CancellationToken ct)
    {
        var childProfileId = /* lấy như các action trẻ khác trong controller */;
        await _listening.SaveStepsAsync(childProfileId, sessionId, interactionId, request, ct);
        return /* envelope thành công giống endpoint nộp attempt */;
    }
```
Copy đúng cách lấy `childProfileId`, attribute quyền và envelope trả về từ action nộp attempt ngay trong `ChildReadingController` (route chính xác lấy theo action đó). `SaveStepsAsync`: chỉ cho `CharacterLetter`; giá trị hợp lệ `tried`/`skipped`; chưa có attempt → `ConflictException("LISTENING_STEPS_BEFORE_ANSWER")`; ghi vào attempt mới nhất, dùng cùng khóa `AcquireTransactionLockAsync(session.StoryId)`.

- [ ] **Step 5: Chạy test**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildListening`
Expected: PASS

---

### Task 10: Chấm mềm giọng nói cho "Nói theo nhân vật" và âm chữ cái

**Việc này làm gì (nói dễ hiểu):**
Bé nhấn mic nói theo câu của nhân vật; hệ thống so từng từ: từ nghe rõ sáng xanh, từ chưa rõ sáng vàng để bé đọc lại (màn hình giới hạn 2 lần). Không có điểm, không có chữ "sai" — chỉ "mình nghe chưa rõ, mình đọc lại nhé". Kết quả từng từ lưu lại cho phụ huynh. Bước đọc âm của chữ cái dùng cùng cơ chế với câu mẫu là âm của chữ ("thờ").

**Cần thêm / còn thiếu:**
- Đã có: `ChildReadAloudService.ScoreAsync` (kiểm quyền giọng nói `VoiceAnswersEnabled`, ASR, `ReadAloudMatcher`, lưu `ReadAloudRecording` theo `ListeningInteractionId`, telemetry chỉ số đếm), endpoint `POST .../read-aloud-practice/{interactionId}`.
- Cần thêm:
  - `CheckPracticeAsync` nhận thêm `RolePlay` (câu mẫu = `SourceQuote`) và `CharacterLetter` (câu mẫu = `SoundOf(option đúng)`), chỉ với bộ của bé (Task 8). Với RolePlay/CharacterLetter không cần scene là luyện đọc cuối truyện.
  - Lưu `WordStatusesJson` khi ghi `ReadAloudRecording` (mọi loại).
  - `ReadAloudResultDto.Message` cho tuổi 6 = `FixedLine(Unclear)` khi còn từ vàng, `MatchFeedback` khi hết từ vàng.
- Còn thiếu / ngoài phạm vi: ASR nhận một âm đơn ("bờ") có thể kém chính xác — chấp nhận vì bước này chỉ ghi "đã thử"; nếu nhận dạng rỗng thì trả mọi từ `Retry`, không báo lỗi. Giới hạn 2 lần đọc lại do frontend đếm (backend thay bản ghi cũ như hiện tại).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildReadAloudService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/ReadAloudPolicy.cs` (nếu message nằm ở đây — tìm `ReadAloudPolicy.Message`)
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildReadAloudServiceTests.cs` (file hiện có; nếu tên khác, tìm `CheckPracticeAsync` trong `tests/`)

**Interfaces:**
- Consumes: `VietnameseLetterUnits.SoundOf` (Task 2), `ReadAloudRecording.WordStatusesJson` (Task 1), lọc tuổi (Task 8), `FixedLine` (Task 7).
- Produces: `CheckPracticeAsync` chấp nhận `ListeningInteractionType.ReadAloud | RolePlay | CharacterLetter`.

- [ ] **Step 1: Test hỏng**

```csharp
    [Fact] public async Task RolePlay_Age6_ScoresAgainstLine() { /* ASR mock trả "có ai ở đó" cho câu "Có ai ở đó không?" → 4 Good, 1 Retry; Message "Mình nghe chưa rõ, mình đọc lại nhé." */ }
    [Fact] public async Task RolePlay_AllGood_ReturnsPraise() { /* Message == MatchFeedback */ }
    [Fact] public async Task CharacterLetter_ScoresAgainstLetterSound() { /* câu mẫu "thờ" */ }
    [Fact] public async Task Recording_StoresWordStatuses() { /* WordStatusesJson == ["Good","Good","Good","Good","Retry"], không chứa chữ bé nói */ }
    [Fact] public async Task VoiceOff_Forbidden() { /* VoiceAnswersEnabled false → ForbiddenException như hiện tại */ }
    [Fact] public async Task OtherAgeSetInteraction_NotFound() { /* bé 7 tuổi gửi RolePlay TargetAge 6 → NotFoundException */ }
```

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildReadAloud`
Expected: FAIL.

- [ ] **Step 3: Sửa `CheckPracticeAsync`**

```csharp
        var target = interaction.Type switch
        {
            ListeningInteractionType.ReadAloud => interaction.SourceQuote,
            ListeningInteractionType.RolePlay => interaction.SourceQuote,
            ListeningInteractionType.CharacterLetter => VietnameseLetterUnits.SoundOf(
                (await LoadOptionsAsync(interactionId, ct)).Single(o => o.IsMatch).Text.Trim()),
            _ => null
        };
        if (interaction == null || interaction.IsDeleted || string.IsNullOrWhiteSpace(target) || /* các kiểm cũ */)
            throw new NotFoundException("Đoạn luyện đọc", interactionId);
```
Kiểm scene + `BuildReadAloud` chỉ áp cho `ReadAloud`; RolePlay/CharacterLetter chỉ cần interaction thuộc version/package/bộ tuổi của phiên. Truyền `target` vào `ScoreAsync`.

- [ ] **Step 4: Lưu trạng thái từng từ và message**

Trong `StageRecordingAsync` thêm tham số `IReadOnlyList<ReadAloudWordStatus> statuses`:
```csharp
        recording.WordStatusesJson = JsonSerializer.Serialize(statuses.Select(s => s.ToString()));
```
Message: nếu interaction là RolePlay/CharacterLetter tuổi 6 → `retry > 0 ? ListeningAudioParts.FixedLine(ListeningAudioParts.Unclear) : interaction.MatchFeedback`; loại khác giữ `ReadAloudPolicy.Message(good, total)`.

- [ ] **Step 5: Chạy test**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildReadAloud`
Expected: PASS

---

### Task 11: Người duyệt và phụ huynh

**Việc này làm gì (nói dễ hiểu):**
Người duyệt thấy riêng từng bộ theo tuổi ("Bộ 6 tuổi", sau này "Bộ 7 tuổi"…) và sửa được các trường mới (tên nhân vật + chữ khuyết, câu thoại + nhân vật, 2 tranh không đáp án). Sửa xong hệ thống kiểm lại luật như lúc AI soạn. Phụ huynh xem được bé đã chọn gì, chọn lại mấy lần, đoán tranh nào, từ nào đọc chưa rõ, đã thử hay bỏ qua bước đọc âm / vẽ chữ — không có điểm số.

**Cần thêm / còn thiếu:**
- Đã có: `StoryReviewService.Listening.cs` (xem/sửa/duyệt tương tác, gọi `ListeningInteractionRules.ValidateText` + kiểm cấu trúc), `ParentReadingFeedbackService` (kết quả cho phụ huynh), readiness kiểm media đủ trước khi xuất bản.
- Cần thêm:
  - DTO duyệt: `TargetAge`, `BlankIndex`; nhóm danh sách theo `TargetAge`; request sửa chấp nhận `blankIndex`, speakers (options có `imageDescription`).
  - Kiểm luật khi sửa dùng `ListeningRuleContext { TargetAge = interaction.TargetAge }`.
  - Readiness: truyện chỉ sẵn sàng xuất bản khi **mọi bộ tuổi đã xây trong nhóm** (`TargetAgesFor(band)`) đã duyệt và đủ media; dòng `TargetAge = null` không tính và không chặn. Nhóm chưa có tuổi nào đã xây thì không cần tương tác để xuất bản.
  - Danh sách duyệt không hiện dòng `TargetAge = null` (không còn tới trẻ).
  - Phụ huynh: mỗi attempt hiển thị `TargetAge`, số lần chọn, đáp án đã chọn, `Graded`, `StepResultsJson`, `WordStatusesJson` (dạng "4/5 từ rõ, từ chưa rõ: không").
- Còn thiếu / ngoài phạm vi: màn hình duyệt/phụ huynh ở frontend. Báo cáo tuần phụ huynh (chưa xây theo memory dự án).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/StoryReview/Services/StoryReviewService.Listening.cs` (+ DTO duyệt listening trong `Features/StoryReview/DTOs/`)
- Modify: `src/Core/StoryPlatform.Application/Features/StoryReview/Services/LearningArtifactService.cs` (request sinh lại có `targetAge`)
- Modify: service readiness listening (tìm `ListeningMediaRequirements.Missing(` trong `src/Core/StoryPlatform.Application`)
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ParentReadingFeedbackService.cs` (+ DTO phản hồi phụ huynh)
- Test: `tests/StoryPlatform.UnitTests/StoryReviewListeningTests.cs`, test hiện có của `ParentReadingFeedbackService` (tìm trong `tests/`)

**Interfaces:**
- Consumes: mọi thứ từ Task 1–10.
- Produces: review DTO `TargetAge`, `BlankIndex`; parent DTO `TargetAge`, `ChoiceCount`, `Graded`, `Steps`, `WordStatuses`.

- [ ] **Step 1: Test hỏng**

```csharp
    [Fact] public async Task Review_GroupsByTargetAge_HidesUntaggedRows() { /* có dòng tuổi 6, tuổi 7, null → trả nhóm 6 và 7, không có dòng null */ }
    [Fact] public async Task Review_EditCharacterLetter_BadBlank_Rejected() { /* sửa BlankIndex trỏ "ỏ" → lỗi LETTER_BLANK_INVALID */ }
    [Fact] public async Task Review_EditAge6Picture_WithMatch_Rejected() { /* MATCH_RULE_INVALID */ }
    [Fact] public async Task Readiness_Band6To8_NeedsAge6SetApproved() { /* chỉ có dòng null đã duyệt → chưa sẵn sàng; duyệt bộ 6 đủ media → sẵn sàng */ }
    [Fact] public async Task Readiness_Band9To10_NoBuiltAge_Ready() { /* không có tương tác nào → vẫn sẵn sàng */ }
    [Fact] public async Task ParentFeedback_ShowsStepsAndWordStatusesWithoutScore() { /* có Steps.sound "tried", WordStatuses; không có trường điểm */ }
```

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~StoryReviewListeningTests|FullyQualifiedName~ParentReadingFeedback"`
Expected: FAIL.

- [ ] **Step 3: Triển khai**

Theo đúng mẫu xử lý trường của Checkpoint/PicturePrediction đã có trong `StoryReviewService.Listening.cs`: map thêm `TargetAge`, `BlankIndex`, options WhoSaidIt; khi lưu sửa, dựng draft rồi gọi `ListeningInteractionRules.Validate([draft], context with { TargetAge = row.TargetAge, Count = 1 })` (luật cấu trúc + chữ), trả lỗi theo định dạng hiện có. Readiness lặp `ListeningAgeSets.TargetAgesFor(band)` và yêu cầu mỗi bộ có ít nhất 1 tương tác đã duyệt, không thiếu media. `ParentReadingFeedbackService` đọc thêm `StepResultsJson`, `WordStatusesJson`, nhóm attempt theo interaction để đếm `ChoiceCount`.

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~StoryReviewListeningTests|FullyQualifiedName~ParentReadingFeedback"`
Expected: PASS

---

### Task 12: Trang hướng dẫn test API tuổi 6 (cần bạn duyệt trước khi tạo file)

**Việc này làm gì (nói dễ hiểu):**
Một trang HTML để cả nhóm test tay các API mới: bé 6 tuổi nhận đúng bộ, chọn lại 1 lần, đoán tranh không chấm, ghi bước đọc âm/vẽ, chấm mềm giọng nói, người duyệt sửa tương tác mới, phụ huynh xem kết quả.

**Cần thêm / còn thiếu:**
- Đã có: template `docs/api/_template-api-test-guide.html`, mẫu `docs/api/api-test-guide-buoc-1-7.html`, seed trong `Database/Seed`.
- Cần thêm: `docs/api/age-6-interactions-api-test-guide.html` dựng từ template (CLAUDE.md §13); khóa localStorage `"age6-interactions-guide-vars"` / `"age6-interactions-guide-checks"`; seed một hồ sơ bé `Age = 6` và một truyện nhóm 6-8 có đủ bộ tuổi 6 đã duyệt nếu seed hiện chưa có (thêm vào file seed hiện có, không tạo file seed mới).
- Còn thiếu / ngoài phạm vi: test tự động end-to-end giao diện.

**Files:**
- Create: `docs/api/age-6-interactions-api-test-guide.html` (copy template)
- Modify (nếu cần): file seed bé/truyện trong `Database/Seed`

- [ ] **Step 1:** Copy template, chỉ sửa `<title>`, `<header>`, `#chung` và phần dữ liệu tới dòng `HẾT PHẦN DỮ LIỆU`.
- [ ] **Step 2:** `STEPS`: Task 8 (GET nội dung đọc: bé 6 tuổi vs bé 7 tuổi), Task 9 (POST attempt ×3 tình huống, PATCH steps), Task 10 (POST read-aloud-practice RolePlay/CharacterLetter), Task 11 (review list/sửa, parent feedback). Mỗi API đủ test `ok`/`neg`/`edge`: sai role, không token, token trẻ khác bộ tuổi, chọn lần 3, `sound` sai giá trị, giọng nói tắt.
- [ ] **Step 3:** Kết quả mong đợi tính từ seed thật và message đúng như code (`LISTENING_ANSWER_INVALID`, `LISTENING_STEPS_BEFORE_ANSWER`, "Bé nhìn lại tranh nhé.", …).
- [ ] **Step 4:** Mở file trong trình duyệt: không lỗi console, đếm đúng số API/test case, thử Copy request của 3 case.

---

### Task 13: Điểm dừng nghĩa đen có đáp án là tranh kèm chữ

**Việc này làm gì (nói dễ hiểu):**
Bé 6 tuổi chưa đọc thạo, nên mỗi đáp án của "Điểm dừng nghĩa đen" hiện thành một thẻ có tranh minh họa và chữ bên dưới (vẫn có giọng đọc). Tranh do AI soạn mô tả từ chính cảnh truyện rồi hệ thống vẽ lại theo tranh truyện, không dùng ảnh bên ngoài. Người duyệt xem và sửa mô tả tranh như với "Đoán tiếp bằng tranh".

**Cần thêm / còn thiếu:**
- Đã có: sinh ảnh cho lựa chọn (`QueueListeningImageTasksAsync`, `EnsureListeningImageAsync`, `ListeningMediaRequirements.ImageOptions`), `ReadingInteractionOptionDto.ImageUrl`, ký URL ảnh của option, `UpdateListeningOptionDto.ImageDescription`.
- Cần thêm: `imageDescription` cho mọi đáp án của Checkpoint `Literal` (schema AI, mapper, luật bắt buộc, cho phép ở `ValidateFields`); `ImageOptions` trả đáp án Literal; ba chỗ đang lọc theo loại để xếp việc tạo ảnh dùng chung một hàm `HasOptionImages`.
- Còn thiếu / ngoài phạm vi: bé chưa đọc được chữ trên tranh nên ảnh không được chứa chữ (đã có trong prompt AI); chất lượng/độ khớp tranh với truyện do người duyệt kiểm.

**Files:**
- Modify: `src/Shared/StoryPlatform.Contracts/AI/Models/StoryContentModels.cs` (`ListeningChoiceDto.ImageDescription`)
- Modify: `src/AI/StoryPlatform.AI.Application/ContentGeneration/GenerateListeningInteractionHandler.cs` (schema và luật `Checkpoint:Age6`)
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionAiMapper.cs` (map `ImageDescription` cho option Checkpoint)
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionRules.cs` và `ListeningInteractionRules.Age6.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningMediaRequirements.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs`, `MediaGenerationService.CreatorUploadTasks.cs`, `MediaGenerationService.ParentPackages.cs` (bộ lọc loại → `HasOptionImages`)
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/DTOs/ChildReadingDtos.cs` (sửa chú thích `ImageUrl`)
- Test: `ListeningInteractionRulesAge6Tests.cs`, `ListeningMediaRequirementsTests.cs`, `ListeningInteractionAiMapperTests.cs`, `ChildListeningContentTests.cs`, `MediaGenerationServiceTests.Listening.cs`, `ListeningHttpContractTests.cs`

**Interfaces:**
- Produces: `ListeningChoiceDto.ImageDescription : string?`; `ListeningMediaRequirements.HasOptionImages(ListeningInteraction) : bool` (true cho PicturePrediction, WhoSaidIt, CharacterLetter và Checkpoint `Literal`); `ImageOptions` trả mọi đáp án của Literal.

- [ ] **Step 1: Viết test hỏng**

```csharp
// ListeningInteractionRulesAge6Tests: đáp án Literal thiếu imageDescription bị từ chối, có thì hợp lệ.
[Fact]
public void Literal_OptionWithoutPicture_Rejected()
{
    var item = LiteralItem(imageDescriptions: [null, "Bác gấu nâu to"]);
    Assert.Contains("SCHEMA_INVALID", Codes(item, Ctx(T.Checkpoint)));
}

[Fact]
public void Literal_AllOptionsWithPicture_Valid() =>
    Assert.Empty(Codes(LiteralItem(imageDescriptions: ["Thỏ con đứng trước rừng", "Bác gấu nâu to"]), Ctx(T.Checkpoint)));

// ListeningMediaRequirementsTests: Literal thiếu hình option → Missing có ":image".
[Fact]
public void Literal_MissingOptionImages_ReportedAsMissing()
{
    var (interaction, options) = LiteralWithTwoOptions();
    var missing = ListeningMediaRequirements.Missing(interaction, options, assets: []);
    Assert.Equal(2, missing.Count(x => x.EndsWith(":image")));
}

// ChildListeningContentTests: thẻ Literal có ImageUrl và vẫn không lộ đáp án đúng.
[Fact]
public void Build_Literal_CardsHavePictureAndText() { /* option.ImageUrl khác null, Feedback null, IsMatch không có trong DTO */ }
```
`LiteralItem`, `LiteralWithTwoOptions` là helper mới trong hai file test, dựng theo mẫu `WhoSaid(...)` có sẵn.

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~Age6Tests|FullyQualifiedName~ListeningMediaRequirementsTests|FullyQualifiedName~ChildListeningContentTests"`
Expected: FAIL.

- [ ] **Step 3: Triển khai**

`ListeningChoiceDto` thêm `public string? ImageDescription { get; init; }`.

`ListeningMediaRequirements`:
```csharp
public static bool HasOptionImages(ListeningInteraction interaction) =>
    interaction.Type is ListeningInteractionType.PicturePrediction or ListeningInteractionType.WhoSaidIt or ListeningInteractionType.CharacterLetter ||
    interaction is { Type: ListeningInteractionType.Checkpoint, Kind: CheckpointKind.Literal };

public static IEnumerable<ListeningInteractionOption> ImageOptions(ListeningInteraction interaction,
    IReadOnlyList<ListeningInteractionOption> options) => options
    .Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted &&
        HasOptionImages(interaction) &&
        (interaction.Type != ListeningInteractionType.CharacterLetter || x.IsMatch))
    .OrderBy(x => x.SortOrder);
```
Ba chỗ `interactions.Where(x => x.Type is PicturePrediction or WhoSaidIt or CharacterLetter)` trong `MediaGenerationService.ListeningMedia.cs`, `.CreatorUploadTasks.cs`, `.ParentPackages.cs` đổi thành `interactions.Where(ListeningMediaRequirements.HasOptionImages)`.

Luật: trong `ValidateFields` cho phép `ImageDescription` khi `t == Checkpoint && item.Kind == Literal`; trong `ValidateCheckpoint` thêm, khi `item.Kind == CheckpointKind.Literal`:
```csharp
for (var j = 0; j < item.Options.Count; j++)
    if (string.IsNullOrWhiteSpace(item.Options[j].ImageDescription))
        errors.Add(new("SCHEMA_INVALID", index, $"options[{j}].imageDescription"));
```
Option của Checkpoint kind khác vẫn bị cấm `ImageDescription`. Hàm `Forbid(o.ImageDescription ...)` cần biết kind của interaction: dùng `item.Kind`.

AI: schema `Checkpoint:Age6` option thành `O(("text", S()), ("imageDescription", S()), ("feedback", S()), ("isMatch", B()))`; thêm vào luật: "Every option also has imageDescription: a picture of that answer taken only from the story scene, preserving characters and setting, with no writing and no characters that are not in the story." Mapper `Checkpoint` map `ImageDescription = o.ImageDescription` khi `x.Kind == "Literal"`.

- [ ] **Step 4: Chạy test, phải qua**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~Listening` và `dotnet test tests/StoryPlatform.AI.IntegrationTests --filter FullyQualifiedName~Listening`
Expected: PASS

---

### Task 14: Mốc thời gian từng chữ để chữ sáng lên theo giọng đọc

**Việc này làm gì (nói dễ hiểu):**
Khi giọng mẫu đọc câu cho bé nói theo (Nói theo nhân vật), từng chữ sáng lên đúng lúc giọng đọc tới chữ đó. Hệ thống lưu thời điểm bắt đầu và kết thúc của từng chữ cùng với file âm thanh, rồi gửi cho màn hình bé. Cũng dùng cho câu thoại của "Chạm vào nhân vật", tên nhân vật của "Tên nhân vật và chữ cái" và câu hỏi nghĩa đen.

**Cần thêm / còn thiếu:**
- Đã có: `GoogleCloudTtsProvider` đã tạo mốc thời gian từng chữ (`EnableTimePointing` + `<mark>`), đặt vào `GeneratedMedia.Metadata["wordTimingsJson"]` dạng `[{"word","start","end"}]`; cột `MediaAsset.WordTimings`; truyện chính đã lưu và trả `WordTimingsJson` cho từng đoạn; gói phụ huynh sao chép `WordTimings` khi dùng lại audio.
- Cần thêm: lưu `WordTimings` khi tạo audio của tương tác lúc nghe; trả `AudioWordTimings` cho các phần cần sáng chữ.
- Còn thiếu / ngoài phạm vi: `GeminiTtsProvider` không có mốc thời gian (`hasWordTimings = false`), khi đó `AudioWordTimings` trống và màn hình sáng cả câu một lần; backend không tự ước lượng mốc. Từ cuối của câu có `end = null` (Cloud TTS không báo độ dài), màn hình lấy hết file âm thanh làm mốc kết thúc. Câu hướng dẫn cố định và lời khen không cần mốc.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs` (`EnsureListeningAudioPartAsync`)
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/DTOs/ChildReadingDtos.cs` (`ReadingInteractionDto.AudioWordTimings`)
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/ChildListeningContent.cs`
- Test: `MediaGenerationServiceTests.Listening.cs`, `ChildListeningContentTests.cs`

**Interfaces:**
- Produces: `ReadingInteractionDto.AudioWordTimings : Dictionary<string, string>` — khóa là `AudioPartKey` (`text`, `name`, `prompt`), giá trị là chuỗi JSON mốc từng chữ giống `WordTimingsJson` của đoạn truyện; `ListeningAudioParts.HighlightKeys = ["text", "name", "prompt"]`.

- [ ] **Step 1: Viết test hỏng**

```csharp
// MediaGenerationServiceTests.Listening.cs: audio listening lưu mốc từng chữ từ provider.
[Fact]
public async Task ListeningAudio_StoresWordTimingsFromProvider()
{
    // TTS giả trả GeneratedMedia có Metadata["wordTimingsJson"] = [{"word":"Có","start":0.0,"end":0.4}, ...]
    // Sau khi tạo audio part Text của RolePlay tuổi 6: asset.WordTimings == chuỗi đó.
}

[Fact]
public async Task ListeningAudio_ProviderWithoutTimings_LeavesNull() { /* Metadata không có khóa → WordTimings null */ }

// ChildListeningContentTests:
[Fact]
public void Build_RolePlayAge6_ExposesWordTimingsForTextOnly()
{
    // asset text có WordTimings, asset match (lời khen) có WordTimings → AudioWordTimings chỉ có khóa "text".
}

[Fact]
public void Build_WithoutTimings_ReturnsEmptyDictionary() { /* AudioWordTimings.Count == 0 */ }
```

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~MediaGenerationServiceTests|FullyQualifiedName~ChildListeningContentTests"`
Expected: FAIL.

- [ ] **Step 3: Triển khai**

Trong `EnsureListeningAudioPartAsync`, ngay sau `asset.MimeType = audio.MimeType;`:
```csharp
asset.WordTimings = ListeningAudioParts.HighlightKeys.Contains(part.PartKey) ? audio.GetMetadata("wordTimingsJson") : null;
```
`ListeningAudioParts`:
```csharp
/// <summary>Phần âm thanh mà bé nhìn chữ đi theo giọng đọc nên cần mốc thời gian từng chữ.</summary>
public static IReadOnlyList<string> HighlightKeys { get; } = [Text, Name, Prompt];
```
`ReadingInteractionDto`:
```csharp
/// <summary>Mốc thời gian từng chữ theo AudioPartKey (text, name, prompt), cùng định dạng WordTimingsJson của đoạn truyện. Trống nếu nhà cung cấp giọng đọc không có mốc.</summary>
public Dictionary<string, string> AudioWordTimings { get; set; } = new();
```
`ChildListeningContent`: trong vòng `foreach (var key in new[] {...})` đã có, thêm khi tìm được asset audio:
```csharp
var audio = ListeningMediaRequirements.FindAudio(assets, interaction.Id, null, key);
if (audio?.Url is { } url) dto.AudioUrls[key] = url;
if (ListeningAudioParts.HighlightKeys.Contains(key) && !string.IsNullOrWhiteSpace(audio?.WordTimings))
    dto.AudioWordTimings[key] = audio!.WordTimings!;
```
Thứ tự chữ trong mốc trùng thứ tự chữ của `ReadAloudResultDto.Words` (cả hai cắt chữ từ cùng câu mẫu), nên màn hình dùng chỉ số chữ để tô xanh/vàng đúng chỗ. Không cần migration (cột `WordTimings` đã có).

- [ ] **Step 4: Chạy test, phải qua**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~Listening`
Expected: PASS

---

### Quyết định bổ sung (28/11/2026, sau review)

- Phụ huynh vẫn xem **tất cả** lựa chọn và kết quả (kể cả khớp/không khớp) — giữ nguyên `ParentListeningFeedbackDto`.
- Hình trong tương tác chỉ do AI mô tả từ cảnh truyện rồi hệ thống vẽ theo tranh truyện; không dùng ảnh bên ngoài hay kho ảnh có sẵn.
- Đáp án của điểm dừng nghĩa đen là tranh kèm chữ (Task 13); mốc thời gian từng chữ lưu cùng audio (Task 14).

### Kiểm tra cuối

- [ ] Run: `dotnet build` — Expected: thành công, không warning mới về switch thiếu nhánh enum.
- [ ] Run: `dotnet test tests/StoryPlatform.UnitTests` — Expected: PASS toàn bộ.
- [ ] Run: `dotnet test tests/StoryPlatform.AI.UnitTests` và `dotnet test tests/StoryPlatform.AI.IntegrationTests --filter FullyQualifiedName~Listening` — Expected: PASS.
- [ ] Run: `dotnet test tests/StoryPlatform.IntegrationTests` (nếu có DB test) — Expected: migration áp được, test listening PASS.
- [ ] `git status` / `git diff --stat`: chỉ các file ghi trong plan; migration không có lệnh xóa.

## Đối chiếu spec (tự rà)

| Spec tuổi 6 | Task |
|---|---|
| Điểm dừng nghĩa đen: 2–3 đáp án có đọc to, đúng → khen; sai → "nhìn lại tranh nhé", chọn lại 1 lần; sai nữa → nói đáp án | 3, 4, 7, 8, 9 |
| Nói theo nhân vật: giọng mẫu từng chữ sáng, mic, từ xanh/vàng, đọc lại tối đa 2 lần, lưu từng từ cho phụ huynh, câu "mình nghe chưa rõ…" | 7, 8, 10, 11 (từng chữ sáng lên do frontend dùng `ModelSentence` + audio) |
| Tên nhân vật và chữ cái: khuyết 1 ô bất kỳ, chọn 1/3, đọc âm, chấm mềm, vẽ theo nét mờ, "đi theo mũi tên nhé", bước đọc âm/vẽ chỉ ghi đã thử/bỏ qua; chữ ghép một ô; không dấu thanh | 2, 4, 7, 8, 9, 10 |
| Đoán tiếp bằng tranh: 2 tranh, không chấm, lưu lựa chọn cho phụ huynh | 4, 7, 8, 9, 11 |
| Chạm vào nhân vật nói câu đó: 2–3 nhân vật, giọng đọc câu thoại, đúng → nhảy mừng; sai → lắc đầu, chọn lại 1 lần, rồi nói đáp án | 4, 5, 7, 8, 9 |
| Không trùng giữa các tuổi | 1, 3, 8 |
| Nút "Bỏ qua" cho mic/vẽ | 9 (`skipped`), 10 (không gửi = không bản ghi) |
| Không hiện điểm / chữ "sai"; lưu cho phụ huynh | 4 (`GRADING_WORD`), 9, 10, 11 |
| Người duyệt xác nhận trước khi tới bé | 6, 11 |
