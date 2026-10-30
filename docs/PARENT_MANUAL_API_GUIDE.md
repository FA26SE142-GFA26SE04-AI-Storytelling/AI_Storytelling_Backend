# Parent authoring và Creative Suggestion Engine — backend

Cập nhật 19/10/2026: Parent tự viết và dùng AI hỗ trợ trên cùng bản nháp. Phạm vi lần này là backend; chưa tích hợp UI/Reader frontend, chưa tạo/apply migration hoặc chạy API xuống DB thật.

## Trạng thái triển khai

- API Parent riêng: `/api/v1/parent/stories`; API Creator giữ nguyên.
- Hai vai trò dùng chung `ManualAuthoringService`: draft, save, import TXT/DOCX, history, restore, transaction, concurrency, replay. Creator facade giữ hợp đồng hiện hành.
- Parent dùng `Scope=Parent`, `Source=Manual`, `CreateOperationId` của editor và không có `StoryGenerationRequest`, child bắt buộc. Không còn thuộc tính `AuthoringWorkflow`. Chỉ tác giả là chủ hồ sơ child được biên tập.
- Lưu thủ công và kho hướng dẫn không gọi AI. AI hỗ trợ là lựa chọn riêng, không tạo outline hoặc generation job. Các API generation/review/public cũ tiếp tục từ chối bản nháp của editor mới; truyện nhập qua pipeline cũ giữ nguyên luồng cũ.
- MVP backend có package đọc toàn văn theo segments, đủ dữ liệu cho Reader mới tích hợp. Không công bố trải nghiệm frontend hoàn chỉnh trước khi nối Reader và kiểm tra hiển thị toàn văn.
- Listening/combined chưa có hợp đồng upload, kiểm chứng audio thủ công: preparation trả `isReady=false`, `MANUAL_AUDIO_PREPARATION_NOT_AVAILABLE`. Không tự tạo TTS hay bỏ điều kiện audio. Ảnh/media nâng cao chưa triển khai trong MVP này.

## Schema cần migration riêng

Migration đề xuất: `AddParentManualAuthoring`. Cần bổ sung:

- `stories.WritingPurpose` nullable varchar(30). Không tạo cột `AuthoringWorkflow`.
- `parent_story_operations`: receipt/snapshot bền vững, phân biệt Kind `Setup`, `Check`, `ContentConfirmation`, `Preparation`, `FamilyConfirmation`, `FamilyRevocation`.
- Khóa ngoại Story, actor, child và version; unique `(StoryId, ActorUserId, OperationId)`; index theo Story/Kind và Child/Kind.

Các operation không phải bản nội dung mới và không thay `StoryVersion`. Check/confirm/preparation/family grant lưu riêng từng quyết định qua Kind và tham chiếu ID; JSON kết quả là receipt có kiểu. Preparation và grant ghim version và fingerprint ngữ cảnh, không suy ra quyền đọc từ IsCurrent.

Theo AGENTS.md, chỉ cập nhật source model/configuration; không tạo, sửa migration/snapshot hoặc cập nhật DB khi chưa có yêu cầu rõ ràng. Cần migration trước khi chạy source trên DB, kể cả API cũ đọc Story có cột WritingPurpose mới. Kiểm tra schema chain sẽ báo thiếu schema mới cho đến lúc đó.

## Kho hướng dẫn

`GET /api/v1/authoring-guides?locale=vi&purpose=family_memory&group=starting_point&pageIndex=1&pageSize=10`

Yêu cầu token role User. Không nhận child/profile/content và không gọi AI. Nội dung nay lưu trong database và do Moderator/Administrator quản lý (xem `AUTHORING_GUIDE_MANAGEMENT_API_GUIDE.md`); API này chỉ đọc **phiên bản đang phát hành**, không bao giờ trả bản nháp. Phiên bản khởi tạo `parent-vi-1` gồm 12 câu hỏi và 18 mẫu diễn đạt; mỗi lần phát hành sinh `catalogVersion` mới (`parent-vi-2`, ...). Cùng phiên bản/filter/page trả cùng danh sách. Chỉ hỗ trợ catalog tiếng Việt; locale khác trả danh sách rỗng, không thay bằng ngôn ngữ sai. Chưa có bộ đang phát hành → 409 `GUIDE_CATALOG_UNAVAILABLE`. Hỏng kho hướng dẫn không phải dependency của API lưu/import.

