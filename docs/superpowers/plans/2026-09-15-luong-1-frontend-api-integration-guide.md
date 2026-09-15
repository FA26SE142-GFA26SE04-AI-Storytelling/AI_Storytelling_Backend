# Luồng 1 — API Reference & Frontend Integration Guide (Next.js)

> Tài liệu **gộp** (trước đây tách 2 file: guide theo-trang + reference theo-endpoint) — tổ chức **theo từng API endpoint** của `AuthController, ChildProfileController, LearningProfileController, SafetyPolicyController, SupervisionController, PermissionController, ClassGroupController, ChildAccessCredentialController, ContentCategoryController, OrganizationController, NotificationController`, mỗi mục có route, auth, JSON request/response mẫu, business rule, **và trang UI đề xuất**. Có kèm bảng tra nhanh theo trang ở mục 0.1 và bảng tổng hợp ở cuối file.
>
> **Trạng thái (2026-09-15):** toàn bộ **49/49 endpoint** của Luồng 1 đã implement xong trong code thật — xác minh bằng `dotnet build` (0 lỗi) và `dotnet test` (281/281 pass). Không còn API nào ở trạng thái "đề xuất, chưa code".
>
> **Phạm vi:** Luồng 1 — Profile & Supervision. Bao gồm giao diện Parent/Teacher (Supervisor) và Child PIN Mode. Không bao gồm AI story pipeline, reading/quiz/TTS (chưa build ở backend). Frontend dự kiến đặt tại `E:\Capstone\AI_Storytelling_Frontend` (Next.js + React + TypeScript), tài liệu này độc lập với việc project đó đã tồn tại hay chưa.

---

## 0. Quy ước chung cho mọi API

**Base URL:** `https://<host>/api/v1`

**Response envelope** — mọi API (thành công lẫn lỗi) đều trả về đúng shape này (`ApiResponse<T>`, định nghĩa tại `StoryPlatform.Application/Common/Models/ApiResponse.cs`):
```json
{
  "success": true,
  "message": "Thao tác thành công.",
  "data": { },
  "errors": null
}
```
Khi lỗi (`success: false`), `data` là `null`, `errors` có thể là mảng string liệt kê chi tiết lỗi validation (hoặc `null` nếu lỗi nghiệp vụ chỉ có 1 message chung). Frontend luôn đọc `success` trước để quyết định nhánh xử lý, không dựa vào HTTP status code một mình (dù status code cũng phản ánh đúng loại lỗi: 400 validation, 401 chưa đăng nhập/token hết hạn, 403 không đủ quyền, 404 không tìm thấy, 409 xung đột trạng thái).

**Auth — 2 loại token tách biệt, KHÔNG dùng lẫn:**
1. **Supervisor session** (Parent/Teacher): lấy từ mục 1.3 (`Auth/login`) hoặc 1.7 (`Auth/refresh-token`) → `AuthResponseDto.accessToken`. Gắn header `Authorization: Bearer <accessToken>` cho mọi API có `[Authorize(Roles="Parent,Teacher")]` hoặc `[Authorize]`.
2. **Child session** (Kid Mode): lấy từ mục 8.4 (`ChildAccessCredential/{id}/login`) → `ChildSessionDto.accessToken`. Chỉ dùng cho `GET /ChildAccessCredential/me` (policy `"ChildSession"`). Không gửi token này tới bất kỳ API Supervisor nào và ngược lại token Supervisor không gọi được `ChildAccessCredential/me`.
- Lưu 2 token này ở 2 key riêng trong storage (vd `supervisorAuth` và `childSession`), vì một thiết bị có thể vừa có phiên Supervisor vừa có phiên Child (Kid Mode mở từ dashboard của Supervisor). Đừng dùng chung 1 axios instance/interceptor cho cả 2 — tách 2 client HTTP riêng (`supervisorApi`, `childApi`) để tránh gắn nhầm header.
- **Refresh token:** khi API trả 401 do access token hết hạn (Supervisor), gọi `POST /Auth/refresh-token` với `refreshToken` hiện có; token cũ bị thu hồi ngay sau khi dùng (rotation) — luôn ghi đè cả `accessToken` lẫn `refreshToken` mới vào storage sau mỗi lần refresh, không giữ lại token cũ.

**Enum:** luôn serialize dạng **string theo tên field enum C#** (không phải số) — ví dụ gửi `"ageBand": "Age_6_8"`, không gửi `"ageBand": 1`. Riêng `Permission` khi dùng trong route cũng là tên enum (`ManageSafetySettings`, ...).

**Phân trang:** chỉ 2 endpoint audit log (mục 10) dùng phân trang, theo shape:
```json
{
  "items": [ ],
  "pageIndex": 1,
  "pageSize": 10,
  "totalCount": 37,
  "totalPages": 4,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```
đây chính là `data` bên trong `ApiResponse<PagedResult<T>>`. Tất cả endpoint danh sách khác (vd `ChildProfile/mine`, `Notification`, `Organization/mine`) trả thẳng mảng đầy đủ, không phân trang — frontend tự phân trang/scroll phía client nếu cần.

**Route trùng, chọn 1 nhánh:** grant/revoke/list permission có ở cả `Supervision/relationships/{id}/permissions/...` (mục 5.7-5.9) và `Permission/relationships/{id}/...`. **Chọn cố định `Supervision/...`** trong toàn bộ app để tránh code trùng lặp, chỉ dùng `GET /Permission` (mục 6.1, base không tham số) để lấy danh mục quyền cho UI.

### 0.1 Bảng tra nhanh: Trang → API

