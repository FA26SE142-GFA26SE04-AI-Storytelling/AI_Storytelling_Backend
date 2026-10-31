# Quản lý hướng dẫn sáng tác (Moderator / Administrator) — hướng dẫn API cho frontend

Cập nhật 21/10/2026. Trạng thái bằng chứng: đã có test tự động với kho dữ liệu giả; **chưa ghi nhận chạy trên PostgreSQL thật** (xem mục "Kết quả chạy thật" trong `AUTHORING_GUIDE_MANAGEMENT_SWAGGER_TEST.md`). Cần migration `AddManagedAuthoringGuides` đã được áp dụng trước khi dùng thật.

## 1. Tính năng này làm gì

Bộ hướng dẫn sáng tác (câu hỏi gợi mở và mẫu diễn đạt) mà Parent thấy khi tự viết truyện nay lưu trong database và do Moderator hoặc Administrator quản lý, không cần developer sửa code. Moderator soạn trên **bản nháp chung**, xem trước, rồi **phát hành cả bộ** thành một phiên bản mới. Trong lúc soạn, Parent vẫn thấy bộ đang phát hành. Đây là nội dung biên tập sẵn: không gọi AI, không tạo `StoryVersion`, không đụng đến truyện của Parent.

Quyền: vai trò `Moderator` hoặc `Administrator`, tài khoản đang hoạt động. Parent, ContentCreator, Child và người chưa đăng nhập nhận 403 / 401. Danh tính người thao tác lấy từ phiên đăng nhập, không gửi trong body.

## 2. Đăng nhập

`POST /api/v1/Auth/login` với `identifier` và `password`, nhận `accessToken`. Gửi `Authorization: Bearer <accessToken>` cho mọi API bên dưới.

## 3. Khái niệm

- **Bộ hướng dẫn (catalog):** một bộ cho Parent + tiếng Việt. Có **phiên bản đang phát hành** (Parent đọc) và tối đa **một bản nháp**.
- **Phiên bản:** có `versionId`, `versionNo`, `catalogVersion` (ví dụ `parent-vi-2`), `status` (`Draft` hoặc `Published`). Phiên bản đã phát hành không bao giờ được sửa hay xóa; muốn ẩn một hướng dẫn thì loại nó khỏi bản nháp rồi phát hành.
- **`revisionToken`:** mã thay đổi sau mỗi thao tác ghi. Lấy từ `GET /` và từ response của thao tác ghi gần nhất; gửi lại ở thao tác kế tiếp. Hai người dùng cùng một token thì chỉ một người thành công.
- Tạo nháp không nhận token: server tự kiểm tra nháp đang tồn tại và xử lý cạnh tranh. Sửa, loại mục, hủy, xem trước và phát hành đều cần `revisionToken`; xem trước không đổi token.
- **`catalogVersion` phía Parent:** Parent gửi kèm khi điền mẫu để biết bộ có bị đổi giữa chừng không.

## 4. Danh sách API

Tiền tố: `/api/v1/moderation/authoring-guides`. Phản hồi bọc trong `ApiResponse` (`success`, `message`, `data`, `errors`).

| API | Mục đích | Request | Thành công |
|---|---|---|---|
| `GET /api/v1/moderation/authoring-guides` | Xem bộ đang phát hành, bản nháp và `revisionToken` | — | 200 |
| `GET /api/v1/moderation/authoring-guides/versions` | Lịch sử phiên bản, mới nhất trước | query `pageIndex` (≥ 1, mặc định 1), `pageSize` (1–100, mặc định 20) | 200 |
| `GET /api/v1/moderation/authoring-guides/versions/{versionId}` | Nội dung một phiên bản | path `versionId` | 200 |
| `POST /api/v1/moderation/authoring-guides/draft` | Tạo bản nháp | body tùy chọn `{ "sourceVersionId": 1 }`; bỏ trống = sao chép bộ đang phát hành | 201 |
| `PUT /api/v1/moderation/authoring-guides/draft/items/{guideId}` | Thêm hoặc sửa một mục trong nháp | body gồm `revisionToken` và các trường của mục | 200 |
| `DELETE /api/v1/moderation/authoring-guides/draft/items/{guideId}` | Loại một mục khỏi nháp | query `revisionToken` | 200 |
| `DELETE /api/v1/moderation/authoring-guides/draft` | Hủy bản nháp (xóa mềm) | query `revisionToken` | 200 |
| `POST /api/v1/moderation/authoring-guides/draft/preview` | Xem trước nháp, hoặc điền thử mẫu | body gồm `revisionToken` | 200 |
| `POST /api/v1/moderation/authoring-guides/draft/publish` | Phát hành cả bộ | body `{ "revisionToken": "..." }` | 200 |

