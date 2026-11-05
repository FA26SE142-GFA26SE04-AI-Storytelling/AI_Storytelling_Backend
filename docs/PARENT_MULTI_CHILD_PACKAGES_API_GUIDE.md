# Hướng dẫn Frontend: Parent Manual, một truyện nhiều bé và gói học liệu riêng

Cập nhật 04/11/2026 theo backend đã push tới commit `30cb3f5` trên nhánh `feat/listening-phase4-readability-20261102`.

Các commit liên quan: `d2c214c` (contract safety/reading context), `5bad40c` (package, media, quyền đọc và regression test), `30cb3f5` (tài liệu). Đây là hướng dẫn để FE triển khai kết nối; source được push không đồng nghĩa server đang dùng đã deploy các commit này.

**Đọc nhanh:** mục 1–6 mô tả request và trình tự nghiệp vụ; mục 8–11 hướng dẫn HTTP client, lấy ID/token, polling và điều kiện hiển thị; mục 12–15 hướng dẫn retry/revise, Reader và các lỗi thường gặp; mục 16 là checklist bàn giao.

```text
Editor: tạo nháp -> lưu version -> finalize
Danh sách bé: tạo package -> poll kiểm tra riêng từng bé
Mỗi package: generate nếu có thành phần cần sinh -> poll -> preview -> confirm
Giao truyện: batch các package đã confirm -> nhận confirmationId từng bé
Reader: child token -> manual-stories -> mở session bằng familyConfirmationId
```

FE chỉ gọi **Core API**. Core queue job, gọi AI service/provider và lưu kết quả. FE không gọi Vertex, AI service nội bộ, không nhận hoặc lưu internal API key.

**Điều kiện chạy:** migration `20261103113109_AddParentStoryPackages` đã được tạo và áp dụng vào PostgreSQL local `localhost:5432/AIStorytellingDB` ngày 03/11/2026. Môi trường khác vẫn phải review/apply migration tăng dần trước khi chạy Core mới. Cập nhật schema không thay thế kiểm thử API với PostgreSQL/provider thật; chưa có xác nhận hoàn tất luồng live.

## 1. Phân biệt các định danh

- `storyId`: tác phẩm Parent sở hữu; không nhân đôi theo bé.
- `versionId` / `baseVersionId`: phiên bản nội dung dùng chung.
- `revisionToken`: token của bản nháp; lấy từ `data.story.revisionToken` sau save/setup.
- `packageId`: gói cho một bé trên một version; có snapshot profile/policy riêng.
- `packageToken`: token mới nhất từ GET package. Worker có thể đổi token, vì vậy đọc lại trước confirm/retry/revise.
- `confirmationId`: quyền giao đã được Parent xác nhận cho đúng bé; không phải version ID hoặc package ID.
- `operationId`: khóa replay. Gửi lại đúng request dùng lại cùng key; một thao tác mới dùng key mới. Không lấy token cũ và đổi operationId để bỏ qua 409.

Role Parent hiện tại là `User`. Creator/Moderator không sử dụng các endpoint authoring này.

## 2. Tạo bản nháp và chốt nội dung

`POST /api/v1/parent/stories/manual`

```json
{
  "operationId": "family-001-create",
  "title": "Chiếc hộp của Mi",
  "language": "vi",
  "ageBand": "6-8",
  "purpose": "family_memory",
  "experienceMode": "reading"
}
```

Không truyền `childProfileId` nếu chọn bé sau khi viết. Request cũ có `childProfileId` vẫn được chấp nhận. Nhóm tuổi trong draft là dự kiến; mỗi bé vẫn được kiểm tra theo profile thật sau đó.

Các API save/import/history/restore giữ nguyên. Ví dụ lưu V1:

`POST /api/v1/parent/stories/{storyId}/manual/versions`

```json
{
  "operationId": "family-001-save-v1",
  "revisionToken": "COPY_CURRENT_DRAFT_TOKEN",
  "baseVersionId": null,
  "title": "Chiếc hộp của Mi",
  "content": "Mi tìm thấy một chiếc hộp giấy bị rách. Mẹ cùng Mi dán lại góc hộp.\n\nMi xếp những tấm thiệp vào hộp. Hai mẹ con vui vẻ ngắm chiếc hộp đã được sửa.",
  "sourceDeclaration": "Tự sáng tác",
  "rightsDeclaration": "Tôi có quyền sử dụng nội dung này"
}
```

Sau save, dùng đúng version ID và revision token vừa nhận:

`POST /api/v1/parent/stories/{storyId}/content/finalize`

```json
{
  "operationId": "family-001-finalize",
  "revisionToken": "COPY_TOKEN_AFTER_SAVE",
  "baseVersionId": 20
}
```

Số 20 chỉ là ví dụ; thay bằng version ID thật. Finalize không tạo thêm version, không giao cho trẻ và không công khai truyện. Save/import-confirm/restore/setup bị khóa cho đến khi reopen.

## 3. Chọn bé và thành phần gói

`POST /api/v1/parent/stories/{storyId}/packages` trả **202**:

```json
{
  "operationId": "family-001-packages",
  "revisionToken": "COPY_TOKEN_AFTER_SAVE",
  "baseVersionId": 20,
  "recipients": [
    {
      "childProfileId": 1,
      "components": { "vocabulary": true, "quiz": true, "listening": false, "images": true, "narration": true }
    },
    {
      "childProfileId": 2,
      "components": { "vocabulary": true, "quiz": false, "listening": false, "images": true, "narration": true }
    }
  ]
}
```

