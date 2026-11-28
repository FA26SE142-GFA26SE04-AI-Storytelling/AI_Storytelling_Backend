# Tương tác lúc nghe cho bé 8 tuổi — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bé 8 tuổi nghe truyện sẽ gặp đúng 5 tương tác riêng của tuổi 8 (Đọc luân phiên với nhân vật, Suy luận từ chi tiết, Nối nguyên nhân và kết quả, Đặt tên cho một cảnh, Bong bóng suy nghĩ), không dùng chung với tuổi 6, 7 hay tuổi khác.

**Architecture:** Dùng lại khung `TargetAge` của tuổi 6 và 7: thêm `8` vào `ListeningAgeCatalog.BuiltAges`, planner có nhánh tuổi 8, luật riêng tuổi 8, prompt AI riêng, API bé lọc theo tuổi. Tuổi 8 cần **năm loại mới** (`AlternateReading`, `InferenceQuestion`, `CauseEffect`, `SceneTitle`, `ThoughtBubble`) và không dùng lại loại nào của tuổi khác. Ba loại trắc nghiệm một đáp án (`InferenceQuestion`, `SceneTitle`, `ThoughtBubble`) đi chung một đường chấm; `CauseEffect` chấm từng cặp; `AlternateReading` chấm mềm giọng nói từng lượt bằng ASR sẵn có. Mọi nội dung do AI soạn nháp rồi người duyệt xác nhận trước khi tới bé.

**Tech Stack:** .NET 10, EF Core (PostgreSQL), xUnit + Moq, dịch vụ AI riêng ở `src/AI`, Gemini TTS/ASR có sẵn (`ReadAloudMatcher`, `ISpeechRecognizer`, `SttAlignedTtsProvider` cho mốc từng chữ).

**Spec:** `C:\Users\Admin\Downloads\Tương tác truyện cho trẻ 6–10 tuổi – Tớ Kể Chuyện.docx`, mục "8 tuổi" và "Nguyên tắc chung". Nền tảng: `docs/superpowers/plans/2026-11-28-age-6-listening-interactions.md` và `2026-11-28-age-7-listening-interactions.md`.

## Global Constraints

- Mỗi tuổi có 5 tương tác, **không trùng nhau giữa các tuổi**; bé chỉ thấy bộ đúng tuổi mình.
- Mỗi tương tác gắn với nội dung truyện, không thêm trò chơi phụ.
- Mọi tương tác dùng mic hoặc vẽ đều có nút "Bỏ qua" (bỏ qua = không gọi API, không có dòng attempt).
- Không hiện điểm hay chữ "sai" cho bé; kết quả lưu cho phụ huynh xem (phụ huynh xem tất cả lựa chọn).
- Chạy trên web, bé thao tác bằng ngón tay hoặc chuột.
- Mọi nội dung cho trẻ do AI soạn nháp và **phải qua người duyệt** trước khi tới trẻ (ràng buộc cứng của dự án); guardrail an toàn không bị bỏ qua.
- Hình trong tương tác là tranh minh họa của chính đoạn truyện (đã có); không dùng ảnh ngoài truyện.
- Chọn lại **1 lần** với Suy luận, Đặt tên cảnh, Bong bóng suy nghĩ; Nối nguyên nhân–kết quả: mỗi thẻ "chuyện xảy ra" sai **2 lần** thì hệ thống nối sẵn cặp đúng; Đọc luân phiên: mỗi lượt của bé chấm tối đa **3 lần** (1 lần đầu + 2 lần đọc lại).
- Câu hệ thống cố định (không do AI sinh): "Nhìn kỹ lại nhé.", "Nhìn lại tranh nhé.", "Mình nghe chưa rõ, mình đọc lại nhé." (đã có), "Bé thử lại nhé." (đã có).
- Không đổi hành vi của bộ tuổi 6 và 7; tuổi 9, 10 vẫn chưa có bộ cho tới plan riêng.
- Không migration nào được áp lên DB thật trong plan này; chỉ tạo migration và test.

## Năm tương tác của tuổi 8 (thiết kế đề xuất)

| # | Loại (enum) | Bé làm gì | Dữ liệu chính | Chấm / kết quả |
|---|---|---|---|---|
| 1 | Đọc luân phiên (`AlternateReading = 13`) | Chọn 1 trong 2 nhân vật; nghe hệ thống đọc lời nhân vật kia; tới lượt mình thì lời mình sáng lên, bấm mic đọc | 2–4 lượt thoại lấy nguyên văn từ truyện (mỗi lượt = 1 option có `Speaker`), 2 người nói khác nhau | Chấm mềm từng chữ (xanh/vàng), đọc lại tối đa 2 lần mỗi lượt, có "Bỏ qua"; không điểm |
| 2 | Suy luận từ chi tiết (`InferenceQuestion = 14`) | Nghe đoạn 2–3 câu lấy từ truyện, trả lời câu hỏi truyện không nói thẳng, chọn 1 trong 3 | Đoạn (`SourceQuote`), câu hỏi (`Prompt`), 3 đáp án, **3 manh mối** (option `Role = Clue`, nguyên văn trong đoạn), giải thích 1–2 câu (`Explanation`) | Đúng: khen + tô 3 manh mối + giải thích. Sai lần 1: tô manh mối rồi chọn lại 1 lần. Sai lần 2: hiện đáp án + giải thích |
| 3 | Nối nguyên nhân và kết quả (`CauseEffect = 15`) | Kéo nối 3 thẻ "chuyện xảy ra" với 3 thẻ "vì sao" | 6 option: 3 `Item` (kết quả) + 3 `Target` (lí do), cặp đúng cùng `CorrectOrder` 1–3, mỗi thẻ có câu trích | Mỗi lần kéo = 1 cặp: đúng thì sáng xanh; sai thì thẻ bật lại; sai 2 lần cùng thẻ thì nối sẵn. Xong: đọc lại 3 câu "Vì … nên …" |
| 4 | Đặt tên cho một cảnh (`SceneTitle = 16`) | Chọn tiêu đề bao quát nhất cho cảnh vừa nghe | 3 tiêu đề (1 đúng), mỗi tiêu đề có `Feedback` (vì sao bao quát / vì sao chỉ nói một chi tiết), `Explanation` | Đúng: khen + giải thích. Sai: nói tiêu đề kia chỉ nói một chi tiết, chọn lại 1 lần; lần 2 hiện đáp án |
| 5 | Bong bóng suy nghĩ (`ThoughtBubble = 17`) | Nhìn nhân vật trong một tình huống, chọn bong bóng suy nghĩ hợp nhất | Tranh của đoạn, `CharacterName`, 3 bong bóng (1 đúng), mỗi bong bóng có `Emotion` | Đúng: nhân vật biểu lộ đúng cảm xúc (`revealEmotion`). Sai: "Nhìn lại tranh nhé." rồi chọn lại 1 lần; lần 2 hiện đáp án |

## Quyết định thiết kế (đề xuất, bạn có thể đổi trước khi làm)