Purpose: `free`, `family_memory`, `understanding`, `message`. Group: `starting_point`, `opening`, `events`, `emotion`, `dialogue`, `transition`, `ending`.

`POST /api/v1/authoring-guides/dialogue-1/render`:

```json
{"values":{"person":"Mẹ","words":"Không sao, mẹ con mình cùng lau nhé"}}
```

Trả plain text; không ghi Story hoặc version. FE phải render dạng text, không dùng innerHTML. Chèn vào editor và undo là trách nhiệm frontend; chỉ Save mới lưu version. Không nội suy lặp placeholder trong giá trị do người dùng nhập.

`catalogVersion` là trường tùy chọn trong body render: nếu gửi mà khác phiên bản đang phát hành thì trả 409 `GUIDE_CATALOG_CHANGED` (frontend tải lại danh sách hướng dẫn); không gửi thì render theo bộ hiện hành (client cũ vẫn chạy); mục đã bị loại khỏi bộ hiện hành trả 404.

## Authoring

Tất cả endpoint `/api/v1/parent/stories` yêu cầu token role User, tài khoản active/unlocked, child Active và quyền chủ hồ sơ trẻ còn hiệu lực. API không yêu cầu gói hoặc feature flag. Child ID/scope được backend kiểm tra, không tin scope do client tự gửi.

1. `POST /manual`: body `operationId`, `childProfileId`, title tùy chọn, language mặc định theo child, purpose tùy chọn, experienceMode mặc định `reading`. Tạo Story Draft, chưa version.
2. `GET /{storyId}`: response `data.story` có `revisionToken`, `selectedVersionId`; `data.childProfileId`, `data.purpose` là ngữ cảnh Parent. Detail còn trả `isReadOnly`, `readOnlyReason`, `canUseAiAssistance`, `aiUnavailableReason`. Thiếu consent chỉ chặn AI, không chặn viết tay. Danh sách `GET /` hiện dùng DTO draft chung, detail bổ sung child/purpose.
   Danh sách lọc child Active và quyền chủ hồ sơ hiện hành trước khi phân trang/tính TotalCount; truyện không còn quyền không làm lỗi trang của những con khác.
   `GET /{storyId}/workflow` trả các receipt/kết quả và SourceIsCurrent để phục hồi bước sau reload; không tự lấy receipt lịch sử làm trạng thái hiện hành.
3. `POST /{storyId}/manual/versions`: body giống Creator; `operationId`, `revisionToken`, `baseVersionId`, title/content, sourceDeclaration/rightsDeclaration. Lưu dở được. Có thể gửi assistanceReferences để ghi nhận DirectionUsed/TrialUsed từ tác vụ AI Parent hợp lệ; bỏ trường này hoặc gửi [] khi không khai tham chiếu mới.
4. `GET /{storyId}/content/versions`, `GET /{storyId}/content/versions/{versionId}`.
5. `POST /{storyId}/content/restore`: sourceVersionId là ID muốn phục hồi, baseVersionId là ID hiện hành. Tạo version mới.
6. `POST /{storyId}/imports/preview`: operationId, revisionToken, content. `preview-file` nhận multipart File/RevisionToken/OperationId; TXT UTF-8 hoặc DOCX, tối đa cấu hình import. Chỉ preview, chưa version.
7. `POST /{storyId}/imports/confirm`: operationId/revisionToken/baseVersionId/previewToken/title/content/sourceDeclaration/rightsDeclaration. Nội dung phải khớp preview; token ràng buộc user/story/revision/expiry. Preview được coi là **thay thế body**; bổ sung thì FE ghép và preview lại rõ ràng trước confirm.
8. `PATCH /{storyId}/setup`: operationId/revisionToken/baseVersionId, purpose/language/experienceMode. Purpose không đổi chữ hay làm stale package. Mode/ngôn ngữ/tuổi đổi làm tăng audienceRevision và cần check/preparation mới. Child giữ nguyên; chọn child khác phải tạo tác phẩm/ngữ cảnh riêng được phép.

Retry đúng operationId/payload trả receipt cũ, không tạo thêm version. Đổi payload cùng ID trả conflict. Sau replay cần GET nếu muốn revision hiện tại. Không dùng một ID cho hai loại command. Cùng base/revision ở hai tab: một lần lưu thành công, lần còn lại conflict. Client giữ bản chưa lưu, tải bản mới để đối chiếu.

