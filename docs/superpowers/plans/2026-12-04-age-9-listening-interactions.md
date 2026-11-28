# Tương tác lúc nghe cho bé 9 tuổi — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bé 9 tuổi (nhóm 9-10) nghe truyện sẽ gặp đúng 5 tương tác riêng của tuổi 9 (Phiên tòa nhân vật, Tìm chi tiết vô lý, Bản đồ mối quan hệ nhân vật, Biên tập viên nhí, "Nếu… thì…"), không dùng chung với tuổi khác.

**Architecture:** Dùng lại khung `TargetAge` của tuổi 6–8 và mở rộng cho **nhóm 9-10** (lần đầu có bộ cho nhóm này). Thêm 5 loại mới (`CharacterTrial`, `FindWrongDetail`, `RelationshipMap`, `StoryEditor`, `WhatIf`); bốn loại chấm được (Tìm chi tiết, Bản đồ, Biên tập, và chọn lại 1 lần) đi qua máy chủ chấm như các tuổi trước, hai loại không chấm (Phiên tòa, "Nếu… thì…") chỉ ghi lựa chọn cho phụ huynh. Lời tự do của bé (gõ hoặc nói) chỉ được lưu cho phụ huynh, hệ thống trả lời chung. Mọi nội dung cho trẻ do AI soạn nháp rồi người duyệt xác nhận.

**Tech Stack:** .NET 10, EF Core (PostgreSQL), xUnit + Moq, dịch vụ AI riêng ở `src/AI`, Gemini TTS (kèm mốc chữ qua STT), kho ghi âm `read_aloud_recordings` sẵn có.

**Spec:** `C:\Users\Admin\Downloads\Tương tác truyện cho trẻ 6–10 tuổi – Tớ Kể Chuyện.docx`, mục "9 tuổi" và "Nguyên tắc chung". Nền tảng: `docs/superpowers/plans/2026-11-28-age-6-…`, `…age-7-…`, `2026-12-03-age-8-listening-interactions.md`.

## Global Constraints

- Mỗi tuổi có 5 tương tác, **không trùng nhau giữa các tuổi**; bé chỉ thấy bộ đúng tuổi mình.
- Mỗi tương tác gắn với nội dung truyện, không thêm trò chơi phụ.
- Mọi tương tác dùng mic hoặc vẽ đều có nút "Bỏ qua" (bỏ qua = không gọi API, không có dòng attempt).
- Không hiện điểm hay chữ "sai" cho bé; kết quả lưu cho phụ huynh xem.
- Chạy trên web, bé thao tác bằng ngón tay hoặc chuột (ô thả to, không dùng đường nối mỏng làm điểm thả).
- Mọi nội dung cho trẻ (lý do, hai mặt, chi tiết bị đổi, nhãn quan hệ, nhãn ý chính của câu) **soạn sẵn từ trước**: AI soạn nháp, người duyệt xác nhận; guardrail an toàn không bị bỏ qua.
- Không có đúng/sai khi có nhiều quan điểm hợp lý (Phiên tòa, "Nếu… thì…"): chỉ đưa hai mặt hoặc giải thích khả năng.
- Chọn lại **1 lần** với Tìm chi tiết vô lý, Bản đồ mối quan hệ (chỉ các đường còn chưa phù hợp), Biên tập viên nhí; lần hai vẫn chưa được thì hệ thống chỉ ra đáp án mẫu rồi truyện chạy tiếp.
- Lời tự do của bé không bị chấm, không hiện cho trẻ khác, không đưa vào log; chỉ chủ hồ sơ xem được.
- Không đổi hành vi của bộ tuổi 6, 7, 8; tuổi 10 vẫn chưa có bộ.
- Không áp migration lên DB thật trong plan này; chỉ tạo migration và test.

## Năm tương tác của tuổi 9 (thiết kế đề xuất)

| # | Loại (enum) | Bé làm gì | Dữ liệu chính | Chấm / kết quả |
|---|---|---|---|---|
| 1 | Phiên tòa nhân vật (`CharacterTrial = 18`) | Nghe tóm tắt tình huống (1–2 câu) khi nhân vật quyết định; chọn 1 trong 3 nhận định cố định (hợp lý / chưa hợp lý / có thể làm tốt hơn); rồi chọn 1 lý do trong 3–4 lý do soạn sẵn, hoặc gõ / nói lý do riêng | `Prompt` = tình huống, `SourceQuote` = câu quyết định, 3–4 option lý do, mỗi lý do có `Feedback` (mặt tích cực) và `Counterpoint` (mặt hạn chế) | **Không chấm.** Lý do soạn sẵn → hệ thống đọc hai mặt. Lời tự do → lưu, trả câu cảm ơn chung |
| 2 | Tìm chi tiết vô lý (`FindWrongDetail = 19`) | Nghe hệ thống kể lại một cảnh (có 1 chi tiết bị đổi); chạm vào câu cho là sai; chọn chi tiết đúng trong 3 | `SourceQuote` = đoạn gốc (2–4 câu), `Prompt` = bản kể lại, `HiddenWord` = chi tiết bị đổi, 3 option chi tiết (1 đúng) | Đúng khi chạm đúng câu **và** chọn đúng chi tiết; tô sáng đoạn gốc; sai lần 1 gợi ý và chọn lại; sai lần 2 hiện đáp án |
| 3 | Bản đồ mối quan hệ (`RelationshipMap = 20`) | Nhìn 3–4 nhân vật nối bằng 3 đường có mũi tên; kéo thẻ quan hệ vào ô nhãn của từng đường | 3–4 nút (`Role.Node`, có hình), 4–5 thẻ (`Role.Card`), `RelationsJson`: 3 đường có hướng, danh sách thẻ chấp nhận, sự kiện làm bằng chứng và lời giải thích | Chấm từng đường (một đường chấp nhận nhiều thẻ); giải thích theo sự kiện; sai thì gợi ý và đặt lại các đường đó 1 lần; lần 2 hiện đáp án mẫu |
| 4 | Biên tập viên nhí (`StoryEditor = 21`) | Nghe đoạn 5–6 câu; yêu cầu "rút gọn còn 3 câu mà vẫn giữ nội dung chính"; chạm để loại / khôi phục từng câu | `SourceQuote` = đoạn, 5–6 option câu (`IsMatch` = ý chính, `Feedback` = vì sao quan trọng), `CorrectOrder` = thứ tự câu | Đúng khi giữ đủ **mọi** câu ý chính và còn không quá 3 câu; thiếu ý chính / còn dài → gợi ý, sửa lại 1 lần; lần 2 hiện bản rút gọn mẫu và giải thích |
| 5 | "Nếu… thì…" (`WhatIf = 22`) | Nghe câu hỏi sau một quyết định quan trọng; chọn 1 trong 3 kết quả, hoặc gõ / nói câu riêng | `Prompt` = câu "Nếu … thì …?", `SourceQuote` = quyết định, 3 option kết quả mỗi cái có `Feedback` = vì sao có thể xảy ra | **Không chấm.** Kết quả soạn sẵn → giải thích khả năng theo chi tiết truyện. Câu riêng → lưu, cảm ơn chung |

## Quyết định thiết kế (đề xuất, bạn có thể đổi trước khi làm)