### Phản hồi mẫu

`GET /api/v1/moderation/authoring-guides` (rút gọn `items`):

```json
{
  "success": true,
  "message": "Thao tác thành công.",
  "data": {
    "locale": "vi",
    "audienceRole": "Parent",
    "revisionToken": "3f1c9a52b0d94c7e8a6b5d2c1e0f4a77",
    "published": {
      "version": {
        "versionId": 1, "versionNo": 1, "catalogVersion": "parent-vi-1", "status": "Published", "itemCount": 30,
        "createdByUserId": null, "createdAt": "2026-10-21T09:00:00Z", "updatedByUserId": null, "updatedAt": null,
        "publishedByUserId": null, "publishedAt": "2026-10-21T09:00:00Z"
      },
      "schemaVersion": 2,
      "items": [
        {
          "id": "opening-1", "audienceRole": "Parent",
          "purposes": ["family_memory", "understanding", "message", "free"], "supportGroup": "opening",
          "itemType": "template", "title": "Mẫu diễn đạt 1",
          "instruction": "Điền chi tiết, xem trước rồi chủ động chèn vào bản thảo. Có thể sửa mọi câu chữ.",
          "example": null, "templateText": "Hôm ấy, {{person}} cùng {{companion}} thử {{activity}}.",
          "placeholderDefinitions": [{ "key": "person", "label": "Ai?", "hint": "ví dụ: bố, bé Na" }, { "key": "companion", "label": "Cùng với ai?", "hint": "ví dụ: mẹ, ông nội" }, { "key": "activity", "label": "Làm việc gì?", "hint": "ví dụ: làm bánh, đạp xe" }], "order": 13
        }
      ]
    },
    "draft": null
  },
  "errors": null
}
```

`PUT /api/v1/moderation/authoring-guides/draft/items/opening-9` — `id` lấy từ URL, `audienceRole` luôn là `Parent` do server đặt:

```json
{
  "revisionToken": "3f1c9a52b0d94c7e8a6b5d2c1e0f4a77",
  "purposes": ["free"],
  "supportGroup": "opening",
  "itemType": "template",
  "title": "Mẫu mở đầu buổi sáng",
  "instruction": "Điền chi tiết, xem trước rồi chủ động chèn vào bản thảo.",
  "example": null,
  "templateText": "Sáng nay, {{person}} thử {{activity}}.",
  "placeholderDefinitions": [{ "key": "person", "label": "Ai?", "hint": "ví dụ: An" }, { "key": "activity", "label": "Làm gì?", "hint": null }],
  "order": 31
}
```

Mỗi phần tử `placeholderDefinitions` là một chỗ trống: `key` trùng với `{{key}}` trong `templateText` (chỉ chữ thường và `_`, không trùng nhau), `label` là nhãn ô nhập (bắt buộc khi lưu, tối đa 100 ký tự), `hint` là gợi ý mờ trong ô (không bắt buộc, tối đa 200 ký tự). Dữ liệu cũ chuyển từ phiên bản trước có thể có `label` = `null`; frontend nên hiển thị `key` khi không có `label`.

Mọi thao tác ghi (tạo nháp, sửa, loại mục) trả cùng dạng `data` với `revisionToken` mới và tóm tắt bản nháp:

```json
{ "revisionToken": "9b7e...", "draft": { "versionId": 2, "versionNo": 2, "catalogVersion": "parent-vi-2", "status": "Draft", "itemCount": 31, "createdByUserId": 11, "createdAt": "2026-10-21T10:00:00Z", "updatedByUserId": 11, "updatedAt": "2026-10-21T10:05:00Z", "publishedByUserId": null, "publishedAt": null } }
```