## Kiểm tra → chọn nội dung → chuẩn bị → xác nhận

Các request dưới đây đều chứa operationId/revisionToken/baseVersionId của Story hiện tại; lấy ID thực từ response trước, không dùng versionNo thay versionId.

1. `POST /{storyId}/content/checks`: kiểm tra version đã lưu, title/body, max length theo policy, keyword categories trên **cả tiêu đề và nội dung** theo **policy đã lưu**, readability khi có threshold. Không ép Lesson và không yêu cầu consent AI. Response: checkId, canPrepare, errors, warnings, coverage, contextFingerprint.
2. `POST /{storyId}/content/confirm`: thêm checkId và checklist với bốn bool `intendedMeaning`, `suitableForChild`, `privacyChecked`, `endingChecked` đều true. Không cho checklist vượt lỗi policy. Trả confirmationId, chưa cấp quyền cho trẻ.
3. `POST /{storyId}/preparations`: thêm contentConfirmationId; segmentEnds tùy chọn. Mặc định dùng paragraph splitter chung; lưu các đoạn bao phủ nguyên vẹn body, kể cả khoảng trắng/newline. Offset UTF-16 `[startOffset,endOffset)`. Nếu tự tách/gộp, gửi toàn bộ exclusive ends tăng dần, phần tử cuối bằng content.Length; không cắt surrogate pair. Không thay chữ tại bước này.
4. `GET /{storyId}/preparations/{preparationId}`: preview có title, language, mode, segments, isReady, missingRequirements. Không ghi reading session/telemetry và không cấp quyền cho trẻ.
5. `POST /{storyId}/family-confirmations`: thêm preparationId. Ngoài quyền authoring, yêu cầu `ApproveStory` và `AssignActivity` (Owner có quyền theo guard hiện hành). Backend kiểm tra current version, fingerprint và readiness; trả confirmationId, storyVersionId, childProfileId, preparationId. Không publish công khai hoặc đặt Story Ready theo pipeline media cũ.
6. `POST /{storyId}/family-confirmations/revoke`: thêm confirmationId để thu hồi grant đó. Các grant khác không bị tự thu hồi.

Nếu safety policy đặt `SafetyScoreThreshold`, keyword scan không đủ căn cứ cấp điểm ngữ nghĩa. Check trả `SEMANTIC_SAFETY_REVIEW_REQUIRED`, chặn confirm cho tới khi có cơ chế review/chính sách được phê duyệt. Không tự đổi ngưỡng để làm happy path. Nhánh xử lý review này chưa có trong MVP; không đánh dấu pass thay.

Check, confirm và preparation ràng buộc version và fingerprint child/policy/learning/context. Sau khi giao truyện, khóa lưu/import-confirm/restore/setup và các thao tác AI phát triển nội dung; trả 409 PARENT_STORY_FINALIZED. Vẫn xem lịch sử, xem bản đã giao, cancel/close tác vụ và thu hồi được.

Thu hồi grant cuối cùng mới mở lại bản nháp, đổi revisionToken và tăng audienceRevision. GET lại detail trước khi sửa; tác vụ AI/check/confirmation/preparation cũ không dùng tiếp. Muốn giao lại phải check → content-confirm → prepare → preview → family-confirm mới. Grant không đọc được do policy/quyền thay đổi không tự mở khóa. Replay đúng request lịch sử vẫn trả receipt, không thực hiện lại tác dụng phụ.

Fingerprint schema 2 bổ sung kiểm tra keyword trong tiêu đề. Các kết quả check/package/grant schema 1 không còn đủ căn cứ: cần kiểm tra, chuẩn bị và xác nhận lại; không chỉ retry operationId cũ để lấy receipt lịch sử rồi dùng tiếp.

## Reader cho child

Các route sau chỉ nhận **child-session token**, không nhận ID child từ query/body:

- `GET /api/v1/child/manual-stories`: các grant hợp lệ, lấy bản xác nhận mới nhất còn hợp lệ của mỗi Story.
- `GET /api/v1/child/manual-stories/{confirmationId}/package`: package được cấp, không tự lấy version current. Trả toàn bộ segments để FE phân trang không cắt mất chữ.