| Câu hỏi | Chốt trong plan |
|---|---|
| Tên loại | Thêm 5 giá trị enum 13–17 như bảng trên. Không dùng lại `RolePlay`/`Checkpoint`/`EventOrdering` cho tuổi 8 để bộ tuổi 8 độc lập. |
| Lượt thoại của Đọc luân phiên | Mỗi lượt là một `ListeningInteractionOption` (`Text` = lời nguyên văn trong ngoặc/gạch đầu dòng của truyện, `CorrectOrder` = thứ tự lượt, cột mới `Speaker`). Hai người nói khác nhau, mỗi người ≥ 1 lượt, lời ≤ 12 từ. Bé chọn nhân vật bằng một attempt (`Speaker` trong request, không chấm). Mỗi lời đều có audio sẵn (hệ thống đọc lời nhân vật còn lại, và làm mẫu cho lượt của bé). |
| Mic bị tắt (`VoiceAnswersEnabled = false`) | Giống `RolePlay` tuổi 6: API chấm trả 403 "Bố mẹ chưa bật đọc bằng giọng nói cho em."; frontend chỉ cho "Bỏ qua". Việc chọn nhân vật vẫn lưu được. |
| Giới hạn chấm từng lượt | Telemetry `AsrWordResult` thêm khóa `optionId`; đếm theo cặp (tương tác, lượt). Tối đa 3 lần / lượt. Hằng số `MaxAge6PracticeAttempts` đổi tên thành `MaxPracticeAttempts` (giá trị 3, dùng chung). |
| Bản ghi âm lần đọc gần nhất | Thêm cột `ReadAloudRecording.ListeningInteractionOptionId` (nullable) và đổi unique index practice thành (phiên, tương tác, lượt) để phụ huynh nghe/xem từng lượt. **Phương án rẻ hơn nếu bạn muốn bớt migration:** chỉ giữ bản ghi của lượt đọc cuối cùng, trạng thái chữ các lượt còn lại chỉ có trong telemetry. |
| Manh mối của Suy luận | Lưu thành 3 option `Role = Clue` (cột `Role` đã có, thêm giá trị `Clue = 3`), `Text` = đoạn con nguyên văn của `SourceQuote`; mapper tính `SourceStart/End` tương đối trong đoạn. Manh mối **không** gửi cho bé trước khi trả lời; chỉ trả `clueSpans` trong kết quả. |
| "Truyện không nói thẳng" | Không kiểm tự động hoàn toàn được. Luật kiểm heuristic: đáp án đúng (≥ 3 từ) không được nằm nguyên văn trong đoạn; phần còn lại do prompt và người duyệt. |
| Cặp Nối nguyên nhân | `Role.Item` = chuyện xảy ra (kết quả), `Role.Target` = vì sao (lí do); hai thẻ cùng `CorrectOrder` là một cặp. Bé gửi từng cặp (`OptionId` + `TargetOptionId`), máy chủ chấm; đáp án không bao giờ nằm trong DTO nội dung. Câu "Vì … nên …" do hệ thống ghép từ hai thẻ, có audio sinh sẵn (`chain`). |
| Cảm xúc của Bong bóng | Cột mới `ListeningInteractionOption.Emotion` (chuỗi), giới hạn trong danh sách cố định: `vui`, `buồn`, `sợ`, `ngạc nhiên`, `giận`, `lo lắng`, `tự hào`, `háo hức`. Chỉ trả cho bé (`revealEmotion`) sau khi chọn đúng hoặc khi lộ đáp án. |
| Tranh | Bong bóng suy nghĩ **cần** tranh minh họa dùng được của đoạn neo (không có thì ẩn, readiness báo `picture`); Đặt tên cảnh hiện tranh nếu có, không bắt buộc. |
| Truyện không có thoại | Planner bỏ `AlternateReading`, thay bằng 1 `InferenceQuestion` thứ hai (giống cách tuổi 6 xử lý thiếu thoại). |
| Sinh | Truyện nhóm 6-8 sinh **ba bộ độc lập** (6, 7, 8). Tuổi 9, 10 vẫn chưa có. |
| Code cũ không còn dùng (FillBlank, ReadAloud, Checkpoint Prediction/Emotion/Opinion) | Giữ nguyên, không dọn trong plan này. |

## File Structure

- `src/Shared/StoryPlatform.Contracts/AI/Models/ListeningAgeCatalog.cs` — thêm 8.
- `src/Core/StoryPlatform.Domain/Enums/ListeningInteractionType.cs`, `ListeningOptionRole.cs` — loại 13–17 và `Clue = 3`.
- `src/Core/StoryPlatform.Domain/Entities/ListeningInteractionOption.cs` (`Speaker`, `Emotion`), `ReadAloudRecording.cs` (`ListeningInteractionOptionId`) — cột mới.
- `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/ReadAloudRecordingConfiguration.cs`, `ListeningInteractionOption` configuration — độ dài cột và index.
- `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddAge8ListeningInteractions.cs` — migration (EF sinh).
- `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/`: `ListeningInteractionPlanner.cs`, `ListeningInteractionRules.cs`, `ListeningInteractionRules.Age8.cs` (**mới**), `CausePairs.cs` (**mới**, hàm thuần), `ClueSpans.cs` (**mới**, hàm thuần), `ListeningInteractionAiMapper.cs`, `ListeningInteractionMapper.cs`, `ListeningInteractionGenerator.cs`, `ListeningAudioParts.cs`, `ListeningMediaRequirements.cs`.
- `src/Shared/StoryPlatform.Contracts/AI/Models/StoryContentModels.cs`, `Requests/GenerationRequests.cs`, `Responses/GenerationResponses.cs` — DTO AI mới.
- `src/AI/StoryPlatform.AI.Application/ContentGeneration/GenerateListeningInteractionHandler.cs`, `src/AI/StoryPlatform.AI.Domain/Enums/GenerationEnums.cs`, `src/AI/StoryPlatform.AI.Infrastructure/PromptCatalog/PromptTemplateProvider.cs` — prompt và schema.
- `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs`, `MediaReadinessService.cs` — audio và yêu cầu media.
- `src/Core/StoryPlatform.Application/Features/ReadingSessions/ChildListeningContent.cs`, `DTOs/ChildReadingDtos.cs`, `DTOs/ChildListeningDtos.cs`, `Services/ChildListeningService.cs`, `Services/ChildReadAloudService.cs`, `Services/ParentReadingFeedbackService.cs`.
- `src/Core/StoryPlatform.Application/Features/StoryReview/Services/StoryReviewService.Listening.cs`, `DTOs/ReviewDtos.cs`.
- `Database/Seed/60_listening_interactions.sql`, `61_listening_interaction_options.sql`.
- `docs/api/age-8-interactions-api-test-guide.html` — **mới** (Task 12, cần bạn duyệt; thư mục bị `.gitignore` chặn).

---

### Task 1: Tuổi 8 vào danh sách đã xây và gỡ các chỗ cứng theo tuổi

**Việc này làm gì (nói dễ hiểu):** Hệ thống hiện chỉ biết "tuổi đã có bộ tương tác" là 6 và 7, và một vài chỗ trong code đang viết cứng theo tuổi 6 hoặc 7. Task này đưa tuổi 8 vào danh sách, đồng thời gom các chỗ cứng thành một nơi dễ mở rộng, để các task sau chỉ cần thêm loại tương tác mà không phải sửa rải rác. Chưa có tương tác tuổi 8 nào được tạo trong task này.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningAgeCatalog.BuiltAges`, `ListeningAgeSets` (đã coi nhóm 6-8 gồm 6, 7, 8), `TargetAgesFor`, `AgeFor`.
- Cần thêm: `8` vào `BuiltAges`; đổi `MaxAge6PracticeAttempts` thành `MaxPracticeAttempts`; chỗ `ListeningInteractionGenerator` đặt tên snapshot và chỗ AI handler kiểm "loại thuộc tuổi nào" đọc từ một bảng tuổi → loại thay vì `if age == 6/7`.
- Còn thiếu / ngoài phạm vi: loại mới (Task 2), planner (Task 4), luật (Task 5).

**Files:**
- Modify: `src/Shared/StoryPlatform.Contracts/AI/Models/ListeningAgeCatalog.cs`
- Modify: `.../Services/ListeningInteractionGenerator.cs:98-100`, `src/AI/.../GenerateListeningInteractionHandler.cs:34-36,95-111,165-166`
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildReadAloudService.cs:25,208`
- Test: cập nhật các test đang khẳng định "tuổi 8 chưa có bộ" (tìm bằng `Grep "BuiltAges|TargetAgesFor|Age8|age 8"` trong `tests/`).

**Interfaces:**
- Produces: `ListeningAgeCatalog.BuiltAges == [6, 7, 8]`; `ChildReadAloudService.MaxPracticeAttempts = 3` (giữ `MaxAge6PracticeAttempts` làm alias `[Obsolete]` nếu có test cũ tham chiếu).

- [ ] **Step 1: Viết test thất bại** — `ListeningAgeSets.TargetAgesFor(AgeBand.Age_6_8)` trả `[6, 7, 8]`; `TargetAgesFor(AgeBand.Age_9_10)` vẫn rỗng; `HasOwnSet(8)` true, `HasOwnSet(9)` false.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3:** Sửa `BuiltAges` thành `{ 6, 7, 8 }`; thay hằng số và chỗ dùng; thay hai chuỗi `if` theo tuổi trong generator/handler bằng một hàm `SnapshotKey(int targetAge, ListeningInteractionType type)` và bảng `AllowedTypes[targetAge]` (tuổi 8 để rỗng tới Task 6).
- [ ] **Step 4:** Chạy `dotnet test --filter "FullyQualifiedName~Listening"`; sửa các test cũ mà kỳ vọng "tuổi 8 không có bộ" thành kỳ vọng "tuổi 8 có bộ rỗng cho tới khi planner có" (hoặc xóa nếu sai nghĩa). Kết quả mong đợi: PASS.

---

### Task 2: Mô hình dữ liệu cho năm loại mới