`POST .../draft/preview` có hai chế độ. Liệt kê như Parent sẽ thấy (có thể kèm `purpose`, `group`, `pageIndex` mặc định 1, `pageSize` mặc định 10):

```json
{ "revisionToken": "9b7e...", "purpose": "free", "pageIndex": 1, "pageSize": 100 }
```
→ `data.page` có `catalogVersion`, `locale`, `pageIndex`, `pageSize`, `totalCount`, `items`; `data.render` là `null`. Điền thử một mẫu:

```json
{ "revisionToken": "9b7e...", "guideId": "opening-9", "values": { "person": "An", "activity": "đạp xe" } }
```
→ `data.render` = `{ "catalogVersion": "parent-vi-2", "guideId": "opening-9", "text": "Sáng nay, An thử đạp xe.", "isPlainText": true }`; `data.page` là `null`. Xem trước **không đổi** `revisionToken`.

`POST .../draft/publish` → `data` gồm `revisionToken` mới và `published` (tóm tắt phiên bản vừa phát hành, `status` = `Published`).

`DELETE .../draft?revisionToken=...` → `data` = `{ "revisionToken": "...", "discardedVersionId": 2, "discardedCatalogVersion": "parent-vi-2" }`. Hủy bản nháp là xóa mềm; bản nháp biến mất khỏi mọi danh sách, số phiên bản và mã của nó được dùng lại cho bản nháp kế tiếp.

## 5. Luồng chuẩn

1. `GET /api/v1/moderation/authoring-guides` → lưu `revisionToken` (R0).
2. `POST /api/v1/moderation/authoring-guides/draft` → R1 (lưu `data.revisionToken`). Đã có nháp → 409 `GUIDE_DRAFT_EXISTS`: gọi lại `GET /` để mở nháp hiện có và lấy token hiện hành.
3. Lặp: `PUT .../draft/items/{guideId}` hoặc `DELETE .../draft/items/{guideId}?revisionToken=...`; sau mỗi lần dùng token mới nhận được.
4. `POST .../draft/preview` để kiểm tra danh sách và thử điền mẫu.
5. `POST .../draft/publish` → Parent nhận trọn bộ mới ngay ở lần tải kế tiếp.
- **Khôi phục:** `POST .../draft` với `sourceVersionId` của phiên bản cũ (lấy từ `GET .../versions`), chỉnh nếu cần, rồi publish → thành phiên bản **mới** (`parent-vi-<số mới>`), không ghi đè lịch sử.
- **Bỏ nháp:** `DELETE .../draft?revisionToken=...`.
- **Xem lịch sử:** `GET .../versions` rồi `GET .../versions/{versionId}`; dùng `versionId`, không dùng `versionNo`.

## 6. Xử lý lỗi

