# Tương tác lúc nghe cho bé 7 tuổi — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bé 7 tuổi nghe truyện sẽ gặp đúng 5 tương tác riêng của tuổi 7 (Tìm chi tiết trong tranh, Phân biệt đúng sai, Làm theo hành động nhân vật, Sắp xếp 3 thẻ tranh, Ghép từ thành câu và đọc to), không dùng chung với tuổi 6 hay tuổi khác.

**Architecture:** Dùng lại khung `TargetAge` đã làm cho tuổi 6: thêm `7` vào `ListeningAgeSets.Built`, planner có nhánh tuổi 7, luật riêng tuổi 7, prompt AI riêng, API bé lọc theo tuổi. Tuổi 7 cần bốn loại mới (`PictureSearch`, `TrueFalse`, `FollowAction`, `SentenceBuilder`) và dùng lại `EventOrdering` (đặt giữa truyện, lộ đáp án sau 2 lần sai). Vị trí đồ vật trong tranh (Tìm chi tiết) do mô hình thị giác xác định rồi người duyệt kiểm; câu bé ghép được đọc lại bằng âm thanh sinh sẵn, không sinh giọng lúc chạy.

**Tech Stack:** .NET 10, EF Core (PostgreSQL), xUnit + Moq, dịch vụ AI riêng ở `src/AI`, Gemini (vision) cho việc định vị đồ vật, TTS và ASR có sẵn (`ReadAloudMatcher`, `ISpeechRecognizer`).

**Spec:** `C:\Users\Admin\Downloads\Tương tác truyện cho trẻ 6–10 tuổi – Tớ Kể Chuyện.docx`, mục "7 tuổi" và "Nguyên tắc chung". Nền tảng: `docs/superpowers/plans/2026-11-28-age-6-listening-interactions.md`.

## Global Constraints

- Mỗi tuổi có 5 tương tác, **không trùng nhau giữa các tuổi**; bé chỉ thấy bộ đúng tuổi mình.
- Mỗi tương tác gắn với nội dung truyện, không thêm trò chơi phụ.
- Mọi tương tác dùng mic hoặc vẽ đều có nút "Bỏ qua" (bỏ qua = không gọi API).
- Không hiện điểm hay chữ "sai" cho bé; kết quả lưu cho phụ huynh xem (phụ huynh xem tất cả lựa chọn).
- Chạy trên web, bé thao tác bằng ngón tay hoặc chuột.
- Mọi nội dung cho trẻ do AI soạn nháp và **phải qua người duyệt** trước khi tới trẻ (ràng buộc cứng của dự án).
- Hình trong tương tác do AI mô tả từ chính cảnh truyện rồi hệ thống vẽ; không dùng ảnh ngoài truyện.
- Chọn lại: **1 lần** với Tìm chi tiết, Đúng sai (mỗi câu), Làm theo hành động và Ghép câu; Sắp xếp 3 thẻ: **2 lần sai thì hệ thống xếp mẫu và kể lại**.
- Câu hệ thống cố định (không do AI sinh): "Nhìn kỹ lại nhé.", "Nhìn lại tranh nhé.", "Nghe lạ quá, mình thử lại nhé.", "Mình nghe chưa rõ, mình đọc lại nhé." (đã có), "Bé thử lại nhé.".
- Ghép câu: mỗi câu **3–5 thẻ**; từ ghép như "con thỏ", "chạy nhanh" là một thẻ; hệ thống chấp nhận mọi thứ tự hợp lệ do AI liệt kê và người duyệt xác nhận.
- Không đổi hành vi của bộ tuổi 6; tuổi 8, 9, 10 vẫn chưa có bộ (chưa có tương tác lúc nghe) cho tới plan riêng.

## Quyết định thiết kế (đề xuất, bạn có thể đổi trước khi làm)

| Câu hỏi | Chốt trong plan |
|---|---|
| Tên loại | Thêm `PictureSearch = 9`, `TrueFalse = 10`, `FollowAction = 11`, `SentenceBuilder = 12`. "Sắp xếp 3 thẻ tranh" dùng lại `EventOrdering = 4` với `TargetAge = 7`. |
| Vị trí đồ vật trong tranh | Tranh là **tranh minh họa của chính đoạn truyện** (đã có). Sau khi tranh sẵn sàng, mô hình thị giác trả khung bao đồ vật (tọa độ 0–1) lưu ở `HotspotJson`; không thấy đồ vật thì tương tác **không hiện** (readiness báo `hotspot`) và người duyệt đổi tên đồ vật hoặc sinh lại tranh. Máy chủ chấm điểm chạm, khung chỉ lộ cho bé khi cần nháy đáp án. |
| Đọc lại câu bé ghép | Không sinh giọng lúc chạy. Mỗi thẻ có audio sẵn; câu ghép **đúng** có audio sinh sẵn cho từng thứ tự được chấp nhận; câu **chưa đúng** thì màn hình ghép audio các thẻ theo thứ tự bé xếp rồi phát "Nghe lạ quá…". |
| Đoán đúng sai | Một tương tác chứa **2 câu** (1 đúng, 1 sai); bé trả lời lần lượt từng câu, mỗi câu tối đa 2 lần chọn. |
| Làm theo hành động | Hoạt hình nhân vật do frontend; backend cho dữ liệu: vật kéo (1 đúng + 1–2 nhiễu) và chỗ thả (1), mỗi thứ có tranh riêng do AI mô tả. |
| Sinh | Truyện nhóm 6-8 sinh **hai bộ** (tuổi 6 và tuổi 7). Mỗi bộ độc lập, neo độc lập. Tuổi 8 vẫn chưa có. |
| Hằng số tuổi đã xây | Chuyển sang `Contracts` để Core và dịch vụ AI dùng chung một nguồn (`ListeningAgeCatalog.BuiltAges`). |
| Code cũ không còn dùng (FillBlank, ReadAloud, Checkpoint Prediction/Emotion/Opinion) | Giữ nguyên, không dọn trong plan này. |

## File Structure

- `src/Shared/StoryPlatform.Contracts/AI/Models/ListeningAgeCatalog.cs` — **mới**: danh sách tuổi đã xây dùng chung.
- `src/Core/StoryPlatform.Domain/Enums/ListeningInteractionType.cs`, `Enums/ListeningOptionRole.cs` (**mới**) — loại mới và vai trò thẻ.
- `src/Core/StoryPlatform.Domain/Entities/ListeningInteraction.cs`, `ListeningInteractionOption.cs`, `ListeningInteractionAttempt.cs` — cột mới.
- `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddAge7ListeningInteractions.cs` — migration (EF sinh).
- `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningAgeSets.cs` — thêm 7.
- `.../Services/HotspotBox.cs` (**mới**), `.../Services/SentenceOrders.cs` (**mới**) — hàm thuần: hộp chạm, thứ tự câu hợp lệ.
- `.../Services/ListeningInteractionPlanner.cs`, `ListeningInteractionRules.cs`, `ListeningInteractionRules.Age7.cs` (**mới**), `ListeningInteractionAiMapper.cs`, `ListeningInteractionMapper.cs`, `ListeningInteractionGenerator.cs`.
- `src/Shared/StoryPlatform.Contracts/AI/Models/StoryContentModels.cs`, `Requests/GenerationRequests.cs`, `Responses/GenerationResponses.cs` — DTO AI mới.
- `src/AI/StoryPlatform.AI.Application/ContentGeneration/GenerateListeningInteractionHandler.cs`, `src/AI/StoryPlatform.AI.Domain/Enums/GenerationEnums.cs`, `src/AI/StoryPlatform.AI.Infrastructure/PromptCatalog/PromptTemplateProvider.cs` — prompt và schema.
- `src/Core/StoryPlatform.Application/Features/MediaGeneration/Interfaces/IMediaGenerationServices.cs` (`IPictureObjectLocator`), `src/Core/StoryPlatform.Infrastructure/AI/GeminiPictureObjectLocator.cs` (**mới**), `MediaGenerationService.ListeningMedia.cs`, `MediaReadinessService.cs`, `DependencyInjection.cs`.
- `.../Services/ListeningAudioParts.cs`, `ListeningMediaRequirements.cs`.
- `src/Core/StoryPlatform.Application/Features/ReadingSessions/ChildListeningContent.cs`, `DTOs/ChildReadingDtos.cs`, `DTOs/ChildListeningDtos.cs`, `Services/ChildListeningService.cs`, `Services/ChildReadAloudService.cs`, `Services/ParentReadingFeedbackService.cs`.
- `src/Core/StoryPlatform.Application/Features/StoryReview/Services/StoryReviewService.Listening.cs`, `DTOs/ReviewDtos.cs`.
- `Database/Seed/60_listening_interactions.sql`, `61_listening_interaction_options.sql`.
- `docs/api/age-7-interactions-api-test-guide.html` — **mới** (Task 13, cần bạn duyệt; thư mục bị `.gitignore` chặn).

---

### Task 1: Tuổi 7 vào danh sách đã xây và gỡ các chỗ cứng "tuổi 6"

**Việc này làm gì (nói dễ hiểu):**
Hệ thống hiện chỉ biết tuổi 6 có bộ riêng và nhiều chỗ trong code ghi cứng số 6. Task này cho phép thêm tuổi mới mà không phải sửa lại từng chỗ: có một danh sách tuổi đã xây dùng chung cho cả hệ thống chính và dịch vụ AI, và các điều kiện kiểu "nếu là tuổi 6" đổi thành "nếu tuổi này có bộ riêng". Chưa thêm tương tác tuổi 7 nào ở bước này.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningAgeSets.Built`, `TargetAgesFor`, `AgeFor`; các nhánh `TargetAge == 6` ở `ListeningAudioParts`, `ChildListeningContent`, `ChildListeningService`, `ChildReadAloudService`, `ListeningInteractionRules`, `InteractionSceneBoundaryEnforcer`, `GenerateListeningInteractionHandler`.
- Cần thêm: `ListeningAgeCatalog.BuiltAges` (Contracts); `ListeningAgeSets.Built` đọc từ đó; hàm `ListeningAgeSets.HasOwnSet(int? age)`; AI handler bỏ điều kiện `age != 6` thay bằng danh sách theo từng loại.
- Còn thiếu / ngoài phạm vi: hành vi riêng của từng loại tuổi 7 (các Task sau). Từng chỗ `== 6` gắn với hành vi riêng của tuổi 6 (Literal, RolePlay có chấm mềm, CharacterLetter) **giữ nguyên** nhưng đổi điều kiện tuổi thành `HasOwnSet` chỉ khi hành vi đó dùng chung cho mọi tuổi có bộ (ví dụ câu hướng dẫn bắt buộc).

**Files:**
- Create: `src/Shared/StoryPlatform.Contracts/AI/Models/ListeningAgeCatalog.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningAgeSets.cs`
- Modify: `.../Services/ListeningAudioParts.cs` (dòng `required: interaction.TargetAge == 6` của `Instruction`)
- Modify: `src/AI/StoryPlatform.AI.Application/ContentGeneration/GenerateListeningInteractionHandler.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningAgeSetsTests.cs`, `ListeningAudioPartsTests.cs`, `tests/StoryPlatform.AI.UnitTests/…` (test handler hiện có)

**Interfaces:**
- Produces: `ListeningAgeCatalog.BuiltAges : IReadOnlyList<int>` (= `[6]` trong task này, thêm 7 ở Task 3 khi planner đã sẵn sàng); `ListeningAgeSets.HasOwnSet(int? age) : bool`.

- [ ] **Step 1: Viết test hỏng**

```csharp
// ListeningAgeSetsTests
[Fact]
public void HasOwnSet_ReadsSharedCatalog()
{
    foreach (var age in ListeningAgeCatalog.BuiltAges) Assert.True(ListeningAgeSets.HasOwnSet(age));
    Assert.False(ListeningAgeSets.HasOwnSet(null));
    Assert.False(ListeningAgeSets.HasOwnSet(10));
}

[Fact]
public void Built_IsTheSharedCatalog() =>
    Assert.Equal(ListeningAgeCatalog.BuiltAges, ListeningAgeSets.Built);
```
Trong test handler AI: yêu cầu có `TargetAge` thuộc `ListeningAgeCatalog.BuiltAges` và nhóm tuổi `6-8` được chấp nhận; `TargetAge = 10` bị từ chối "Unsupported target age or interaction kind.".

- [ ] **Step 2: Chạy test, phải hỏng**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningAgeSetsTests`
Expected: FAIL — `ListeningAgeCatalog` chưa tồn tại.

- [ ] **Step 3: Triển khai**

```csharp
namespace StoryPlatform.Contracts.AI.Models;

/// <summary>Các tuổi đã có bộ tương tác lúc nghe riêng; Core và dịch vụ AI dùng chung một nguồn.</summary>
public static class ListeningAgeCatalog
{
    public static IReadOnlyList<int> BuiltAges { get; } = [6];
}
```
`ListeningAgeSets.Built => ListeningAgeCatalog.BuiltAges;` và
```csharp
public static bool HasOwnSet(int? age) => age is int a && ListeningAgeCatalog.BuiltAges.Contains(a);
```
`ListeningAudioParts`: `required: ListeningAgeSets.HasOwnSet(interaction.TargetAge)`.
AI handler: điều kiện tuổi thành `!ListeningAgeCatalog.BuiltAges.Contains(age) || !(request.AgeBand is "6-8" or "Age_6_8")`; kiểm "loại hợp lệ theo tuổi" tách thành bảng `AllowedTypes[age]` (tuổi 6: `Checkpoint, RolePlay, CharacterLetter, PicturePrediction, WhoSaidIt`).

