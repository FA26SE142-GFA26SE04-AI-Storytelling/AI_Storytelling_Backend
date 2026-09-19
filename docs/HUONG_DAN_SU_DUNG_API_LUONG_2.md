# Hướng dẫn chi tiết thực thi API Luồng 2 (Guided AI Story Generation Pipeline)

Tài liệu này cung cấp các bước thực thi API chuẩn từ đầu đến cuối cho **Luồng 2 (Quy trình sinh truyện AI có định hướng)** theo mã nguồn và cơ sở dữ liệu hiện tại của hệ thống `StoryPlatform`.

---

## 1. Thông tin chung & Thiết lập môi trường

* **Base URL:** `http://localhost:5259`
* **Swagger UI:** `http://localhost:5259/`
* **Tài khoản Demo (Parent):**
  * Email / Username: `parent1@example.com`
  * Mật khẩu: `Demo@123`
* **Header bắt buộc cho mọi API nghiệp vụ:**
  ```http
  Authorization: Bearer <access_token>
  Content-Type: application/json
  ```

---

## 2. Quy trình 5 Phase tổng quát

```text
[Phase 1: Input & Safety]
       ↓ (Tạo Story Draft + InputAccepted)
[Phase 2: Sinh & Duyệt Dàn ý (Outline)]  ← Story 11 hiện đang ở đây
       ↓ (Approve Outline)
[Phase 3: AI Sinh Nội dung tuần tự (Story + Vocab + Quiz + Discussion)]
       ↓ (AI hoàn tất → Chuyển sang ContentReview)
[Phase 4: Phụ huynh/Giáo viên Kiểm duyệt & Chỉnh sửa (Review & Edit)]
       ↓ (Validation đạt chuẩn → Approve Story)
[Phase 5: Sinh Media (Minh họa & Giọng đọc TTS)]
       ↓ (Hoàn tất → Story chuyển sang Ready)
```

---

## 3. Chi tiết từng bước thực thi API

---

### BƯỚC 0: Đăng nhập lấy Access Token

* **Endpoint:** `POST /api/v1/auth/login`
* **Request Body:**
  ```json
  {
    "identifier": "parent1@example.com",
    "password": "Demo@123"
  }
  ```
* **Thao tác trên Swagger UI:**
  1. Gọi API `POST /api/v1/auth/login`, sao chép chuỗi `data.accessToken` trong phản hồi.
  2. Bấm nút **Authorize** (biểu tượng ổ khóa màu xanh lá cây ở góc trên bên phải Swagger).
  3. Dán chuỗi token vào ô Value (hoặc nhập `Bearer <access_token>`) rồi bấm **Authorize** -> **Close**.

---

### PHASE 1: Thu thập ngữ cảnh & Khởi tạo truyện

#### 1.1. Lấy ngữ cảnh hồ sơ trẻ (Context)
* **Endpoint:** `GET /api/v1/ai-story-input/children/{childProfileId}/context`
* **Ví dụ:** `GET /api/v1/ai-story-input/children/1/context`
* **Mục đích:** Lấy thông tin độ tuổi (`ageBand`), cấp độ đọc (`readingLevel`), vốn từ vựng (`vocabularyLevel`), sở thích (`interests`) và giới hạn độ dài từ (`maximumLength`) để chuẩn bị tham số tạo truyện.

#### 1.2. Gửi yêu cầu tạo truyện (Khởi tạo Story)
* **Endpoint:** `POST /api/v1/ai-story-input/requests`
* **Request Body mẫu:**
  ```json
  {
    "childProfileId": 1,
    "existingStoryId": null,
    "idempotencyKey": "create-story-an-20260918-001",
    "topic": "Một chuyến phiêu lưu trong khu rừng nấm rực rỡ",
    "genre": "Adventure",
    "characterMode": "specified",
    "characters": [
      "Bé An",
      "Chú Sóc Nâu"
    ],
    "settingMode": "specified",
    "setting": "Khu rừng rợp bóng cây sau cơn mưa rào",
    "lesson": "Biết lắng nghe và giúp đỡ bạn bè khi gặp thử thách",
    "vocabularyLevel": "level_2",
    "language": "vi",
    "targetLength": 650
  }
  ```