| HTTP | Mã (`message`) | Ý nghĩa và cách xử lý |
|---|---|---|
| 400 | `GUIDE_REVISION_TOKEN_REQUIRED` | Thiếu `revisionToken`. |
| 409 | `GUIDE_REVISION_CONFLICT` | Token không còn đúng (người khác vừa sửa/phát hành, hoặc bạn dùng token cũ). **Tải lại** `GET /`, hiển thị bản nháp mới nhất, cho người dùng thử lại với token mới. Không tự gửi lại nguyên request cũ. |
| 409 | `GUIDE_DRAFT_EXISTS` | Đã có bản nháp. Mở bản nháp hiện có thay vì tạo mới. |
| 404 | `GUIDE_DRAFT_NOT_FOUND` | Không có bản nháp để sửa / xem trước / phát hành / hủy. Tạo bản nháp trước. |
| 404 | `GUIDE_ITEM_NOT_FOUND` | Không có mục `guideId` trong nháp. |
| 400 | `GUIDE_SOURCE_VERSION_INVALID` | `sourceVersionId` không phải phiên bản đã phát hành của bộ này. |
| 404 | Thông điệp không tìm thấy phiên bản | `versionId` hoặc `sourceVersionId` không tồn tại, thuộc bộ khác hoặc đã bị xóa mềm. `GUIDE_SOURCE_VERSION_INVALID` áp dụng khi tìm thấy phiên bản nguồn nhưng chưa phát hành. |
| 400 | `GUIDE_CATALOG_EMPTY` | Không phát hành bộ rỗng. |
| 409 | `GUIDE_CATALOG_UNAVAILABLE` | Chưa có bộ đang phát hành (dữ liệu khởi tạo chưa nạp). |
| 400 | `PAGINATION_INVALID` | `pageIndex` < 1 hoặc `pageSize` ngoài 1–100. |
| 400 | `WRITING_PURPOSE_INVALID` | `purpose` không thuộc `free`, `family_memory`, `understanding`, `message`. |
| 400 | `GUIDE_ID_DUPLICATED`, `GUIDE_ID_INVALID`, `GUIDE_FIELD_REQUIRED` | `id` trùng / sai định dạng (chỉ chữ, số, `_`, `-`, tối đa 100 ký tự) / thiếu `title` hoặc `instruction`. |
| 400 | `GUIDE_AUDIENCE_INVALID`, `GUIDE_PURPOSE_INVALID`, `GUIDE_GROUP_INVALID`, `GUIDE_ITEM_TYPE_INVALID` | Sai đối tượng, mục đích (4 giá trị), nhóm (`starting_point`, `opening`, `events`, `emotion`, `dialogue`, `transition`, `ending`) hoặc `itemType` (`question` hoặc `template`). |
| 400 | `GUIDE_QUESTION_HAS_TEMPLATE`, `GUIDE_TEMPLATE_REQUIRED`, `GUIDE_PLACEHOLDERS_MISMATCH` | Câu hỏi không được có mẫu/biến; mẫu phải có `templateText`; `placeholderDefinitions[].key` phải khớp chính xác và không trùng các biến `{{x}}` trong mẫu. |
| 400 | `GUIDE_PLACEHOLDER_LABEL_INVALID` | Khi lưu mục mẫu: một chỗ trống thiếu `label`, `label` dài hơn 100 ký tự hoặc `hint` dài hơn 200 ký tự. |
| 400 | `PLACEHOLDER_VALUES_INVALID`, `GUIDE_HAS_NO_TEMPLATE` | Khi điền mẫu: thiếu/thừa biến, giá trị rỗng hoặc dài hơn 500 ký tự; hoặc mục là câu hỏi nên không điền được. |
| 401 / 403 | — | Chưa đăng nhập / không phải Moderator hoặc Administrator đang hoạt động. |

Một xung đột phát hiện ở mức database cũng trả 409 `GUIDE_REVISION_CONFLICT`. Lỗi 500 là sự cố hệ thống, không phải luồng nghiệp vụ.

## 7. Phía Parent (không đổi đường dẫn)

- `GET /api/v1/authoring-guides?locale=vi&purpose=family_memory&group=opening&pageIndex=1&pageSize=10` — chỉ trả **phiên bản đang phát hành**; có `catalogVersion` trong `data`. Câu hỏi (`itemType = question`) hiển thị bằng `title`; mẫu (`template`) mới điền được.
- `POST /api/v1/authoring-guides/{guideId}/render` với `{ "values": { ... }, "catalogVersion": "parent-vi-1" }`. `catalogVersion` **không bắt buộc**: không gửi thì render theo bộ hiện hành (client cũ vẫn chạy). Gửi mà bộ đã đổi → 409 `GUIDE_CATALOG_CHANGED`: tải lại danh sách hướng dẫn rồi cho Parent chọn lại. Mục đã bị loại khỏi bộ hiện hành → 404.
- Kết quả render là **văn bản thuần**: hiển thị bằng text, không dùng `innerHTML`. Parent tự quyết định chèn vào trình soạn; hệ thống không lưu, không tạo `StoryVersion`, không sửa các câu đã chèn trước đó.
- Đổi hướng dẫn **không** sửa các câu Parent đã chèn vào truyện.

## 8. Lưu ý khi làm giao diện

- Luôn lưu và dùng `revisionToken` mới nhất; coi mọi 409 là tín hiệu phải tải lại.
- Không cache danh sách hướng dẫn phía Parent qua lần phát hành; kiểm tra `catalogVersion`.
- Giá trị Parent nhập vào mẫu tối đa 500 ký tự mỗi biến; hệ thống chỉ thay một lượt, không diễn giải lại giá trị.
- Moderator không có thêm quyền xem hồ sơ trẻ hay truyện gia đình qua tính năng này.