| Trang | Phương thức API chính |
|---|---|
| `/register` | `POST /Auth/register` (1.1) |
| `/verify-email` | `POST /Auth/verify-email` (1.2), `POST /Auth/resend-verification-email` (1.11) |
| `/login` | `POST /Auth/login` (1.3) |
| `/forgot-password` | `POST /Auth/forgot-password` (1.4) |
| `/reset-password` | `POST /Auth/reset-password` (1.5) |
| `/account` | `GET /Auth/me` (1.8), `POST /Auth/change-password` (1.6), `POST /Auth/logout` (1.9), `POST /Auth/logout-all-devices` (1.10), `GET/DELETE /Auth/sessions[/{id}]` (1.12-1.13) |
| `/account/activity` (mới) | `GET /Auth/me/audit-log` (1.14) |
| `/profiles` | `GET /ChildProfile/mine` (2.2) |
| `/profiles/new` | `POST /ChildProfile` (2.1) |
| `/profiles/[id]` (Tổng quan) | `GET/PUT/DELETE /ChildProfile/{id}` (2.3-2.4, 2.6), `PATCH /ChildProfile/{id}/activate` (2.5) |
| `/profiles/[id]` (Lịch sử thay đổi) | `GET /ChildProfile/{id}/audit-log` (2.7) |
| `/profiles/[id]` (Hồ sơ học tập) | `GET/PUT/DELETE /LearningProfile/{childProfileId}` (3.1-3.3) |
| `/profiles/[id]` (Chính sách an toàn) | `GET/PUT/DELETE /SafetyPolicy/{childProfileId}` (4.1-4.3), dropdown category từ `GET /ContentCategory` (9.1) |
| `/profiles/[id]` (Giám sát & Lời mời) | mục 5.1-5.9, danh mục quyền `GET /Permission` (6.1) |
| `/invitations/accept` | `POST /Supervision/invitations/accept` (5.3) |
| `/profiles/[id]` (Mã PIN) | `GET/PUT/DELETE /ChildAccessCredential/{childProfileId}` (8.1-8.3) |
| `/organizations` (mới) | `GET /Organization/mine` (11.2) |
| `/organizations/new` (mới) | `POST /Organization` (11.1) |
| `/organizations/[id]` (mới) | `GET /Organization/{id}` (11.3) |
| `/classes` | `GET /ClassGroup/mine` (7.2, Teacher) |
| `/classes/new` | `POST /ClassGroup` (7.1) |
| `/classes/[id]` | mục 7.3-7.8 |
| `/notifications` | `GET /Notification`, `PUT /Notification/{id}/read`, `PUT /Notification/read-all` (12.1), `DELETE /Notification/{id}` |
| `/kids` | `GET /ChildProfile/mine` (tái sử dụng mục 2.2) |
| `/kids/[id]/pin` | `POST /ChildAccessCredential/{id}/login` (8.4) |
| `/kids/[id]/home` | `GET /ChildAccessCredential/me` (8.5) |

---

## 1. AuthController — base route `api/v1/Auth`

### 1.1 `POST /Auth/register`
**Auth:** AllowAnonymous
**Request:**
```json
{
  "username": "parent01",
  "email": "parent01@example.com",
  "fullName": "Nguyễn Văn A",
  "password": "Passw0rd!",
  "confirmPassword": "Passw0rd!",
  "phoneNumber": "0912345678",
  "role": "Parent"
}
```
**Ràng buộc:** `username` 3-50 ký tự · `fullName` ≤100 · `password`/`confirmPassword` 6-100 ký tự và phải khớp nhau · `role` chỉ nhận `"Parent"` hoặc `"Teacher"` (ẩn "Administrator" trên UI, gửi giá trị khác sẽ bị 400) · `phoneNumber` optional.
**Response (thành công, HTTP 200):**
```json
{ "success": true, "message": "Đăng ký thành công.", "data": null, "errors": null }
```
**Business rule:** không trả token — user phải xác thực email rồi mới đăng nhập được.
**Trang UI:** `/register` → sau thành công điều hướng `/verify-email?email=<email>` kèm thông báo "kiểm tra email". KHÔNG tự động đăng nhập.

### 1.2 `POST /Auth/verify-email`
**Auth:** AllowAnonymous
**Request:**
```json
{ "email": "parent01@example.com", "token": "A1B2C3D4" }
```
**Response:**
```json
{ "success": true, "message": "Xác thực email thành công.", "data": null, "errors": null }
```
**Business rule:** token hết hạn sau 24h kể từ lúc đăng ký.
**Trang UI:** `/verify-email` → sau thành công điều hướng `/login` với thông báo "xác thực thành công, mời đăng nhập".

### 1.3 `POST /Auth/login`
**Auth:** AllowAnonymous
**Request:**
```json
{ "identifier": "parent01@example.com", "password": "Passw0rd!" }
```
`identifier` nhận cả email lẫn username.
**Response (thành công):**
```json
{
  "success": true,
  "message": "Đăng nhập thành công.",
  "data": {
    "accessToken": "eyJhbGciOi...",
    "refreshToken": "8f14e45f-...",
    "tokenType": "Bearer",
    "expiresInSeconds": 7200,
    "user": {
      "id": 12,
      "username": "parent01",
      "email": "parent01@example.com",
      "fullName": "Nguyễn Văn A",
      "phoneNumber": "0912345678",
      "avatarUrl": null,
      "role": "Parent",
      "status": "Active"
    }
  },
  "errors": null
}
```
**Lưu trữ:** Lưu `accessToken`+`refreshToken` dưới key `supervisorAuth`; lưu `user` vào state toàn cục (Zustand/Context) để hiển thị tên/role trên header.
**Lỗi cần xử lý riêng trên UI:**
- HTTP 403 + `message` báo "email chưa xác thực" → hiển thị link sang `/verify-email` (mục 1.2).
- Sau 5 lần sai liên tiếp: tài khoản khoá 15 phút — hiển thị đúng `message` server trả, không hardcode số phút trên UI.
**Trang UI:** `/login` → sau thành công điều hướng `/profiles` (danh sách hồ sơ trẻ).

### 1.4 `POST /Auth/forgot-password`
**Auth:** AllowAnonymous
**Request:** `{ "email": "parent01@example.com" }`
**Response:** luôn cùng 1 message bất kể email tồn tại hay không (chống dò email):
```json
{ "success": true, "message": "Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi.", "data": null, "errors": null }
```
**Trang UI:** `/forgot-password` — không phân biệt trên UI dù email có tồn tại hay không.

### 1.5 `POST /Auth/reset-password`
**Auth:** AllowAnonymous
**Request:**
```json
{
  "email": "parent01@example.com",
  "resetToken": "9f8e7d6c...",
  "newPassword": "NewPassw0rd!",
  "confirmPassword": "NewPassw0rd!"
}
```
**Response:** `{ "success": true, "message": "Đặt lại mật khẩu thành công.", "data": null, "errors": null }`
**Trang UI:** `/reset-password?email&token` — `resetToken` lấy từ link email. Sau thành công → điều hướng `/login`.

