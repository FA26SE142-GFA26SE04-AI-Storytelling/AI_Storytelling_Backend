# Creator Creative Suggestion Engine — hướng dẫn API backend

Ngày: 16/10/2026. Đây là hợp đồng của source triển khai; các đợt chạy database/provider thật được ghi riêng phía cuối tài liệu.

## Điều kiện chạy

- Core API và AI API phải có cấu hình AI service-to-service/provider hợp lệ từ cấu hình hiện hành. Không đưa internal API key vào frontend.
- Đã tạo và áp dụng migration `20261016064348_AddStoryAssistanceTasksAndVersionProvenance` lên `AIStorytellingDB` tại localhost:5432 ngày 16/10/2026, theo yêu cầu riêng của người dùng. Migration thêm story_assistance_tasks, story_assistance_steps và story_versions.AssistanceProvenanceJson, giữ nguyên dữ liệu cũ.
- Môi trường khác cần apply migration mới trước khi chạy source này; không tạo lại hoặc xóa migration cũ.
- Các snapshot/step là dữ liệu runtime. Seed hiện tại chưa có fixture cho hai bảng mới; kiểm tra SeedDataAlignment còn cần cập nhật fixture. Không chạy seed trong thao tác migration này.
- Dùng token tài khoản active, không bị khóa và sở hữu Story có Scope=Creator. Không thêm yêu cầu mua gói/Creator feature flag.

Base route mới: `/api/v1/creator/stories/{storyId}/assistance-tasks`. Tất cả API Core dùng envelope success/message/data/errors hiện hành.

## Quy tắc editor

Apply chỉ sửa editor, có thể undo. Chỉ manual/versions mới ghi StoryVersion. Không có endpoint apply ghi tác phẩm.

Gửi snapshot title/content hiện tại, kể cả chưa lưu. Offset là UTF-16, tính từ 0, không cắt surrogate pair. Chưa normalize newline/whitespace. Client giữ nguyên snapshot đã gửi và snapshotHash server trả.

Trước Apply, so toàn bộ title/content với snapshot nguồn, kiểm tra audience chưa đổi. Nếu editor khác thì giữ gợi ý tham khảo và mở task mới. Không tự rebase patch. Sau Apply, Creator vẫn được sửa tiếp rồi Save; nội dung cuối không phải khớp snapshot AI.

GET detail trả snapshot nhiệm vụ, trạng thái, taskRevision, activeOperation, các active step IDs, activeResult, acceptedAnswer, activeDirection/trial, goal/constraints và context stale flags. Không tự thay editor bằng snapshot hoặc coi task đã lưu tác phẩm. Task Closed chỉ đọc lịch sử.

## Kịch bản Swagger: khám phá viết tiếp

### 1. Chuẩn bị Story

Tạo bằng `POST /api/v1/creator/stories/manual` hoặc chọn Story thuộc tài khoản. GET Story để lấy revisionToken và selectedVersionId. Nếu có version, đọc version để tải content, sau đó thêm phần chưa lưu trong editor tùy ý.

### 2. Tạo nhiệm vụ

POST base route, ví dụ dưới dùng snapshot 15 UTF-16 code units. Thay revisionToken bằng giá trị thực; baseVersionId null chỉ khi Story chưa có version.

```json
{
  "operationId": "cse-001-create",
  "entryPoint": "ContinueWriting",
  "baseVersionId": null,
  "revisionToken": "REVISION_THUC_TE",
  "editorSnapshot": { "title": "Tiệm sửa đồ của Na", "content": "Na muốn sửa xe." },
  "target": { "kind": "cursor", "start": 15, "length": 0 },
  "goal": "Gợi ý bất đồng giữa hai người bạn",
  "constraints": { "keep": ["Na và Nam tôn trọng nhau"], "avoid": ["Chế giễu bạn"] }
}
```

Trả 201, lưu taskId, taskRevision, snapshotHash. Không gọi model và không tạo version.