Thay 1 và 2 bằng các hồ sơ Parent thực sự quản lý. Tất cả component có thể false để tạo gói chỉ đọc. `listening` là hoạt động tương tác khi nghe; `narration` là TTS kể truyện. Listening cần audio/ảnh tương tác tương ứng và audio đoạn mẫu cho RolePlay, kể cả khi không chọn kể toàn truyện.

202 chỉ xác nhận đã queue kiểm tra. **Gói chỉ đọc vẫn phải qua kiểm tra suitability/Safety AI và vùng xám**, nhưng không sinh vocabulary/quiz/listening/ảnh/TTS nếu không chọn. Cần consent hợp lệ khi gọi AI; không bypass khi thiếu consent hoặc rubric/provider chưa cấu hình.

Poll:

- `GET /api/v1/parent/stories/{storyId}/packages`
- `GET /api/v1/parent/stories/{storyId}/packages/{packageId}`

Theo dõi `status`, `check.passed`, `check.errors`, `check.safetyScore`, `check.evidence`, `check.reasonCode`, `componentStatus`, các count và `lastErrorCode`.

Safety dùng thang thứ bậc `none/low/moderate/high/critical` → `100/90/70/40/0`, không phải xác suất an toàn. Mỗi bé được so với threshold riêng. Thiếu điểm hoặc bằng chứng không hợp lệ không được coi là đạt; không lấy trung bình điểm hai bé. Vùng xám tiếp tục có queue/claim/decision của Moderator, gắn riêng với package; Clear không bỏ qua safety/readability đã trượt.

`Review` sau bước check chỉ có nghĩa đã có thể chuẩn bị gói; gói có component cần sinh chưa được confirm ngay. Gói không chọn component có thể preview/confirm sau check đạt.

## 4. Sinh học liệu/media và xem trước

Khi check đã đạt:

`POST /api/v1/parent/stories/{storyId}/packages/{packageId}/generate`

```json
{ "operationId": "family-001-generate-a", "packageToken": "COPY_LATEST_PACKAGE_TOKEN" }
```

Trả 202; tiếp tục poll GET package. Chỉ sinh các phần đã chọn. Học liệu có evidence trong chính content; số lượng theo cấu hình/profile và khả năng nguồn, không bịa thêm để đạt target. Worker dùng batch/lease/gate/retry hiện có; không tạo outline hoặc request generation giả.

Ảnh và TTS kể cùng version có thể dùng lại. Ảnh có sẵn được đọc và đánh giá lại cho ngữ cảnh bé mới. Media tương tác được cách ly theo package. Gói được xác nhận ghim các ID asset; regenerate không đổi file đang giao.

`GET /api/v1/parent/stories/{storyId}/packages/{packageId}/preview`

Response có content, manifest, vocabulary/quiz/listening và endpoint preview media. **Parent preview có đáp án**; không chuyển response này trực tiếp cho giao diện trẻ. Asset được đọc qua endpoint kiểm tra quyền, không trả storage key/public URL:

`GET /api/v1/parent/stories/{storyId}/packages/{packageId}/assets/{assetId}`

Frontend gửi Bearer token khi tải file, dùng response binary/blob; không đặt URL bảo vệ vào thẻ ảnh/audio mà bỏ Authorization. Endpoint hỗ trợ range và không cache response.

Khi mọi thành phần đã đạt, đọc packageToken mới nhất rồi gọi:

`POST /api/v1/parent/stories/{storyId}/packages/{packageId}/confirm`

```json
{ "operationId": "family-001-confirm-a", "packageToken": "COPY_LATEST_PACKAGE_TOKEN" }
```

Confirm ghim manifest và trả token của gói đã xác nhận. Không tạo version truyện hoặc grant tại bước này.

## 5. Giao hai bé trong một transaction

`POST /api/v1/parent/stories/{storyId}/family-confirmations/batch`

```json
{
  "operationId": "family-001-deliver",
  "packages": [
    { "packageId": 10, "packageToken": "COPY_CONFIRMED_PACKAGE_A_TOKEN" },
    { "packageId": 11, "packageToken": "COPY_CONFIRMED_PACKAGE_B_TOKEN" }
  ]
}
```

Thay các ID minh họa bằng ID thật. Tất cả package phải thuộc cùng story/version, mỗi bé một gói, còn quyền quản lý, profile/policy khớp và đủ media/học liệu.

- Thành công: 201, trả danh sách confirmation cho từng bé.
- Một gói không đạt: 409 `PARENT_BATCH_NOT_READY`, nêu bé/lý do; không có grant mới cho bé nào.
- Replay đúng request/key: trả các confirmation cũ, không tạo bản ghi mới.
- Bé đã có grant hiệu lực cho story: yêu cầu thu hồi grant cũ trước khi giao revision khác; không âm thầm thay thế.

Trẻ dùng token/session của chính mình:

1. `GET /api/v1/child/manual-stories`: lấy `confirmationId`, `parentStoryPackageId`.
2. `POST /api/v1/child/reading/sessions` với `{ "familyConfirmationId": 123 }`. Không gửi kèm `storyId` hoặc `communitySubmissionId`.
3. `GET /api/v1/child/reading/sessions/{sessionId}/content`; quiz/listening dùng API child hiện hữu trên session đó.

Session ghim grant và package. Backend không trả quiz/listening của package bé khác; các API nộp đáp án cũng kiểm tra scope. Child asset endpoint `/api/v1/child/manual-stories/{confirmationId}/assets/{assetId}` kiểm tra lại grant, profile và manifest, kể cả sau khi đọc file từ storage.

## 6. Sửa, retry và thu hồi