### 1.6 `POST /Auth/change-password`
**Auth:** `[Authorize]` (mọi role đã đăng nhập)
**Request:**
```json
{ "currentPassword": "Passw0rd!", "newPassword": "NewPassw0rd!", "confirmPassword": "NewPassw0rd!" }
```
**Response:** `{ "success": true, "message": "Đổi mật khẩu thành công.", "data": null, "errors": null }`
**Business rule:** sau khi đổi, phiên hiện tại coi như hết hạn — frontend nên tự động điều hướng `/login` và xoá token cũ khỏi storage ngay khi nhận response thành công.
**Trang UI:** trang `/account`.

### 1.7 `POST /Auth/refresh-token`
**Auth:** AllowAnonymous (nhưng cần `refreshToken` hợp lệ trong body)
**Request:** `{ "refreshToken": "8f14e45f-..." }`
**Response:** giống hệt shape mục 1.3 (`AuthResponseDto` đầy đủ `accessToken`/`refreshToken`/`user`...).
**Business rule — QUAN TRỌNG:** đây là **rotation model** — `refreshToken` cũ bị vô hiệu hoá ngay khi dùng. Frontend PHẢI ghi đè cả `accessToken` lẫn `refreshToken` mới vào storage sau mỗi lần gọi, không được giữ lại `refreshToken` cũ để dùng lại (sẽ bị lỗi lần sau).

### 1.8 `GET /Auth/me`
**Auth:** `[Authorize]`
**Response:**
```json
{
  "success": true,
  "message": "Lấy thông tin thành công.",
  "data": {
    "id": 12, "username": "parent01", "email": "parent01@example.com",
    "fullName": "Nguyễn Văn A", "phoneNumber": "0912345678",
    "avatarUrl": null, "role": "Parent", "status": "Active"
  },
  "errors": null
}
```
**Trang UI:** dùng để tải trang `/account`; cũng dùng để lấy `id`/`role` hiện tại cho các so sánh Owner-check ở nhiều trang khác (vd mục 2.3, 2.6).

### 1.9 `POST /Auth/logout`
**Auth:** `[Authorize]`
**Request:** `{ "refreshToken": "8f14e45f-..." }` (refresh token của chính thiết bị hiện tại)
**Response:** `{ "success": true, "message": "Đăng xuất thành công.", "data": null, "errors": null }`
**Trang UI:** `/account` → sau thành công xoá `supervisorAuth` khỏi storage, điều hướng `/login`.

### 1.10 `POST /Auth/logout-all-devices`
**Auth:** `[Authorize]`
**Request:** không body.
**Response:** `{ "success": true, "message": "Đã đăng xuất khỏi tất cả thiết bị.", "data": null, "errors": null }`
**Trang UI:** `/account` → xoá storage tương tự mục 1.9.

### 1.11 `POST /Auth/resend-verification-email`
**Auth:** AllowAnonymous
**Request:** `{ "email": "parent01@example.com" }`
**Response:** `{ "success": true, "message": "Nếu email tồn tại và chưa xác thực, mã xác thực mới đã được gửi.", "data": null, "errors": null }`
**Business rule:** luôn cùng 1 message (chống dò email); giới hạn 1 lần/60 giây/email; mã mới vô hiệu hoá mã cũ, hạn 24h.
**Trang UI:** không tạo trang mới — thêm nút **"Gửi lại email xác thực"** vào trang `/verify-email`, kèm đếm ngược 60 giây disable nút sau khi bấm (timer phía client, API không trả thời gian còn lại).

### 1.12 `GET /Auth/sessions`
**Auth:** `[Authorize]`
**Response:**
```json
{
  "success": true,
  "message": "Lấy danh sách phiên đăng nhập thành công.",
  "data": [
    { "id": 45, "issuedAt": "2026-09-10T08:00:00Z", "expiresAt": "2026-09-17T08:00:00Z" },
    { "id": 51, "issuedAt": "2026-09-14T19:22:00Z", "expiresAt": "2026-09-21T19:22:00Z" }
  ],
  "errors": null
}
```
**Ghi chú:** DTO KHÔNG có field `isCurrent`/`deviceInfo` — token JWT không mang định danh refresh-token cụ thể nên backend không xác định được "đây có phải phiên đang dùng không". UI hiển thị danh sách như các thiết bị bình đẳng, không đánh dấu "phiên hiện tại".
**Trang UI:** section **"Thiết bị đăng nhập"** trong trang `/account` — bảng danh sách session (id, issuedAt, expiresAt), nút "Đăng xuất" cho từng dòng gọi mục 1.13.

### 1.13 `DELETE /Auth/sessions/{id}`
**Auth:** `[Authorize]`
**Response:** `{ "success": true, "message": "Thu hồi phiên đăng nhập thành công.", "data": null, "errors": null }`
**Lỗi:** 404 nếu `id` không thuộc về user hiện tại.
**Trang UI:** cùng section "Thiết bị đăng nhập" ở `/account` (mục 1.12).

### 1.14 `GET /Auth/me/audit-log?pageIndex=1&pageSize=10`
**Auth:** `[Authorize]`
**Response:**
```json
{
  "success": true, "message": "Lấy lịch sử hoạt động thành công.",
  "data": {
    "items": [
      { "id": 501, "actorUserId": 12, "action": "CHANGE_PASSWORD", "entityType": "UserAccount", "entityId": 12, "occurredAt": "2026-09-14T09:00:00Z" },
      { "id": 498, "actorUserId": 12, "action": "LOGOUT_ALL_DEVICES", "entityType": "UserAccount", "entityId": 12, "occurredAt": "2026-09-10T21:12:00Z" }
    ],
    "pageIndex": 1, "pageSize": 10, "totalCount": 2, "totalPages": 1, "hasPreviousPage": false, "hasNextPage": false
  },
  "errors": null
}
```
**Ghi chú:** `AuditLogDto` KHÔNG có field `ipAddress` (entity thật không lưu IP) — đừng thiết kế UI hiển thị cột này.
**Trang UI:** trang mới `/account/activity` — danh sách lịch sử hoạt động cá nhân (đổi mật khẩu, đăng xuất tất cả thiết bị, gửi lại mã xác thực...), phân trang theo `pageIndex`/`hasNextPage`. Thêm link từ `/account`.

---

## 2. ChildProfileController — base route `api/v1/ChildProfile`

