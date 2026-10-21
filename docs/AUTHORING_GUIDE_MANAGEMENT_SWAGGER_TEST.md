# Kiểm thử quản lý hướng dẫn sáng tác bằng Swagger

Trạng thái: **chưa chạy trên API thật với PostgreSQL** (xem mục "Kết quả chạy thật" ở cuối). Điều kiện trước khi chạy: migration `AddManagedAuthoringGuides` đã được áp dụng (theo yêu cầu riêng của người dùng), API đã build lại và khởi động; log khởi động có dòng `Authoring guide catalog seeded (parent-vi-1)` hoặc `already present`. Swagger: `http://localhost:5259/` (Docker Compose) hoặc địa chỉ API đang chạy.

Cần: một tài khoản Moderator (ví dụ `moderator1@example.com`), một tài khoản Administrator (`admin@example.com`) và một Parent (`parent1@example.com`). Mật khẩu seed lấy từ người quản trị dữ liệu, không ghi vào tài liệu. Nhân sự có thể phải qua MFA TOTP.

Ký hiệu: `R0`, `R1`... là `revisionToken` thực tế bạn nhận được; `V1`, `V2` là `versionId` thực tế. Luôn dùng **giá trị mới nhất vừa nhận**. Các số phiên bản và số mục bên dưới giả định database mới đã seed, chưa có thay đổi. Hủy nháp không đưa lịch sử đã phát hành về trạng thái ban đầu; nếu chạy lại trên database đã dùng thì phải điều chỉnh các giá trị mong đợi theo dữ liệu thực tế.

## Route đối chiếu

Thay `{versionId}`, `{guideId}` bằng giá trị thực tế; các bước bên dưới dùng ví dụ cụ thể của các route này.

| Route | Bước |
|---|---|
| `GET /api/v1/moderation/authoring-guides` | 3–4 |
| `GET /api/v1/moderation/authoring-guides/versions` | 5, 16 |
| `GET /api/v1/moderation/authoring-guides/versions/{versionId}` | 5 |
| `POST /api/v1/moderation/authoring-guides/draft` | 6, 17–19 |
| `PUT /api/v1/moderation/authoring-guides/draft/items/{guideId}` | 8–10 |
| `DELETE /api/v1/moderation/authoring-guides/draft/items/{guideId}` | 11, 19 |
| `DELETE /api/v1/moderation/authoring-guides/draft` | 17, 19 |
| `POST /api/v1/moderation/authoring-guides/draft/preview` | 12–13 |
| `POST /api/v1/moderation/authoring-guides/draft/publish` | 14, 18–19 |
| `GET /api/v1/authoring-guides` | 7, 15 |
| `POST /api/v1/authoring-guides/{guideId}/render` | 15 |

## A. Đăng nhập

1. `POST /api/v1/Auth/login` với `{ "identifier": "moderator1@example.com", "password": "<mật khẩu seed>" }`. Nếu `data.mfaSetupRequired` hoặc `data.mfaRequired` là `true`: lấy `data.mfaChallengeToken` (và `data.mfaProvisioningUri` khi thiết lập lần đầu, quét vào ứng dụng TOTP), rồi `POST /api/v1/Auth/mfa/verify` với `{ "challengeToken": "...", "code": "<6 số hiện tại>" }`. Lưu `data.accessToken` thành MOD_TOKEN. Bấm **Authorize** trong Swagger với `Bearer MOD_TOKEN`.
2. Làm tương tự cho `admin@example.com` → ADMIN_TOKEN, và `POST /api/v1/Auth/login` với `parent1@example.com` → PARENT_TOKEN (Parent không qua MFA).

## B. Phân quyền

3. Với PARENT_TOKEN: `GET /api/v1/moderation/authoring-guides` → **403**. Không token → **401**.
4. Với MOD_TOKEN: `GET /api/v1/moderation/authoring-guides` → **200**; `data.published.version.catalogVersion` = `parent-vi-1`, `itemCount` = 30, `data.draft` = `null`. Lưu `data.revisionToken` thành R0. Với ADMIN_TOKEN cũng phải **200**.
5. `GET /api/v1/moderation/authoring-guides/versions?pageIndex=1&pageSize=10` → 200, `totalCount` = 1; lưu `items[0].versionId` thành V1. `GET /api/v1/moderation/authoring-guides/versions/{V1}` → 200, `items` có 30 mục. `GET .../versions/999999` → **404**. `GET .../versions?pageIndex=0` → **400** `PAGINATION_INVALID`.

## C. Bản nháp: tạo, sửa, loại

