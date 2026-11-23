# Quyền của bé (phụ huynh bật/tắt chức năng cho từng con) — Thiết kế

Ngày: 2026-11-23. Nguồn: brainstorming với user, nối tiếp `2026-11-22-phan-quyen-permission-design.md`.

> **Trạng thái:** đã cài trên branch `feat/child-feature-permissions` (backend + frontend), chưa merge. Plan: `docs/superpowers/plans/2026-11-23-quyen-cua-be.md`.

## 1. Mục đích

Phụ huynh quyết định **từng bé** được dùng những chức năng nào trong phiên của bé (ví dụ tắt câu đố, tắt tìm truyện trong thư viện, tắt cảm nhận), giống nút bật/tắt điều ước và giọng nói đã có. Admin không chỉnh quyền của bé: đây là quyết định của gia đình.

Đây là việc thứ nhất trong hai việc user yêu cầu ngày 23/11/2026. Việc thứ hai (Admin cấp chéo quyền giữa Kiểm duyệt viên và Quản trị viên) làm sau, theo spec riêng.

## 2. Hiện trạng

- Phiên của bé dùng `[Authorize(Policy = ChildSession)]` (token có `token_type = child`, `sub` = `childProfileId`). Không dùng permission của người lớn.
- Mỗi bé có một dòng `safety_policies` (entity `SafetyPolicy`, 1–1 với `ChildProfile`). Phụ huynh chỉnh qua `SafetyPolicyController` (`PUT/POST/GET api/v1/SafetyPolicy/{childProfileId}`) và màn `features/safety/SafetyWorkspace.tsx`.
- Các nút đã có và có tác dụng: `StoryWishesEnabled` (chặn trong `StoryWishService`, `ChildWishCardService`), `VoiceAnswersEnabled` (chặn trong `ChildReadAloudService`, `ChildStoryReflectionService`, `ChildStorySummaryService`), `ParentalGateEnabled`, `AllowInteractiveStories`.
- `ChildCreationEnabled` không service nào đọc vì chức năng "bé tự sáng tác" chưa có API: **ngoài phạm vi**, không đụng.
- Giao diện của bé (`features/kid/*`) không đọc các nút này; bị chặn thì chỉ thấy lỗi. Thông tin phiên lấy qua `GET api/v1/ChildAccessCredential/my-session` (`ChildSessionProfileDto`).

## 3. Quyết định của user

| Câu hỏi | Chọn |
|---|---|
| Nhóm chức năng | Cốt lõi luôn bật; Học tập cho tắt kèm cảnh báo; Mở rộng cho tắt tự do |
| Ai chỉnh | Chỉ phụ huynh chủ hồ sơ |
| Lưu ở đâu | **A**: thêm cột `bool` vào `safety_policies`, dùng lại 2 cột đã có |
| Thứ tự | Làm quyền của bé trước, cấp chéo cho nhân viên sau |

## 4. Mục tiêu, không phải mục tiêu, điều cấm

**Mục tiêu**
- G1. Phụ huynh bật/tắt 7 chức năng của từng bé (bảng mục 5).
- G2. Chức năng bị tắt bị chặn ở API của bé, có hiệu lực ngay ở request tiếp theo, bé không cần đăng nhập lại.
- G3. Giao diện của bé ẩn chức năng bị tắt; gọi thẳng API thì nhận thông báo dễ hiểu.
- G4. Mặc định giữ nguyên hành vi hiện tại của mọi bé.

**Không phải mục tiêu**
- Không cho tắt chức năng cốt lõi (đọc truyện, lộ trình, truyện gia đình).
- Không làm chức năng "bé tự sáng tác".
- Không đổi gói/hạn mức: gói quyết định "bao nhiêu", quyền của bé quyết định "có hay không"; bé dùng được khi cả hai cho phép.
- Admin không xem/chỉnh quyền của từng bé.

**Điều cấm**
- A1. Không để việc tắt một chức năng làm mất bước duyệt nội dung của người lớn.
- A2. Không cho phụ huynh khác (không phải chủ hồ sơ) đọc hoặc sửa quyền của bé.
- A3. Không đổi hành vi của bé nào khi deploy (cột mới mặc định `true`, cột cũ giữ nguyên giá trị).

## 5. Danh mục chức năng của bé

| Mã | Nhóm | Cột `safety_policies` | Chặn khi tắt (controller / action) |
|---|---|---|---|
| `child.quiz` | Học tập | `QuizEnabled` (mới) | `ChildQuizController` (cả 3 action) |
| `child.notebook` | Học tập | `NotebookEnabled` (mới) | `ChildNotebookController` (Collect, List, Remove) |
| `child.library.browse` | Mở rộng | `LibraryBrowseEnabled` (mới) | Phần thư viện của `GET child/reading/stories`: `source=library` → 403; để trống → chỉ trả truyện của em. `source=mine` (Truyện em đọc được) không bị chặn. Kiểm trong `ChildStoryBrowseService`, không dùng attribute (sửa 23/11/2026 sau khi test prod thấy tắt thư viện chặn luôn truyện của bé). |
| `child.wishes` | Mở rộng | `StoryWishesEnabled` (có sẵn) | `ChildWishesController` (cả 5 action) |
| `child.voice` | Mở rộng | `VoiceAnswersEnabled` (có sẵn) | `ChildReadingController.ReadAloud`, `ReadAloudPractice`; `ChildStorySummaryController.SendRecording` |
| `child.reflection` | Mở rộng | `ReflectionEnabled` (mới) | `ChildStoryReflectionController` (cả 4 action) |
| `child.rewards` | Mở rộng | `RewardsEnabled` (mới) | `ChildRewardsController` (Get, WaterKnowledgeTree) |