- [ ] **Step 4: Chạy test, phải qua**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~Listening` và `dotnet test tests/StoryPlatform.AI.UnitTests`
Expected: PASS (hành vi tuổi 6 không đổi).

---

### Task 2: Mô hình dữ liệu cho bốn loại mới

**Việc này làm gì (nói dễ hiểu):**
Bốn tương tác mới cần chỗ lưu những thứ chưa có: tên đồ vật cần tìm và vị trí của nó trong tranh, vai trò của từng thẻ (vật kéo hay chỗ thả), danh sách các cách xếp câu hợp lệ. Task này thêm các cột đó. Không xóa hay đổi dữ liệu cũ.

**Cần thêm / còn thiếu:**
- Đã có: bảng `listening_interactions`, `listening_interaction_options`, `listening_interaction_attempts`; `TargetAge`; `OrderedOptionIdsJson`, `AnswerText` của attempt.
- Cần thêm: enum `PictureSearch/TrueFalse/FollowAction/SentenceBuilder`; enum `ListeningOptionRole { None = 0, Item = 1, Target = 2 }`; cột `ListeningInteraction.TargetLabel`, `HotspotJson`, `AcceptedOrdersJson`; `ListeningInteractionOption.Role`, `HotspotJson`.
- Còn thiếu / ngoài phạm vi: logic dùng các cột (các Task sau). Vị trí chạm của bé lưu trong `AnswerText` (JSON `{"x":0.41,"y":0.62}`), không thêm cột.

**Files:**
- Modify: `src/Core/StoryPlatform.Domain/Enums/ListeningInteractionType.cs`
- Create: `src/Core/StoryPlatform.Domain/Enums/ListeningOptionRole.cs`
- Modify: `src/Core/StoryPlatform.Domain/Entities/ListeningInteraction.cs`, `ListeningInteractionOption.cs`
- Create (EF sinh): `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddAge7ListeningInteractions.cs` và `.Designer.cs`
- Test: `tests/StoryPlatform.UnitTests/Age7ListeningMigrationTests.cs` (theo mẫu `Age6ListeningMigrationTests.cs`)

**Interfaces:**
- Produces:
  - `ListeningInteractionType.PictureSearch = 9, TrueFalse = 10, FollowAction = 11, SentenceBuilder = 12`
  - `enum ListeningOptionRole { None = 0, Item = 1, Target = 2 }`
  - `ListeningInteraction.TargetLabel : string?` (tên đồ vật cần tìm, ví dụ "cái nón đỏ"), `.HotspotJson : string?` (`{"assetId":int,"x":d,"y":d,"w":d,"h":d}` tọa độ 0–1), `.AcceptedOrdersJson : string?` (`[[1,2,3,4],[1,2,4,3]]` theo `SortOrder` 1-based của thẻ)
  - `ListeningInteractionOption.Role : ListeningOptionRole`, `.HotspotJson : string?` (bằng chứng trong tranh của câu đúng/sai, tùy chọn)

- [ ] **Step 1: Viết test hỏng** — migration test: migration `AddAge7ListeningInteractions` chỉ có `AddColumn` (không `DropColumn`/`DropTable`/`DropIndex`), 5 cột nullable hoặc có mặc định 0 cho `Role`.

```csharp
[Fact]
public void Migration_OnlyAddsColumns()
{
    var up = File.ReadAllText(MigrationPath("AddAge7ListeningInteractions"));
    Assert.DoesNotContain("DropColumn", up[..up.IndexOf("protected override void Down", StringComparison.Ordinal)]);
    Assert.DoesNotContain("DropTable", up);
    Assert.Contains("TargetLabel", up);
    Assert.Contains("AcceptedOrdersJson", up);
}
```
(`MigrationPath` lấy cách tìm file từ `Age6ListeningMigrationTests`.)

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~Age7ListeningMigrationTests`; Expected: FAIL (chưa có migration).

- [ ] **Step 3: Thêm enum, cột; cấu hình EF** — `Role` lưu `int` mặc định 0 (`HasDefaultValue(ListeningOptionRole.None)`), các cột chuỗi `text`.

- [ ] **Step 4: Tạo migration** — `dotnet ef migrations add AddAge7ListeningInteractions --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api`; kiểm file chỉ có `AddColumn`.

- [ ] **Step 5: Build và test** — `dotnet build StoryPlatform.sln` rồi `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~Age7ListeningMigrationTests`; Expected: PASS.

---

### Task 3: Hàm thuần — hộp chạm và thứ tự câu hợp lệ

**Việc này làm gì (nói dễ hiểu):**
Hai phép tính nền tảng của tuổi 7 được tách riêng cho dễ kiểm: (1) chạm của bé có nằm trong khung bao đồ vật không (cho phép chạm lệch một chút cho ngón tay trẻ nhỏ); (2) thứ tự thẻ bé xếp có thuộc một trong các cách xếp câu hợp lệ không, và danh sách cách xếp do AI đưa có hợp lý không (cùng bộ thẻ, không trùng, tối đa 6 cách).

**Cần thêm / còn thiếu:**
- Đã có: không có.
- Cần thêm: `HotspotBox.TryParse`, `HotspotBox.Hits`, `HotspotBox.IsValid`; `SentenceOrders.Parse`, `SentenceOrders.IsValid`, `SentenceOrders.Matches`, `SentenceOrders.Canonical`.
- Còn thiếu / ngoài phạm vi: chọn giá trị đệm chạm (đề xuất 0,04 mỗi cạnh, tức 4% kích thước tranh) có thể chỉnh sau khi thử với bé thật.

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/HotspotBox.cs`
- Create: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/SentenceOrders.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/HotspotBoxTests.cs`, `SentenceOrdersTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record HotspotBox(int AssetId, double X, double Y, double W, double H)
{
    public const double TouchPadding = 0.04;
    public static HotspotBox? TryParse(string? json);
    public static bool IsValid(HotspotBox? box);              // 0 ≤ X,Y; W,H > 0; X+W ≤ 1, Y+H ≤ 1; diện tích ≥ 0,2%
    public bool Hits(double x, double y);                     // trong khung cộng đệm
    public string ToJson();
}
public static class SentenceOrders
{
    public static IReadOnlyList<IReadOnlyList<int>>? Parse(string? json);   // null nếu hỏng
    public static bool IsValid(IReadOnlyList<IReadOnlyList<int>> orders, int cardCount); // 1–6 cách, mỗi cách là hoán vị 1..n, không trùng
    public static bool Matches(IReadOnlyList<IReadOnlyList<int>> orders, IReadOnlyList<int> sortOrdersChosen);
    public static IReadOnlyList<int> Canonical(IReadOnlyList<IReadOnlyList<int>> orders) => orders[0];
}
```

- [ ] **Step 1: Viết test hỏng**

```csharp
public sealed class HotspotBoxTests
{
    private static readonly HotspotBox Box = new(7, 0.40, 0.50, 0.20, 0.20);

    [Theory]
    [InlineData(0.50, 0.60, true)]    // giữa khung
    [InlineData(0.37, 0.48, true)]    // lệch trong đệm 0,04
    [InlineData(0.30, 0.60, false)]   // xa khung
    [InlineData(0.50, 0.80, false)]
    public void Hits_UsesPaddedBox(double x, double y, bool expected) => Assert.Equal(expected, Box.Hits(x, y));

    [Fact] public void Hits_RejectsPointsOutsideTheImage() => Assert.False(Box.Hits(-0.1, 0.6));
    [Fact] public void TryParse_RoundTrips() => Assert.Equal(Box, HotspotBox.TryParse(Box.ToJson()));
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("not json")] [InlineData("{\"x\":2}")]
    public void TryParse_BadInput_ReturnsNull(string? json) => Assert.Null(HotspotBox.TryParse(json));
    [Theory]
    [InlineData(0.9, 0.5, 0.3, 0.2, false)]   // vượt mép phải
    [InlineData(0.1, 0.1, 0.01, 0.01, false)] // quá nhỏ
    [InlineData(0.1, 0.1, 0.2, 0.2, true)]
    public void IsValid_ChecksBounds(double x, double y, double w, double h, bool expected) =>
        Assert.Equal(expected, HotspotBox.IsValid(new HotspotBox(1, x, y, w, h)));
}

public sealed class SentenceOrdersTests
{
    [Fact] public void Parse_ReadsArrays() => Assert.Equal(2, SentenceOrders.Parse("[[1,2,3],[2,1,3]]")!.Count);
    [Fact] public void Parse_Bad_ReturnsNull() => Assert.Null(SentenceOrders.Parse("[1,2,3]"));

    [Fact] public void IsValid_AcceptsPermutations() => Assert.True(SentenceOrders.IsValid([[1, 2, 3], [2, 1, 3]], 3));
    [Fact] public void IsValid_RejectsDuplicates() => Assert.False(SentenceOrders.IsValid([[1, 2, 3], [1, 2, 3]], 3));
    [Fact] public void IsValid_RejectsNonPermutation() => Assert.False(SentenceOrders.IsValid([[1, 2, 2]], 3));
    [Fact] public void IsValid_RejectsTooManyOrders() =>
        Assert.False(SentenceOrders.IsValid(Enumerable.Range(0, 7).Select(i => (IReadOnlyList<int>)new[] { 1 + i % 3, 1 + (i + 1) % 3, 1 + (i + 2) % 3 }).ToArray(), 3));
    [Fact] public void IsValid_RejectsEmptyAndWrongCardCount() { Assert.False(SentenceOrders.IsValid([], 3)); Assert.False(SentenceOrders.IsValid([[1, 2, 3]], 4)); }

    [Fact] public void Matches_AnyAcceptedOrder() { Assert.True(SentenceOrders.Matches([[1, 2, 3], [2, 1, 3]], [2, 1, 3])); Assert.False(SentenceOrders.Matches([[1, 2, 3]], [3, 2, 1])); }
}
```

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~HotspotBoxTests|FullyQualifiedName~SentenceOrdersTests"`; Expected: FAIL (chưa có lớp).

- [ ] **Step 3: Triển khai**

```csharp
using System.Text.Json;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public sealed record HotspotBox(int AssetId, double X, double Y, double W, double H)
{
    public const double TouchPadding = 0.04;
    private const double MinArea = 0.002;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static HotspotBox? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("assetId", out var a) || !r.TryGetProperty("x", out var x) || !r.TryGetProperty("y", out var y) ||
                !r.TryGetProperty("w", out var w) || !r.TryGetProperty("h", out var h)) return null;
            var box = new HotspotBox(a.GetInt32(), x.GetDouble(), y.GetDouble(), w.GetDouble(), h.GetDouble());
            return IsValid(box) ? box : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    public static bool IsValid(HotspotBox? b) =>
        b is not null && b.X >= 0 && b.Y >= 0 && b.W > 0 && b.H > 0 && b.X + b.W <= 1.0000001 && b.Y + b.H <= 1.0000001 && b.W * b.H >= MinArea;

    public bool Hits(double x, double y) =>
        x is >= 0 and <= 1 && y is >= 0 and <= 1 &&
        x >= X - TouchPadding && x <= X + W + TouchPadding && y >= Y - TouchPadding && y <= Y + H + TouchPadding;

    public string ToJson() => JsonSerializer.Serialize(new { assetId = AssetId, x = X, y = Y, w = W, h = H }, Json);
}

public static class SentenceOrders
{
    public const int MaxOrders = 6;

    public static IReadOnlyList<IReadOnlyList<int>>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<int[][]>(json)?.Select(x => (IReadOnlyList<int>)x).ToArray(); }
        catch (JsonException) { return null; }
    }

    public static bool IsValid(IReadOnlyList<IReadOnlyList<int>> orders, int cardCount) =>
        cardCount is >= 3 and <= 5 && orders.Count is >= 1 and <= MaxOrders &&
        orders.All(o => o.Count == cardCount && o.OrderBy(x => x).SequenceEqual(Enumerable.Range(1, cardCount))) &&
        orders.Select(o => string.Join(',', o)).Distinct().Count() == orders.Count;

    public static bool Matches(IReadOnlyList<IReadOnlyList<int>> orders, IReadOnlyList<int> chosen) =>
        orders.Any(o => o.SequenceEqual(chosen));

    public static IReadOnlyList<int> Canonical(IReadOnlyList<IReadOnlyList<int>> orders) => orders[0];
}
```
(Test "Parse_Bad_ReturnsNull": `JsonSerializer.Deserialize<int[][]>("[1,2,3]")` ném `JsonException` → trả null ✓.)

- [ ] **Step 4: Chạy test, phải qua** — cùng lệnh Step 2; Expected: PASS.

---

### Task 4: Planner bộ tuổi 7

**Việc này làm gì (nói dễ hiểu):**
Khi truyện đủ dài, bé 7 tuổi gặp mỗi tương tác trong 5 loại đúng một lần, rải đều trong truyện. Truyện ngắn thì ưu tiên theo thứ tự: Tìm chi tiết → Đúng sai → Ghép câu → Làm theo hành động → Sắp xếp 3 thẻ tranh. Sắp xếp 3 thẻ đặt ở chỗ muộn nhất vì bé phải đã nghe đủ 3 sự kiện; ghép câu ưu tiên đoạn có câu ngắn.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionPlanner.Create(int targetAge, blocks)` (tuổi 6), `AssignZones`, `HasShortSentence`.
- Cần thêm: nhánh `7 => CreateAge7`; `ListeningPlanItem.Kind` không dùng; hàm `TakeLatest` trong `AssignZones`; `ListeningAgeCatalog.BuiltAges` thêm `7`; `IsEndOfStory(type, targetAge)` (EventOrdering tuổi 7 không còn ở cuối truyện).
- Còn thiếu / ngoài phạm vi: truyện có dưới 2 đoạn không có tương tác (như tuổi 6). Đoạn có câu thích hợp để ghép (3–5 thẻ) do AI chọn; nếu không có thì job báo `SENTENCEBUILDER_VALIDATION_FAILED` như các loại khác.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionPlanner.cs`
- Modify: `.../Services/ListeningInteractionRules.cs` (`IsEndOfStory`, `LastBlockRuleHolds`, `ValidateAnchor`)
- Modify: `src/Shared/StoryPlatform.Contracts/AI/Models/ListeningAgeCatalog.cs` (thêm 7)
- Test: `ListeningInteractionPlannerTests.cs`, `ListeningAgeSetsTests.cs`, `ListeningInteractionRulesAge7Tests.cs`

**Interfaces:**
- Consumes: `ListeningInteractionType.*` mới (Task 2).
- Produces: `ListeningInteractionRules.IsEndOfStory(ListeningInteractionType type, int? targetAge = null) : bool` (EventOrdering và ReadAloud là cuối truyện, **trừ** EventOrdering khi `targetAge == 7`); `ListeningInteractionPlanner.Create(7, blocks)`.

- [ ] **Step 1: Viết test hỏng**

