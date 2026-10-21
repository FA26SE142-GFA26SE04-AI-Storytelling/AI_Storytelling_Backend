# Kiểm thử Parent authoring và AI hỗ trợ bằng Swagger

Chưa chạy bộ bước này trên DB thật. Trước khi thử cần migration `AddParentManualAuthoring` đã được tạo/review/apply theo yêu cầu riêng, API rebuild, tài khoản Parent có hồ sơ con Active và quyền GenerateStory. Xác nhận cho con còn cần ApproveStory + AssignActivity; Owner được guard hiện hành cho phép.

Bước 1–10 kiểm tra viết tay; kịch bản AI tùy chọn ở cuối tài liệu chạy trên một Story khác, trước khi giao cho con. Nếu chính sách hiện tại yêu cầu safety score ngữ nghĩa thì dừng ở bước check với `SEMANTIC_SAFETY_REVIEW_REQUIRED`; không hạ chính sách chỉ để làm test pass. Happy path đọc văn bản chỉ áp dụng khi các kiểm tra/policy hiện hành cho phép bằng đường không AI.

Mỗi lần chạy mới thay prefix `parent-box-001`. Mỗi request mới dùng operationId riêng; retry đúng request giữ nguyên ID/payload. Lưu ID thực từ response; không thay versionId bằng versionNo.

## 1. Xem hướng dẫn

`GET /api/v1/authoring-guides?locale=vi&purpose=family_memory&group=opening&pageIndex=1&pageSize=10`

Mong đợi danh sách của phiên bản đang phát hành (mặc định `parent-vi-1`; sau khi Moderator phát hành sẽ là `catalogVersion` mới), không phát sinh Story/version/AI. Có thể bỏ qua bước này. Kịch bản quản lý bộ hướng dẫn nằm ở `AUTHORING_GUIDE_MANAGEMENT_SWAGGER_TEST.md`.

## 2. Tạo nháp

`POST /api/v1/parent/stories/manual`

```json
{
  "operationId": "parent-box-001-create",
  "childProfileId": 3,
  "title": "Chiếc hộp trên bàn",
  "purpose": "family_memory",
  "experienceMode": "reading"
}
```

Thay childProfileId bằng hồ sơ được phép. Lưu `data.story.storyId` thành STORY_ID, `data.story.revisionToken` thành R0. selectedVersionId phải null; chưa có version.

## 3. Lưu V1

`POST /api/v1/parent/stories/{STORY_ID}/manual/versions`

```json
{
  "operationId": "parent-box-001-save-v1",
  "revisionToken": "R0_THUC_TE",
  "baseVersionId": null,
  "title": "Chiếc hộp trên bàn",
  "content": "Chiều chủ nhật, mẹ đặt một chiếc hộp giấy lên bàn. Mi muốn giữ những tấm thiệp cũ trong hộp.\n\nHai mẹ con cùng lau bụi và dán lại góc hộp bị bong. Mi tự vẽ một bông hoa nhỏ trên nắp.\n\nKhi xếp tấm thiệp cuối cùng vào hộp, Mi mỉm cười. Chiếc hộp đã có một công việc mới.",
  "sourceDeclaration": "Nội dung mẫu tự sáng tác để kiểm thử",
  "rightsDeclaration": "Tôi có quyền sử dụng nội dung mẫu này"
}
```

Lưu `data.story.revisionToken` thành R1; `data.version.versionId` thành V1. Retry nguyên request không tăng số version.

## 4. Check bản đã lưu

`POST /api/v1/parent/stories/{STORY_ID}/content/checks`

```json
{
  "operationId": "parent-box-001-check-v1",
  "revisionToken": "R1_THUC_TE",
  "baseVersionId": 100
}
```

Thay 100 bằng V1 thực. Lưu `data.checkId`. Kiểm tra canPrepare/errors/warnings. Chỉ đi tiếp khi canPrepare=true. Không có Lesson hoặc consent AI trong request. Check không tạo StoryVersion mới.

## 5. Chọn bản nội dung

`POST /api/v1/parent/stories/{STORY_ID}/content/confirm`

```json
{
  "operationId": "parent-box-001-content-confirm",
  "revisionToken": "R1_THUC_TE",
  "baseVersionId": 100,
  "checkId": 200,
  "checklist": {
    "intendedMeaning": true,
    "suitableForChild": true,
    "privacyChecked": true,
    "endingChecked": true
  }
}
```

Thay V1/checkId. Lưu confirmationId. Đây chưa phải cấp quyền đọc cho con.

## 6. Chuẩn bị và preview

`POST /api/v1/parent/stories/{STORY_ID}/preparations`

```json
{
  "operationId": "parent-box-001-prepare",
  "revisionToken": "R1_THUC_TE",
  "baseVersionId": 100,
  "contentConfirmationId": 300,
  "segmentEnds": null
}
```

