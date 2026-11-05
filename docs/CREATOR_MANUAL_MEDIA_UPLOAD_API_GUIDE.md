# Creator Manual: upload ảnh và audio kể truyện

## Phạm vi

Chỉ tài khoản `ContentCreator`, đúng tác giả, đang Active, truyện scope Creator và source Manual. Parent và Creator AI tiếp tục luồng hiện hữu. Migration `20261104100610_AddCreatorMediaUploads` đã có trong source; mỗi môi trường cần áp dụng schema này trước khi dùng API mới trên PostgreSQL.

Ảnh upload gắn với một **illustration beat**; audio upload gắn với một **story segment**. Scene được backend phân đoạn từ nội dung đã xác nhận, không bắt buộc truyện phải có đúng ba scene mở–thân–kết. FE hiển thị scene và văn bản segment để tác giả chọn đúng vị trí.

## 1. Xác nhận nội dung và lấy các vị trí

`POST /api/v1/creator/stories/{storyId}/manual/confirm-content`

```json
{ "versionId": 89, "revisionToken": "TOKEN_MOI_NHAT_CUA_STORY", "mediaMode": "choose" }
```

`auto` là mặc định: tạo media bằng AI như trước. `choose` chỉ chuẩn bị cấu trúc scene/beat/segment, chờ tác giả chọn từng phần. Không gọi lại confirm-content để đổi mode của job đã tồn tại.

Poll `GET /api/v1/stories/{storyId}/media/progress`, sau đó đọc `GET /api/v1/stories/{storyId}/media/package`:

- Lấy `storyVersionId`, `packageRevision`, `isConfirmed` mới nhất.
- Lấy `scenes[].illustrations[].beatId` cho ảnh, `scenes[].audio[].segmentId` cho audio.
- `mediaMode=choose`, `isAwaitingSelection=true`: còn phần chưa chọn.
- Item `source=Unspecified`, `status=AwaitingSelection`: chưa có upload/lệnh AI. Đây không phải lỗi provider.
- `source=UserUpload` hoặc `AiGenerated` cho biết nguồn của asset.

## 2. Upload một file hoặc chọn AI cho từng phần

Ảnh: `POST /api/v1/creator/stories/{storyId}/media/illustration-beats/{beatId}/upload`.

Audio: `POST /api/v1/creator/stories/{storyId}/media/segments/{segmentId}/upload`.

Cả hai dùng `multipart/form-data`, một file mỗi request:

- `file`: file ảnh hoặc audio tương ứng.
- `operationId`: khóa duy nhất cho thao tác, tối đa 100 ký tự.
- `storyVersionId`, `packageRevision`: lấy từ package mới nhất.
- `expectedConfirmed`: giá trị `isConfirmed` hiện tại, bắt buộc truyền cả khi false.
- `sourceDeclaration`, `rightsDeclaration`: khai báo nguồn và quyền sử dụng, bắt buộc, tối đa 2.000 ký tự mỗi trường.

Ảnh: JPEG/PNG/WebP, tối đa 10 MiB. Audio: MP3/WAV, tối đa 25 MiB. MIME phải khớp chữ ký file. FE dùng `FormData`; không tự đặt header Content-Type vì trình duyệt cần thêm boundary.

Nếu muốn AI tạo phần chưa chọn, dùng JSON tại:

- `POST /api/v1/creator/stories/{storyId}/media/illustration-beats/{beatId}/generate`
- `POST /api/v1/creator/stories/{storyId}/media/segments/{segmentId}/generate`

```json
{
  "operationId": "creator-45-beat-12-ai-001",
  "storyVersionId": 89,
  "packageRevision": 1,
  "expectedConfirmed": false
}
```

Các lệnh trả `202`, gồm `assetId`, `storyVersionId`, `packageRevision`, `status`, `progressUrl`, `packageUrl`. `202` nghĩa đã xếp hàng, chưa phải file đã được kiểm tra xong. Không gọi endpoint AI service trực tiếp từ FE.