* **Kết quả nhận được (`202 Accepted`):**
  * `storyId`: Mã ID của câu chuyện vừa được tạo (ví dụ: `11`).
  * `requestId`: Mã ID của yêu cầu input (ví dụ: `6`).
  * Trạng thái câu chuyện: `storyStatus: "draft"`. Hệ thống tự động tạo job chạy ngầm để sinh dàn ý ban đầu.

---

### PHASE 2: Sinh & Duyệt Dàn ý (Outline Review)

*(Story 11 hiện đã hoàn thành tạo dàn ý và đang ở trạng thái `outline_review`)*

#### 2.1. Kiểm tra dàn ý hiện tại (Poll Progress)
* **Endpoint:** `GET /api/v1/stories/{storyId}/outline`
* **Ví dụ:** `GET /api/v1/stories/11/outline`
* **Response mẫu khi thành công:**
  ```json
  {
    "success": true,
    "message": "Thao tác thành công.",
    "data": {
      "storyId": 11,
      "storyStatus": "outline_review",
      "currentVersion": {
        "id": 11,
        "versionNo": 1,
        "editType": "Initial",
        "title": "An và Chuyến Phiêu Lưu Khu Rừng Nấm Lấp Lánh",
        "opening": "An là một cô bé tò mò, thích khám phá mọi thứ xung quanh. Một buổi sáng đẹp trời...",
        "development": "An và Chú Sóc Nâu cùng nhau bước vào Khu Rừng Nấm Lấp Lánh...",
        "ending": "An và Chú Sóc Nâu vui vẻ trở về nhà, tay trong tay. Chuyến phiêu lưu đã dạy cho An...",
        "isCurrent": true,
        "createdAt": "2026-09-18T09:27:27.293343Z"
      },
      "activeOperation": null,
      "activeJobStatus": null,
      "lastErrorCode": null
    },
    "errors": null
  }
  ```

#### 2.2. Chỉnh sửa dàn ý thủ công (Tùy chọn)
Nếu muốn tự sửa nội dung dàn ý theo ý muốn:
* **Endpoint:** `PUT /api/v1/stories/{storyId}/outline/versions/{versionNo}`
* **Ví dụ:** `PUT /api/v1/stories/11/outline/versions/1`
* **Request Body:**
  ```json
  {
    "title": "An và Chú Sóc Nâu Khám Phá Rừng Nấm",
    "opening": "Bé An và Chú Sóc Nâu bắt đầu chuyến đi vào khu rừng nấm kỳ diệu sau cơn mưa.",
    "development": "Hai bạn vượt qua các con đường nấm phát sáng, học cách lắng nghe nhau để không bị lạc lối.",
    "ending": "Cả hai cùng tìm thấy hạt dẻ vàng và trở về an toàn."
  }
  ```
* *Lưu ý: Thao tác chỉnh sửa sẽ tự động tạo một phiên bản mới (`versionNo: 2`).*

#### 2.3. Yêu cầu AI tạo lại dàn ý (Regenerate - Tùy chọn)
* **Endpoint:** `POST /api/v1/stories/{storyId}/outline/versions/{versionNo}/regenerate`
* **Ví dụ:** `POST /api/v1/stories/11/outline/versions/1/regenerate`
* **Request Body:**
  ```json
  {
    "operationKey": "regen-outline-11-v2"
  }
  ```
  *(Lưu ý: `operationKey` là chuỗi định danh duy nhất, độ dài 8-100 ký tự).*