- Provider/validation lỗi: `POST /packages/{id}/retry`, body gồm `operationId`, `packageToken`, `components`. Các tên hợp lệ: `check`, `learning`, `vocabulary`, `quiz`, `listening`, `media`, `images`, `narration`. Mảng rỗng chọn phần thất bại. Phần đã đạt được giữ lại. `AI_OUTPUT_TRUNCATED` không được retry mù cùng cấu hình.
- Gói đã confirm: `POST /packages/{id}/revise` với `operationId`, `packageToken`, `components` đầy đủ và `regenerate` liệt kê phần muốn sinh mới. Endpoint tạo revision/package ID mới, kiểm tra profile lại. Các học liệu không đổi có thể được sao chép metadata để giữ cách ly; không sửa bản đã giao.
- Profile/policy/consent thay đổi: tạo revision mới qua revise; không ép retry trên snapshot cũ.
- Sửa content: nếu còn grant hiệu lực thì phải thu hồi trước. Gọi `/content/reopen` bằng envelope version hiện tại, lấy revisionToken mới, rồi save version mới và finalize lại. Các package nguồn cũ chuyển Stale; không tự áp dụng gói cũ lên V2.
- Thu hồi: giữ endpoint `/family-confirmations/revoke` hiện tại. Thu hồi A không làm mất B; phiên đọc/asset request tiếp theo của A bị chặn.
- Không gọi API công khai/generation/lộ trình để mở quyền cho truyện Parent Manual.

Với truyện legacy đã có scene/media: giữ nguyên layout đã lưu. Nếu listening mới không neo được vào layout đó, trả lỗi có kiểm soát; không sửa các scene trẻ đang đọc. Cần xử lý version mới sau khi thu hồi theo quy trình biên tập.

## 7. Triển khai và kiểm chứng

- AI service cần hỗ trợ schema safety v2 trước Core mới. Worker Core cũ phải drain trước khi nhận job package.
- Migration phải bổ sung bảng package, FK/index, điều chỉnh constraint Parent Manual chưa có bé, cho phép receipt Setup không có child và giữ các constraint Parent AI/Creator.
- Snapshot được cập nhật cùng migration tăng dần AddParentStoryPackages. Giữ nguyên các assertion migration-chain/snapshot; không bỏ test để che schema thiếu ở môi trường khác.
- File seed số 70 là fixture Cancelled không cấp quyền, không phải kết quả safety. Không chạy seed trong đợt coding này.
- Test HTTP/SQLite và provider giả lập không chứng minh PostgreSQL advisory lock hoặc Vertex thật. Cần kiểm thử riêng sau khi được phép migration và chạy live.

## 8. Kết nối HTTP và định dạng response

### 8.1. Cấu hình môi trường

- Cấu hình một Core origin theo môi trường, ví dụ local `http://localhost:5259`. Không hardcode origin này trong component hoặc dùng URL Swagger `/index.html` làm API base.
- Trong tài liệu, các path đã có `/api/v1`. Không nối thêm `/api/v1` lần thứ hai nếu API client hiện hữu đã có prefix.
- Parent dùng access token đăng nhập hiện hữu, role backend là `User`; Creator không được gọi thay các endpoint Parent.
- Danh sách hồ sơ hiện hữu lấy từ `GET /api/v1/ChildProfile/mine`; chọn bé Parent có quyền Owner, không dùng danh mục mọi trẻ trong hệ thống. Backend kiểm tra lại quyền và trạng thái Active khi tạo/đánh giá/giao gói.
- Child dùng token/session riêng theo luồng đăng nhập trẻ hiện hữu; không dùng JWT Parent hoặc chỉ thay `childProfileId` trong request.
- Nếu FE và Core khác origin, Core cần cấu hình CORS cho origin FE. FE không sửa được lỗi CORS bằng việc thêm header vào request hoặc dùng `no-cors`.

Request JSON:

```http
Authorization: Bearer <ACCESS_TOKEN_CUA_SESSION_HIEN_TAI>
Content-Type: application/json
Accept: application/json
```

Response JSON thành công có envelope:

```json
{
  "success": true,
  "message": "Thao tác thành công.",
  "data": {},
  "errors": null
}
```

`data` thay đổi theo endpoint: object, array hoặc boolean. Asset endpoint trả binary, không có envelope này. Validation có thể trả ProblemDetails của ASP.NET; client cần xử lý cả `message` và `title`/`errors`, không giả định mọi response đều có `data`.

- **201:** đã tạo tài nguyên, ví dụ draft, version hoặc family confirmation.
- **202:** đã ghi nhận thao tác. Với create/generate/retry/revise, đọc lại GET package; không hiển thị “đã sinh xong”.
- **200 + success=true trên GET:** đã đọc được trạng thái, kể cả khi `data.status="Failed"`.
- Lỗi HTTP khác lỗi job. GET bị mất mạng không có nghĩa job backend đã Failed.

### 8.2. API client TypeScript tham khảo

Đây là mẫu độc lập framework để ghép vào API client/refresh-token hiện có, không phải một SDK đã được triển khai ở frontend.

```ts
type Envelope<T> = {
  success: boolean;
  message: string;
  data: T;
  errors: unknown;
};

class CoreApiError extends Error {
  constructor(public status: number, message: string, public errors?: unknown) {
    super(message);
  }
}

function coreUrl(origin: string, path: string): URL {
  const url = new URL(path, origin);
  if (url.origin !== new URL(origin).origin) {
    throw new Error("API path must belong to the configured Core origin");
  }
  return url;
}

async function coreJson<T>(
  origin: string,
  token: string,
  path: string,
  method = "GET",
  body?: unknown,
  signal?: AbortSignal,
): Promise<T> {
  const response = await fetch(coreUrl(origin, path), {
    method,
    signal,
    cache: "no-store",
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: "application/json",
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok || payload?.success !== true) {
    throw new CoreApiError(
      response.status,
      payload?.message ?? payload?.title ?? `HTTP_${response.status}`,
      payload?.errors,
    );
  }
  return (payload as Envelope<T>).data;
}
```

