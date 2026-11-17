# Thay đổi: Phụ huynh nghe lại giọng bé đọc to & Cảm nhận sau khi đọc

Ngày: 17/11/2026 · Nhánh: `feat/interactions`

| Phần | Commit | Plan |
|---|---|---|
| 1. Phụ huynh nghe lại giọng bé đọc to | `d8463c8` | `docs/superpowers/plans/2026-11-17-parent-read-aloud-recordings.md` |
| 2. Cảm nhận sau khi đọc (cảm xúc + cảnh yêu thích + lời bé) | `803b1e1` | `docs/superpowers/plans/2026-11-17-post-story-reflection.md` |

Hai phần dùng chung cách lưu ghi âm của "Bé kể lại" (Story Summary): file lưu trên Supabase Storage, phụ huynh nghe qua link ký hết hạn sau 15 phút, file giữ tới khi chủ hồ sơ xóa hoặc xóa tài khoản. Không phần nào dùng AI để đọc/chấm lời bé (ngoài Google STT đã có để chấm đọc to).

---

## 1. Phụ huynh nghe lại giọng bé đọc to

### Nghiệp vụ thay đổi

- **Trước:** khi bé đọc to, âm thanh chỉ dùng trong bộ nhớ để chấm rồi bỏ; phụ huynh chỉ thấy số liệu (chữ đọc tốt / cần đọc lại / bỏ qua).
- **Sau:** hệ thống vẫn chấm như cũ, **đồng thời giữ bản ghi lần đọc gần nhất** của mỗi đoạn (đoạn truyện hoặc đoạn luyện đọc to) trong một phiên. Phụ huynh nghe lại được, kèm chữ của đoạn và điểm.

### Quy tắc

| Quy tắc | Chi tiết |
|---|---|
| Công tắc | Không thêm công tắc mới. Đọc to vốn đã cần `SafetyPolicy.VoiceAnswersEnabled` (chủ hồ sơ đồng ý giọng nói một lần). |
| Số bản ghi | Mỗi đoạn trong một phiên chỉ giữ **lần đọc gần nhất**. Bé đọc lại → file mới thay file cũ, file cũ bị xóa khỏi kho. |
| Upload lỗi | Bé **vẫn nhận kết quả chấm**; chỉ ghi log, lần đó không có bản ghi. Bản ghi cũ (nếu có) giữ nguyên. |
| Lưu DB lỗi | Rollback, xóa file vừa upload, trả lỗi như trước. |
| Bé bấm gửi hai lần cùng lúc | Lưu chạy trong transaction có khóa theo phiên (`AcquireTransactionLockAsync(sessionId)`): request sau chờ request trước, không thêm trùng dòng, không để file ghi âm "mồ côi". |
| Telemetry | Vẫn chỉ ghi số đếm (`AsrWordResult`), không ghi lời bé. |
| Đường dẫn file | `read-aloud/{childProfileId}/{sessionId}/segment-{segmentId}/{guid}.{ext}` hoặc `.../practice-{interactionId}/{guid}.{ext}` |
| Thời gian lưu | Tới khi chủ hồ sơ xóa hoặc xóa tài khoản. Không tự xóa theo thời gian. |

### API

**Phía bé — không đổi request/response**, chỉ đổi hành vi (có lưu bản ghi):
- `POST /api/v1/child/reading/sessions/{sessionId}/segments/{segmentId}/read-aloud`
- `POST /api/v1/child/reading/sessions/{sessionId}/read-aloud-practice/{interactionId}`

**Phía phụ huynh — mới** (role `User`, chỉ chủ hồ sơ):

| Method | Route | Việc |
|---|---|---|
| `GET` | `/api/v1/ParentReadingFeedback/{childProfileId}/sessions/{sessionId}/read-aloud` | Danh sách bản ghi của phiên. Message: `Lấy bản ghi đọc to của con thành công.` |
| `DELETE` | `/api/v1/ParentReadingFeedback/{childProfileId}/sessions/{sessionId}/read-aloud/{recordingId}` | Xóa một bản ghi (xóa file khỏi kho). Message: `Đã xóa bản ghi đọc to.` |