| Câu hỏi | Chốt trong plan |
|---|---|
| Nhóm 9-10 lần đầu có bộ | Thêm `9` vào `ListeningAgeCatalog.BuiltAges` ở **task cuối** để các bước giữa không đổi hành vi. Từ đó truyện nhóm 9-10 có ≥ 2 đoạn sẽ **bắt buộc có bộ tuổi 9 và được người duyệt xác nhận** (như nhóm 6-8). Truyện 9-10 đã có từ trước (seed 901) phải sinh lại bộ này bằng `artifacts/generate`. |
| Bé 10 tuổi trong nhóm 9-10 | Chưa có bộ (tuổi 10 làm sau); không thấy tương tác lúc nghe. Bé nhóm 9-10 chưa nhập tuổi tính là 9 (`ListeningAgeSets.AgeFor`). |
| Ba nhận định của Phiên tòa | Hằng số của hệ thống (không có trong DB): `reasonable`, `notYet`, `couldBeBetter`; gửi trong request, lưu ở `AnswerText`. Không bên nào là "đúng". |
| Hai mặt của một lý do | Cột mới `ListeningInteractionOption.Counterpoint` (mặt hạn chế); `Feedback` là mặt tích cực. Hệ thống đọc "Mặt tốt: … / Mặt cần cân nhắc: …". |
| Lời tự do (gõ) | `FreeText` ≤ 100 ký tự (như `MaxAnswerLength`), lưu ở `AnswerText` của attempt; chỉ chủ hồ sơ xem; không đưa vào telemetry. |
| Lời tự do (nói) | Endpoint mới `POST …/free-answer/{interactionId}` (multipart `Audio`, cần `VoiceAnswersEnabled`) chỉ **lưu bản ghi âm** vào `read_aloud_recordings` với `TotalCount = 0` (không nhận dạng, không chấm), giữ bản mới nhất mỗi phiên. Attempt sau đó gửi `freeVoice = true`; máy chủ kiểm có bản ghi (không thì 409 `LISTENING_FREE_ANSWER_MISSING`). |
| Câu trả lời chung cho lời tự do | Câu cố định có audio sinh sẵn, khóa `thanks`: "Cảm ơn con đã chia sẻ! Mỗi quyết định đều có mặt tốt và mặt cần cân nhắc." (Phiên tòa) / "Cảm ơn con đã nghĩ ra điều này! Mình cùng nghe tiếp nhé." ("Nếu… thì…"). |
| Bản kể lại có chi tiết đổi sai | Máy chủ kiểm bất biến: thay `HiddenWord` bằng chi tiết đúng thì `Prompt` phải **bằng đúng** `SourceQuote`. Bé chạm theo **câu** của bản kể lại (tách câu bằng hàm chung `SentenceSplitter`). |
| Bản đồ quan hệ | Nút = option `Role.Node` (tên + `ImageDescription`, có hình sinh sẵn như WhoSaidIt), thẻ = option `Role.Card`; đường nằm trong cột JSON mới `ListeningInteraction.RelationsJson` (chỉ số nút và chỉ số thẻ theo `SortOrder`). Thẻ lấy từ 5 nhãn cố định (giúp đỡ, cản trở, bảo vệ, tin tưởng, hiểu lầm); "hiểu lầm" cần câu trích bằng chứng có thật (người duyệt xác nhận). Bé gửi cả 3 vị trí một lần; thẻ không phải dùng một lần (mỗi đường chấm riêng). |
| Biên tập viên nhí | Số câu giữ tối đa = 3 (hằng số, yêu cầu hiển thị ở prompt cố định). Đoạn có 2–3 câu ý chính; chấp nhận mọi cách giữ đủ ý chính trong 3 câu. Hệ thống "đọc to đoạn đã biên tập" bằng cách phát audio từng câu theo thứ tự (không sinh giọng lúc chạy). |
| Mốc chữ khi đọc | Dùng chung `NeedsWordTimings`: đoạn gốc của Biên tập viên, bản kể lại của Tìm chi tiết vô lý và câu hỏi "Nếu… thì…" có mốc từng chữ. |
| Sinh | Truyện nhóm 9-10 sinh một bộ (tuổi 9); `targetAge` hợp lệ của nhóm là `[9]`. Handler AI hiện chỉ nhận nhóm 6-8 khi có `targetAge` nên phải đổi sang bảng "nhóm → tuổi → loại". |
| Seed | Truyện 901 chỉ có 4 đoạn (3 chỗ neo) nên **thêm truyện mẫu mới 902** (6 đoạn, nhóm 9-10) cho bộ tuổi 9; 901 và dòng FillBlank 905 giữ nguyên. |
| Code cũ không còn dùng (FillBlank, ReadAloud, Checkpoint Prediction/Emotion/Opinion) | Giữ nguyên, không dọn trong plan này. |

## File Structure

- `src/Shared/StoryPlatform.Contracts/AI/Models/ListeningAgeCatalog.cs` — thêm 9 (task cuối).
- `src/Core/StoryPlatform.Domain/Enums/ListeningInteractionType.cs`, `ListeningOptionRole.cs` — loại 18–22, vai trò `Node = 4`, `Card = 5`.
- `src/Core/StoryPlatform.Domain/Entities/ListeningInteraction.cs` (`RelationsJson`), `ListeningInteractionOption.cs` (`Counterpoint`) + cấu hình EF.
- `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddAge9ListeningInteractions.cs` — migration (EF sinh).
- `.../ListeningInteractions/Services/`: `SentenceSplitter.cs` (**mới**), `RelationMap.cs` (**mới**), `EditorSentences.cs` (**mới**), `ListeningInteractionRules.Age9.cs` (**mới**), và sửa `ListeningInteractionPlanner.cs`, `ListeningInteractionRules.cs`, `ListeningInteractionAiMapper.cs`, `ListeningInteractionMapper.cs`, `ListeningAudioParts.cs`, `ListeningMediaRequirements.cs`.
- `src/Shared/StoryPlatform.Contracts/AI/Models/StoryContentModels.cs`, `Responses/GenerationResponses.cs`; `src/AI/.../GenerateListeningInteractionHandler.cs`, `GenerationEnums.cs`, `PromptTemplateProvider.cs`.
- `src/Core/.../ReadingSessions/ChildListeningContent.cs`, `DTOs/ChildReadingDtos.cs`, `DTOs/ChildListeningDtos.cs`, `Services/ChildListeningService.cs`, `Services/ChildFreeAnswerService.cs` (**mới**), `Services/ParentReadingFeedbackService.cs`, `StoryPlatform.Api/Controllers/ChildReadingController.cs`.
- `src/Core/.../StoryReview/Services/StoryReviewService.Listening.cs`, `DTOs/ReviewDtos.cs`, `MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs`.
- `Database/Seed/11_stories.sql`, `13_story_versions.sql`, `60_listening_interactions.sql`, `61_listening_interaction_options.sql` (+ các file seed khác mà truyện 900 cần, xem Task 13).
- `docs/api/age-9-interactions-api-test-guide.html` — **mới** (Task 13, cần bạn duyệt; thư mục bị `.gitignore` chặn).

---

### Task 1: Gỡ chỗ cứng "chỉ nhóm 6-8" ở dịch vụ AI

**Việc này làm gì (nói dễ hiểu):** Hiện dịch vụ AI chỉ chấp nhận yêu cầu sinh tương tác theo tuổi khi truyện thuộc nhóm 6-8. Nhóm 9-10 chưa từng có bộ nên bị chặn cứng. Task này đổi sang một bảng "nhóm tuổi → tuổi → loại tương tác được phép" để thêm tuổi 9 mà không phải sửa rải rác. Chưa có loại tuổi 9 nào trong bảng (bảng cho tuổi 9 để rỗng tới Task 6), và chưa bật tuổi 9 cho bé.

**Cần thêm / còn thiếu:**
- Đã có: `GenerateListeningInteractionHandler` (kiểm tuổi/loại bằng chuỗi `if`), `ValidateProfile` (`younger`/`older`).
- Cần thêm: bảng `AllowedTypes[(band, age)]`; kiểm `targetAge` theo bảng; nhóm 9-10 chấp nhận `targetAge = 9`.
- Còn thiếu / ngoài phạm vi: loại tuổi 9 (Task 6); mở `BuiltAges` (Task 13).

**Files:**
- Modify: `src/AI/StoryPlatform.AI.Application/ContentGeneration/GenerateListeningInteractionHandler.cs:33-37,99-103`
- Test: `tests/StoryPlatform.AI.IntegrationTests/ListeningHttpContractTests.cs`

**Interfaces:**
- Produces: `ListeningGenerationContract.AllowedTypes(string ageBand, int age) : IReadOnlySet<string>`; hành vi cũ của 6, 7, 8 không đổi.

- [ ] **Step 1: Viết test thất bại** — (a) yêu cầu `targetAge = 9` với `ageBand = "9-10"` nhưng loại chưa có (bảng rỗng) → 400, 0 lần gọi model; (b) mọi test tuổi 6/7/8 hiện có vẫn qua; (c) `targetAge = 9` với `ageBand = "6-8"` → 400.
- [ ] **Step 2: Chạy test, thấy thất bại** (hiện ném 400 vì nhóm không phải 6-8 cho **mọi** targetAge, kể cả loại đúng sau này).
- [ ] **Step 3:** Thay các điều kiện `age == 6/7/8 && request.InteractionType is not (…)` bằng tra bảng theo `(ageBand chuẩn hóa, age)`; chuẩn hóa `"6-8"|"Age_6_8"` và `"9-10"|"Age_9_10"`.
- [ ] **Step 4:** `dotnet test tests/StoryPlatform.AI.IntegrationTests --filter "FullyQualifiedName~ListeningHttpContractTests"`. Kết quả mong đợi: PASS.