Token trong ví dụ được truyền từ session hiện tại; không ghi JWT vào log, URL hoặc source. Khi nhận 401, dùng cơ chế refresh/logout hiện có. Không tự lặp vô hạn request 401/403/409.

## 9. Lấy đúng ID, token và phục hồi màn hình

### 9.1. Kết quả của từng loại thao tác

- Tạo draft `201`: `data` là `ParentDraftDto`. Lấy `data.story.storyId`, `data.story.revisionToken`, `data.story.selectedVersionId`.
- Save/import-confirm/restore `201`: `data` là `CreatorContentSavedDto`. Lấy `data.story.revisionToken` và **`data.version.versionId`**; không dùng `versionNo` làm `baseVersionId`.
- GET draft `200`: lấy `data.story.revisionToken` và `data.story.selectedVersionId`. Nếu cần text, gọi GET `/content/versions/{selectedVersionId}`.
- Finalize/reopen `200`: `data` gồm `storyId`, `storyVersionId`, `contentHash`, `isFinalized`; **không trả revisionToken**. Finalize hiện không xoay token; reopen có xoay, nên phải GET draft sau reopen.
- Create packages `202`: `data` là array package. Ghép đúng bé bằng `childProfileId`, không dựa vào vị trí array hoặc thứ tự FE đã gửi.
- GET package `200`: nguồn chính cho `packageToken`, `status`, `componentStatus`, check và số lượng thực tế.
- Revise `202`: `data.packageId` là ID revision mới. Chuyển lựa chọn UI và polling sang ID mới; GET ID cũ vẫn trả cấu hình cũ.
- Deliver `201`: `data` là array confirmation, mỗi phần gồm `confirmationId`, `storyId`, `storyVersionId`, `childProfileId`, `preparationId`, `confirmedAt`, `parentStoryPackageId`.

Response finalize minh họa; mọi ID và token mẫu trong tài liệu phải thay bằng dữ liệu thật:

```json
{
  "success": true,
  "message": "Thao tác thành công.",
  "data": {
    "storyId": 45,
    "storyVersionId": 89,
    "contentHash": "HASH_NOI_DUNG_DO_BACKEND_TRA_VE",
    "isFinalized": true
  },
  "errors": null
}
```

Create packages tiếp theo dùng `baseVersionId=89` trong ví dụ này và revisionToken hiện tại từ draft/save. `contentHash` không phải concurrency token.

### 9.2. State FE nên giữ

- State editor theo `storyId`: draft DTO, selected version/content, revisionToken và bản sửa chưa lưu.
- State package theo **`packageId`**: child ID, version ID, revision, packageToken và toàn bộ GET DTO mới nhất. Cùng một bé có thể có nhiều package revision.
- State chọn giao: danh sách package ID đã confirm. Không chọn hai revision cho cùng một bé trong một batch.
- State thao tác đang gửi: `operationId` và **toàn bộ payload ban đầu** để replay khi mất response.
- Khi reload trang, gọi GET draft và GET packages, không tự tạo lại draft/package hoặc suy ra trạng thái từ state cũ trong trình duyệt.
- `GET /workflow` là lịch sử `ParentStoryOperation` của luồng cũ; không dùng nó thay GET packages để dựng tiến độ package mới.

### 9.3. Quy tắc operationId

Sinh key mới khi người dùng thực sự tạo thao tác mới, ví dụ `crypto.randomUUID()`; tối đa 100 ký tự. Khóa nút khi request đang chạy để tránh double click.

Nếu mất response, gửi lại **đúng operationId + đúng payload**, gồm cả token ban đầu. Không thay token trong payload rồi dùng lại operationId cũ: backend kiểm tra hash và trả `OPERATION_ID_REUSED` khi payload khác. Sau replay, GET lại tài nguyên vì response receipt có thể cũ hơn trạng thái worker hiện tại.

409 do revision đã đổi là tình huống khác: GET trạng thái mới, cho người dùng đối chiếu rồi tạo thao tác mới với key mới. Đổi operationId một mình không khắc phục token sai.

## 10. Polling, trạng thái và nút thao tác

### 10.1. Poll riêng từng giai đoạn

1. Sau create/revise: poll GET package đến khi check hoàn tất hoặc bị chặn/lỗi.
2. Khi check đạt và có component cần sinh, hiển thị nút “Tạo gói”. Chỉ POST generate một lần cho thao tác đó; không POST mỗi lần poll thấy Review.
3. Sau generate/retry: tiếp tục poll GET package cho đến Review, Failed, Blocked, Stale, Cancelled hoặc Confirmed.
4. Sau confirm: refresh package/list; sau deliver: lưu các confirmation trả về và refresh thư viện trẻ khi chuyển vai trò.

Nhịp gợi ý: 2–3 giây khi tab đang mở; có thể backoff khi mất mạng. Mỗi package chỉ có một vòng poll; không dùng `setInterval` tạo các GET chồng nhau. Dừng khi rời màn hình/logout/đổi package. Hủy polling chỉ hủy request FE, không hủy job backend.

Nếu GET lỗi mạng hoặc 5xx, giữ trạng thái backend lần cuối và hiển thị “Chưa cập nhật được tiến độ”; không sửa local status thành Failed. 401/403/404 cần dừng và xử lý session/quyền/tài nguyên. Nếu job lâu không đổi, cho refresh và gửi thông tin story/package/error cho backend; không tự POST lại input/generate.

