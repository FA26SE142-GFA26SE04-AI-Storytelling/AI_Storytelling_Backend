# Phân quyền theo Permission Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thay `[Authorize(Roles)]` bằng `[HasPermission]` mà không đổi quyền hiện có, lưu bản gán role→permission trong DB, và cho Administrator chỉnh quyền của role nhân viên qua API và màn hình "Phân quyền".

**Architecture:** Danh mục permission là hằng số trong Application; một policy provider ở Api dựng policy `perm:<code>`, handler hỏi `IPermissionService` xem role trong token có permission đó không. Giai đoạn 1 dùng bản gán tĩnh trong code; giai đoạn 2 đọc bảng `role_permissions` qua `IAppCache` (Redis, dùng chung giữa các instance), seed bằng code lúc khởi động. Một test ma trận quyền chụp quyền của mọi action trước khi sửa và chặn mọi lệch sau đó.

**Tech Stack:** ASP.NET Core (.NET 10), EF Core + Npgsql, xUnit + SQLite in-memory, Next.js/React + Vitest.

**Spec:** `docs/superpowers/specs/2026-11-22-phan-quyen-permission-design.md` (đọc mục 5.3 cho danh mục permission, 5.6 cho luật của API).

## Global Constraints

- Mã permission: `<khu vực>.<đối tượng>.<hành động>`, chữ thường, khớp regex `^[a-z_]+(\.[a-z_]+){1,2}$`.
- Role code dùng đúng chuỗi claim: `User`, `ContentCreator`, `CreatorApplicant`, `Moderator`, `Administrator`.
- `ChildSessionPolicy`, `DefaultPolicy`, `[AllowAnonymous]` và `[Authorize]` không kèm role giữ nguyên.
- Không đổi JWT, `TokenVersion`, `CreatorApplicantAccess`.
- Chỉ role có `IsStaff = true` (`Moderator`, `Administrator`) sửa được qua API; chỉ permission nhóm "Kiểm duyệt" và "Quản trị" có `IsStaffGrantable = true`.
- Không tự commit (CLAUDE.md). Không in nội dung `appsettings.*.json`/`.env`.

## Review Focus

1. Bảng quyền trống hoặc thiếu dòng của một role (seeder lỗi, DB mới) → hệ thống dùng bản gán mặc định trong code, không chặn toàn bộ request. Test ở Task 6.
2. Admin sửa quyền trên một instance → instance khác thấy ngay vì cache nằm ở Redis và bị xóa khi lưu; không có Redis thì đọc DB mỗi lần. Test ở Task 6.
3. Body PUT có mã trùng, mã viết hoa, danh sách rỗng, role code sai chữ hoa/thường → 400/404 rõ ràng, không lưu một phần. Test ở Task 7.
4. Claim `CreatorApplicant` (không có trong enum `UserRole`) vẫn được tra quyền đúng. Test ở Task 3.
5. Hai PUT cùng lúc cho một role → tập quyền cuối là của một request trọn vẹn, không trộn. Lưu trong một `SaveChangesAsync`; test ở Task 7.

---

### Task 1: Chụp ma trận quyền hiện tại

**Việc này làm gì (nói dễ hiểu):**
Trước khi đụng vào phân quyền, ta ghi lại chính xác ai gọi được API nào ở thời điểm hiện tại. File ghi lại này là "bản gốc". Mọi bước sau phải cho ra đúng bản gốc này, nếu lệch một API thì test báo lỗi. Đây là cách chứng minh việc chuyển sang permission không làm thay đổi quyền của bất kỳ ai.

**Cần thêm / còn thiếu:**
- Đã có: `AddAppAuthorization()` (DefaultPolicy, ChildSessionPolicy), test duyệt mọi action trong `ChildTokenAuthorizationTests`.
- Cần thêm: helper tính ma trận quyền bằng cách chạy authorization thật cho từng loại principal; file snapshot JSON; test so sánh.
- Còn thiếu / ngoài phạm vi: không kiểm `OnTokenValidated` (nó không phụ thuộc endpoint); quyền sở hữu trong service không nằm trong ma trận.

**Files:**
- Create: `tests/StoryPlatform.UnitTests/Authorization/AuthorizationMatrix.cs`
- Create: `tests/StoryPlatform.UnitTests/Authorization/EndpointAuthorizationMatrixTests.cs`
- Create: `tests/StoryPlatform.UnitTests/Authorization/endpoint-authorization.snapshot.json` (sinh ra, copy vào output qua `<None Update=... CopyToOutputDirectory>` hoặc đọc theo đường dẫn tìm `StoryPlatform.sln` như `SeedDataAlignmentTests`)

**Interfaces:**
- Produces: `static Task<SortedDictionary<string, string[]>> AuthorizationMatrix.ComputeAsync(IServiceProvider services)` — khóa `"<Controller>.<Action>#<HTTP verb> <route template>"` (đủ để phân biệt overload), giá trị là danh sách principal được phép, đã sắp xếp, lấy từ tập `anonymous`, `child`, `User`, `ContentCreator`, `CreatorApplicant`, `Moderator`, `Administrator`.
- Produces: `static IServiceProvider AuthorizationMatrix.BuildServices(Action<IServiceCollection> configure)`.