Response `GET` (`ParentReadAloudRecordingsDto`):

```json
{
  "sessionId": 12,
  "recordings": [
    {
      "recordingId": 3, "kind": "Segment", "segmentId": 41, "interactionId": null,
      "sceneIndex": 0, "segmentOrder": 1, "text": "Chữ của đoạn bé đọc",
      "goodCount": 3, "retryCount": 1, "skippedCount": 0, "totalCount": 4, "goodPercent": 75,
      "audioUrl": "<link ký, hết hạn 15 phút>", "recordedAt": "2026-11-17T09:00:00Z"
    }
  ]
}
```

- `kind`: `"Segment"` (đoạn truyện) hoặc `"Practice"` (đoạn luyện sau truyện).
- Thứ tự: đoạn truyện theo trang/đoạn, sau đó đoạn luyện theo thời gian.
- Lỗi: phiên không thuộc bé → 404 `Phiên đọc`; `recordingId` không thuộc phiên → 404 `Bản ghi đọc to`; không phải chủ hồ sơ → bị chặn bởi `EnsureOwnerAsync`.

**Phía phụ huynh — thêm trường** trong `GET /api/v1/ParentReadingFeedback/{childProfileId}` (mỗi dòng):
- `readAloudRecordingCount` (int): số bản ghi đọc to nghe được trong phiên.

### Cơ sở dữ liệu

Migration `20261117093237_AddReadAloudRecordings` — **chỉ tạo bảng mới**, không đổi bảng cũ.

Bảng `read_aloud_recordings`:

| Cột | Ghi chú |
|---|---|
| `ReadingSessionId` | FK `reading_sessions` (Restrict) |
| `ChildProfileId` | dùng cho purge khi xóa tài khoản |
| `StorySegmentId` / `ListeningInteractionId` | đúng **một** trong hai có giá trị (check `CK_read_aloud_recordings_one_target`) |
| `AudioStoragePath` (500), `AudioContentType` (50), `AudioBytes` | file trên Supabase |
| `GoodCount`, `RetryCount`, `SkippedCount`, `TotalCount` | điểm của lần đọc đó |
| `RecordedAt` | thời điểm đọc |

Index: unique (`ReadingSessionId`, `StorySegmentId`) và (`ReadingSessionId`, `ListeningInteractionId`) chỉ tính dòng chưa xóa; index `ChildProfileId`.

### Code chính

- Mới: `ReadAloudRecording` (entity), `ReadAloudRecordingConfiguration`, `ParentReadAloudRecordingService` + `IParentReadAloudRecordingService`, `ParentReadAloudRecordingDtos`.
- Sửa: `ChildReadAloudService` (upload + lưu bản ghi dưới khóa phiên), `ReadAloudPolicy` (`StorageFolder`, `ListenUrlLifetime`), `ParentReadingFeedbackController` (2 endpoint), `ParentReadingFeedbackService` (đếm bản ghi), `AccountDeletionService` (purge file), comment "không lưu âm thanh" ở `ISpeechRecognizer`, `ReadAloudDtos`, `ChildReadingController`.

---

## 2. Cảm nhận sau khi đọc

### Nghiệp vụ thay đổi

- **Trước:** sau khi đọc xong, bé chỉ chọn 1 trong 4 mức thích (Rất thích / Thích / Bình thường / Chưa thích).
- **Sau:** giữ nguyên 4 mức thích, rồi bé làm tiếp 3 bước (đều có thể bỏ qua):
  1. Chọn **1–2 cảm xúc**.
  2. Chọn **1 cảnh thích nhất** (hệ thống nhớ đúng tấm tranh bé đã thấy).
  3. Trả lời **"Điều gì em thích ở cảnh này?"** bằng cách **viết** và/hoặc **kể (ghi âm)**.
- Phụ huynh xem cảm xúc, cảnh (tranh + chữ), đọc/nghe lời bé, xóa lời bé; trang tổng quan đếm số lần mỗi cảm xúc.

### Danh sách cảm xúc

