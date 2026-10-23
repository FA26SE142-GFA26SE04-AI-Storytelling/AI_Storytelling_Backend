# Phase 5: Image/TTS độc lập và tạo lại media

## Quy trình

Người dùng vẫn yêu cầu tạo cả gói qua luồng xác nhận nội dung hiện tại. Coordinator chuẩn bị context, scenes, beats và segments rồi tạo hai job con. Image và TTS worker có DbContext, lease, token và lịch retry riêng. Lỗi một nhánh không ngăn nhánh còn lại hoàn tất.

Khi retry, nhánh lỗi chỉ được đưa lại vào hàng chờ trong transaction hoàn tất preparation của coordinator. Nhánh đã hoàn tất được giữ nguyên. Worker gặp job cha đang chờ hoặc đang chuẩn bị sẽ để job con chờ, không hủy job chỉ vì coordinator chưa sẵn sàng.

`MediaProcessing` → đủ media hợp lệ → `MediaReview` → người dùng xác nhận gói → `Ready`.

Creator vẫn gửi Moderator duyệt trước khi công khai; Parent vẫn xác nhận giao cho trẻ. Parent chỉ văn bản không tự phát sinh AI.

## Progress và package

Tiếp tục dùng `GET /api/v1/stories/{storyId}/media/progress` và `/package`, với tài khoản Parent/ContentCreator có quyền truy cập truyện.

Progress giữ các trường cũ, bổ sung `images` và `audio`:

```json
{
  "status": "Pending",
  "requiredCount": 5,
  "readyCount": 3,
  "errorCode": "GEMINI_IMAGE_HTTP_429",
  "nextRetryAt": "2026-10-23T08:00:15Z"
}
```

`Preparing` nghĩa là cấu trúc chưa được chuẩn bị xong; số lượng bằng 0 lúc này không có nghĩa hoàn tất. Lỗi nhánh có thể xuất hiện trong khi `jobStatus` tổng vẫn `Processing`. Khi hai nhánh đã kết thúc mà còn lỗi, job tổng chuyển `Failed`. Gọi API retry sau trạng thái này; không tự gửi retry khi còn đang backoff.

Package bổ sung `replacements`, mỗi item gồm `candidateAssetId`, `previousAssetId`, `type`, `targetId` và `status` (`Generating`, `Failed`, `Applied`, `Kept`). `targetId` là beat ID với Illustration, segment ID với TtsAudio. Các URL xem trước vẫn nằm trên media current, được ký có thời hạn; ứng viên chưa đạt không được đưa vào danh sách media sử dụng.

Số lượng ready tính asset current. Khi đang thay một ảnh cũ đã đạt, readyCount vẫn có thể bằng requiredCount: cần đọc trạng thái job/replacement và `isAwaitingConfirmation`, không suy khả năng confirm chỉ từ số lượng.

## Tạo lại một ảnh hoặc audio

- `POST /api/v1/stories/{storyId}/media/illustration-beats/{beatId}/regenerate`
- `POST /api/v1/stories/{storyId}/media/segments/{segmentId}/regenerate`

```json
{
  "operationId": "replace-image-001",
  "storyVersionId": 38,
  "packageRevision": 1,
  "expectedConfirmed": false
}
```

Các ID, revision và expectedConfirmed lấy từ package hiện tại. `operationId` bắt buộc, tối đa 100 ký tự, mới cho mỗi thao tác logic. Retry request mạng bằng cùng operationId và payload; backend replay, không tạo thêm ứng viên hay tăng revision lần nữa. Đổi payload phải dùng ID mới. Hai endpoint trả `202`, sau đó poll progress/package.

Backend tăng revision gói và hủy xác nhận cũ. Trong lúc sinh bản mới, ảnh/audio cũ tiếp tục current và vẫn xem được. File mới có đường dẫn riêng. Khi bản mới đạt kiểm tra, backend đổi current trong một transaction; bản cũ được giữ trong lịch sử. Chỉ nhánh và target được chọn cần gọi lại provider. Không tạo StoryVersion mới.