### 2.1 `POST /ChildProfile`
**Auth:** `Roles="Parent,Teacher"`
**Request (scope Personal):**
```json
{ "nickname": "Bin", "ageBand": "Age_6_8", "language": "vi", "scope": "Personal" }
```
**Request (scope Organization — 2 field organizationId/classGroupId BẮT BUỘC):**
```json
{
  "nickname": "Bin", "ageBand": "Age_6_8", "language": "vi",
  "scope": "Organization", "organizationId": 3, "classGroupId": 7
}
```
**Response (HTTP 201):**
```json
{
  "success": true,
  "message": "Tạo hồ sơ trẻ thành công.",
  "data": {
    "id": 101, "ownerUserId": 12, "nickname": "Bin", "ageBand": "Age_6_8",
    "language": "vi", "status": "Draft", "scope": "Personal",
    "organizationId": null, "createdAt": "2026-09-15T10:00:00Z"
  },
  "errors": null
}
```
**Business rule:** `status` luôn khởi tạo `"Draft"`. Nếu `scope="Personal"`, `organizationId`/`classGroupId` phải để trống (gửi kèm sẽ bị 400). Owner supervision relationship được tạo tự động trong cùng transaction, và ghi audit log `CREATE_CHILD_PROFILE`.
**Trang UI:** `/profiles/new` — validate bắt buộc trên form: nếu `scope="Organization"` → 2 field kia bắt buộc có giá trị (chọn từ dropdown `GET /Organization/mine`, mục 11.2). Sau thành công, điều hướng `/profiles/[id]` để tiếp tục thiết lập Learning Profile + Safety Policy (bắt buộc trước khi activate — mục 2.5).

### 2.2 `GET /ChildProfile/mine`
**Auth:** `Roles="Parent,Teacher"`
**Response:**
```json
{
  "success": true, "message": "Lấy danh sách hồ sơ trẻ thành công.",
  "data": [
    { "id": 101, "ownerUserId": 12, "nickname": "Bin", "ageBand": "Age_6_8", "language": "vi", "status": "Active", "scope": "Personal", "organizationId": null, "createdAt": "2026-09-15T10:00:00Z" }
  ],
  "errors": null
}
```
Không phân trang — trả full danh sách.
**Trang UI:** `/profiles` (Profile Switcher) — mỗi thẻ hiển thị nickname, ageBand, badge trạng thái (`status`: Draft/PendingSupervision/ReadyForActivation/Active/PendingParentConsent/Suspended/Archived). Click vào thẻ → `/profiles/[id]`. Nút "Thêm hồ sơ" → `/profiles/new`. Nút "Chuyển sang chế độ trẻ em" → `/kids` (mục 8.4, tái sử dụng `data` này để hiển thị avatar chọn hồ sơ).

### 2.3 `GET /ChildProfile/{id}`
**Auth:** `Roles="Parent,Teacher"`, caller phải là supervisor còn hiệu lực của hồ sơ.
**Response:** shape `ChildProfileDto` giống mục 2.1.
**Lỗi:** 403 nếu không phải supervisor; 404 nếu id không tồn tại.
**Trang UI:** tab "Tổng quan" trong `/profiles/[id]`.

### 2.4 `PUT /ChildProfile/{id}`
**Auth:** `Roles="Parent,Teacher"`
**Request:** `{ "nickname": "Bin Bin", "ageBand": "Age_9_12", "language": "vi" }` (KHÔNG có `scope`/`organizationId` — ẩn field này trên form edit, đừng gửi).
**Response:** `ChildProfileDto` mới.
**Trang UI:** tab "Tổng quan" trong `/profiles/[id]`.

### 2.5 `PATCH /ChildProfile/{id}/activate`
**Auth:** `Roles="Parent,Teacher"`
**Request:** không body.
**Response (đủ điều kiện):**
```json
{ "success": true, "message": "Kích hoạt hồ sơ thành công.", "data": { "id": 101, "status": "Active", "...": "..." }, "errors": null }
```
**Response (thiếu điều kiện BR-1.9 — vẫn HTTP 200, không phải lỗi):**
```json
{ "success": true, "message": "Hồ sơ chờ xác nhận từ phụ huynh.", "data": { "id": 101, "status": "PendingParentConsent", "...": "..." }, "errors": null }
```
**Business rule (BR-1.9):** cần đủ 3 điều kiện — có `LearningProfile` (mục 3), có `SafetyPolicy` (mục 4), có ít nhất 1 supervisor role Parent còn hiệu lực (mục 5). Frontend đọc `data.status` trả về để quyết định thông báo, không tự suy diễn "gọi thành công = Active".
**Trang UI:** tab "Tổng quan" trong `/profiles/[id]`. Nên hiển thị checklist 3 điều kiện trên trước khi cho bấm "Kích hoạt" (✅ Learning Profile / ✅ Safety Policy / ✅ có Parent giám sát) — tự suy ra từ kết quả `GET LearningProfile` (mục 3.2), `GET SafetyPolicy` (mục 4.2), `GET Supervision/relationships` (mục 5.5); nếu backend không lộ đủ thông tin để tính điều kiện thứ 3 phía client, chấp nhận gọi thẳng `PATCH activate` và hiển thị theo `status` trả về.

### 2.6 `DELETE /ChildProfile/{id}`
**Auth:** `Roles="Parent,Teacher"`, **chỉ Owner** (403 nếu Additional Supervisor gọi — so `ownerUserId` với id user hiện tại lấy từ mục 1.8).
**Response:** `{ "success": true, "message": "Lưu trữ hồ sơ thành công.", "data": null, "errors": null }`
**Business rule:** soft-delete (archive), giữ lịch sử.
**Trang UI:** tab "Tổng quan" trong `/profiles/[id]` — chỉ hiện nút này nếu là Owner.

### 2.7 `GET /ChildProfile/{id}/audit-log?pageIndex=1&pageSize=10`
**Auth:** `Roles="Parent,Teacher"`, caller phải là supervisor còn hiệu lực của hồ sơ.
**Response:** cùng shape `PagedResult<AuditLogDto>` như mục 1.14, lọc theo `entityType="ChildProfile"` và `entityId={id}`.
**Ghi chú:** `ChildProfileService` ghi audit log cho 4 hành động vòng đời hồ sơ trẻ (`CREATE_CHILD_PROFILE`, `UPDATE_CHILD_PROFILE`, `ACTIVATE_CHILD_PROFILE`, `ARCHIVE_CHILD_PROFILE`).
**Trang UI:** tab mới **"Lịch sử thay đổi"** trong `/profiles/[id]`, hiển thị timeline theo `occurredAt` giảm dần.