### 10.2. Ý nghĩa trạng thái

- `PendingCheck`, `Checking`: đang chờ/đang kiểm tra riêng theo profile của bé; chưa generate/confirm.
- `Generating`: đang làm học liệu hoặc media. Có `lastErrorCode` vẫn có thể đang chờ automatic retry; chưa cho retry thủ công chỉ vì thấy mã lỗi.
- `Review`: có thể là **sau check** hoặc **sau generation**. Phải đọc thêm `componentStatus`.
- `Confirmed`: gói đã chốt, chưa đồng nghĩa đã giao cho trẻ. Giao bằng batch và nhận confirmation riêng.
- `Failed`: thao tác đã dừng lỗi; có thể retry phần phù hợp nếu source/context còn hợp lệ.
- `Blocked`: suitability/safety/readability hoặc vùng xám chưa đạt. Hiển thị `check.errors`/bằng chứng; không mở nút confirm để vượt gate.
- `Stale`: source hoặc context đã không còn dùng được cho gói; không generate/confirm/giao gói này.
- `Cancelled`: gói không hoạt động; không dùng để giao.

`componentStatus` có các key `check`, `vocabulary`, `quiz`, `listening`, `media`. Các giá trị thường gặp là `NotRequested`, `NotStarted`, `Pending`, `Processing`, `Completed`, `Failed`, `Cancelled`; giữ nguyên cách viết hoa từ backend. `media` là nhóm chung cho images/narration/listening media, không có tiến độ ảnh và TTS riêng trong DTO package hiện tại.

Các command POST trả DTO gọn; `componentStatus` có thể là `{}` và count là mặc định. Sau command phải GET package để dựng tiến độ, không dùng `{}` từ POST để xóa state GET trước đó.

### 10.3. Điều kiện hiển thị nút

- **Generate:** check đã đạt, status Review và các phần được chọn còn NotStarted; không có tác vụ gửi ở FE. Nếu không chọn phần nào, bỏ qua generate.
- **Preview/Confirm:** status Review, `check.passed=true`, check Completed và tất cả phần được yêu cầu đã Completed. Gói chỉ đọc không có phần cần sinh nên được đi tiếp sau check đạt.
- **Retry:** chỉ khi status Failed; đọc GET/token mới rồi chọn phần lỗi. Generating có lỗi tạm thời vẫn tiếp tục poll.
- **Revise:** hiển thị thao tác “Tạo revision gói mới”; không diễn đạt là sửa đè gói cũ. Backend quyết định hợp lệ theo source/version hiện tại.
- **Giao:** tất cả package được chọn đã Confirmed, cùng story/version và mỗi bé đúng một gói. Backend kiểm tra lại trong transaction; điều kiện FE không thay thế phân quyền/readiness.

Ví dụ package chỉ mới qua check:

```json
{
  "status": "Review",
  "check": { "passed": true, "errors": [] },
  "componentStatus": {
    "check": "Completed",
    "vocabulary": "NotStarted",
    "quiz": "NotRequested",
    "listening": "NotRequested",
    "media": "NotStarted"
  }
}
```

Đây là trích đoạn GET, không phải toàn bộ response. Bật generate, chưa bật confirm. Khi các phần yêu cầu thành Completed thì mới bật confirm. Không dùng count bằng 0 hoặc một con số target cứng để suy ra job lỗi: backend chịu trách nhiệm grounding, shortfall và readiness.

GET package hiện **không trả** `nextRetryAt`, `jobId`, `readyIllustrations`, asset issues hoặc `canRetry`. FE không dựng countdown/tiến độ 3/8 ảnh từ các field không có; có thể hiển thị “Đang chuẩn bị media”. Không thay GET package bằng `/stories/{id}/media/progress` của luồng Phase 5 theo story.

## 11. Preview ảnh/audio có Authorization

`GET /packages/{id}/preview` trả `ParentPackagePreviewDto`: `package`, `title`, `content`, `manifest`, `vocabulary`, `quiz`, `listening`, `media`. Mỗi phần tử `media` gồm `assetId` và `previewUrl` tương đối tới Core.

Chỉ dùng URL/ID trong response hiện tại. Không ghép storage path, suy ra asset theo scene index hoặc lấy toàn bộ asset của story. Parent preview có đáp án, không tái sử dụng payload này trong Reader trẻ.

```ts
async function loadProtectedMedia(
  origin: string, token: string, relativeUrl: string, signal?: AbortSignal,
): Promise<string> {
  const response = await fetch(coreUrl(origin, relativeUrl), {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
    signal,
  });
  if (!response.ok) throw new CoreApiError(response.status, "MEDIA_LOAD_FAILED");
  return URL.createObjectURL(await response.blob());
}

// Ví dụ gắn vào img.src hoặc audio.src:
// const objectUrl = await loadProtectedMedia(origin, parentToken, item.previewUrl);
// Khi unmount/đổi package/logout: URL.revokeObjectURL(objectUrl).
```

Các URL protected không dùng trực tiếp trong `<img src>`/`<audio src>` nếu browser không gửi Bearer header. Blob URL là một cách tích hợp; giới hạn tải đồng thời và thu hồi object URL để tránh giữ toàn bộ audio trong RAM.

Backend chặn request mới sau thu hồi grant; không thể thu lại byte đã tải xuống browser. FE cần dừng phát và giải phóng blob khi biết grant/session mất quyền. Response binary có thể trả 401/403/404; không cố parse nó thành `ApiResponse` thành công hoặc liên tục retry.