```csharp
private static string DescribeAge7(int blocks, bool dialogue = true) =>
    string.Join(",", ListeningInteractionPlanner.Create(7, Story(blocks, dialogue)).Items.Select(x => $"{x.Type}x{x.Count}"));

[Theory]
[InlineData(1, "")]
[InlineData(2, "PictureSearchx1")]
[InlineData(3, "PictureSearchx1,TrueFalsex1")]
[InlineData(4, "PictureSearchx1,TrueFalsex1,SentenceBuilderx1")]
[InlineData(5, "PictureSearchx1,TrueFalsex1,SentenceBuilderx1,FollowActionx1")]
[InlineData(6, "PictureSearchx1,TrueFalsex1,SentenceBuilderx1,FollowActionx1,EventOrderingx1")]
[InlineData(12, "PictureSearchx1,TrueFalsex1,SentenceBuilderx1,FollowActionx1,EventOrderingx1")]
public void Age7_FiveDistinctTypes(int blocks, string expected) => Assert.Equal(expected, DescribeAge7(blocks));

[Fact]
public void Age7_EventOrderingTakesLatestZone_AfterThreeBlocks()
{
    var plan = ListeningInteractionPlanner.Create(7, Story(6));
    var zone = plan.Items.Single(x => x.Type == T.EventOrdering).Zones.Single().Single();
    Assert.Equal("P5", zone);                       // 5 chỗ trống, vùng muộn nhất; trước đó đã có P1..P5 để chứa 3 sự kiện
}

[Fact]
public void Age7_HasNoReadAloud_AndEventOrderingIsMidStory()
{
    var plan = ListeningInteractionPlanner.Create(7, Story(9));
    Assert.DoesNotContain(plan.Items, x => x.Type == T.ReadAloud);
    Assert.False(ListeningInteractionRules.IsEndOfStory(T.EventOrdering, 7));
    Assert.True(ListeningInteractionRules.IsEndOfStory(T.EventOrdering, null));
}

[Fact] public void Age7_PlanCarriesTargetAge() => Assert.Equal(7, ListeningInteractionPlanner.Create(7, Story(6)).TargetAge);

[Fact]
public void Age6_PlanIsUnchanged() =>
    Assert.Equal("Checkpoint:Literalx1,RolePlayx1,CharacterLetterx1,PicturePredictionx1,WhoSaidItx1", DescribeAge6(6));

[Fact]
public void BuiltAges_NowIncludeSeven() =>
    Assert.Equal([6, 7], ListeningAgeSets.TargetAgesFor(AgeBand.Age_6_8));
```
Sửa test tuổi 6 hiện có `Band6To8_GeneratesOnlyBuiltAges` (kỳ vọng `[6, 7]`).

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ListeningInteractionPlannerTests|FullyQualifiedName~ListeningAgeSetsTests"`; Expected: FAIL.

- [ ] **Step 3: Triển khai**

```csharp
public static ListeningInteractionPlan Create(int targetAge, IReadOnlyList<StoryTextBlock> blocks) => targetAge switch
{
    6 => CreateAge6(blocks) with { TargetAge = 6 },
    7 => CreateAge7(blocks) with { TargetAge = 7 },
    _ => ListeningInteractionPlan.Empty with { TargetAge = targetAge }
};

/// <summary>Bộ tuổi 7: thứ tự ưu tiên PictureSearch, TrueFalse, SentenceBuilder, FollowAction, EventOrdering; không có mục sau đoạn cuối.</summary>
private static ListeningInteractionPlan CreateAge7(IReadOnlyList<StoryTextBlock> blocks)
{
    var slots = Math.Max(0, blocks.Count - 1);
    if (slots == 0) return ListeningInteractionPlan.Empty;
    var order = new[] { ListeningInteractionType.PictureSearch, ListeningInteractionType.TrueFalse,
        ListeningInteractionType.SentenceBuilder, ListeningInteractionType.FollowAction, ListeningInteractionType.EventOrdering };
    // EventOrdering cần 3 sự kiện ở 3 đoạn khác nhau trước chỗ dừng: chỉ khi có đủ chỗ trống như bảng ưu tiên.
    var items = order.Take(slots).Select(t => new ListeningPlanItem(t, 1)).ToArray();
    var dialogue = FindDialogueBlockIds(blocks);
    return new(slots, AssignZones(items, blocks, dialogue), dialogue);
}
```
Trong `AssignZones` thêm (đặt sau các khối `RolePlay/WhoSaidIt/FillBlank`, trước `Checkpoint or CharacterLetter`):
```csharp
IReadOnlyList<string> TakeLatest() => Claim(free[^1]);
foreach (var item in items.Where(x => x.Type == ListeningInteractionType.SentenceBuilder))
    assigned[item.Type] = [TakeFitting(HasShortSentence)];
foreach (var item in items.Where(x => x.Type == ListeningInteractionType.EventOrdering))
    assigned[item.Type] = [TakeLatest()];
foreach (var item in items.Where(x => x.Type is ListeningInteractionType.PictureSearch or ListeningInteractionType.TrueFalse or ListeningInteractionType.FollowAction))
    assigned[item.Type] = [TakeEarliest()];
```
`middle` (số mục giữa truyện) phải tính cả EventOrdering tuổi 7: sửa `items.Where(x => !ListeningInteractionRules.IsEndOfStory(x.Type, targetAge))` — thêm tham số `targetAge` vào `AssignZones` (tuổi 6 truyền 6).
`IsEndOfStory`:
```csharp
public static bool IsEndOfStory(ListeningInteractionType type, int? targetAge = null) =>
    type is ListeningInteractionType.ReadAloud ||
    type == ListeningInteractionType.EventOrdering && targetAge != 7;
```
`LastBlockRuleHolds(item, anchor, blocks)` và `ValidateAnchor` truyền `context.TargetAge`/`item.TargetAge`. Cập nhật `ListeningAgeCatalog.BuiltAges` thành `[6, 7]`.
Bảng "10 đoạn có 9 chỗ": các loại ít chỗ hơn số tương tác thì `Take(slots)` đã đủ; chỗ trống dư không thêm Checkpoint như bộ cũ.

- [ ] **Step 4: Chạy test, phải qua (gồm các test tuổi 6)** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractions`; Expected: PASS.

---

### Task 5: Luật kiểm tra bộ tuổi 7

**Việc này làm gì (nói dễ hiểu):**
AI soạn nháp có thể sai (đồ vật không có trong đoạn truyện, hai câu đúng sai cùng đúng, thiếu chỗ thả, cách xếp câu không hợp lệ, sự kiện sắp xếp nằm sau chỗ dừng). Luật này chặn bản nháp sai trước khi tới người duyệt và chạy lại khi người duyệt sửa tay. Mỗi loại có luật riêng theo đúng spec.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionRules.Validate/ValidateText/ValidateFields/RequiredFeedback`, `ValidateOrdering` (3 sự kiện, mỗi sự kiện một đoạn, thứ tự tăng), partial `ListeningInteractionRules.Age6.cs`.
- Cần thêm (partial `ListeningInteractionRules.Age7.cs`):
  - `PictureSearch`: `TargetLabel` có trong truyện trước neo (`NOT_GROUNDED`), không rỗng, ≤ 6 từ; có `Prompt` ("Tìm …") nhắc lại đúng `TargetLabel` (`SCHEMA_INVALID`); không có thẻ đáp án; `SourceQuote` chứa đồ vật trong đoạn neo.
  - `TrueFalse`: đúng 2 thẻ (câu), đúng 1 thẻ `IsMatch` (câu đúng), mỗi câu ≤ 12 từ và khác nhau; cả hai câu bám truyện trước neo (`NOT_GROUNDED` theo từ nội dung); `MatchFeedback`/`NoMatchFeedback` bắt buộc; câu sai không được là phủ định đơn thuần của câu đúng chỉ thêm "không" (mã `TRUEFALSE_TRIVIAL`).
  - `FollowAction`: `Prompt` ("Bé giúp … nhé: …"); 2–4 thẻ: đúng 1 thẻ `Role = Item` và `IsMatch`, 0–2 thẻ `Item` nhiễu, đúng 1 thẻ `Role = Target`; mỗi thẻ có `ImageDescription`; `MatchFeedback` bắt buộc; `CharacterName` có trong truyện.
  - `SentenceBuilder`: 3–5 thẻ (`CorrectOrder` 1..n là thứ tự chuẩn); `SourceQuote` là câu nguyên văn trước neo, ≤ 8 từ hiển thị; `AcceptedOrdersJson` hợp lệ (`SentenceOrders.IsValid`), thứ tự đầu tiên chính là thứ tự trong truyện; `MatchFeedback` và `NoMatchFeedback` bắt buộc; mã lỗi mới `SENTENCE_ORDERS_INVALID`.
  - `EventOrdering` tuổi 7: mỗi sự kiện có `SourceEnd` ≤ neo (`EVENT_AFTER_ANCHOR`); neo không phải đoạn cuối.
  - Mã lỗi mới còn lại: `TARGET_NOT_FOUND`.
- Còn thiếu / ngoài phạm vi: kiểm "đồ vật có trong tranh" (Task 7); kiểm "câu sai là sai thật" bằng ngữ nghĩa không tự động được, người duyệt xác nhận.

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionRules.Age7.cs`
- Modify: `.../Services/ListeningInteractionRules.cs` (`Validate`, `ValidateFields`, `RequiredFeedback`, `TextFields`)
- Modify: `.../Models/ListeningInteractionModels.cs` (`ListeningInteractionDraft.TargetLabel`, `AcceptedOrdersJson`; `ListeningOptionDraft.Role`)
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionRulesAge7Tests.cs`

**Interfaces:**
- Consumes: `SentenceOrders`, `ListeningOptionRole`, `ListeningInteractionType.*` (Task 2, 3).
- Produces: `ListeningInteractionDraft.TargetLabel : string?`, `.AcceptedOrdersJson : string?`; `ListeningOptionDraft.Role : ListeningOptionRole`; mã lỗi `TARGET_NOT_FOUND`, `TRUEFALSE_TRIVIAL`, `SENTENCE_ORDERS_INVALID`, `EVENT_AFTER_ANCHOR`.

- [ ] **Step 1: Viết test hỏng** (dùng truyện chung `ListeningTestStory`, P2 có thoại "Có ai ở đó không?", P3 có "bác Gấu", P4 "Bác Gấu dẫn Thỏ về tới cửa hang."; `Ctx` = `S.Context(type, 1) with { TargetAge = 7 }`)

```csharp
public sealed class ListeningInteractionRulesAge7Tests
{
    private static ListeningRuleContext Ctx(T type, int count = 1) => S.Context(type, count) with { TargetAge = 7 };
    private static string[] Codes(ListeningInteractionDraft item, ListeningRuleContext ctx) =>
        ListeningInteractionRules.Validate([S.Resolve(item)], ctx).Select(e => e.Code).Distinct().ToArray();

    private static ListeningInteractionDraft Search(string label = "bác Gấu", string prompt = "Tìm bác Gấu trong tranh") => new()
    {
        Type = T.PictureSearch, AnchorBlockId = "P3", Prompt = prompt, TargetLabel = label,
        SourceQuote = "Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.", NoMatchFeedback = "Nhìn kỹ lại nhé.", MatchFeedback = "Con tìm giỏi quá!"
    };
    [Fact] public void PictureSearch_Valid() => Assert.Empty(Codes(Search(), Ctx(T.PictureSearch)));
    [Fact] public void PictureSearch_LabelNotInStory_Rejected() => Assert.Contains("TARGET_NOT_FOUND", Codes(Search("con rồng lửa"), Ctx(T.PictureSearch)));
    [Fact] public void PictureSearch_PromptMustNameTheTarget() => Assert.Contains("SCHEMA_INVALID", Codes(Search(prompt: "Tìm cái gì đó"), Ctx(T.PictureSearch)));
    [Fact] public void PictureSearch_WithAnswerCards_Rejected() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Search() with { Options = [new ListeningOptionDraft { Text = "x" }] }, Ctx(T.PictureSearch)));

    private static ListeningInteractionDraft TrueFalse(string t = "Bác Gấu đến giúp Thỏ", string f = "Bác Gấu đến để hù dọa Thỏ") => new()
    {
        Type = T.TrueFalse, AnchorBlockId = "P4", MatchFeedback = "Đúng rồi!", NoMatchFeedback = "Mình nghe lại nhé.",
        Options = [new ListeningOptionDraft { Text = t, IsMatch = true, SortOrder = 0 }, new ListeningOptionDraft { Text = f, SortOrder = 1 }]
    };
    [Fact] public void TrueFalse_Valid() => Assert.Empty(Codes(TrueFalse(), Ctx(T.TrueFalse)));
    [Fact] public void TrueFalse_BothTrue_Rejected() =>
        Assert.Contains("MATCH_RULE_INVALID", Codes(TrueFalse() with { Options = [TrueFalse().Options[0], TrueFalse().Options[1] with { IsMatch = true }] }, Ctx(T.TrueFalse)));
    [Fact] public void TrueFalse_OnlyOneStatement_Rejected() =>
        Assert.Contains("OPTION_COUNT_INVALID", Codes(TrueFalse() with { Options = [TrueFalse().Options[0]] }, Ctx(T.TrueFalse)));
    [Fact] public void TrueFalse_FalseStatementIsPlainNegation_Rejected() =>
        Assert.Contains("TRUEFALSE_TRIVIAL", Codes(TrueFalse(f: "Bác Gấu không đến giúp Thỏ"), Ctx(T.TrueFalse)));

    private static ListeningInteractionDraft Action() => new()
    {
        Type = T.FollowAction, AnchorBlockId = "P4", Prompt = "Bé giúp Thỏ nhé: dắt bác Gấu về hang", CharacterName = "Thỏ", MatchFeedback = "Thỏ cảm ơn con!",
        NoMatchFeedback = "Bé thử lại nhé.",
        Options =
        [
            new ListeningOptionDraft { Text = "bác Gấu", IsMatch = true, Role = ListeningOptionRole.Item, ImageDescription = "Bác Gấu nâu to", SortOrder = 0 },
            new ListeningOptionDraft { Text = "bụi cây", Role = ListeningOptionRole.Item, ImageDescription = "Bụi cây xanh", SortOrder = 1 },
            new ListeningOptionDraft { Text = "cửa hang", Role = ListeningOptionRole.Target, ImageDescription = "Cửa hang ấm áp", SortOrder = 2 }
        ]
    };
    [Fact] public void FollowAction_Valid() => Assert.Empty(Codes(Action(), Ctx(T.FollowAction)));
    [Fact] public void FollowAction_NoTarget_Rejected() =>
        Assert.Contains("OPTION_COUNT_INVALID", Codes(Action() with { Options = Action().Options.Take(2).ToArray() }, Ctx(T.FollowAction)));
    [Fact] public void FollowAction_TwoCorrectItems_Rejected() =>
        Assert.Contains("MATCH_RULE_INVALID", Codes(Action() with { Options = [Action().Options[0], Action().Options[1] with { IsMatch = true }, Action().Options[2]] }, Ctx(T.FollowAction)));
    [Fact] public void FollowAction_ItemWithoutPicture_Rejected() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Action() with { Options = [Action().Options[0] with { ImageDescription = null }, Action().Options[1], Action().Options[2]] }, Ctx(T.FollowAction)));

    private static ListeningInteractionDraft Builder(string orders = "[[1,2,3,4]]") => new()
    {
        Type = T.SentenceBuilder, AnchorBlockId = "P4", SourceQuote = "Bác Gấu dẫn Thỏ về tới cửa hang.", AcceptedOrdersJson = orders,
        MatchFeedback = "Con ghép giỏi quá!", NoMatchFeedback = "Nghe lạ quá, mình thử lại nhé.",
        Options =
        [
            new ListeningOptionDraft { Text = "Bác Gấu", CorrectOrder = 1, SortOrder = 0 }, new ListeningOptionDraft { Text = "dẫn Thỏ", CorrectOrder = 2, SortOrder = 1 },
            new ListeningOptionDraft { Text = "về tới", CorrectOrder = 3, SortOrder = 2 }, new ListeningOptionDraft { Text = "cửa hang", CorrectOrder = 4, SortOrder = 3 }
        ]
    };
    [Fact] public void SentenceBuilder_Valid() => Assert.Empty(Codes(Builder(), Ctx(T.SentenceBuilder)));
    [Fact] public void SentenceBuilder_FirstOrderMustBeStoryOrder() => Assert.Contains("SENTENCE_ORDERS_INVALID", Codes(Builder("[[2,1,3,4]]"), Ctx(T.SentenceBuilder)));
    [Fact] public void SentenceBuilder_BadOrders_Rejected() => Assert.Contains("SENTENCE_ORDERS_INVALID", Codes(Builder("[[1,2,3,3]]"), Ctx(T.SentenceBuilder)));
    [Fact] public void SentenceBuilder_TooFewCards_Rejected() =>
        Assert.Contains("OPTION_COUNT_INVALID", Codes(Builder("[[1,2]]") with { Options = Builder().Options.Take(2).ToArray() }, Ctx(T.SentenceBuilder)));
    [Fact] public void SentenceBuilder_CardsMustSpellTheQuote() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Builder() with { Options = [Builder().Options[0], Builder().Options[1], Builder().Options[2], Builder().Options[3] with { Text = "ngôi nhà" }] }, Ctx(T.SentenceBuilder)));
    [Fact] public void SentenceBuilder_NoGradingWords() =>
        Assert.Contains("GRADING_WORD", Codes(Builder() with { NoMatchFeedback = "Con xếp sai rồi" }, Ctx(T.SentenceBuilder)));

    [Fact]
    public void EventOrdering_Age7_EventsMustBeBeforeAnchor()
    {
        var ctx = Ctx(T.EventOrdering) with { Zones = [["P5"]] };
        // dùng bộ 3 sự kiện hợp lệ có sẵn của ListeningTestStory nhưng neo ở P2 (trước sự kiện thứ 3) → EVENT_AFTER_ANCHOR
        Assert.Contains("EVENT_AFTER_ANCHOR", Codes(S.EventOrderingDraft(anchorBlockId: "P2"), ctx));
    }
}
```
(`S.EventOrderingDraft` là helper mới trong `ListeningTestStory`, dựng bộ 3 sự kiện ở P1, P3, P5 với neo tham số; thêm một test hợp lệ cho neo P5 → không lỗi.)

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionRulesAge7Tests`; Expected: FAIL (chưa có `TargetLabel`).

- [ ] **Step 3: Triển khai** — `ListeningInteractionRules.Age7.cs`:

```csharp
public static partial class ListeningInteractionRules
{
    public const int TargetLabelMaxWords = 6;
    public const int StatementMaxWords = 12;
    public const int SentenceMaxWords = 8;