- [ ] **Step 1: Viết `AuthorizationMatrix`**

Duyệt mọi lớp con không trừu tượng của `ControllerBase` trong assembly `StoryPlatform.Api` và mọi action có `HttpMethodAttribute`. Action có `[AllowAnonymous]` (ở controller hoặc action) → `["anonymous", "child", ...mọi role]`. Còn lại: `AuthorizationPolicy.CombineAsync(policyProvider, authorizeData)`, rồi `IAuthorizationService.AuthorizeAsync` lần lượt với principal trẻ (claim `token_type=child`, như `ChildTokenAuthorizationTests.ChildPrincipal`) và principal người lớn có `ClaimTypes.Role` = từng role code + `ClaimTypes.NameIdentifier = "1"`.

- [ ] **Step 2: Viết test `Matrix_matches_snapshot`**

```csharp
[Fact]
public async Task Matrix_matches_snapshot()
{
    var actual = await AuthorizationMatrix.ComputeAsync(AuthorizationMatrix.BuildServices(s => s.AddAppAuthorization()));
    if (Environment.GetEnvironmentVariable("UPDATE_AUTHZ_SNAPSHOT") == "1") { WriteSnapshot(actual); return; }
    var expected = ReadSnapshot();
    Assert.Equal(expected.Keys, actual.Keys);
    foreach (var key in expected.Keys) Assert.True(expected[key].SequenceEqual(actual[key]), $"{key}: expected [{string.Join(",", expected[key])}] got [{string.Join(",", actual[key])}]");
}
```

- [ ] **Step 3: Sinh snapshot từ code hiện tại**

Run (PowerShell): `$env:UPDATE_AUTHZ_SNAPSHOT='1'; dotnet test tests/StoryPlatform.UnitTests --filter EndpointAuthorizationMatrixTests; Remove-Item Env:UPDATE_AUTHZ_SNAPSHOT`
Expected: PASS, file snapshot được tạo, có khoảng 300+ khóa.

- [ ] **Step 4: Chạy lại không có biến môi trường**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter EndpointAuthorizationMatrixTests`
Expected: PASS.

---

### Task 2: Danh mục permission và bản gán mặc định

**Việc này làm gì (nói dễ hiểu):**
Tạo một danh sách duy nhất các quyền của hệ thống (ví dụ "duyệt vùng xám", "duyệt chi trả Creator"), mỗi quyền có tên mã, nhóm và mô tả tiếng Việt. Kèm theo là bảng "role nào có quyền nào" giống hệt hiện tại. Từ đây, câu hỏi "role này làm được gì" chỉ cần xem một file.

**Cần thêm / còn thiếu:**
- Đã có: enum `UserRole`, hằng `CreatorApplicantAccess.ApplicantRole`.
- Cần thêm: lớp hằng `Permissions`, `PermissionCatalog` (mô tả, nhóm, `IsStaffGrantable`), `DefaultRolePermissions`, `RoleCodes`, interface `IPermissionService` và bản tĩnh `StaticPermissionService`.
- Còn thiếu / ngoài phạm vi: bản đọc DB làm ở Task 6.

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/Permissions.cs`
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/PermissionCatalog.cs`
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/DefaultRolePermissions.cs`
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/RoleCodes.cs`
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/Interfaces/IPermissionService.cs`
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/Services/StaticPermissionService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/AccessControl/PermissionCatalogTests.cs`

**Interfaces:**
- Produces: `public static class Permissions` với lớp lồng theo nhóm, mỗi mã là `public const string` (ví dụ `Permissions.Review.GrayZone = "review.gray_zone"`). Danh sách mã = bảng mục 5.3 của spec; thêm mã mới khi Task 4 gặp một action có tập role chưa có mã.
- Produces: `public sealed record PermissionDefinition(string Code, string Group, string Description, bool IsStaffGrantable);` và `PermissionCatalog.All : IReadOnlyList<PermissionDefinition>`. Nhóm: `"Phụ huynh"`, `"Dùng chung"`, `"Creator"`, `"Kiểm duyệt"`, `"Quản trị"`.
- Produces: `RoleCodes.User/ContentCreator/CreatorApplicant/Moderator/Administrator` (string), `RoleCodes.All`, `RoleCodes.Staff = [Moderator, Administrator]`.
- Produces: `DefaultRolePermissions.Map : IReadOnlyDictionary<string, IReadOnlySet<string>>` (role code → mã).
- Produces: `public interface IPermissionService { Task<bool> RoleHasAsync(string roleCode, string permissionCode, CancellationToken ct = default); Task<IReadOnlySet<string>> GetForRoleAsync(string roleCode, CancellationToken ct = default); Task InvalidateAsync(string roleCode, CancellationToken ct = default); }`
- Produces: `StaticPermissionService : IPermissionService` đọc `DefaultRolePermissions.Map`; role lạ → tập rỗng; `InvalidateAsync` không làm gì.

- [ ] **Step 1: Viết test**

```csharp
[Fact] public void Codes_are_unique_and_well_formed()
{ var codes = PermissionCatalog.All.Select(p => p.Code).ToList();
  Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
  Assert.All(codes, c => Assert.Matches(@"^[a-z_]+(\.[a-z_]+){1,2}$", c)); }