* **Cơ chế hoạt động (Bất đồng bộ - Asynchronous):**
  1. Khi vừa bấm gửi POST, API trả về ngay **HTTP 202 Accepted** với thông tin tiến trình nền được khởi tạo:
     - `activeOperation`: `"regenerate_outline"`
     - `activeJobStatus`: `"pending"` hoặc `"processing"`
     - `currentVersion`: Tại thời điểm này vẫn hiển thị Version 1 (vì worker mới bắt đầu chạy, chưa xong).
  2. Worker ngầm gọi Gemini để sinh biến thể dàn ý mới (thời gian khoảng 15 - 20 giây).
  3. Sau 15 - 20 giây, gọi lại **`GET /api/v1/stories/{storyId}/outline`**, bạn sẽ nhận được **Version 2** mới (`editType: "AiRegenerated"`) với nội dung dàn ý đã được AI tạo mới hoàn toàn.
  4. Bạn cũng có thể xem toàn bộ lịch sử các phiên bản qua **`GET /api/v1/stories/{storyId}/outline/versions`**.

#### 2.4. Thử lại nếu dàn ý ban đầu gặp lỗi (Retry Initial)
* **Endpoint:** `POST /api/v1/stories/{storyId}/outline/retry`
* **Ví dụ:** `POST /api/v1/stories/11/outline/retry`
* **Request Body:**
  ```json
  {
    "operationKey": "retry-outline-11-v3"
  }
  ```

#### 2.5. Duyệt Dàn ý để chuyển sang sinh nội dung (BẮT BUỘC)
Khi đã hài lòng với dàn ý hiện tại:
* **Endpoint:** `POST /api/v1/stories/{storyId}/outline/versions/{versionNo}/approve`
* **Ví dụ:** `POST /api/v1/stories/11/outline/versions/1/approve`
* **Request Body:**
  ```json
  {
    "approvalKey": "approve-outline-11-v1"
  }
  ```
  *(Lưu ý: `approvalKey` là chuỗi định danh duy nhất, độ dài từ 8 đến 100 ký tự).*
* **Kết quả:** Trả về HTTP `202 Accepted`, hệ thống tự động khởi tạo tiến trình nền của **Phase 3**.

---

### PHASE 3: Theo dõi AI sinh nội dung tuần tự

Sau khi dàn ý được duyệt, hệ thống nền tự động xử lý tuần tự 4 tác vụ:
1. Sinh nội dung toàn bài truyện (`GenerateContent`).
2. Trích xuất và giải nghĩa từ vựng (`GenerateVocabulary`).
3. Tạo bộ câu hỏi trắc nghiệm kiểm tra hiểu biết (`GenerateQuiz`).
4. Xây dựng câu hỏi thảo luận & bài học đạo đức (`GenerateDiscussion`).

* **Endpoint:** `GET /api/v1/stories/{storyId}/generation/progress`
* **Ví dụ:** `GET /api/v1/stories/11/generation/progress`
* **Cách theo dõi:**
  * Gọi API định kỳ mỗi **3 – 5 giây**.
  * Khi phản hồi có `isComplete: true` và `storyStatus: "content_review"`, truyện đã sẵn sàng cho bước kiểm duyệt chi tiết ở Phase 4.

---

### PHASE 4: Kiểm duyệt & Chỉnh sửa chi tiết (Human Review)

#### 4.1. Lấy tổng quan gói kiểm duyệt (Review Summary)
* **Endpoint:** `GET /api/v1/stories/{storyId}/review`
* **Ví dụ:** `GET /api/v1/stories/11/review`
* Cung cấp toàn cảnh về story, các artifact con, cờ quyền chỉnh sửa (`canEdit`) và trạng thái duyệt (`canApprove`).

#### 4.2. Xem & Sửa nội dung truyện (Story Content)
* **Xem:** `GET /api/v1/stories/{storyId}/review/story`
* **Sửa:** `PUT /api/v1/stories/{storyId}/review/story`
* **Body mẫu:**
  ```json
  {
    "versionId": 11,
    "title": "An và Chuyến Phiêu Lưu Khu Rừng Nấm Lấp Lánh",
    "content": "Một buổi sáng trong trẻo sau cơn mưa rào, Bé An tung tăng đến bìa rừng...",
    "lesson": "Biết lắng nghe và sẵn lòng giúp đỡ bạn bè khi gặp thử thách."
  }
  ```