## 12. Retry phần lỗi và revise cấu hình

### 12.1. Media lỗi sau khi học liệu đã đạt

Đọc GET package mới nhất. Nếu status Failed, vocabulary/quiz Completed và media Failed:

```http
POST /api/v1/parent/stories/{storyId}/packages/{packageId}/retry
```

```json
{
  "operationId": "family-media-retry-NEW_UNIQUE_ID",
  "packageToken": "COPY_LATEST_PACKAGE_TOKEN",
  "components": ["media"]
}
```

Nhận 202 rồi poll lại đúng package. Các phần đã đạt được giữ lại; media listening đã đạt được tái sử dụng, asset cũ lỗi không cần xóa thủ công. Backend giữ thông tin quota/Retry-After để điều tiết retry. Retry là cơ hội thử lại, không bảo đảm Vertex hết 429 ngay.

- Check Failed do cấu hình/provider đã được backend sửa: chọn `["check"]`.
- Học liệu lỗi: chọn `["learning"]` hoặc phần lỗi phù hợp như `["vocabulary"]`, `["quiz"]`, `["listening"]`.
- `components: []`: yêu cầu thử lại phần thất bại theo logic backend.
- Chọn phần không khớp job Failed trả `PACKAGE_RETRY_COMPONENT_INVALID`; không gửi `["check"]` cho lỗi media.
- `NotRequested` không phải Failed. Listening false không bật lên bằng retry.

### 12.2. Bật listening hoặc đổi lựa chọn

```http
POST /api/v1/parent/stories/{storyId}/packages/{oldPackageId}/revise
```

```json
{
  "operationId": "family-enable-listening-NEW_UNIQUE_ID",
  "packageToken": "COPY_LATEST_OLD_PACKAGE_TOKEN",
  "components": {
    "vocabulary": true,
    "quiz": true,
    "listening": true,
    "images": true,
    "narration": true
  },
  "regenerate": []
}
```

Gửi **toàn bộ** cấu hình mong muốn. Field boolean bỏ sót mặc định false; request không phải patch một flag.

Response trả package mới cho **cùng bé và cùng storyVersion**, với revision tăng lên. Lưu `data.packageId`, poll ID mới -> đợi check đạt -> generate -> preview -> confirm -> giao. Bản cũ vẫn giữ nguyên để truy vết.

`regenerate` nhận `vocabulary`, `quiz`, `listening`, `images`, `narration`; ví dụ `["images"]` yêu cầu sinh lại ảnh cho revision mới. Mảng rỗng không bắt buộc mọi phần cũ được dùng lại: backend chỉ tái sử dụng khi nguồn/context/trạng thái phù hợp. Listening tạo gói tương tác có media cần thiết dù images/narration không được chọn cho toàn truyện.

Nếu bé đang có grant hiệu lực, revision mới không tự thay thế quyền đọc cũ. Thu hồi grant cũ của bé đó trước khi giao lại; không cần thu hồi grant bé khác nếu chỉ đổi gói của bé này và không sửa content.

## 13. Giao truyện, Child Reader, thu hồi và sửa content

### 13.1. Batch giao và lỗi toàn bộ

FE gửi packageToken đã confirm của từng package. Sau 201, ghép confirmation theo `childProfileId`/`parentStoryPackageId`; không lấy ID package làm confirmation.

Nếu có 409 `PARENT_BATCH_NOT_READY: ...`, hiển thị danh sách lý do theo bé từ message và refresh các package liên quan. Không tự gọi lại từng bé để biến batch thành giao một phần. Một batch bị rollback không tạo grant mới cho bé nào; các grant đã tồn tại trước đó vẫn giữ nguyên.

### 13.2. Child Reader dùng session riêng

1. Child gọi `GET /api/v1/child/manual-stories`. `data` là array confirmation mà trẻ còn quyền đọc.
2. Có thể gọi `GET /api/v1/child/manual-stories/{confirmationId}/package` để lấy phần chuẩn bị/segments. Đây không phải Parent preview với đáp án.
3. Mở session bằng body sau:

```http
POST /api/v1/child/reading/sessions
```

```json
{ "familyConfirmationId": 123 }
```

4. Lấy `data.sessionId`, gọi `GET /api/v1/child/reading/sessions/{sessionId}/content` để render scenes, vocabulary, media và tương tác của đúng manifest.
5. Tương tác nghe dùng `POST /api/v1/child/reading/sessions/{sessionId}/interactions/{interactionId}/attempts` theo contract Child hiện hữu.
6. Sau hoàn tất đọc, quiz dùng `GET /api/v1/child/quiz/sessions/{sessionId}` và `POST /api/v1/child/quiz/sessions/{sessionId}/answers`.

Chỉ gửi một nguồn mở session: familyConfirmationId **hoặc** storyId **hoặc** communitySubmissionId. Với package mới dùng familyConfirmationId; không gửi kèm storyId để “bổ sung thông tin”. Backend ghim version/package/grant vào session và kiểm tra item/đáp án thuộc package đó.

Media URL trẻ trả về có dạng `/api/v1/child/manual-stories/{confirmationId}/assets/{assetId}`; tải bằng **child token**, áp dụng cách fetch/blob ở mục 11. Khi nhận mất quyền/thu hồi, dừng Reader, bỏ state media/session có liên quan và refresh thư viện trẻ.

### 13.3. Thu hồi grant

Parent gọi endpoint hiện hữu, dùng draft token và version ID hiện tại:

```http
POST /api/v1/parent/stories/{storyId}/family-confirmations/revoke
```