[Fact] public void Constants_match_catalog() // reflection trên mọi const string trong Permissions
{ Assert.Equal(PermissionConstants().OrderBy(x => x), PermissionCatalog.All.Select(p => p.Code).OrderBy(x => x)); }
[Fact] public void Staff_grantable_only_in_staff_groups()
{ Assert.All(PermissionCatalog.All, p => Assert.Equal(p.Group is "Kiểm duyệt" or "Quản trị", p.IsStaffGrantable)); }
[Fact] public void Default_map_uses_known_roles_and_codes()
{ Assert.All(DefaultRolePermissions.Map, kv => { Assert.Contains(kv.Key, RoleCodes.All); Assert.All(kv.Value, c => Assert.Contains(c, PermissionCatalog.All.Select(p => p.Code))); }); }
[Fact] public void Administrator_has_permission_manage()
{ Assert.Contains(Permissions.Admin.PermissionManage, DefaultRolePermissions.Map[RoleCodes.Administrator]); }
[Fact] public async Task Static_service_answers_from_default_map()
{ var s = new StaticPermissionService();
  Assert.True(await s.RoleHasAsync("Moderator", Permissions.Review.GrayZone));
  Assert.False(await s.RoleHasAsync("User", Permissions.Review.GrayZone));
  Assert.False(await s.RoleHasAsync("Ghost", Permissions.Review.GrayZone)); }