### 3. Lấy gợi ý

POST `/{taskId}/suggestions`:

```json
{ "operationId": "cse-001-suggest", "expectedTaskRevision": 1, "answerStepId": null }
```

Luôn thay expectedTaskRevision bằng giá trị trả về mới nhất, không mặc định tăng đúng 1: inference tăng revision lúc bắt đầu và kết thúc.

200 có data.step.result.resultType là Options hoặc Clarification. ReviewDraft trả ReviewFindings. Cùng operation/payload đang Running trả 202 kèm Location/pollUrl; GET task để xem. Lặp request không tạo inference mới.

Nếu Clarification, POST `/{taskId}/clarification-answer`:

```json
{
  "operationId": "cse-001-answer",
  "expectedTaskRevision": 3,
  "clarificationStepId": 101,
  "questionId": "ID_CAU_HOI_THUC_TE",
  "letAiPropose": true
}
```

Thay question/step/revision thực. Chọn đúng một trong choiceId, customAnswer hoặc letAiPropose=true. Answer chỉ commit câu trả lời. Tiếp tục POST suggestions với operation mới, revision từ answer và answerStepId = data.stepId của answer. AI lỗi thì câu trả lời vẫn giữ; GET rồi retry suggestions bằng operation mới trên cùng answerStepId.

### 4. Chốt hướng

POST `/{taskId}/direction`:

```json
{
  "operationId": "cse-001-direction",
  "expectedTaskRevision": 3,
  "optionsStepId": 102,
  "optionId": "ID_PHUONG_AN_THUC_TE",
  "directionText": "Hai bạn thử hai cách sửa xe khác nhau nhưng vẫn tôn trọng nhau.",
  "confirmedConstraints": { "keep": [], "avoid": [] }
}
```

Thay IDs/revision thực. Bỏ directionText nếu giữ nguyên option. Bỏ optionId khi nhập hướng riêng. Server trả step.direction.confirmedDirectionText. Không gọi AI. Creator được tự viết tiếp ở đây.

### 5. Sinh đoạn thử tùy chọn

POST `/{taskId}/trials` với operationId mới, expectedTaskRevision mới nhất và directionStepId lấy từ bước 4. Trả data.step.result.patch và snapshotHash.

Patch insert/replace do server dựng theo target nguồn. Hiển thị so sánh rồi Apply trong editor. Không gọi save tự động. Thử lại dùng operation mới; đổi hướng vô hiệu activeTrialStepId cũ nhưng giữ trial history.

Mỗi lượt AI gồm sinh gợi ý và một lần đánh giá an toàn có cấu trúc; chi phí/độ trễ có thể gồm hai lần gọi model. Kiểm tra model không thay duyệt nội dung trước xuất bản.

### 6. Lưu bản cuối

POST `/api/v1/creator/stories/{storyId}/manual/versions` với operationId mới, revisionToken/baseVersionId **hiện tại của Story**, title/content cuối và khai báo nguồn/quyền như luồng manual. Thêm:

```json
{
  "assistanceReferences": [
    { "taskId": 100, "stepId": 103, "usage": "DirectionUsed" },
    { "taskId": 100, "stepId": 104, "usage": "TrialUsed" }
  ]
}
```

Đây chỉ là phần mở rộng payload, không phải toàn bộ request Save. Thay IDs thực; chỉ khai những kết quả Creator sử dụng. Có thể khai hướng mà không có trial. Metadata provider/model lấy từ server, không nhận client tự dựng.

Save thành công mới tạo version. assistance.newReferences mô tả lần lưu này; baseVersionId/restoredFromVersionId truy vết lịch sử. Không có reference mới không đồng nghĩa chưa từng dùng AI. Version cũ metadata null là dữ liệu chưa có theo schema mới. Restore giữ nguồn qua RestoredFromVersionId, không ghi inference mới.

## Hai điểm vào còn lại