Chỉ cho một yêu cầu regenerate hoạt động trên mỗi gói. Không regenerate bản chưa hoàn tất; dùng retry cho lần sinh ban đầu bị lỗi. Gói chờ Moderator/đã phát hành hoặc Parent đã giao tiếp tục chịu quy tắc khóa hiện có.

## Khi tạo lại thất bại

Giữ ảnh/audio cũ. Người dùng có hai lựa chọn:

1. `POST /api/v1/stories/{storyId}/media/retry`: queue lại phần lỗi/chưa hoàn tất, không tăng revision, không tạo lại media đã đạt.
2. `POST /api/v1/stories/{storyId}/media/replacements/{candidateAssetId}/keep-current`:

```json
{
  "operationId": "keep-image-001",
  "storyVersionId": 38,
  "packageRevision": 2
}
```

`candidateAssetId` lấy từ replacements của revision hiện tại. Keep-current chỉ nhận khi yêu cầu thay thế đã thất bại và không còn job đang chạy. Trả `200` cùng progress; chỉ quay lại `MediaReview` nếu phần còn lại của gói đạt readiness. Thao tác này kết thúc yêu cầu thay thế, không khôi phục xác nhận cũ.

Sau khi thay thành công hoặc giữ bản cũ, gọi `POST /api/v1/stories/{storyId}/media/confirm` với `storyVersionId` và `packageRevision` mới. Chưa xử lý xong replacement thì confirm trả `409`.

## Lỗi và kiểm thử Swagger

- `MEDIA_OPERATION_ID_REQUIRED`: thiếu/không hợp lệ operationId; API validation có thể trả 400 trước service.
- `MEDIA_OPERATION_ID_CONFLICT`: operationId đã dùng với payload/actor/version khác.
- `STALE_MEDIA_PACKAGE`: phiên bản, revision hoặc expectedConfirmed không còn khớp.
- `MEDIA_JOB_ACTIVE`: còn công việc đang hoạt động.
- `MEDIA_REPLACEMENT_UNRESOLVED`: chưa kết thúc yêu cầu thay thế.
- `MEDIA_REPLACEMENT_NOT_FAILED`: candidate sai truyện/revision, đã xử lý hoặc chưa thất bại.
- `MEDIA_REGENERATION_NOT_ALLOWED`: chưa ở bước cho phép tạo lại.

Kiểm thử happy path: tạo gói → thấy ảnh/audio hoàn tất → confirm → regenerate một ảnh → ảnh cũ vẫn có trong package khi chờ → chỉ ảnh đó đổi assetId/URL → revision tăng → confirm lại.

Kiểm thử thất bại: gây lỗi provider bằng test double → audio vẫn hoàn tất nếu ảnh lỗi → sửa provider rồi retry; hoặc với regenerate thất bại chọn keep-current → ảnh cũ còn current → confirm revision mới. Không thay policy/consent để làm ca test qua.

## Cấu hình và triển khai

`AI:MediaGeneration:Images` và `Audio` có `Concurrency` (mặc định Image = 2, Audio = 1), `ProviderTimeoutSeconds` (120), `RateLimit` và `CircuitBreaker` riêng. Image concurrency là số tác vụ tranh mỗi tiến trình; mỗi vòng lặp tạo DI scope/DbContext riêng. `AI:MediaGeneration:MaxConcurrentImagesPerStory` mặc định 2, được kiểm tra dưới transaction/story lock trong database, kể cả khi chạy nhiều tiến trình. Cấu hình Images.Concurrency đã có trước vẫn được ưu tiên nếu có giá trị explicit. Shared HTTP rate limiter hiện có vẫn giới hạn tổng request HTTP. Cấu hình timeout của provider hiện hữu cũng được giữ; timeout hiệu lực không vượt giới hạn thấp hơn của transport/provider.

### Tác vụ từng tranh và đo thời gian

Nhánh Image tổng không gọi provider tuần tự cho toàn truyện. Coordinator tạo asset chờ và `MediaAssetGenerationTask` cho từng tranh cần xử lý, sau khi đã chuẩn bị context/scenes/beats. Mỗi tác vụ có claim token, lease và lịch retry riêng. Một tranh lỗi không chặn các tranh còn lại hoặc audio. Nhánh Image chỉ hoàn tất khi mọi tác vụ đã hoàn tất; gói vẫn cần readiness và xác nhận như trước. Retry không tạo thêm tác vụ cho cùng asset. Regenerate tạo candidate asset mới và tác vụ riêng, giữ file cũ đến khi đổi current thành công.