---

### Task 2: Mô hình dữ liệu cho năm loại mới

**Việc này làm gì (nói dễ hiểu):** Năm tương tác tuổi 9 cần chỗ lưu những thứ chưa có: "mặt hạn chế" của một lý do, sơ đồ các đường quan hệ giữa nhân vật, và hai vai trò thẻ mới (nhân vật, thẻ quan hệ). Task này thêm loại, vai trò, cột và migration, chưa có logic nghiệp vụ.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteraction` (`Prompt`, `SourceQuote`, `SourceStart/End`, `HiddenWord`, `Explanation`, `MatchFeedback`), `ListeningInteractionOption` (`Role`, `Feedback`, `IsMatch`, `CorrectOrder`, `ImageDescription`, `SourceQuote`), `ListeningInteractionAttempt.AnswerText/OrderedOptionIdsJson`, `ReadAloudRecording`.
- Cần thêm: enum `CharacterTrial = 18, FindWrongDetail = 19, RelationshipMap = 20, StoryEditor = 21, WhatIf = 22`; `ListeningOptionRole.Node = 4, Card = 5`; cột `ListeningInteraction.RelationsJson` (text) và `ListeningInteractionOption.Counterpoint` (text).
- Còn thiếu / ngoài phạm vi: logic (các task sau); seed (Task 13).

**Files:**
- Modify: `src/Core/StoryPlatform.Domain/Enums/ListeningInteractionType.cs`, `ListeningOptionRole.cs`
- Modify: `src/Core/StoryPlatform.Domain/Entities/ListeningInteraction.cs`, `ListeningInteractionOption.cs`; cấu hình EF của hai bảng (`ListeningInteractionConfiguration`, `ListeningInteractionOptionConfiguration`)
- Create (EF sinh): `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddAge9ListeningInteractions.cs` và `.Designer.cs`, cập nhật `ApplicationDbContextModelSnapshot.cs`
- Test: `tests/StoryPlatform.UnitTests/Age9ListeningMigrationTests.cs` (mới, theo mẫu `Age8ListeningMigrationTests.cs`)

**Interfaces:**
- Produces: `ListeningInteraction.RelationsJson : string?`, `ListeningInteractionOption.Counterpoint : string?`.

- [ ] **Step 1: Viết test migration thất bại** — chỉ thêm 2 cột nullable (`listening_interactions.RelationsJson`, `listening_interaction_options.Counterpoint`), không drop/SQL, `HasPendingModelChanges() == false`.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3:** Thêm enum/vai trò/cột; `dotnet ef migrations add AddAge9ListeningInteractions --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api` (chỉ tạo file, **không** `database update`).
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~Age9ListeningMigration"`. Kết quả mong đợi: PASS.

---

### Task 3: Hàm thuần — tách câu, sơ đồ quan hệ, chấm bản biên tập

**Việc này làm gì (nói dễ hiểu):** Ba việc cần đúng tuyệt đối và dễ test riêng: (1) tách một đoạn văn thành các câu theo cùng một cách ở mọi nơi (để bé chạm "câu thứ mấy" mà máy chủ hiểu giống nhau); (2) đọc sơ đồ quan hệ và chấm từng đường (một đường có thể có nhiều thẻ hợp lý); (3) chấm bản biên tập của bé (giữ đủ câu ý chính, không quá 3 câu).

**Cần thêm / còn thiếu:**
- Đã có: mẫu hàm thuần `SentenceOrders`, `CausePairs`, `ClueSpans`, `ListeningInteractionRules.CountWords`.
- Cần thêm: `SentenceSplitter.Split`; `RelationMap` (parse, kiểm hợp lệ, chấm); `EditorSentences.Grade`.
- Còn thiếu / ngoài phạm vi: nối vào luật (Task 5) và chấm (Task 9–10).

**Files:**
- Create: `.../ListeningInteractions/Services/SentenceSplitter.cs`, `RelationMap.cs`, `EditorSentences.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/Age9PureFunctionsTests.cs`

**Interfaces:**
- Produces:
  - `SentenceSplitter.Split(string text) : IReadOnlyList<string>` — tách sau `.`, `!`, `?`, `…` (giữ dấu), bỏ phần rỗng; chữ trong ngoặc kép không tách giữa chừng khi dấu câu nằm trong dấu ngoặc (ví dụ `"Có ai ở đó không?" Thỏ hỏi.` giữ là một câu).
  - `RelationMap.TryParse(string? json) : RelationMapDefinition?`; `record RelationEdge(int From, int To, IReadOnlyList<int> Accepted, string Evidence, string Explanation)`; `RelationMapDefinition.IsValid(int nodeCount, int cardCount)`; `RelationMapDefinition.Grade(IReadOnlyDictionary<int,int> cardByEdge) : IReadOnlyList<bool>` (theo chỉ số đường).
  - `EditorSentences.Grade(IReadOnlyList<bool> isMain, IReadOnlySet<int> kept, int maxKept = 3) : EditorGrade` với `record EditorGrade(bool Matched, IReadOnlyList<int> MissingMain, bool TooLong)`; `EditorSentences.Model(IReadOnlyList<bool> isMain, int maxKept = 3) : IReadOnlyList<int>` (chỉ số mẫu: mọi câu ý chính, thêm câu phụ đầu tiên cho đủ `maxKept` nếu còn chỗ — không, chỉ ý chính để bản mẫu ngắn nhất).

- [ ] **Step 1: Viết test thất bại** — ví dụ:

```csharp
[Fact] public void Splitter_KeepsQuotedSpeechTogether() =>
    Assert.Equal(["Thỏ hỏi: \"Có ai ở đó không?\"", "Bác Gấu đáp: \"Bác đây!\""],
        SentenceSplitter.Split("Thỏ hỏi: \"Có ai ở đó không?\" Bác Gấu đáp: \"Bác đây!\""));

[Fact] public void Editor_AllMainKeptWithinLimit_Matches()
{
    var grade = EditorSentences.Grade([true, false, true, false, false], new HashSet<int> { 0, 2 });
    Assert.True(grade.Matched);
}
[Fact] public void Editor_MissingMain_IsNotMatched() =>
    Assert.Equal([2], EditorSentences.Grade([true, false, true, false, false], new HashSet<int> { 0, 1 }).MissingMain);
[Fact] public void Editor_TooLong_IsNotMatched() =>
    Assert.True(EditorSentences.Grade([true, false, true, false, false], new HashSet<int> { 0, 1, 2, 3 }).TooLong);

[Fact] public void Relation_EdgeAcceptsAnyOfItsCards()
{
    var map = RelationMap.TryParse("""{"edges":[{"from":0,"to":1,"accepted":[0,2],"evidence":"a","explanation":"b"},{"from":1,"to":2,"accepted":[1],"evidence":"c","explanation":"d"},{"from":0,"to":2,"accepted":[3],"evidence":"e","explanation":"f"}]}""")!;
    Assert.Equal([true, false, false], map.Grade(new Dictionary<int,int> { [0] = 2, [1] = 0, [2] = 1 }));
}
```
  cộng ca xấu: JSON hỏng → `null`; đường tự nối (`from == to`), chỉ số ngoài phạm vi, trùng đường, thiếu `accepted` → `IsValid` false.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** (mỗi hàm ngắn, không phụ thuộc DB).
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~Age9PureFunctions"`. Kết quả mong đợi: PASS.

---

### Task 4: Planner bộ tuổi 9

**Việc này làm gì (nói dễ hiểu):** Quyết định truyện nhóm 9-10 có tương tác nào của tuổi 9 và đặt sau đoạn nào: "Nếu… thì…" và Phiên tòa cần chỗ nhân vật quyết định (đặt ở giữa–cuối truyện), Bản đồ cần đoạn có nhiều nhân vật, Biên tập viên cần đoạn dài, Tìm chi tiết vô lý ở bất kỳ cảnh nào. Truyện ngắn thì chỉ lấy đủ số tương tác theo số chỗ neo.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionPlanner` (6, 7, 8), `AssignZones`, `HasShortSentence`.
- Cần thêm: `CreateAge9`; thứ tự ưu tiên `FindWrongDetail → RelationshipMap → StoryEditor → CharacterTrial → WhatIf` (hai loại cần quyết định ở các vùng muộn nhất); `AssignZones` biết loại tuổi 9: `StoryEditor` lấy vùng có đoạn dài nhất (nhiều câu), `RelationshipMap` lấy vùng có nhiều tên riêng viết hoa, `CharacterTrial` và `WhatIf` lấy hai vùng muộn nhất (cao trào), `FindWrongDetail` lấy vùng sớm nhất.
- Còn thiếu / ngoài phạm vi: luật nội dung (Task 5).

**Files:**
- Modify: `.../Services/ListeningInteractionPlanner.cs` (`Create`, `CreateAge9`, `AssignZones`)
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionPlannerTests.cs`