#### 4.3. Xem & Sửa danh sách từ vựng (Vocabulary)
* **Xem:** `GET /api/v1/stories/{storyId}/review/vocabulary`
* **Sửa:** `PUT /api/v1/stories/{storyId}/review/vocabulary`
* **Body mẫu:**
  ```json
  {
    "versionId": 11,
    "items": [
      { "id": null, "term": "kỳ diệu", "definition": "Lạ lùng và đẹp đẽ như trong truyện cổ tích." },
      { "id": null, "term": "thử thách", "definition": "Việc khó khăn cần vượt qua để trưởng thành." },
      { "id": null, "term": "lắng nghe", "definition": "Tập trung nghe để thấu hiểu người khác." },
      { "id": null, "term": "giúp đỡ", "definition": "Làm việc tốt để hỗ trợ bạn bè khi cần." },
      { "id": null, "term": "lấp lánh", "definition": "Phát ra ánh sáng lung linh, đẹp mắt." }
    ]
  }
  ```
  *(Quy chuẩn để duyệt: Tối thiểu 5 từ vựng, mỗi từ phải xuất hiện trong nội dung câu chuyện).*

#### 4.4. Xem & Sửa bộ câu hỏi trắc nghiệm (Quiz)
* **Xem:** `GET /api/v1/stories/{storyId}/review/quiz`
* **Sửa:** `PUT /api/v1/stories/{storyId}/review/quiz`
* **Body mẫu:**
  ```json
  {
    "versionId": 11,
    "items": [
      {
        "id": null,
        "type": "MultipleChoice",
        "question": "Bé An đã gặp ai ở bìa rừng nấm?",
        "correctAnswer": "Chú Sóc Nâu",
        "choices": ["Chú Sóc Nâu", "Bác Gấu Đen", "Bạn Thỏ Trắng"]
      },
      {
        "id": null,
        "type": "TrueFalse",
        "question": "Bé An đã giúp Chú Sóc Nâu tìm lại hạt dẻ vàng quý giá.",
        "correctAnswer": "true",
        "choices": null
      },
      {
        "id": null,
        "type": "ShortAnswer",
        "question": "Bài học lớn nhất mà An học được trong chuyến phiêu lưu là gì?",
        "correctAnswer": "Biết lắng nghe và giúp đỡ bạn bè",
        "choices": null
      }
    ]
  }
  ```
  *(Quy chuẩn để duyệt: Tối thiểu 3 câu hỏi và có đủ cả 3 loại: MultipleChoice, TrueFalse, ShortAnswer).*

#### 4.5. Xem & Sửa câu hỏi thảo luận (Discussion)
* **Xem:** `GET /api/v1/stories/{storyId}/review/discussion`
* **Sửa:** `PUT /api/v1/stories/{storyId}/review/discussion`
* **Body mẫu:**
  ```json
  {
    "versionId": 11,
    "items": [
      { "id": null, "question": "Con thích nhất hành động nào của Bé An trong câu chuyện?", "isMoralLesson": false },
      { "id": null, "question": "Khi bạn của con gặp khó khăn, con sẽ làm gì để giúp đỡ bạn?", "isMoralLesson": true }
    ]
  }
  ```
  *(Quy chuẩn để duyệt: Tối thiểu 2 câu hỏi và có ít nhất 1 câu mang bài học đạo đức `isMoralLesson: true`).*

#### 4.6. Kiểm tra điều kiện phê duyệt (Validation Check)
* **Endpoint:** `GET /api/v1/stories/{storyId}/review/validation`
* **Ví dụ:** `GET /api/v1/stories/11/review/validation`
* **Yêu cầu:** Trường `data.canApprove` phải đạt `true`. Nếu `false`, kiểm tra mảng `issues` để bổ sung phần chưa đạt.