**Việc này làm gì (nói dễ hiểu):** Năm tương tác mới cần nơi lưu thứ mà bộ cũ chưa có: ai nói câu nào trong đoạn đối thoại, cảm xúc của mỗi bong bóng suy nghĩ, thẻ manh mối của câu suy luận, và bản ghi âm từng lượt đọc. Task này thêm các loại, vai trò và cột cần thiết cùng migration, chưa có logic nghiệp vụ.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteraction` (`CharacterName`, `Prompt`, `SourceQuote`, `Explanation`, `MatchFeedback`, `NoMatchFeedback`, `Hint`), `ListeningInteractionOption` (`Role`, `CorrectOrder`, `ImageDescription`, `Feedback`, `IsMatch`, `SourceQuote/Start/End`), `ListeningInteractionAttempt.AnswerText`, `ReadAloudRecording`.
- Cần thêm: enum `AlternateReading = 13, InferenceQuestion = 14, CauseEffect = 15, SceneTitle = 16, ThoughtBubble = 17`; `ListeningOptionRole.Clue = 3`; cột `Speaker` (≤ 60), `Emotion` (≤ 20) trên option; cột `ListeningInteractionOptionId` trên `ReadAloudRecording` và index mới.
- Còn thiếu / ngoài phạm vi: dữ liệu seed (Task 12); không áp migration lên DB thật.

**Files:**
- Modify: `src/Core/StoryPlatform.Domain/Enums/ListeningInteractionType.cs`, `ListeningOptionRole.cs`
- Modify: `src/Core/StoryPlatform.Domain/Entities/ListeningInteractionOption.cs`, `ReadAloudRecording.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/ReadAloudRecordingConfiguration.cs`, cấu hình option (tìm bằng `Grep "class ListeningInteractionOptionConfiguration"`)
- Create (EF sinh): `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddAge8ListeningInteractions.cs` và `.Designer.cs`, cập nhật `ApplicationDbContextModelSnapshot.cs`
- Test: `tests/StoryPlatform.UnitTests/Age8ListeningMigrationTests.cs` (mới, theo mẫu `Age7ListeningMigrationTests.cs`)

**Interfaces:**
- Produces: `ListeningInteractionOption.Speaker : string?`, `.Emotion : string?`; `ReadAloudRecording.ListeningInteractionOptionId : int?`; `ListeningOptionRole.Clue`.

- [ ] **Step 1: Viết test migration thất bại** (theo `Age7ListeningMigrationTests`): kiểm model snapshot có 3 cột mới, enum mới, và index `UX_read_aloud_recordings_session_practice` gồm 3 cột (phiên, tương tác, lượt) với filter còn nguyên.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3:** Thêm enum và cột; sửa cấu hình index:

```csharp
builder.HasIndex(x => new { x.ReadingSessionId, x.ListeningInteractionId, x.ListeningInteractionOptionId })
    .IsUnique()
    .HasFilter("\"ListeningInteractionId\" IS NOT NULL AND \"IsDeleted\" = false")
    .HasDatabaseName("UX_read_aloud_recordings_session_practice");
```

Lưu ý PostgreSQL coi `NULL` là khác nhau trong unique index, nên hai dòng cùng (phiên, tương tác) với `ListeningInteractionOptionId = NULL` sẽ **không** bị chặn trùng. Vì vậy thêm `NULLS NOT DISTINCT` (PostgreSQL 15+) bằng `.AreNullsDistinct(false)` nếu phiên bản Npgsql hỗ trợ, nếu không thì tạo hai index lọc: một cho `ListeningInteractionOptionId IS NULL`, một cho `IS NOT NULL`. Kiểm bằng test index trong migration test.
- [ ] **Step 4:** `dotnet ef migrations add AddAge8ListeningInteractions` (chỉ tạo file, **không** `database update`); xem lại SQL; chạy `dotnet test --filter "FullyQualifiedName~Age8ListeningMigration"`. Kết quả mong đợi: PASS.

---

### Task 3: Hàm thuần — cặp nguyên nhân, đoạn manh mối

**Việc này làm gì (nói dễ hiểu):** Hai việc nhỏ cần làm đúng và dễ test riêng: (1) kiểm một danh sách thẻ nguyên nhân–kết quả có ghép cặp hợp lệ không và ghép thành câu "Vì … nên …"; (2) tìm vị trí từng manh mối trong đoạn văn để tô sáng. Viết thành hàm không phụ thuộc cơ sở dữ liệu để test thật kỹ.

**Cần thêm / còn thiếu:**
- Đã có: mẫu hàm thuần `SentenceOrders`, `HotspotBox`.
- Cần thêm: `CausePairs` (kiểm 3 cặp, ghép câu), `ClueSpans` (tìm đoạn con, không trùng, đúng thứ tự).
- Còn thiếu / ngoài phạm vi: nối vào luật (Task 5) và chấm (Task 9).

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/CausePairs.cs`
- Create: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ClueSpans.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/CausePairsTests.cs`, `ClueSpansTests.cs`

**Interfaces:**
- Produces:
  - `public static class CausePairs { public const int PairCount = 3; public static bool IsValid(IReadOnlyList<(ListeningOptionRole Role, int? Pair)> cards); public static string Sentence(string cause, string effect); public static bool Matches(ListeningInteractionOption effect, ListeningInteractionOption cause); }`
  - `public static class ClueSpans { public static IReadOnlyList<(int Start, int End)>? Find(string passage, IReadOnlyList<string> clues); }` — trả `null` nếu có manh mối không nằm trong đoạn, trùng nhau hoặc chồng lấn.

- [ ] **Step 1: Viết test thất bại**

```csharp
public sealed class CausePairsTests
{
    private static (ListeningOptionRole, int?) C(ListeningOptionRole r, int p) => (r, p);

    [Fact]
    public void ThreeItemsAndThreeTargets_WithMatchingPairNumbers_AreValid() =>
        Assert.True(CausePairs.IsValid([C(ListeningOptionRole.Item, 1), C(ListeningOptionRole.Item, 2), C(ListeningOptionRole.Item, 3),
            C(ListeningOptionRole.Target, 3), C(ListeningOptionRole.Target, 1), C(ListeningOptionRole.Target, 2)]));

    [Fact]
    public void DuplicatedPairNumberOrWrongCounts_AreInvalid()
    {
        Assert.False(CausePairs.IsValid([C(ListeningOptionRole.Item, 1), C(ListeningOptionRole.Item, 1), C(ListeningOptionRole.Item, 3),
            C(ListeningOptionRole.Target, 1), C(ListeningOptionRole.Target, 2), C(ListeningOptionRole.Target, 3)]));
        Assert.False(CausePairs.IsValid([C(ListeningOptionRole.Item, 1), C(ListeningOptionRole.Target, 1)]));
    }

    [Fact]
    public void Sentence_JoinsCauseAndEffectInVietnamese() =>
        Assert.Equal("Vì trời mưa to nên Thỏ trú dưới gốc cây.", CausePairs.Sentence("Trời mưa to", "thỏ trú dưới gốc cây"));
}

public sealed class ClueSpansTests
{
    [Fact]
    public void FindsEachClueInOrderOfAppearance()
    {
        var spans = ClueSpans.Find("Thỏ run lẩy bẩy. Thỏ nghe tiếng sói hú. Thỏ nấp sau cây.", ["nghe tiếng sói hú", "run lẩy bẩy"]);
        Assert.Equal([(0, 16), (18, 36)].Length, spans!.Count);
        Assert.True(spans[0].Start < spans[1].Start);
    }

    [Fact]
    public void ClueMissingDuplicatedOrOverlapping_ReturnsNull()
    {
        Assert.Null(ClueSpans.Find("Thỏ run lẩy bẩy.", ["con sói"]));
        Assert.Null(ClueSpans.Find("Thỏ run lẩy bẩy.", ["run", "run"]));
        Assert.Null(ClueSpans.Find("Thỏ run lẩy bẩy.", ["run lẩy", "lẩy bẩy"]));
    }
}
```

- [ ] **Step 2: Chạy test, thấy thất bại (chưa có lớp).**
- [ ] **Step 3: Cài đặt**

```csharp
public static class CausePairs
{
    public const int PairCount = 3;

    public static bool IsValid(IReadOnlyList<(ListeningOptionRole Role, int? Pair)> cards)
    {
        var items = cards.Where(c => c.Role == ListeningOptionRole.Item).Select(c => c.Pair).ToArray();
        var targets = cards.Where(c => c.Role == ListeningOptionRole.Target).Select(c => c.Pair).ToArray();
        if (cards.Count != PairCount * 2 || items.Length != PairCount || targets.Length != PairCount) return false;
        var expected = Enumerable.Range(1, PairCount).Select(n => (int?)n).ToArray();
        return items.OrderBy(x => x).SequenceEqual(expected) && targets.OrderBy(x => x).SequenceEqual(expected);
    }

    public static bool Matches(ListeningInteractionOption effect, ListeningInteractionOption cause) =>
        effect.Role == ListeningOptionRole.Item && cause.Role == ListeningOptionRole.Target &&
        effect.CorrectOrder is not null && effect.CorrectOrder == cause.CorrectOrder;

    /// <summary>"Vì {lí do} nên {kết quả}." — lí do và kết quả viết thường chữ đầu, bỏ dấu chấm cuối.</summary>
    public static string Sentence(string cause, string effect) =>
        $"Vì {Lower(Trim(cause))} nên {Lower(Trim(effect))}.";

    private static string Trim(string s) => s.Trim().TrimEnd('.', '!', '?', ',', ';');
    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}