**Interfaces:**
- Consumes: `SentenceSplitter.Split` (Task 3), `ListeningPlanItem`, `StoryTextBlock`.
- Produces: `ListeningInteractionPlanner.Create(9, blocks)` trả plan `TargetAge = 9`, tối đa 5 mục, mỗi mục một vùng riêng.

- [ ] **Step 1: Viết test thất bại** — (a) truyện 6 đoạn → 5 mục đúng thứ tự, 5 vùng khác nhau, hai loại cần quyết định ở hai vùng muộn nhất, `StoryEditor` ở vùng có đoạn nhiều câu nhất; (b) 2, 3, 4 đoạn → lấy 1, 2, 3, ... mục theo thứ tự ưu tiên; (c) 1 đoạn → rỗng; (d) tuổi 10 vẫn rỗng; (e) không loại nào là "cuối truyện" (`IsEndOfStory(type, 9) == false`).
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** như mục "Cần thêm".
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ListeningInteractionPlanner"`. Kết quả mong đợi: PASS.

---

### Task 5: Luật kiểm tra bộ tuổi 9

**Việc này làm gì (nói dễ hiểu):** Mọi nội dung AI soạn phải qua bộ kiểm trước khi tới người duyệt. Task này viết bộ kiểm cho năm tương tác tuổi 9: tóm tắt tình huống có căn cứ trong truyện, lý do có đủ hai mặt, bản kể lại chỉ sai đúng một chi tiết và sửa lại thì ra đúng đoạn gốc, sơ đồ quan hệ có hướng và có bằng chứng, đoạn biên tập có đủ nhãn ý chính, câu "Nếu… thì…" đúng dạng.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningInteractionRules.Validate`, `AppearsBefore`, `CountWords`, `HasDuplicateText`, `ValidateFields`, `RequiredFeedback`, mã lỗi chung.
- Cần thêm (`ListeningInteractionRules.Age9.cs`):
  - **CharacterTrial:** `Prompt` 1–2 câu (≤ 40 từ); `SourceQuote` (câu nhân vật quyết định) nguyên văn trước điểm neo; 3–4 option lý do, mỗi lý do ≤ 12 từ, không trùng, mỗi cái có `Feedback` và `Counterpoint` không rỗng; không option nào `IsMatch`; không từ chấm điểm. Mã lỗi: `OPTION_COUNT_INVALID`, `TWO_SIDES_MISSING`, `MATCH_RULE_INVALID`.
  - **FindWrongDetail:** `SourceQuote` 2–4 câu (≤ 60 từ) nguyên văn trước điểm neo; đúng 3 option chi tiết, đúng 1 `IsMatch`, ≤ 6 từ, không trùng; `HiddenWord` có trong `Prompt` đúng 1 lần; **bất biến:** thay `HiddenWord` bằng chi tiết đúng thì `Prompt == SourceQuote` (mã `DETAIL_SWAP_INVALID`); chi tiết sai không được giống chi tiết đúng; `Explanation` bắt buộc.
  - **RelationshipMap:** 3–4 option `Node` (tên xuất hiện trong truyện trước neo, có `ImageDescription`); 4–5 option `Card` với `Text` ∈ {giúp đỡ, cản trở, bảo vệ, tin tưởng, hiểu lầm}, không trùng; `RelationMap.TryParse(RelationsJson)` hợp lệ với số nút và thẻ; đúng 3 đường, mỗi đường có `Evidence` nguyên văn trước neo và `Explanation`; tên hai đầu mỗi đường xuất hiện trong câu bằng chứng; "hiểu lầm" chỉ được nằm trong `accepted` khi câu bằng chứng chứa dấu hiệu hiểu lầm (từ khóa `nhầm`, `hiểu lầm`, `tưởng`) — nếu không là `RELATION_EVIDENCE_INVALID`. Mã lỗi: `RELATION_MAP_INVALID`.
  - **StoryEditor:** `SourceQuote` 5–6 câu liên tiếp nguyên văn trước neo; option câu = đúng các câu của `SourceQuote` theo thứ tự (`CorrectOrder` 1..n, so với `SentenceSplitter.Split`); 2–3 câu `IsMatch` (ý chính), mỗi câu ý chính có `Feedback`; mã lỗi `EDITOR_SENTENCES_INVALID`, `MAIN_IDEA_COUNT_INVALID`.
  - **WhatIf:** `Prompt` bắt đầu bằng "Nếu", chứa "thì", kết thúc "?"; `SourceQuote` (quyết định) nguyên văn trước neo; đúng 3 option kết quả ≤ 12 từ, mỗi cái có `Feedback` (vì sao có thể xảy ra) không rỗng, không `IsMatch`.
- Còn thiếu / ngoài phạm vi: kiểm "tính hợp lý" của giải thích (người duyệt lo).

**Files:**
- Create: `.../Services/ListeningInteractionRules.Age9.cs`
- Modify: `.../Services/ListeningInteractionRules.cs` (switch, `ValidateFields`, `RequiredFeedback`, danh sách nhãn quan hệ), `.../Models/ListeningInteractionModels.cs` (`ListeningInteractionDraft.RelationsJson`, `ListeningOptionDraft.Counterpoint`)
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningInteractionRulesAge9Tests.cs` (theo mẫu `…Age8Tests.cs`, dùng truyện thử 9-10 riêng trong file, kèm test "phản hồi AI dùng chung của `ListeningTestStory` đạt luật")

**Interfaces:**
- Consumes: Task 3. Produces: mã lỗi mới `TWO_SIDES_MISSING`, `DETAIL_SWAP_INVALID`, `RELATION_MAP_INVALID`, `RELATION_EVIDENCE_INVALID`, `EDITOR_SENTENCES_INVALID`, `MAIN_IDEA_COUNT_INVALID`.

- [ ] **Step 1: Viết test thất bại** — mỗi loại: một bản hợp lệ và mỗi luật bị vi phạm (đúng mã lỗi + trường); ca biên: 2 hoặc 5 lý do, bản kể lại đổi hai chỗ, `HiddenWord` không có trong `Prompt`, 2 hoặc 4 đường, đường tự nối, "hiểu lầm" không có bằng chứng, đoạn 4 hoặc 7 câu, 1 hoặc 4 câu ý chính, câu hỏi không bắt đầu bằng "Nếu".
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** — khung hàm cho bất biến chi tiết bị đổi:

```csharp
private static void ValidateFindWrongDetail(ListeningInteractionDraft item, int index, ListeningRuleContext context, List<ListeningValidationError> errors)
{
    var original = item.SourceQuote?.Trim();
    if (string.IsNullOrWhiteSpace(original) || PositionBefore(context.Content, original, item.AnchorOffset) < 0)
        errors.Add(new("NOT_GROUNDED", index, "sourceQuote"));
    var correct = item.Options.FirstOrDefault(o => o.IsMatch)?.Text.Trim();
    var wrong = item.HiddenWord?.Trim();
    if (item.Options.Count != 3 || item.Options.Count(o => o.IsMatch) != 1 || HasDuplicateText(item))
        errors.Add(new("OPTION_COUNT_INVALID", index, "options"));
    if (string.IsNullOrWhiteSpace(item.Prompt) || string.IsNullOrWhiteSpace(wrong) || correct is null ||
        CountOccurrences(item.Prompt, wrong) != 1 ||
        !string.Equals(item.Prompt.Replace(wrong, correct, StringComparison.Ordinal), original, StringComparison.Ordinal))
        errors.Add(new("DETAIL_SWAP_INVALID", index, "retold"));
    if (string.IsNullOrWhiteSpace(item.Explanation)) errors.Add(new("SCHEMA_INVALID", index, "explanation"));
}
```
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ListeningInteractionRules"`. Kết quả mong đợi: PASS (tuổi 6, 7, 8 không đổi).

---

### Task 6: Hợp đồng AI và prompt cho bộ tuổi 9