6. `POST /api/v1/moderation/authoring-guides/draft` (không body) → **201**; `data.draft.catalogVersion` = `parent-vi-2`, `status` = `Draft`, `itemCount` = 30. Lưu `data.revisionToken` thành R1. Gọi lại → **409** `GUIDE_DRAFT_EXISTS`.
7. Bên Parent (PARENT_TOKEN): `GET /api/v1/authoring-guides?locale=vi&purpose=free&pageIndex=1&pageSize=100` → `catalogVersion` = `parent-vi-1`, `totalCount` = 21 (Parent chưa thấy bản nháp).
8. `PUT /api/v1/moderation/authoring-guides/draft/items/opening-9` (MOD_TOKEN):

```json
{
  "revisionToken": "R1",
  "purposes": ["free"],
  "supportGroup": "opening",
  "itemType": "template",
  "title": "Mẫu mở đầu buổi sáng",
  "instruction": "Điền chi tiết, xem trước rồi chủ động chèn vào bản thảo.",
  "example": null,
  "templateText": "Sáng nay, {{person}} thử {{activity}}.",
  "placeholderDefinitions": ["person", "activity"],
  "order": 31
}
```
→ **200**, `itemCount` = 31, `revisionToken` mới = R2 (khác R1).
9. Gửi lại đúng request ở bước 8 nhưng vẫn dùng R1 → **409** `GUIDE_REVISION_CONFLICT`. Gửi thiếu `revisionToken` → **400** `GUIDE_REVISION_TOKEN_REQUIRED`.
10. Dữ liệu sai: `PUT .../draft/items/bad-1` với R2, `templateText` = `"Xin chào {{person}}"` nhưng `placeholderDefinitions` = `["place"]` → **400** `GUIDE_PLACEHOLDERS_MISMATCH`; sau đó `GET /api/v1/moderation/authoring-guides` cho thấy `revisionToken` vẫn là R2 (lỗi không làm đổi token).
11. `DELETE /api/v1/moderation/authoring-guides/draft/items/khong-ton-tai?revisionToken=R2` → **404** `GUIDE_ITEM_NOT_FOUND`.

## D. Xem trước

12. `POST /api/v1/moderation/authoring-guides/draft/preview` với `{ "revisionToken": "R2", "purpose": "free", "pageIndex": 1, "pageSize": 100 }` → 200, `data.page.totalCount` = 22, `data.page.catalogVersion` = `parent-vi-2`, `data.revisionToken` vẫn là R2.
13. Cùng API với `{ "revisionToken": "R2", "guideId": "opening-9", "values": { "person": "An", "activity": "đạp xe" } }` → `data.render.text` = `Sáng nay, An thử đạp xe.`. Thử `values` = `{ "person": "<b>An</b>", "activity": "{{person}}" }` → text giữ nguyên `<b>An</b>` và `{{person}}` (văn bản thuần, không diễn giải lại). Thiếu `activity` → **400** `PLACEHOLDER_VALUES_INVALID`.

## E. Phát hành và phía Parent

14. `POST /api/v1/moderation/authoring-guides/draft/publish` với `{ "revisionToken": "R2" }` (ADMIN_TOKEN cũng được) → **200**, `data.published.catalogVersion` = `parent-vi-2`, `status` = `Published`, `itemCount` = 31. Lưu `data.revisionToken` thành R3. Gọi lại với R2 → **409** `GUIDE_REVISION_CONFLICT`.
15. Bên Parent: `GET /api/v1/authoring-guides?locale=vi&purpose=free&pageIndex=1&pageSize=100` → `catalogVersion` = `parent-vi-2`, `totalCount` = 22. `POST /api/v1/authoring-guides/opening-9/render` với `{ "values": { "person": "An", "activity": "đạp xe" } }` (không có `catalogVersion`) → 200, text như bước 13. Cùng API cho `dialogue-1` với `{ "values": { "person": "An", "words": "Chào" }, "catalogVersion": "parent-vi-1" }` → **409** `GUIDE_CATALOG_CHANGED`. Câu hỏi `free-start-1` → `POST .../free-start-1/render` → **400** `GUIDE_HAS_NO_TEMPLATE`.
16. `GET /api/v1/moderation/authoring-guides/versions` → `totalCount` = 2 (`parent-vi-2` rồi `parent-vi-1`); phiên bản `parent-vi-1` vẫn là `Published` và `itemCount` = 30 (lịch sử không đổi).

## F. Hủy bản nháp và khôi phục