```

- [ ] **Step 2: Chạy test, thấy fail vì chưa có kiểu** — `dotnet test tests/StoryPlatform.UnitTests --filter PermissionCatalogTests` → build error.
- [ ] **Step 3: Viết các file ở trên**, mã và role mặc định theo bảng 5.3 của spec.
- [ ] **Step 4: Chạy lại** → PASS.

---

### Task 3: Hạ tầng `[HasPermission]`

**Việc này làm gì (nói dễ hiểu):**
Thêm cách khai báo mới trên API: thay vì ghi "role nào được gọi", ta ghi "cần quyền gì". Khi có request, hệ thống đọc role trong token rồi tra bảng quyền. Token của trẻ luôn bị từ chối ở các API này, giống hiện nay.

**Cần thêm / còn thiếu:**
- Đã có: `AddAppAuthorization` với `DefaultPolicy` chặn token trẻ, `ChildSessionPolicy`.
- Cần thêm: `HasPermissionAttribute`, `PermissionRequirement`, `PermissionPolicyProvider`, `PermissionAuthorizationHandler`; đăng ký trong `AddAppAuthorization`.
- Còn thiếu / ngoài phạm vi: chưa controller nào dùng (Task 4).

**Files:**
- Create: `src/Core/StoryPlatform.Api/Authorization/HasPermissionAttribute.cs`
- Create: `src/Core/StoryPlatform.Api/Authorization/PermissionRequirement.cs`
- Create: `src/Core/StoryPlatform.Api/Authorization/PermissionPolicyProvider.cs`
- Create: `src/Core/StoryPlatform.Api/Authorization/PermissionAuthorizationHandler.cs`
- Modify: `src/Core/StoryPlatform.Api/Extensions/ServiceExtensions.cs` (`AddAppAuthorization`, khoảng dòng 222–239)
- Test: `tests/StoryPlatform.UnitTests/Authorization/PermissionAuthorizationTests.cs`

**Interfaces:**
- Consumes: `IPermissionService`, `Permissions`, `ServiceExtensions.ChildTokenType`.
- Produces: `HasPermissionAttribute(string permission) : AuthorizeAttribute` với `Policy = PermissionPolicyProvider.Prefix + permission`, thuộc tính `Permission`.
- Produces: `PermissionPolicyProvider : IAuthorizationPolicyProvider` (ctor nhận `IOptions<AuthorizationOptions>`), `public const string Prefix = "perm:"`. Tên bắt đầu bằng prefix → policy `RequireAuthenticatedUser` + assertion không có `token_type=child` + `PermissionRequirement(code)`. Tên khác, default và fallback policy → chuyển cho `DefaultAuthorizationPolicyProvider`.
- Produces: `PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>` lấy `ClaimTypes.Role`, gọi `RoleHasAsync`; thiếu claim → không succeed.
- `AddAppAuthorization` đăng ký provider và handler (singleton provider, scoped handler) và `TryAddScoped<IPermissionService, StaticPermissionService>()` (Task 6 thay bằng bản DB bên Application DI).

- [ ] **Step 1: Viết test** (dùng `AuthorizationMatrix.BuildServices(s => s.AddAppAuthorization())`)

```csharp
[Theory]
[InlineData("Moderator", "review.gray_zone", true)]
[InlineData("Administrator", "review.gray_zone", true)]
[InlineData("User", "review.gray_zone", false)]
[InlineData("CreatorApplicant", "creator.application.submit", true)]
[InlineData("ContentCreator", "creator.application.submit", false)]
public async Task Role_is_checked_against_permission(string role, string code, bool expected) { ... Assert.Equal(expected, result.Succeeded); }
[Fact] public async Task Child_token_is_rejected_even_without_role() { ... Assert.False(...); }
[Fact] public async Task Unknown_permission_is_denied() { /* "x.y" cho Administrator */ Assert.False(...); }
[Fact] public async Task Child_session_policy_still_resolves() { Assert.NotNull(await provider.GetPolicyAsync(ServiceExtensions.ChildSessionPolicy)); }
```

- [ ] **Step 2: Chạy test** → fail (build).
- [ ] **Step 3: Viết bốn lớp và đăng ký.**
- [ ] **Step 4: Chạy** `dotnet test tests/StoryPlatform.UnitTests --filter "PermissionAuthorizationTests|EndpointAuthorizationMatrixTests|ChildTokenAuthorizationTests"` → PASS.

---

### Task 4: Đổi các controller sang `[HasPermission]`

**Việc này làm gì (nói dễ hiểu):**
Thay 138 chỗ ghi role trên 56 controller bằng quyền tương ứng. Sau bước này, quyền của mọi người vẫn y hệt trước (test ma trận ở Task 1 chứng minh), nhưng giờ đã có thể đổi quyền mà không sửa controller.

**Cần thêm / còn thiếu:**
- Đã có: snapshot (Task 1), danh mục (Task 2), attribute (Task 3).
- Cần thêm: thay attribute; sửa các test đang tự dựng `services.AddAuthorization()` để dùng `AddAppAuthorization()`; sửa test đọc `AuthorizeAttribute.Roles` sang kiểm qua authorization thật.
- Còn thiếu / ngoài phạm vi: khoảng 90 chỗ `UserRole.X` trong service giữ nguyên (giai đoạn 3 của spec).

**Files:**
- Modify: mọi file trong `src/Core/StoryPlatform.Api/Controllers/` có `[Authorize(Roles = ...)]` (56 file; lấy danh sách bằng `grep -l "Authorize(Roles" src/Core/StoryPlatform.Api/Controllers/*.cs`).
- Modify: test dựng authorization tự chế: `Features/Administration/StaffAccountControllerAuthorizationTests.cs`, `Features/Auth/AuthAuthorizationTests.cs`, `Features/BusinessReports/BusinessReportControllerAuthorizationTests.cs`, `Features/ContentCategories/ContentCategoryControllerAuthorizationTests.cs`, `Features/Payments/PaymentControllerAuthorizationTests.cs`, `Features/TokenQuota/TokenQuotaControllerAuthorizationTests.cs`, `ParentManualApiFlowTests.cs`, cùng mọi test khác đọc `.Roles` (`grep -rln "\.Roles" tests --include=*.cs`).
- Test: `tests/StoryPlatform.UnitTests/Authorization/PermissionUsageTests.cs`

**Interfaces:**
- Consumes: `HasPermissionAttribute`, `Permissions`, `DefaultRolePermissions`.

- [ ] **Step 1: Viết `PermissionUsageTests`**

```csharp
[Fact] public void No_controller_uses_role_lists() // không còn AuthorizeAttribute nào có Roles != null trong StoryPlatform.Api
[Fact] public void Every_used_permission_is_in_catalog()
[Fact] public void Every_catalog_permission_is_used_except_permission_manage() // permission.manage được dùng ở Task 7; khi đó bỏ ngoại lệ
```

- [ ] **Step 2: Chạy** → `No_controller_uses_role_lists` fail.
- [ ] **Step 3: Đổi attribute.** Quy tắc: mỗi `[Authorize(Roles = "A,B")]` → `[HasPermission(Permissions.<mã>)]` mà `DefaultRolePermissions` cho đúng tập {A,B}, theo bảng 5.3 của spec. `CreatorApplicantAccess.ApplicantRole` → `creator.application.submit`; `ApplicantRole + ",ContentCreator"` → `creator.application.view_own`. Một controller/action có hai attribute role (AND) → chọn mã có tập role bằng giao của chúng. Gặp tập role chưa có mã → thêm mã vào Task 2 (cùng nhóm, mô tả tiếng Việt) rồi dùng.
- [ ] **Step 4: Sửa test cũ.** Thay `services.AddAuthorization();` bằng `services.AddAppAuthorization();` (thêm `using StoryPlatform.Api.Extensions;`). Test assert `attr.Roles == "X"` → đổi sang gọi `AuthorizationMatrix` hoặc assert `HasPermissionAttribute.Permission` bằng mã tương ứng.
- [ ] **Step 5: Chạy** `dotnet test tests/StoryPlatform.UnitTests` → PASS toàn bộ, đặc biệt `Matrix_matches_snapshot` không đổi snapshot.

---

### Task 5: Bảng `roles`, `permissions`, `role_permissions` và seed bằng code

**Việc này làm gì (nói dễ hiểu):**
Đưa bảng quyền vào database để có thể sửa khi hệ thống đang chạy. Khi ứng dụng khởi động, nó tự thêm các role và quyền còn thiếu, và chỉ ghi bản gán mặc định nếu role đó chưa có quyền nào. Như vậy quyền Admin đã chỉnh sẽ không bị khởi động lại ghi đè.

**Cần thêm / còn thiếu:**
- Đã có: mẫu seed bằng code `AuthoringGuideCatalogSeeder` + `SeedAuthoringGuidesAsync` (`DatabaseSeedExtensions.cs`), danh sách `seededByCode` trong `Seed-Database.ps1` và `SeedDataAlignmentTests`.
- Cần thêm: 3 entity, 3 cấu hình EF, migration, `PermissionCatalogSeeder`, gọi seeder lúc khởi động, thêm 3 bảng vào `seededByCode` (cả ps1 lẫn test).
- Còn thiếu / ngoài phạm vi: **khác spec** — spec mục 5.5 nói seed bằng file SQL; plan chọn seed bằng code vì repo đã có mẫu này và runner yêu cầu số file seed liên tục theo bảng. Cập nhật spec ở Task 9.

**Files:**
- Create: `src/Core/StoryPlatform.Domain/Entities/Role.cs`, `Permission.cs`, `RolePermission.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/RoleConfiguration.cs`, `PermissionConfiguration.cs`, `RolePermissionConfiguration.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/Persistence/ApplicationDbContext.cs` (thêm 3 `DbSet` cạnh dòng 91)
- Create: migration `AddRolePermissions` trong `src/Core/StoryPlatform.Infrastructure/Migrations/` (sinh bằng tool)
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/Services/PermissionCatalogSeeder.cs`
- Modify: `src/Core/StoryPlatform.Application/DependencyInjection.cs` (đăng ký seeder), `src/Core/StoryPlatform.Api/Extensions/DatabaseSeedExtensions.cs` (thêm `SeedPermissionsAsync`), `src/Core/StoryPlatform.Api/Program.cs` (gọi cạnh `SeedAuthoringGuidesAsync`)
- Modify: `Database/Seed-Database.ps1:189-190`, `tests/StoryPlatform.UnitTests/SeedDataAlignmentTests.cs:169-170`
- Test: `tests/StoryPlatform.UnitTests/Features/AccessControl/PermissionCatalogSeederTests.cs`

**Interfaces:**
- Produces: `Role : BaseEntity { string Code; string Name; bool IsStaff; bool IsSystem; ICollection<RolePermission> Permissions }` → bảng `roles`, `Code` varchar(50) unique.
- Produces: `Permission : BaseEntity { string Code; string Group; string Description; bool IsStaffGrantable }` → `permissions`, `Code` varchar(100) unique, `Group` varchar(50), `Description` varchar(300).
- Produces: `RolePermission { int RoleId; int PermissionId; DateTime CreatedAt; Role Role; Permission Permission }` (không kế thừa BaseEntity) → `role_permissions`, khóa `(RoleId, PermissionId)`, FK cascade.
- Produces: `PermissionCatalogSeeder.SeedAsync(CancellationToken) : Task<int>` (số dòng thêm). Luật: thêm role thiếu (`Name` tiếng Việt: Phụ huynh, Nhà sáng tạo, Người đăng ký sáng tạo, Kiểm duyệt viên, Quản trị viên; `IsStaff` cho Moderator/Administrator; `IsSystem = true`); thêm permission thiếu theo `PermissionCatalog`, cập nhật `Group/Description/IsStaffGrantable` của permission có sẵn, đánh dấu `IsDeleted` permission không còn trong catalog; role chưa có dòng `role_permissions` nào → ghi `DefaultRolePermissions`.

- [ ] **Step 1: Viết test seeder** (SQLite in-memory như các test service khác)

```csharp
[Fact] public async Task First_run_creates_roles_permissions_and_defaults() { Assert.Equal(5, roles); Assert.Equal(PermissionCatalog.All.Count, perms); Assert.Contains("review.gray_zone", codesOf("Moderator")); }
[Fact] public async Task Second_run_adds_nothing() { Assert.Equal(0, await seeder.SeedAsync(default)); }
[Fact] public async Task Edited_role_permissions_are_kept() { /* xóa review.gray_zone khỏi Moderator, chạy lại */ Assert.DoesNotContain("review.gray_zone", codesOf("Moderator")); }
[Fact] public async Task Code_removed_from_catalog_is_soft_deleted() { /* chèn permission "old.code" rồi seed */ Assert.True(old.IsDeleted); }
```

- [ ] **Step 2: Chạy** → fail (build).
- [ ] **Step 3: Viết entity, cấu hình, seeder, đăng ký; thêm 3 tên bảng vào `seededByCode` ở ps1 và test.**
- [ ] **Step 4: Sinh migration** — `dotnet ef migrations add AddRolePermissions --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api`. Kiểm file migration chỉ tạo 3 bảng + index, không đụng bảng khác.
- [ ] **Step 5: Chạy** `dotnet test tests/StoryPlatform.UnitTests --filter "PermissionCatalogSeederTests|SeedDataAlignmentTests"` → PASS.

---

### Task 6: `PermissionService` đọc DB qua cache

**Việc này làm gì (nói dễ hiểu):**
Chuyển việc tra quyền từ bảng cố định trong code sang bảng trong database, có cache dùng chung giữa các máy chủ để không chậm. Nếu database chưa có quyền của một role (ví dụ DB mới chưa seed), hệ thống dùng quyền mặc định thay vì chặn hết.

**Cần thêm / còn thiếu:**
- Đã có: `IAppCache` (Redis, có thể null) + `GetOrSetOrLoadAsync`/`RemoveIfConfiguredAsync`, `IUnitOfWork`.
- Cần thêm: `PermissionService : IPermissionService`, đăng ký trong `AddApplication` (thay bản tĩnh ở runtime).
- Còn thiếu / ngoài phạm vi: **khác spec** — spec mục 5.7 nói `IMemoryCache` TTL 60 giây; plan dùng `IAppCache` (Redis dùng chung) với TTL 60 giây nên xóa cache có hiệu lực ngay trên mọi instance. Ghi vào spec ở Task 9.

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/Services/PermissionService.cs`
- Modify: `src/Core/StoryPlatform.Application/DependencyInjection.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/AccessControl/PermissionServiceTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork`, `IAppCache?`, `DefaultRolePermissions`.
- Produces: `PermissionService(IUnitOfWork uow, IAppCache? cache = null)`; khóa cache `$"access:role-permissions:{roleCode}"`, TTL `TimeSpan.FromSeconds(60)`, giá trị `string[]`. Role không có dòng trong `roles` → trả `DefaultRolePermissions` (không cache). Role có dòng nhưng không có quyền → tập rỗng. Role code so khớp phân biệt hoa/thường (claim luôn đúng chữ). Trong một request, kết quả của một role được nhớ trong field của service (scoped) để handler gọi nhiều lần không đọc lại.