---

## 3. LearningProfileController — base route `api/v1/LearningProfile`

### 3.1 `PUT /LearningProfile/{childProfileId}` (và `POST` cùng route — alias, cùng logic)
**Auth:** `Roles="Parent,Teacher"`, bất kỳ supervisor còn hiệu lực nào (không cần permission riêng).
**Request:**
```json
{
  "readingLevel": 3,
  "comprehensionGoal": "Đọc hiểu đoạn văn 200 từ",
  "topics": [
    { "topic": "Khủng long", "relation": "FavoriteTopic" },
    { "topic": "Toán tư duy", "relation": "PriorityFocusArea" }
  ]
}
```
`readingLevel` 1-5 · `comprehensionGoal` optional ≤500 ký tự · `topics[].topic` ≤150 ký tự · `relation` ∈ `"FavoriteTopic" | "PriorityFocusArea"`.
**Response:**
```json
{
  "success": true, "message": "Cập nhật hồ sơ học tập thành công.",
  "data": {
    "id": 55, "childProfileId": 101, "readingLevel": 3,
    "comprehensionGoal": "Đọc hiểu đoạn văn 200 từ",
    "topics": [{ "topic": "Khủng long", "relation": "FavoriteTopic" }, { "topic": "Toán tư duy", "relation": "PriorityFocusArea" }]
  },
  "errors": null
}
```
**Trang UI:** tab "Hồ sơ học tập" trong `/profiles/[id]` — dùng chung 1 API cho tạo mới lẫn cập nhật (upsert).

### 3.2 `GET /LearningProfile/{childProfileId}`
**Auth:** `Roles="Parent,Teacher"` — Response shape giống mục 3.1. 404 nếu chưa thiết lập (hiện form trống).
**Trang UI:** tải trang cho tab "Hồ sơ học tập" trong `/profiles/[id]`.

### 3.3 `DELETE /LearningProfile/{childProfileId}`
**Auth:** `Roles="Parent,Teacher"` — `{ "success": true, "message": "Xoá hồ sơ học tập thành công.", "data": null, "errors": null }`
**Cảnh báo UI:** xoá cái này làm hồ sơ trẻ mất 1 trong 2 điều kiện BR-1.9 để activate — hiện dialog xác nhận rõ ràng.

---

## 4. SafetyPolicyController — base route `api/v1/SafetyPolicy`

### 4.1 `PUT /SafetyPolicy/{childProfileId}` (và `POST` cùng route — alias)
**Auth:** `Roles="Parent,Teacher"` **+ yêu cầu permission `ManageSafetySettings`** trên quan hệ giám sát của caller (kiểm tra trước qua mục 5.7 nếu là Additional Supervisor).
**Request:**
```json
{
  "maxStoryLength": 2000,
  "requiredApprovalMode": "AlwaysManual",
  "parentalGateEnabled": true,
  "consentRecorded": true,
  "categories": [
    { "contentCategoryId": 1, "rule": "Allowed" },
    { "contentCategoryId": 4, "rule": "Blocked" }
  ]
}
```
`maxStoryLength` 100-20000 (mặc định 2000) · `requiredApprovalMode` ∈ `"AlwaysManual" | "AutoPublishOnThreshold"` · `categories[].rule` ∈ `"Allowed" | "Restricted" | "Blocked"` · `contentCategoryId` lấy từ mục 9 (ContentCategory).
**Response:**
```json
{
  "success": true, "message": "Cập nhật chính sách an toàn thành công.",
  "data": {
    "id": 33, "childProfileId": 101, "maxStoryLength": 2000,
    "requiredApprovalMode": "AlwaysManual", "parentalGateEnabled": true, "consentRecorded": true,
    "categories": [{ "contentCategoryId": 1, "rule": "Allowed" }, { "contentCategoryId": 4, "rule": "Blocked" }]
  },
  "errors": null
}
```
**Lỗi:** 403 nếu Additional Supervisor chưa được cấp `ManageSafetySettings`. Nếu vậy, ẩn/disable form và hiển thị thông báo "cần quyền Quản lý an toàn".
**Trang UI:** tab "Chính sách an toàn" trong `/profiles/[id]` — dropdown category đổ từ `GET /ContentCategory` (mục 9.1).

### 4.2 `GET /SafetyPolicy/{childProfileId}`
**Auth:** `Roles="Parent,Teacher"` — Response shape giống mục 4.1. 404 nếu chưa thiết lập.

### 4.3 `DELETE /SafetyPolicy/{childProfileId}`
**Auth:** `Roles="Parent,Teacher"` + `ManageSafetySettings` — `{ "success": true, "message": "Xoá chính sách an toàn thành công.", "data": null, "errors": null }`
Cảnh báo UI tương tự mục 3.3 (mất điều kiện BR-1.9).

---

## 5. SupervisionController — base route `api/v1/Supervision`

### 5.1 `POST /Supervision/{childProfileId}/invitations`
**Auth:** `Roles="Parent,Teacher"`, bất kỳ supervisor còn hiệu lực nào.
**Request:** `{ "inviteeEmail": "teacher02@example.com", "expiresInDays": 7 }` (`inviteeEmail` optional, `expiresInDays` 1-365, mặc định 7)
**Response (HTTP 201):**
```json
{
  "success": true, "message": "Tạo lời mời thành công.",
  "data": { "id": 8, "childProfileId": 101, "invitationCode": "INV-9X7K2Q", "status": "Pending", "expiresAt": "2026-09-22T10:00:00Z" },
  "errors": null
}
```
**Business rule:** nếu có `inviteeEmail`, backend tự gửi email chứa `invitationCode` qua Resend.
**Trang UI:** tab "Giám sát & Lời mời" trong `/profiles/[id]`.

### 5.2 `GET /Supervision/{childProfileId}/invitations`
**Auth:** `Roles="Parent,Teacher"` — trả mảng `InvitationDto` (mọi trạng thái: Pending/Accepted/Cancelled/Expired), không phân trang. Hiển thị badge theo `status`.
**Trang UI:** tab "Giám sát & Lời mời" trong `/profiles/[id]`.