public static class ClueSpans
{
    public static IReadOnlyList<(int Start, int End)>? Find(string passage, IReadOnlyList<string> clues)
    {
        var spans = new List<(int Start, int End)>();
        foreach (var clue in clues)
        {
            var text = clue.Trim();
            var start = text.Length == 0 ? -1 : passage.IndexOf(text, StringComparison.Ordinal);
            if (start < 0) return null;
            spans.Add((start, start + text.Length));
        }
        var ordered = spans.OrderBy(s => s.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].Start < ordered[i - 1].End) return null;   // trùng hoặc chồng lấn
        return ordered;
    }
}
```

Lưu ý: `ClueSpansTests.FindsEachClueInOrderOfAppearance` ở Step 1 chỉ kiểm số lượng và thứ tự, không cố định chỉ số; nếu chỉ số tuyệt đối cần kiểm, tính bằng `IndexOf` trong test.
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~CausePairsTests|FullyQualifiedName~ClueSpansTests"`. Kết quả mong đợi: PASS.

---

### Task 4: Planner bộ tuổi 8

**Việc này làm gì (nói dễ hiểu):** Quyết định truyện này sẽ có tương tác nào của tuổi 8 và đặt mỗi tương tác sau đoạn nào: đọc luân phiên cần đoạn có thoại, các tương tác còn lại đặt rải trong truyện để bé không gặp liên tiếp. Truyện ngắn thì chỉ lấy đủ số tương tác theo số đoạn có thể neo.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionPlanner` (tuổi 6 và 7), `AssignZones`, `FindDialogueBlockIds`, `ListeningInteractionRules.IsEndOfStory`.
- Cần thêm: `CreateAge8`; bảng ưu tiên `AlternateReading → InferenceQuestion → SceneTitle → ThoughtBubble → CauseEffect`; nếu không có thoại thì thay `AlternateReading` bằng `InferenceQuestion` thứ hai (`Count = 2`); `AssignZones` biết loại tuổi 8: `AlternateReading` lấy vùng có thoại; `InferenceQuestion`, `CauseEffect`, `SceneTitle`, `ThoughtBubble` lấy vùng còn lại, `ThoughtBubble` lấy vùng muộn nhất (cao trào cảm xúc).
- Còn thiếu / ngoài phạm vi: luật nội dung (Task 5).

**Files:**
- Modify: `.../Services/ListeningInteractionPlanner.cs` (`Create`, thêm `CreateAge8`, mở rộng `AssignZones`)
- Modify: `.../Services/ListeningInteractionRules.cs` (`IsEndOfStory`: không loại tuổi 8 nào đặt cuối truyện)
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionPlannerTests.cs` (thêm ca tuổi 8; tìm tên file bằng `Glob`)

**Interfaces:**
- Consumes: `ListeningPlanItem(type, count)`, `StoryTextBlock`.
- Produces: `ListeningInteractionPlanner.Create(8, blocks)` trả plan có `TargetAge = 8`, tối đa 5 mục.

- [ ] **Step 1: Viết test thất bại** — (a) truyện 6 đoạn có thoại → 5 mục đúng thứ tự `AlternateReading, InferenceQuestion, SceneTitle, ThoughtBubble, CauseEffect`, mỗi mục một vùng khác nhau, vùng của `AlternateReading` chứa đoạn thoại; (b) truyện không thoại → `InferenceQuestion` Count = 2 và không có `AlternateReading`; (c) truyện 3 đoạn (2 chỗ neo) → tối đa 2 mục; (d) truyện 1 đoạn → plan rỗng.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt**

```csharp
private static ListeningInteractionPlan CreateAge8(IReadOnlyList<StoryTextBlock> blocks)
{
    var slots = Math.Max(0, blocks.Count - 1);
    if (slots == 0) return ListeningInteractionPlan.Empty;
    var dialogue = FindDialogueBlockIds(blocks);
    var order = new List<ListeningPlanItem>();
    if (dialogue.Count > 0) order.Add(new(ListeningInteractionType.AlternateReading, 1));
    order.Add(new(ListeningInteractionType.InferenceQuestion, dialogue.Count > 0 ? 1 : 2));
    order.Add(new(ListeningInteractionType.SceneTitle, 1));
    order.Add(new(ListeningInteractionType.ThoughtBubble, 1));
    order.Add(new(ListeningInteractionType.CauseEffect, 1));
    var items = new List<ListeningPlanItem>();
    var used = 0;
    foreach (var item in order)
    {
        if (used + item.Count > slots) break;
        items.Add(item);
        used += item.Count;
    }
    return new(slots, AssignZones(items, blocks, dialogue, 8), dialogue);
}
```

và trong `Create` thêm `8 => CreateAge8(blocks) with { TargetAge = 8 }`. Trong `AssignZones`: `AlternateReading` → `TakeFitting(b => dialogue.Contains(b.BlockId))`; `ThoughtBubble` → `TakeLatest()`; các loại tuổi 8 còn lại → `TakeEarliest()` lặp `Count` lần (như `Checkpoint`).
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ListeningInteractionPlanner"`. Kết quả mong đợi: PASS.

---

### Task 5: Luật kiểm tra bộ tuổi 8

**Việc này làm gì (nói dễ hiểu):** Mọi nội dung AI soạn phải qua một "bộ kiểm" trước khi tới người duyệt: câu trích có thật trong truyện không, số đáp án có đúng không, có từ chấm điểm kiểu "sai" không, manh mối có nằm trong đoạn không... Task này viết bộ kiểm riêng cho từng tương tác của tuổi 8, báo lỗi theo mã và tên trường như các tuổi trước để người duyệt và AI sinh lại biết sửa chỗ nào.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionRules.Validate` (khung chung, mã lỗi `SCHEMA_INVALID`, `NOT_GROUNDED`, `OPTION_COUNT_INVALID`, `MATCH_RULE_INVALID`, `LENGTH_LIMIT`, `GRADING_WORD`…), `AppearsBefore`, `IsInsideQuotes`, `HasDuplicateText`, `CountWords`, `ListeningRuleContext`.
- Cần thêm (`ListeningInteractionRules.Age8.cs`):
  - **AlternateReading:** 2–4 option; mỗi `Text` nằm nguyên văn trong một đoạn thoại trước điểm neo và trong ngoặc/gạch đầu dòng (dùng `IsInsideQuotes`); ≤ 12 từ; đúng 2 `Speaker` khác nhau, mỗi người ≥ 1 lượt; `Speaker` xuất hiện trong truyện trước điểm neo; `CorrectOrder` là 1..n liên tục và theo đúng thứ tự xuất hiện trong truyện.
  - **InferenceQuestion:** `SourceQuote` 2–3 câu (≤ 40 từ) nguyên văn trước điểm neo; `Prompt` kết thúc bằng `?`; đúng 3 option đáp án (`Role.None`), đúng 1 `IsMatch`, không trùng; đáp án đúng (≥ 3 từ) không nằm nguyên văn trong đoạn (mã `NOT_INFERENCE`); đúng 3 option `Role.Clue` với `ClueSpans.Find` khác null; `Explanation` 1–2 câu; không có từ chấm điểm.
  - **CauseEffect:** `CausePairs.IsValid`; mỗi thẻ ≤ 10 từ, có `SourceQuote` nguyên văn trước điểm neo; hai thẻ cùng cặp có câu trích khác nhau; không trùng nội dung giữa các thẻ cùng vai.
  - **SceneTitle:** đúng 3 option, đúng 1 `IsMatch`; mỗi tiêu đề ≤ 8 từ, không trùng; mỗi option có `Feedback`; `Explanation` bắt buộc; `SourceStart/End` bao phủ đoạn được đặt tên (≥ 40 ký tự, trước điểm neo).
  - **ThoughtBubble:** `CharacterName` xuất hiện trong truyện trước điểm neo; đúng 3 option, đúng 1 `IsMatch`, mỗi bong bóng ≤ 12 từ và mỗi option có `Emotion` thuộc danh sách cố định; 3 cảm xúc khác nhau; `SourceQuote` nguyên văn trước điểm neo làm căn cứ cho tình huống.
- Còn thiếu / ngoài phạm vi: kiểm "không nói thẳng" ngoài heuristic (người duyệt và prompt lo).

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionRules.Age8.cs`
- Modify: `.../Services/ListeningInteractionRules.cs` (switch theo `Type`, thêm danh sách `Emotions`, từ giới hạn), `.../Models/ListeningInteractionModels.cs` (thêm `Speaker`, `Emotion`, `Role` vào `ListeningInteractionDraftOption` nếu chưa có)
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionRulesAge8Tests.cs` (mới, theo mẫu `ListeningInteractionRulesAge7Tests.cs`)

**Interfaces:**
- Consumes: Task 3 (`CausePairs`, `ClueSpans`), Task 2 (cột và vai trò mới).
- Produces: `ListeningInteractionRules.Validate(...)` trả đúng mã lỗi ở bảng dưới.

Bảng mã lỗi mới: `NOT_INFERENCE` (đáp án đúng nằm nguyên văn trong đoạn), `CLUE_INVALID` (manh mối thiếu/trùng/ngoài đoạn), `SPEAKER_INVALID` (đã có, dùng lại cho người nói), `PAIR_INVALID` (cặp nguyên nhân không hợp lệ), `EMOTION_INVALID` (cảm xúc ngoài danh sách hoặc trùng).

- [ ] **Step 1: Viết test thất bại** — mỗi loại một nhóm: một ca hợp lệ (không có lỗi) và các ca vi phạm từng luật ở trên (mỗi ca kỳ vọng đúng `Code` và `Field`, ví dụ `Code = "CLUE_INVALID", Field = "options"`), kèm ca biên: lượt thoại thứ 5, đoạn 4 câu, đáp án đúng nằm trong đoạn, hai cặp cùng số, cảm xúc "giận dữ" (không có trong danh sách).
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** — thêm `case` cho 5 loại trong `Validate`, mỗi loại gọi một hàm `ValidateXxxAge8` trong file mới; dùng lại `AppearsBefore`, `IsInsideQuotes`, `CountWords`, `HasDuplicateText`. Khung hàm:

```csharp
private static void ValidateInferenceQuestion(ListeningInteractionDraft item, int index, ListeningRuleContext context,
    List<ListeningValidationError> errors)
{
    var passage = item.SourceQuote;
    if (string.IsNullOrWhiteSpace(passage) || !context.Content.Contains(passage, StringComparison.Ordinal) ||
        context.Content.IndexOf(passage, StringComparison.Ordinal) + passage.Length > item.AnchorOffset)
        errors.Add(new("NOT_GROUNDED", index, "sourceQuote"));
    else if (CountWords(passage) > 40 || SentenceCount(passage) is < 2 or > 3)
        errors.Add(new("LENGTH_LIMIT", index, "sourceQuote"));
    if (string.IsNullOrWhiteSpace(item.Prompt) || !item.Prompt.TrimEnd().EndsWith('?'))
        errors.Add(new("SCHEMA_INVALID", index, "question"));

    var answers = item.Options.Where(o => o.Role == ListeningOptionRole.None).ToList();
    var clues = item.Options.Where(o => o.Role == ListeningOptionRole.Clue).ToList();
    if (answers.Count != 3 || HasDuplicateText(answers)) errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
    if (answers.Count(o => o.IsMatch) != 1) errors.Add(new("MATCH_RULE_INVALID", index, "options"));
    var correct = answers.FirstOrDefault(o => o.IsMatch);
    if (correct is not null && passage is not null && CountWords(correct.Text) >= 3 &&
        passage.Contains(correct.Text.Trim(), StringComparison.OrdinalIgnoreCase))
        errors.Add(new("NOT_INFERENCE", index, "options"));
    if (clues.Count != 3 || passage is null || ClueSpans.Find(passage, clues.Select(c => c.Text).ToList()) is null)
        errors.Add(new("CLUE_INVALID", index, "options"));
    if (string.IsNullOrWhiteSpace(item.Explanation)) errors.Add(new("SCHEMA_INVALID", index, "explanation"));
}
```

(Tên `HasDuplicateText` hiện nhận `item`; thêm overload nhận danh sách option nếu cần. Bốn hàm còn lại viết theo cùng khung, mỗi hàm bám đúng danh sách luật ở mục "Cần thêm".)
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ListeningInteractionRulesAge8"` rồi `--filter "FullyQualifiedName~ListeningInteractionRules"` để chắc tuổi 6 và 7 không đổi. Kết quả mong đợi: PASS.