Thay các ID thực. Lưu preparationId; kiểm tra `isReady=true` cho mode reading hợp lệ và ghép `segments[].text` phải bằng nguyên content V1, kể cả newline. Preview bằng `GET /api/v1/parent/stories/{STORY_ID}/preparations/{PREPARATION_ID}`. Chưa có grant/lịch sử trẻ.

## 7. Xác nhận cho con

`POST /api/v1/parent/stories/{STORY_ID}/family-confirmations`

```json
{
  "operationId": "parent-box-001-family-confirm",
  "revisionToken": "R1_THUC_TE",
  "baseVersionId": 100,
  "preparationId": 400
}
```

Lưu confirmationId thành GRANT_ID. Retry nguyên request nhận cùng grant. Story vẫn là private Draft theo workflow authoring, không xuất bản công khai và không sinh generation job/media.

## 8. Đọc bằng phiên trẻ

Dùng child-session token của đúng child, không dùng token Parent:

- `GET /api/v1/child/manual-stories`
- `GET /api/v1/child/manual-stories/{GRANT_ID}/package`

Mong đợi package V1 được ghim. Đổi sang token child khác phải không truy cập được package đó. Token Parent không được vào hai route này.

## 9. Kiểm tra khóa sau khi giao

GET detail phải có isReadOnly=true, readOnlyReason=PARENT_STORY_FINALIZED. Gọi lưu V2, restore hoặc PATCH setup bằng operationId mới phải trả 409. Child vẫn đọc đúng V1 đã giao. Request Save V1 lặp nguyên operationId/payload vẫn trả receipt, không tạo version mới.

## 10. Thu hồi rồi mở lại bản nháp

`POST /api/v1/parent/stories/{STORY_ID}/family-confirmations/revoke`

```json
{
  "operationId": "parent-box-001-revoke",
  "revisionToken": "R1_THUC_TE",
  "baseVersionId": 100,
  "confirmationId": 500
}
```

Thay baseVersionId bằng V1, confirmationId bằng GRANT_ID. Child không đọc được grant đã thu hồi. Nếu không còn grant nào chưa thu hồi, GET detail trả isReadOnly=false, revisionToken mới R2 và audienceRevision tăng. Lưu V2 bằng R2/baseVersionId=V1. Check/confirmation/preparation V1 không dùng lại; thực hiện lại bước 4–7 trước khi giao V2. Nếu còn grant khác chưa thu hồi thì truyện vẫn khóa.

## Kiểm tra lỗi bổ sung

- Hai tab cùng base/token: một save thành công, save còn lại 409; giữ nội dung chưa lưu.
- Cùng operationId đổi content/purpose: 409, không tạo thao tác mới.
- Đổi Story/version/check/preparation ID sang tác phẩm khác: từ chối.
- Preview-file rồi hủy: không thêm version; confirm mới thêm đúng một version.
- Sau thay đổi safety policy: check/preparation cũ không được dùng để tạo grant mới.
- Chọn listening/combined: thiếu manual audio, không được cấp grant.
- `GET /api/v1/Story/{STORY_ID}` công khai không được lộ bản nháp Parent mới.
- Reload editor: lấy detail và `GET /api/v1/parent/stories/{STORY_ID}/workflow` để phục hồi ID/kết quả; xem SourceIsCurrent, không tự áp dụng receipt cũ.

## 11. AI hỗ trợ — chạy trên Story mới, chưa giao cho con

Điều kiện bổ sung: Core và AI service đã rebuild, kết nối service-to-service/provider hợp lệ; consent AI của hồ sơ con đã được Owner ghi nhận qua API chính sách hiện có. GET detail phải trả canUseAiAssistance=true. Thiếu consent thì xác minh AI bị chặn và Save thủ công vẫn dùng được.

Tạo Story khác bằng bước 2 với operationId mới `parent-ai-box-001-create`. Chưa lưu version, giữ R0. Đặt nội dung editor đúng snapshot dưới đây.

### 11.1. Mở tác vụ viết tiếp

`POST /api/v1/parent/stories/{STORY_ID}/assistance-tasks`

```json
{
  "operationId": "parent-ai-box-001-task",
  "entryPoint": "ContinueWriting",
  "revisionToken": "R0_THUC_TE",
  "baseVersionId": null,
  "editorSnapshot": {
    "title": "Chiếc hộp của Mi",
    "content": "Mi tìm thấy một chiếc hộp cũ."
  },
  "target": {"kind": "cursor", "start": 29, "length": 0},
  "goal": "Gợi ý hai cách để Mi cùng mẹ sửa chiếc hộp",
  "constraints": {"keep": ["Mi và mẹ tôn trọng ý kiến của nhau"], "avoid": []}
}
```