**Việc này làm gì (nói dễ hiểu):** Dịch vụ AI cần biết soạn từng loại tuổi 9: nhận đoạn truyện và vùng được neo, trả JSON đúng khuôn. Task này thêm khuôn và lời nhắc cho năm loại, bảng loại cho nhóm 9-10, và đổi JSON AI trả về thành bản nháp để Task 5 kiểm.

**Cần thêm / còn thiếu:**
- Đã có: `GenerateListeningInteractionHandler`, `ListeningGenerationContract`, `PromptTemplateProvider`, `ListeningInteractionAiMapper`, `ListeningInteractionGenerator`.
- Cần thêm: 5 `PromptType` (`ListeningCharacterTrial`…`ListeningWhatIf`); schema:
  - `CharacterTrial`: `afterBlockId`, `situation`, `decisionQuote`, `reasons[3–4]{text, positive, limitation}`, `matchFeedback`.
  - `FindWrongDetail`: `afterBlockId`, `originalQuote`, `retold`, `wrongDetail`, `choices[3]{text, isCorrect}`, `explanation`, `matchFeedback`.
  - `RelationshipMap`: `afterBlockId`, `characters[3–4]{name, imageDescription}`, `cards[4–5]` (enum 5 nhãn), `relations[3]{from, to, accepted[], evidence, explanation}`, `matchFeedback`.
  - `StoryEditor`: `afterBlockId`, `passage`, `sentences[5–6]{text, isMain, whyImportant}` (whyImportant bắt buộc khi isMain, để chuỗi rỗng khi không), `matchFeedback`.
  - `WhatIf`: `afterBlockId`, `decisionQuote`, `question`, `outcomes[3]{text, because}`, `matchFeedback`.
  - Prompt mô tả đúng bất biến Task 5 (bản kể lại chỉ đổi **một** chi tiết; mỗi lý do có cả mặt tốt lẫn mặt hạn chế, không nói đúng sai; nhãn quan hệ có hướng và có bằng chứng; chỉ 2–3 câu ý chính; câu "Nếu … thì …" trung tính).
  - Mapper: dựng option `Counterpoint`, `Role.Node/Card`, `RelationsJson`, `HiddenWord`, `CorrectOrder`.
- Còn thiếu / ngoài phạm vi: gọi Gemini thật.

**Files:**
- Modify: `src/Shared/StoryPlatform.Contracts/AI/Models/StoryContentModels.cs`, `Responses/GenerationResponses.cs`
- Modify: `src/AI/StoryPlatform.AI.Domain/Enums/GenerationEnums.cs`, `…/GenerateListeningInteractionHandler.cs`, `…/PromptCatalog/PromptTemplateProvider.cs`
- Modify: `…/Services/ListeningInteractionAiMapper.cs`, `ListeningInteractionMapper.cs` (tìm vị trí câu trích cho `FindWrongDetail`, `StoryEditor`, `CharacterTrial`, `WhatIf`; vị trí từng câu của `StoryEditor`), `ListeningInteractionGenerator.cs`
- Test: `tests/StoryPlatform.AI.IntegrationTests/ListeningHttpContractTests.cs`, `…/ListeningInteractionRulesAge9Tests.cs` (phản hồi AI → bản nháp đạt luật)

**Interfaces:**
- Consumes: Task 1, Task 5. Produces: yêu cầu `InteractionType ∈ {CharacterTrial, FindWrongDetail, RelationshipMap, StoryEditor, WhatIf}` với `ageBand = "9-10"`, `targetAge = 9`.

- [ ] **Step 1: Viết test thất bại** — chấp nhận từng loại; `targetAge = 9` với `6-8` bị từ chối; loại của tuổi khác với tuổi 9 bị từ chối; sai schema bị từ chối (2 lý do, 2 đường, nhãn quan hệ ngoài danh sách, 4 câu, thiếu `retold`); snapshot theo `PromptType`; mapper → bản nháp đạt `Validate`.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** schema, prompt, mapper.
- [ ] **Step 4:** `dotnet test tests/StoryPlatform.AI.IntegrationTests` và `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ListeningInteractionAiMapper|FullyQualifiedName~Age9"`. Kết quả mong đợi: PASS.

---

### Task 7: Audio và yêu cầu media cho năm loại mới

**Việc này làm gì (nói dễ hiểu):** Mỗi tương tác tuổi 9 cần audio sinh sẵn: tình huống, câu hỏi, từng lý do và hai mặt của nó, bản kể lại, từng câu của đoạn biên tập, các kết quả "nếu… thì…". Bản đồ quan hệ cần hình từng nhân vật như tương tác "Chạm vào nhân vật nói câu đó" của tuổi 6. Task này khai báo danh sách audio và hình bắt buộc để worker tạo và để hệ thống ẩn tương tác nào chưa đủ.

**Cần thêm / còn thiếu:**
- Đã có: `ListeningAudioParts.For/FixedLine/NeedsWordTimings`, `ListeningMediaRequirements` (`HasOptionImages`, `ImageOptions`), tạo media ở `MediaGenerationService.ListeningMedia.cs`, bù mốc chữ.
- Cần thêm:
  - Khóa mới `Thanks = "thanks"`, `Positive = "positive"`, `Limitation = "limitation"`.
  - Phần theo loại: **CharacterTrial**: `instruction`, `text` (tình huống = `Prompt`), `thanks` (cố định), mỗi lý do: `text`, `positive`, `limitation`; **FindWrongDetail**: `instruction`, `text` (bản kể lại, có mốc chữ), `match`, `explanation`, `retryLookClose`, mỗi chi tiết: `text`; **RelationshipMap**: `instruction`, `match`, `retryTry`, mỗi nút: `text` (tên), mỗi thẻ: `text`, và với mỗi đường một phần `explanation` mức interaction (khóa `relation:{i}`) đọc lời giải thích của đường; hình cho mỗi nút (`Role.Node` thuộc `HasOptionImages`); **StoryEditor**: `instruction` (cố định: "Em hãy chạm để bỏ những câu chưa cần, giữ lại ba câu quan trọng nhất nhé!"), `text` (cả đoạn, có mốc chữ), mỗi câu: `text` (phát nối nhau khi đọc bản đã biên tập) và với câu ý chính: `feedback`, `match`, `retryHear`; **WhatIf**: `instruction`, `text` (câu hỏi, có mốc chữ), `thanks`, mỗi kết quả: `text` và `feedback`.
  - `NeedsWordTimings`: thêm bản kể lại (FindWrongDetail), đoạn (StoryEditor), câu hỏi (WhatIf), tình huống (CharacterTrial).
- Còn thiếu / ngoài phạm vi: chất lượng giọng đọc (kiểm thủ công).

**Files:**
- Modify: `.../Services/ListeningAudioParts.cs`, `ListeningMediaRequirements.cs`, `…/MediaGeneration/Services/MediaGenerationService.ListeningMedia.cs` (nếu cần cho khóa `relation:{i}`), `MediaGenerationService.WordTimingBackfill.cs` (điều kiện chọn)
- Test: `tests/StoryPlatform.UnitTests/Features/ListeningInteractions/ListeningAudioPartsAge9Tests.cs`, `MediaGenerationServiceTests.Listening.cs`

**Interfaces:**
- Produces: `ListeningAudioParts.Thanks/Positive/Limitation`; `ListeningAudioParts.RelationKey(int edgeIndex) => $"relation:{edgeIndex}"`.

- [ ] **Step 1: Viết test thất bại** — mỗi loại: danh sách `PartKey` và `OptionId` kỳ vọng; bản đồ có hình cho mỗi `Node` và thiếu hình thì bị liệt kê; `NeedsWordTimings` đúng/sai theo bảng.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt.**
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ListeningAudioParts|FullyQualifiedName~MediaGenerationServiceTests"`. Kết quả mong đợi: PASS.

---

### Task 8: Gửi cho bé đúng bộ tuổi 9 (DTO nội dung)

**Việc này làm gì (nói dễ hiểu):** Khi bé mở truyện, màn hình cần dữ liệu để vẽ: tình huống và các lý do, bản kể lại tách theo câu, sơ đồ nhân vật với các đường và thẻ quan hệ, các câu của đoạn biên tập, câu hỏi và kết quả. Task này làm API nội dung trả đúng bộ tuổi 9 cho bé 9 tuổi, đã xáo thẻ nhưng không lộ đáp án, ý chính hay chi tiết bị đổi.