```json
{
  "operationId": "family-revoke-NEW_UNIQUE_ID",
  "revisionToken": "COPY_CURRENT_DRAFT_TOKEN",
  "baseVersionId": 89,
  "confirmationId": 123
}
```

Trả 200, `data=true`. GET lại draft sau mỗi lần thu hồi, đặc biệt khi thu hồi grant cuối cùng vì backend đổi revisionToken. Không dùng cùng token cũ để thu hồi liên tiếp nhiều grant.

### 13.4. Sửa nội dung chung

Thu hồi tất cả grant hiệu lực của story -> GET draft -> POST `/content/reopen` với operationId/revisionToken/baseVersionId -> GET draft lấy token mới -> save V2 -> finalize V2 -> tạo package mới.

Reopen làm các package nguồn cũ Stale và hủy job package đang hoạt động. Version V2 là nguồn mới, không tự gắn gói V1 sang V2. Nếu chỉ bổ sung bé nhận trên version đã chốt, gọi create packages cho bé mới; không cần reopen hoặc nhân đôi Story.

## 14. AI hỗ trợ trên editor sau thay đổi này

Ba entry point vẫn là `SupportSelection`, `ContinueWriting`, `ReviewDraft`. FE dùng API `/api/v1/parent/stories/{storyId}/assistance-tasks` và contract tác vụ trong [hướng dẫn assistance](CREATOR_ASSISTANCE_API_FRONTEND_GUIDE.md).

Phần bổ sung cho Parent nhiều bé là `targetChildProfileId` trong request tạo task:

```json
{
  "operationId": "family-assistance-NEW_UNIQUE_ID",
  "targetChildProfileId": 1,
  "entryPoint": "SupportSelection",
  "baseVersionId": 89,
  "revisionToken": "COPY_CURRENT_DRAFT_TOKEN",
  "editorSnapshot": { "title": "Chiếc hộp của Mi", "content": "Mi tìm thấy một chiếc hộp. Em muốn sửa chiếc hộp cùng mẹ." },
  "target": { "kind": "selection", "start": 0, "length": 26 },
  "goal": "Mô tả rõ hơn cảm xúc của Mi",
  "constraints": { "keep": [], "avoid": [] }
}
```

`start`/`length` phải là vị trí UTF-16 đúng trong snapshot thật, không copy số mẫu sau khi đổi text. JavaScript string offsets tương ứng UTF-16.

`SupportSelection` yêu cầu chọn một phần nhỏ hơn toàn bộ bản nháp; chọn toàn bộ nội dung thì dùng `ReviewDraft` với `kind="wholeDraft"`, start 0 và length bằng độ dài nội dung. `ContinueWriting` dùng `kind="cursor"`, length 0 ở vị trí muốn viết tiếp.

Trong luồng mới, backend nhận danh sách bé đã chọn từ các package. Chưa có bé được chọn thì assistance không hoạt động; có nhiều bé thì FE cho chọn một target, không gộp policy hoặc lấy điểm trung bình. Consent/profile của target được backend kiểm tra lại.

GET draft có `canUseAiAssistance` và `aiUnavailableReason`. Với nhiều bé, `canUseAiAssistance=false` cùng `TARGET_CHILD_REQUIRED` báo cần chọn target, không phải tính năng bị tắt cho cả truyện. FE mở bộ chọn bé trước khi tạo task. Với `RECIPIENT_SELECTION_REQUIRED`, cần chọn bé qua package; với reason do finalized/grant hoặc consent, xử lý đúng điều kiện đó thay vì chỉ thêm target.

Finalize khóa biên tập và assistance ghi mới. Khi cần sửa, dùng reopen theo mục 13 trước; sau đó dùng token/snapshot mới. Không gửi task trực tiếp lên AI service hoặc tự áp dụng kết quả vào bản đã chốt. Parent chọn áp dụng vào editor rồi lưu version qua API save, có thể gửi `assistanceReferences` như contract hiện hữu.

## 15. Xử lý lỗi cho UI

Đọc cả HTTP status và `message`; message có thể là mã hoặc mã kèm lý do. Không giả định response có field `code` riêng.