Backend kiểm tra child khớp, grant chưa thu hồi, Story còn tồn tại, child Active, quyền/tài khoản của người cấp còn hợp lệ và policy/context còn khớp. Không gộp đường đọc riêng này vào GET /Story công khai. Không tạo lịch sử/điểm trẻ khi chỉ preview.

## Giới hạn nghiệm thu

- Unit test và model/query translation không chứng minh migration, concurrency PostgreSQL thật hoặc giao diện hoạt động.
- UI editor/catalog insertion/undo/recovery/mobile và Reader frontend chưa triển khai trong lần backend này.
- Upload/thu âm thủ công, xác minh nội dung audio, ảnh và workflow xử lý semantic safety review còn thiếu.
- Giữ API Creator và AI Story Generation hiện hành. Creator và Parent dùng chung engine hỗ trợ; policy truy cập riêng. Không thay đổi gói người dùng hoặc public publishing.
- Không công bố hoàn tất toàn bộ kế hoạch V4 trước migration, nối Reader frontend, kiểm thử API/DB thật và các phần nghiệm thu còn thiếu.

## AI hỗ trợ bản nháp Parent

Tiền tố `A = /api/v1/parent/stories/{storyId}/assistance-tasks`. Role User; tác giả còn quyền chủ hồ sơ trẻ, tài khoản/trẻ active và truyện chưa giao. Consent AI phải hợp lệ theo SafetyPolicy hiện hành. Chủ tài khoản phải xử lý consent qua API policy đã có; không tự bật consent trong request hỗ trợ.

- POST A: CreateAssistanceTaskRequest gồm operationId, revisionToken, baseVersionId, editorSnapshot {title,content}, entryPoint, target {kind,start,length}, goal, constraints {keep,avoid}. Snapshot có thể gồm phần chưa lưu.
- GET A, GET A/{taskId}, GET A/{taskId}/steps: danh sách, trạng thái và lịch sử. Snapshot, activeResult, active step IDs, taskRevision và stale flags giúp phục hồi sau reload.
- POST A/{taskId}/suggestions: operationId, expectedTaskRevision; answerStepId khi tiếp tục từ câu trả lời. AI trả 2–3 options hoặc một clarification.
- POST A/{taskId}/clarification-answer: operationId, expectedTaskRevision, clarificationStepId, questionId và đúng một trong choiceId/customAnswer/letAiPropose. Chỉ lưu câu trả lời, chưa gọi AI; sau đó gọi suggestions bằng operation mới.
- POST A/{taskId}/direction: operationId, expectedTaskRevision, optionsStepId, optionId và/hoặc directionText, confirmedConstraints. Không gọi AI. Parent có thể tự viết tiếp ngay tại đây.
- POST A/{taskId}/trials: operationId, expectedTaskRevision, directionStepId. AI phát triển đoạn thử cục bộ.
- POST A/{taskId}/cancel: operationId riêng cho lệnh hủy, expectedTaskRevision, targetOperationId đang chạy.
- POST A/{taskId}/close: operationId, expectedTaskRevision. Tác vụ đóng chỉ đọc lịch sử.

EntryPoint: ContinueWriting dùng target cursor/length=0; SupportSelection dùng selection với độ dài >0 và nhỏ hơn toàn body; ReviewDraft dùng wholeDraft/start=0/length=body.Length. Offset tính theo UTF-16, không cắt surrogate pair; giữ nguyên newline và khoảng trắng của snapshot. Chỉ đặt con trỏ/bôi đen không tự gọi AI.

ReviewDraft trả ReviewFindings với findings, checksPerformed, coverage và warnings, không phải chứng nhận an toàn. Chọn một finding để tạo task mới với parentTaskId/sourceReviewStepId/findingId và target nằm trong finding. Snapshot, audience và policy phải còn khớp.

Core lấy policy của trẻ, blocked/restricted categories và reading level, lưu snapshot/fingerprint trong task.ContextJson và gửi phần policy cần thiết sang AI. Không gửi ID/tên thật của trẻ cho model. Kiểm tra quyền và policy trước lượt chạy và lúc nhận kết quả; policy/consent/ngôn ngữ thay đổi trong lúc chạy khiến kết quả bị từ chối. Không lấy kết quả ReviewDraft thay thế content/checks hoặc quy trình giao cho con. SafetyScoreThreshold vẫn có thể chặn hoàn tất theo giới hạn kiểm tra thủ công đã nêu.

