# Phân quyền theo Permission — Thiết kế

Ngày: 2026-11-22. Nguồn: brainstorming với user.

> **Trạng thái (cập nhật 2026-11-22): Giai đoạn 1–2 đã triển khai** trên branch `feat/permission-authorization` (backend và frontend), chưa merge, chưa deploy. Plan: `docs/superpowers/plans/2026-11-22-phan-quyen-permission.md`. Mục 2 mô tả hiện trạng *trước* khi triển khai. Điểm khác so với bản thiết kế ban đầu:
>
> - **Danh mục (mục 5.3)** theo đúng tập role trên `main` lúc triển khai: `ContentCategory` chỉ Moderator nên `category.manage` thuộc nhóm Kiểm duyệt; `moderation.queue.process` mặc định cho Moderator và Administrator, thêm `moderation.statistics.view` (Moderator); `PublishedContentReview` tách `moderation.published.view` / `moderation.published.review`; đọc lộ trình mẫu là `template.roadmap.view`; báo cáo nhận xét là `story.rating.report`. Danh mục chính thức nằm ở `Features/AccessControl/PermissionCatalog.cs`; test ma trận quyền khóa hành vi.
> - **Seed (mục 5.5)** bằng code (`PermissionCatalogSeeder`, chạy lúc khởi động như `AuthoringGuideCatalogSeeder`), không dùng file SQL. Ba bảng không có global query filter; nơi đọc tự lọc `IsDeleted`.
> - **Cache (mục 5.7)** dùng `IAppCache` (Redis dùng chung), TTL 60 giây, xóa khóa khi lưu nên mọi instance thấy ngay. Không có Redis thì đọc DB.
> - **PUT (mục 5.6)** chỉ quản lý quyền nhóm Kiểm duyệt/Quản trị; quyền khác của role nhân viên (ví dụ `community.view`) được giữ nguyên.
> - **Màn hình**: trang "Phân quyền" trong khu Administrator của frontend.
> - **Chỉ thu/trả lại (PR #186, 22/11/2026)**: vì khoảng 53 chỗ trong service vẫn kiểm role, PUT chỉ nhận quyền mặc định của role cộng quyền role đang giữ; GET trả `AssignableCodes`.
> - **Mọi role người lớn (23/11/2026)**: bỏ A4; PUT thay toàn bộ tập quyền của role (mọi nhóm); `story.review.approve` thêm vào danh sách bước duyệt phải còn người giữ.

## 1. Mục đích

Hệ thống hiện phân quyền theo **role cố định**: mỗi tài khoản có một role và quyền được viết cứng trên từng controller. Tài liệu này phân tích hạn chế của cách đó và đề xuất thêm một **lớp permission** (quyền theo hành động) gắn với role. Lớp này giữ nguyên hành vi hiện tại và cho phép Admin chỉnh quyền của nhân viên (Moderator, Administrator) mà không cần sửa code.

Đối tượng đọc: hội đồng và người đọc báo cáo SEP490, và nhóm phát triển nếu sau này triển khai.

## 2. Hiện trạng

### 2.1 Ba lớp kiểm tra đang có

| Lớp | Câu hỏi nó trả lời | Lưu ở đâu | Kiểm ở đâu |
|---|---|---|---|
| Role | Được gọi API này không? | `user_accounts.Role` (enum `UserRole`: `User`, `ContentCreator`, `Moderator`, `Administrator`) | `[Authorize(Roles = "...")]` trên controller |
| Quyền sở hữu | Dữ liệu này có phải của mình không? | Quan hệ dữ liệu (`child_profiles.OwnerUserId`, chủ truyện…) | Trong service |
| Gói | Dùng được bao nhiêu? | `subscription_plans`, `user_subscriptions`, `plan_settings`, `credit_lots`, `credit_usages` | `PlanBenefitService`, `PlanUsageService`, `TokenQuotaService` |

Các cơ chế bổ sung:

- **Role tạm `CreatorApplicant`**: Creator ở trạng thái `AwaitingApproval` nhận claim role `CreatorApplicant` (`CreatorApplicantAccess.RoleClaimFor`).
- **Thu hồi token**: `OnTokenValidated` (`ServiceExtensions.cs`) đọc tài khoản từ DB ở mỗi request. Token bị từ chối nếu tài khoản đã bị xóa, `TokenVersion` khác, trạng thái là `Suspended`/`Registered`, hoặc role trong token khác role hiện tại.
- **Phiên của trẻ**: token có `token_type = child`. `DefaultPolicy` chặn token này khỏi mọi `[Authorize]` của người lớn. Endpoint của trẻ dùng `ChildSessionPolicy`.
- **`[AllowAnonymous]`**: đăng nhập/đăng ký, đăng nhập của trẻ bằng PIN/QR, Parental Gate, callback thanh toán.

### 2.2 Số liệu (đếm trên code ngày 2026-11-22)

- 138 chỗ `[Authorize(Roles = ...)]` trên 56 controller.
- Khoảng 90 chỗ so sánh `UserRole.X` trong tầng Application (ví dụ `StaffAccountService`, `CreatorPayoutService`, `ContentReportReviewService`, `CreatorApplicationService`).
- 19 file frontend tự so tên role (ví dụ `AdminPeople.tsx`, `ConnectedModerationQueue.tsx`).
- Không có bảng `roles`, `permissions` hay `role_permissions`.
- Bảng `token_quota_configs` còn entity và cấu hình EF, nhưng không service nào đọc hay ghi.

### 2.3 Vấn đề

| # | Vấn đề | Hệ quả |
|---|---|---|
| P1 | Quyền viết cứng trong attribute | Đổi quyền phải sửa code và deploy lại |
| P2 | Kiểm role rải rác trong service | Khó trả lời "role X làm được gì", dễ sót khi sửa |
| P3 | Frontend tự so tên role | Frontend và backend có thể lệch nhau |
| P4 | Không tách được quyền nhân viên | Mọi Moderator xử lý được mọi hàng đợi; Admin làm được mọi việc, kể cả duyệt chi trả, nên không tách được trách nhiệm |
| P5 | Một tài khoản chỉ có một role | Không cấp thêm được một quyền lẻ |

Với người dùng cuối (Phụ huynh, Trẻ, Creator), role cố định vẫn đủ dùng. Nhu cầu chỉnh quyền thật sự nằm ở **khu vực nhân viên**.

## 3. Mục tiêu, không phải mục tiêu, điều cấm

**Mục tiêu**

- G1. Mỗi endpoint khai báo **permission** mà nó cần, thay cho danh sách role.
- G2. Bản gán role→permission ban đầu cho **đúng y hệt** quyền hiện tại (không đổi hành vi).
- G3. Admin xem và sửa được quyền của role nhân viên (`Moderator`, `Administrator`) mà không cần deploy. Mọi thay đổi đều được ghi audit.
- G4. Có một nguồn sự thật duy nhất cho câu hỏi "role X làm được gì".

**Không phải mục tiêu (ở giai đoạn 1–2)**

- Không thay các chỗ so `UserRole.X` trong service (giai đoạn 3).
- Không đổi frontend (giai đoạn 3).
- Không cấp quyền riêng cho từng người dùng (giai đoạn 4).
- Không cho một tài khoản nhiều role.
- Không đưa gói/hạn mức vào permission. Gói vẫn là lớp riêng.

**Điều cấm (làm thì coi như thất bại, dù chạy được)**

- A1. Không được làm mất bước duyệt của con người trước khi nội dung đến tay trẻ. Sửa quyền chỉ đổi **ai** được duyệt, không bỏ được bước duyệt.
- A2. Token của trẻ không bao giờ có permission của người lớn. `ChildSessionPolicy` và `DefaultPolicy` giữ nguyên.
- A3. Không có trạng thái nào mà không còn ai sửa được quyền (tự khóa mình).
- A4. ~~Không mở quyền của người dùng cuối qua màn hình Admin.~~ **Bỏ ngày 23/11/2026** theo yêu cầu của user: Admin chỉnh được quyền của mọi role người lớn (`User`, `ContentCreator`, `CreatorApplicant`, `Moderator`, `Administrator`), nhưng chỉ thu hoặc trả lại quyền mặc định của role; `story.review.approve` (phụ huynh duyệt truyện) nằm trong luật "luôn còn ít nhất một role duyệt". Phiên của trẻ vẫn không dùng permission.

## 4. Các phương án đã cân nhắc

| | A. Gom quyền trong code | **B. RBAC lưu trong DB (chọn)** | C. ABAC / policy engine (OPA…) |
|---|---|---|---|
| Cách làm | Hằng số permission, bản gán role→permission viết trong code | Danh mục permission ở code, bản gán role→permission ở DB, Admin sửa được | Luật dựa trên thuộc tính người dùng/tài nguyên/ngữ cảnh |
| Đổi quyền không deploy | Không | Có | Có |
| Tách quyền nhân viên | Một phần | Có | Có |
| Công sức | Thấp | Trung bình | Cao |
| Ghi chú | Giải quyết P2 về mặt khai báo nhưng không giải quyết P1, P4 | Giải quyết P1, P4 và một phần P2 | Quá mức với 5 role; thêm hạ tầng; khó trình bày |

**Chọn B.** Đây là cách phổ biến trong ASP.NET Core (policy-based authorization). Nó không đổi `UserRole` và JWT, nên dễ chuyển dần. Nó cũng giải quyết đúng chỗ cần là quyền nhân viên. Phương án A được dùng làm **giai đoạn 1** của B.

**Hình thức trong báo cáo:** user chọn trình bày B dưới dạng **thiết kế, chưa triển khai**, vì không muốn đụng vào hệ thống đang chạy ổn định ở giai đoạn nộp báo cáo.

## 5. Thiết kế

### 5.1 Nguyên tắc

1. **Ba lớp tách biệt**: permission (được làm hành động này không), quyền sở hữu (có phải dữ liệu của mình không), gói (còn hạn mức không). Một request phải qua cả ba lớp.
2. **Danh mục permission định nghĩa trong code, bản gán lưu trong DB.** Admin không tạo được permission mới, vì permission chỉ có nghĩa khi code có kiểm tra nó.
3. **Giữ enum `UserRole`** và cột `user_accounts.Role`. Bảng `roles` dùng `Code` trùng tên enum. Không cần migrate dữ liệu người dùng.
4. **Permission không nằm trong JWT.** Permission được tra theo role ở mỗi request, có cache. JWT, `TokenVersion` và `CreatorApplicant` giữ nguyên.
5. **Độ chi tiết của permission**: mỗi permission ứng với một cụm endpoint có **cùng tập role hiện tại**. Nhờ vậy bản gán ban đầu khớp đúng quyền hiện có (G2). Ví dụ `TokenQuotaController` có 3 tập role khác nhau nên tách thành 3 permission.

### 5.2 Luồng kiểm tra một request

```text
Request ─► JWT Bearer (chữ ký, hạn) ─► OnTokenValidated (DB: TokenVersion, trạng thái, role)  ── lỗi ─► 401
        ─► token_type = child?
             ├─ có  ─► ChildSessionPolicy (giữ nguyên) ──────────────────────────────┐
             └─ không ─► DefaultPolicy (đã xác thực, không phải token trẻ)          │
                         ─► [HasPermission("x")] : role có permission "x"? ── không ─► 403
                         ─► Service: kiểm quyền sở hữu ── không ─► 403/404 ◄─────────┘
                         ─► Service: kiểm gói/hạn mức (PlanBenefitService) ── hết ─► 403/409/429
                         ─► Xử lý
```

### 5.3 Danh mục permission (giai đoạn 1)

Quy ước mã: `<khu vực>.<đối tượng>.<hành động>`, chữ thường. Cột "Role hiện tại" là bản gán mặc định. Nó được lấy từ `[Authorize(Roles)]` hiện có nên không đổi hành vi.

**Phụ huynh**

| Permission | Controller (endpoint) | Role hiện tại |
|---|---|---|
| `account.self.manage` | `AuthController` (đổi email, xem trước/xóa tài khoản) | User |
| `child.profile.manage` | `ChildProfile`, `LearningProfile`, `ChildRoadmap`, `RoadmapLibrary`, `ChildAccessCredential` (phần phụ huynh) | User |
| `parent.story.author` | `AIStoryInput`, `ParentAuthoring`, `ParentStoryPackages`, `ParentPackageAssets`, `ExistingStories`, `ParentAssistance`, `AuthoringGuides`, `SafetyPolicy`, `Story` (tạo/sửa/xóa/publish) | User |
| `parent.feedback.manage` | `ParentReadingFeedback`, `ParentStorySummary`, `ParentWishes`, `ParentWishSuggestions` | User |
| `content.report.submit` | `ContentReport` | User |
| `story.rating.write` | `StoryRating` (endpoint chỉ User) | User |
| `quota.parent.view` | `TokenQuota` (endpoint chỉ User) | User |

**Dùng chung**

| Permission | Controller (endpoint) | Role hiện tại |
|---|---|---|
| `media.generate` | `MediaGeneration`, `StoryCover`, `AiGenerationCapacity` | User, ContentCreator |
| `story.rating.view` | `StoryRating` (endpoint User, ContentCreator) | User, ContentCreator |
| `story.review.approve` | `Review` (approve/reject) | User, Administrator |
| `story.package.confirm` | `StoryPackageConfirmation` | User, ContentCreator, Administrator |
| `community.view` | `StoryCommunity` (endpoint 4 role) | User, ContentCreator, Moderator, Administrator |

**Creator**

| Permission | Controller (endpoint) | Role hiện tại |
|---|---|---|
| `creator.application.submit` | `CreatorApplication` (endpoint chỉ applicant) | CreatorApplicant |
| `creator.application.view_own` | `CreatorApplication` (applicant + Creator) | CreatorApplicant, ContentCreator |
| `creator.studio` | `CreatorAuthoring`, `CreatorAIStoryInput`, `CreatorMediaUploads`, `CreatorModerationCheck`, `PublicStorySubmission`, `CreatorStatistics`, `CreatorPublication` (phần Creator), `Review` (confirm-content của Creator) | ContentCreator |
| `creator.rating.view_own` | `StoryRating` (endpoint chỉ Creator) | ContentCreator |
| `creator.community.manage` | `StoryCommunity` (endpoint chỉ Creator) | ContentCreator |
| `creator.payout.view_own` | `CreatorPayout` (phần Creator) | ContentCreator |
| `quota.creator.view` | `TokenQuota` (endpoint chỉ Creator) | ContentCreator |

**Kiểm duyệt (nhân viên, sửa được ở giai đoạn 2)**

| Permission | Controller (endpoint) | Role hiện tại |
|---|---|---|
| `moderation.queue.process` | `ModerationQueue` (endpoint chỉ Moderator) | Moderator |
| `moderation.queue.view` | `ModerationQueue`, `PublishedContentReview` (endpoint Moderator, Administrator) | Moderator, Administrator |
| `moderation.published.review` | `PublishedContentReview` (endpoint chỉ Moderator) | Moderator |
| `moderation.report.review` | `ContentReportReview` | Moderator |
| `moderation.rating.moderate` | `StoryRatingModeration` | Moderator |
| `review.public_story` | `PublicStoryReview` | Moderator, Administrator |
| `review.gray_zone` | `GrayZoneReview` | Moderator, Administrator |
| `review.roadmap` | `RoadmapReview` | Moderator, Administrator |
| `review.creator_publication` | `CreatorPublication` (phần duyệt) | Moderator, Administrator |
| `template.roadmap.author` | `RoadmapTemplate` (endpoint chỉ Moderator) | Moderator |
| `template.roadmap.manage` | `RoadmapTemplate` (endpoint Moderator, Administrator) | Moderator, Administrator |
| `rubric.manage` | `ModerationRubrics` | Moderator, Administrator |
| `guide.manage` | `AuthoringGuideManagement` | Moderator, Administrator |
| `wishcard.manage` | `WishCardManagement`, `WishCardMatchManagement` | Moderator, Administrator |

**Quản trị (nhân viên, sửa được ở giai đoạn 2)**

| Permission | Controller (endpoint) | Role hiện tại |
|---|---|---|
| `staff.account.manage` | `StaffAccount` | Administrator |
| `creator.account.manage` | `CreatorAccount` | Administrator |
| `creator.application.review` | `CreatorApplication` (phần Admin) | Administrator |
| `creator.tier.manage` | `CreatorTier` | Administrator |
| `creator.payout.manage` | `CreatorPayout` (phần Admin) | Administrator |
| `quota.manage` | `TokenQuota` (phần Admin) | Administrator |
| `category.manage` | `ContentCategory` | Administrator |
| `payment.manage` | `Payment` (phần Admin) | Administrator |
| `report.business.view` | `BusinessReport` | Administrator |
| `audit.view` | `AdminAuditLog` | Administrator |
| `permission.manage` | API mới ở mục 5.6 | Administrator |

**Giữ nguyên, không gắn permission**

- `[AllowAnonymous]`: không đổi.
- `[Authorize]` không kèm role (`Notification`, `Outline`, `ContentGeneration`, `CreatorAssistance`, phần chung của `Auth`, `Payment`, `BusinessReport`…): mọi người lớn đã đăng nhập đều dùng được, vẫn để `[Authorize]`.
- Endpoint của trẻ (`ChildReading`, `ChildQuiz`, `ChildNotebook`, `ChildRewards`, `ChildWishes`, `MyRoadmap`, `ChildStorySummary`, `ChildStoryReflection`, `ChildManualStories`, `ChildPackageAssets`, phần phiên trẻ của `ChildAccessCredential`): vẫn dùng `ChildSessionPolicy`.

Bảng trên được lập ở mức controller. Khi triển khai, người làm phải kiểm lại **từng action**. Controller nào có nhiều tập role thì mỗi tập role ứng với một permission, đúng theo nguyên tắc 5.1.5.

### 5.4 Thành phần code (giai đoạn 1)

| Thành phần | Tầng | Vai trò |
|---|---|---|
| `Permissions` (lớp hằng số lồng nhau, ví dụ `Permissions.Review.GrayZone`) | Application | Danh mục mã permission duy nhất |
| `DefaultRolePermissions` | Application | Bản gán mặc định role→permission (bảng 5.3). Ở giai đoạn 1 đây là nguồn chính; ở giai đoạn 2 nó là dữ liệu seed |
| `HasPermissionAttribute : AuthorizeAttribute` | Api | `[HasPermission(code)]` đặt tên policy là `perm:<code>` |
| `PermissionPolicyProvider : IAuthorizationPolicyProvider` | Api | Tạo policy cho tên `perm:*`: `RequireAuthenticatedUser`, không phải token trẻ, cộng `PermissionRequirement(code)`. Tên khác thì chuyển cho provider mặc định (để `ChildSession` và `DefaultPolicy` không đổi) |
| `PermissionAuthorizationHandler` | Api | Đọc claim role, gọi `IPermissionService.RoleHasAsync(role, code)` |
| `IPermissionService` | Application | Giai đoạn 1 đọc `DefaultRolePermissions`; giai đoạn 2 đọc DB qua cache |

Ví dụ:

```csharp
// Trước
[Authorize(Roles = "Moderator,Administrator")]
public class GrayZoneReviewController : BaseApiController { }

// Sau
[HasPermission(Permissions.Review.GrayZone)]
public class GrayZoneReviewController : BaseApiController { }
```

**Kiểm chứng không đổi hành vi (bắt buộc ở giai đoạn 1)**

1. Trước khi sửa, chạy một test duyệt bằng reflection mọi action trong `StoryPlatform.Api`. Với mỗi action, ghi lại tập role hiệu lực (gồm attribute của controller và của action, `AllowAnonymous`, policy) vào một file snapshot.
2. Sau khi sửa, test tính lại tập role hiệu lực từ `[HasPermission]` cộng `DefaultRolePermissions`, rồi so với snapshot. Chỉ cần lệch một action là test fail.
3. Thêm test: mọi mã trong `Permissions` đều được ít nhất một endpoint dùng, và mọi endpoint chỉ dùng mã có trong danh mục.

### 5.5 Dữ liệu (giai đoạn 2)

```text
roles
  Id            int pk
  Code          varchar(50)  unique   -- "User", "ContentCreator", "CreatorApplicant", "Moderator", "Administrator"
  Name          varchar(100)
  IsStaff       boolean               -- true: Admin sửa quyền được (Moderator, Administrator)
  IsSystem      boolean               -- true: không xóa được
  CreatedAt, UpdatedAt, IsDeleted

permissions
  Id            int pk
  Code          varchar(100) unique   -- "review.gray_zone"
  Group         varchar(50)           -- "Kiểm duyệt", "Quản trị", ...
  Description   varchar(300)
  IsStaffGrantable boolean            -- true: được gán cho role nhân viên qua API
  CreatedAt, UpdatedAt, IsDeleted

role_permissions
  RoleId        int fk -> roles.Id
  PermissionId  int fk -> permissions.Id
  CreatedAt
  pk (RoleId, PermissionId)
```

- **Seed** viết idempotent (`ON CONFLICT DO NOTHING`), theo đúng kiểu các file trong `Database/Seed`. `roles` có 5 dòng; `permissions` lấy từ `Permissions`; `role_permissions` lấy từ `DefaultRolePermissions`.
- **Đồng bộ danh mục**: khi ứng dụng khởi động, các mã permission có trong code mà chưa có trong DB sẽ được thêm vào, không tự gán cho role nào. Mã có trong DB mà code không còn dùng thì đánh dấu `IsDeleted`.
- `IsStaffGrantable = true` cho mọi permission thuộc nhóm "Kiểm duyệt" và "Quản trị" ở mục 5.3; các nhóm khác là `false`. Như vậy Admin chỉ chuyển được quyền nhân viên giữa Moderator và Administrator, không gán được quyền của phụ huynh, trẻ hay Creator cho nhân viên.
- `user_accounts.Role` giữ nguyên. Tên enum dùng làm khóa liên kết logic với `roles.Code`, không có FK.
- `CreatorApplicant` không có trong enum `UserRole` nhưng có trong `roles`, vì đó là giá trị claim role thực tế.

### 5.6 API quản lý quyền (giai đoạn 2)

Tất cả đều yêu cầu `[HasPermission(Permissions.Admin.PermissionManage)]`.

| Method | Route | Mô tả |
|---|---|---|
| GET | `/api/v1/admin/permissions` | Danh mục permission, gom theo `Group` |
| GET | `/api/v1/admin/roles` | Danh sách role, kèm `IsStaff` |
| GET | `/api/v1/admin/roles/{code}/permissions` | Mã permission đang gán cho role |
| PUT | `/api/v1/admin/roles/{code}/permissions` | Thay toàn bộ tập permission của role. Body: `{ "permissionCodes": ["review.gray_zone", "..."] }` |

**Luật của PUT**

1. `{code}` phải là role có `IsStaff = true`. Nếu không, trả 403 "Không được sửa quyền của role người dùng." (A4).
2. Mọi mã phải tồn tại và có `IsStaffGrantable = true`. Nếu không, trả 400 kèm danh sách mã sai.
3. Không được gỡ `permission.manage` khỏi `Administrator`. Nếu cố gỡ, trả 409 (A3).
4. Với mỗi permission của một bước duyệt nội dung cho trẻ (`moderation.queue.process`, `moderation.published.review`, `review.public_story`, `review.gray_zone`, `review.roadmap`, `review.creator_publication`), sau khi lưu phải còn ít nhất một role nhân viên giữ permission đó. Nếu không, trả 409 "Phải còn ít nhất một role duyệt bước này." (A1). Quyền duyệt của phụ huynh (`story.review.approve` trên role `User`) không sửa được qua API nên luôn còn.
5. Ghi một dòng `audit_logs` với `Action = "RolePermissionsUpdated"`, `EntityType = "Role"`, `EntityId = roles.Id`, `BeforeState` và `AfterState` là danh sách mã dạng JSON, `ActorUserId` là Admin thực hiện.
6. Xóa cache của role đó.

### 5.7 Cache

- `IPermissionService` cache tập permission theo `roles.Code` bằng `IMemoryCache`.
- PUT xóa cache ở instance đang xử lý. Các instance khác tự làm mới sau **TTL 60 giây** (quyết định của thiết kế: Core API có thể chạy nhiều task ECS, và 60 giây trễ là chấp nhận được với thao tác quản trị hiếm). Nếu cần tức thời thì thêm cột `roles.PermissionsVersion` và so sánh mỗi request; để lại cho người triển khai.
- Không cần tăng `TokenVersion` khi đổi quyền, vì permission không nằm trong token.

## 6. Lộ trình

| Giai đoạn | Nội dung | Hành vi thay đổi? | Giải quyết |
|---|---|---|---|
| 1 | Mục 5.3 và 5.4: danh mục, attribute, policy provider, handler, test snapshot | Không | P2 (khai báo) |
| 2 | Mục 5.5 đến 5.7: 3 bảng, seed, đồng bộ danh mục, API quản lý quyền, audit, cache | Có: Admin sửa được quyền nhân viên | P1, P4 |
| 3 | Thay khoảng 90 chỗ `UserRole.X` trong service bằng `IPermissionService`; thêm `GET /api/v1/auth/me/permissions`; frontend dùng permission thay cho tên role (19 file); màn hình Admin sửa quyền | Không | P2 (trọn vẹn), P3 |
| 4 | `user_permission_grants (UserId, PermissionId, IsGranted, GrantedByAdminId, ExpiresAt)` để cấp hoặc thu quyền từng người; xóa `token_quota_configs` | Có | P5 |

**Giới hạn của giai đoạn 1–2 (phải nói rõ trong báo cáo):** khi Admin gỡ một permission ở giai đoạn 2, endpoint mang `[HasPermission]` sẽ chặn đúng. Nhưng các chỗ service còn kiểm `UserRole.X` vẫn theo role cho đến giai đoạn 3, và frontend vẫn hiện nút theo tên role. Muốn chỉnh quyền có hiệu lực hoàn toàn thì cần xong giai đoạn 3.

## 7. Rủi ro

| Rủi ro | Cách giảm |
|---|---|
| Đổi 138 attribute làm sai quyền | Test snapshot ở mục 5.4 chạy trong CI |
| Admin tự khóa mình | Luật PUT số 3 |
| Gỡ hết người duyệt nội dung cho trẻ | Luật PUT số 4 |
| Cache lệch giữa các instance | TTL 60 giây; nâng cấp bằng `PermissionsVersion` nếu cần |
| Hiểu nhầm gói là quyền | Nguyên tắc 5.1.1: gói vẫn ở `PlanBenefitService`, không thành permission |

## 8. Trình bày trong báo cáo

- Mục hiện trạng: dùng sơ đồ `docs/report5/R5-2_Authorization.drawio` (trang "Authorization Flow" và "Role Matrix").
- Mục hướng phát triển: tóm tắt mục 2.3, 4, 5.2, 5.5 và 6 của tài liệu này. Dùng trang "Proposed Permission Model" trong cùng file `R5-2_Authorization.drawio`: ERD 3 bảng (mục 5.5), luồng kiểm tra request (mục 5.2) và luồng Admin sửa quyền kèm các luật chặn (mục 5.6).
- Ghi rõ "đề xuất, chưa triển khai".

## 9. Để lại cho người triển khai

- Đối chiếu bảng 5.3 với từng action (mục 5.3 chỉ ở mức controller).
- Chọn có dùng `PermissionsVersion` hay không (mục 5.7).
- Đặt tên lớp hằng số và vị trí file theo quy ước của repo.