Ảnh upload qua kiểm tra khớp ngữ cảnh và safety. Audio upload kiểm tra kỹ thuật; không có ASR kiểm chứng lời đọc, không tạo word timings giả. Moderator bắt buộc nghe từng segment audio upload trước khi Approve. Media phát âm từ vựng và tương tác listening hiện có vẫn do Phase 5 tạo, không dùng các endpoint upload narration để thay thế chúng.

## 3. Theo dõi, retry và thay file

Poll progress/package; chỉ hiện URL ký có thời hạn sau khi file đạt kiểm tra. File chưa đạt không được cung cấp URL xem trước. Không lưu URL ký làm định danh lâu dài; dùng asset ID và gọi lại package khi cần.

`POST /api/v1/stories/{storyId}/media/retry` giữ luồng retry hiện hữu. Với file upload, worker đọc lại file và kiểm tra, không chuyển sang sinh AI. Các phần đã đạt được giữ lại. Lỗi transient giữ thời gian retry; không tự POST retry liên tục khi worker đang chờ.

Để thay ảnh/audio đã đạt, gửi upload mới với operationId mới và revision/confirmation mới nhất. Backend tạo candidate, tăng revision và hủy xác nhận gói. File cũ còn current đến khi candidate đạt; lịch sử và file cũ được giữ lại. Tại một thời điểm chỉ xử lý một replacement. Regenerate AI của phần đã đạt dùng endpoint regenerate Phase 5 hiện hữu và contract revision hiện hữu.

Replay cùng operationId và payload trả kết quả cũ, không lưu file lần hai. Đổi payload nhưng dùng lại operationId trả `409`. Sau replay vẫn GET package để lấy trạng thái mới nhất.

## 4. Xác nhận gói và gửi Moderator

Khi mọi phần hoàn tất: `storyStatus=MediaReview`, `isGenerated=true`, `isAwaitingConfirmation=true`, `isReady=false`.

`POST /api/v1/stories/{storyId}/media/confirm`

```json
{ "storyVersionId": 89, "packageRevision": 1 }
```

Xác nhận thành công đưa truyện sang Ready. Creator gửi duyệt theo API public review hiện hữu; chưa tự công khai. Phiên bản đã nộp/đã phát hành bị khóa mọi upload/regenerate. Muốn sửa phải dùng quy trình revision/Moderator hiện hữu.

## 5. Lỗi và checklist tích hợp

- `400 MEDIA_FILE_SIZE_INVALID/MEDIA_FILE_TYPE_INVALID`: kiểm tra file và MIME.
- `400 MEDIA_RIGHTS_DECLARATION_REQUIRED`: bổ sung khai báo.
- `403/404`: sai actor/quyền/scope/target; không suy ra quyền từ URL hoặc ID.
- `409 STALE_MEDIA_PACKAGE`: GET package và yêu cầu người dùng kiểm tra lại thay đổi trước khi gửi lệnh mới.
- `409 MEDIA_TARGET_ALREADY_SELECTED/MEDIA_JOB_ACTIVE`: chờ công việc đang chạy; không gửi lựa chọn trùng.
- `409 MEDIA_OPERATION_ID_CONFLICT`: tạo operationId mới cho thao tác mới.
- `409 CREATOR_PUBLICATION_LOCKED/SUBMITTED_VERSION_IMMUTABLE_CREATE_REVISION`: không cho sửa bản đã gửi duyệt.
- Worker: `UPLOADED_IMAGE_ALIGNMENT_FAILED`, `UPLOADED_IMAGE_SAFETY_FAILED`, `UPLOADED_AUDIO_INVALID`, `MEDIA_UPLOAD_HASH_MISMATCH` phải hiển thị lỗi tương ứng; không xem như media đã đạt.

Test FE: chọn choose → đợi cấu trúc → upload ảnh, chọn AI cho một ảnh khác → upload audio → chờ MediaReview → confirm → submit → Moderator phải nghe toàn bộ narration upload. Kiểm tra replay, stale revision, sai quyền, file không hợp lệ, thay file và xác nhận lại.

## 6. Ví dụ TypeScript kết nối từ FE

