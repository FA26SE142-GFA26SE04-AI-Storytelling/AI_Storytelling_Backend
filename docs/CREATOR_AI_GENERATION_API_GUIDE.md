# AI Story Generation cho Content Creator

## Mục đích và phân quyền

Creator có thể yêu cầu AI tạo truyện qua pipeline 5 phase hiện có: đầu vào → outline → nội dung → review → media.
Đây là generation toàn truyện, khác Creative Suggestion Engine hỗ trợ một bản nháp thủ công.

- Đăng nhập tài khoản `ContentCreator` đang Active, dùng JWT Bearer trên Swagger.
- Creator chọn nhóm tuổi `6-8` hoặc `9-10`, reading level `1..5`, ngôn ngữ `vi` hoặc `en`.
- Story được tạo với `Scope=Creator`, `Source=Ai`, `ChildProfileId=null`, `IsPublished=false`.
- Chỉ chủ sở hữu Creator đang Active được đọc/sửa/duyệt/retry pipeline này. Không mượn hồ sơ trẻ và không tạo consent giả.
- Parent giữ nguyên endpoint đầu vào theo ChildProfile, quyền GenerateStory và consent hiện có.

## 1. Xem cấu hình đầu vào

`GET /api/v1/creator/ai-story-input/context?ageBand=6-8&readingLevel=2&language=vi`

Response cho biết vocabulary level, giới hạn độ dài, ngưỡng an toàn, nhóm nội dung bị chặn và output mode.
Mặc định: tối đa 1.000 từ, safety score tối thiểu 90, yêu cầu review thủ công.
Giới hạn được cấu hình phía server qua `CreatorAuthoring:GenerationMaximumLength` và
`CreatorAuthoring:GenerationSafetyScoreThreshold`; client không được tự sửa policy.
Nhóm chặn lấy từ các ContentCategory đang hoạt động có preset cho nhóm tuổi đã chọn.

## 2. Tạo yêu cầu generation

`POST /api/v1/creator/ai-story-input/requests`

```json
{
  "idempotencyKey": "creator-garden-generation-001",
  "ageBand": "6-8",
  "readingLevel": 2,
  "topic": "Hai người bạn cùng chăm sóc khu vườn",
  "genre": "Đời thường",
  "characterMode": "specified",
  "characters": ["Na", "Nam"],
  "settingMode": "specified",
  "setting": "Khu vườn nhỏ sau nhà",
  "lesson": "Biết chia sẻ công việc và kiên nhẫn",
  "vocabularyLevel": "level_2",
  "language": "vi",
  "targetLength": 500,
  "outputMode": "reading_media_only"
}
```

- `202` với `input_accepted`: input đã được kiểm tra và job outline đã được xếp hàng; chưa có truyện hoàn chỉnh.
- `422`: input bị chặn hoặc cần điều chỉnh. Không có outline job được tạo cho input bị chặn.
- `409`: key trùng với nội dung/đối tượng khác hoặc draft không còn hợp lệ.
- Để replay cùng request, giữ nguyên key và payload. Đổi nội dung/nhóm tuổi phải dùng key mới.
- `existingStoryId` chỉ dùng lại AI draft thuộc chính Creator, chưa có accepted request/job đang hoạt động.
  Không chuyển bản nháp thủ công hoặc truyện Parent qua trường này.
- `outputMode=reading_media_only` bỏ qua vocabulary/quiz/tương tác lúc nghe; `learning` chạy cả chuỗi artifacts.

Theo dõi input:
`GET /api/v1/creator/ai-story-input/stories/{storyId}/requests/{requestId}`.
Nếu `canRetry=true`, dùng `POST` cùng đường dẫn thêm `/retry`, gửi các trường creative input và `retryKey` mới;
giữ nguyên nội dung đã kiểm tra, audience và output mode. Trường hợp thay đổi input thì tạo request mới.

## 3. Xem và duyệt outline

Các phase sau sử dụng chung endpoint `/api/v1/stories/{storyId}`; backend kiểm tra scope và tác giả.
Danh sách/chi tiết `/api/v1/creator/stories` trả thêm `source` và `status` để frontend phân biệt truyện
`Ai` với `Manual`, lựa chọn API biên tập đúng sau khi tải lại trang. Lịch sử vẫn đọc được qua API Creator.

- `GET /outline`: poll tới khi có outline và trạng thái `OutlineReview`.
- `GET /outline/versions`: xem các phiên bản outline.
- `PUT /outline/versions/{versionNo}`: sửa title/opening/development/ending.
- `POST /outline/versions/{versionNo}/regenerate`: tạo lại với `operationKey` mới.
- `POST /outline/versions/{versionNo}/approve`:

```json
{ "approvalKey": "creator-garden-outline-approve-001" }
```

Lấy `versionNo` từ response outline, không nhầm với ID database của StoryVersion.
Chấp nhận outline xếp hàng GenerateContent gắn với đúng phiên outline Creator chọn; đây là quyết định sáng tác, không phải duyệt công khai của Moderator.