SupportSelection: target.kind=selection, length>0 và nhỏ hơn toàn body; gợi ý rồi chốt hướng/trial như trên. Không dùng selection toàn truyện để vượt phạm vi prototype.

ReviewDraft: target.kind=wholeDraft, start=0, length=content.Length. Suggestions trả ReviewReady kể cả findings rỗng, cùng coverage/checks/warnings. Không cho trials trực tiếp. Chọn finding để POST task mới với parentTaskId, sourceReviewStepId, findingId và target cục bộ nằm trong finding. Snapshot/audience phải còn khớp. Server kế thừa constraints explicit và dùng suggestedGoal khi goal trống.

## Hủy, đóng và các lỗi cần xử lý

- Cancel khi Running: POST `/{taskId}/cancel` với operationId mới cho **lệnh hủy**, expectedTaskRevision lấy từ GET và targetOperationId bằng activeOperation.operationId. Không dùng một ID cho hai mục đích.
- Close: POST `/{taskId}/close` với command envelope. Hủy active operation và đóng. GET/steps vẫn đọc được. Muốn tiếp tục sáng tác mở task mới.
- Cancel/Close chỉ cố ngừng provider trên cùng process; database luôn chặn kết quả đến muộn kể cả nhiều instance. Không cam kết provider chưa tính phí.
- 409 TASK_REVISION_CONFLICT: GET task trước khi quyết định gửi lệnh mới. Không tự lặp với operation cũ và payload thay đổi.
- 409 EDITOR_CONTEXT_STALE: Story/audience đã đổi, giữ kết quả tham khảo và mở task mới. Audience cũ không được khai reference hỗ trợ mới khi Save.
- 409 OPERATION_NOT_RUNNING: target đã hoàn tất hoặc khác active operation. GET để xem; không hủy operation khác.
- 429 ASSISTANCE_RATE_LIMIT: giới hạn kỹ thuật, không phải gói thương mại.
- 502 AI_OUTPUT_INVALID/AI_OUTPUT_REJECTED/AI_SERVICE_UNAVAILABLE và 504 AI_TIMEOUT: giữ bản nháp; GET task/steps để lấy revision và trạng thái trước retry bằng operation mới.

Assistance errors có mã ổn định trong errors[0]. Không parse message để quyết định UX. Một receipt thành công replay trả revision lịch sử của receipt; sau đó GET nếu cần trạng thái hiện tại.

## Giới hạn mặc định và kiểm chứng

Cấu hình CreatorAssistance mặc định sau sửa timeout: snapshot 30.000 ký tự, trial 3.000, timeout tổng 120 giây (sinh nội dung + kiểm tra an toàn), lease 150 giây, 10 yêu cầu tạo task hoặc inference/phút/tài khoản (hai bộ đếm riêng). Lease phải >= timeout + 10 giây hoàn tất DB + 5 giây dự phòng; cấu hình không hợp lệ làm startup thất bại. Không có Creator Enabled switch. Nếu môi trường có override cấu hình, cần kiểm tra giá trị hiệu lực khi khởi động lại.

SDK Vertex chỉ gọi tối đa một lần cho mỗi công đoạn Creator, thay vì mặc định năm lần thử. Một lượt hỗ trợ thành công vẫn gồm hai lần gọi riêng: sinh nội dung rồi kiểm tra an toàn. Retry chủ động bằng operation mới; không tự retry sau timeout/cancel. Cấu hình này không đổi chính sách các luồng generation khác. Trần output gồm cả reasoning: 16.384 token cho sinh nội dung và 4.096 token cho đánh giá an toàn; validation ký tự/phạm vi vẫn áp dụng.

Client/proxy cần có ngân sách chờ lớn hơn timeout backend và thời gian hoàn tất trạng thái (ví dụ ít nhất 135 giây với mặc định này), hoặc chủ động xử lý disconnect bằng GET task. Giới hạn 120 giây không bảo đảm provider luôn hoàn tất; 504 vẫn là kết quả hợp lệ khi vượt hạn.

