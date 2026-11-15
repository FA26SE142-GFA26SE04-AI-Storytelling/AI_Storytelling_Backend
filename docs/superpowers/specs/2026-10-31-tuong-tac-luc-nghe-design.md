# Tương tác lúc nghe — Thiết kế backend

**Ngày:** 31/10/2026
**Spec nguồn:** "Luồng 3 — Danh sách tương tác của trẻ", mục 5 (Tương tác trong lúc nghe), mục 7 (Quyền từ chối) và phần "Cách AI gắn tương tác vào truyện khi sinh".
**Tài liệu phần AI (giao cho người làm AI):** [Tương tác lúc nghe — Phần AI sinh ở Phase 4](https://claude.ai/artifact/RvB45z8z3vswo2TegtKkAt). Request/response JSON của từng loại lấy theo tài liệu đó.

## 1. Mục tiêu và phạm vi

Trong lúc nghe truyện, sau một số scene, trẻ gặp một tương tác soạn sẵn đã qua duyệt. Trẻ trả lời thì nghe phản hồi, bấm Bỏ qua thì truyện chạy tiếp. Không có câu nào bị "sai", không đếm ngược, không cộng hay trừ EXP.

Trong phạm vi:

- 5 loại tương tác: Điểm dừng (`Checkpoint`), Nói theo nhân vật (`RolePlay`), Điền từ còn thiếu (`FillBlank`), Sắp xếp sự kiện (`EventOrdering`), Đoán tiếp bằng tranh (`PicturePrediction`).
- Sinh ở Phase 4 theo kế hoạch do backend tính, kiểm tra theo luật cố định, sinh lại riêng từng loại.
- Duyệt, moderation, copy revision, báo cáo nội dung.
- Phase 5: cắt scene tại vị trí neo, gắn tương tác vào scene, sinh TTS và ảnh.
- API cho trẻ (lấy tương tác, gửi câu trả lời, chế độ "Chỉ nghe truyện") và cho phụ huynh (câu trả lời cảm xúc, cảm nhận).

Ngoài phạm vi:

- Trả lời điểm dừng bằng giọng nói (ASR). Phản hồi "ý khác" vẫn được sinh và lưu để dùng sau.
- Mục 6 của spec (tóm tắt cuối truyện). Đây là phần riêng, đứng sau tương tác cuối cùng (EventOrdering).
- Ngẫu nhiên hoá tương tác giữa các lần đọc.

## 2. Quyết định đã chốt

| Chủ đề | Quyết định |
|---|---|
| Độ tuổi | Giữ như spec: `RolePlay` chỉ cho nhóm 6–8, `FillBlank` chỉ cho nhóm 9–10. Mỗi truyện có tối đa 4 loại |
| Số lượng | Backend tính kế hoạch theo số chỗ trống và nhóm tuổi, không ngẫu nhiên (mục 4) |
| Truyện ngắn | 2 đoạn: chỉ 1 điểm dừng kiểu `Emotion` hoặc `Opinion`. 1 đoạn: không có tương tác |
| Vị trí | AI chọn đoạn neo. Neo luôn ở cuối một đoạn văn. Phase 5 cắt scene tại đó. Riêng `EventOrdering` luôn neo sau đoạn cuối truyện, do backend tự gán, và hiện trước Quiz |
| Ảnh sắp xếp | Phase 5 cắt scene thêm tại đoạn của từng sự kiện để mỗi thẻ có ảnh riêng |
| Sinh lại | Từ lần thử thứ 2, gửi mã lỗi của lần trước cho AI (`previousErrors`) |
| Độ dài | Câu thoại ≤ 12 chữ, câu điền từ ≤ 15 chữ, mô tả sự kiện ≤ 10 chữ. Một chữ là một tiếng tách bằng khoảng trắng |
| Từ chấm điểm bị chặn | "sai", "chưa đúng", "không đúng", đặt trong cấu hình |
| Lưu dữ liệu | Bảng chung `listening_interactions` + bảng con `listening_interaction_options`, cột rõ nghĩa, không dùng JSON payload |
| Quyền duyệt | Sửa chữ, sinh lại cả loại. Không xoá, không đổi neo, câu trích hay đáp án |
| Xác nhận duyệt tương tác | Bắt buộc người duyệt bấm `review/listening/complete` (chốt 01/11/2026), để người dùng tự rà soát theo ý mình. Đạt luật thôi chưa đủ. Vì vậy truyện có tương tác lúc nghe **không tự xuất bản** dù bật `AutoPublishOnThreshold`; truyện có kế hoạch rỗng hoặc ở chế độ `ReadingMediaOnly` không bị ảnh hưởng |
| Chế độ nghe | "Chỉ nghe truyện" lưu theo từng truyện (`ReadingProgress`). Truyện mới mặc định "Cùng trò chuyện" |
| Lặp lại | Trong một phiên đọc, tương tác đã thử không hỏi lại khi mở tiếp. Phiên mới thì hỏi lại. Bỏ qua không lưu |
| Xếp sai EventOrdering | Chốt 02/11/2026: tới lần sai thứ 3 trong một phiên thì hiện thứ tự đúng và cho đi tiếp, để trẻ nhỏ không bị kẹt |
| Media lỗi | Audio hoặc ảnh của tương tác hỏng sau số lần thử tối đa thì chặn xuất bản, giống TTS của segment |
| `discussion_questions` | Gỡ hẳn (01/11/2026): bỏ endpoint, DTO, moderation và AI; drop bảng bằng migration RemoveDiscussionQuestions. Giữ enum GenerateDiscussion để đọc job cũ |

## 3. Dữ liệu

### 3.1. `listening_interactions`

Mỗi dòng là một tương tác của một `StoryVersion`.

| Cột | Kiểu | Dùng cho | Ghi chú |
|---|---|---|---|
| `Id`, `CreatedAt`, `UpdatedAt`, `IsDeleted` | | tất cả | `BaseEntity`, query filter `!IsDeleted` |
| `StoryVersionId` | int FK | tất cả | `Restrict`, theo `StoryVersion` |
| `Type` | enum `ListeningInteractionType` | tất cả | `Checkpoint = 1`, `RolePlay = 2`, `FillBlank = 3`, `EventOrdering = 4`, `PicturePrediction = 5` |
| `AnchorOffset` | int | tất cả | Vị trí ký tự kết thúc của đoạn neo trong `StoryVersion.Content` |
| `StorySceneId` | int? FK | tất cả | Null tới khi Phase 5 gắn scene |
| `Kind` | enum `CheckpointKind?` | Checkpoint | `Prediction = 1`, `Emotion = 2`, `Opinion = 3` |
| `Prompt` | text? | Checkpoint, PicturePrediction | Câu hỏi |
| `SourceQuote`, `SourceStart`, `SourceEnd` | text?, int?, int? | Checkpoint, RolePlay, FillBlank, PicturePrediction | Câu căn cứ / câu thoại / câu điền từ |
| `CharacterName` | text? | RolePlay | |
| `HiddenWord` | text? | FillBlank | Phải nằm trong `SourceQuote` |
| `Hint` | text? | FillBlank | Không chứa `HiddenWord` |
| `RevealOffset` | int? | PicturePrediction | Vị trí kết thúc của đoạn có đáp án, lớn hơn `AnchorOffset` |
| `Explanation` | text? | Checkpoint | Chỉ người duyệt xem |
| `MatchFeedback` | text? | RolePlay (lời khen), FillBlank, EventOrdering | |
| `NoMatchFeedback` | text? | Checkpoint ("ý khác"), FillBlank (gợi ý), EventOrdering (mời thử lại) | |
| `WaitFeedback` | text? | PicturePrediction | Phát ngay khi trẻ chọn hình |

Ràng buộc DB:

- Unique (`StoryVersionId`, `AnchorOffset`) lọc theo `!IsDeleted`: mỗi đoạn neo tối đa 1 tương tác, nên mỗi scene tối đa 1 tương tác.
- Index (`StoryVersionId`, `Type`).

Cột nào không thuộc `Type` của dòng thì phải null. `ListeningInteractionRules` (mục 5.3) kiểm tra điều này mỗi khi ghi.

### 3.2. `listening_interaction_options`

| Cột | Kiểu | Dùng cho | Ghi chú |
|---|---|---|---|
| `Id`, `CreatedAt`, `UpdatedAt`, `IsDeleted` | | | `BaseEntity` |
| `ListeningInteractionId` | int FK | | `Restrict` |
| `SortOrder` | int | | Unique cùng `ListeningInteractionId` |
| `Text` | text | Checkpoint (thẻ), FillBlank (đáp án chấp nhận), EventOrdering (sự kiện), PicturePrediction (nhãn hình) | |
| `Feedback` | text? | Checkpoint, PicturePrediction | |
| `IsMatch` | bool | Checkpoint, PicturePrediction | Mặc định `false` |
| `CorrectOrder` | int? | EventOrdering | 1, 2, 3 |
| `SourceQuote`, `SourceStart`, `SourceEnd` | text?, int?, int? | EventOrdering | Câu nguyên văn của sự kiện |
| `ImageDescription` | text? | PicturePrediction | Mô tả để sinh ảnh |

`RolePlay` không có option.

### 3.3. `listening_interaction_attempts`

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id`, `CreatedAt`, `UpdatedAt`, `IsDeleted` | | `BaseEntity` |
| `ReadingSessionId` | int FK | |
| `ChildProfileId` | int FK | |
| `ListeningInteractionId` | int FK | |
| `SelectedOptionId` | int? FK | Checkpoint, PicturePrediction |
| `AnswerText` | text? | FillBlank |
| `OrderedOptionIdsJson` | text? | EventOrdering, mảng 3 id |
| `IsMatch` | bool? | FillBlank, EventOrdering, Checkpoint, PicturePrediction (lấy từ option) |
| `AttemptedAt` | timestamp | |

Index (`ReadingSessionId`, `ListeningInteractionId`). Unique theo cặp này cho mọi loại trừ `EventOrdering`. Luật này kiểm tra bằng code, vì `EventOrdering` được thử nhiều lần.

### 3.4. Cột và enum thêm vào bảng có sẵn

| Bảng / enum | Thay đổi |
|---|---|
| `ReadingProgress` | `ListenOnly` bool, mặc định `false` |
| `MediaAsset` | `ListeningInteractionId` int?, `ListeningInteractionOptionId` int?, `AudioPartKey` string? |
| `MediaType` | `ListeningAudio = 4`, `ListeningImage = 5` |
| `GenerationJobOperation` | `GenerateListeningInteraction = 14` |
| `StoryGenerationJob` | `ListeningInteractionType` (enum lưu chuỗi), chỉ có giá trị với operation mới. Unique index cũ (`Operation`, `StoryVersionId`) thêm điều kiện `"Operation" <> 'GenerateListeningInteraction'` vào filter. Thêm unique index (`StoryVersionId`, `ListeningInteractionType`) cho operation mới, để mỗi loại có đúng một job |

Một migration EF Core duy nhất, chỉ thêm bảng và cột, không xoá dữ liệu.

## 4. Kế hoạch tương tác

`ListeningInteractionPlanner` (Application, thuần luật, không I/O) nhận `AgeBand`, danh sách block từ `StoryBlockParser`, và cờ "có câu thoại". Kết quả là danh sách `(Type, Count, Zones)`, với `Zones[i]` là các block mà mục thứ i được phép neo (mục 4.1).

- Chỗ trống = số block − 1. Đây là chỗ cho các loại đặt giữa truyện.
- Thứ tự lấp chỗ: Checkpoint ×2 → RolePlay ×1 (6–8) hoặc FillBlank ×1 (9–10) → PicturePrediction ×1 → thêm Checkpoint tới trần.
- EventOrdering ×1 không chiếm chỗ trống: luôn neo sau block cuối, khi trẻ đã nghe hết truyện, rồi mới tới Quiz. Ba sự kiện chọn từ cả truyện. Loại này đứng cuối kế hoạch và sinh sau cùng. Vì không tranh chỗ với loại khác, thứ tự sinh không gây kẹt.
- Trần: Checkpoint + PicturePrediction ≤ 3 (6–8), ≤ 4 (9–10). Mỗi `Kind` của Checkpoint ≤ 2.
- Ngoại lệ: 1 chỗ trống → Checkpoint ×1. 0 chỗ trống → kế hoạch rỗng, không gọi AI.
- RolePlay chỉ có khi có block chứa câu thoại: có dấu ngoặc kép, hoặc có dòng mở đầu bằng gạch ngang. Không có thì loại này nhường chỗ.
- EventOrdering chỉ có khi có từ 3 block trở lên, để 3 sự kiện ở 3 block khác nhau.

| Chỗ trống | Nhóm 6–8 | Nhóm 9–10 |
|---|---|---|
| 1 | Checkpoint ×1 | Checkpoint ×1 |
| 2 | Checkpoint ×2 + EventOrdering | Checkpoint ×2 + EventOrdering |
| 3 | + RolePlay | + FillBlank |
| 4 | + PicturePrediction | + PicturePrediction |
| 5+ | Giữ nguyên | + Checkpoint thứ 3 |

EventOrdering có từ 2 chỗ trống (3 block) trở lên và không tính vào cột chỗ trống.

Kế hoạch được tính lại mỗi lần sinh hoặc sinh lại, từ nội dung hiện tại của version.

### 4.1. Chia vùng để rải đều tương tác

Mục đích: truyện nhiều block không bị dồn hết tương tác vào vài block đầu, để lại khúc dài không có tương tác.

1. **Chia vùng.** S = số chỗ trống (P1 … P(S)). M = số tương tác giữa truyện = tổng `Count` của mọi loại trừ EventOrdering (planner luôn cho M ≤ S). Vùng k (k = 0 … M−1) gồm block thứ ⌊k·S/M⌋+1 tới ⌊(k+1)·S/M⌋. Các vùng liền nhau, không giao nhau, phủ hết P1 … P(S), chênh nhau tối đa 1 block.
2. **Giao vùng.** Mỗi tương tác giữa truyện nhận một vùng. Loại kén chỗ chọn trước:
   - RolePlay lấy vùng có block thoại; FillBlank lấy vùng có câu ≤ 15 chữ (tách câu theo `.`, `!`, `?`, xuống dòng). Trong các vùng hợp, lấy vùng gần giữa truyện nhất (chỉ số gần (M−1)/2 nhất), hoà thì lấy vùng trước. Không vùng nào hợp thì vẫn lấy vùng gần giữa nhất, để rules báo lỗi nội dung.
   - Checkpoint lấy các vùng còn trống theo thứ tự từ đầu truyện; PicturePrediction lấy vùng còn lại muộn nhất, để đáp án (`RevealOffset`) gần cuối truyện, được là block cuối.
   - EventOrdering không có vùng: luôn sau block cuối.
3. **Kiểm tra.** Request gửi `allowedBlockIds[i]` = vùng của mục thứ i. Neo của mục i ngoài vùng của nó thì báo `ANCHOR_OUT_OF_ZONE` (mục 5.3).

Hai tương tác giữa truyện liền nhau cách nhau tối đa 2·⌈S/M⌉ − 1 block. Ví dụ 15 block, nhóm 6–8, thoại chỉ ở P2:

| Vùng | Block | Giao cho |
|---|---|---|
| 0 | P1–P3 | RolePlay (vùng duy nhất có thoại) |
| 1 | P4–P7 | Checkpoint |
| 2 | P8–P10 | Checkpoint |
| 3 | P11–P14 | PicturePrediction |
| — | sau P15 | EventOrdering |

Vì các vùng không giao nhau, các loại không còn tranh chỗ; `occupiedBlockIds` vẫn gửi để AI biết đoạn nào đã có tương tác.

## 5. Phase 4: sinh và kiểm tra

### 5.1. Luồng job

1. Sau `GenerateQuiz`, chỗ trước đây nối sang `GenerateDiscussion` giờ nối sang `GenerateListeningInteraction`. Mỗi loại trong kế hoạch là một job riêng, mang `ListeningInteractionType`, chạy lần lượt theo thứ tự ưu tiên ở mục 4.
2. Mỗi job gọi `IAIStoryGenerationClient.GenerateListeningInteractionAsync`. Request gồm `count`, `blocks`, `lastBlockId`, `occupiedBlockIds` (neo của các loại đã lưu), `vocabularyTerms` (chỉ FillBlank), `dialogueBlockIds` (chỉ RolePlay), `allowedBlockIds` (vùng của từng mục, mục 4.1) và `previousErrors`. EventOrdering không có `afterBlockId` trong response; mapper tự gán neo là block cuối.
3. `ListeningInteractionMapper` đổi `blockId` thành vị trí ký tự, và tìm từng câu trích bằng tìm nguyên văn trong block tương ứng.
4. `ListeningInteractionRules` kiểm tra theo bảng ở mục 5.3. Đạt thì lưu, trong transaction của story giống `CompleteArtifactAsync`. Không đạt thì thử lại, tối đa `ArtifactMaxAttempts` lần. Từ lần thứ 2 kèm `previousErrors` `{ code, itemIndex, field }`, chỉ mã lỗi, không kèm lại nội dung AI đã viết.
5. Hết số lần thử thì job thất bại với mã `<TYPE>_VALIDATION_FAILED`, và được chạy lại qua cơ chế retry Phase 4 hiện có (`ContentGenerationService.LearningRetry`).
6. Phase 4 hoàn tất khi vocabulary, quiz và **mọi loại trong kế hoạch** đã lưu. Điều kiện `discussionDone` hiện tại được thay bằng điều kiện này. `ReadingMediaOnly` vẫn bỏ qua toàn bộ.

`LearningArtifactService` (sinh lại đồng bộ) và proposal sinh lại khi duyệt đều dùng chung planner, mapper và rules với job.

### 5.2. Hai AI client

Sửa cả AI service (`src/AI`) lẫn `GeminiDirectClient`, theo tài liệu phần AI. Không sửa `DiscussionQuestionDto`, vì `StoryContentDto` và `RuleBasedStoryEvaluationService` đang dùng.

### 5.3. Luật kiểm tra

`ListeningInteractionRules` là thuần luật, có unit test riêng. Phase 4, sửa chữ khi duyệt, và kiểm tra trước khi duyệt đều dùng chung.

| Mã lỗi | Luật | Loại |
|---|---|---|
| `SCHEMA_INVALID` | Thiếu trường hoặc có trường lạ | tất cả |
| `COUNT_MISMATCH` | Số mục khác `count` | tất cả |
| `ANCHOR_INVALID` | Neo không có trong block, đã bị chiếm, hoặc trùng. Loại khác EventOrdering neo ở block cuối, hoặc EventOrdering không neo ở block cuối | tất cả |
| `ANCHOR_OUT_OF_ZONE` | Neo hợp lệ theo `ANCHOR_INVALID` nhưng nằm ngoài vùng của mục đó. Mỗi mục chỉ báo một trong hai mã | tất cả trừ EventOrdering |
| `QUOTE_NOT_FOUND` | Câu trích không có nguyên văn trong block tương ứng | tất cả |
| `QUOTE_AFTER_ANCHOR` | `SourceEnd > AnchorOffset` | Checkpoint, PicturePrediction |
| `KIND_LIMIT` | Một `Kind` quá 2, hoặc không có `Emotion`/`Opinion` nào | Checkpoint |
| `OPTION_COUNT_INVALID` | Prediction/Opinion không phải 2–3, Emotion không phải 4, PicturePrediction không phải 2–3, hoặc chữ thẻ trùng | Checkpoint, PicturePrediction |
| `MATCH_RULE_INVALID` | Prediction/Emotion/PicturePrediction không có đúng 1 `IsMatch`, hoặc Opinion có `IsMatch` | Checkpoint, PicturePrediction |
| `FEEDBACK_MISSING` | Trường phản hồi bắt buộc bị rỗng | tất cả |
| `DIALOGUE_BLOCK_INVALID` | Neo không nằm trong `dialogueBlockIds` | RolePlay |
| `BLANK_INVALID` | `HiddenWord` không có trong câu, đáp án chấp nhận thiếu `HiddenWord`, hoặc `Hint` chứa đáp án | FillBlank |
| `EVENT_ORDER_INVALID` | Không đủ 3 sự kiện ở 3 block khác nhau theo thứ tự tăng dần | EventOrdering |
| `REVEAL_BLOCK_INVALID` | `RevealOffset ≤ AnchorOffset` | PicturePrediction |
| `LENGTH_LIMIT` | Vượt 12/15/10 chữ | RolePlay, FillBlank, EventOrdering |
| `GRADING_WORD` | Có từ chấm điểm bị chặn, so theo nguyên tiếng, không phân biệt hoa thường | tất cả |
| `UNSAFE_CONTENT` | Không qua `IsArtifactSafe` | tất cả |
| `NOT_GROUNDED` | `Prompt` không qua `LearningArtifactGrounding.HasRelevantTerm`. Không áp cho phản hồi (lời khen chung) và chữ trên thẻ (thẻ cảm xúc, hình đoán khác vốn không có trong truyện; sự kiện đã bám truyện bằng câu trích) | Checkpoint, PicturePrediction |
| `STALE_ANCHOR` | Sau khi nội dung version đổi, neo không còn là cuối block hoặc câu trích lệch vị trí | tất cả, chỉ ở bước kiểm tra trước khi duyệt |

`GRADING_WORD` không áp cho `MatchFeedback` của FillBlank và EventOrdering, vì hai loại này được khen "đúng rồi".

## 6. Duyệt và moderation

### 6.1. Endpoint duyệt (`ReviewController`)

| Endpoint | Hành vi |
|---|---|
| `GET review/listening` | Cả gói nhóm theo `Type`, kèm option và `Explanation`, cùng kế hoạch hiện tại |
| `PUT review/listening/{interactionId}` | Sửa chữ: `Prompt`, `Hint`, `Explanation`, `MatchFeedback`, `NoMatchFeedback`, `WaitFeedback`, cùng `Text`/`Feedback` của option. Với FillBlank, `Text` của option được sửa nhưng phải vẫn còn một đáp án bằng `HiddenWord` |
| `POST review/listening/{type}/ai/regenerate` | Sinh lại một loại, tạo proposal theo `ProposalService` hiện có. Apply thì thay toàn bộ dòng của loại đó trong version |
| `POST review/listening/complete` | Đánh dấu đã duyệt gói. **Bắt buộc** khi version có tương tác: `ValidateAsync` chỉ cho duyệt khi gói đã được bấm xác nhận, không dựa vào việc gói đạt luật như từ vựng và quiz. Gói bị sinh lại hoặc sửa chữ sau khi bấm thì phải bấm lại |

Chỉ truyện `Source = Ai` ở chế độ `Learning` có từ 2 đoạn mới có gói tương tác. Truyện có sẵn (luồng 2), truyện 1 đoạn và `ReadingMediaOnly` là "skipped". Truyện AI thiếu ngữ cảnh sinh thì đóng chặn với `LISTENING_CONTEXT_MISSING`. Tương tác bị lệch (`STALE_ANCHOR`) chặn duyệt và không tự sinh lại: người duyệt bấm sinh lại loại đó, hoặc gọi `artifacts/generate` (có `IncludeListening`) để sinh lại cả gói.

Không có endpoint xoá. Trường không được sửa: `Type`, `Kind`, neo, mọi câu trích, `HiddenWord`, `IsMatch`, `CorrectOrder`, `RevealOffset`, `ImageDescription`, `CharacterName`. Gửi lên trường không được sửa thì trả 400 `FIELD_NOT_EDITABLE`.

Sửa xong thì chạy lại các luật `FEEDBACK_MISSING`, `LENGTH_LIMIT`, `GRADING_WORD`, `UNSAFE_CONTENT`, `BLANK_INVALID` cho riêng tương tác đó. Không đạt thì trả 400 `LISTENING_TEXT_INVALID:<CODE>@<field>;...` và không lưu. Luật `NOT_GROUNDED` của câu hỏi cũng chạy, vì luật trước khi duyệt có chạy luật này.

Sửa chữ sau khi Phase 5 đã sinh audio thì audio của đoạn bị sửa được đánh dấu `IsCurrent = false` và sinh lại, giống cách thay media hiện có.

### 6.2. Gỡ câu hỏi thảo luận

Câu thảo luận đã được gỡ theo plan `docs/superpowers/plans/2026-11-01-go-cau-thao-luan.md`. P4a chỉ còn bổ sung phần duyệt tương tác lúc nghe. Job `GenerateDiscussion` cũ được đóng mà không gọi AI.

### 6.3. Moderation, copy revision, báo cáo

- `PublicVersionSnapshot` thêm tương tác cùng option. `PublicVersionComparer` thêm `DiffKeyed(..., "listening", ...)`, khoá theo (`Type`, `AnchorOffset`) và theo `SortOrder` của option. `PublicReviewRequirements` thêm `ListeningPart = "listening"`.
- `CreatorPublicationService` copy tương tác và option sang revision, map lại `Id` của option, `StorySceneId = null`. Media không copy, giống quy tắc hiện tại.
- `ReportedContentBuilder` thêm phần tương tác vào nội dung bị báo cáo, dùng cùng cách che dữ liệu (`Mask`).
- `PublicStoryReviewService` trả `ListeningInteractions` (kèm option, đáp án, audio, ảnh) trong gói duyệt Public Library, và nhận loại phần `listening` khi yêu cầu sửa. Không thêm điều kiện "phải xem" từng tương tác; checklist đã bắt chấm tiêu chí `interaction`.
- Khoá so sánh và dấu vân tay dùng `Type@AnchorOffset` (không dùng Id, vì Id đổi khi chép revision). Version không có tương tác giữ nguyên dấu vân tay cũ.

## 7. Phase 5: scene, TTS, ảnh

### 7.1. Cắt scene

`InteractionSceneBoundaryEnforcer` chạy giữa `_segmenter.SegmentAsync` và `_coverageValidator.ValidateAndAssemble` trong `MediaGenerationService.GetOrCreateScenesAsync`.

- Block bắt buộc là cuối scene gồm: block neo của mọi tương tác, block của từng sự kiện EventOrdering (tìm từ `SourceStart` của option), và block có `RevealOffset` của PicturePrediction.
- Selection chứa block bắt buộc mà block đó không ở cuối thì tách ngay sau block. Các phần tách ra giữ `Focus` cũ, rồi đánh lại `SceneIndex` từ 0 liên tục.
- Thuần luật, có unit test riêng.

### 7.2. Gắn scene

Sau khi lưu scene, mỗi tương tác nhận `StorySceneId` của scene có `TextRangeEnd == AnchorOffset`. Không tìm được thì job lỗi `INTERACTION_SCENE_UNRESOLVED`.

### 7.3. TTS

Làm theo `MediaGenerationService.VocabularyAudio.cs`: mỗi đoạn là một `MediaAsset` loại `ListeningAudio`, qua `_audioQualityGate`, và có cùng cơ chế thử lại, lưu trữ, `IsCurrent`.

| Loại | `AudioPartKey` (option dùng `ListeningInteractionOptionId`) |
|---|---|
| Checkpoint | `prompt`, `noMatch`; option: `text`, `feedback` |
| RolePlay | `match`. Audio mẫu là TTS sẵn có của segment chứa `SourceStart` |
| FillBlank | `hint`, `match`, `noMatch` |
| EventOrdering | `match`, `noMatch`; option: `text` |
| PicturePrediction | `prompt`, `wait`; option: `text`, `feedback` |

### 7.4. Ảnh

- EventOrdering: dùng ảnh minh hoạ của beat đầu tiên trong scene chứa sự kiện. Không sinh thêm.
- PicturePrediction: mỗi option sinh một `MediaAsset` loại `ListeningImage` từ `ImageDescription`, dùng ảnh tham chiếu nhân vật (`ReferenceImageAssetId`), qua cùng kiểm tra an toàn hình ảnh như ảnh scene.

### 7.5. Lỗi media

Asset của tương tác hết số lần thử thì sang `ManualReview`. Gói media chưa được coi là Ready, và truyện chưa xuất bản được cho tới khi asset được sinh lại thành công. Áp dụng như TTS của segment.

## 8. API cho trẻ (`ChildReadingController`)

### 8.1. `GET sessions/{id}/content`

- Gốc response thêm `ListenOnly`.
- Mỗi `ReadingSceneDto` thêm `Interaction?` và `AttemptedInSession`.
- `Interaction` gồm `Id`, `Type`, `Prompt`, `CharacterName`, `ModelAudioUrl` (RolePlay), `MaskedSentence` (FillBlank, do backend tạo từ `SourceQuote` và `HiddenWord`), `Hint`, `WaitFeedback`, `RevealSceneIndex` (PicturePrediction: FE phát phản hồi của hình đã chọn sau khi nghe hết scene này), và các audio URL theo `AudioPartKey`.
- `Options` gồm `Id`, `Text`, `ImageUrl`, `AudioUrl`, cùng `Feedback` và `FeedbackAudioUrl` (chỉ Checkpoint, PicturePrediction). Option của EventOrdering được xáo thứ tự. EventOrdering gắn vào scene cuối; FE hiện nó sau khi nghe hết scene cuối và trước Quiz.
- **Không trả** `Explanation`, `IsMatch`, `CorrectOrder`, đáp án FillBlank, câu trích của EventOrdering.
- Chỉ trả tương tác đã gắn scene và có đủ media Ready.

### 8.2. `PUT sessions/{id}/listen-only`

Body `SetFlagRequestDto { Value }`. Lưu `ReadingProgress.ListenOnly`, làm giống `bookmark` và `favorite`. Trả về giá trị hiện tại.

### 8.3. `POST sessions/{id}/interactions/{interactionId}/attempts`

| Loại | Body | Trả về |
|---|---|---|
| Checkpoint, PicturePrediction | `{ optionId }` | `feedback` + audio của option |
| RolePlay | `{}` | `matchFeedback` + audio |
| FillBlank | `{ answer }` | `matched`, `match` hoặc `noMatch`, đáp án gợi ý khi chưa khớp |
| EventOrdering | `{ orderedOptionIds }` (3 id) | `matched`, phản hồi, `replaySceneIndexes` khi chưa khớp: chỉ scene chứa những sự kiện bị đặt sai vị trí. Từ lần sai thứ 3: thêm `revealed = true` và `correctOrderOptionIds` |

- Kiểm tra: phiên thuộc trẻ hiện tại và đang mở, tương tác thuộc version của phiên, option thuộc tương tác.
- So khớp FillBlank không phân biệt hoa thường và bỏ khoảng trắng thừa.
- Loại khác EventOrdering đã có attempt trong phiên thì trả lại kết quả cũ, không ghi thêm.
- EventOrdering: thẻ ở vị trí i (tính từ 1) bị coi là đặt sai khi `CorrectOrder` của nó khác i. `replaySceneIndexes` là scene chứa `SourceStart` của các sự kiện đặt sai, theo thứ tự truyện, bỏ trùng. Trẻ nghe lại các scene đó rồi xếp lại.
- Mỗi lần xếp của EventOrdering là một dòng `listening_interaction_attempts`, kèm `OrderedOptionIdsJson` và `IsMatch`, lưu trong DB theo `ReadingSessionId`. Số lần sai trong phiên = số dòng `IsMatch = false` của cặp (phiên, tương tác). Không dùng Redis, vì đây là dữ liệu học tập cần giữ lâu, còn Redis trong dự án chỉ làm cache.
- EventOrdering, lần sai thứ 3 trong phiên (chốt 02/11/2026): response có `revealed = true`, `correctOrderOptionIds` (3 id theo `CorrectOrder`), phản hồi là `NoMatchFeedback`, `replaySceneIndexes = []`. FE hiện thứ tự đúng rồi đi tiếp sang Quiz. Tương tác coi như đã thử trong phiên (`AttemptedInSession = true`). Gửi thêm sau đó (đúng hay sai) vẫn lưu dòng mới và vẫn trả `revealed = true`. Lần sai 1–2 không lộ đáp án. Phiên mới đếm lại từ 0. `CorrectOrder` vẫn không trả trong `GET content`. Không cộng/trừ EXP.
- Không đụng EXP, badge, quiz, Hold Mode. Bỏ qua thì FE không gọi API.

## 9. API cho phụ huynh

`GET parent/reading-feedback/{childProfileId}` (`ParentReadingFeedbackService`): mỗi phiên thêm `ConversationAnswers: [{ question, kind, answerText, sceneIndex, answeredAt }]`. Chỉ lấy attempt của Checkpoint kiểu `Emotion` hoặc `Opinion`. `answerText` là `Text` của option đã chọn. Quyền truy cập giữ nguyên kiểm tra hiện có.

## 10. Kiểm thử

- Unit: `ListeningInteractionPlanner` (mọi dòng của bảng mục 4, truyện không có thoại, truyện dưới 3 block, chia vùng và giao vùng ở mục 4.1), `ListeningInteractionRules` (mỗi mã lỗi một ca đạt và một ca hỏng), `ListeningInteractionMapper` (block → offset, câu trích lặp), `InteractionSceneBoundaryEnforcer` (tách giữa, không cần tách, nhiều điểm tách trong một selection).
- Service: job Phase 4 sinh đủ loại, sinh lại riêng một loại, `previousErrors` ở lần thử 2, hết lượt thử; duyệt sửa chữ hợp lệ và bị chặn; proposal sinh lại; copy revision; `STALE_ANCHOR` sau khi sửa nội dung.
- Phase 5: gắn scene đúng, TTS đủ `AudioPartKey`, asset lỗi chặn Ready.
- API: content không lộ đáp án, attempt idempotent, EventOrdering thử lại nhiều lần và chỉ nghe lại scene của sự kiện đặt sai, listen-only lưu theo truyện, phụ huynh chỉ thấy Emotion và Opinion.
- Chạy `dotnet build` và `dotnet test` toàn bộ trước khi báo xong.

## 11. Chia plan

Mỗi plan là một file `docs/superpowers/plans/2026-..-tuong-tac-luc-nghe-pN-....md`, làm và test xong mới sang plan phụ thuộc vào nó.

| Plan | Nội dung | Mục spec | Phụ thuộc | Trạng thái |
|---|---|---|---|---|
| P1 | Dữ liệu, migration, `ListeningInteractionPlanner`, `ListeningInteractionMapper`, `ListeningInteractionRules` | 3, 4, 5.3 | — | Xong (commit `1bc52c5`). Phần sửa "EventOrdering sau đoạn cuối" chưa commit |
| P2 | AI service (`src/AI`: prompt, JSON schema, handler, endpoint) và hai client `AIStoryGenerationClient`, `GeminiDirectClient`, dùng đúng DTO và method interface do P3 tạo. EventOrdering không có `afterBlockId` | 5.2 | Hợp đồng JSON trong tài liệu phần AI; DTO của P3 Task 1 | Chưa làm. Plan (bản có chia vùng): `docs/superpowers/plans/2026-11-01-tuong-tac-luc-nghe-p2-ai-service-va-client.md`. P3 đã xong nên làm được ngay |
| P3 | DTO hợp đồng trong `StoryPlatform.Contracts` và method mặc định `IAIStoryGenerationClient.GenerateListeningInteractionAsync`. Job Phase 4 trong `ContentGenerationService`: nối sau `GenerateQuiz`, một job mỗi loại theo thứ tự kế hoạch, gửi `occupiedBlockIds`, mapper → rules → lưu trong transaction, thử lại kèm `previousErrors`, `LearningRetry`, điều kiện hoàn tất Phase 4 thay `discussionDone`, ngừng sinh `DiscussionQuestion`, từ chấm điểm đưa vào cấu hình | 5.1 | P1 (test dùng client giả) | Xong, chờ P2 để chạy thật với AI. Không deploy trước P2 |
| Gỡ câu thảo luận | Gỡ hẳn tính năng câu thảo luận: endpoint, DTO, luật duyệt, moderation, báo cáo, AI service, entity; migration `RemoveDiscussionQuestions` drop bảng `discussion_questions`; đánh số lại seed. Giữ giá trị enum `GenerateDiscussion` để đóng job cũ không gọi AI | 2, 6.2 | P3 | Xong, migration RemoveDiscussionQuestions đã sinh, chưa áp. Cần sao lưu bảng trước khi áp migration |
| P4a | Duyệt: endpoint `review/listening` trong `ReviewController`, sinh lại đồng bộ (`LearningArtifactService`) và proposal (`ProposalService`), trạng thái artifact `Listening` (`StoryReviewService`), luật trước khi duyệt (khớp kế hoạch, qua rules, không `STALE_ANCHOR`) | 6.1, 6.2 | P3 (dùng chung luồng sinh một loại) | Xong. Đã thêm endpoint duyệt, proposal sinh lại, kiểm gói và cờ xác nhận; 26 test P4a tập trung đạt. `STALE_ANCHOR` chặn duyệt, người duyệt bấm sinh lại thủ công |
| P4b | Moderation và copy: `PublicVersionSnapshot`, `PublicVersionComparer`, `PublicReviewRequirements`, copy sang revision (`CreatorPublicationService`), `ReportedContentBuilder` | 6.3 | P1 | Xong. Build solution thành công; 3148 unit test qua, 147 integration test qua và 12 skip theo môi trường. Phần copy revision đã hoàn tất cùng P4a. |
| P5a | `InteractionSceneBoundaryEnforcer` (thuần luật): cắt scene tại neo, tại block của từng sự kiện EventOrdering, tại block `RevealOffset`. Neo EventOrdering là cuối truyện, nên scene cuối không cần tách | 7.1 | P1 | Xong. Enforcer thuần luật + nối vào `GetOrCreateScenesAsync`; neo lệch thì job lỗi vĩnh viễn `INTERACTION_BOUNDARY_UNRESOLVED`. Scene đã lưu trước P5a không được cắt lại |
| P5b | Phase 5 trong `MediaGenerationService`: gắn `StorySceneId` (EventOrdering vào scene cuối), TTS theo `AudioPartKey`, ảnh PicturePrediction, ảnh thẻ EventOrdering lấy từ scene, media lỗi chặn Ready | 7.2–7.5 | P5a; có dữ liệu tương tác (P3 hoặc seed) | Xong. Gắn scene sau khi tạo scene (`INTERACTION_SCENE_UNRESOLVED`); `ListeningAudio` trong nhánh TTS theo `ListeningAudioParts`; `ListeningImage` chạy chung hàng đợi ảnh, giữ nhân vật qua `MediaContextJson` (pipeline chưa có ảnh tham chiếu); readiness thêm `MissingListeningMedia`. Chưa làm: sinh lại audio khi sửa chữ sau Phase 5 (6.1), tiến độ/Issues chưa đếm media tương tác |
| P6 | API trẻ trong `ChildReadingController`: `Interaction` trên `ReadingSceneDto` (EventOrdering ở scene cuối, FE hiện sau scene cuối và trước Quiz, không lộ đáp án), `listen-only`, attempts (loại khác EventOrdering idempotent; EventOrdering ghi mỗi lần xếp, trả `replaySceneIndexes` của thẻ đặt sai). API phụ huynh: `ConversationAnswers` | 8, 9 | P5b | Xong. `GET content` trả `Interaction`/`AttemptedInSession`/`ListenOnly` và dùng chung `ListeningMediaRequirements` với readiness; `PUT listen-only`; `POST interactions/{id}/attempts` (lần sai thứ 3 của Sắp xếp trả `revealed`, `correctOrderOptionIds`; phiên đã `Completed` trả 409 `LISTENING_SESSION_CLOSED`); phụ huynh có `ConversationAnswers` (Emotion/Opinion). Thêm `ModelSentence` cho RolePlay |

Thứ tự đề xuất:

1. Commit phần sửa P1, rồi làm cùng lúc P3, P4b, P5a. P2 do người làm AI chạy song song từ đầu.
2. Khi P3 xong thì làm P4a. Khi P5a xong thì làm P5b.
3. Khi P5b xong thì làm P6.
4. Chạy P3 với AI client thật sau khi P2 xong.

Ngoài các plan trên:

- Tóm tắt cuối truyện (mục 6 của spec nguồn) làm riêng, đứng sau EventOrdering.
- Đã chốt (02/11/2026): xếp sai EventOrdering tới lần thứ 3 trong phiên thì hiện thứ tự đúng và cho qua (mục 8.3).