## 4. Sinh nội dung và review

- Poll `GET /generation/progress` đến khi `ContentReview`.
- Nếu lỗi có thể retry: `POST /generation/retry` theo schema Swagger.
- `GET /review` và `GET /review/story`: xem gói review và nội dung.
- `PUT /review/story`: sửa nội dung qua schema UpdateStoryRequestDto trong Swagger.
- `POST /review/story/complete`: xác nhận đã xem nội dung.
- Với mode `learning`, rà soát vocabulary/quiz/tương tác lúc nghe và sử dụng các endpoint hoàn tất tương ứng.
- `GET /review/validation`: kiểm tra `canConfirmContent` trước khi xác nhận nội dung; `canApprove=false` cho Creator.
- `POST /api/v1/creator/stories/{storyId}/generation/confirm-content` với body `{ "versionId": 123 }` (ID phiên hiện tại): khi validation đạt, trả 202 và chuyển sang `MediaProcessing`, xếp hàng media. ID cũ trả 409; không đổi trạng thái kiểm duyệt/công khai của phiên bản.

Xử lý đồng thời: confirm thành công trước thì request sửa đang chờ safety trả `409 INVALID_STORY_STATUS`; sửa tạo phiên mới trước thì confirm dùng versionId cũ trả `409 STALE_STORY_VERSION` (không trả nhầm 400 do validation bản mới). Tải lại review/media progress để đồng bộ; không tự retry ghi hoặc tự confirm phiên mới. Backend không giữ transaction/khóa trong lúc chờ safety provider.

Creator không có quyền gọi `/review/approve` hoặc `/review/archive` (403); service cũng chặn Creator approve. Creator không tự động approve/publish qua `auto-publish-check`. Moderator quyết định duyệt/công khai qua Public Story Review.
Đọc/sửa nội dung generation bằng endpoint review; không gửi vào `/creator/stories/{storyId}/manual/versions`.
AI suggestion của bản nháp thủ công vẫn giữ quy trình riêng.

## 5. Media, Ready và gửi kiểm duyệt công khai

- Poll `GET /media/progress`; chỉ coi hoàn thành khi `Ready` và readiness đạt.
- Dùng endpoint media retry/regenerate trong Swagger nếu tiến trình báo lỗi có thể xử lý lại.
- `MediaProcessing` hoặc `202` không đồng nghĩa media đã hoàn tất.
- Generation không tự công khai truyện. Sau Ready, đi qua PublicStorySubmission → Moderator review hiện có.
  Chủ sở hữu vẫn phải đáp ứng các yêu cầu về phiên bản, media và nội dung gửi duyệt.

## 6. Sửa sau khi gửi duyệt hoặc phát hành

Submit thành công khóa phiên nội dung, learning artifacts và media. Không được sửa trực tiếp phiên đã nộp, kể cả regenerate ảnh. Pending có thể withdraw; Claimed phải chờ Moderator. Sau Withdraw/ChangesRequested/Rejected, gọi `POST /api/v1/creator/stories/{storyId}/revisions` với `{ "sourceVersionId": 123 }` để tạo phiên mới.

Đang công khai: `POST /api/v1/creator/stories/{storyId}/removal-requests` với `{ "reason": "Chỉnh sửa phần kết" }`. Gửi yêu cầu chưa gỡ truyện hoặc mở quyền sửa. Moderator nhận mục PublicStoryRemoval ở ModerationQueue rồi gọi `POST /api/v1/moderation/public-story-removals/{itemId}/decision` với approve/reason/rowVersion. Chỉ khi approve gỡ thành công mới tạo revision.

Revision AI về ContentReview, bản cũ giữ nguyên trong lịch sử. Media không được sao chép; sau khi chỉnh sửa phải review/complete/confirm-content, tạo media mới, preview và gửi Moderator duyệt lại. Đọc `GET /api/v1/creator/stories/{storyId}/publication` để kiểm tra trạng thái; dùng versionId/revisionToken mới từ response revision. Xem contract chi tiết tại [API_FRONTEND_ACTOR_FLOWS.md](API_FRONTEND_ACTOR_FLOWS.md).

## Giới hạn và kiểm chứng

Tính năng không thay đổi gói người dùng hoặc tạo cơ chế thanh toán/quota Creator mới.
Quota theo ChildProfile tiếp tục áp dụng cho Parent; không ghi usage vào trẻ bất kỳ cho Creator.
Không có entity/migration mới: audience snapshot được lưu trong ContextSnapshotJson hiện có.

Các automated tests dùng fake persistence/provider và HTTP JWT thật trong host test.
Kết quả đó không chứng minh PostgreSQL, worker/provider trên môi trường triển khai hoặc Swagger live đã chạy thành công.
Để test live cần Core, AI service, workers và provider đã cấu hình; giữ dữ liệu truyện tổng hợp để truy vết.
