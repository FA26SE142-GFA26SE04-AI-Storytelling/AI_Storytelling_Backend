# Bảng Kế Hoạch & Test Cases Toàn Diện API Luồng 1 (Profile & Supervision)

> **Tài liệu tham chiếu:** [2026-09-15-luong-1-frontend-api-integration-guide.md](file:///e:/Capstone/AI_Storytelling_Backend/docs/superpowers/plans/2026-09-15-luong-1-frontend-api-integration-guide.md) và [2026-09-15-role-based-account-provisioning.md](file:///e:/Capstone/AI_Storytelling_Backend/docs/superpowers/plans/2026-09-15-role-based-account-provisioning.md)  
> **Mục đích:** Cung cấp bộ kịch bản kiểm thử (Test Cases) chi tiết cho **tất cả API ở toàn bộ các Controller thuộc Luồng 1**, định dạng chuẩn để copy trực tiếp vào Swagger UI hoặc Postman.

---

## 0. Môi trường kiểm thử & Tài khoản mẫu (Seed Data)

* **Base URL:** `https://localhost:7048/api/v1` (hoặc cổng HTTP do môi trường local chỉ định)
* **Quy ước Header:**
  * Endpoint bảo mật Supervisor: `Authorization: Bearer <SUPERVISOR_JWT_TOKEN>`
  * Endpoint chế độ trẻ em Kid Mode: `Authorization: Bearer <CHILD_SESSION_TOKEN>`
* **Tài khoản có sẵn trong DB seed (mật khẩu chung: `Demo@123`):**
  * **Administrator:** `admin_demo` hoặc `admin@example.com`
  * **Teacher (Giáo viên / SchoolAdmin):** `teacher_demo1` hoặc `teacher1@example.com` (Member trường Id: `1`)
  * **Parent 1:** `parent_demo1` hoặc `parent1@example.com` (Đã verify email)
  * **Parent 2:** `parent_demo2` hoặc `parent2@example.com` (Đã verify email)
  * **Parent 3 (Chưa verify):** `parent_demo5` hoặc `parent5@example.com`

---

## 1. AuthController (`/api/v1/Auth`)

### TC-AUTH-01: Tự đăng ký tài khoản Phụ huynh (Happy Path)
* **Endpoint:** `POST /api/v1/Auth/register`
* **Auth:** Không (AllowAnonymous)
* **Request Body:**
```json
{
  "username": "parent_test_01",
  "email": "parent_test01@example.com",
  "fullName": "Nguyễn Văn Phụ Huynh",
  "password": "Password@123",
  "confirmPassword": "Password@123",
  "phoneNumber": "0912345678"
}
```
* **Kỳ vọng:** `200 OK`
  * `success: true`
  * `message: "Đăng ký thành công. Vui lòng kiểm tra email để lấy mã xác thực trước khi đăng nhập."`
  * Tài khoản được tạo ở trạng thái `Registered`, vai trò `Parent`.

---

### TC-AUTH-02: Xác thực email tài khoản mới (Happy Path)
* **Endpoint:** `POST /api/v1/Auth/verify-email`
* **Auth:** Không (AllowAnonymous)
* **Tiền điều kiện:** Lấy mã xác thực 8 ký tự (hoặc tra cột `VerificationTokenHash` / console log) gửi cho `parent_test01@example.com`.
* **Request Body:**
```json
{
  "email": "parent_test01@example.com",
  "token": "A1B2C3D4"
}
```
* **Kỳ vọng:** `200 OK`
  * `success: true`
  * `message: "Xác thực email thành công. Bạn có thể đăng nhập ngay bây giờ."`

---

### TC-AUTH-03: Gửi lại mã xác thực email (Resend Verification Email)
* **Endpoint:** `POST /api/v1/Auth/resend-verification-email`
* **Auth:** Không (AllowAnonymous)
* **Request Body:**
```json
{
  "email": "parent_test01@example.com"
}
```
* **Kỳ vọng:** `200 OK`
  * `success: true`
  * `message: "Nếu email tồn tại và chưa xác thực, mã xác thực mới đã được gửi."`

---

### TC-AUTH-04: Đăng nhập hệ thống (Happy Path)
* **Endpoint:** `POST /api/v1/Auth/login`
* **Auth:** Không (AllowAnonymous)
* **Request Body:**
```json
{
  "identifier": "parent_demo1",
  "password": "Demo@123"
}
```
* **Kỳ vọng:** `200 OK`
  * `data.accessToken` và `data.refreshToken` được trả về.
  * `data.user.role` là `"Parent"`.
  * `data.user.status` là `"Active"` hoặc `"EmailVerified"`.

---

### TC-AUTH-05: Yêu cầu quên mật khẩu (Forgot Password)
* **Endpoint:** `POST /api/v1/Auth/forgot-password`
* **Auth:** Không (AllowAnonymous)
* **Request Body:**
```json
{
  "email": "parent1@example.com"
}
```
* **Kỳ vọng:** `200 OK`
  * `success: true`
  * `message: "Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi."`

---

### TC-AUTH-06: Đặt lại mật khẩu bằng Reset Token (Reset Password)
* **Endpoint:** `POST /api/v1/Auth/reset-password`
* **Auth:** Không (AllowAnonymous)
* **Request Body:**
```json
{
  "email": "parent1@example.com",
  "resetToken": "<TOKEN_RESET_LẤY_TỪ_EMAIL_HOẶC_DB>",
  "newPassword": "NewPassword@123",
  "confirmPassword": "NewPassword@123"
}
```
* **Kỳ vọng:** `200 OK`
  * `success: true`
  * `message: "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại."`

---

### TC-AUTH-07: Đổi mật khẩu khi đang đăng nhập (Change Password)
* **Endpoint:** `POST /api/v1/Auth/change-password`
* **Auth:** `Bearer <TOKEN_PARENT_HOẶC_TEACHER>`
* **Request Body:**
```json
{
  "currentPassword": "Demo@123",
  "newPassword": "UpdatedPassword@123",
  "confirmPassword": "UpdatedPassword@123"
}
```
* **Kỳ vọng:** `200 OK`
  * `success: true`
  * `message: "Đổi mật khẩu thành công. Vui lòng đăng nhập lại."`

---

### TC-AUTH-08: Làm mới Access Token (Refresh Token Rotation)
* **Endpoint:** `POST /api/v1/Auth/refresh-token`
* **Auth:** Không (AllowAnonymous)
* **Request Body:**
```json
{
  "refreshToken": "<REFRESH_TOKEN_NHẬN_ĐƯỢC_TỪ_BƯỚC_LOGIN>"
}
```
* **Kỳ vọng:** `200 OK`
  * Cấp phát cặp `accessToken` và `refreshToken` mới.
  * Refresh token cũ ngay lập tức bị vô hiệu hóa.

---

### TC-AUTH-09: Lấy hồ sơ tài khoản hiện tại (Get Me)
* **Endpoint:** `GET /api/v1/Auth/me`
* **Auth:** `Bearer <TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `data.id`, `data.username`, `data.email`, `data.role` khớp thông tin token.

---

### TC-AUTH-10: Đăng xuất phiên hiện tại (Logout)
* **Endpoint:** `POST /api/v1/Auth/logout`
* **Auth:** `Bearer <TOKEN>`
* **Request Body:**
```json
{
  "refreshToken": "<REFRESH_TOKEN_CỦA_THIẾT_BỊ>"
}
```
* **Kỳ vọng:** `200 OK`
  * Thu hồi refresh token đã cung cấp.

---

### TC-AUTH-11: Đăng xuất toàn bộ thiết bị (Logout All Devices)
* **Endpoint:** `POST /api/v1/Auth/logout-all-devices`
* **Auth:** `Bearer <TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Tất cả refresh token của user bị vô hiệu. `TokenVersion` tăng lên 1.

---

### TC-AUTH-12: Xem danh sách phiên đăng nhập (Sessions)
* **Endpoint:** `GET /api/v1/Auth/sessions`
* **Auth:** `Bearer <TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về danh sách phiên `{ id, issuedAt, expiresAt }`.

---

### TC-AUTH-13: Thu hồi một phiên đăng nhập cụ thể
* **Endpoint:** `DELETE /api/v1/Auth/sessions/{id}`
* **Auth:** `Bearer <TOKEN>`
* **Tham số URL:** `id` = ID của session lấy từ TC-AUTH-12.
* **Kỳ vọng:** `200 OK`
  * `message: "Thu hồi phiên đăng nhập thành công."`

---

### TC-AUTH-14: Xem lịch sử kiểm toán của tài khoản (Audit Log)
* **Endpoint:** `GET /api/v1/Auth/me/audit-log?pageIndex=1&pageSize=10`
* **Auth:** `Bearer <TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `data.items` chứa danh sách log các hành động: `CHANGE_PASSWORD`, `LOGOUT_ALL_DEVICES`...

---

### TC-AUTH-15: Giáo viên tạo tài khoản Phụ huynh (Provision Parent)
* **Endpoint:** `POST /api/v1/Auth/parents`
* **Auth:** `Bearer <TEACHER_TOKEN>` (Ví dụ token của `teacher_demo1`)
* **Request Body:**
```json
{
  "username": "parent_by_teacher_01",
  "email": "parent_created@example.com",
  "fullName": "Phụ Huynh Do Cô Tạo",
  "phoneNumber": "0988776655"
}
```
* **Kỳ vọng:** `201 Created`
  * `data.role = "Parent"`, `data.status = "PasswordResetPending"`.
  * Phụ huynh nhận token qua email để đặt mật khẩu tại `reset-password`.

---

## 2. ChildProfileController (`/api/v1/ChildProfile`)

### TC-CHILD-01: Tạo hồ sơ trẻ cá nhân (Scope Personal)
* **Endpoint:** `POST /api/v1/ChildProfile`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Request Body:**
```json
{
  "nickname": "Bé Bo",
  "ageBand": "Age_6_8",
  "language": "vi",
  "scope": "Personal"
}
```
* **Kỳ vọng:** `201 Created`
  * `data.status = "Draft"`, `data.scope = "Personal"`, `data.organizationId = null`.
  * Tự động gán người gọi là `Owner` trong quan hệ giám sát. Ghi nhớ `data.id` (ví dụ: `101`).

---

### TC-CHILD-02: Tạo hồ sơ trẻ thuộc trường học (Scope Organization)
* **Endpoint:** `POST /api/v1/ChildProfile`
* **Auth:** `Bearer <TEACHER_TOKEN>`
* **Request Body:**
```json
{
  "nickname": "Bé Na",
  "ageBand": "Age_3_5",
  "language": "vi",
  "scope": "Organization",
  "organizationId": 1,
  "classGroupId": 1
}
```
* **Kỳ vọng:** `201 Created`
  * `data.status = "Draft"`, `data.scope = "Organization"`, `data.organizationId = 1`.

---

### TC-CHILD-03: Lấy danh sách hồ sơ trẻ của tôi (Mine)
* **Endpoint:** `GET /api/v1/ChildProfile/mine`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Mảng danh sách các hồ sơ trẻ mà tài khoản hiện tại đang làm Supervisor.

---

### TC-CHILD-04: Lấy thông tin chi tiết một hồ sơ trẻ
* **Endpoint:** `GET /api/v1/ChildProfile/{id}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>` (phải là supervisor của trẻ)
* **Kỳ vọng:** `200 OK`
  * Trả về chi tiết `ChildProfileDto`.

---

### TC-CHILD-05: Cập nhật thông tin hồ sơ trẻ
* **Endpoint:** `PUT /api/v1/ChildProfile/{id}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Request Body:**
```json
{
  "nickname": "Bé Bo Bo",
  "ageBand": "Age_6_8",
  "language": "vi"
}
```
* **Kỳ vọng:** `200 OK`
  * Nickname được cập nhật thành `"Bé Bo Bo"`.

---

### TC-CHILD-06: Kích hoạt hồ sơ trẻ (Activate - BR-1.9)
* **Endpoint:** `PATCH /api/v1/ChildProfile/{id}/activate`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Request Body:** Không có
* **Kịch bản A (Chưa đủ điều kiện - thiếu LearningProfile hoặc SafetyPolicy hoặc Parent supervisor):**
  * `200 OK` kèm `data.status = "PendingParentConsent"` hoặc `"ReadyForActivation"`.
* **Kịch bản B (Đã đủ 3 điều kiện):**
  * `200 OK` kèm `data.status = "Active"`.

---

### TC-CHILD-07: Xem lịch sử thay đổi hồ sơ trẻ (Audit Log)
* **Endpoint:** `GET /api/v1/ChildProfile/{id}/audit-log?pageIndex=1&pageSize=10`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về danh sách hành động như `CREATE_CHILD_PROFILE`, `UPDATE_CHILD_PROFILE`, `ACTIVATE_CHILD_PROFILE`.

---

### TC-CHILD-08: Lưu trữ / Xóa mềm hồ sơ trẻ (Archive - Owner Only)
* **Endpoint:** `DELETE /api/v1/ChildProfile/{id}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>` (Bắt buộc là `Owner`)
* **Kỳ vọng:** `200 OK`
  * `message: "Lưu trữ hồ sơ thành công."`

---

## 3. LearningProfileController (`/api/v1/LearningProfile`)

### TC-LEARN-01: Thiết lập / Cập nhật hồ sơ học tập (Upsert)
* **Endpoint:** `PUT /api/v1/LearningProfile/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Request Body:**
```json
{
  "readingLevel": 3,
  "comprehensionGoal": "Đọc hiểu truyện ngắn 300 từ và trả lời câu hỏi phản xạ",
  "topics": [
    {
      "topic": "Thế giới động vật hoang dã",
      "relation": "FavoriteTopic"
    },
    {
      "topic": "Kỹ năng giao tiếp bạn bè",
      "relation": "PriorityFocusArea"
    }
  ]
}
```
* **Kỳ vọng:** `200 OK`
  * `data.childProfileId = {childProfileId}`
  * `data.readingLevel = 3`
  * `data.topics` có 2 phần tử.

---

### TC-LEARN-02: Tạo hồ sơ học tập bằng POST
* **Endpoint:** `POST /api/v1/LearningProfile/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Ghi chú:** Endpoint này hiện dùng cùng logic upsert với `PUT`; vì vậy nếu hồ sơ học tập đã tồn tại thì dữ liệu sẽ được cập nhật.
* **Request Body:**
```json
{
  "readingLevel": 2,
  "comprehensionGoal": "Đọc hiểu truyện ngắn và nhận biết nhân vật chính",
  "topics": [
    {
      "topic": "Khám phá thiên nhiên",
      "relation": "FavoriteTopic"
    }
  ]
}
```
* **Kỳ vọng:** `200 OK`
  * `data.childProfileId = {childProfileId}`.
  * `data.readingLevel = 2`.
  * `message: "Tạo Learning Profile thành công."`

---

### TC-LEARN-03: Lấy thông tin hồ sơ học tập
* **Endpoint:** `GET /api/v1/LearningProfile/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về dữ liệu vừa tạo ở TC-LEARN-01.

---

### TC-LEARN-04: Xóa hồ sơ học tập
* **Endpoint:** `DELETE /api/v1/LearningProfile/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `message: "Xoá Learning Profile thành công."`

---

## 4. SafetyPolicyController (`/api/v1/SafetyPolicy`)

### TC-SAFE-01: Thiết lập / Cập nhật chính sách an toàn (Upsert)
* **Endpoint:** `PUT /api/v1/SafetyPolicy/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>` (Cần có quyền `ManageSafetySettings` nếu là Additional Supervisor)
* **Request Body:**
```json
{
  "maxStoryLength": 2500,
  "requiredApprovalMode": "AlwaysManual",
  "parentalGateEnabled": true,
  "consentRecorded": true,
  "categories": [
    {
      "contentCategoryId": 1,
      "rule": "Allowed"
    },
    {
      "contentCategoryId": 2,
      "rule": "Restricted"
    }
  ]
}
```
* **Kỳ vọng:** `200 OK`
  * `data.maxStoryLength = 2500`
  * `data.requiredApprovalMode = "AlwaysManual"`
  * `data.categories` được cập nhật đúng luật.

---

### TC-SAFE-02: Tạo chính sách an toàn bằng POST
* **Endpoint:** `POST /api/v1/SafetyPolicy/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>` (Cần có quyền `ManageSafetySettings` nếu là Additional Supervisor)
* **Ghi chú:** Endpoint này hiện dùng cùng logic upsert với `PUT`; vì vậy nếu Safety Policy đã tồn tại thì dữ liệu sẽ được cập nhật.
* **Request Body:**
```json
{
  "maxStoryLength": 1800,
  "requiredApprovalMode": "AlwaysManual",
  "parentalGateEnabled": true,
  "consentRecorded": true,
  "categories": [
    {
      "contentCategoryId": 1,
      "rule": "Allowed"
    }
  ]
}
```
* **Kỳ vọng:** `200 OK`
  * `data.childProfileId = {childProfileId}`.
  * `data.maxStoryLength = 1800`.
  * `message: "Tạo Safety Policy thành công."`

---

### TC-SAFE-03: Lấy thông tin chính sách an toàn
* **Endpoint:** `GET /api/v1/SafetyPolicy/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về dữ liệu chính sách an toàn của trẻ.

---

### TC-SAFE-04: Xóa chính sách an toàn
* **Endpoint:** `DELETE /api/v1/SafetyPolicy/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `message: "Xoá Safety Policy thành công."`

---

## 5. SupervisionController (`/api/v1/Supervision`)

### TC-SUP-01: Tạo lời mời giám sát (Create Invitation)
* **Endpoint:** `POST /api/v1/Supervision/{childProfileId}/invitations`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>` (Đang giám sát trẻ)
* **Request Body:**
```json
{
  "inviteeEmail": "teacher1@example.com",
  "expiresInDays": 7
}
```
* **Kỳ vọng:** `201 Created`
  * `data.invitationCode` được sinh (dạng `INV-XXXXXX`).
  * `data.status = "Pending"`. Lưu lại `invitationCode`.

---

### TC-SUP-02: Xem danh sách lời mời của hồ sơ trẻ
* **Endpoint:** `GET /api/v1/Supervision/{childProfileId}/invitations`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về danh sách lời mời (Pending/Accepted/Cancelled/Expired).

---

### TC-SUP-03: Hủy lời mời giám sát đang chờ (Cancel Invitation)
* **Endpoint:** `DELETE /api/v1/Supervision/invitations/{invitationId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `message: "Huỷ lời mời thành công."`

---

### TC-SUP-04: Chấp nhận lời mời giám sát (Accept Invitation)
* **Endpoint:** `POST /api/v1/Supervision/invitations/accept`
* **Auth:** `Bearer <INVITEE_TOKEN>` (Tài khoản người được mời, ví dụ `teacher_demo1`)
* **Request Body:**
```json
{
  "invitationCode": "<INVITATION_CODE_TẠO_Ở_TC-SUP-01>"
}
```
* **Kỳ vọng:** `200 OK`
  * `data.supervisorRole = "AdditionalSupervisor"`.
  * Thiết lập quan hệ giám sát mới. Ghi nhận `data.id` (Relationship ID).

---

### TC-SUP-05: Xem danh sách người giám sát hồ sơ trẻ
* **Endpoint:** `GET /api/v1/Supervision/{childProfileId}/relationships`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về danh sách các quan hệ giám sát (Owner và AdditionalSupervisor).

---

### TC-SUP-06: Xem danh sách quyền hạn của một quan hệ giám sát
* **Endpoint:** `GET /api/v1/Supervision/relationships/{relationshipId}/permissions`
* **Auth:** `Bearer <OWNER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Mảng quyền hiện tại của supervisor (ví dụ: `["ViewProgress"]`).

---

### TC-SUP-07: Cấp quyền cho giám sát viên (Grant Permission)
* **Endpoint:** `POST /api/v1/Supervision/relationships/{relationshipId}/permissions/{permission}`
* **Auth:** `Bearer <OWNER_TOKEN>`
* **Ví dụ URL:** `POST /api/v1/Supervision/relationships/1/permissions/ManageSafetySettings`
* **Request Body:** Không có
* **Kỳ vọng:** `200 OK`
  * `message: "Cấp quyền thành công."`

---

### TC-SUP-08: Thu hồi quyền của giám sát viên (Revoke Permission)
* **Endpoint:** `DELETE /api/v1/Supervision/relationships/{relationshipId}/permissions/{permission}`
* **Auth:** `Bearer <OWNER_TOKEN>`
* **Ví dụ URL:** `DELETE /api/v1/Supervision/relationships/1/permissions/ManageSafetySettings`
* **Kỳ vọng:** `200 OK`
  * `message: "Thu hồi quyền thành công."`

---

### TC-SUP-09: Chấm dứt quan hệ giám sát (Terminate Relationship)
* **Endpoint:** `DELETE /api/v1/Supervision/relationships/{relationshipId}`
* **Auth:** `Bearer <OWNER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `message: "Thu hồi quan hệ giám sát thành công."`

---

### TC-SUP-10: Chuyển quyền sở hữu hồ sơ trẻ (Transfer Ownership)
* **Endpoint:** `POST /api/v1/Supervision/{childProfileId}/transfer-ownership`
* **Auth:** `Bearer <OWNER_TOKEN>` (chỉ Owner hiện tại)
* **Tiền điều kiện:** `targetSupervisorUserId` là một Additional Supervisor đang hoạt động của đúng hồ sơ trẻ.
* **Request Body:**
```json
{
  "targetSupervisorUserId": 3
}
```
* **Kỳ vọng:** `200 OK`
  * `data.supervisorUserId = 3`.
  * `data.supervisorRole = "Owner"`.
  * Owner cũ trở thành `AdditionalSupervisor` và có quyền mặc định `ViewResults`.
  * Owner mới không còn các dòng permission riêng lẻ.
  * `message: "Chuyển nhượng quyền Owner thành công."`

---

## 6. PermissionController (`/api/v1/Permission`)

### TC-PERM-01: Lấy danh mục tất cả quyền giám sát trong hệ thống
* **Endpoint:** `GET /api/v1/Permission`
* **Auth:** `Bearer <TOKEN>` (Mọi role)
* **Kỳ vọng:** `200 OK`
  * Trả về đầy đủ danh mục enum quyền:
```json
{
  "success": true,
  "message": "Lấy danh mục quyền thành công.",
  "data": [
    "ViewProgress",
    "ViewResults",
    "AssignActivity",
    "ReceiveReport",
    "ApproveReadingLevel",
    "ApproveStory",
    "ManageSafetySettings",
    "GenerateStory"
  ],
  "errors": null
}
```

---

### TC-PERM-02: Lấy quyền của một quan hệ giám sát qua PermissionController
* **Endpoint:** `GET /api/v1/Permission/relationships/{relationshipId}`
* **Auth:** `Bearer <OWNER_TOKEN>`
* **Ghi chú:** Đây là route tương thích, dùng cùng nghiệp vụ với `GET /api/v1/Supervision/relationships/{relationshipId}/permissions`.
* **Kỳ vọng:** `200 OK`
  * `data` là mảng tên permission đã cấp, ví dụ `["ViewResults", "ManageSafetySettings"]`.

---

### TC-PERM-03: Cấp quyền qua PermissionController
* **Endpoint:** `POST /api/v1/Permission/relationships/{relationshipId}/{permission}`
* **Auth:** `Bearer <OWNER_TOKEN>`
* **Ví dụ URL:** `POST /api/v1/Permission/relationships/1/ManageSafetySettings`
* **Request Body:** Không có
* **Kỳ vọng:** `200 OK`
  * `message: "Cấp quyền thành công."`

---

### TC-PERM-04: Thu hồi quyền qua PermissionController
* **Endpoint:** `DELETE /api/v1/Permission/relationships/{relationshipId}/{permission}`
* **Auth:** `Bearer <OWNER_TOKEN>`
* **Ví dụ URL:** `DELETE /api/v1/Permission/relationships/1/ManageSafetySettings`
* **Kỳ vọng:** `200 OK`
  * `message: "Thu hồi quyền thành công."`

---

## 7. ClassGroupController (`/api/v1/ClassGroup`)

### TC-CLASS-01: Giáo viên tạo lớp học mới (Create Class)
* **Endpoint:** `POST /api/v1/ClassGroup`
* **Auth:** `Bearer <TEACHER_TOKEN>`
* **Request Body:**
```json
{
  "name": "Lớp 1A - Họa Mi",
  "organizationId": 1
}
```
* **Kỳ vọng:** `201 Created`
  * `data.name = "Lớp 1A - Họa Mi"`, `data.status = "Active"`. Ghi nhớ `data.id`.

---

### TC-CLASS-02: Lấy danh sách lớp học của giáo viên (Mine)
* **Endpoint:** `GET /api/v1/ClassGroup/mine`
* **Auth:** `Bearer <TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về danh sách lớp mà giáo viên hiện tại phụ trách.

---

### TC-CLASS-03: Lấy chi tiết lớp học
* **Endpoint:** `GET /api/v1/ClassGroup/{id}`
* **Auth:** `Bearer <TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về `ClassGroupDto` tương ứng.

---

### TC-CLASS-04: Cập nhật thông tin lớp học
* **Endpoint:** `PUT /api/v1/ClassGroup/{id}`
* **Auth:** `Bearer <TEACHER_TOKEN>`
* **Request Body:**
```json
{
  "name": "Lớp 1A - Sơn Ca (Đổi tên)"
}
```
* **Kỳ vọng:** `200 OK`
  * Tên lớp được cập nhật mới.

---

### TC-CLASS-05: Thêm học sinh vào lớp học
* **Endpoint:** `POST /api/v1/ClassGroup/{classGroupId}/members/{childProfileId}`
* **Auth:** `Bearer <TEACHER_TOKEN>`
* **Request Body:** Không có
* **Kỳ vọng:** `200 OK`
  * `message: "Thêm thành viên thành công."`

---

### TC-CLASS-06: Xem danh sách học sinh trong lớp
* **Endpoint:** `GET /api/v1/ClassGroup/{classGroupId}/members`
* **Auth:** `Bearer <TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về mảng danh sách `ChildProfileDto` của các học sinh trong lớp.

---

### TC-CLASS-07: Xóa học sinh khỏi lớp
* **Endpoint:** `DELETE /api/v1/ClassGroup/{classGroupId}/members/{childProfileId}`
* **Auth:** `Bearer <TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `message: "Xoá thành viên khỏi lớp thành công."`

---

### TC-CLASS-08: Lưu trữ / Xóa lớp học
* **Endpoint:** `DELETE /api/v1/ClassGroup/{id}`
* **Auth:** `Bearer <TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `message: "Lưu trữ lớp học thành công."`

---

## 8. ChildAccessCredentialController (`/api/v1/ChildAccessCredential`)

### TC-PIN-01: Phụ huynh thiết lập / đổi mã PIN cho bé
* **Endpoint:** `PUT /api/v1/ChildAccessCredential/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Request Body:**
```json
{
  "avatarId": "avatar_bear_01",
  "pin": "1234"
}
```
* **Kỳ vọng:** `200 OK`
  * `message: "Cập nhật mã truy cập thành công."`

---

### TC-PIN-02: Xem trạng thái mã PIN của bé
* **Endpoint:** `GET /api/v1/ChildAccessCredential/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `data.hasPin = true`, `data.isLocked = false` (tuyệt đối không lộ mã PIN hay hash).

---

### TC-PIN-03: Trẻ đăng nhập vào chế độ Kid Mode bằng mã PIN
* **Endpoint:** `POST /api/v1/ChildAccessCredential/{childProfileId}/login`
* **Auth:** Không (AllowAnonymous)
* **Request Body:**
```json
{
  "pin": "1234"
}
```
* **Kỳ vọng:** `200 OK`
  * Trả về `data.accessToken` (Đây là **Child Session Token**, thời hạn 4 tiếng).
  * Lưu token này vào biến riêng `CHILD_SESSION_TOKEN`.

---

### TC-PIN-04: Trẻ truy cập màn hình chính Kid Mode bằng Child Token
* **Endpoint:** `GET /api/v1/ChildAccessCredential/me`
* **Auth:** `Bearer <CHILD_SESSION_TOKEN>` (Sử dụng token lấy từ TC-PIN-03, policy `ChildSession`)
* **Kỳ vọng:** `200 OK`
  * Trả về thông tin của bé:
```json
{
  "success": true,
  "message": "Lấy thông tin thành công.",
  "data": {
    "childProfileId": 101,
    "nickname": "Bé Bo Bo",
    "ageBand": "Age_6_8"
  },
  "errors": null
}
```

---

### TC-PIN-05: Thu hồi / Xóa mã PIN của bé
* **Endpoint:** `DELETE /api/v1/ChildAccessCredential/{childProfileId}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * `message: "Thu hồi mã truy cập thành công."`
  * Gọi lại TC-PIN-02 sẽ thấy `data.hasPin = false`.

---

## 9. ContentCategoryController (`/api/v1/ContentCategory`)

### TC-CAT-01: Lấy danh sách danh mục nội dung truyện
* **Endpoint:** `GET /api/v1/ContentCategory`
* **Auth:** `Bearer <TOKEN>` (Mọi role)
* **Kỳ vọng:** `200 OK`
  * Trả về danh sách danh mục (VD: Cổ tích, Khoa học, Thám hiểm, Kỹ năng sống...) để đổ vào dropdown thiết lập chính sách an toàn.

---

### TC-CAT-02: Lấy chi tiết một danh mục nội dung
* **Endpoint:** `GET /api/v1/ContentCategory/{contentCategoryId}`
* **Auth:** `Bearer <TOKEN>` (Mọi role)
* **Kỳ vọng:** `200 OK`
  * Trả về thông tin `{ id, code, displayName, isActive }`.

---

### TC-CAT-03: Administrator tạo danh mục nội dung
* **Endpoint:** `POST /api/v1/ContentCategory`
* **Auth:** `Bearer <ADMIN_TOKEN>`
* **Request Body:**
```json
{
  "code": "SPACE_EXPLORATION",
  "displayName": "Khám phá vũ trụ",
  "isActive": true
}
```
* **Kỳ vọng:** `201 Created`
  * `data.code = "SPACE_EXPLORATION"`.
  * `data.displayName = "Khám phá vũ trụ"`.
  * `data.isActive = true`.

---

### TC-CAT-04: Administrator cập nhật danh mục nội dung
* **Endpoint:** `PUT /api/v1/ContentCategory/{contentCategoryId}`
* **Auth:** `Bearer <ADMIN_TOKEN>`
* **Request Body:**
```json
{
  "displayName": "Khám phá không gian",
  "isActive": true
}
```
* **Kỳ vọng:** `200 OK`
  * `data.id = {contentCategoryId}`.
  * `data.displayName = "Khám phá không gian"`.
  * `message: "Cập nhật Content Category thành công."`

---

### TC-CAT-05: Administrator xóa danh mục nội dung
* **Endpoint:** `DELETE /api/v1/ContentCategory/{contentCategoryId}`
* **Auth:** `Bearer <ADMIN_TOKEN>`
* **Tiền điều kiện:** Dùng danh mục vừa tạo ở TC-CAT-03 và chưa được tham chiếu bởi Safety Policy.
* **Kỳ vọng:** `200 OK`
  * `message: "Xoá Content Category thành công."`

---

## 10. OrganizationController (`/api/v1/Organization`)

### TC-ORG-01: Administrator tạo Tổ chức kèm tài khoản SchoolAdmin
* **Endpoint:** `POST /api/v1/Organization`
* **Auth:** `Bearer <ADMIN_TOKEN>`
* **Request Body:**
```json
{
  "name": "Trường Tiểu Học Ban Mai",
  "address": "Phố Triều Khúc, Thanh Xuân, Hà Nội",
  "contactEmail": "contact@banmai.edu.vn",
  "schoolAdminUsername": "schooladmin_banmai",
  "schoolAdminEmail": "admin@banmai.edu.vn",
  "schoolAdminFullName": "Nguyễn Thị Hiệu Trưởng",
  "schoolAdminPhoneNumber": "0911223344"
}
```
* **Kỳ vọng:** `201 Created`
  * `data.organization.verificationStatus = "Active"`.
  * `data.schoolAdminAccount.role = "Teacher"`, `data.schoolAdminAccount.status = "PasswordResetPending"`.

---

### TC-ORG-02: SchoolAdmin tạo tài khoản Giáo viên thuộc trường
* **Endpoint:** `POST /api/v1/Organization/{id}/teachers`
* **Auth:** `Bearer <SCHOOL_ADMIN_TOKEN>`
* **Request Body:**
```json
{
  "username": "teacher_hoa_banmai",
  "email": "hoa.teacher@banmai.edu.vn",
  "fullName": "Phan Thị Hoa",
  "phoneNumber": "0988112233"
}
```
* **Kỳ vọng:** `201 Created`
  * `data.role = "Teacher"`, `data.status = "PasswordResetPending"`.
  * Tự động thêm membership `OrgRole = Teacher` vào tổ chức `{id}`.

---

### TC-ORG-03: Xem danh sách tổ chức của người dùng hiện tại
* **Endpoint:** `GET /api/v1/Organization/mine`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Trả về danh sách tổ chức mà user có membership active.

---

### TC-ORG-04: Xem chi tiết một tổ chức
* **Endpoint:** `GET /api/v1/Organization/{id}`
* **Auth:** `Bearer <PARENT_OR_TEACHER_TOKEN>` (phải là thành viên tổ chức)
* **Kỳ vọng:** `200 OK`
  * Trả về thông tin tổ chức tương ứng.

---

## 11. NotificationController (`/api/v1/Notification`)

### TC-NOTI-01: Lấy danh sách thông báo
* **Endpoint:** `GET /api/v1/Notification`
* **Auth:** `Bearer <TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Mảng danh sách thông báo sắp xếp mới nhất trước (`status: "Unread"` hoặc `"Read"`).

---

### TC-NOTI-02: Đánh dấu một thông báo đã đọc
* **Endpoint:** `PUT /api/v1/Notification/{notificationId}/read`
* **Auth:** `Bearer <TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Thông báo chuyển sang trạng thái `Read`.

---

### TC-NOTI-03: Đánh dấu tất cả thông báo là đã đọc
* **Endpoint:** `PUT /api/v1/Notification/read-all`
* **Auth:** `Bearer <TOKEN>`
* **Request Body:** Không có
* **Kỳ vọng:** `200 OK`
  * `message: "Đã đánh dấu tất cả thông báo là đã đọc."`

---

### TC-NOTI-04: Xóa một thông báo
* **Endpoint:** `DELETE /api/v1/Notification/{notificationId}`
* **Auth:** `Bearer <TOKEN>`
* **Kỳ vọng:** `200 OK`
  * Thông báo bị xóa khỏi danh sách.

---

## 12. Kịch bản kiểm thử E2E liên hoàn (Golden Happy Path)

Thực hiện tuần tự 8 chặng sau trên Swagger để kiểm chứng trọn vẹn Luồng 1:

1. **Chặng 1 - Chuỗi phân quyền tài khoản (Account Provisioning):**
   * Admin (`admin_demo`) login $\rightarrow$ Tạo Org & SchoolAdmin (`POST /Organization`).
   * SchoolAdmin reset password (`POST /Auth/reset-password`) $\rightarrow$ login $\rightarrow$ Tạo Giáo viên (`POST /Organization/{id}/teachers`).
   * Giáo viên reset password $\rightarrow$ login $\rightarrow$ Tạo Phụ huynh (`POST /Auth/parents`).
   * Phụ huynh reset password $\rightarrow$ login lấy token Phụ huynh.
2. **Chặng 2 - Khởi tạo hồ sơ trẻ:**
   * Phụ huynh gọi `POST /ChildProfile` tạo bé "Bé Bo" (Trạng thái: `Draft`).
3. **Chặng 3 - Cấu hình điều kiện tiên quyết kích hoạt (BR-1.9):**
   * Gọi `PUT /LearningProfile/{id}` để thiết lập trình độ đọc và sở thích.
   * Gọi `GET /ContentCategory` lấy danh mục $\rightarrow$ Gọi `PUT /SafetyPolicy/{id}` cài đặt an toàn.
4. **Chặng 4 - Kích hoạt hồ sơ:**
   * Gọi `PATCH /ChildProfile/{id}/activate` $\rightarrow$ Xác nhận trạng thái chuyển thành `Active`.
5. **Chặng 5 - Mời & Phân quyền Giám sát viên:**
   * Phụ huynh gọi `POST /Supervision/{childProfileId}/invitations` mời Giáo viên.
   * Giáo viên đăng nhập $\rightarrow$ gọi `POST /Supervision/invitations/accept`.
   * Phụ huynh gọi `POST /Supervision/relationships/{relId}/permissions/ManageSafetySettings` để cấp quyền quản lý an toàn cho Giáo viên.
6. **Chặng 6 - Giáo viên quản lý lớp học:**
   * Giáo viên gọi `POST /ClassGroup` tạo lớp "Lớp 1A".
   * Giáo viên gọi `POST /ClassGroup/{classId}/members/{childProfileId}` thêm bé vào lớp.
7. **Chặng 7 - Chế độ Trẻ Em (Kid Mode):**
   * Phụ huynh gọi `PUT /ChildAccessCredential/{childProfileId}` cài PIN `1234`.
   * Bé gọi `POST /ChildAccessCredential/{childProfileId}/login` với PIN `1234` $\rightarrow$ nhận Child Token.
   * Bé gọi `GET /ChildAccessCredential/me` để vào trang chủ đọc truyện.
8. **Chặng 8 - Thông báo & Nhật ký:**
   * Kiểm tra thông báo qua `GET /Notification`.
   * Kiểm tra lịch sử hoạt động qua `GET /ChildProfile/{id}/audit-log`.
