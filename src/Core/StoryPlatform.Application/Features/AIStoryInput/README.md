# Phase 1 AI Story Input

## Source mapping

| Khái niệm nghiệp vụ | Thành phần hiện có | Khoảng trống đã xử lý | Vị trí triển khai |
|---|---|---|---|
| Quyền tạo truyện cho child | `SupervisionRelationship`, `SupervisionPermission` | Thiếu quyền riêng cho generation | `Permission.GenerateStory`, `AIStoryInputService.EnsureGeneratePermissionAsync` |
| Context và safety policy | `ChildProfile`, `LearningProfile`, `SafetyPolicy`, organization policy/category | Thiếu effective context dành cho form | `GetContextAsync`, strict policy merge trong `ResolveContextAsync` |
| Story và Generation Request | `Story`, `StoryGenerationJob`; chưa có request | Thiếu idempotency, snapshot và trạng thái input | `StoryGenerationRequest` và EF configuration |
| Guardrail | AI outline có `RequestGuard`, nhưng gọi liền generator | Thiếu gate input độc lập trước outline | `IInputGuardrail`, `RuleBasedInputGuardrail` |
| Handoff outline | `StoryGenerationJob` và internal AI outline endpoint | Chưa có handoff bền vững từ Core | Job `InputValidated` được tạo cùng transaction finalize Allow |
| Form và API client | Không có frontend trong repository | Thiếu public Core API | `AIStoryInputController`; frontend cần tích hợp theo contract này |

## Boundary

Phase 1 tạo một `Story` draft và `StoryGenerationRequest`, chạy input guardrail và, chỉ khi Allow, lưu accepted snapshot cùng một `StoryGenerationJob` ở stage `InputValidated`. Phase 1 không gọi `IAIStoryGenerationClient.GenerateOutlineAsync`, không tạo `StoryVersion` và không thay đổi trạng thái Story khỏi `Draft`.

`StoryGenerationJob` đóng vai trò pending handoff bền vững. Consumer Phase 2 chưa được triển khai; consumer phải tìm request theo `HandoffJobId`, đọc `AcceptedInputJson` và `ContextSnapshotJson`, sau đó gọi Generate Outline theo idempotency của job.

## API

- `GET /api/v1/ai-story-input/children/{childProfileId}/context`
- `POST /api/v1/ai-story-input/requests`
- `GET /api/v1/ai-story-input/stories/{storyId}/requests/{requestId}`
- `POST /api/v1/ai-story-input/stories/{storyId}/requests/{requestId}/retry`

Mở context là read-only. Submit xác định owner/source/status/policy ở backend. Retry giữ request cũ và yêu cầu client gửi lại cùng input để đối chiếu fingerprint.

`TargetLength` và `MaximumLength` dùng đơn vị số từ dự kiến của câu chuyện, không phải token của nhà cung cấp AI. Vượt trần là hard validation error; backend không tự cắt input.

Source hiện chưa có genre catalog hoặc language catalog dùng chung. Vì vậy Phase 1 giới hạn `Genre` thành chuỗi tùy chọn tối đa 50 ký tự và chỉ chấp nhận đúng ngôn ngữ đang cấu hình trên Child Profile. Khi Core bổ sung catalog chính thức, các lựa chọn này cần được thay bằng mã catalog thay vì để AI tự suy đoán.

## Retry and idempotency

- `MaxAttempts = 3`, gồm lần kiểm tra đầu tiên và tối đa hai retry kỹ thuật.
- Policy Block không tự retry; người dùng sửa ý tưởng và tạo request mới trên cùng draft.
- Unique `(SubmittedByUserId, IdempotencyKey)` ngăn gửi trùng giữa các request của cùng người dùng.
- Cùng key nhưng payload khác trả `409 Conflict`.
- Một Story chỉ có một request pending/checking/accepted chờ outline.
- Accepted snapshot chỉ được lưu sau Allow; raw creative input không được ghi trước Allow.
- Attempt checking quá hai phút được chuyển thành `input_check_failed` khi đọc lại tiến độ.

## Guardrail runtime

Adapter hiện tại là deterministic `core-rule-based-input-v1`. Nó kiểm tra category blocked/restricted từ effective policy, prompt injection marker và email/số điện thoại không cần thiết. Không có moderation model provider được cấu hình trong Core ở phase này. Lỗi, timeout, kết quả restricted/không kết luận đều fail closed và không tạo handoff.

## Required database migration

Source thay đổi cần migration cho:

- Bảng `story_generation_requests` và các unique/filter index.
- `stories.Title` nullable để Phase 1 không tạo title giả trước outline.
- `stories.ReadingLevel` nullable integer.
- `stories.VocabularyLevel` nullable varchar(20).

Không có migration nào được tạo hoặc chạy trong thay đổi này. Tên đề xuất: `AddPhase1AIStoryInput`.