#### 4.7. Phê duyệt chính thức câu chuyện (Approve Story)
* **Endpoint:** `POST /api/v1/stories/{storyId}/review/approve`
* **Ví dụ:** `POST /api/v1/stories/11/review/approve`
* **Kết quả:** Truyện được cập nhật trạng thái `"Approved"`, hệ thống tự động tạo job bàn giao sang Phase 5 để tạo hình ảnh và giọng đọc.

---

### PHASE 5: Theo dõi tiến trình sinh Media (Hình ảnh & Âm thanh)

* **Endpoint:** `GET /api/v1/stories/{storyId}/media/progress`
* **Ví dụ:** `GET /api/v1/stories/11/media/progress`
* **Các chỉ số theo dõi:**
  * `storyStatus`: `"Approved"` → `"MediaProcessing"` → `"Ready"`.
  * `readyIllustrations`: Số lượng hình ảnh minh họa cho các phân cảnh đã tạo xong.
  * `readyAudio`: Số lượng file âm thanh TTS cho từng đoạn đã sinh xong.
  * `isReady`: Chuyển sang `true` khi toàn bộ câu chuyện đã sẵn sàng để phát hành và trải nghiệm trên app di động/web.

---

## 4. Bảng tổng hợp API Luồng 2

| Phase | Nghiệp vụ | Method | Endpoint |
| :--- | :--- | :---: | :--- |
| **Xác thực** | Đăng nhập hệ thống | `POST` | `/api/v1/auth/login` |
| **Phase 1** | Lấy context tạo truyện của trẻ | `GET` | `/api/v1/ai-story-input/children/{childId}/context` |
| **Phase 1** | Gửi yêu cầu khởi tạo truyện | `POST` | `/api/v1/ai-story-input/requests` |
| **Phase 2** | Xem dàn ý hiện tại | `GET` | `/api/v1/stories/{storyId}/outline` |
| **Phase 2** | Chỉnh sửa dàn ý thủ công | `PUT` | `/api/v1/stories/{storyId}/outline/versions/{versionNo}` |
| **Phase 2** | Yêu cầu AI tạo lại dàn ý | `POST` | `/api/v1/stories/{storyId}/outline/versions/{versionNo}/regenerate` |
| **Phase 2** | Thử lại tạo dàn ý ban đầu nếu lỗi | `POST` | `/api/v1/stories/{storyId}/outline/retry` |
| **Phase 2** | **Duyệt dàn ý (Chuyển sang Phase 3)** | `POST` | `/api/v1/stories/{storyId}/outline/versions/{versionNo}/approve` |
| **Phase 3** | Theo dõi AI sinh nội dung tuần tự | `GET` | `/api/v1/stories/{storyId}/generation/progress` |
| **Phase 4** | Xem tổng quan gói kiểm duyệt | `GET` | `/api/v1/stories/{storyId}/review` |
| **Phase 4** | Xem / Sửa nội dung truyện | `GET` / `PUT` | `/api/v1/stories/{storyId}/review/story` |
| **Phase 4** | Xem / Sửa từ vựng | `GET` / `PUT` | `/api/v1/stories/{storyId}/review/vocabulary` |
| **Phase 4** | Xem / Sửa trắc nghiệm Quiz | `GET` / `PUT` | `/api/v1/stories/{storyId}/review/quiz` |
| **Phase 4** | Xem / Sửa câu hỏi thảo luận | `GET` / `PUT` | `/api/v1/stories/{storyId}/review/discussion` |
| **Phase 4** | Kiểm tra điều kiện duyệt trước khi submit | `GET` | `/api/v1/stories/{storyId}/review/validation` |
| **Phase 4** | **Phê duyệt toàn bộ truyện (Sang Phase 5)** | `POST` | `/api/v1/stories/{storyId}/review/approve` |
| **Phase 5** | Theo dõi sinh ảnh & giọng đọc TTS | `GET` | `/api/v1/stories/{storyId}/media/progress` |