17. `POST .../draft` → 201 (`parent-vi-3`, R4). `DELETE /api/v1/moderation/authoring-guides/draft?revisionToken=R4` → **200** (`discardedCatalogVersion` = `parent-vi-3`, token mới R5). `GET /api/v1/moderation/authoring-guides` → `draft` = `null`; `GET .../versions` → `totalCount` vẫn 2. `DELETE .../draft?revisionToken=R5` → **404** `GUIDE_DRAFT_NOT_FOUND`.
18. Khôi phục phiên bản cũ: `POST .../draft` với `{ "sourceVersionId": V1 }` → 201, **`catalogVersion` = `parent-vi-3`** (số của bản đã hủy được dùng lại), `itemCount` = 30. `POST .../draft/publish` với token mới nhất → 200. Bên Parent: `catalogVersion` = `parent-vi-3`, `totalCount` = 21 (không còn `opening-9`).
19. Bộ rỗng: tạo bản nháp, loại hết 30 mục (hoặc dùng bản sao nhỏ) rồi publish → **400** `GUIDE_CATALOG_EMPTY`; hủy bản nháp để dọn.

## Kiểm tra bổ sung

- Audit: truy vấn bảng `audit_logs` (hoặc API xem audit nếu có) cho thấy mỗi thao tác ghi có đúng một dòng: `AuthoringGuideDraftCreated`, `AuthoringGuideItemUpserted`, `AuthoringGuideItemRemoved`, `AuthoringGuideDraftDiscarded`, `AuthoringGuideCatalogPublished`; không dòng nào chứa giá trị mà Parent nhập vào mẫu.
- Không có `StoryVersion` mới, không có yêu cầu AI nào phát sinh trong cả kịch bản.

## Kết quả chạy thật

Ngày cập nhật: 21/10/2026. Chưa chạy API thật trên PostgreSQL; các bước A–F ở trên là kỳ vọng kiểm thử, chưa phải kết quả PASS.

| Mức kiểm chứng | Kết quả |
|---|---|
| Unit test, kho dữ liệu giả và host HTTP giả lập | 2.030/2.030 đạt; gồm 5 test đối chiếu tài liệu và 1 test bổ sung bảo vệ Parent khỏi bản nháp |
| Test mô hình EF không kết nối database | 42/42 đạt |
| Test tự động với PostgreSQL thật | 8 test đã biên dịch; 8 bỏ qua, 0 chạy, do không đặt `STORYPLATFORM_TEST_PG` |
| API thật trên PostgreSQL thật | Chưa chạy; chưa có HTTP thực tế hoặc số liệu audit |

`dotnet build StoryPlatform.slnx --no-restore`: thành công, 0 lỗi. Còn các cảnh báo dependency hiện có (NU1510, NU1903). Migration `20261021163456_AddManagedAuthoringGuides` đã có trong repository; lần triển khai này không tạo, sửa hoặc áp dụng migration, không khởi động API hay kết nối database.

### Cách chạy bộ test PostgreSQL sau khi được phép

Người vận hành chỉ bật `STORYPLATFORM_TEST_PG` sau khi cho phép tạo/xóa database tạm và áp dụng toàn bộ migration trên máy chủ PostgreSQL thử nghiệm đã chọn. Biến này chứa chuỗi kết nối tới database quản trị đang tồn tại, với tài khoản có quyền CREATE/DROP DATABASE; không ghi giá trị vào chat, file hoặc log.

```powershell
dotnet test tests/StoryPlatform.IntegrationTests --filter "FullyQualifiedName~AuthoringGuideManagementPostgresTests"
```

Fixture tạo template `storyplatform_it_tpl_...`, migrate và seed, clone thành `storyplatform_it_...` cho từng test, rồi xóa các database do nó tạo. Bộ test dùng service + EF + UnitOfWork thật; có kiểm tra cạnh tranh, ràng buộc unique, lịch sử, xóa mềm, Parent render, và rollback khi PostgreSQL từ chối ghi audit sau khi đã lưu trạng thái phát hành. Đây vẫn là kiểm thử ở tầng service, không phải API HTTP thật.

Giới hạn còn lại: chưa thực thi 8 test trên PostgreSQL, chưa chạy lại các ca cạnh tranh 3 lần, chưa chạy Swagger/API, chưa đối chiếu audit thực tế và chưa kiểm tra frontend. Khi được phép chạy API, cần ghi môi trường, nhánh/commit, phiên bản PostgreSQL, trạng thái migration, PASS/FAIL cùng HTTP thực tế từng bước và các giới hạn còn lại vào mục này.