    private static void ValidatePictureSearch(ListeningInteractionDraft item, int index, ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        var label = item.TargetLabel?.Trim();
        if (string.IsNullOrWhiteSpace(label) || CountWords(label) > TargetLabelMaxWords) { errors.Add(new("SCHEMA_INVALID", index, "targetLabel")); return; }
        if (!AppearsBefore(context.Content, label, item.AnchorOffset)) errors.Add(new("TARGET_NOT_FOUND", index, "targetLabel"));
        if (item.Prompt is null || !item.Prompt.Contains(label, StringComparison.OrdinalIgnoreCase)) errors.Add(new("SCHEMA_INVALID", index, "question"));
        if (item.Options.Count > 0) errors.Add(new("SCHEMA_INVALID", index, "options"));
    }

    private static void ValidateTrueFalse(ListeningInteractionDraft item, int index, ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        if (item.Options.Count != 2 || HasDuplicateText(item)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (item.Options.Count(o => o.IsMatch) != 1) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
        {
            var text = item.Options[j].Text;
            if (CountWords(text) > StatementMaxWords) errors.Add(new("LENGTH_LIMIT", index, $"options[{j}].text"));
            if (!LearningArtifactGrounding.HasRelevantTerm(text, context.GroundingSource)) errors.Add(new("NOT_GROUNDED", index, $"options[{j}].text"));
        }
        // Câu sai chỉ là câu đúng thêm "không": bé đoán mò được, không đo hiểu truyện.
        var truth = item.Options.FirstOrDefault(o => o.IsMatch)?.Text;
        var falsehood = item.Options.FirstOrDefault(o => !o.IsMatch)?.Text;
        if (truth is not null && falsehood is not null && IsPlainNegation(truth, falsehood)) errors.Add(new("TRUEFALSE_TRIVIAL", index, "options"));
    }

    private static bool IsPlainNegation(string a, string b)
    {
        static string[] Words(string s) => Syllables(s).Where(w => w is not ("không" or "chẳng" or "chưa")).ToArray();
        return Words(a).SequenceEqual(Words(b)) && !Syllables(a).SequenceEqual(Syllables(b));
    }

    private static void ValidateFollowAction(ListeningInteractionDraft item, int index, ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        var items = item.Options.Where(o => o.Role == ListeningOptionRole.Item).ToArray();
        var targets = item.Options.Where(o => o.Role == ListeningOptionRole.Target).ToArray();
        if (targets.Length != 1 || items.Length is < 1 or > 3 || item.Options.Any(o => o.Role == ListeningOptionRole.None) || HasDuplicateText(item))
            errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
        if (items.Count(o => o.IsMatch) != 1 || targets.Any(o => o.IsMatch)) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
        for (var j = 0; j < item.Options.Count; j++)
            if (string.IsNullOrWhiteSpace(item.Options[j].ImageDescription)) errors.Add(new("SCHEMA_INVALID", index, $"options[{j}].imageDescription"));
        if (string.IsNullOrWhiteSpace(item.CharacterName) || !AppearsBefore(context.Content, item.CharacterName, item.AnchorOffset))
            errors.Add(new("NOT_GROUNDED", index, "characterName"));
    }

    private static void ValidateSentenceBuilder(ListeningInteractionDraft item, int index, ListeningRuleContext context, List<ListeningValidationError> errors)
    {
        var cards = item.Options.OrderBy(o => o.CorrectOrder).ToArray();
        if (cards.Length is < 3 or > 5 || !cards.Select(o => o.CorrectOrder).SequenceEqual(Enumerable.Range(1, cards.Length).Select(i => (int?)i)) || HasDuplicateText(item))
        { errors.Add(new("OPTION_COUNT_INVALID", index, "options")); return; }
        var quote = item.SourceQuote;
        if (string.IsNullOrWhiteSpace(quote) || CountWords(quote) > SentenceMaxWords) { errors.Add(new("LENGTH_LIMIT", index, "sentenceQuote")); return; }
        // Ghép các thẻ theo thứ tự chuẩn phải ra đúng câu trích (không phân biệt hoa thường, bỏ dấu câu cuối).
        var spelled = string.Join(' ', cards.Select(c => c.Text.Trim()));
        if (!string.Equals(Normalize(StripPunctuation(spelled)), Normalize(StripPunctuation(quote)), StringComparison.OrdinalIgnoreCase))
            errors.Add(new("SCHEMA_INVALID", index, "options"));
        var orders = SentenceOrders.Parse(item.AcceptedOrdersJson);
        if (orders is null || !SentenceOrders.IsValid(orders, cards.Length) || !orders[0].SequenceEqual(Enumerable.Range(1, cards.Length)))
            errors.Add(new("SENTENCE_ORDERS_INVALID", index, "acceptedOrders"));
    }

    private static string StripPunctuation(string s) => new(s.Where(c => !char.IsPunctuation(c)).ToArray());

    private static void ValidateEventOrderingAge7(ListeningInteractionDraft item, int index, List<ListeningValidationError> errors)
    {
        if (item.AnchorOffset is { } anchor && item.Options.Any(o => o.SourceEnd is { } end && end > anchor))
            errors.Add(new("EVENT_AFTER_ANCHOR", index, "events"));
    }
}
```
Nối vào `Validate`:
```csharp
case ListeningInteractionType.PictureSearch: ValidatePictureSearch(item, i, context, errors); break;
case ListeningInteractionType.TrueFalse: ValidateTrueFalse(item, i, context, errors); break;
case ListeningInteractionType.FollowAction: ValidateFollowAction(item, i, context, errors); break;
case ListeningInteractionType.SentenceBuilder: ValidateSentenceBuilder(item, i, context, errors); break;
case ListeningInteractionType.EventOrdering: ValidateOrdering(item, i, errors); if (context.TargetAge == 7) ValidateEventOrderingAge7(item, i, errors); break;
```
`ValidateFields`: cho phép `TargetLabel` chỉ khi `PictureSearch`; `AcceptedOrdersJson` chỉ khi `SentenceBuilder`; `Prompt` thêm `PictureSearch`/`FollowAction`; `CharacterName` thêm `FollowAction`; `Options` được phép ở `TrueFalse`/`FollowAction`/`SentenceBuilder`; `ImageDescription` thêm `FollowAction`; `Role` chỉ khác `None` ở `FollowAction`; `CorrectOrder` thêm `SentenceBuilder`; `SourceQuote` thêm `PictureSearch`, `SentenceBuilder`; `Feedback` của option cấm với 3 loại mới. `RequiredFeedback`: `PictureSearch` → `matchFeedback`, `retryFeedback`; `TrueFalse` → `matchFeedback`, `retryFeedback`; `FollowAction` → `matchFeedback`, `retryFeedback`; `SentenceBuilder` → `matchFeedback`, `retryFeedback`. Cập nhật `TextFields` để quét `GRADING_WORD`/`UNSAFE_CONTENT` cho `TargetLabel`.

- [ ] **Step 4: Chạy test, phải qua** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionRules`; Expected: PASS (test luật tuổi 6 và bộ cũ không đổi).

---

### Task 6: Hợp đồng AI và prompt cho bộ tuổi 7

**Việc này làm gì (nói dễ hiểu):**
Dịch vụ AI cần biết đang soạn cho bé 7 tuổi và soạn loại nào: một yêu cầu "tìm đồ vật" kèm tên đồ vật; hai câu đúng/sai; một việc bé làm giúp nhân vật kèm vật kéo và chỗ thả; một câu ngắn để ghép kèm mọi cách xếp hợp lệ; ba sự kiện để sắp xếp giữa truyện. Core gửi tuổi đích và loại, AI trả các trường mới, Core chuyển về bản nháp để kiểm luật.

**Cần thêm / còn thiếu:**
- Đã có: `GenerateListeningInteractionRequest.TargetAge/CheckpointKind/AllowedLetters`, `ListeningInteractionAiMapper`, `ListeningGenerationContract` (Template/Schema/PromptTypeFor), `PromptTemplateProvider`, `GetListeningPromptSnapshotAsync(type, ageBand, language)`, `EventOrderingItemDto`.
- Cần thêm: DTO `PictureSearchItemDto`, `TrueFalseItemDto`, `FollowActionItemDto`, `SentenceBuilderItemDto`; `GenerateListeningInteractionResponse.PictureSearches/TrueFalses/FollowActions/SentenceBuilders`; prompt `ListeningPictureSearch`, `ListeningTrueFalse`, `ListeningFollowAction`, `ListeningSentenceBuilder`, `ListeningEventOrderingAge7`; `AllowedTypes[7]`; `PromptTypeFor("EventOrdering", 7)` → `:Age7`; request cho `EventOrdering` tuổi 7 gửi `AllowedBlockIds` (vùng) thay vì rỗng.
- Còn thiếu / ngoài phạm vi: câu chữ prompt do người phụ trách prompt AI duyệt lại; plan chốt schema JSON và luật. Đồ vật trong tranh do AI nêu theo **chữ trong truyện**, không bịa thêm.

**Files:**
- Modify: `src/Shared/StoryPlatform.Contracts/AI/Models/StoryContentModels.cs`, `Responses/GenerationResponses.cs`, `Requests/GenerationRequests.cs` (nếu cần cờ)
- Modify: `src/AI/StoryPlatform.AI.Application/ContentGeneration/GenerateListeningInteractionHandler.cs`, `src/AI/StoryPlatform.AI.Domain/Enums/GenerationEnums.cs`, `src/AI/StoryPlatform.AI.Infrastructure/PromptCatalog/PromptTemplateProvider.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionAiMapper.cs`, `ListeningInteractionGenerator.cs` (snapshot key `:Age7`)
- Test: `ListeningInteractionAiMapperTests.cs`, `tests/StoryPlatform.AI.IntegrationTests/ListeningHttpContractTests.cs`, `tests/StoryPlatform.AI.UnitTests/…`

**Interfaces:**
- Consumes: `TargetLabel`, `AcceptedOrdersJson`, `Role` (Task 2, 5).
- Produces (schema JSON AI trả):
  - `PictureSearch`: `{ afterBlockId, sourceQuote, targetLabel, question, matchFeedback, retryFeedback }`
  - `TrueFalse`: `{ afterBlockId, statements: [{ text, isTrue, evidenceLabel? }] (đúng 2), matchFeedback, retryFeedback }`
  - `FollowAction`: `{ afterBlockId, characterName, question, items: [{ name, isCorrect, imageDescription }] (2–3), target: { name, imageDescription }, matchFeedback, retryFeedback }`
  - `SentenceBuilder`: `{ afterBlockId, sentenceQuote, cards: [text] (3–5, đúng thứ tự trong truyện), acceptedOrders: [[1,2,3,4], …] (1–6), matchFeedback, retryFeedback }`
  - `EventOrdering:Age7`: như `EventOrdering` hiện có nhưng có `afterBlockId` thuộc vùng cho phép.
  - `ListeningInteractionAiMapper.ToDrafts` cho 4 loại mới; `evidenceLabel` của câu đúng/sai gán vào `ListeningOptionDraft.ImageDescription` tạm thời **không dùng** (bằng chứng trong tranh làm ở Task 7).

- [ ] **Step 1: Viết test hỏng**

```csharp
[Fact]
public void Age7_TrueFalse_MapsStatementsAsOptions()
{
    var draft = Assert.Single(ListeningInteractionAiMapper.ToDrafts(S.Age7AiResponse(T.TrueFalse), T.TrueFalse));
    Assert.Equal(2, draft.Options.Count);
    Assert.Single(draft.Options, o => o.IsMatch);
}

[Fact]
public void Age7_FollowAction_MapsItemsAndTargetWithRoles()
{
    var draft = Assert.Single(ListeningInteractionAiMapper.ToDrafts(S.Age7AiResponse(T.FollowAction), T.FollowAction));
    Assert.Single(draft.Options, o => o.Role == ListeningOptionRole.Target);
    Assert.Single(draft.Options, o => o.Role == ListeningOptionRole.Item && o.IsMatch);
    Assert.All(draft.Options, o => Assert.False(string.IsNullOrWhiteSpace(o.ImageDescription)));
}

[Fact]
public void Age7_SentenceBuilder_MapsCardsAndAcceptedOrders()
{
    var draft = Assert.Single(ListeningInteractionAiMapper.ToDrafts(S.Age7AiResponse(T.SentenceBuilder), T.SentenceBuilder));
    Assert.Equal([1, 2, 3, 4], draft.Options.Select(o => o.CorrectOrder!.Value));
    Assert.Equal("[[1,2,3,4]]", draft.AcceptedOrdersJson);
}

[Fact]
public void Age7_PictureSearch_MapsTargetLabel() =>
    Assert.Equal("bác Gấu", Assert.Single(ListeningInteractionAiMapper.ToDrafts(S.Age7AiResponse(T.PictureSearch), T.PictureSearch)).TargetLabel);

[Fact]
public void BuildRequest_Age7EventOrdering_SendsZones()
{
    var request = ListeningInteractionAiMapper.BuildRequest("age7", T.EventOrdering, 1, AgeBand.Age_6_8, "vi", "Story", "Lesson",
        S.Blocks, [], [], [["P5"]], [], [], targetAge: 7);
    Assert.Equal(7, request.TargetAge);
    Assert.Equal([["P5"]], request.AllowedBlockIds);
}
```
Hợp đồng HTTP (`ListeningHttpContractTests`): request `targetAge: 7` cho 5 loại hợp lệ; `targetAge: 7` với `CharacterLetter` bị từ chối; `Checkpoint` tuổi 7 bị từ chối; schema `SentenceBuilder` có `cards` 3–5 và `acceptedOrders` 1–6.

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionAiMapperTests`; Expected: FAIL.

- [ ] **Step 3: Triển khai** — thêm DTO, response lists, mapper, prompt và schema (mẫu từ `CharacterLetter`/`WhoSaidIt`), ví dụ luật prompt:
  - `PictureSearch`: "Pick one concrete, visible object or character named in the paragraph that ends at afterBlockId (not an idea or feeling). targetLabel is its name exactly as in the story, at most 6 words. question starts with 'Tìm' and contains targetLabel. sourceQuote is the verbatim sentence naming it. Include matchFeedback and retryFeedback without grading words."
  - `TrueFalse`: "Write exactly two short statements (≤12 words) about the paragraph just heard: one true, one false. The false one must change a concrete detail (who, what, colour, place), not just add 'không'. Both must use words from the story. Include matchFeedback and retryFeedback without grading words."
  - `FollowAction`: "Choose one physical action a character does in the paragraph (move or put an object somewhere). question: 'Bé giúp {name} nhé: …'. Provide 1–2 distractor items plus exactly one correct item and exactly one target place, each with a story-faithful imageDescription (no writing, no extra characters)."
  - `SentenceBuilder`: "Pick one short sentence (3–5 cards when split into words/phrases; keep compound words like 'con thỏ' as one card) from the paragraph. cards in story order. acceptedOrders lists every other grammatical Vietnamese ordering of the same cards (maximum 6 total, first is the story order); do not include orderings that change the meaning."
  - `EventOrdering:Age7`: như prompt `EventOrdering` hiện có, thêm: "Place the question at afterBlockId in its allowed zone; all three events must come from paragraphs up to afterBlockId."
  Snapshot key `$"{type}:Age7"` cho `EventOrdering` tuổi 7; các loại tuổi 7 mới dùng tên loại thường (`PictureSearch`...).

- [ ] **Step 4: Chạy test, phải qua** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ListeningInteractionAiMapperTests`, `dotnet test tests/StoryPlatform.AI.UnitTests`, `dotnet test tests/StoryPlatform.AI.IntegrationTests --filter FullyQualifiedName~Listening`; Expected: PASS.

---

### Task 7: Định vị đồ vật trong tranh (hotspot)

**Việc này làm gì (nói dễ hiểu):**
Để chấm "chạm vào cái nón đỏ", hệ thống phải biết cái nón nằm ở đâu trong tranh. Sau khi tranh minh họa của đoạn truyện sẵn sàng, một mô hình nhìn ảnh được hỏi "đồ vật này ở đâu" và trả khung bao; khung được lưu cùng tương tác. Nếu không tìm thấy hoặc khung bất thường, tương tác không hiện cho bé cho tới khi người duyệt sửa tên đồ vật hoặc sinh lại tranh.

**Cần thêm / còn thiếu:**
- Đã có: `IMediaAlignmentEvaluator` + `GeminiMediaAlignmentEvaluator` (mẫu gọi Gemini/Vertex, kiểm riêng tư `IMediaOutboundPrivacyGuard`, đo thời gian, mã lỗi), nhánh tạo ảnh trong `MediaGenerationService.ImageTasks.cs`, `EventSceneImageUrl` (chọn tranh của đoạn), `MediaReadinessService.MissingListeningMediaAsync`.
- Cần thêm: `IPictureObjectLocator` + `GeminiPictureObjectLocator`; bước `LocateListeningHotspotsAsync(versionId)` chạy sau khi tranh của đoạn sẵn sàng; lưu `ListeningInteraction.HotspotJson`; readiness báo `"{id}:hotspot"` khi thiếu; khi tranh được sinh lại, xóa `HotspotJson` để định vị lại.
- Còn thiếu / ngoài phạm vi: bằng chứng trong tranh của câu đúng/sai (`ListeningInteractionOption.HotspotJson`) dùng chung bộ định vị này, **tùy chọn**: câu nào có `evidenceLabel` mà không tìm được khung thì chỉ bỏ phần "chỉ vào tranh", không chặn tương tác.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Interfaces/IMediaGenerationServices.cs` (`IPictureObjectLocator`)
- Create: `src/Core/StoryPlatform.Infrastructure/AI/GeminiPictureObjectLocator.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs` (đăng ký, giống `IMediaAlignmentEvaluator`; nơi không cấu hình dùng bản fail-closed)
- Modify: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs` (+ nơi gọi sau nhánh ảnh trong `MediaGenerationService.ImageTasks.cs`)
- Modify: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaReadinessService.cs`, `ListeningMediaRequirements.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ObjectLocatorParsingTests.cs`, `MediaGenerationServiceTests.Listening.cs`

**Interfaces:**
- Consumes: `HotspotBox` (Task 3), `ListeningInteraction.TargetLabel/HotspotJson` (Task 2).
- Produces:
```csharp
public sealed record LocatedObject(double X, double Y, double W, double H, double Confidence);
public interface IPictureObjectLocator
{
    /// <summary>Khung bao (0–1) của đồ vật theo tên; null khi không thấy. Không gửi dữ liệu cá nhân của bé.</summary>
    Task<LocatedObject?> LocateAsync(GeneratedMedia picture, string objectLabel, CancellationToken ct = default);
}
public static class ObjectLocatorResponse
{
    public const double MinConfidence = 0.5;
    public static LocatedObject? Parse(string modelJson);   // box_2d [ymin,xmin,ymax,xmax] thang 0–1000 → 0–1
}
```
`ListeningMediaRequirements.Missing`: với `PictureSearch` thêm `"{id}:i:hotspot"` khi `HotspotBox.TryParse(interaction.HotspotJson)` là null hoặc `AssetId` không còn là tranh hiện tại của đoạn.

- [ ] **Step 1: Viết test hỏng**

```csharp
public sealed class ObjectLocatorParsingTests
{
    [Fact]
    public void Parse_ConvertsGeminiBoxToUnitSpace()
    {
        // Gemini trả box_2d = [ymin, xmin, ymax, xmax] trong thang 0–1000.
        var box = ObjectLocatorResponse.Parse("{\"found\":true,\"confidence\":0.9,\"box_2d\":[500,400,700,600]}");
        Assert.NotNull(box);
        Assert.Equal(0.40, box!.X, 3); Assert.Equal(0.50, box.Y, 3); Assert.Equal(0.20, box.W, 3); Assert.Equal(0.20, box.H, 3);
    }