### 5.3 `POST /Supervision/invitations/accept`
**Auth:** `Roles="Parent,Teacher"`
**Request:** `{ "invitationCode": "INV-9X7K2Q" }`
**Response:**
```json
{
  "success": true, "message": "Chấp nhận lời mời thành công.",
  "data": { "id": 15, "childProfileId": 101, "supervisorUserId": 20, "supervisorRole": "AdditionalSupervisor" },
  "errors": null
}
```
**Business rule:** re-check BR-1.9 ngay trong API này — hồ sơ trẻ có thể tự chuyển `Active` ngay sau khi chấp nhận (không cần gọi `activate` riêng), gọi lại mục 2.3 để lấy `status` mới nhất nếu cần hiển thị.
**Trang UI:** trang riêng `/invitations/accept?code=...` — sau thành công điều hướng `/profiles/{childProfileId}`.

### 5.4 `DELETE /Supervision/invitations/{invitationId}`
**Auth:** `Roles="Parent,Teacher"` — chỉ khi `status="Pending"` (ẩn nút Huỷ với trạng thái khác). `{ "success": true, "message": "Huỷ lời mời thành công.", "data": null, "errors": null }`

### 5.5 `GET /Supervision/{childProfileId}/relationships`
**Auth:** `Roles="Parent,Teacher"` — trả mảng `SupervisionRelationshipDto { id, childProfileId, supervisorUserId, supervisorRole: "Owner"|"AdditionalSupervisor" }` (chỉ quan hệ còn hiệu lực, đã revoke thì không xuất hiện).

### 5.6 `DELETE /Supervision/relationships/{relationshipId}`
**Auth:** `Roles="Parent,Teacher"`, **chỉ Owner**. `{ "success": true, "message": "Thu hồi quyền giám sát thành công.", "data": null, "errors": null }`
**Business rule:** trigger "revoke cascade check" — có thể khiến hồ sơ trẻ mất điều kiện Active nếu supervisor bị thu hồi là người duy nhất role Parent.

### 5.7 `GET /Supervision/relationships/{relationshipId}/permissions`
**Auth:** `Roles="Parent,Teacher"`, chỉ Owner xem được.
**Response:** `{ "success": true, "message": "...", "data": ["ViewProgress", "ApproveStory"], "errors": null }`

### 5.8 `POST /Supervision/relationships/{relationshipId}/permissions/{permission}`
**Auth:** `Roles="Parent,Teacher"` — `{permission}` là tên enum ngay trên URL, ví dụ:
```
POST /api/v1/Supervision/relationships/15/permissions/ManageSafetySettings
```
Không body. `{ "success": true, "message": "Cấp quyền thành công.", "data": null, "errors": null }`

### 5.9 `DELETE /Supervision/relationships/{relationshipId}/permissions/{permission}`
**Auth:** `Roles="Parent,Teacher"` — tương tự mục 5.8 nhưng thu hồi quyền.

**UI gợi ý cho 5.7-5.9:** với mỗi relationship trong danh sách (mục 5.5), hiện danh sách checkbox từ `GET /Permission` (mục 6.1), tick sẵn theo mục 5.7; khi user tick/untick 1 checkbox thì gọi ngay mục 5.8/5.9 tương ứng (không cần nút "Lưu" riêng).

---

## 6. PermissionController — base route `api/v1/Permission`

### 6.1 `GET /Permission`
**Auth:** `[Authorize]` (mọi role)
**Response:**
```json
{
  "success": true, "message": "Lấy danh mục quyền thành công.",
  "data": ["ViewProgress", "ViewResults", "AssignActivity", "ReceiveReport", "ApproveReadingLevel", "ApproveStory", "ManageSafetySettings", "GenerateStory"],
  "errors": null
}
```
Dùng để render checkbox list quyền. **Đây là API duy nhất frontend nên dùng ở `Permission` controller** — 3 endpoint còn lại của controller này (`GET/POST/DELETE relationships/{id}/...`) trùng hoàn toàn logic với mục 5.7-5.9, KHÔNG dùng song song cả 2 nhánh route.

---

## 7. ClassGroupController — base route `api/v1/ClassGroup` (chỉ Teacher)

### 7.1 `POST /ClassGroup`
**Auth:** `Roles="Teacher"`
**Request:** `{ "name": "Lớp 2A", "organizationId": 3 }`
**Response (HTTP 201):**
```json
{ "success": true, "message": "Tạo lớp học thành công.", "data": { "id": 7, "teacherUserId": 20, "name": "Lớp 2A", "status": "Active", "organizationId": 3 }, "errors": null }
```
**Trang UI:** `/classes/new` — `organizationId` chọn từ dropdown `GET /Organization/mine` (mục 11.2), chỉ org `verificationStatus="Active"`.

### 7.2 `GET /ClassGroup/mine`
**Auth:** `Roles="Teacher"` — trả mảng `ClassGroupDto`.
**Trang UI:** `/classes` — chỉ hiện mục "Lớp học" trên menu điều hướng nếu `user.role === "Teacher"` (mục 1.8).

### 7.3 `GET /ClassGroup/{id}`
**Auth:** `Roles="Teacher"` — `ClassGroupDto` đơn.
**Trang UI:** `/classes/[id]`.

### 7.4 `PUT /ClassGroup/{id}`
**Auth:** `Roles="Teacher"` — `{ "name": "Lớp 2A - Buổi sáng" }` (chỉ field này editable).

### 7.5 `DELETE /ClassGroup/{id}`
**Auth:** `Roles="Teacher"` — archive, giữ lịch sử.

### 7.6 `POST /ClassGroup/{classGroupId}/members/{childProfileId}`
**Auth:** `Roles="Teacher"` — không body, cả 2 id trên route. `{ "success": true, "message": "Thêm thành viên thành công.", "data": null, "errors": null }`
**Trang UI:** `/classes/[id]` — danh sách hồ sơ trẻ để chọn thêm lấy từ mục 2.2 (lọc `scope="Organization"` và chưa có trong danh sách thành viên).

### 7.7 `GET /ClassGroup/{classGroupId}/members`
**Auth:** `Roles="Teacher"` — trả mảng `ChildProfileDto` (shape giống mục 2.1).

### 7.8 `DELETE /ClassGroup/{classGroupId}/members/{childProfileId}`
**Auth:** `Roles="Teacher"` — xoá cứng, không giữ lịch sử, nên xác nhận trước khi gọi.