Progress `images` bổ sung `queuedCount`, `runningCount`, `failedCount`. `nextRetryAt` là lịch retry sớm nhất của tác vụ tranh đang chờ. Số lượng media ready vẫn tính theo asset current; không đồng nhất nó với số tác vụ hoàn tất.

Log `Media stage` đo `ImageGeneration`, `ImageAlignment`, `ImageSafety`, `ImageUpload`, `TtsGeneration`, `AudioUpload`; log `Media timing` có queue/total milliseconds, task/story/version/revision/asset/attempt. Provider gate và HTTP limiter ghi thời gian chờ giới hạn request. Dùng logging scope để liên kết task với log provider; không ghi nội dung truyện hoặc credentials. Khi thử nghiệm hiệu năng, báo riêng tổng thời gian gói, thời gian đến ảnh đầu tiên, p50/p95 của từng công đoạn, số tranh, số lần gọi và tỷ lệ 429/retry trên cùng workload.

`AI:MediaGeneration:ParallelImageEvaluation` mặc định false. Khi bật, alignment và safety chạy đồng thời trên cùng ảnh bất biến, cùng cancellation/timeout; cả hai phải đạt trước upload. Chỉ bật sau khi kiểm tra provider/client và so sánh số đo; không giảm số tranh mặc định hoặc bỏ kiểm tra an toàn.

Schema tác vụ ảnh đã được gom cùng học liệu vào [`AddMediaAssetTasksAndLearningArtifactBatches`](../src/Core/StoryPlatform.Infrastructure/Migrations/20261023113952_AddMediaAssetTasksAndLearningArtifactBatches.cs), tạo `media_asset_generation_tasks`, FK/index/unique MediaAssetId. Đã cập nhật snapshot và database local `localhost:5432/AIStorytellingDB` ngày 2026-10-23; không cần tạo riêng migration `AddMediaAssetGenerationTasks`. Các môi trường khác cần áp dụng migration trước khi chạy worker mới. Các job Image đã chuẩn bị ở bản cũ được coordinator đối chiếu, tạo tác vụ còn thiếu và giữ media Ready; không chạy worker cũ/mới đồng thời.

Retry asset được scheduler quản lý; Image/TTS REST không cộng thêm retry transport. Giữ tối đa AssetMaxAttempts (mặc định 3) lần gọi cho một lượt thử. Retry có backoff/jitter, tôn trọng Retry-After. Circuit đang mở không tiêu tốn lượt gọi sinh nội dung. Retry chủ động mở lượt thử mới. Alignment/safety evaluator vẫn giữ pipeline kiểm tra riêng.

Migration [`SplitPhaseFiveMediaJobs`](../src/Core/StoryPlatform.Infrastructure/Migrations/20261023033058_SplitPhaseFiveMediaJobs.cs) đã có trong lịch sử database local được kiểm tra ngày 2026-10-23; các môi trường khác cần kiểm tra riêng. Migration thêm trường job và asset, thay index unique theo asset current, thêm FK/unique cho ứng viên chưa xử lý. `ReplacementResolvedAt` phân biệt ứng viên đã bị bỏ qua với ứng viên còn cần xử lý. Rollback bị chặn nếu đã có job con hoặc dữ liệu thay thế mà schema cũ không thể lưu đầy đủ.

Backfill asset cũ là current theo liên kết đang dùng; giữ RequestedPackageRevision NULL cho media cũ. Không tự chọn một bản nếu dữ liệu cũ có nhiều asset hợp lệ cùng target. Giữ revision và xác nhận gói cũ; không reset hoặc seed thay thế dữ liệu.

Dừng worker cũ, đợi request/lease kết thúc rồi mới áp dụng migration được cho phép và bật ba worker mới. Job cũ chưa hoàn tất sẽ chuẩn bị/tái sử dụng cấu trúc và media đã đạt để tạo hai nhánh còn thiếu. Không chạy hai phiên bản worker đồng thời. Chưa chạy runtime mới trên schema cũ.