| Mã | Nhãn |
|---|---|
| `Happy` | Vui |
| `Funny` | Buồn cười |
| `Surprised` | Ngạc nhiên |
| `Excited` | Hồi hộp |
| `Touched` | Cảm động |
| `Sad` | Buồn |
| `Scared` | Hơi sợ |

Chỉ nhận đúng tên (không phân biệt hoa thường); từ chối số và chữ lạ.

### Quy tắc

| Quy tắc | Chi tiết |
|---|---|
| Thời điểm | Mọi thao tác ghi của bé chỉ được khi phiên **đã đọc xong** (`Completed`); chưa xong → 409 `Em đọc xong truyện rồi mình chọn cảm nhận nhé!`; phiên bị ngắt → 403. |
| Cảm xúc | 1–2 cái, không trùng; chọn lại thì thay toàn bộ. |
| Cảnh | Phải thuộc truyện của phiên (và manifest nếu là gói gia đình). Đổi sang cảnh khác → lời của cảnh cũ bị xóa (kể cả file). Chọn lại đúng cảnh cũ → giữ lời. |
| Lời bé | Phải đã chọn cảnh. Có ít nhất chữ hoặc ghi âm. Chữ tối đa **200** ký tự (cắt khoảng trắng hai đầu). Ghi âm tối đa **3 MB**, định dạng âm thanh hỗ trợ. Gửi lại thì thay lời cũ. |
| Công tắc | **Không thêm công tắc mới.** Ghi âm cần `VoiceAnswersEnabled`. Khi tắt, bé **vẫn viết được**; API trả `canRecord = false` để app ẩn nút micro. |
| Bé bấm gửi hai lần cùng lúc | Ba thao tác ghi đều chạy dưới khóa theo phiên: không thêm trùng dòng, không để file "mồ côi". |
| Đổi cảnh trong lúc lời đang upload | 409 `Em vừa đổi cảnh khác, em gửi lại lời cho cảnh mới nhé.`; xóa file vừa upload, lời cũ giữ nguyên. |
| Quyền xem | Chỉ chủ hồ sơ; không AI, không kiểm duyệt (giống câu hỏi "Bé kể lại"). |
| Đường dẫn file | `story-reflections/{childProfileId}/{sessionId}/{guid}.{ext}` |

### API phía bé — mới

Route gốc: `/api/v1/child/reading/sessions/{sessionId}/reflection` (policy ChildSession).

| Method | Route | Body | Việc |
|---|---|---|---|
| `GET` | `/reflection` | — | Cảm nhận đã chọn + `canRecord` |
| `PUT` | `/reflection/emotions` | `{ "emotions": ["Happy", "Touched"] }` | Chọn 1–2 cảm xúc |
| `PUT` | `/reflection/favorite-scene` | `{ "sceneId": 12 }` | Chọn cảnh thích nhất (`scenes[].sceneId` của màn đọc) |
| `POST` | `/reflection/note` | multipart: `Text?`, `Audio?` | Gửi lời về cảnh; thành công trả message `Đã gửi cho bố mẹ` |

Response chung (`ChildStoryReflectionDto`):

```json
{
  "sessionId": 12,
  "emotions": [{ "code": "Happy", "label": "Vui" }, { "code": "Touched", "label": "Cảm động" }],
  "favoriteSceneId": 34,
  "noteText": "Con thích mèo con",
  "hasNoteRecording": true,
  "canRecord": true,
  "message": "Đã gửi cho bố mẹ"
}
```

Lỗi chính:

| Tình huống | Mã | Message |
|---|---|---|
| 0 cảm xúc / mã lạ / trùng | 400 | `Em chọn một hoặc hai cảm xúc nhé.` |
| Quá 2 cảm xúc | 400 | `Em chọn tối đa hai cảm xúc thôi nhé.` |
| Cảnh không thuộc truyện | 400 | `Cảnh em chọn không thuộc truyện này.` |
| Gửi lời khi chưa chọn cảnh | 400 | `Em chọn cảnh em thích nhất trước nhé.` |
| Lời rỗng | 400 | `Em viết hoặc kể cho bố mẹ nghe nhé.` |
| Chữ quá 200 ký tự | 400 | `Em viết ngắn hơn một chút nhé.` |
| Ghi âm khi tắt giọng nói | 403 | `Bố mẹ chưa bật đọc bằng giọng nói cho em.` |
| File quá 3 MB | 400 | `Đoạn ghi âm dài quá, em kể ngắn hơn nhé.` |
| Định dạng không hỗ trợ | 400 | `Định dạng ghi âm chưa được hỗ trợ.` |
| Đổi cảnh lúc đang upload | 409 | `Em vừa đổi cảnh khác, em gửi lại lời cho cảnh mới nhé.` |

### API phía phụ huynh

**Mới** (role `User`, chỉ chủ hồ sơ):

| Method | Route | Việc |
|---|---|---|
| `GET` | `/api/v1/ParentReadingFeedback/{childProfileId}/sessions/{sessionId}/reflection` | Cảm xúc, cảnh yêu thích, lời con. Message: `Lấy cảm nhận của con thành công.` |
| `DELETE` | `/api/v1/ParentReadingFeedback/{childProfileId}/sessions/{sessionId}/reflection/note` | Xóa lời con (chữ + file); cảm xúc và cảnh giữ nguyên. Message: `Đã xóa lời của con.` Không có lời → 404 `Lời con`. |

Response `GET` (`ParentStoryReflectionDto`):

```json
{
  "sessionId": 12,
  "emotions": [{ "code": "Scared", "label": "Hơi sợ" }],
  "favoriteScene": { "sceneId": 34, "sceneIndex": 2, "sceneText": "Mèo tìm được nhà.", "illustrationUrl": "<link ký>" },
  "noteText": "Con thích mèo con",
  "noteAudioUrl": "<link ký, hết hạn 15 phút>",
  "noteSentAt": "2026-11-17T09:05:00Z"
}
```

Chưa có cảm nhận → `emotions: []`, các trường khác `null`.

**Thêm trường** vào API có sẵn:
- `GET /api/v1/ParentReadingFeedback/{childProfileId}` (mỗi dòng):
  - `emotions`: `[{ code, label }]`
  - `reflection`: `{ favoriteSceneId, favoriteSceneIndex, hasNoteText, hasNoteRecording }` hoặc `null` khi bé chưa chọn cảnh.
- `GET /api/v1/ParentReadingFeedback/{childProfileId}/overview`:
  - `emotionCounts`: đủ 7 khóa cảm xúc, đếm trên các lần đọc xong trong khoảng thời gian.

Giữ nguyên: `feeling`, `feelingLabel`, `feelingCounts` và `PUT /api/v1/child/reading/sessions/{sessionId}/feeling`.

### Cơ sở dữ liệu

Migration `20261117100303_AddStoryReflections` — **chỉ tạo bảng mới**, không đổi bảng cũ.

Bảng `story_reflections` (một dòng sống mỗi phiên đọc):

| Cột | Ghi chú |
|---|---|
| `ReadingSessionId` | FK `reading_sessions` (Restrict); unique khi chưa xóa (`UX_story_reflections_one_per_session`) |
| `ChildProfileId`, `StoryId`, `StoryVersionId` | phạm vi phiên; `ChildProfileId` có index cho purge |
| `Emotions` | `text[]`, tên cảm xúc (0–2 phần tử) |
| `FavoriteSceneId` | FK `story_scenes` (Restrict) |
| `FavoriteMediaAssetId` | FK `media_assets` (SetNull) — tấm tranh bé đã thấy |
| `NoteText` (200) | chữ bé viết |
| `NoteAudioStoragePath` (500), `NoteAudioContentType` (50), `NoteAudioBytes` | file ghi âm |
| `NoteSentAt` | lúc bé gửi lời |

Mức thích 4 mức **vẫn ở** `reading_sessions.Feeling`.

### Code chính