---

## 8. ChildAccessCredentialController — base route `api/v1/ChildAccessCredential`

### 8.1 `PUT /ChildAccessCredential/{childProfileId}`
**Auth:** `Roles="Parent,Teacher"` (chỉ Supervisor thiết lập, trẻ không tự đổi PIN được)
**Request:** `{ "avatarId": "avatar_03", "pin": "1234" }` (`pin` regex `^\d{4,6}$`, validate ngay trên input trước khi gửi)
**Response:** `{ "success": true, "message": "Cập nhật mã truy cập thành công.", "data": null, "errors": null }`
**Trang UI:** tab "Mã PIN" trong `/profiles/[id]`.

### 8.2 `GET /ChildAccessCredential/{childProfileId}`
**Auth:** `Roles="Parent,Teacher"`
**Response:**
```json
{ "success": true, "message": "Lấy thông tin mã truy cập thành công.", "data": { "childProfileId": 101, "avatarId": "avatar_03", "hasPin": true, "isLocked": false }, "errors": null }
```
**Lưu ý:** không bao giờ trả PIN thật hay hash.

### 8.3 `DELETE /ChildAccessCredential/{childProfileId}`
**Auth:** `Roles="Parent,Teacher"` — `{ "success": true, "message": "Thu hồi mã truy cập thành công.", "data": null, "errors": null }`. Sau đó `hasPin` về `false`, trẻ mất khả năng đăng nhập độc lập cho tới khi set PIN mới.

### 8.4 `POST /ChildAccessCredential/{childProfileId}/login`
**Auth:** AllowAnonymous (không cần Supervisor JWT — trẻ không có `UserAccount`)
**Request:** `{ "pin": "1234" }`
**Response:**
```json
{ "success": true, "message": "Đăng nhập thành công.", "data": { "childProfileId": 101, "avatarId": "avatar_03", "accessToken": "eyJhbGciOi...", "expiresInSeconds": 14400 }, "errors": null }
```
**Business rule:** khoá sau 5 lần nhập sai liên tiếp (15 phút) — hiển thị `message` trả về, không hardcode số phút. `accessToken` này là **Child Access Token**, khác hẳn Supervisor JWT, lưu ở key `childSession` (KHÔNG ghi đè `supervisorAuth`).
**Trang UI — luồng Kid Mode 3 bước:**
1. `/kids` — hiển thị avatar/nickname từng trẻ để chọn (tái sử dụng `data` từ mục 2.2). Click 1 trẻ → `/kids/{childProfileId}/pin`.
2. `/kids/[childProfileId]/pin` — form nhập PIN, gọi API này. Sau thành công → điều hướng `/kids/{childProfileId}/home`.
3. Thoát Kid Mode: xoá `childSession` khỏi storage, điều hướng về `/profiles` (phiên Supervisor trong `supervisorAuth` không bị ảnh hưởng).

### 8.5 `GET /ChildAccessCredential/me`
**Auth:** `[Authorize(Policy = "ChildSession")]` — header `Authorization: Bearer <childAccessToken>` từ mục 8.4, KHÔNG dùng Supervisor token.
**Response:** `{ "success": true, "message": "...", "data": { "childProfileId": 101, "nickname": "Bin", "ageBand": "Age_6_8" }, "errors": null }`
**Trang UI:** `/kids/[childProfileId]/home` (bước 3 của luồng Kid Mode ở mục 8.4). Đây là API duy nhất hiện có cho Child session — các tính năng đọc truyện/quiz/TTS dự kiến dùng chung policy `"ChildSession"` này nhưng **chưa được backend triển khai**, không tạo API call cho các tính năng đó ở giai đoạn này.

---

## 9. ContentCategoryController — base route `api/v1/ContentCategory`

### 9.1 `GET /ContentCategory`
**Auth:** `[Authorize]` (mọi role đã đăng nhập — Parent/Teacher gọi được bình thường). Chỉ `POST`/`PUT`/`DELETE` (tạo/sửa/xoá) mới giữ `Administrator`-only.
**Response:**
```json
{ "success": true, "message": "Lấy danh sách Content Category thành công.", "data": [{ "id": 1, "code": "FANTASY", "displayName": "Cổ tích - Kỳ ảo", "isActive": true }], "errors": null }
```
**Trang UI:** không có trang riêng — API nền cho dropdown chọn category ở tab "Chính sách an toàn" trong `/profiles/[id]` (mục 4.1).

### 9.2 `GET /ContentCategory/{contentCategoryId}`
**Auth:** `[Authorize]` chung cho mọi role (giống mục 9.1).

---

## 10. AuditLog (route trên AuthController/ChildProfileController)

Xem chi tiết đầy đủ + trang UI tại mục **1.14** (`GET /Auth/me/audit-log`) và **2.7** (`GET /ChildProfile/{id}/audit-log`).

---

## 11. OrganizationController — base route `api/v1/Organization`

**Lưu ý quan trọng:** entity `Organization` thật trong DB không có field `description`; thay vào đó có `address`, `contactEmail`, `verificationStatus` (enum: PendingVerification/Active/Suspended/PendingReverification/Rejected).

### 11.1 `POST /Organization`
**Auth:** `Roles="Teacher"`
**Request:**
```json
{ "name": "Trường Tiểu học ABC", "address": "123 Đường Lê Lợi, Q1, TP.HCM", "contactEmail": "lienhe@abc.edu.vn" }
```
`name` 1-150 ký tự bắt buộc · `address` optional ≤255 · `contactEmail` optional, đúng định dạng email, ≤150.
**Response (HTTP 201):**
```json
{
  "success": true, "message": "Tạo tổ chức thành công.",
  "data": {
    "id": 3, "name": "Trường Tiểu học ABC", "address": "123 Đường Lê Lợi, Q1, TP.HCM",
    "contactEmail": "lienhe@abc.edu.vn", "verificationStatus": "PendingVerification",
    "createdByUserId": 20, "createdAt": "2026-09-15T10:00:00Z"
  },
  "errors": null
}
```
**Business rule — QUAN TRỌNG cho UI:** Organization mới tạo luôn ở trạng thái `"PendingVerification"`. `ChildProfileService` đã có sẵn logic chặn: chỉ Organization ở trạng thái `"Active"` mới dùng được để tạo `ChildProfile`/`ClassGroup` (mục 2.1, 7.1). **UI bắt buộc hiển thị rõ `verificationStatus`** trên dropdown chọn Organization và giải thích vì sao Organization vừa tạo chưa chọn được ngay — đây không phải lỗi, là quy trình duyệt của Admin (ngoài phạm vi Luồng 1).
**Trang UI:** `/organizations/new` → điều hướng `/organizations/{id}`.