---

### Task 6: Hợp đồng AI và prompt cho bộ tuổi 8

**Việc này làm gì (nói dễ hiểu):** Dịch vụ AI cần biết cách soạn từng loại tương tác tuổi 8: nó nhận danh sách đoạn truyện và vùng được phép neo, rồi trả JSON đúng khuôn. Task này thêm khuôn JSON (schema) và lời nhắc (prompt) cho năm loại, thêm vào bảng "loại nào thuộc tuổi nào", và chuyển JSON AI thành bản nháp trong Core để Task 5 kiểm.

**Cần thêm / còn thiếu:**
- Đã có: `GenerateListeningInteractionHandler` (schema, prompt, `PromptTypeFor`, kiểm tuổi/loại), `ListeningGenerationContract`, `PromptTemplateProvider`, `ListeningInteractionAiMapper`, `ListeningInteractionGenerator`.
- Cần thêm: 5 `PromptType` mới (`ListeningAlternateReading`, `ListeningInferenceQuestion`, `ListeningCauseEffect`, `ListeningSceneTitle`, `ListeningThoughtBubble`); schema từng loại (trường `lines[{speaker,text}]`; `passage`, `question`, `answers[]`, `clues[]`, `explanation`; `effects[]` + `causes[]` (mỗi thẻ có `text`, `sourceQuote`, `pair`); `titles[]` kèm `feedback`; `characterName`, `bubbles[]` kèm `emotion`); mapper dựng option `Speaker/Emotion/Role.Clue/CorrectOrder` và tính `SourceStart/End`; `AllowedTypes[8]` = năm loại mới (Task 1).
- Còn thiếu / ngoài phạm vi: gọi Gemini thật (cần khóa).

**Files:**
- Modify: `src/Shared/StoryPlatform.Contracts/AI/Models/StoryContentModels.cs`, `Requests/GenerationRequests.cs`, `Responses/GenerationResponses.cs`
- Modify: `src/AI/StoryPlatform.AI.Domain/Enums/GenerationEnums.cs`, `src/AI/StoryPlatform.AI.Application/ContentGeneration/GenerateListeningInteractionHandler.cs`, `src/AI/StoryPlatform.AI.Infrastructure/PromptCatalog/PromptTemplateProvider.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ListeningInteractions/Services/ListeningInteractionAiMapper.cs`, `ListeningInteractionGenerator.cs`
- Test: `tests/StoryPlatform.AI.UnitTests/.../ListeningHttpContractTests.cs` (thêm ca tuổi 8), `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionAiMapperTests.cs`

**Interfaces:**
- Consumes: Task 5 (luật), Task 2 (cột).
- Produces: yêu cầu `InteractionType` ∈ `AlternateReading | InferenceQuestion | CauseEffect | SceneTitle | ThoughtBubble` với `TargetAge = 8`; bản nháp có `Options[*].Speaker/Emotion/Role/CorrectOrder` đúng như luật.

Nội dung prompt (tiếng Anh, theo mẫu tuổi 7), điểm chính cho từng loại:
- **AlternateReading:** chọn 2–4 lời thoại liên tiếp của đúng hai nhân vật từ **một** đoạn thoại trong vùng cho phép; `text` nguyên văn trong ngoặc/gạch đầu dòng, ≤ 12 từ; `speaker` là tên nhân vật xuất hiện trong truyện; không thêm lời; có `matchFeedback` khen cả đoạn.
- **InferenceQuestion:** `passage` là 2–3 câu liên tiếp nguyên văn, ≤ 40 từ, chứa đúng 3 manh mối là cụm nguyên văn; câu hỏi dạng "Vì sao…?" mà đoạn **không nói thẳng** đáp án; 3 đáp án, 1 đúng, đáp án đúng viết lại bằng lời khác, không chép nguyên cụm từ đoạn; `explanation` 1–2 câu nhắc lại manh mối.
- **CauseEffect:** 3 cặp (kết quả, lí do) từ truyện vừa nghe, mỗi thẻ ≤ 10 từ, có `sourceQuote` nguyên văn; thẻ lí do viết sao cho ghép được "Vì {lí do} nên {kết quả}"; các cặp khác nhau về nội dung.
- **SceneTitle:** 3 tiêu đề ≤ 8 từ cho đoạn vừa nghe: 1 bao quát cả cảnh, 2 chỉ nói một chi tiết có thật trong cảnh; mỗi tiêu đề có `feedback` (vì sao bao quát / vì sao chỉ nói một chi tiết); có `explanation`.
- **ThoughtBubble:** chọn một nhân vật và một tình huống trong đoạn vừa nghe; 3 suy nghĩ ngôi thứ nhất ≤ 12 từ với 3 cảm xúc khác nhau chọn trong danh sách cố định; đúng 1 suy nghĩ hợp với tình huống; có `sourceQuote` làm căn cứ.
- Mọi loại: ngôn ngữ thân thiện với trẻ 8 tuổi, không từ chấm điểm ("sai", "điểm"), không nội dung ngoài truyện.

- [ ] **Step 1: Viết test thất bại** — (a) schema từng loại có đủ trường bắt buộc (`required`) và `enum` cảm xúc; (b) handler từ chối loại của tuổi khác với `targetAge = 8` (ví dụ `PictureSearch`, `Checkpoint`) và từ chối loại tuổi 8 với tuổi 6/7; (c) `PromptTypeFor("InferenceQuestion", 8)` trả `PromptType.ListeningInferenceQuestion`; (d) mapper: JSON mẫu của mỗi loại → bản nháp có option đúng vai trò, `CorrectOrder` cặp, `SourceStart/End` của manh mối nằm trong đoạn.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** schema, prompt, mapper theo danh sách trên.
- [ ] **Step 4:** `dotnet test tests/StoryPlatform.AI.UnitTests` và `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ListeningInteractionAiMapper|FullyQualifiedName~ListeningInteractionGenerator"`. Kết quả mong đợi: PASS.