**Cần thêm / còn thiếu:**
- Đã có: `ChildListeningContent.Build/ToDto`, xáo cố định theo phiên, `StoryPicture`, `AudioWordTimings`, ký URL.
- Cần thêm:
  - `ReadingInteractionDto`: `Situation` (Phiên tòa), `Verdicts` (3 nhãn cố định), `Reasons[]` (id, text, audioUrl — không gồm hai mặt), `RetoldSentences[]` (index, text) và `Choices` dùng lại `Options` (FindWrongDetail), `Nodes[]` (id, name, imageUrl, audioUrl), `Edges[]` (index, fromNodeId, toNodeId; **không** có thẻ chấp nhận, bằng chứng) và `Cards[]` (id, text, audioUrl) (RelationshipMap), `EditSentences[]` (id, text, audioUrl) và `KeepLimit = 3` (StoryEditor), `Question` và `Outcomes` (WhatIf); `AllowsFreeAnswer` (Phiên tòa, WhatIf) và `FreeAnswerMaxLength = 100`.
  - Quy tắc "thấy khi nào": tuổi 9 chỉ thấy 5 loại mới; ẩn khi thiếu audio/hình bắt buộc, khi `RelationsJson` hỏng hoặc không khớp số nút/thẻ, hoặc khi đoạn biên tập không còn khớp từng câu.
  - `attemptedInSession`: Phiên tòa, "Nếu… thì…" = đã có attempt; ba loại chấm = đã đúng hoặc đã hết lượt chọn.
- Còn thiếu / ngoài phạm vi: nộp trả lời (Task 9–11).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/ChildListeningContent.cs`, `DTOs/ChildReadingDtos.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildListeningContentAge9Tests.cs` (theo mẫu `…Age8Tests.cs`; trẻ nhóm 9-10, tuổi 9)

**Interfaces:**
- Consumes: Task 3, 7. Produces: JSON không chứa `isMatch`, `accepted`, `evidence`, `explanation`, `counterpoint`, `positive`, `hiddenWord`, nhãn ý chính.

- [ ] **Step 1: Viết test thất bại** — mỗi loại đúng trường; JSON tuần tự hóa không chứa chuỗi bị cấm (tìm bằng chuỗi thật của dữ liệu: câu hai mặt, chi tiết đúng, bằng chứng, ý chính); xáo ổn định theo phiên; thiếu một hình nút → ẩn; bé 8 tuổi không thấy bộ tuổi 9; `RelationsJson` hỏng → ẩn.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt** nhánh `ToDto` cho 5 loại.
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ChildListeningContent"`. Kết quả mong đợi: PASS.

---

### Task 9: Nộp câu trả lời — Phiên tòa, Tìm chi tiết vô lý, Biên tập viên nhí, "Nếu… thì…"

**Việc này làm gì (nói dễ hiểu):** Khi bé chọn nhận định và lý do, chạm vào câu, loại bớt câu hay chọn một kết quả, máy chủ ghi lại cho phụ huynh và trả phản hồi: hai mặt của lý do, đoạn gốc được tô sáng, bản biên tập mẫu, giải thích khả năng. Không hiện điểm hay chữ "sai"; ba loại chấm cho chọn lại đúng 1 lần.

**Cần thêm / còn thiếu:**
- Đã có: `ChildListeningService.SubmitAttemptAsync/AnswerAsync`, `Age8Result`, cách ký URL (`SignAsync`), `MaxChoiceAttempts = 2`.
- Cần thêm:
  - `SubmitListeningAttemptRequestDto`: `Verdict` (string), `ReasonOptionId`, `FreeText`, `FreeVoice` (bool), `SentenceIndex`, `KeptOptionIds` (dùng lại `OrderedOptionIds`).
  - **CharacterTrial:** `Verdict` ∈ 3 hằng số; đúng một trong `ReasonOptionId` / `FreeText` / `FreeVoice = true` (không thì 400 `LISTENING_ANSWER_INVALID`); `FreeText` ≤ 100 ký tự sau khi trim; `FreeVoice` cần bản ghi tự do của phiên (nếu không: 409 `LISTENING_FREE_ANSWER_MISSING`); ghi một attempt (`AnswerText` = JSON `{verdict, free?}`, `SelectedOptionId` = lý do, `IsMatch` = null). Kết quả: lý do soạn sẵn → `Positive`, `Limitation` (+ audio); lời tự do → câu cảm ơn cố định (+ audio `thanks`). Gửi lại → trả kết quả cũ, không ghi thêm.
  - **FindWrongDetail:** request `SentenceIndex` + `OptionId`; hợp lệ khi chỉ số trong khoảng câu của `Prompt` (`SentenceSplitter`) và option thuộc tương tác; `IsMatch = (câu chứa HiddenWord) && option.IsMatch`. Kết quả: đúng → khen + `Explanation` + đoạn gốc (`SourceQuote`, khoảng `SourceStart/End` để tô); sai lần 1 → gợi ý cố định ("Nhìn kỹ lại nhé.") và `HintSentenceIndex` (chỉ cho biết câu chạm có đúng không: `SentenceCorrect`) rồi chọn lại; sai lần 2 → lộ `CorrectSentenceIndex`, `CorrectOptionId`, đoạn gốc.
  - **StoryEditor:** request `KeptOptionIds` (id các câu giữ lại, không trùng, thuộc tương tác, ≥ 1); chấm bằng `EditorSentences.Grade`; kết quả: `Matched`, `MissingMainOptionIds` rỗng cho tới khi lộ, `TooLong`, gợi ý cố định ("Con thử giữ lại ý chính nhé." / "Con thử bỏ thêm chi tiết phụ nhé."); đúng hoặc lộ đáp án → `Explanations[]` (câu ý chính + `Feedback`) và `ModelKeptOptionIds` (bản mẫu) + audio phát nối nhau.
  - **WhatIf:** `OptionId` hoặc `FreeText`/`FreeVoice`; kết quả: kết quả soạn sẵn → `Feedback` (vì sao có thể xảy ra) + audio; lời tự do → câu cảm ơn.
- Còn thiếu / ngoài phạm vi: Bản đồ quan hệ (Task 10); ghi âm tự do (Task 11).

**Files:**
- Modify: `…/ReadingSessions/Services/ChildListeningService.cs` (`AnswerAsync`, thêm `Age9Result`), `DTOs/ChildListeningDtos.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildListeningServiceAge9Tests.cs`

**Interfaces:**
- Consumes: Task 3, 7, 8. Produces: `ListeningAttemptResultDto` thêm `Positive`, `Limitation`, `SentenceCorrect`, `CorrectSentenceIndex`, `SourceSpan` (start,end), `TooLong`, `ModelKeptOptionIds`, `Explanations`.

- [ ] **Step 1: Viết test thất bại** — mỗi loại: đúng ngay; sai→đúng; sai→sai (lộ đáp án); gửi lại không thêm dòng; dữ liệu sai dạng (400); bé tuổi khác (404); Phiên tòa: ba nhận định, lý do soạn sẵn trả hai mặt, `FreeText` quá dài, vừa lý do vừa `FreeText`, `FreeVoice` khi chưa có bản ghi; Tìm chi tiết: chạm đúng câu nhưng chọn sai chi tiết, chọn đúng chi tiết nhưng chạm sai câu; Biên tập: thiếu ý chính, còn quá 3 câu, giữ đúng 2 hay 3 câu hợp lệ, câu không thuộc tương tác.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt.**
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ChildListeningServiceAge9"`. Kết quả mong đợi: PASS.

---

### Task 10: Bản đồ mối quan hệ — chấm theo đường

**Việc này làm gì (nói dễ hiểu):** Bé kéo thẻ quan hệ vào ô nhãn của từng đường nối. Máy chủ chấm từng đường (một đường có thể có nhiều thẻ hợp lý), giải thích bằng sự kiện trong truyện, rồi cho đặt lại đúng 1 lần các đường chưa phù hợp; lần hai vẫn chưa thì hiện đáp án mẫu.