- `401`: xử lý session/refresh theo cơ chế hiện hữu.
- `403`/`404`: không có quyền hoặc không được thấy tài nguyên; dừng thao tác/poll phù hợp. Không đổi ID hoặc dùng token actor khác để thử lại.
- `AUTHORING_SOURCE_CHANGED`: GET draft và current version, thay đúng revisionToken/baseVersionId sau khi đối chiếu bản sửa. Không dùng versionNo hoặc version của story khác.
- `PACKAGE_REVISION_CHANGED`: GET đúng package và cập nhật packageToken. Token thay đổi do worker hoặc thao tác khác, không phải mật khẩu/token JWT.
- `OPERATION_ID_REUSED`: key đã dùng với payload khác; giữ nguyên request cho replay hoặc tạo thao tác mới sau khi người dùng xác nhận ý định mới.
- `PARENT_CONTENT_FINALIZATION_REQUIRED`: save version và finalize trước create/revise package.
- `PARENT_CONTENT_REOPEN_REQUIRED`/`PARENT_STORY_FINALIZED`: đang khóa biên tập; kiểm tra grant và dùng đúng trình tự thu hồi/reopen.
- `PACKAGE_JOB_ACTIVE`/`PACKAGE_JOB_ALREADY_EXISTS`: không gửi generate lần nữa; refresh trạng thái và tiếp tục theo tác vụ hiện có.
- `PACKAGE_RETRY_NOT_AVAILABLE`: package chưa Failed hoặc đã chuyển trạng thái; GET lại trước khi hiển thị retry.
- `PACKAGE_RETRY_COMPONENT_INVALID`: phần chọn retry không khớp; đọc lại componentStatus và chọn đúng nhóm lỗi.
- `PACKAGE_CHECK_NOT_PASSED`, `PACKAGE_NOT_READY`, `MEDIA_PACKAGE_INCOMPLETE`, `PACKAGE_LEARNING_INCOMPLETE`: chưa đủ điều kiện; refresh tiến độ/preview và giữ khóa confirm/giao.
- `CHILD_CONTEXT_CHANGED`, `PARENT_PACKAGE_SOURCE_CHANGED`: không retry trên snapshot cũ. Nếu chỉ context đổi và version vẫn giữ nguyên, tạo revision package mới; nếu content version đổi, tạo package từ version mới sau finalize.
- `MODERATION_NOT_CONFIGURED`, lỗi cấu hình provider: thông báo backend cần kiểm tra cấu hình, không yêu cầu Parent sửa truyện để khắc phục cấu hình.
- `GRAY_ZONE_REVIEW_REQUIRED` hoặc check bị vùng xám chặn: hiển thị chờ Moderator; không tự tạo vòng retry để vượt duyệt.
- `GEMINI_IMAGE_HTTP_429`, `GEMINI_TTS_HTTP_429`, timeout/provider unavailable: nếu Generating thì tiếp tục poll vì có thể đang automatic retry; nếu Failed thì cung cấp retry phần lỗi với token mới.
- `AI_OUTPUT_TRUNCATED`: không tự retry vô hạn cùng cấu hình; giữ dữ liệu đã đạt và chuyển thông tin lỗi cho backend.
- `PARENT_BATCH_NOT_READY`, `CHILD_ALREADY_HAS_GRANT`: đối chiếu package/grant từng bé; không ngầm giao một phần hoặc thay thế grant cũ.
- `PARENT_USE_PACKAGE_API`: đang gọi nhầm các bước check/confirm/preparation/family-confirmation legacy cho story đã có package; chuyển sang API package trong tài liệu này. Endpoint revoke vẫn dùng theo mục 13.

`check.reasonCode` có thể là `OK` hoặc `NONE` khi đạt; quyết định dựa trên `check.passed` và trạng thái backend, không hardcode một reasonCode thành công duy nhất. SafetyScore là thông tin hiển thị/bằng chứng; FE không dùng score để ghi đè quyết định Blocked.

## 16. Checklist bàn giao FE và nguồn contract

- Tạo nháp chưa chọn bé, save/reload không mất text; finalize dùng đúng version ID và token.
- Hai bé khác profile tạo hai package trên cùng storyVersion; hiển thị kết quả check riêng.
- Review sau check không bật confirm khi thành phần còn NotStarted; readonly đi tiếp mà không gọi generate.
- Generate chỉ gửi một lần; reload/resume chỉ GET, không tạo job/package lặp.
- Giả lập mất response và replay đúng payload/key không sinh thao tác trùng; token stale được xử lý bằng refresh/đối chiếu.
- Media 429 không làm FE xóa vocabulary/quiz đã đạt; retry `["media"]` giữ đúng package.
- Revise bật listening trả package mới; UI đổi sang ID mới và giữ bản cũ trong lịch sử.
- Preview có Authorization, binary hiển thị được, blob được cleanup; không đưa đáp án Parent sang Reader.
- Confirm chưa làm trẻ thấy truyện. Batch có một bé chưa đạt không bị FE chia nhỏ để giao riêng.
- Child mở session bằng familyConfirmationId, chỉ đọc/nộp item thuộc package được giao.
- Thu hồi bé A chặn request tiếp theo của A, bé B không mất quyền; sửa content phải thu hồi toàn bộ và reopen.
- Kiểm tra trên môi trường đã deploy đúng Core/AI và schema. Push source, test HTTP/SQLite/provider giả lập không thay cho nghiệm thu PostgreSQL/Vertex thật.

Nguồn đối chiếu khi có chênh lệch Swagger/tài liệu:

- [ParentStoryPackagesController](../src/Core/StoryPlatform.Api/Controllers/ParentStoryPackagesController.cs): routes và HTTP status package.
- [ParentAuthoringController](../src/Core/StoryPlatform.Api/Controllers/ParentAuthoringController.cs): draft/version/import/revoke.
- [ParentStoryPackageDtos](../src/Core/StoryPlatform.Application/Features/ParentAuthoring/ParentStoryPackageDtos.cs): request/response và defaults.
- [ParentAuthoringDtos](../src/Core/StoryPlatform.Application/Features/ParentAuthoring/ParentAuthoringDtos.cs), [CreatorAuthoringDtos](../src/Core/StoryPlatform.Application/Features/CreatorAuthoring/DTOs/CreatorAuthoringDtos.cs): envelope version, draft và save.
- [ParentStoryPackageService](../src/Core/StoryPlatform.Application/Features/ParentAuthoring/ParentStoryPackageService.cs), [delivery](../src/Core/StoryPlatform.Application/Features/ParentAuthoring/ParentStoryPackageService.Delivery.cs): replay, token, readiness, batch và preview.
- [ChildReadingDtos](../src/Core/StoryPlatform.Application/Features/ReadingSessions/DTOs/ChildReadingDtos.cs): mở session và Reader.
- [ParentStoryPackagesApiTests](../tests/StoryPlatform.IntegrationTests/Api/ParentStoryPackagesApiTests.cs), [media regression tests](../tests/StoryPlatform.UnitTests/ParentStoryPackageTests.Media.cs): kiểm thử contract và retry bằng fixture, không phải bằng chứng provider thật.