---

### Task 7: Audio và yêu cầu media cho năm loại mới

**Việc này làm gì (nói dễ hiểu):** Mỗi tương tác tuổi 8 cần audio sinh sẵn: lời hướng dẫn, câu hỏi, lời từng nhân vật, các đáp án, câu giải thích, câu "Vì … nên …". Task này khai báo danh sách audio cần có cho từng loại và điều kiện để tương tác được coi là "đủ media" (chưa đủ thì ẩn với bé), để worker tạo audio và kiểm tra giống các tuổi trước. Tương tác nào bé nhìn chữ theo giọng đọc thì được bù mốc từng chữ.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningAudioParts.For`, `FixedLine`, `HighlightKeys`, `ListeningMediaRequirements.IsUsable/FindAudio`, tạo audio ở `MediaGenerationService.ListeningMedia.cs`, `SttAlignedTtsProvider` + bù mốc cho phần `HighlightKeys`.
- Cần thêm: khóa audio mới `Explanation = "explanation"`, `Chain = "chain"`; phần theo loại:
  - `AlternateReading`: instruction (câu hướng dẫn cố định, ví dụ "Em chọn một bạn rồi cùng đọc lời của bạn ấy nhé!"), `match` (khen), `unclear` (cố định), và với **mỗi lượt** audio `text` của lượt đó (`OptionId = lượt`, có mốc từng chữ).
  - `InferenceQuestion`: `text` (đoạn, có mốc), `prompt` (câu hỏi, có mốc), option-level `text` cho 3 đáp án, `explanation`, `match`, `retryLook` (cố định "Nhìn kỹ lại nhé.").
  - `CauseEffect`: option-level `text` cho 6 thẻ, `chain` cho mỗi thẻ kết quả (`OptionId = Item`, text = `CausePairs.Sentence(cause, effect)`), `match`, instruction.
  - `SceneTitle`: `prompt`, option-level `text` cho 3 tiêu đề, option-level `feedback`, `explanation`, `match`.
  - `ThoughtBubble`: `prompt` ("{CharacterName} đang nghĩ gì?"), option-level `text` cho 3 bong bóng, `match`, `retryLookClose` (cố định "Nhìn lại tranh nhé.").
  - Mốc từng chữ cũng áp dụng cho option-level `text` của `AlternateReading` (hiện điều kiện chỉ cho `OptionId is null`).
  - `ListeningMediaRequirements`: `ThoughtBubble` yêu cầu tranh minh họa dùng được của đoạn neo (readiness báo `picture` nếu thiếu).
- Còn thiếu / ngoài phạm vi: chất lượng giọng đọc (kiểm thủ công).

**Files:**
- Modify: `.../Services/ListeningAudioParts.cs`, `.../Services/ListeningMediaRequirements.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs` (điều kiện gán `WordTimings`), `MediaGenerationService.WordTimingBackfill.cs` (điều kiện chọn audio cần bù mốc), `MediaReadinessService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningAudioPartsTests.cs` (thêm ca tuổi 8), `MediaGenerationServiceTests.Listening.cs`, `MediaGenerationServiceTests.WordTimingBackfill.cs`

**Interfaces:**
- Produces: `ListeningAudioParts.Explanation`, `ListeningAudioParts.Chain`; `ListeningAudioParts.For(interaction, options)` trả đúng danh sách ở trên cho 5 loại; quy tắc mốc chữ dùng chung `ListeningAudioParts.NeedsWordTimings(ListeningInteraction, ListeningAudioPart)`.

- [ ] **Step 1: Viết test thất bại** — mỗi loại: danh sách `PartKey` và `OptionId` kỳ vọng; `AlternateReading` có đúng n phần `text` option-level; `CauseEffect` có `chain` cho đúng 3 thẻ kết quả với văn bản "Vì … nên …"; `NeedsWordTimings` true cho `text/prompt` của Suy luận và `text` của lượt thoại, false cho `match/explanation`; `ThoughtBubble` thiếu tranh thì không "usable".
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** thêm các `case` trong `For`; thay hai chỗ kiểm `HighlightKeys` rải rác (`ListeningMedia.cs:100`, `WordTimingBackfill.cs`) bằng `NeedsWordTimings`; bù mốc chỉ chọn audio mà hàm này trả true (giữ điều kiện DB hiện có, thêm khóa `text` option-level của `AlternateReading` vào truy vấn).
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ListeningAudioParts|FullyQualifiedName~MediaGenerationServiceTests"`. Kết quả mong đợi: PASS.

---

### Task 8: Gửi cho bé đúng bộ tuổi 8 (DTO nội dung)

**Việc này làm gì (nói dễ hiểu):** Khi bé mở truyện, màn hình cần dữ liệu để vẽ từng tương tác: đoạn văn, các thẻ, các lượt thoại, tranh. Task này làm API nội dung trả đúng bộ của tuổi 8 cho bé 8 tuổi, đã xáo thẻ nhưng không lộ đáp án đúng hay manh mối, và ẩn tương tác nào chưa đủ media.

**Cần thêm / còn thiếu:**
- Đã có: `ChildListeningContent.Build/ToDto`, xáo cố định theo phiên (`Shuffle`, `ShuffleItems`), `StoryPicture`, ký URL audio, `AudioWordTimings`.
- Cần thêm:
  - `ReadingInteractionDto`: `Passage` (Suy luận), `Lines[]` (`id`, `speaker`, `text`, `audioUrl`, thứ tự cố định của truyện) và `Speakers[]` (hai tên) cho Đọc luân phiên, `Effects[]`/`Causes[]` (đã xáo, không có số cặp) cho Nối nguyên nhân, `SceneImageUrl` cho Đặt tên cảnh và Bong bóng, `Answers[]` dùng lại `Options` cho ba loại trắc nghiệm.
  - Quy tắc "thấy khi nào": tuổi 8 chỉ thấy 5 loại mới; ẩn khi thiếu audio bắt buộc; `ThoughtBubble` ẩn khi không có tranh dùng được; `InferenceQuestion` ẩn khi có manh mối không còn nằm trong đoạn.
  - `attemptedInSession`: Đọc luân phiên = đã có attempt chọn nhân vật; Nối nguyên nhân = đã xong cả 3 thẻ (nối đúng hoặc nối sẵn); ba loại trắc nghiệm = đã đúng hoặc đã hết lượt chọn.
- Còn thiếu / ngoài phạm vi: nộp trả lời (Task 9–10).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/ChildListeningContent.cs`, `DTOs/ChildReadingDtos.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildListeningContentTests.cs` (thêm helper `Add8`, nhắc: fixture mặc định bé 8 tuổi)

**Interfaces:**
- Consumes: Task 7 (audio parts), Task 2 (cột/vai trò).
- Produces: JSON không chứa `isMatch`, `correctOrder`/số cặp, manh mối, `emotion` của đáp án đúng; mỗi tương tác tuổi 8 có `targetAge = 8`.

- [ ] **Step 1: Viết test thất bại** — mỗi loại: bé 8 tuổi nhận DTO đúng trường; bé 7 tuổi không thấy; không có chuỗi nào trong JSON tuần tự hóa của DTO chứa `isMatch`, `clue`, `CorrectOrder`, `emotion` (kiểm bằng cách serialize và tìm chuỗi); thứ tự xáo ổn định giữa hai lần gọi cùng phiên; thiếu một audio bắt buộc → `interaction = null`.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** nhánh `ToDto` cho 5 loại.
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ChildListeningContentTests"`. Kết quả mong đợi: PASS.

---

### Task 9: Nộp câu trả lời — ba loại trắc nghiệm và Nối nguyên nhân

**Việc này làm gì (nói dễ hiểu):** Khi bé chọn đáp án hoặc kéo nối một cặp, máy chủ chấm, ghi lại cho phụ huynh và trả kết quả để màn hình phản hồi (khen, tô manh mối, giải thích, bật thẻ lại, hoặc chỉ đáp án sau khi hết lượt). Không hiện điểm hay chữ "sai".