### 11.2 `GET /Organization/mine`
**Auth:** `Roles="Parent,Teacher"`
**Response:** mảng `OrganizationDto` (shape giống `data` ở mục 11.1) — các Organization mà user hiện tại có `OrganizationMembership`.
**Giới hạn đã biết:** `OrganizationMembership.OrgRole` chỉ có `"Teacher"`/`"SchoolAdmin"`, không có role đại diện Parent → một tài khoản Parent thuần tuý sẽ luôn nhận mảng rỗng ở API này (không phải lỗi/403).
**Trang UI:** `/organizations` — thẻ hiển thị Name, Address, badge `verificationStatus`. Nút "Tạo tổ chức mới". Chỉ hiện trong menu nếu role = Teacher. Cũng dùng cho dropdown chọn Organization ở `/profiles/new` (mục 2.1) và `/classes/new` (mục 7.1).

### 11.3 `GET /Organization/{id}`
**Auth:** `Roles="Parent,Teacher"`, 403 nếu user không có membership với Organization này.
**Response:** `OrganizationDto` đơn, shape giống mục 11.1.
**Trang UI:** `/organizations/[id]` — hiển thị rõ badge trạng thái duyệt.

---

## 12. NotificationController — base route `api/v1/Notification`

### 12.1 `GET /Notification`
**Auth:** `[Authorize]`
**Response:** `NotificationDto[] { id, type, payload?, status, readAt?, createdAt }`, mới nhất trước, không phân trang.
**Trang UI:** icon chuông trên header toàn app — badge số lượng đếm từ `status !== "Read"`, gọi lại API mỗi khi mở dropdown hoặc theo interval polling.

### 12.2 `PUT /Notification/{notificationId}/read`
**Auth:** `[Authorize]` — đánh dấu 1 thông báo đã đọc.

### 12.3 `PUT /Notification/read-all`
**Auth:** `[Authorize]`
**Request:** không body.
**Response:** `{ "success": true, "message": "Đã đánh dấu tất cả thông báo là đã đọc.", "data": null, "errors": null }`
**Trang UI:** nút **"Đánh dấu tất cả đã đọc"** vào dropdown/trang `/notifications`.

### 12.4 `DELETE /Notification/{notificationId}`
**Auth:** `[Authorize]` — xoá 1 thông báo.

---

## Ghi chú tổng hợp cần lưu ý khi code

1. **2 hệ thống token song song** — tách 2 client HTTP riêng (`supervisorApi`, `childApi`) để tránh gắn nhầm header (mục 0).
2. **Chọn `Supervision/...` cho permission grant/revoke**, không dùng song song với `Permission/...` để tránh code trùng lặp (2 route cùng trỏ 1 service, mục 5.8-5.9 và mục 6).
3. **BR-1.9** (điều kiện activate) nên hiển thị rõ ràng dạng checklist trên UI thay vì chỉ disable nút "Kích hoạt" không rõ lý do (mục 2.5).
4. **Enum gửi/nhận luôn là string theo tên**, không convert sang số (mục 0).
5. **Organization mới tạo ở trạng thái PendingVerification**, chưa dùng ngay được cho ChildProfile/ClassGroup — hiển thị rõ badge trạng thái (mục 11.1).

---

## Bảng tổng hợp trạng thái toàn bộ API Luồng 1

| # | Controller | Endpoint | Trang UI đề xuất |
|---|---|---|---|
| 1.1-1.10 | Auth | register, verify-email, login, forgot-password, reset-password, change-password, refresh-token, me, logout, logout-all-devices | `/register`, `/verify-email`, `/login`, `/forgot-password`, `/reset-password`, `/account` |
| 1.11 | Auth | `POST resend-verification-email` | Nút trong `/verify-email` |
| 1.12-1.13 | Auth | `GET/DELETE sessions[/{id}]` | Section "Thiết bị đăng nhập" trong `/account` |
| 1.14 | Auth | `GET me/audit-log` | `/account/activity` (mới) |
| 2.1-2.6 | ChildProfile | CRUD + activate | `/profiles`, `/profiles/new`, `/profiles/[id]` |
| 2.7 | ChildProfile | `GET {id}/audit-log` | Tab "Lịch sử thay đổi" trong `/profiles/[id]` |
| 3.1-3.3 | LearningProfile | PUT/POST/GET/DELETE | Tab "Hồ sơ học tập" trong `/profiles/[id]` |
| 4.1-4.3 | SafetyPolicy | PUT/POST/GET/DELETE | Tab "Chính sách an toàn" trong `/profiles/[id]` |
| 5.1-5.9 | Supervision | invitations, relationships, permissions | Tab "Giám sát & Lời mời" trong `/profiles/[id]`, `/invitations/accept` |
| 6.1 | Permission | `GET` danh mục | Checkbox list quyền trong tab Giám sát |
| 7.1-7.8 | ClassGroup | CRUD + members | `/classes`, `/classes/new`, `/classes/[id]` (Teacher) |
| 8.1-8.5 | ChildAccessCredential | CRUD + login + me | Tab "Mã PIN" trong `/profiles/[id]`; `/kids`, `/kids/[id]/pin`, `/kids/[id]/home` |
| 9.1-9.2 | ContentCategory | `GET` list/detail | Nền cho dropdown category trong tab Safety Policy |
| 10.1-10.2 | AuditLog | `GET me/audit-log`, `GET ChildProfile/{id}/audit-log` | `/account/activity`, tab "Lịch sử thay đổi" trong `/profiles/[id]` |
| 11.1-11.3 | Organization | `POST`, `GET mine`, `GET {id}` | `/organizations`, `/organizations/new`, `/organizations/[id]` |
| 12.1-12.4 | Notification | `GET`, `PUT {id}/read`, `PUT read-all`, `DELETE {id}` | `/notifications` |

**Tổng: 49/49 endpoint Luồng 1 đã có sẵn trong code (build + `dotnet test` 281/281 pass, xác minh ngày 2026-09-15).**