    [Theory]
    [InlineData("{\"found\":false}")]
    [InlineData("{\"found\":true,\"confidence\":0.2,\"box_2d\":[500,400,700,600]}")]   // tin cậy thấp
    [InlineData("{\"found\":true,\"confidence\":0.9,\"box_2d\":[700,600,500,400]}")]   // ngược
    [InlineData("{\"found\":true,\"confidence\":0.9,\"box_2d\":[0,0,1000,1000]}")]     // cả tranh = không định vị được
    [InlineData("not json")]
    public void Parse_Unusable_ReturnsNull(string json) => Assert.Null(ObjectLocatorResponse.Parse(json));
}
```
Media test: sau khi tranh của đoạn sẵn sàng, với locator giả trả khung hợp lệ, `ListeningInteraction.HotspotJson` được ghi với `assetId` của tranh; locator trả null → `HotspotJson` null và `ListeningMediaRequirements.Missing` có `"…:i:hotspot"`; sinh lại tranh → `HotspotJson` bị xóa.

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ObjectLocatorParsingTests|FullyQualifiedName~MediaGenerationServiceTests"`; Expected: FAIL.

- [ ] **Step 3: Triển khai**
  - `ObjectLocatorResponse.Parse`: đọc `found`, `confidence`, `box_2d`; từ chối khi `found = false`, `confidence < MinConfidence`, `ymin ≥ ymax` hoặc `xmin ≥ xmax`, hoặc khung phủ > 80% tranh (nghi ngờ trả cả ảnh); đổi sang `X = xmin/1000, Y = ymin/1000, W = (xmax-xmin)/1000, H = (ymax-ymin)/1000`.
  - `GeminiPictureObjectLocator`: sao cấu trúc gọi HTTP, chọn Vertex hay Gemini, hết hạn, mã lỗi, ghi nhật ký từ `GeminiMediaAlignmentEvaluator` (không viết lại cách khác). Prompt (tiếng Anh, JSON bắt buộc): `Locate "{label}" in this illustration. Reply only JSON {"found":bool,"confidence":0..1,"box_2d":[ymin,xmin,ymax,xmax]} on a 0-1000 scale. found=false if the object is not clearly visible.` Chạy qua `IMediaOutboundPrivacyGuard` như bộ đánh giá hiện có. Không cấu hình khóa thì trả null (tương tác không hiện, không sập job).
  - `LocateListeningHotspotsAsync(versionId)`: với mỗi `PictureSearch` chưa gắn hotspot, chọn tranh minh họa Ready của đoạn neo (cùng hàm chọn như `EventSceneImageUrl`: beat chứa `SourceStart`, không có beat thì tranh scene); tải bytes từ kho lưu trữ; gọi locator; `HotspotBox.IsValid`; ghi `HotspotJson`. Gọi hàm này sau khi nhánh minh họa của version hoàn tất (tìm chỗ `CompleteBranchAsync`/`AggregateImageBranchAsync` đã gọi `QueueListeningImageTasksAsync`) và trong luồng gói phụ huynh tương ứng (`MediaGenerationService.ParentPackages.cs`).
  - Khi một tranh minh họa của đoạn bị thay (`IsCurrent` đổi), `HotspotJson` của các `PictureSearch` trỏ tới nó bị xóa (trong cùng chỗ `RetireMediaAsync` hiện có dùng khi tắt media).
  - `MediaReadinessService.MissingListeningMediaAsync` và `ListeningMediaRequirements.Missing` thêm `"{id}:i:hotspot"`.

- [ ] **Step 4: Chạy test, phải qua** — Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ObjectLocator|FullyQualifiedName~MediaGeneration|FullyQualifiedName~ListeningMediaRequirements"`; Expected: PASS.

---

### Task 8: Audio và hình cho bốn loại mới

**Việc này làm gì (nói dễ hiểu):**
Bé 7 tuổi vẫn nghe giọng đọc cho mọi chữ trên màn hình: câu "Tìm cái nón đỏ", hai câu đúng/sai, yêu cầu "Bé giúp thỏ nhé…", từng thẻ từ của câu, và cả câu ghép đúng. Vật kéo và chỗ thả của "Làm theo hành động" cần tranh riêng. Task này khai báo những audio và hình bắt buộc để hệ thống tạo media và chỉ hiện tương tác khi đủ.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningAudioParts.For/InstructionFor/FixedLine`, `ListeningMediaRequirements.Missing/ImageOptions/HasOptionImages`, các hàm tạo audio/ảnh trong `MediaGenerationService.ListeningMedia.cs`.
- Cần thêm: câu hướng dẫn cố định cho 4 loại mới; phần cố định `retryLookClose` ("Nhìn kỹ lại nhé."), `retryHear` ("Nghe lạ quá, mình thử lại nhé."), `retryTry` ("Bé thử lại nhé."); `PictureSearch`: `prompt`, `match`, `noMatch`; `TrueFalse`: `text` từng câu, `match`, `noMatch`; `FollowAction`: `prompt`, `match`, `noMatch`, `text` từng thẻ; `SentenceBuilder`: `text` từng thẻ, `sentence` (câu đúng thứ tự gốc), `sentence:{n}` (mỗi thứ tự chấp nhận khác), `match`, `noMatch`; hình cho thẻ `FollowAction` (cả vật và chỗ thả).
- Còn thiếu / ngoài phạm vi: `EventOrdering` tuổi 7 dùng nguyên audio/hình đang có (thẻ `text` + tranh đoạn), thêm hướng dẫn cố định đã có. Mốc từng chữ (`HighlightKeys`) mở rộng cho `sentence` để chữ sáng khi đọc câu đúng.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningAudioParts.cs`, `ListeningMediaRequirements.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs` (chỉ nếu khóa audio theo thứ tự `sentence:{n}` cần xử lý riêng), `…CreatorUploadTasks.cs`, `…ParentPackages.cs` (đã dùng `HasOptionImages`)
- Test: `ListeningAudioPartsTests.cs`, `ListeningMediaRequirementsTests.cs`, `MediaGenerationServiceTests.Listening.cs`

**Interfaces:**
- Consumes: `SentenceOrders`, `ListeningOptionRole`.
- Produces: hằng `ListeningAudioParts.RetryLookClose`, `RetryHear`, `RetryTry`, `Sentence`; `ListeningAudioParts.SentenceKey(int orderIndex) : string` (`"sentence"` cho thứ tự 0, `"sentence:2"`, `"sentence:3"`… cho các thứ tự còn lại); `HasOptionImages` trả true cho `FollowAction`.

- [ ] **Step 1: Viết test hỏng**

```csharp
[Fact]
public void SentenceBuilder_ReadsEveryCardAndEveryAcceptedSentence()
{
    var interaction = new ListeningInteraction { Id = 9, Type = ListeningInteractionType.SentenceBuilder, TargetAge = 7,
        SourceQuote = "Bác Gấu dẫn Thỏ về hang.", AcceptedOrdersJson = "[[1,2,3],[2,1,3]]", MatchFeedback = "Con ghép giỏi quá!", NoMatchFeedback = "Nghe lạ quá, mình thử lại nhé." };
    var options = new[] { ("Bác Gấu", 1), ("dẫn Thỏ", 2), ("về hang", 3) }.Select((c, i) => new ListeningInteractionOption
        { Id = 90 + i, ListeningInteractionId = 9, SortOrder = i, Text = c.Item1, CorrectOrder = c.Item2 }).ToArray();
    var parts = ListeningAudioParts.For(interaction, options);
    Assert.Equal(3, parts.Count(p => p.PartKey == ListeningAudioParts.Text && p.OptionId is not null));
    Assert.Equal("Bác Gấu dẫn Thỏ về hang", parts.Single(p => p.PartKey == "sentence").Text.TrimEnd('.'));
    Assert.Equal("dẫn Thỏ Bác Gấu về hang", parts.Single(p => p.PartKey == "sentence:2").Text);
    Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.RetryHear && p.Text == "Nghe lạ quá, mình thử lại nhé.");
}

[Fact]
public void PictureSearch_ReadsPromptAndFixedLookHint()
{
    var interaction = new ListeningInteraction { Id = 7, Type = ListeningInteractionType.PictureSearch, TargetAge = 7, Prompt = "Tìm bác Gấu trong tranh", MatchFeedback = "Con tìm giỏi quá!", NoMatchFeedback = "Nhìn kỹ lại nhé." };
    var parts = ListeningAudioParts.For(interaction, []);
    Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.Prompt);
    Assert.Contains(parts, p => p.PartKey == ListeningAudioParts.RetryLookClose && p.Text == "Nhìn kỹ lại nhé.");
}

[Fact]
public void FollowAction_RequiresPictureForItemsAndTarget()
{
    var interaction = new ListeningInteraction { Id = 8, Type = ListeningInteractionType.FollowAction, TargetAge = 7 };
    var options = new[] { new ListeningInteractionOption { Id = 80, ListeningInteractionId = 8, Text = "bác Gấu", Role = ListeningOptionRole.Item, IsMatch = true },
                          new ListeningInteractionOption { Id = 81, ListeningInteractionId = 8, Text = "cửa hang", Role = ListeningOptionRole.Target } };
    var missing = ListeningMediaRequirements.Missing(interaction, options, []);
    Assert.Contains("8:o80:image", missing);
    Assert.Contains("8:o81:image", missing);
}

[Fact]
public void TrueFalse_ReadsBothStatements()
{
    var interaction = new ListeningInteraction { Id = 6, Type = ListeningInteractionType.TrueFalse, TargetAge = 7, MatchFeedback = "Đúng rồi!", NoMatchFeedback = "Mình nghe lại nhé." };
    var options = new[] { new ListeningInteractionOption { Id = 60, ListeningInteractionId = 6, Text = "Thỏ đội nón xanh", SortOrder = 0 },
                          new ListeningInteractionOption { Id = 61, ListeningInteractionId = 6, Text = "Thỏ đội nón đỏ", SortOrder = 1, IsMatch = true } };
    Assert.Equal(2, ListeningAudioParts.For(interaction, options).Count(p => p.OptionId is not null && p.PartKey == ListeningAudioParts.Text));
}
```
Thêm test cho `HighlightKeys` chứa `sentence`, và bảo đảm bộ tuổi 6 không đổi (các test sẵn có).

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ListeningAudioPartsTests|FullyQualifiedName~ListeningMediaRequirementsTests"`; Expected: FAIL.

- [ ] **Step 3: Triển khai** — `InstructionFor`:
```csharp
ListeningInteractionType.PictureSearch => "Em hãy nghe rồi chạm vào đồ vật trong tranh nhé!",
ListeningInteractionType.TrueFalse => "Em hãy nghe câu rồi chạm dấu đúng hoặc dấu sai nhé!",
ListeningInteractionType.FollowAction => "Em hãy kéo đồ vật đến đúng chỗ giúp bạn nhé!",
ListeningInteractionType.SentenceBuilder => "Em hãy kéo các thẻ chữ thành câu rồi đọc to câu đó nhé!",
```
Trong `For` thêm các nhánh theo bảng ở "Cần thêm"; `SentenceBuilder` ghép chữ bằng `string.Join(' ', ordered.Select(texts))`, câu `sentence` dùng nguyên `SourceQuote` đã chấp nhận (giữ dấu câu), các thứ tự còn lại ghép từ thẻ. Vòng cuối của `For` đọc `text` từng thẻ cho `TrueFalse`, `FollowAction`, `SentenceBuilder` (không đọc phản hồi của thẻ). `HasOptionImages` thêm `FollowAction`. `HighlightKeys = [Text, Name, Prompt, "sentence"]`. `MediaGenerationService.ListeningMedia.cs`: khóa phần `sentence:n` đã hợp lệ cho cột `AudioPartKey` (kiểm độ dài tối đa của cột, nếu giới hạn nhỏ thì dùng `sentence2`, `sentence3`); kiểm trong test tạo audio rằng `sentence:2` được lưu và tìm lại bằng `FindAudio`.

- [ ] **Step 4: Chạy test, phải qua** — `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ListeningAudioParts|FullyQualifiedName~ListeningMediaRequirements|FullyQualifiedName~MediaGenerationServiceTests"`; Expected: PASS.

---

### Task 9: Gửi cho bé đúng bộ tuổi 7 (DTO nội dung)

**Việc này làm gì (nói dễ hiểu):**
Khi bé 7 tuổi mở truyện, mỗi scene gửi xuống đủ dữ liệu để màn hình vẽ: tranh cần tìm đồ vật (không kèm vị trí), hai câu đúng sai kèm audio, vật kéo và chỗ thả kèm tranh, các thẻ từ của câu cần ghép kèm audio, ba thẻ sự kiện kèm tranh. Đáp án đúng và vị trí đồ vật không bao giờ gửi trước khi cần.

**Cần thêm / còn thiếu:**
- Đã có: `ChildListeningContent.Build`, `ReadingInteractionDto`, ký URL media (`ChildMediaUrlSigning`), lọc `TargetAge == childAge`, `EventSceneImageUrl`, `Shuffle`.
- Cần thêm: DTO và trường `PictureSearch` (`SearchImageUrl`, `Target` = `TargetLabel`), `TrueFalse` (`Statements[]` với `id`, `text`, `audioUrl`), `FollowAction` (`Items[]`, `Target`, `CharacterName`), `SentenceBuilder` (`WordCards[]` đã xáo, `SlotCount`); `MaxChoices`, `Graded` cho loại mới; `AudioWordTimings` có khóa `sentence`; ký URL ảnh/âm thanh mới.
- Còn thiếu / ngoài phạm vi: không gửi `IsMatch`, `HotspotJson`, `AcceptedOrdersJson`, `CorrectOrder` của thẻ ghép. Tranh của `PictureSearch` là tranh minh họa của đoạn neo (cùng hàm chọn tranh với Task 7). Thiếu tranh hoặc thiếu hotspot thì tương tác không hiện.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/ChildListeningContent.cs`, `DTOs/ChildReadingDtos.cs`, `Services/ChildMediaUrlSigner.cs`, `Services/ChildReadingService.cs` (nạp thêm beats/segments nếu chưa có)
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildListeningContentTests.cs`

**Interfaces:**
- Produces (thêm vào `ReadingInteractionDto`):
```csharp
public string? SearchImageUrl { get; set; }                       // PictureSearch
public string? SearchTarget { get; set; }                         // "cái nón đỏ"
public List<ReadingStatementDto>? Statements { get; set; }        // TrueFalse: [{ Id, Text, AudioUrl }]
public List<ReadingActionItemDto>? ActionItems { get; set; }      // FollowAction: vật kéo [{ Id, Name, ImageUrl, AudioUrl }]
public ReadingActionItemDto? ActionTarget { get; set; }           // FollowAction: chỗ thả
public List<ReadingWordCardDto>? WordCards { get; set; }          // SentenceBuilder (đã xáo): [{ Id, Text, AudioUrl }]
public int? SlotCount { get; set; }
```

- [ ] **Step 1: Viết test hỏng**

```csharp
[Fact] public async Task PictureSearch_ShowsStoryPictureAndTarget_WithoutHotspot() { /* bé 7 tuổi, tranh minh họa Ready của scene, HotspotJson hợp lệ → SearchImageUrl, SearchTarget = "bác Gấu"; JSON của DTO không chứa "hotspot", "box", "x":, "isMatch" */ }
[Fact] public async Task PictureSearch_HiddenWithoutValidHotspot() { /* HotspotJson null hoặc assetId không phải tranh hiện tại → interaction = null */ }
[Fact] public async Task TrueFalse_ReturnsTwoStatementsShuffledBySession_WithoutAnswer() { /* 2 Statements, có AudioUrl, không lộ câu đúng, thứ tự cố định theo phiên */ }
[Fact] public async Task FollowAction_ReturnsItemsAndTargetWithPictures_WithoutAnswer() { /* ActionItems 2–3 có ImageUrl, ActionTarget có ImageUrl, không lộ vật đúng */ }
[Fact] public async Task SentenceBuilder_ReturnsShuffledCards_NeverStoryOrder() { /* WordCards không trùng thứ tự chuẩn, SlotCount = số thẻ, mỗi thẻ có AudioUrl */ }
[Fact] public async Task EventOrdering_Age7_ShownMidStory() { /* EventOrdering TargetAge 7 neo ở scene giữa truyện, thẻ có ImageUrl của đoạn */ }
[Fact] public async Task Age7Child_SeesOnlyAge7Set_AndNotAge6() { /* cùng truyện có bộ tuổi 6 và 7; bé Age = 7 chỉ thấy bộ 7, bé Age = 6 chỉ thấy bộ 6 */ }
```
Dùng helper `Add/Option/_h.AddListeningMedia` đang có trong file; mở rộng `ReadingHarness.AddListeningMedia` cho hình `FollowAction` (qua `ImageOptions`) và thêm helper tạo tranh minh họa Ready cho scene (đã có `AddBeatIllustration`).

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildListeningContentTests`; Expected: FAIL.

- [ ] **Step 3: Triển khai** — trong `ToDto`, thêm nhánh theo loại (trả sớm như `CharacterLetter`); `PictureSearch` dùng `EventSceneImageUrl`-giống hàm mới `StoryPictureUrl(interaction.SourceStart, …)` (tách logic chọn tranh thành hàm dùng chung với `EventSceneImageUrl` và `LocateListeningHotspotsAsync`); ẩn tương tác khi `HotspotBox.TryParse` null hoặc `AssetId` không khớp tranh đã chọn; `TrueFalse` xáo bằng cùng bộ xáo cố định theo phiên (`Shuffle`, không để lộ thứ tự đúng); `SentenceBuilder` xáo, đảm bảo khác thứ tự chuẩn; `MaxChoices = 2` cho loại mới; `Graded = true`. Cập nhật `ChildMediaUrlSigning` ký `SearchImageUrl`, ảnh/âm thanh của `ActionItems`, `ActionTarget`, `WordCards`, `Statements`.

- [ ] **Step 4: Chạy test, phải qua** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildListening`; Expected: PASS.

---

### Task 10: Nộp câu trả lời của bé 7 tuổi

**Việc này làm gì (nói dễ hiểu):**
Bé chạm vào tranh, trả lời đúng/sai, kéo vật tới chỗ thả, xếp các thẻ tranh hoặc thẻ chữ. Hệ thống chấm mềm, cho thử lại đúng số lần quy định rồi nói đáp án và cho truyện chạy tiếp. Không hiện điểm hay chữ "sai" cho bé.

**Cần thêm / còn thiếu:**
- Đã có: `ChildListeningService.SubmitAttemptAsync/AnswerAsync/Result/OrderingResult`, `SubmitListeningAttemptRequestDto` (`OptionId`, `Answer`, `OrderedOptionIds`), `ListeningAttemptResultDto` (`Matched`, `CanRetry`, `Revealed`, `CorrectOptionId`, `CorrectOrderOptionIds`, `ReplaySceneIndexes`), `Misplaced`, `ChildListeningContent.IsAttempted/AllowsRetry/MaxChoiceAttempts`.
- Cần thêm: trường yêu cầu `X`, `Y` (chạm), `Truth` (đúng/sai), `TargetOptionId` (chỗ thả); trường kết quả `RevealHotspot` (nháy đồ vật khi lộ đáp án), `Completed` (cả tương tác đã xong), `OptionId` (câu vừa chấm của đúng/sai); `Result` theo từng loại:
  - **PictureSearch:** chạm trong `HotspotBox.Hits` → khớp, lời khen; chưa trúng → "Nhìn kỹ lại nhé." chọn lại 1 lần; lần 2 chưa trúng → `Revealed`, `RevealHotspot` để nháy, truyện chạy tiếp. `AnswerText` lưu `{"x":…,"y":…}`.
  - **TrueFalse:** mỗi câu có ≤ 2 lần chọn; gửi `{optionId, truth}`; khớp khi `truth == option.IsMatch`; câu 2 chỉ mở sau khi câu 1 xong; lần 2 sai của một câu → `Revealed` (nói đáp án; `RevealHotspot` nếu câu có bằng chứng); `Completed` khi cả hai câu xong.
  - **FollowAction:** `{optionId (vật), targetOptionId}` khớp khi vật `IsMatch` và `targetOptionId` là chỗ thả; sai → vật trả về, "Bé thử lại nhé.", 1 lần thử lại; lần 2 sai → `Revealed`, `CorrectOptionId` = vật đúng (frontend làm mẫu bằng hoạt hình).
  - **EventOrdering tuổi 7:** dùng `OrderingResult` nhưng `RevealAfterWrongOrders = 2` cho tuổi 7 (lần sai thứ 2: hệ thống xếp mẫu, `Revealed`, `CorrectOrderOptionIds`).
  - **SentenceBuilder:** `OrderedOptionIds` (thẻ theo thứ tự bé xếp) → đổi sang `SortOrder`/`CorrectOrder` → khớp khi nằm trong `AcceptedOrdersJson`; sai → "Nghe lạ quá, mình thử lại nhé." (chọn lại 1 lần); lần 2 sai → `Revealed`, `CorrectOrderOptionIds` = thứ tự chuẩn; đúng → lời khen và `Completed = false` cho tới khi bé đọc to hoặc bỏ qua (Task 11 lấy thứ tự bé vừa xếp làm câu mẫu).
- Còn thiếu / ngoài phạm vi: hoạt hình nhân vật làm theo, tô chữ, âm thanh phát lại câu bé ghép (frontend ghép audio thẻ). Gửi lại sau khi đã xong trả kết quả cuối (như tuổi 6).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildListeningService.cs`, `ChildListeningContent.cs` (`AllowsRetry`, `IsAttempted`, hằng số theo tuổi)
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/DTOs/ChildListeningDtos.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildListeningServiceTests.cs`

**Interfaces:**
- Consumes: `HotspotBox`, `SentenceOrders` (Task 3); DTO nội dung (Task 9).
- Produces:
```csharp
// SubmitListeningAttemptRequestDto: thêm
public double? X { get; set; }  public double? Y { get; set; }
public bool? Truth { get; set; }
public int? TargetOptionId { get; set; }
// ListeningAttemptResultDto: thêm
public int? OptionId { get; set; }
public bool Completed { get; set; }
public HotspotDto? RevealHotspot { get; set; }   // { X, Y, W, H }
```
`ChildListeningContent.RevealAfterWrongOrders(int? targetAge) => targetAge == 7 ? 2 : 3`.

- [ ] **Step 1: Viết test hỏng**

```csharp
[Fact] public async Task PictureSearch_TapInside_Matches() { /* hotspot (0.4,0.5,0.2,0.2); tap (0.5,0.6) → matched true, canRetry false, completed true */ }
[Fact] public async Task PictureSearch_TapNearEdge_UsesPadding() { /* tap (0.37,0.48) vẫn khớp */ }
[Fact] public async Task PictureSearch_FirstMiss_AllowsOneRetryWithLookHint() { /* feedback "Nhìn kỹ lại nhé.", canRetry true, không lộ RevealHotspot */ }
[Fact] public async Task PictureSearch_SecondMiss_RevealsHotspot() { /* revealed true, RevealHotspot có 4 số, completed true */ }
[Fact] public async Task PictureSearch_TapOutsideImage_BadRequest() { /* x = 1.4 → 400 LISTENING_ANSWER_INVALID */ }
[Fact] public async Task TrueFalse_AnswersEachStatementTwice_ThenCompletes() { /* câu 1 đúng → câu 2 mở; câu 2 sai 2 lần → revealed, completed true */ }
[Fact] public async Task TrueFalse_SecondStatementBeforeFirst_Rejected() { /* optionId của câu 2 khi câu 1 chưa xong → 400 LISTENING_ANSWER_INVALID */ }
[Fact] public async Task FollowAction_DropCorrectItemOnTarget_Matches() { /* optionId = vật đúng, targetOptionId = chỗ thả → matched */ }
[Fact] public async Task FollowAction_WrongItem_ReturnsItemAndAllowsOneRetry() { /* feedback "Bé thử lại nhé.", canRetry true; lần 2 sai → revealed, correctOptionId */ }
[Fact] public async Task FollowAction_DropOnWrongTarget_Rejected() { /* targetOptionId là một vật → 400 */ }
[Fact] public async Task EventOrdering_Age7_RevealsAfterSecondWrongOrder() { /* sai lần 2 → revealed, CorrectOrderOptionIds; tuổi khác vẫn lần 3 */ }
[Fact] public async Task SentenceBuilder_AcceptsAnyAcceptedOrder() { /* thứ tự thứ 2 trong AcceptedOrdersJson → matched */ }
[Fact] public async Task SentenceBuilder_WrongOrder_AllowsOneRetryWithHearLine() { /* feedback "Nghe lạ quá, mình thử lại nhé." */ }
[Fact] public async Task SentenceBuilder_SecondWrong_RevealsCanonicalOrder() { /* revealed, CorrectOrderOptionIds theo CorrectOrder */ }
[Fact] public async Task SentenceBuilder_PartialOrDuplicateCards_BadRequest() { /* thiếu hoặc trùng thẻ → 400 */ }
[Fact] public async Task NewTypes_ResendAfterFinish_ReturnsPreviousResult_NoExtraRow() { /* không thêm attempt */ }
```
Mỗi test dựng dữ liệu bằng helper hiện có trong file (`_h.ListeningInteractions`, `AddListeningMedia`) và gán `TargetAge = 7`, hồ sơ bé `Age = 7`; assert cụ thể như ghi chú (HTTP/mã lỗi `LISTENING_ANSWER_INVALID`, trường kết quả, số dòng attempt).

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildListeningServiceTests`; Expected: FAIL.

- [ ] **Step 3: Triển khai** — trong `AnswerAsync` thêm nhánh:

```csharp
case ListeningInteractionType.PictureSearch:
{
    var box = HotspotBox.TryParse(interaction.HotspotJson) ?? throw new BadRequestException(AnswerInvalidCode);
    if (request.X is not { } x || request.Y is not { } y || x is < 0 or > 1 || y is < 0 or > 1) throw new BadRequestException(AnswerInvalidCode);
    attempt.AnswerText = JsonSerializer.Serialize(new { x, y });
    attempt.IsMatch = box.Hits(x, y);
    break;
}
case ListeningInteractionType.TrueFalse:
{
    var option = context.Options.FirstOrDefault(o => o.Id == request.OptionId) ?? throw new BadRequestException(AnswerInvalidCode);
    if (request.Truth is not { } truth) throw new BadRequestException(AnswerInvalidCode);
    if (!TrueFalseOpenOptionIds(context).Contains(option.Id)) throw new BadRequestException(AnswerInvalidCode);   // đúng thứ tự câu
    attempt.SelectedOptionId = option.Id;
    attempt.AnswerText = truth ? "true" : "false";
    attempt.IsMatch = truth == option.IsMatch;
    break;
}
case ListeningInteractionType.FollowAction:
{
    var item = context.Options.FirstOrDefault(o => o.Id == request.OptionId && o.Role == ListeningOptionRole.Item) ?? throw new BadRequestException(AnswerInvalidCode);
    var target = context.Options.FirstOrDefault(o => o.Id == request.TargetOptionId && o.Role == ListeningOptionRole.Target) ?? throw new BadRequestException(AnswerInvalidCode);
    attempt.SelectedOptionId = item.Id;
    attempt.AnswerText = JsonSerializer.Serialize(new { target = target.Id });
    attempt.IsMatch = item.IsMatch;
    break;
}
case ListeningInteractionType.SentenceBuilder:
{
    var ordered = request.OrderedOptionIds ?? [];
    if (ordered.Count != context.Options.Count || ordered.Distinct().Count() != ordered.Count || ordered.Any(id => context.Options.All(o => o.Id != id)))
        throw new BadRequestException(AnswerInvalidCode);
    var chosen = ordered.Select(id => context.Options.First(o => o.Id == id).CorrectOrder!.Value).ToArray();
    var accepted = SentenceOrders.Parse(interaction.AcceptedOrdersJson) ?? throw new BadRequestException(AnswerInvalidCode);
    attempt.OrderedOptionIdsJson = JsonSerializer.Serialize(ordered);
    attempt.IsMatch = SentenceOrders.Matches(accepted, chosen);
    break;
}
```
`ChildListeningContent.AllowsRetry` thêm `PictureSearch`, `TrueFalse`, `FollowAction`, `SentenceBuilder`; `IsAttempted`: `TrueFalse` xong khi cả hai câu đã khớp hoặc đủ 2 lần; `SentenceBuilder` xong khi đã khớp hoặc 2 lần sai. `Result(...)` thêm nhánh theo loại với phản hồi đã chốt (khớp: `MatchFeedback`; chưa khớp lần 1: câu cố định `RetryLookClose`/`RetryTry`/`RetryHear` hoặc `NoMatchFeedback` với `TrueFalse`; lộ đáp án: `RevealHotspot` hoặc `CorrectOptionId`/`CorrectOrderOptionIds`). `RevealAfterWrongOrders(targetAge)` dùng trong `OrderingResult` và `IsAttempted` (tuổi 7 → 2).

- [ ] **Step 4: Chạy test, phải qua** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildListening`; Expected: PASS (các test tuổi 6 và tuổi khác không đổi).

---

### Task 11: Ghép câu rồi đọc to — chấm mềm giọng nói

**Việc này làm gì (nói dễ hiểu):**
Sau khi bé ghép xong câu, bé nhấn mic đọc to cả câu. Hệ thống so từng chữ: rõ thì sáng xanh, chưa rõ thì sáng vàng để bé đọc lại (tối đa 2 lần đọc lại); không có điểm, không có chữ "sai". Nếu bé không ghép đúng và hệ thống đã hiện câu đúng, bé đọc theo câu đúng đó.

**Cần thêm / còn thiếu:**
- Đã có: `ChildReadAloudService.CheckPracticeAsync/ScoreAsync` (kiểm quyền giọng nói, ASR, `ReadAloudMatcher`, telemetry, bản ghi gần nhất, giới hạn 3 lần chấm cho tuổi 6), `ReadAloudRecording.WordStatusesJson`.
- Cần thêm: `SentenceBuilder` được chấp nhận ở `CheckPracticeAsync`; câu mẫu = câu theo thứ tự bé vừa xếp **đúng** trong phiên, hoặc câu chuẩn nếu hệ thống đã lộ đáp án; chưa xếp xong → 409 `LISTENING_SENTENCE_NOT_BUILT`; giới hạn chấm và câu "Mình nghe chưa rõ, mình đọc lại nhé." như tuổi 6 (`softFeedback` cho tuổi có bộ riêng).
- Còn thiếu / ngoài phạm vi: nút "Bỏ qua" là không gọi API.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildReadAloudService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildReadAloudServiceTests.cs`

**Interfaces:**
- Consumes: attempt `OrderedOptionIdsJson` (Task 10), `SentenceOrders`.
- Produces: `CheckPracticeAsync` chấp nhận `SentenceBuilder`; mã lỗi `LISTENING_SENTENCE_NOT_BUILT`.

- [ ] **Step 1: Viết test hỏng**

```csharp
[Fact] public async Task SentenceBuilder_ScoresAgainstTheOrderTheChildBuilt() { /* thứ tự thứ 2 được chấp nhận → câu mẫu là câu theo thứ tự đó */ }
[Fact] public async Task SentenceBuilder_AfterReveal_ScoresAgainstCanonicalSentence() { /* lần 2 sai (Revealed) → câu chuẩn */ }
[Fact] public async Task SentenceBuilder_BeforeBuilding_Conflict() { /* chưa có attempt → 409 LISTENING_SENTENCE_NOT_BUILT; không nhận dạng, không upload */ }
[Fact] public async Task SentenceBuilder_UnclearWords_UseSoftMessage_AndSameRetryLimitAsAge6() { /* retry > 0 → "Mình nghe chưa rõ, mình đọc lại nhé."; lần chấm thứ 4 → 409 LISTENING_VOICE_RETRY_LIMIT */ }
[Fact] public async Task SentenceBuilder_VoiceOff_Forbidden() { /* VoiceAnswersEnabled false → 403 "Bố mẹ chưa bật đọc bằng giọng nói cho em." */ }
[Fact] public async Task SentenceBuilder_OtherAgeChild_NotFound() { /* bé 6 tuổi gửi tương tác TargetAge 7 → 404 */ }
```

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildReadAloudServiceTests`; Expected: FAIL.

- [ ] **Step 3: Triển khai** — thêm `SentenceBuilder` vào danh sách loại hợp lệ; `target` tính:

```csharp
if (interaction.Type == ListeningInteractionType.SentenceBuilder)
{
    var options = await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
        o => o.ListeningInteractionId == interactionId && !o.IsDeleted, cancellationToken: ct);
    var attempts = (await _unitOfWork.Repository<ListeningInteractionAttempt>().FindAsync(
        a => a.ReadingSessionId == session.Id && a.ListeningInteractionId == interactionId && !a.IsDeleted, cancellationToken: ct))
        .OrderBy(a => a.Id).ToArray();
    var accepted = attempts.LastOrDefault(a => a.IsMatch == true);
    var revealed = attempts.Length >= ChildListeningContent.MaxChoiceAttempts && accepted is null;
    if (accepted is null && !revealed) throw new ConflictException("LISTENING_SENTENCE_NOT_BUILT");
    var ids = accepted is null
        ? options.OrderBy(o => o.CorrectOrder).Select(o => o.Id).ToList()
        : JsonSerializer.Deserialize<List<int>>(accepted.OrderedOptionIdsJson!)!;
    target = string.Join(' ', ids.Select(id => options.First(o => o.Id == id).Text.Trim()));
}
```
`softFeedback` thành `ListeningAgeSets.HasOwnSet(interaction.TargetAge) && interaction.Type is RolePlay or CharacterLetter or SentenceBuilder`; thông điệp lời khen lấy `MatchFeedback`.

- [ ] **Step 4: Chạy test, phải qua** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildReadAloud`; Expected: PASS.

---

### Task 12: Người duyệt, phụ huynh và readiness bộ tuổi 7

**Việc này làm gì (nói dễ hiểu):**
Người duyệt thấy riêng "Bộ 7 tuổi" cạnh "Bộ 6 tuổi", xem tên đồ vật cần tìm kèm khung đã định vị đè lên tranh, sửa được câu đúng/sai, vật kéo và chỗ thả, các thẻ chữ và các cách xếp câu hợp lệ; sửa xong hệ thống kiểm lại luật. Truyện chỉ xong bước duyệt khi cả bộ 6 và bộ 7 hợp lệ và đủ media. Phụ huynh xem được mọi lựa chọn của bé (chạm ở đâu, đúng/sai từng câu, vật kéo, thứ tự xếp, chữ đọc chưa rõ).

**Cần thêm / còn thiếu:**
- Đã có: `StoryReviewService.Listening` (xem, sửa, duyệt, sinh lại theo `targetAge`), `ListeningAgeSetReviewDto`, `UpdateListeningInteractionRequestDto/OptionDto`, readiness theo `TargetAgesFor`, `ParentReadingFeedbackService.LoadListeningFeedbackAsync`, `ParentListeningFeedbackDto`.
- Cần thêm:
  - DTO duyệt: `TargetLabel`, `Hotspot` (`HotspotJson` đã phân tích), `AcceptedOrders`, `Role` của thẻ, `HasValidHotspot`; request sửa nhận `targetLabel`, `acceptedOrders`, `role`; đổi `TargetLabel` hoặc xóa mô tả → xóa `HotspotJson` để định vị lại (media task chạy lại).
  - Luật khi sửa dùng `RuleContext` với `TargetAge`; readiness yêu cầu bộ 7 (qua `TargetAgesFor`).
  - Phụ huynh: `Taps` (chạm `x`,`y`), `Choices` mang `Truth` cho đúng/sai, `ActionTargetOptionId`, `OrderedOptionIds` cho ghép câu và xếp thẻ; không có điểm.
- Còn thiếu / ngoài phạm vi: giao diện duyệt hiển thị khung đè trên tranh (frontend dùng `Hotspot` và ảnh).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/StoryReview/Services/StoryReviewService.Listening.cs`, `DTOs/ReviewDtos.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ParentReadingFeedbackService.cs`, `DTOs/ParentReadingFeedbackDtos.cs`
- Test: `tests/StoryPlatform.UnitTests/StoryReviewListeningTests.cs`, `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ParentReadingFeedbackServiceTests.cs`

**Interfaces:**
- Produces:
```csharp
// ListeningInteractionReviewDto thêm: TargetLabel, Hotspot (HotspotDto?), AcceptedOrders (IReadOnlyList<IReadOnlyList<int>>?), ListeningOptionReviewDto.Role
// UpdateListeningInteractionRequestDto thêm: TargetLabel, AcceptedOrders; UpdateListeningOptionDto thêm: Role
// ParentListeningChoiceDto: record (…) thêm AnswerText đã có; thêm OrderedOptionIds (IReadOnlyList<int>?) và TargetOptionId (int?)
```

- [ ] **Step 1: Viết test hỏng**

```csharp
[Fact] public async Task Review_ListsAge6AndAge7SetsSeparately() { /* ageSets có targetAge 6 và 7, mỗi nhóm đủ 5 item */ }
[Fact] public async Task Review_PictureSearch_ShowsHotspotForReviewer() { /* item PictureSearch có Hotspot x,y,w,h */ }
[Fact] public async Task Review_EditTargetLabel_ClearsHotspot() { /* sửa TargetLabel → HotspotJson null; media readiness báo hotspot */ }
[Fact] public async Task Review_EditTrueFalse_BothTrue_Rejected() { /* 400 chứa MATCH_RULE_INVALID@options */ }
[Fact] public async Task Review_EditSentenceBuilder_BadOrders_Rejected() { /* 400 chứa SENTENCE_ORDERS_INVALID@acceptedOrders */ }
[Fact] public async Task Review_EditFollowAction_NoTarget_Rejected() { /* 400 chứa OPTION_COUNT_INVALID@options */ }
[Fact] public async Task Readiness_Band6To8_NeedsBothAge6AndAge7Sets() { /* chỉ có bộ 6 → chưa sẵn sàng ("age:7:listening-set"); đủ cả hai và đủ media → sẵn sàng */ }
[Fact] public async Task Readiness_PictureSearchWithoutHotspot_NotReady() { /* thiếu hotspot → "…:i:hotspot" */ }
[Fact] public async Task ParentFeedback_ShowsTapsTruthOrdersAndWordStatuses() { /* có đủ các trường, không có trường điểm */ }
```

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~StoryReviewListeningTests|FullyQualifiedName~ParentReadingFeedback"`; Expected: FAIL.

- [ ] **Step 3: Triển khai** — theo mẫu xử lý trường của `CharacterLetter`/`WhoSaidIt` trong `StoryReviewService.Listening.cs`: map thêm trường, áp lên `draft` rồi gọi `ListeningInteractionRules.Validate([draft], context with { TargetAge = interaction.TargetAge, Count = 1, Zones = [] })`; ghi lại `TargetLabel`, `AcceptedOrdersJson`, `Role`; khi `TargetLabel` đổi hoặc thẻ `ImageDescription` đổi thì xóa `HotspotJson` và gọi `RetireMediaAsync`. `ParentReadingFeedbackService` đọc `AnswerText` (JSON chạm hoặc "true"/"false"), `OrderedOptionIdsJson`.

- [ ] **Step 4: Chạy test, phải qua** — `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~StoryReviewListeningTests|FullyQualifiedName~ParentReadingFeedback|FullyQualifiedName~MediaReadiness"`; Expected: PASS.

---

### Task 13: Seed và trang hướng dẫn test API tuổi 7 (cần bạn duyệt trước khi tạo file)

**Việc này làm gì (nói dễ hiểu):**
Thêm dữ liệu mẫu bộ tuổi 7 cho truyện 900 để tester chạy ngay, và một trang HTML hướng dẫn test các API mới (nội dung bé 7 tuổi nhận, chạm vào tranh, đúng sai, kéo thả, sắp xếp 3 thẻ, ghép câu và đọc to, duyệt, phụ huynh).

**Cần thêm / còn thiếu:**
- Đã có: seed bộ tuổi 6 (920–924, thẻ 920–928), template `docs/api/_template-api-test-guide.html`, trang mẫu `docs/api/age-6-interactions-api-test-guide.html` (khung có hỗ trợ PATCH).
- Cần thêm: seed bộ tuổi 7 cho truyện 900 (tương tác 930–934, thẻ 930–941; neo theo planner tuổi 7 với 6 đoạn: P1 (44) `PictureSearch`, P2 (111) `TrueFalse`, P3 (160) `SentenceBuilder`, P4 (194) `FollowAction`, P5 (219) `EventOrdering`); trang `docs/api/age-7-interactions-api-test-guide.html` dựng từ template theo CLAUDE.md mục 13.
- Còn thiếu / ngoài phạm vi: seed không có media và hotspot thật (hotspot do mô hình thị giác tính khi chạy Phase 5; trang ghi rõ điều kiện). Thư mục `docs/api` bị `.gitignore` chặn (`/docs/api/*`), muốn đưa vào repo cần `git add -f`.

**Files:**
- Modify: `Database/Seed/60_listening_interactions.sql`, `61_listening_interaction_options.sql`
- Create: `docs/api/age-7-interactions-api-test-guide.html` (copy template; khóa localStorage `age7-interactions-guide-vars/checks`)
- Test: `tests/StoryPlatform.UnitTests/SeedDataAlignmentTests.cs` (thêm kiểm bộ tuổi 7 hợp luật), mở trang trong trình duyệt

**Interfaces:**
- Seed dùng đúng văn bản truyện 900 (P1=44, P2=111, P3=160, P4=194, P5=219, P6=259):
  - `930` PictureSearch neo 44: `TargetLabel` "Thỏ con", `Prompt` "Tìm Thỏ con trong tranh", `SourceQuote` "Thỏ con đi lạc vào rừng." [0,24); không có thẻ.
  - `931` TrueFalse neo 111: thẻ 930 "Thỏ nghe tiếng sột soạt sau bụi cây" (đúng), 931 "Thỏ nghe tiếng chuông trên cây" (sai).
  - `932` SentenceBuilder neo 160: câu "Thỏ con đi lạc vào rừng." [0,24) (đã nghe ở P1), 4 thẻ 932 "Thỏ con", 933 "đi lạc", 934 "vào", 935 "rừng" (`CorrectOrder` 1–4), `AcceptedOrdersJson` `[[1,2,3,4]]`.
  - `933` FollowAction neo 194: `Prompt` "Bé giúp Thỏ nhé: dắt bác Gấu về cửa hang", `CharacterName` "Thỏ"; thẻ vật 936 "bác Gấu" (đúng), 937 "bụi cây"; chỗ thả 938 "cửa hang" (`Role` Item/Item/Target, đủ `ImageDescription`).
  - `934` EventOrdering neo 219: 3 sự kiện ở P1, P3, P4 (thẻ 939 "Thỏ đi lạc vào rừng" [0,24), 940 "Bác Gấu đến giúp Thỏ" [111,158), 941 "Bác Gấu dẫn Thỏ về cửa hang" [160,192), `CorrectOrder` 1–3), tất cả trước neo.
  - Seed kiểm đạt bằng `ListeningInteractionRules.Validate` trong test (cùng cách test của bộ tuổi 6).

- [ ] **Step 1: Viết test hỏng** — `SeedDataAlignmentTests` (hoặc test mới `Age7SeedTests`): đọc seed, dựng draft cho 930–934 và gọi `ListeningInteractionRules.Validate` với context tuổi 7 của truyện 900; mong 0 lỗi; kiểm đúng 5 loại và `TargetAge = 7`.

- [ ] **Step 2: Chạy test, phải hỏng** — `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~Age7SeedTests`; Expected: FAIL (chưa có seed).

- [ ] **Step 3: Viết seed** — thêm khối `INSERT ... ON CONFLICT ("Id") DO NOTHING` cho 60 và 61 theo mẫu bộ tuổi 6 (cột `TargetAge`, `BlankIndex`, và các cột mới `TargetLabel`, `HotspotJson`, `AcceptedOrdersJson`, `Role`). Cập nhật chú thích đầu file (số dòng).

- [ ] **Step 4: Chạy test, phải qua** — `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~Seed|FullyQualifiedName~Age7"`; Expected: PASS.

- [ ] **Step 5: Dựng trang hướng dẫn** — copy template, chỉ sửa `<title>`, header, `#chung` và phần dữ liệu tới `HẾT PHẦN DỮ LIỆU`; thêm hỗ trợ PATCH như bản tuổi 6 nếu có API PATCH (không có API PATCH mới ở tuổi 7). Nội dung: các Task 6 (sinh lại bộ tuổi 7), 9–12 (nội dung bé, nộp trả lời cho từng loại, đọc to ghép câu, duyệt, phụ huynh). Test case phủ: đúng, từng nhánh lỗi, biên (chạm sát mép, chọn lần 3, câu thứ hai trước câu thứ nhất, kéo vào chỗ thả sai, ghép thiếu thẻ), quyền (sai vai trò, không token, bé tuổi khác → 404), mốc thời gian câu `sentence`, và "không hiện tương tác khi chưa có hotspot".

- [ ] **Step 6: Kiểm trang** — mở file trong trình duyệt; không lỗi console; số API và số test case hiển thị đúng; vài request JSON dựng đúng tài khoản, URL, body.

---

### Kiểm tra cuối

- [ ] Run: `dotnet build StoryPlatform.sln` — Expected: 0 lỗi.
- [ ] Run: `dotnet test tests/StoryPlatform.UnitTests` — Expected: PASS toàn bộ (gồm test tuổi 6 và bộ cũ không đổi).
- [ ] Run: `dotnet test tests/StoryPlatform.AI.UnitTests` và `dotnet test tests/StoryPlatform.AI.IntegrationTests --filter FullyQualifiedName~Listening` — Expected: PASS.
- [ ] Run: `dotnet test tests/StoryPlatform.IntegrationTests` (nếu có DB test) — Expected: migration áp được.
- [ ] `git status` / `git diff --stat`: chỉ các file trong plan; migration chỉ có `AddColumn`.
- [ ] Thử tay với Gemini thật: một truyện có đồ vật rõ trong tranh → `HotspotJson` ghi đúng khung; một truyện không có đồ vật → tương tác ẩn và readiness báo `hotspot`.

## Đối chiếu spec (tự rà)

| Spec tuổi 7 | Task |
|---|---|
| Tìm chi tiết trong tranh: nghe yêu cầu, chạm đúng vật; đúng → sáng + khen; chưa đúng → "nhìn kỹ lại nhé", chọn lại 1 lần; sai nữa → vật nháy rồi chạy tiếp | 2, 3, 4, 5, 6, 7, 9, 10 |
| Phân biệt đúng sai: hai câu (1 đúng, 1 sai), ✓/✗, chọn lại 1 lần, sai nữa → nói đáp án và chỉ vào chi tiết đúng trong tranh | 5, 6, 7 (bằng chứng tùy chọn), 9, 10 |
| Làm theo hành động nhân vật: kéo vật đến đúng chỗ, hoạt hình khi đúng, thử lại 1 lần, rồi làm mẫu | 2, 5, 6, 8, 9, 10 (hoạt hình ở frontend) |
| Sắp xếp 3 thẻ tranh: kéo 3 tranh vào 1-2-3, thẻ sai quay về, sai nữa thì xếp mẫu và kể lại | 4 (đặt giữa truyện), 5, 6, 10 (2 lần sai) |
| Ghép từ thành câu và đọc to: thẻ từ đọc khi chạm, mọi thứ tự hợp lệ, "nghe lạ quá…", chấm mềm đọc to, tối đa 2 lần đọc lại, nút Bỏ qua | 3, 5, 6, 8, 10, 11 |
| Mỗi tuổi 5 tương tác không trùng nhau | 1, 4, 9 |
| Không hiện điểm hay chữ "sai"; lưu cho phụ huynh | 5 (`GRADING_WORD`), 10, 12 |
| Mọi nội dung do AI soạn nháp và người duyệt xác nhận | 6, 12 |