**Cần thêm / còn thiếu:**
- Đã có: `ChildListeningService.SubmitAttemptAsync/AnswerAsync`, `Age7Result`, `AllowsRetry`, `MaxChoiceAttempts = 2`, ký URL audio phản hồi, khóa giao dịch theo truyện.
- Cần thêm:
  - `SubmitListeningAttemptRequestDto`: thêm `Speaker : string?` (Đọc luân phiên).
  - `ListeningAttemptResultDto`: thêm `ClueSpans[]` (Suy luận), `Explanation`, `ExplanationAudioUrl`, `RevealEmotion` (Bong bóng), `PairResults`/`MatchedCauseOptionId` và `ChainSentences[]` (Nối nguyên nhân).
  - **InferenceQuestion / SceneTitle / ThoughtBubble:** một option trong request; dùng đường `AllowsRetry` (tối đa 2 lần). Đúng: `matched`, phản hồi + (Suy luận) `clueSpans` + `explanation`. Sai lần 1: `canRetry` + (Suy luận) `clueSpans`, (Đặt tên) feedback của tiêu đề bé chọn, (Bong bóng) câu cố định "Nhìn lại tranh nhé."; sai lần 2: `revealed`, `correctOptionId`, giải thích, (Bong bóng) `revealEmotion`.
  - **CauseEffect:** mỗi request = một cặp (`OptionId` = thẻ `Item`, `TargetOptionId` = thẻ `Target`); đúng khi `CausePairs.Matches`; ghi attempt (`SelectedOptionId` = thẻ kết quả, `AnswerText` = `{"target":id}`). Thẻ kết quả đã nối đúng hoặc đã nối sẵn thì gửi lại trả kết quả cũ không ghi thêm; sai 2 lần cùng thẻ → `revealed` kèm `matchedCauseOptionId`. Khi cả 3 thẻ xong: `completed = true` và `chainSentences` (3 câu "Vì … nên …" kèm URL audio đã ký).
- Còn thiếu / ngoài phạm vi: Đọc luân phiên (Task 10).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildListeningService.cs` (`AnswerAsync`, thêm `Age8Result`), `DTOs/ChildListeningDtos.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildListeningServiceTests.cs` (thêm ca tuổi 8)

**Interfaces:**
- Consumes: Task 3 (`CausePairs`, `ClueSpans`), Task 7 (audio), Task 8 (DTO).
- Produces: `ListeningAttemptResultDto.ClueSpans : IReadOnlyList<ClueSpanDto>?`, `.Explanation`, `.ExplanationAudioUrl`, `.RevealEmotion`, `.ChainSentences : IReadOnlyList<ChainSentenceDto>?` (`EffectOptionId`, `Text`, `AudioUrl`).

- [ ] **Step 1: Viết test thất bại** — Suy luận: đúng lần 1; sai→đúng; sai→sai (lộ đáp án, có giải thích); sai lần 1 trả `clueSpans` nhưng không trả `correctOptionId`; Đặt tên cảnh: feedback của tiêu đề đã chọn khi sai; Bong bóng: `revealEmotion` chỉ khi đúng hoặc lộ đáp án; Nối nguyên nhân: 3 cặp đúng liên tiếp → `completed` và 3 câu; cặp sai bật lại, sai lần 2 thì nối sẵn; gửi lại thẻ đã xong không thêm dòng attempt; `OptionId` là thẻ `Target` hoặc `TargetOptionId` là thẻ `Item` → 400 `LISTENING_ANSWER_INVALID`; option của tương tác khác → 400; bé tuổi khác → 404 (đã có ở `LoadAsync`).
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** thêm `case` trong `switch (interaction.Type)` và `Age8Result`; mở rộng điều kiện `finished` cho `CauseEffect` (đã xong khi đủ 3 thẻ kết quả đúng hoặc nối sẵn).
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ChildListeningServiceTests"`. Kết quả mong đợi: PASS.

---

### Task 10: Đọc luân phiên — chọn nhân vật và chấm mềm từng lượt

**Việc này làm gì (nói dễ hiểu):** Bé chọn một nhân vật, hệ thống đọc lời nhân vật kia, tới lượt bé thì bé bấm mic đọc lời của mình. Hệ thống đối chiếu từng chữ (rõ thì xanh, chưa rõ thì vàng), cho đọc lại tối đa 2 lần mỗi lượt, và chỉ lưu kết quả cho phụ huynh, không có điểm hay chữ "sai". Có nút "Bỏ qua".

**Cần thêm / còn thiếu:**
- Đã có: `ChildReadAloudService.CheckPracticeAsync/ScoreAsync` (chấm mềm, giới hạn 3 lần, telemetry không chứa lời bé), bản ghi `ReadAloudRecording`, endpoint `POST /api/v1/child/reading/sessions/{sessionId}/read-aloud-practice/{interactionId}`.
- Cần thêm:
  - Chọn nhân vật: `POST …/attempts` với `{ "speaker": "<tên>" }`; tên phải thuộc hai người nói của tương tác (nếu không: 400 `LISTENING_ANSWER_INVALID`); ghi attempt (`AnswerText = speaker`, `IsMatch = null`), gửi lại trả kết quả cũ và **không đổi** nhân vật.
  - Chấm một lượt: thêm tham số `lineId` (id option của lượt) vào `ReadAloudRequest` và endpoint (form field `LineId` hoặc query `?lineId=`, chọn theo cách `Audio` đang được nhận trong controller); `CheckPracticeAsync` cho `AlternateReading`: yêu cầu đã chọn nhân vật (nếu chưa: 409 `LISTENING_SPEAKER_NOT_CHOSEN`), lượt phải thuộc nhân vật bé đã chọn (nếu không: 404), `target = Text của lượt`; chấm mềm như `RolePlay`; message "Mình nghe chưa rõ, mình đọc lại nhé." khi còn chữ chưa rõ, ngược lại `interaction.MatchFeedback`.
  - Giới hạn **3 lần mỗi lượt**: telemetry thêm khóa `optionId`; `IsPracticeResultFor(payload, interactionId, optionId?)` đếm theo cặp; quá giới hạn: 409 `LISTENING_VOICE_RETRY_LIMIT`.
  - Bản ghi lần đọc gần nhất lưu theo lượt (`ListeningInteractionOptionId`, Task 2).
  - Phụ huynh: `ParentReadingFeedbackService` hiển thị lượt nào bé đã đọc, trạng thái từng chữ, chữ chưa rõ (không có lời bé nói).
- Còn thiếu / ngoài phạm vi: hoạt hình sáng chữ của lượt (frontend).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildReadAloudService.cs`, `ChildListeningService.cs` (chọn nhân vật), `DTOs/ChildReadingDtos.cs` (`ReadAloudRequest.LineId`, `SubmitListeningAttemptRequestDto.Speaker`), `Services/ParentReadingFeedbackService.cs`
- Modify: controller nhận multipart (tìm bằng `Grep "read-aloud-practice"` trong `src/Core/StoryPlatform.Api`)
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildReadAloudServiceTests.cs`, `ChildListeningServiceTests.cs`, `ParentReadingFeedbackServiceTests.cs`

**Interfaces:**
- Consumes: Task 2 (cột bản ghi), Task 7 (audio lượt thoại), Task 9 (khung `AnswerAsync`).
- Produces: `ReadAloudRequest.LineId : int?`; `ReadAloudResultDto.LineId : int?`; mã lỗi `LISTENING_SPEAKER_NOT_CHOSEN`.

- [ ] **Step 1: Viết test thất bại** — chọn nhân vật hợp lệ/không hợp lệ/gửi lại; chấm lượt của nhân vật đã chọn thành công; lượt của nhân vật kia → 404; chưa chọn nhân vật → 409 và **không** gọi nhận dạng, không upload; lần chấm thứ 3 của một lượt vẫn được, thứ 4 → 409 `LISTENING_VOICE_RETRY_LIMIT`, nhưng lượt khác vẫn chấm được; tắt giọng nói → 403; mỗi lượt có bản ghi riêng (kiểm trên `ReadAloudRecording`); phụ huynh thấy trạng thái chữ từng lượt, không có lời bé nói.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** như mục "Cần thêm".
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ChildReadAloudServiceTests|FullyQualifiedName~ChildListeningServiceTests|FullyQualifiedName~ParentReadingFeedbackServiceTests"`. Kết quả mong đợi: PASS.

---

### Task 11: Người duyệt, phụ huynh và readiness bộ tuổi 8

**Việc này làm gì (nói dễ hiểu):** Người duyệt phải thấy riêng "Bộ 8 tuổi", xem và sửa từng tương tác (lượt thoại, manh mối, thẻ cặp, cảm xúc), và hệ thống kiểm lại luật tuổi 8 mỗi lần sửa. Phụ huynh xem được mọi lựa chọn của bé, kể cả từng cặp nối. Truyện chỉ qua bước media khi đủ audio và tranh cho cả ba bộ tuổi.