Ví dụ gọi Core API bằng access token của Creator đang đăng nhập. `apiBase` lấy từ cấu hình FE (ví dụ local `http://localhost:5259`); không chứa đường dẫn AI service. Không hardcode JWT trong source.

```ts
type Envelope<T> = { success: boolean; message: string; data: T; errors: unknown };
type MediaCommand = {
  operationId: string;
  storyVersionId: number;
  packageRevision: number;
  expectedConfirmed: boolean;
};
type SelectedMedia = {
  assetId: number;
  storyVersionId: number;
  packageRevision: number;
  status: string;
  progressUrl: string;
  packageUrl: string;
};

async function callCore<T>(
  apiBase: string, token: string, path: string, init: RequestInit = {},
): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Authorization", `Bearer ${token}`);
  const response = await fetch(`${apiBase}${path}`, { ...init, headers });
  const text = await response.text();
  // Regenerate có thể trả 202 không body; reverse proxy có thể trả lỗi không phải JSON.
  let body: Envelope<T> | undefined;
  try { body = text ? JSON.parse(text) : undefined; } catch { /* xử lý theo HTTP bên dưới */ }
  if (!response.ok || body?.success === false) {
    throw Object.assign(new Error(body?.message ?? `HTTP ${response.status}`), {
      status: response.status, errors: body?.errors,
    });
  }
  return body?.data as T;
}

async function uploadCreatorMedia(args: {
  apiBase: string; token: string; storyId: number; targetId: number;
  kind: "image" | "audio"; command: MediaCommand; file: File;
  sourceDeclaration: string; rightsDeclaration: string;
}) {
  const form = new FormData();
  form.append("file", args.file);
  for (const [key, value] of Object.entries(args.command)) {
    form.append(key, String(value)); // false phải được gửi thành "false", không bỏ qua.
  }
  form.append("sourceDeclaration", args.sourceDeclaration);
  form.append("rightsDeclaration", args.rightsDeclaration);
  const target = args.kind === "image" ? "illustration-beats" : "segments";
  return callCore<SelectedMedia>(args.apiBase, args.token,
    `/api/v1/creator/stories/${args.storyId}/media/${target}/${args.targetId}/upload`,
    { method: "POST", body: form }); // Không đặt Content-Type cho FormData.
}

async function generateCreatorMedia(args: {
  apiBase: string; token: string; storyId: number; targetId: number;
  kind: "image" | "audio"; command: MediaCommand;
}) {
  const target = args.kind === "image" ? "illustration-beats" : "segments";
  return callCore<SelectedMedia>(args.apiBase, args.token,
    `/api/v1/creator/stories/${args.storyId}/media/${target}/${args.targetId}/generate`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify(args.command),
    });
}
```

Mỗi lần người dùng bắt đầu thao tác mới, tạo `operationId` bằng `crypto.randomUUID()`. Nếu chỉ gửi lại do mất response/mất mạng, giữ nguyên cả operationId, file và toàn bộ command ban đầu. Không tự đổi revision trong payload replay. Khi thay file hoặc thay ý định, đó là thao tác mới.

Sau `uploadCreatorMedia`, cập nhật revision từ response rồi GET package trước lần sửa tiếp theo. Khi đang chọn nhiều phần lần đầu, nên gửi tuần tự để quản lý xung đột và không dồn tải; đừng dùng chung một operationId cho các beat/segment khác nhau.

## 7. Polling và trạng thái giao diện

Poll progress mỗi 2–5 giây khi job Pending/Processing; chỉ gửi request tiếp theo sau khi request trước hoàn tất. Đây là nhịp FE đề xuất, không phải cấu hình worker. Dừng polling khi component unmount, đổi story hoặc đăng xuất; dùng AbortController cho request đang chờ. Không có tiến độ mới không có nghĩa phải POST lại confirm-content.