Lưu taskId và taskRevision. start=29 là cuối đúng chuỗi snapshot mẫu; nếu sửa chuỗi phải tính lại độ dài UTF-16. Task chưa tạo StoryVersion.

### 11.2. Xin phương án

`POST /api/v1/parent/stories/{STORY_ID}/assistance-tasks/{TASK_ID}/suggestions`

```json
{"operationId":"parent-ai-box-001-suggest","expectedTaskRevision":1}
```

Thay revision thực. Nếu OptionsReady: lưu stepId làm OPTIONS_STEP_ID, taskRevision và một option.id. Nếu AwaitingClarification: POST clarification-answer với clarificationStepId/questionId/choiceId thực, rồi POST suggestions bằng operationId mới, revision mới và answerStepId từ response trả lời. Không giả định AI luôn trả Options ngay.

Nếu trả 202: GET đúng Location/PollUrl rồi lấy active result/step IDs và taskRevision; chưa thực hiện bước tiếp theo khi còn running.

### 11.3. Chọn hướng

`POST /api/v1/parent/stories/{STORY_ID}/assistance-tasks/{TASK_ID}/direction`

```json
{
  "operationId":"parent-ai-box-001-direction",
  "expectedTaskRevision":2,
  "optionsStepId":200,
  "optionId":"ID_OPTION_THUC_TE",
  "confirmedConstraints":{"keep":[],"avoid":[]}
}
```

Thay taskRevision/optionsStepId/optionId thực. Có thể thêm directionText để sửa hướng hoặc bỏ optionId và nhập hướng riêng. Không gọi AI ở bước này. Parent có thể tự viết và đi thẳng bước lưu với DirectionUsed.

### 11.4. Phát triển đoạn thử

`POST /api/v1/parent/stories/{STORY_ID}/assistance-tasks/{TASK_ID}/trials`

```json
{"operationId":"parent-ai-box-001-trial","expectedTaskRevision":3,"directionStepId":300}
```

Thay revision và directionStepId thực. Lấy step.result.patch để xem bản thử. Đến đây database vẫn chưa có StoryVersion. Giữ nguyên editor nguồn trước khi áp dụng patch; nếu đã sửa thì mở task mới hoặc chỉ tham khảo kết quả.

### 11.5. Parent áp dụng và lưu V1

`POST /api/v1/parent/stories/{STORY_ID}/manual/versions`

```json
{
  "operationId":"parent-ai-box-001-save",
  "revisionToken":"R0_THUC_TE",
  "baseVersionId":null,
  "title":"Chiếc hộp của Mi",
  "content":"Mi tìm thấy một chiếc hộp cũ. Mi hỏi mẹ cách dán lại góc hộp. Hai mẹ con cùng thử và giữ lại những tấm thiệp yêu thích.",
  "sourceDeclaration":"Parent tự viết, có dùng gợi ý AI",
  "rightsDeclaration":"Tôi có quyền sử dụng nội dung này",
  "assistanceReferences":[
    {"taskId":100,"stepId":300,"usage":"DirectionUsed"},
    {"taskId":100,"stepId":400,"usage":"TrialUsed"}
  ]
}
```

Thay taskId và stepId thực. Content phải là nội dung Parent quyết định lưu; không bắt buộc giữ nguyên đoạn AI. Chỉ khai TrialUsed nếu thực sự sử dụng. Kỳ vọng 201, chỉ một version mới, Source vẫn Manual, có provenance server lưu. Retry nguyên request không tạo version thứ hai.

Tiếp tục các bước check/confirm/prepare/preview/family-confirm bằng token/version mới. Sau khi giao, mở tác vụ AI mới phải bị chặn; close/cancel tác vụ đang có vẫn được khi Parent còn quyền.

### 11.6. Hai điểm vào còn lại

Tạo task mới bằng operationId mới và snapshot/revision/baseVersionId hiện hành:

- SupportSelection: target `{"kind":"selection","start":0,"length":2}` chọn chữ Mi trong snapshot mẫu. Suggestions → direction → trial trả patch replace cục bộ; không tự sửa Story.
- ReviewDraft: target `{"kind":"wholeDraft","start":0,"length":29}` khi dùng đúng snapshot 29 ký tự ở 11.1. Suggestions trả ReviewReady với findings có thể rỗng; không gọi trial trực tiếp. Chọn finding tạo task cục bộ mới với parentTaskId/sourceReviewStepId/findingId.
- Thay đổi policy sau khi có task: GET task thấy audienceContextStale; lượt phát triển tiếp hoặc Save tham chiếu cũ phải bị chặn. Save thủ công không có tham chiếu mới vẫn được nếu truyện chưa giao.