Test code mới dùng fake persistence/provider, có kiểm tra model EF và MVC routes nhưng không tạo DB. Kết quả thực tế xem báo cáo TestResults/creator-assistance. Chưa xác nhận độ trễ/chất lượng model thực hoặc SQL thực thi trên PostgreSQL cho schema mới.

Kết quả ngày 16/10/2026: build Core/AI thành công; 26 test Core mới và 13 test AI mới qua. Hồi quy: 1.031 Core và 36 AI qua, báo cáo `creator-assistance-core-verified.trx` và `creator-assistance-ai-verified.trx` trong TestResults/creator-assistance. Đợt Core cuối loại hai class MigrationChainSchemaTests/SeedDataAlignmentTests khỏi filter: đợt chạy đầy đủ trước đó xác nhận hai assertion schema/seed thất bại do thiếu bảng mới trong migration và seed fixtures. Không sửa/xóa migration, snapshot hoặc nới assertion để làm xanh các kiểm tra này.

### Đợt API thật ban đầu ngày 16/10/2026 (trước sửa timeout)

Đã chạy bằng Core test riêng cổng 15259 (không dừng API người dùng ở 5259), AI service 5260, PostgreSQL localhost:5432/AIStorytellingDB và provider VertexAI/gemini-3.8-flash. Tắt tự động migration/seed trên host test.

- ContinueWriting: tạo task từ snapshot có phần chưa lưu, lấy Options, chốt Direction, sinh Trial và Save V2 có provenance đều thành công. Trial/Save retry không tạo trùng. Task cũ bị chặn sinh tiếp với 409 sau Save; truy cập không token trả 401.
- ReviewDraft: ReviewReady, findings và tạo/đóng task con từ finding thành công, có kế thừa constraints.
- SupportSelection: suggestions trả 504 AI_TIMEOUT ở giới hạn 60 giây; thử lại đúng một lần với operation mới vẫn timeout. Chưa kiểm chứng thành công sinh đoạn replace bằng provider thật. Snapshot còn nguyên, active operation được giải phóng.
- DB xác nhận Story 18 của user 13, Scope Creator/Source Manual/Status Draft, không child profile, không publish. V1 ID 22 không current, V2 ID 23 current; V2 có hai newReferences. Không thêm generation job/media; chỉ có hai version đã lưu.

Lần chạy đầu Story 17/V1 ID 21 phát hiện lỗi EF Core 8/.NET 10 khi dịch array Contains sang truy vấn rate limit. Đã đổi sang Enumerable.Contains rõ ràng, thêm test dùng Npgsql ToQueryString (không mở DB), và chạy qua 56 test Creator/Authoring liên quan, gồm các ca recovery sau sửa Bugbot. Story 17 là dữ liệu test còn giữ để truy vết, không xóa dữ liệu.

Báo cáo đầy đủ, đã loại thông tin xác thực: `TestResults/creator-assistance-live/live-2d51e59b-f783-4382-9f88-41a6815f9821.json`. Kết quả là hai trong ba kịch bản qua, không phải toàn bộ feature đã đạt live test. API đang chạy ở 5259 cần rebuild/restart để nhận bản sửa truy vấn; không tự thay cấu hình timeout để che lỗi chậm ở nhánh selection.

### Sửa cancellation và đo lại timeout ngày 16/10/2026

AI middleware phân biệt request-aborted, TimeoutException và provider tự hủy khi caller còn kết nối. Request-aborted được ghi Information và không ghi response; provider tự hủy không được mặc định coi là timeout. Core dùng token deadline riêng để phân loại AI_TIMEOUT/AI_OPERATION_CANCELLED/AI_SERVICE_UNAVAILABLE, giữ đúng lỗi timeout từ AI service và lưu kết quả lỗi trước khi trả response. Core middleware bỏ qua ghi response nếu caller đã ngắt. Kết quả muộn không được đổi một step đã Failed/Cancelled/Interrupted thành thành công.