- [ ] **Step 1: Viết test**

```csharp
[Fact] public async Task Reads_permissions_from_database() { /* seed bằng PermissionCatalogSeeder, gỡ review.gray_zone khỏi Moderator */ Assert.False(await svc.RoleHasAsync("Moderator", "review.gray_zone")); }
[Fact] public async Task Falls_back_to_defaults_when_role_row_missing() { /* DB trống */ Assert.True(await svc.RoleHasAsync("Moderator", "review.gray_zone")); }
[Fact] public async Task Invalidate_removes_cache_key() { /* fake IAppCache ghi lại khóa đã xóa */ Assert.Contains("access:role-permissions:Moderator", cache.Removed); }
[Fact] public async Task Works_without_cache() { var svc = new PermissionService(uow, null); Assert.True(await svc.RoleHasAsync("Administrator", "permission.manage")); }
```

- [ ] **Step 2: Chạy** → fail.
- [ ] **Step 3: Viết service, đăng ký `services.AddScoped<IPermissionService, PermissionService>()` trong `AddApplication`** (`Program.cs` gọi `AddAppAuthorization` trước `AddApplication`; đăng ký sau cùng thắng khi resolve, nên runtime dùng bản DB, còn test chỉ gọi `AddAppAuthorization` vẫn dùng bản tĩnh).
- [ ] **Step 4: Chạy** `dotnet test tests/StoryPlatform.UnitTests --filter "PermissionServiceTests|EndpointAuthorizationMatrixTests"` → PASS.