**Cần thêm / còn thiếu:**
- Đã có: `StoryReviewService.Listening` (xem/sửa/hoàn tất, `ageSets`, `plan`, `zones`), `ListeningReadiness`, `ParentReadingFeedbackService.listeningInteractions`, ràng buộc `HotspotJson` bị xóa khi đổi nguồn.
- Cần thêm: DTO review cho `speaker`, `emotion`, `role`, thẻ manh mối; cho phép sửa các trường đó (kèm mã `FIELD_NOT_EDITABLE`/`LISTENING_OPTION_NOT_FOUND` như cũ) và chạy lại `ListeningInteractionRules` của tuổi 8; sửa `SourceQuote` của Suy luận hoặc nội dung thẻ manh mối → kiểm lại `ClueSpans`, đổi nội dung thẻ/tiêu đề/lượt thoại → tắt media cũ và bỏ xác nhận duyệt (như tuổi 7); `ListeningReadiness`: bộ tuổi 8 phải đủ 5 loại (hoặc đủ theo plan), đủ audio, `ThoughtBubble` có tranh; `complete` yêu cầu cả bộ 6, 7, 8 hợp lệ. Phụ huynh: `choices[]` có `pairs` cho Nối nguyên nhân (`effectOptionId`, `causeOptionId`, `isMatch`), `speaker` và `lines[]` (kèm trạng thái chữ) cho Đọc luân phiên, `selectedOptionId` cho ba loại trắc nghiệm.
- Còn thiếu / ngoài phạm vi: giao diện (frontend).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/StoryReview/Services/StoryReviewService.Listening.cs`, `DTOs/ReviewDtos.cs`, `.../MediaGeneration/Services/MediaReadinessService.cs`, `.../ReadingSessions/Services/ParentReadingFeedbackService.cs`
- Test: `tests/StoryPlatform.UnitTests/StoryReviewListeningAge8Tests.cs` (mới, theo mẫu `StoryReviewListeningAge7Tests.cs`), `ParentReadingFeedbackServiceTests.cs`, test readiness hiện có

**Interfaces:**
- Consumes: Tasks 2, 5, 7, 9, 10.
- Produces: `GET /api/v1/stories/{id}/review/listening` có `ageSets` tuổi 8 với 5 item; `PUT …/listening/{id}` nhận `speaker`, `emotion`, `role`, `options[*]`; `POST …/listening/complete` trả 409 `LISTENING_PACKAGE_INVALID` khi thiếu hoặc sai bất kỳ bộ nào.

- [ ] **Step 1: Viết test thất bại** — xem bộ tuổi 8; sửa hợp lệ từng loại; mỗi luật tuổi 8 bị vi phạm → 400 `LISTENING_TEXT_INVALID:<mã>@<trường>`; sửa manh mối làm manh mối ra ngoài đoạn → 400 `CLUE_INVALID`; sửa chữ tiêu đề → media cũ bị tắt; `complete` thiếu bộ tuổi 8 → 409; phụ huynh xem được từng cặp và từng lượt đọc.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt.**
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~StoryReview|FullyQualifiedName~ParentReadingFeedback|FullyQualifiedName~MediaReadiness"`. Kết quả mong đợi: PASS.

---

### Task 12: Seed và trang hướng dẫn test API tuổi 8 (cần bạn duyệt trước khi tạo file)

**Việc này làm gì (nói dễ hiểu):** Nạp sẵn bộ tương tác tuổi 8 cho truyện mẫu "Thỏ con đi lạc" để thử ngay mà không cần chạy AI, và làm trang HTML hướng dẫn test API (giống trang tuổi 6 và 7) với các tình huống đúng, lỗi và biên.

**Cần thêm / còn thiếu:**
- Đã có: seed tuổi 6 (interaction 920–924) và tuổi 7 (930–934, thẻ 930–941); `SeedDataAlignmentTests.ListeningSeed_PassesTheRulesOfItsAge`; mẫu trang `docs/api/_template-api-test-guide.html`.
- Cần thêm: 5 dòng `listening_interactions` (id kế tiếp **935–939**) và các dòng `listening_interaction_options` (id kế tiếp **942** trở đi) cho truyện 900: lượt thoại giữa hai nhân vật có thật trong nội dung seed của truyện 900, đoạn suy luận + 3 manh mối + 3 đáp án, 3 cặp nguyên nhân, 3 tiêu đề, 3 bong bóng kèm cảm xúc; viết nội dung bám **đúng** văn bản truyện 900 trong seed (đọc `Database/Seed` để chép nguyên văn, không tự bịa), rồi mở rộng test `ListeningSeed_PassesTheRulesOfItsAge` cho tuổi 8. Trang `docs/api/age-8-interactions-api-test-guide.html` dựng từ template (CLAUDE.md mục 13): các bước sinh/sinh lại tuổi 8, nội dung cho bé tuổi 8 (đặt `Age = 8`), nộp từng loại, đọc luân phiên (chọn nhân vật, chấm từng lượt, giới hạn 3 lần/lượt), duyệt/phụ huynh; localStorage `age8-interactions-guide-vars/checks`.
- Còn thiếu / ngoài phạm vi: media thật (audio, tranh) vẫn do worker tạo; trang chỉ ghi điều kiện cần.

**Files:**
- Modify: `Database/Seed/60_listening_interactions.sql`, `61_listening_interaction_options.sql`
- Modify: `tests/StoryPlatform.UnitTests/SeedDataAlignmentTests.cs`
- **Create (cần bạn duyệt):** `docs/api/age-8-interactions-api-test-guide.html` — trang hướng dẫn test API và test case cho tuổi 8, dựng từ `docs/api/_template-api-test-guide.html`; thư mục `docs/api/` bị `.gitignore` chặn nên cần `git add -f` nếu muốn đưa vào repo.

**Interfaces:**
- Consumes: mọi task trên.
- Produces: seed idempotent như seed hiện có; trang test mở được trong trình duyệt không lỗi console.

- [ ] **Step 1: Viết test thất bại** — mở rộng `ListeningSeed_PassesTheRulesOfItsAge` cho tuổi 8 (thất bại vì chưa có dòng seed).
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3:** Viết seed theo văn bản truyện 900; chạy lại test cho tới khi `ListeningInteractionRules` không báo lỗi nào.
- [ ] **Step 4:** Sau khi bạn duyệt tạo file: dựng trang từ template (chỉ sửa phần dữ liệu và khối chung như CLAUDE.md mục 13), tính kết quả mong đợi từ seed và code thật; mở trong trình duyệt: không lỗi console, đếm API/test case đúng, vài request JSON dựng ra đúng tài khoản/URL/body.
- [ ] **Step 5:** Chạy toàn bộ: `dotnet build StoryPlatform.sln` và `dotnet test StoryPlatform.sln`. Kết quả mong đợi: 0 lỗi build, mọi test đạt (trừ các test Postgres bị skip sẵn).

---

## Đối chiếu spec (tự rà)

| Yêu cầu trong tài liệu (mục 8 tuổi) | Task |
|---|---|
| Đọc luân phiên: chọn nhân vật, hệ thống đọc lời nhân vật còn lại, lượt bé sáng lên, mic, xanh/vàng, đọc lại ≤ 2 lần, khen, lưu phụ huynh, "Bỏ qua" | 4, 5, 7, 8, 10 |
| Suy luận: đoạn 2–3 câu, 3 manh mối, câu hỏi, chọn 1/3, tô manh mối + giải thích, sai thì xem manh mối rồi chọn lại 1 lần | 3, 5, 6, 8, 9 |
| Nối nguyên nhân–kết quả: 3 + 3 thẻ xáo, kéo nối, cặp đúng sáng xanh, cặp sai bật lại, đọc lại 3 câu "vì… nên…" | 3, 5, 7, 8, 9 |
| Đặt tên cảnh: 3 tiêu đề, giải thích vì sao bao quát, sai thì nói tiêu đề kia chỉ một chi tiết rồi chọn lại 1 lần | 5, 6, 8, 9 |
| Bong bóng suy nghĩ: nhân vật + 3 bong bóng, đúng thì biểu lộ cảm xúc, sai thì nhìn lại tranh rồi chọn lại 1 lần | 5, 6, 8, 9 |
| Nguyên tắc chung (gắn truyện, "Bỏ qua", không điểm/chữ "sai", lưu cho phụ huynh, web) | Global Constraints, 8, 9, 10, 11 |
| Mỗi tuổi 5 tương tác, không trùng nhau giữa các tuổi | 1, 2, 4 |

## Rủi ro đã biết

- Mic là điều kiện của Đọc luân phiên: bé của gia đình tắt giọng nói chỉ thực hiện được phần chọn nhân vật và nghe; ghi trong trang test.
- Chất lượng "truyện không nói thẳng" của Suy luận phụ thuộc prompt và người duyệt; luật chỉ chặn trường hợp đáp án nằm nguyên văn trong đoạn.
- Truyện ngắn (ít đoạn) không đủ 5 vùng neo nên bé chỉ gặp một phần bộ; truyện không có thoại không có Đọc luân phiên.
- `ThoughtBubble` phụ thuộc tranh minh họa của đoạn neo; thiếu tranh thì ẩn (readiness báo `picture`).
- Việc đổi unique index của `read_aloud_recordings` cần kiểm kỹ hành vi `NULL` của PostgreSQL (Task 2, Step 3); nếu không muốn đụng bảng này, dùng phương án rẻ hơn ở bảng quyết định.
- Chưa có Gemini thật trong môi trường test nên chất lượng nội dung AI của tuổi 8 chỉ kiểm bằng luật và người duyệt.