**Cần thêm / còn thiếu:**
- Đã có: `RelationMap` (Task 3), khung `AnswerAsync`.
- Cần thêm: request `Placements` (danh sách `{edgeIndex, cardOptionId}`); hợp lệ khi đủ 3 đường, không trùng chỉ số, thẻ thuộc tương tác và có `Role.Card`; ghi một attempt cho mỗi lần gửi (`AnswerText` = JSON placements, `IsMatch` = cả 3 đúng); kết quả: `EdgeResults[]` (edgeIndex, matched, `Explanation` + audio khi đúng hoặc khi lộ, `AcceptedCardOptionId` mẫu khi lộ); lần 1 còn đường chưa đúng → `CanRetry` với gợi ý cố định ("Bé thử lại nhé."), chỉ các đường chưa đúng mở lại; lần 2 → `Revealed` kèm đáp án mẫu cho các đường còn lại; gửi lại sau khi xong trả kết quả cũ.
- Còn thiếu / ngoài phạm vi: giao diện kéo thả (frontend).

**Files:**
- Modify: `…/ReadingSessions/Services/ChildListeningService.cs`, `DTOs/ChildListeningDtos.cs` (`PlacementDto`, `EdgeResultDto`)
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildListeningServiceAge9Tests.cs` (thêm nhóm bản đồ)

**Interfaces:**
- Consumes: Task 3, 7. Produces: `ListeningAttemptResultDto.EdgeResults : List<EdgeResultDto>?`.

- [ ] **Step 1: Viết test thất bại** — đúng cả 3 ngay; một đường đúng bằng thẻ khác trong danh sách chấp nhận vẫn tính đúng; sai 1 đường → `CanRetry`, `EdgeResults` chỉ ra đường nào, **không** lộ thẻ đúng; lần 2 vẫn sai → `Revealed` với thẻ mẫu và giải thích; thiếu đường / trùng đường / thẻ không phải `Card` / thẻ của tương tác khác → 400; gửi lại sau khi xong không thêm dòng.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt.**
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ChildListeningServiceAge9"`. Kết quả mong đợi: PASS.

---

### Task 11: Lời tự do bằng giọng nói

**Việc này làm gì (nói dễ hiểu):** Ở Phiên tòa và "Nếu… thì…", bé có thể nói lý do hoặc kết quả của riêng mình. Hệ thống không chấm, không ghi chữ bé nói; chỉ giữ bản ghi âm để chủ hồ sơ nghe lại, rồi trả câu cảm ơn chung. Cần bật giọng nói như các chức năng đọc to khác.

**Cần thêm / còn thiếu:**
- Đã có: `read_aloud_recordings`, kho upload `IMediaStorage`, `ReadAloudPolicy` (kiểm file, giọng nói bật), controller multipart, `RequireChildFeature(Voice)`.
- Cần thêm: `IChildFreeAnswerService.SaveAsync(childId, sessionId, interactionId, audio)` và endpoint `POST /api/v1/child/reading/sessions/{sessionId}/free-answer/{interactionId}` (multipart `Audio`); chỉ nhận tương tác `CharacterTrial` / `WhatIf` đúng bộ tuổi của bé; kiểm `VoiceAnswersEnabled`, file, định dạng, kích thước như đọc to; lưu một bản ghi mỗi (phiên, tương tác) với `TotalCount = 0` (thay bản cũ, xóa file cũ), **không** nhận dạng, **không** telemetry nội dung; trả `{ saved: true }`.
- Còn thiếu / ngoài phạm vi: nghe lại phía phụ huynh dùng API ghi âm sẵn có (Task 12 chỉ báo có hay không).

**Files:**
- Create: `…/ReadingSessions/Services/ChildFreeAnswerService.cs`, và `…/Interfaces/IChildFreeAnswerService.cs` nếu mẫu dự án tách interface riêng (kiểm cách `IChildReadAloudService` được khai báo)
- Modify: `src/Core/StoryPlatform.Api/Controllers/ChildReadingController.cs`, `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs` (đăng ký)
- Test: `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildFreeAnswerServiceTests.cs`

**Interfaces:**
- Produces: `SaveAsync` ném `NotFoundException` (loại/tuổi sai), `ForbiddenException(VoiceOffMessage)`, `BadRequestException` (file); lỗi kho lưu trữ không làm hỏng phản hồi (ghi log, trả `saved = false`).

- [ ] **Step 1: Viết test thất bại** — lưu thành công tạo đúng một bản ghi; lần hai thay bản cũ và xóa file cũ; không gọi nhận dạng; giọng nói tắt → 403; file rỗng/sai định dạng/quá 1 MB → 400; loại tương tác khác → 404; bé tuổi khác → 404; sau khi lưu, attempt `FreeVoice = true` của Task 9 qua được.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt.**
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~ChildFreeAnswer|FullyQualifiedName~ChildListeningServiceAge9"`. Kết quả mong đợi: PASS.

---

### Task 12: Người duyệt, phụ huynh và readiness bộ tuổi 9

**Việc này làm gì (nói dễ hiểu):** Người duyệt thấy riêng "Bộ 9 tuổi", xem và sửa lý do, hai mặt, bản kể lại, sơ đồ quan hệ, nhãn ý chính, kết quả "nếu… thì…", và hệ thống kiểm lại luật tuổi 9 sau mỗi lần sửa. Phụ huynh xem được nhận định và lý do của bé, câu bé gõ, có hay không bản ghi giọng nói, các đường đã đặt và câu đã giữ. Truyện nhóm 9-10 chỉ qua bước media khi đủ audio và hình cho bộ tuổi 9.

**Cần thêm / còn thiếu:**
- Đã có: `StoryReviewService.Listening` (xem, sửa, hoàn tất, `ageSets`), `ListeningReadiness`, `ParentReadingFeedbackService`.
- Cần thêm: DTO review cho `counterpoint`, `relationsJson` (dạng đối tượng cho FE: `relations[]`), vai trò `Node/Card`; request sửa nhận `counterpoint`, `relations` (sửa `accepted`, `evidence`, `explanation`; không đổi số nút/thẻ), `retold`/`hiddenWord`; sửa → chạy lại luật tuổi 9, xóa HotspotJson không liên quan, tắt media cũ, bỏ xác nhận duyệt; `ListeningReadiness`: bộ tuổi 9 của nhóm 9-10 đủ 5 loại, đủ audio, đủ hình nút; phụ huynh: `ParentListeningFeedbackDto` thêm `Verdict`, `ReasonText`, `FreeText`, `HasFreeVoice`, `Placements[]` (đường, thẻ, đúng/sai), `KeptSentences[]`; chặn hiển thị `FreeText` cho ai không phải chủ hồ sơ.
- Còn thiếu / ngoài phạm vi: giao diện; nghe lại bản ghi tự do dùng API ghi âm có sẵn.

**Files:**
- Modify: `…/StoryReview/Services/StoryReviewService.Listening.cs`, `DTOs/ReviewDtos.cs`, `…/MediaGeneration/Services/MediaReadinessService.cs`, `…/ReadingSessions/Services/ParentReadingFeedbackService.cs`, `DTOs/ParentReadingFeedbackDtos.cs`
- Test: `tests/StoryPlatform.UnitTests/StoryReviewListeningAge9Tests.cs` (theo mẫu `…Age8Tests.cs`), `ParentReadingFeedbackServiceTests.cs`, test readiness

**Interfaces:**
- Consumes: Tasks 2, 5, 7, 9–11. Produces: `GET …/review/listening` của truyện 9-10 có `ageSets` tuổi 9 với 5 item; `PUT` nhận các trường mới; `complete` yêu cầu bộ tuổi 9 hợp lệ.

- [ ] **Step 1: Viết test thất bại** — xem bộ tuổi 9 (truyện nhóm 9-10); sửa hợp lệ từng loại; mỗi luật bị vi phạm → 400 `LISTENING_TEXT_INVALID:<mã>@<trường>`; sửa `retold` làm hỏng bất biến → `DETAIL_SWAP_INVALID`; đổi nhãn quan hệ ngoài danh sách → `RELATION_MAP_INVALID`; `complete` thiếu bộ → 409; phụ huynh thấy nhận định/lý do/đường/câu giữ; `FreeText` không lộ cho người ngoài (kiểm quyền hiện có).
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3: Cài đặt.**
- [ ] **Step 4:** `dotnet test --filter "FullyQualifiedName~StoryReview|FullyQualifiedName~ParentReadingFeedback|FullyQualifiedName~MediaReadiness|FullyQualifiedName~MediaGenerationServiceTests"`. Kết quả mong đợi: PASS.

---

### Task 13: Mở tuổi 9, seed và trang hướng dẫn test API (cần bạn duyệt trước khi tạo file)