Mỗi lượt AI có sinh nội dung và kiểm tra an toàn đầu ra. Tái sử dụng cấu hình CreatorAssistance, timeout/cancel/rate limit hiện có; không thêm cờ bật/tắt feature hoặc gói. Không ghi prompt/nội dung/response model vào log.

### Áp dụng gợi ý và lưu

FE so editor hiện tại với snapshot nguồn và kiểm tra stale flags trước Apply. Nếu editor đã đổi, giữ gợi ý tham khảo hoặc mở task mới; không tự rebase patch. Sau Apply, Parent được sửa thêm bằng tay. Gọi API manual/versions với revisionToken/baseVersionId hiện hành và nội dung cuối. Chỉ Save tạo StoryVersion:

```json
"assistanceReferences": [
  {"taskId": 1, "stepId": 2, "usage": "DirectionUsed"},
  {"taskId": 1, "stepId": 3, "usage": "TrialUsed"}
]
```

Đây là phần bổ sung payload Save, dùng ID thật. Backend kiểm tra đúng user/story/task/step, step thành công và policy snapshot còn hợp lệ. Provenance/provider/model lấy từ server. Source=Manual vẫn là nguồn khởi tạo, không có nghĩa phiên bản chưa từng dùng AI.

### Lỗi và xử lý client

- 401: đăng nhập lại; 403: thiếu quyền hoặc consent. Viết tay vẫn khả dụng khi chỉ thiếu consent.
- 404: truyện/task không thuộc người dùng hoặc sai phạm vi Parent/Creator.
- 409: PARENT_STORY_FINALIZED, STORY_REVISION_CONFLICT, TASK_REVISION_CONFLICT, EDITOR_CONTEXT_STALE, AUDIENCE_CONTEXT_STALE, AUTHORING_PIPELINE_CONFLICT. Giữ phần đang viết, GET lại detail/task; không tự áp dụng kết quả cũ.
- 429: rate limit; 502: lỗi AI/output không hợp lệ; 504: AI_TIMEOUT. Giữ bản nháp, GET task để lấy trạng thái/revision trước khi retry bằng operationId mới.
- Response 202 nghĩa lượt xử lý còn chạy; dùng Location/PollUrl dưới API Parent để GET trạng thái, chưa được coi là AI hoàn tất.

## Kiểm chứng và điều kiện chạy local

Source có unit test, Npgsql query-translation và Kestrel loopback HTTP test dùng production controllers/JWT/middleware/services, persistence giả lập có transaction và AI giả lập. Những kiểm tra này không chứng minh PostgreSQL/provider thật hoặc frontend hoạt động.

Chưa tạo/apply migration hoặc chạy seed. Cần schema WritingPurpose/parent_story_operations trước khi dùng API trên database thật; migration chain test tiếp tục báo thiếu bảng cho tới khi cập nhật migration theo yêu cầu riêng. Không khởi động Program tự migration để vượt bước này.

### Kết quả ngày 19/10/2026

- Hồi quy toàn solution: 1.322 pass / 1.323 test. Core unit 1.205 pass, 1 fail; Core integration 53 pass; AI unit 47 pass; AI integration 17 pass. Lỗi duy nhất: MigrationChainSchemaTests báo `Missing table: parent_story_operations`. Báo cáo `TestResults/parent-cse-verified/*.trx`.
- Sau bổ sung đọc context mới, 102 test Parent/Creator assistance và Parent authoring/HTTP đều pass: `TestResults/parent-cse-freshness/parent-creator.trx`. Có test mô phỏng GetById giữ bản tracked cũ để bảo đảm tái kiểm tra sau AI dùng truy vấn mới.
- Nhóm Parent mới gồm 18 ca service và 9 ca HTTP (6 manual + 3 AI). HTTP chạy loopback Kestrel, persistence và AI giả lập, không phải PostgreSQL/provider thật.
- `git diff --check` qua. Không thay migration/model snapshot, không seed, không commit/push hoặc thay cấu hình bí mật.
- CreatorAssistanceService giữ facade/API cũ; phần điều phối và Commands được chuyển sang StoryAssistanceService để tái sử dụng. File Commands cũ hiện hiển thị deleted trong Source Control vì đã chuyển tên; tính năng Creator vẫn được kiểm thử hồi quy.