Cốt lõi, không gắn mã: các action còn lại của `ChildReadingController` (bắt đầu phiên, nội dung, tiến độ, hoàn thành, bookmark, yêu thích, nghe, cảm xúc, tín hiệu, sự kiện, nghĩa từ), `MyRoadmapController`, `ChildManualStoriesController`, `ChildPackageAssetsController`, `ChildAccessCredentialController.GetMySession`, `ChildStorySummaryController.Ask`.

Ghi chú: các service đang tự kiểm `StoryWishesEnabled`/`VoiceAnswersEnabled` giữ nguyên (lớp bảo vệ thứ hai, không xóa).

## 6. Thiết kế

### 6.1 Dữ liệu
- Thêm 5 cột `boolean NOT NULL DEFAULT true` vào `safety_policies`: `QuizEnabled`, `NotebookEnabled`, `LibraryBrowseEnabled`, `ReflectionEnabled`, `RewardsEnabled`. Migration `AddChildFeatureToggles`. Seed SQL `safety_policies` thêm cột nếu `SeedDataAlignmentTests` yêu cầu.
- `child.voice` theo `VoiceAnswersEnabled`, vốn mặc định `false` (cần phụ huynh đồng ý), nên với bé mới giọng nói vẫn tắt như trước.
- Bé chưa có dòng `safety_policies` → coi như mọi chức năng bật (giống `ChildWishCardService` hiện coi `policy is null` là không chặn).

### 6.2 Danh mục trong code
- `Features/ChildProfiles/Safety/ChildFeatures.cs`: hằng mã, nhóm (`Học tập`/`Mở rộng`), mô tả tiếng Việt, và hàm đọc trạng thái từ `SafetyPolicy` (`bool IsEnabled(SafetyPolicy? policy, string code)`).
- `IChildFeatureService.IsEnabledAsync(int childProfileId, string code)` đọc `safety_policies` của bé (không cache: một truy vấn theo khóa duy nhất mỗi request, cài đặt đổi là có hiệu lực ngay).

### 6.3 Chặn ở API
- `[RequireChildFeature(ChildFeatures.Quiz)]` (Api) là `IAsyncActionFilter`/`TypeFilterAttribute` đặt trên controller hoặc action ở mục 5, chạy sau authorization `ChildSession`.
- Tắt → filter ném `ChildFeatureDisabledException` (`AppException`, 403); middleware trả `{ success: false, message: "Bố mẹ chưa bật chức năng này.", errors: ["CHILD_FEATURE_DISABLED"] }` như mọi lỗi có mã khác (quyết định khi làm: dùng định dạng lỗi sẵn có thay cho trường `errorCode`/`data.feature` riêng).
- Test ma trận quyền hiện có (`EndpointAuthorizationMatrixTests`) không đổi vì đây là filter, không phải policy.

### 6.4 Phụ huynh chỉnh
- `SafetyPolicyDto` và `SetSafetyPolicyRequestDto` thêm 5 trường mới (nullable trong request: thiếu = giữ nguyên, để client cũ không vô tình tắt).
- Chủ hồ sơ được kiểm như hiện nay (`IChildProfileAccessGuard.EnsureOwnerAsync`). Mọi thay đổi đã được ghi vào nhật ký hồ sơ trẻ (luồng `SafetyPolicy` hiện có).
- `SafetyWorkspace.tsx`: thêm khối "Bé được dùng" gồm 7 công tắc theo 2 nhóm; công tắc nhóm Học tập có dòng cảnh báo "Tắt câu đố thì EXP và tiến độ lộ trình từ câu đố không được tính."

### 6.5 Giao diện của bé
- `ChildSessionProfileDto` thêm `DisabledFeatures: string[]` (mã chức năng đang tắt).
- Kid UI ẩn lối vào tương ứng: câu đố sau truyện (`KidQuiz`), sổ từ vựng, ô tìm truyện (`KidSearchAction`), điều ước, đọc to/tóm tắt bằng giọng nói (`KidReadAloudPractice`, `KidSummary`), cảm nhận (`KidReflection`), phần thưởng (`KidRewards`).
- Gặp lỗi `CHILD_FEATURE_DISABLED` (ví dụ phụ huynh vừa tắt) → hiện thông báo thân thiện, không hiện lỗi kỹ thuật.

## 7. Kiểm chứng
- Test filter: mỗi mã ở mục 5 → bé bị tắt nhận 403 `CHILD_FEATURE_DISABLED`; bé bật hoặc chưa có policy → qua.
- Test phủ: mọi action của bé hoặc thuộc danh sách cốt lõi, hoặc có `[RequireChildFeature]` (không sót action mới).
- Test DTO/service: request thiếu trường mới không đổi giá trị; phụ huynh khác không sửa được.
- Frontend: helper ẩn/hiện theo `disabledFeatures`; trang phụ huynh lưu đúng 5 trường mới.

## 8. Để lại cho người triển khai
- Vị trí chính xác của các lối vào trong kid UI (mục 6.5) đối chiếu khi làm.
- Có cần sửa seed SQL `safety_policies` hay không (theo `SeedDataAlignmentTests`).
- Tên hiển thị tiếng Việt của 7 chức năng trên trang phụ huynh.