Log Core và AI dùng cùng W3C TraceId do HTTP instrumentation của .NET truyền qua. AI đo riêng công đoạn creator_assistance và creator_assistance_safety, gồm EntryPoint/Mode/Outcome/LatencyMs; không ghi prompt, nội dung truyện hoặc output. Prompt hỗ trợ selection được bổ sung TARGET_TEXT tính bởi server từ UTF-16 range đã kiểm tra; vẫn giữ snapshot để kiểm tra ngữ cảnh.

Đợt đo với ngân sách 60 giây (`cancellation-d4a38a22-5398-463e-ac1b-b9df9625f06d.json`) cho thấy SupportSelection sinh gợi ý mất 43,6 giây rồi bị hủy khi safety đã chạy 16,5 giây. Middleware ghi đúng caller-aborted, không Unhandled. ReviewDraft cũng hết tổng 60 giây. Đây là căn cứ tăng ngân sách tổng lên 120 giây và lease 150 giây; không khẳng định việc tăng giới hạn làm model nhanh hơn.

Bản cuối chạy Core 15459 / AI 15460 với VertexAI/gemini-3.8-flash và DB local, tắt migration/seed:

- ContinueWriting → Direction → Trial → Save V2 và replay thành công. Story 23, V1 ID 29, V2 ID 30; chỉ V2 current, có hai tham chiếu hỗ trợ; V1 không đổi.
- Ba task SupportSelection liên tiếp đều sinh Options và Trial replace đúng vị trí. Thời gian suggestions lần lượt 37,6 / 20,2 / 30,8 giây; Trial 19,3 / 11,1 / 9,5 giây. Đây là mẫu đo nhỏ, không phải SLA.
- Lệnh Cancel và việc client ngắt kết nối khi Running: 9 kiểm tra qua, step Cancelled/AI_OPERATION_CANCELLED, không active operation; replay giữ receipt đã hủy, không thêm StoryVersion. AI log không có Unhandled error.
- DB xác nhận vẫn chỉ hai StoryVersion; không có generation job/media của Story 23. Tất cả task đã giải phóng active operation. Task ReviewDraft 14 được giữ để điều tra.
- ReviewDraft trả 502 AI_OUTPUT_INVALID sau khi model trả kết quả khoảng 18,5 giây; không phải timeout. Chưa cô lập nguyên nhân bên trong output validation. Không bỏ validation để chấp nhận kết quả này.

Báo cáo tổng: `TestResults/creator-assistance-live/cancellation-bcbd7a9c-99b5-49fd-a442-0b22bbe37bc5.json`, gồm kết quả PostgreSQL, thời gian từng stage và kiểm chứng cancellation. Kết quả 4/5 kịch bản chính qua, 20 assertions qua, thêm 9 assertions cancellation qua; kịch bản chưa qua là ReviewDraft. Báo cáo cancellation riêng: `TestResults/creator-assistance-live/cancel-check-5c8631ab-9c64-4226-a9a2-186f88ce744c.json`.

Unit/regression: 70 test CreatorAssistance/CreatorAuthoring Core và toàn bộ 46 test AI qua, tại `TestResults/creator-assistance-cancellation/core-cancellation-final.trx` và `ai-cancellation-final.trx`. Kiểm tra bao gồm timeout/replay/retry, lease budget, provider cancellation, upstream error mapping, cancellation tại generation/safety và kết quả muộn sau timeout. Không tạo/apply migration hoặc seed trong đợt sửa này.

Cần rebuild/restart cả Core và AI service đang dùng để nhận bản sửa; không chỉ restart Core. Các host thử nghiệm riêng được dừng sau kiểm chứng. Giữ dữ liệu thử để truy vết, bao gồm Story 19–21 từ các lần kết nối sandbox thất bại và Story 22 từ đợt đo 60 giây.