---

### Task 7: API quản lý quyền cho Administrator

**Việc này làm gì (nói dễ hiểu):**
Thêm các API để Administrator xem danh mục quyền, xem quyền của một role và lưu tập quyền mới cho Moderator hoặc Administrator. Trước khi lưu, hệ thống kiểm 4 luật an toàn: chỉ sửa role nhân viên, chỉ gán quyền nhân viên, Administrator luôn giữ quyền quản lý quyền, và mỗi bước duyệt nội dung cho trẻ luôn còn người duyệt. Mỗi lần lưu đều ghi nhật ký.

**Cần thêm / còn thiếu:**
- Đã có: `IAuditLogWriter.LogAsync`, `BadRequestException`, `ForbiddenException`, `ConflictException`, `NotFoundException`, `BaseApiController.HandleResult`, `GetCurrentUserId`.
- Cần thêm: DTO, `IRolePermissionAdminService` + bản cài, `AdminPermissionsController` với 4 endpoint.
- Còn thiếu / ngoài phạm vi: cấp quyền theo từng người (giai đoạn 4).

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/DTOs/AccessControlDtos.cs`
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/Interfaces/IRolePermissionAdminService.cs`
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/Services/RolePermissionAdminService.cs`
- Create: `src/Core/StoryPlatform.Api/Controllers/AdminPermissionsController.cs`
- Modify: `src/Core/StoryPlatform.Application/DependencyInjection.cs`; `tests/.../Authorization/PermissionUsageTests.cs` (bỏ ngoại lệ `permission.manage`); snapshot (thêm endpoint mới — sinh lại bằng `UPDATE_AUTHZ_SNAPSHOT=1`, rồi kiểm diff snapshot chỉ thêm 4 khóa `AdminPermissions.*` với giá trị `["Administrator"]`)
- Test: `tests/StoryPlatform.UnitTests/Features/AccessControl/RolePermissionAdminServiceTests.cs`

**Interfaces:**
- Produces DTO: `PermissionDto(string Code, string Group, string Description, bool IsStaffGrantable)`, `RoleDto(string Code, string Name, bool IsStaff)`, `RolePermissionsDto(string RoleCode, IReadOnlyList<string> PermissionCodes)`, `UpdateRolePermissionsRequest { List<string> PermissionCodes }`.
- Produces service: `ListPermissionsAsync`, `ListRolesAsync`, `GetRolePermissionsAsync(string roleCode)`, `UpdateRolePermissionsAsync(int actorUserId, string roleCode, UpdateRolePermissionsRequest request)` → `RolePermissionsDto`.
- Routes (`[HasPermission(Permissions.Admin.PermissionManage)]` ở controller): `GET api/v1/admin/permissions`, `GET api/v1/admin/roles`, `GET api/v1/admin/roles/{code}/permissions`, `PUT api/v1/admin/roles/{code}/permissions`.
- Luật PUT, theo thứ tự (message tiếng Việt chính xác):
  1. role không tồn tại → `NotFoundException("Role", code)`.
  2. `!IsStaff` → `ForbiddenException("Không được sửa quyền của role người dùng.")`.
  3. `PermissionCodes` null → `BadRequestException("Danh sách quyền không hợp lệ.")`; mã trùng được gộp; mã không tồn tại hoặc `!IsStaffGrantable` → `BadRequestException("Quyền không hợp lệ hoặc không được gán cho nhân viên: <mã1>, <mã2>.")`.
  4. role là `Administrator` và thiếu `permission.manage` → `ConflictException("Administrator phải giữ quyền quản lý phân quyền.", "PERMISSION_MANAGE_REQUIRED")`.
  5. với mỗi mã duyệt `moderation.queue.process`, `moderation.published.review`, `review.public_story`, `review.gray_zone`, `review.roadmap`, `review.creator_publication`: sau khi lưu không còn role nhân viên nào giữ → `ConflictException("Phải còn ít nhất một role duyệt bước này: <mã>.", "REVIEWER_REQUIRED")`.
  6. Thay toàn bộ dòng `role_permissions` của role trong một `SaveChangesAsync`; ghi `audit_logs` (`Action = "RolePermissionsUpdated"`, `EntityType = "Role"`, `EntityId = role.Id`, before/after = mảng mã đã sắp xếp); `InvalidateAsync(roleCode)`.
- Lưu ý: quyền của role không phải nhân viên chỉ được giữ nguyên, nên tập quyền nhân viên không bao giờ chứa mã ngoài nhóm Kiểm duyệt/Quản trị.

- [ ] **Step 1: Viết test**

```csharp
[Fact] public async Task Update_replaces_set_writes_audit_and_invalidates()
[Fact] public async Task Non_staff_role_is_forbidden()            // "User" → ForbiddenException
[Fact] public async Task Unknown_role_is_not_found()              // "moderator" (sai hoa/thường) → NotFoundException
[Fact] public async Task Non_grantable_or_unknown_codes_are_rejected_with_list() // ["parent.story.author","x.y"] → message liệt kê cả hai
[Fact] public async Task Duplicate_codes_are_merged()
[Fact] public async Task Administrator_cannot_drop_permission_manage()
[Fact] public async Task Last_reviewer_of_a_step_cannot_be_removed() // gỡ review.gray_zone khỏi cả Moderator và Administrator
[Fact] public async Task Empty_list_for_moderator_is_allowed_when_admin_still_reviews() // rỗng nhưng Administrator giữ mọi mã duyệt → OK
[Fact] public async Task Failed_update_leaves_permissions_unchanged()
```

- [ ] **Step 2: Chạy** → fail.
- [ ] **Step 3: Viết DTO, service, controller; đăng ký service.** Lưu ý: `moderation.queue.process` và `moderation.published.review` mặc định chỉ Moderator có, nên test luật 5 phải dựng tình huống đúng.
- [ ] **Step 4: Sinh lại snapshot, kiểm diff** — `git diff tests/StoryPlatform.UnitTests/Authorization/endpoint-authorization.snapshot.json` chỉ thêm 4 khóa mới.
- [ ] **Step 5: Chạy** `dotnet test tests/StoryPlatform.UnitTests` → PASS.

---

### Task 8: Màn hình "Phân quyền" ở frontend

**Việc này làm gì (nói dễ hiểu):**
Thêm trang "Phân quyền" vào khu Administrator. Admin chọn role, thấy quyền chia theo nhóm, bật/tắt rồi lưu. Lỗi từ backend (ví dụ "Phải còn ít nhất một role duyệt bước này") hiện ngay trên trang. Script kiểm kê API của frontend cũng được dạy đọc `[HasPermission]`.

**Cần thêm / còn thiếu:**
- Đã có: `StaffApp.tsx` (mảng `PAGES.Administrator`), `apiRequest`, các thành phần `Page`, `Notice`, `useAction` trong `features/staff/ui`, script `scripts/controller-inventory.mjs`.
- Cần thêm: kiểu dữ liệu, service, helper thuần (gom nhóm, so thay đổi), trang `AdminPermissions`, mục menu, nhận diện `[HasPermission(...)]` trong inventory.
- Còn thiếu / ngoài phạm vi: ẩn/hiện chức năng theo permission (giai đoạn 3); Moderator không thấy trang này.

**Files (gốc `AI_Storytelling_Frontend/src`):**
- Create: `app/types/accessControl.ts`
- Create: `app/services/accessControlService.ts`
- Create: `app/features/staff/permissionMatrix.ts`
- Create: `app/features/staff/AdminPermissions.tsx`
- Modify: `app/features/staff/StaffApp.tsx` (thêm `{ id: "permissions", label: "Phân quyền", icon: KeyRound, view: AdminPermissions, hint: "Bật/tắt quyền của Moderator và Administrator" }` sau mục `staff`)
- Modify: `scripts/controller-inventory.mjs:29-31`
- Test: `tests/unit/staff/permissionMatrix.test.ts`, `tests/unit/services/controllerInventory.test.ts` (thêm case)

**Interfaces:**
- Consumes: 4 endpoint của Task 7 (đường dẫn `apiRequest` bỏ tiền tố `/api/v1`, như `creatorAdminService`).
- Produces: `accessControlService = { permissions(), roles(), rolePermissions(code), updateRolePermissions(code, permissionCodes: string[]) }`.
- Produces: `groupPermissions(list: PermissionDto[]): { group: string; items: PermissionDto[] }[]` (giữ thứ tự nhóm Phụ huynh, Dùng chung, Creator, Kiểm duyệt, Quản trị; trong nhóm sắp theo code); `changedCodes(saved: string[], draft: string[]): { added: string[]; removed: string[] }`.
- Trang: chọn role bằng `Select` (chỉ role `isStaff`); mỗi nhóm một bảng `st-table` cột "Quyền / Mô tả / Bật"; checkbox bị khóa với quyền `!isStaffGrantable`; nút "Lưu thay đổi" chỉ bật khi có thay đổi; trước khi lưu `confirmDialog` liệt kê số quyền thêm/bớt; thành công → Notice "Đã cập nhật quyền của <tên role>."; lỗi → Notice với message backend.
- Inventory: `[HasPermission(Permissions.X.Y)]` → `authorization` chứa `Permission = Permissions.X.Y`.

- [ ] **Step 1: Viết test**

```ts
it('groups permissions in fixed group order', () => { expect(groupPermissions(list).map(g => g.group)).toEqual(['Kiểm duyệt', 'Quản trị']); });
it('reports added and removed codes', () => { expect(changedCodes(['a','b'], ['b','c'])).toEqual({ added: ['c'], removed: ['a'] }); });
it('reads HasPermission attributes', () => { /* như test hiện có, controller dùng [HasPermission(Permissions.Review.GrayZone)] */ expect(result[0].authorization).toContain('Permissions.Review.GrayZone'); });
```

- [ ] **Step 2: Chạy** `npm test -- tests/unit/staff/permissionMatrix.test.ts tests/unit/services/controllerInventory.test.ts` (trong `src`) → fail.
- [ ] **Step 3: Viết các file và mục menu.**
- [ ] **Step 4: Chạy lại** → PASS; rồi `npx tsc --noEmit` và `npm run lint` → không lỗi mới.

---

### Task 9: Kiểm tra toàn bộ và cập nhật tài liệu

**Việc này làm gì (nói dễ hiểu):**
Chạy toàn bộ build và test của cả hai repo, mở thử màn hình Phân quyền với backend chạy local, và cập nhật spec cho đúng những gì đã làm (đã triển khai giai đoạn 1–2, seed bằng code, cache Redis).

**Cần thêm / còn thiếu:**
- Đã có: lệnh build/test của hai repo.
- Cần thêm: cập nhật trạng thái và các quyết định trong spec; cập nhật DBML nếu repo có cách sinh (kiểm `Database/ai_storytelling_platform.dbml` có test đồng bộ không).
- Còn thiếu / ngoài phạm vi: deploy prod và chạy migration trên RDS (user tự quyết; workflow Deploy Core API không chạy CDK nhưng có chạy migration lúc app khởi động — kiểm trước khi deploy).

**Files:**
- Modify: `docs/superpowers/specs/2026-11-22-phan-quyen-permission-design.md` (trạng thái, mục 5.5 seed, mục 5.7 cache)
- Modify: `Database/ai_storytelling_platform.dbml` (nếu có generator/test đồng bộ)

- [ ] **Step 1:** `dotnet build` → 0 error.
- [ ] **Step 2:** `dotnet test` → toàn bộ PASS (test cần PostgreSQL thật được bỏ qua như hiện nay).
- [ ] **Step 3:** Frontend (trong `src`): `npm test`, `npx tsc --noEmit`, `npm run lint` → PASS.
- [ ] **Step 4:** Chạy backend + frontend local, đăng nhập tài khoản Administrator seed, mở "Phân quyền": tắt `review.gray_zone` của Moderator → lưu OK; tắt nó ở cả Administrator → thấy lỗi "Phải còn ít nhất một role duyệt bước này"; đăng nhập Moderator gọi `GET /api/v1/GrayZoneReview/...` → 403 sau khi đã tắt; bật lại.
- [ ] **Step 5:** Cập nhật spec; `git status` / `git diff --stat` ở hai repo, báo cáo file tạo/sửa.