- **Chuẩn bị:** chưa có scene/target; khóa nút upload cho đến khi GET package có ID thực.
- **Chọn media:** hiển thị văn bản từng scene, visualFocus của beat và nội dung audio segment. Cho upload hoặc AI ở từng phần AwaitingSelection. Không dùng sceneIndex/beatOrder thay cho ID.
- **Đang xử lý:** vô hiệu hóa lệnh trùng trên target đó; tiếp tục hiển thị phần đã đạt. `isAwaitingSelection=false` chỉ có nghĩa đã chọn đủ, chưa đồng nghĩa hoàn tất.
- **Lỗi:** hiển thị `progress.errorCode`, `issues[]`, lỗi từng nhánh và `nextRetryAt` nếu có. Giữ bản đã đạt; lỗi nội dung file cần thay file, không retry vô hạn cùng file.
- **Xem trước:** render ảnh/audio từ URL package trả về. Audio upload có `wordTimings=null`; dùng trình phát bình thường, không bật highlight từng từ.
- **Thay thế:** xem `replacements[]` để biết candidate đang Generating/Failed/Applied/Kept. URL current vẫn có thể là file cũ khi candidate chưa đạt, nên không báo thay file thành công chỉ vì HTTP 202.
- **Chờ xác nhận:** chỉ bật nút xác nhận khi `isAwaitingConfirmation=true`; gửi version/revision vừa GET. `MediaReview` cùng `isReady=false` là trạng thái bình thường trước xác nhận.
- **Ready:** sau confirm thành công, tải lại package/progress; cho phép tiếp tục gửi Moderator. Không gộp nút xác nhận gói với duyệt công khai.

Nếu nhận `401`, xử lý đăng nhập/refresh theo cơ chế chung. `409` cần tải lại trạng thái và giải thích cho người dùng trước khi tạo thao tác mới. `429` có thể đến từ rate limit API hoặc provider: không lập vòng retry dày; với provider hãy theo dõi trạng thái worker và thời điểm retry. Không hiển thị raw provider response, token hoặc thông tin nội bộ.

### Ví dụ response upload/generate (minh họa)

```json
{
  "success": true,
  "message": "Thao tác thành công.",
  "data": {
    "assetId": 301,
    "storyVersionId": 89,
    "packageRevision": 1,
    "status": "Queued",
    "progressUrl": "/api/v1/stories/45/media/progress",
    "packageUrl": "/api/v1/stories/45/media/package"
  },
  "errors": null
}
```

ID trên chỉ để minh họa. `storyVersionId` lấy từ package; progress dùng tên `approvedStoryVersionId`. Hai trường cùng chỉ phiên bản đang tạo media trong luồng này. `revisionToken` của story và `packageRevision` là hai loại khác nhau, không thay thế cho nhau.

### Tạo lại bằng AI hoặc giữ file cũ

Phần đã Ready muốn sinh lại bằng AI dùng một trong hai endpoint:

- `POST /api/v1/stories/{storyId}/media/illustration-beats/{beatId}/regenerate`
- `POST /api/v1/stories/{storyId}/media/segments/{segmentId}/regenerate`

Body dùng cùng các trường của `MediaCommand`; response `202` có thể không có body. GET package sau đó để lấy revision mới. Nếu candidate thay thế thất bại và người dùng muốn giữ file cũ:

`POST /api/v1/stories/{storyId}/media/replacements/{candidateAssetId}/keep-current`

```json
{
  "operationId": "creator-keep-current-001",
  "storyVersionId": 89,
  "packageRevision": 2
}
```

Lấy `candidateAssetId` từ `replacements[]`, không dùng `previousAssetId`. Giữ file cũ thành công vẫn phải xác nhận lại gói bằng revision hiện tại. Retry toàn gói dùng POST `/media/retry` không có body và trả 200; không gửi `components` hoặc packageToken của Parent vào API Creator này.

## Mức kiểm chứng

Test tự động dùng fake provider/storage và HTTP in-process không thay thế kiểm thử Vertex thật. Migration và snapshot đã bổ sung Source của asset, Kind của task và beat FK nullable. Migration đã áp dụng lên PostgreSQL local ngày 04/11/2026 theo quyền người dùng; các môi trường khác vẫn cần cập nhật riêng. Kiểm thử HTTP local trước migration đã xác nhận routing/validation và phát hiện lỗi thiếu Source; chưa coi đó là upload ảnh hợp lệ hoàn tất qua provider/storage.