- Mới: `StoryEmotion` (enum), `StoryReflection` (entity), `StoryReflectionConfiguration`, `StoryEmotionLabels`, `StoryReflectionPolicy`, `StoryReflectionDtos`, `ChildStoryReflectionService` + `IChildStoryReflectionService`, `ParentStoryReflectionService` + `IParentStoryReflectionService`, `ChildStoryReflectionController`.
- Sửa: `ParentReadingFeedbackController` (2 endpoint), `ParentReadingFeedbackService` (`emotions`, `reflection`, `emotionCounts`), `AccountDeletionService` (purge file), `DependencyInjection`.

---

## Phần dùng chung của hai plan

- **Xóa tài khoản:** `AccountDeletionService` sau khi commit sẽ purge lần lượt ghi âm "Bé kể lại", ghi âm đọc to, và lời bé trong cảm nhận của từng hồ sơ con; mỗi loại có try/catch riêng nên lỗi kho lưu trữ không làm hỏng việc xóa tài khoản.
- **`ParentReadingFeedbackController`** giờ nhận 3 service: `IParentReadingFeedbackService`, `IParentReadAloudRecordingService`, `IParentStoryReflectionService`.
- **Khóa theo phiên:** dùng `IUnitOfWork.AcquireTransactionLockAsync(sessionId)` có sẵn. Khóa này dùng chung dãy số với khóa theo id truyện; nếu trùng số chỉ chờ một transaction ngắn.
- **Seed:** `read_aloud_recordings` và `story_reflections` không có dữ liệu seed (cần file ghi âm thật); `SeedDataAlignmentTests` đã ghi nhận hai bảng này.

## Kiểm thử

- Unit test: **4.001/4.001 qua** (build bằng thư mục output riêng). Test mới gồm model, service bé/phụ huynh, controller, xóa tài khoản, và các ca lỗi: upload lỗi, lưu DB lỗi, xóa file lỗi, khóa theo phiên, đổi cảnh lúc đang upload.
- Trang hướng dẫn test API (chỉ sửa phần dữ liệu, đã mở trong trình duyệt, không lỗi console):
  - `docs/api/api-test-tuong-tac-cua-tre.html`: thêm bước "Cảm nhận sau khi đọc" (4 API, 37 test case, có RF37 đổi cảnh lúc upload); cập nhật mô tả 2 API đọc to.
  - `docs/api/ket-qua-doc-phu-huynh-api-test-guide.html`: thêm "Nghe và xóa bản ghi đọc to" và "Cảm nhận sau khi đọc"; thêm `readAloudRecordingCount` vào mẫu response.

## Việc FE cần làm

- Màn kết quả đọc (phụ huynh): hiện nút Nghe khi `readAloudRecordingCount > 0`, gọi `GET .../read-aloud`; hiện cảm xúc và dấu "bé có gửi lời" từ `emotions` / `reflection`.
- Màn sau khi đọc (bé): giữ 7 mã cảm xúc + nhãn (bảng ở trên) và câu giải thích ngắn cho TTS; ẩn micro khi `canRecord = false`; gửi lời bằng `multipart/form-data`; xử lý 409 đổi cảnh bằng cách mời bé gửi lại.
- Link nghe/tranh hết hạn sau 15 phút: gọi lại API khi link hết hạn.

## Còn mở / chưa làm

- **Chưa áp hai migration lên database** và chưa gọi API thật với Postgres + Supabase; việc hai request thật chờ nhau qua khóa mới chỉ kiểm bằng unit test (thứ tự gọi).
- **Chưa chạy integration test.**
- **Văn bản đồng ý giọng nói (mục e)** hiện chỉ nói về nhận dạng giọng nói; nên sửa (FE / tài liệu pháp lý) để nói rõ giọng bé được lưu cho chủ hồ sơ nghe lại và xóa được.
- **Chưa có thông báo** cho phụ huynh khi bé gửi lời cảm nhận (có thể thêm `NotificationType` mới giống `StorySummarySent`).
- **Xóa riêng một hồ sơ con** (không xóa tài khoản) hiện chưa purge ghi âm — áp dụng cho cả "Bé kể lại", đọc to và cảm nhận; nên làm thành việc riêng.