**Việc này làm gì (nói dễ hiểu):** Bật chính thức tuổi 9 (nhóm 9-10 bắt đầu có tương tác lúc nghe), nạp sẵn một truyện mẫu 9-10 có đủ 6 đoạn và bộ tuổi 9 để thử ngay không cần AI, và làm trang HTML hướng dẫn test API như các tuổi trước.

**Cần thêm / còn thiếu:**
- Đã có: seed truyện 900 và các bộ tuổi 6–8; `SeedDataAlignmentTests`; mẫu trang `docs/api/_template-api-test-guide.html`.
- Cần thêm:
  - `ListeningAgeCatalog.BuiltAges = [6, 7, 8, 9]` (một chỗ duy nhất); cập nhật các test đang giả định "nhóm 9-10 chưa có bộ" (`ListeningAgeSetsTests`, test readiness/review/generation của nhóm 9-10; fixture `ListeningTestStory` có thêm hàm truyện 9-10 và `Age9AiResponse`, `SeedPackageAsync` sinh thêm bộ tuổi 9 cho truyện nhóm 9-10).
  - **Truyện mẫu 902** (nhóm 9-10, 6 đoạn: có quyết định của nhân vật, ≥ 3 nhân vật có quan hệ rõ, một đoạn 5–6 câu) và các dòng seed phụ trợ giống cách truyện 900 được seed (đọc `Database/Seed` để biết file nào có hàng cho truyện 900 và nhân bản tối thiểu: `11_stories`, `13_story_versions`, vocabulary, quiz, generation request kèm ngữ cảnh 9-10, các bảng trạng thái media/moderation nếu test seed yêu cầu), và 5 tương tác (id kế tiếp **940–944**) cùng thẻ (id kế tiếp **962** trở đi) của bộ tuổi 9 viết bám **đúng** văn bản truyện 902; một bé mẫu nhóm 9-10 có Age = 9 để thử (dùng bé Bình hoặc Đạt đã có ở `04_child_profiles.sql`, kiểm quyền sở hữu).
  - Mở rộng `SeedDataAlignmentTests.ListeningSeed_PassesTheRulesOfItsAge` cho tuổi 9 (đọc truyện 902 thay vì 900 cho nhóm 9-10).
  - Trang `docs/api/age-9-interactions-api-test-guide.html` dựng từ template (CLAUDE.md mục 13): sinh/sinh lại tuổi 9, nội dung cho bé 9 tuổi, nộp từng loại (kể cả lời tự do gõ và ghi âm), bản đồ quan hệ, duyệt/phụ huynh; localStorage `age9-interactions-guide-vars/checks`.
- Còn thiếu / ngoài phạm vi: truyện 9-10 cũ (seed 901 và truyện đã có trong DB thật) cần **sinh lại bộ tuổi 9** bằng `artifacts/generate` trước khi qua bước duyệt/media; media thật do worker tạo.

**Files:**
- Modify: `src/Shared/StoryPlatform.Contracts/AI/Models/ListeningAgeCatalog.cs`
- Modify: `Database/Seed/11_stories.sql`, `13_story_versions.sql`, `60_listening_interactions.sql`, `61_listening_interaction_options.sql` và các seed phụ trợ cần thiết (xác định ở Step 3)
- Modify: `tests/StoryPlatform.UnitTests/SeedDataAlignmentTests.cs`, `Features/ListeningInteractions/ListeningTestStory.cs`, và các test bị ảnh hưởng bởi việc mở nhóm 9-10
- **Create (cần bạn duyệt):** `docs/api/age-9-interactions-api-test-guide.html` — trang hướng dẫn test API và test case cho tuổi 9, dựng từ `docs/api/_template-api-test-guide.html`; thư mục `docs/api/` bị `.gitignore` chặn nên cần `git add -f` nếu muốn đưa vào repo.

**Interfaces:**
- Produces: `ListeningAgeSets.TargetAgesFor(AgeBand.Age_9_10) == [9]`; seed idempotent như seed hiện có.

- [ ] **Step 1: Viết test thất bại** — `TargetAgesFor(Age_9_10) == [9]`, `HasOwnSet(9)`, `HasOwnSet(10) == false`; seed tuổi 9 qua luật; truyện 9-10 mới sinh đủ 5 job tuổi 9.
- [ ] **Step 2: Chạy test, thấy thất bại.**
- [ ] **Step 3:** Mở `BuiltAges`; viết seed theo văn bản truyện 902; chạy `dotnet test` toàn bộ rồi sửa các test đang giả định "nhóm 9-10 chưa có bộ".
- [ ] **Step 4:** Sau khi bạn duyệt tạo file: dựng trang từ template (chỉ sửa phần dữ liệu và khối chung); mở trong trình duyệt: không lỗi console, đếm API/test case đúng, vài request JSON dựng đúng tài khoản/URL/body.
- [ ] **Step 5:** `dotnet build StoryPlatform.sln` và `dotnet test StoryPlatform.sln`. Kết quả mong đợi: 0 lỗi build, mọi test đạt (trừ test Postgres bị skip sẵn).

---

## Đối chiếu spec (tự rà)

| Yêu cầu trong tài liệu (mục 9 tuổi) | Task |
|---|---|
| Phiên tòa: tóm tắt 1–2 câu, 3 nhận định, 3–4 lý do hoặc nói riêng, "Bỏ qua", hai mặt theo lý do, lời tự do chỉ lưu + trả chung, lưu cho phụ huynh | 5, 6, 8, 9, 11, 12 |
| Tìm chi tiết vô lý: kể lại có 1 chi tiết đổi, chạm câu/từ, chọn chi tiết đúng trong 3, tô sáng đoạn gốc, sai thì gợi ý + chọn lại 1 lần | 3, 5, 6, 8, 9 |
| Bản đồ quan hệ: 3–4 nhân vật + 3 đường có hướng, 4–5 thẻ, kéo thả vào ô nhãn, nhiều đáp án chấp nhận, giải thích theo sự kiện, thử lại 1 lần; "hiểu lầm" chỉ khi có hiểu lầm rõ | 3, 5, 6, 7, 8, 10 |
| Biên tập viên nhí: đoạn 5–6 câu đọc to, rút gọn còn 3 câu, chạm loại/khôi phục, nhãn ý chính/chi tiết phụ, chấp nhận mọi cách giữ đủ ý chính, gợi ý + sửa 1 lần | 3, 5, 6, 7, 8, 9 |
| "Nếu… thì…": câu hỏi sau quyết định, 3 kết quả hoặc câu riêng, giải thích theo chi tiết truyện, câu riêng chỉ lưu + cảm ơn | 5, 6, 8, 9, 11 |
| Nội dung cho trẻ soạn sẵn, người duyệt xác nhận; mỗi tuổi 5 tương tác riêng; "Bỏ qua"; không điểm/chữ "sai"; lưu cho phụ huynh | Global Constraints, 5, 12 |

## Rủi ro đã biết

- **Nhóm 9-10 lần đầu bị bắt buộc có bộ tương tác:** mọi truyện 9-10 từ 2 đoạn trở lên sẽ không qua bước duyệt/media cho tới khi có bộ tuổi 9 hợp lệ. Truyện cũ cần sinh lại; bé 10 tuổi trong nhóm vẫn không thấy tương tác nào cho tới plan tuổi 10.
- **Lời tự do là dữ liệu của trẻ:** gõ lưu ở attempt, nói lưu ở kho ghi âm; chỉ chủ hồ sơ xem; không log, không đưa vào nội dung cho trẻ khác. Nên có người rà chính sách trước khi bật (ví dụ thời hạn lưu).
- **Bản kể lại và bản đồ quan hệ khó soạn đúng:** AI dễ đổi hơn một chi tiết hoặc gán nhãn quan hệ không có bằng chứng. Luật có bất biến cứng cho bản kể lại và yêu cầu câu bằng chứng cho mỗi đường, nhưng "hợp lý" của giải thích vẫn do người duyệt.
- **Chạm theo câu thay vì theo từ:** tài liệu cho phép "câu hoặc từ"; plan chọn câu để chấm chắc chắn và dễ chạm trên màn hình cảm ứng. Nếu muốn chạm từ, cần chia từ thống nhất như `SentenceSplitter`.
- **Truyện 9-10 mẫu mới (902):** cần nhân bản nhiều dòng seed; nếu bạn muốn bớt công, có thể chỉ seed các bảng tối thiểu đủ cho test luật và trang test API (không cần media thật).
- Chưa có Gemini thật trong môi trường test nên chất lượng nội dung AI của tuổi 9 chỉ kiểm bằng luật và người duyệt.
