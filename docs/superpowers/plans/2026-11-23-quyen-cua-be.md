# Quyền của bé Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phụ huynh bật/tắt 7 chức năng cho từng bé; API của bé chặn chức năng bị tắt, giao diện của bé ẩn nó.

**Architecture:** 5 cột `bool` mới trên `safety_policies` (mặc định `true`) cạnh 2 cột có sẵn; danh mục `ChildFeatures` trong Application đọc trạng thái từ `SafetyPolicy`; filter `[RequireChildFeature]` ở Api chặn action; `my-session` trả `DisabledFeatures`; frontend dùng context `KidFeatures` để ẩn.

**Tech Stack:** ASP.NET Core (.NET 10), EF Core + Npgsql, xUnit + SQLite; Next.js/React + Vitest.

**Spec:** `docs/superpowers/specs/2026-11-23-quyen-cua-be-design.md`

## Global Constraints

- Mã: `child.quiz`, `child.notebook`, `child.library.browse`, `child.wishes`, `child.voice`, `child.reflection`, `child.rewards`.
- Cột mới: `QuizEnabled`, `NotebookEnabled`, `LibraryBrowseEnabled`, `ReflectionEnabled`, `RewardsEnabled` — `boolean NOT NULL DEFAULT true`.
- Lỗi khi tắt: HTTP 403, message `"Bố mẹ chưa bật chức năng này."`, `errorCode` `"CHILD_FEATURE_DISABLED"`.
- Bé chưa có `safety_policies` → mọi chức năng bật.
- Request lưu cài đặt thiếu trường mới → giữ giá trị cũ.
- Không tự commit; không đụng `ChildCreationEnabled`.

## Review Focus

1. Bé chưa có dòng `safety_policies` gọi API bị gắn filter → phải qua (Task 2 test).
2. Client cũ gửi `PUT SafetyPolicy` không có 5 trường mới → không tắt gì (Task 1 test).
3. Phụ huynh tắt chức năng giữa lúc bé đang mở màn đó → bé thấy thông báo thân thiện, không màn lỗi (Task 5 test helper nhận diện lỗi).
4. Thêm action mới vào controller của bé mà quên gắn → test phủ fail (Task 2).
5. `LibraryBrowseEnabled = false` không được chặn đọc truyện từ lộ trình/truyện gia đình (Task 2 test: `Start`/`GetContent` không có filter).

---

### Task 1: Dữ liệu và cài đặt của phụ huynh (backend)

**Việc này làm gì (nói dễ hiểu):** Thêm 5 công tắc mới vào cài đặt an toàn của mỗi bé, mặc định bật. Phụ huynh đọc và lưu được 5 công tắc này qua API cài đặt an toàn đang có; ứng dụng cũ không gửi trường mới thì không bị tắt nhầm.

**Cần thêm / còn thiếu:**
- Đã có: `SafetyPolicy`, `SafetyPolicyService` (map ở `:312-322`, DTO ở `:399-407`), seed `07_safety_policies.sql`.
- Cần thêm: 5 thuộc tính, cấu hình default, migration, trường DTO, map trong service.
- Còn thiếu / ngoài phạm vi: chặn API (Task 2).

**Files:** Modify `src/Core/StoryPlatform.Domain/Entities/SafetyPolicy.cs`, `.../Configurations/SafetyPolicyConfiguration.cs`, `.../ChildProfiles/Safety/DTOs/SafetyPolicyDtos.cs`, `.../Safety/Services/SafetyPolicyService.cs`; Create migration `AddChildFeatureToggles`; Test: `tests/StoryPlatform.UnitTests/Features/ChildProfiles/SafetyPolicyChildFeatureTests.cs` (theo cách dựng service trong test `SafetyPolicyService` hiện có).

**Interfaces:**
- Produces: `SafetyPolicy.QuizEnabled|NotebookEnabled|LibraryBrowseEnabled|ReflectionEnabled|RewardsEnabled : bool` (mặc định `true` trong C# và DB); `SafetyPolicyDto` cùng 5 tên `bool`; `SetSafetyPolicyRequestDto` cùng 5 tên kiểu `bool?`.

- [ ] Step 1: Test `Missing_new_fields_keep_current_values` (policy có `QuizEnabled=false`, request không set 5 trường → vẫn `false`), `Set_new_fields_are_saved` (gửi `false` cho cả 5 → lưu `false`, DTO trả `false`), `New_policy_defaults_to_all_enabled`.
- [ ] Step 2: Chạy → fail (build).
- [ ] Step 3: Thêm thuộc tính, `HasDefaultValue(true)`, DTO, map `if (request.X is bool x) policy.X = x;`, DTO ra.
- [ ] Step 4: `dotnet ef migrations add AddChildFeatureToggles` (chỉ `AddColumn` 5 cột `defaultValue: true`).
- [ ] Step 5: `dotnet test --filter "SafetyPolicy|SeedDataAlignmentTests"` → PASS.

### Task 2: Danh mục, filter và gắn lên API của bé (backend)

**Việc này làm gì (nói dễ hiểu):** Mỗi chức năng của bé có một mã. Mỗi API của bé thuộc chức năng nào thì gắn mã đó; khi phụ huynh đã tắt, API trả thông báo "Bố mẹ chưa bật chức năng này." thay vì chạy.

**Cần thêm / còn thiếu:**
- Đã có: `[Authorize(Policy = ChildSession)]`, `ChildId` lấy từ claim `NameIdentifier`/`sub`, `ApiResponse`, các service tự kiểm wishes/voice (giữ nguyên).
- Cần thêm: `ChildFeatures`, `IChildFeatureService`, `RequireChildFeatureAttribute` + filter, gắn lên action theo bảng mục 5 spec, test phủ.

**Files:** Create `src/Core/StoryPlatform.Application/Features/ChildProfiles/Safety/ChildFeatures.cs`, `.../Safety/Interfaces/IChildFeatureService.cs`, `.../Safety/Services/ChildFeatureService.cs`, `src/Core/StoryPlatform.Api/Authorization/RequireChildFeatureAttribute.cs`; Modify `ChildQuizController`, `ChildNotebookController`, `ChildReadingController` (`SearchStories`, `ReadAloud`, `ReadAloudPractice`), `ChildWishesController`, `ChildStorySummaryController` (`SendRecording`), `ChildStoryReflectionController`, `ChildRewardsController`, `Application/DependencyInjection.cs`; Test `tests/StoryPlatform.UnitTests/Features/ChildProfiles/ChildFeatureTests.cs`.

**Interfaces:**
- Produces: `ChildFeatures.Quiz/Notebook/LibraryBrowse/Wishes/Voice/Reflection/Rewards` (string const); `ChildFeatures.All : IReadOnlyList<ChildFeatureDefinition(Code, Group, Description)>`; `static bool ChildFeatures.IsEnabled(SafetyPolicy? policy, string code)`; `IChildFeatureService.IsEnabledAsync(int childProfileId, string code, CancellationToken)`, `GetDisabledAsync(int childProfileId, CancellationToken) : Task<IReadOnlyList<string>>`.
- Produces: `[RequireChildFeature(string feature)]` (`TypeFilterAttribute`), 403 body = `ApiResponse<object>` lỗi với `ErrorCode = "CHILD_FEATURE_DISABLED"`, `Data = { feature }`.

- [ ] Step 1: Test `IsEnabled` cho từng mã (null policy → true; cột false → false), filter test (bé tắt → 403 + errorCode; bật → chạy tiếp), `Every_child_action_is_core_or_gated` (danh sách cốt lõi theo spec mục 5; action có `ChildSession` mà không thuộc cốt lõi phải có `[RequireChildFeature]`), `Library_browse_does_not_gate_reading` (`ChildReadingController.Start`/`GetContent` không có attribute).
- [ ] Step 2: Chạy → fail.
- [ ] Step 3: Cài đặt, đăng ký service, gắn attribute.
- [ ] Step 4: `dotnet test tests/StoryPlatform.UnitTests` → PASS toàn bộ (ma trận quyền không đổi).

### Task 3: Phiên của bé trả danh sách chức năng đang tắt (backend)

**Việc này làm gì (nói dễ hiểu):** Khi ứng dụng của bé hỏi "bé là ai", backend trả thêm danh sách chức năng bố mẹ đã tắt để giao diện ẩn trước.

**Files:** Modify `ChildAccessCredentialDtos.cs` (`ChildSessionProfileDto.DisabledFeatures : List<string>`), `ChildAccessCredentialService.cs:322`; Test thêm vào test hiện có của `GetMySessionProfileAsync`.

- [ ] Step 1: Test `Session_profile_lists_disabled_features` (tắt quiz + rewards → `["child.quiz","child.rewards"]`, theo thứ tự danh mục).
- [ ] Step 2–4: fail → cài (dùng `IChildFeatureService.GetDisabledAsync`) → PASS.

### Task 4: Công tắc trên trang cài đặt an toàn (frontend)

**Việc này làm gì (nói dễ hiểu):** Trang cài đặt an toàn của phụ huynh thêm khối "Bé được dùng" với 5 công tắc mới (điều ước và giọng nói vẫn ở chỗ cũ), có cảnh báo cho câu đố.

**Files:** Modify `src/app/features/safety/SafetyWorkspace.tsx` (thêm state, `settingsKey`, payload, khối `Toggle`), `src/app/types/childProfile.ts` + `backendContracts.ts` (5 trường); Create `src/app/features/safety/childFeatures.ts` (danh sách 5 công tắc: field, nhãn, gợi ý); Test `src/tests/unit/safety/childFeatures.test.ts`.

- [ ] Step 1: Test danh sách có đúng 5 field `quizEnabled, notebookEnabled, libraryBrowseEnabled, reflectionEnabled, rewardsEnabled` và mục quiz có cảnh báo `"Tắt câu đố thì EXP và tiến độ lộ trình từ câu đố không được tính."`; helper `readChildFeatures(policy)` coi thiếu trường là `true`.
- [ ] Step 2–4: fail → cài → `npm test`, `tsc`, lint PASS.

### Task 5: Giao diện của bé ẩn chức năng bị tắt (frontend)

**Việc này làm gì (nói dễ hiểu):** Ứng dụng của bé không hiện nút câu đố, sổ từ vựng, ô tìm truyện, điều ước, đọc to, cảm nhận, phần thưởng khi bố mẹ đã tắt; nếu vừa bị tắt giữa chừng thì hiện câu nhẹ nhàng thay vì báo lỗi.

**Files:** Create `src/app/features/kid/kidFeatures.tsx` (`KidFeaturesProvider`, `useKidFeature(code): boolean`, `isFeatureDisabledError(res): boolean`); Modify `ConnectedKidHome.tsx` (đọc `disabledFeatures` từ `getSessionProfile`, bọc provider), `KidAfterReading.tsx` (quiz, reflection, summary voice), `ChildReadingLibrary.tsx` (rewards, wish), `KidShelves.tsx` (`KidSearchAction`), `ChildReader.tsx` (`KidReadAloudPractice`), `KidHud.tsx` (không gọi rewards khi tắt), `src/app/types/supervision.ts` (`disabledFeatures?: string[]`); Test `src/tests/unit/kid/kidFeatures.test.ts`.

- [ ] Step 1: Test `isFeatureDisabledError` nhận `{status:403, errorCode:'CHILD_FEATURE_DISABLED'}` (và message đúng), từ chối 403 khác; logic tính `enabled` từ `disabledFeatures` (thiếu → bật).
- [ ] Step 2–4: fail → cài → `npm test`, `tsc`, lint PASS. Nếu `apiRequest` không giữ `errorCode`, nhận diện bằng `status === 403 && message === "Bố mẹ chưa bật chức năng này."`.

### Task 6: Kiểm tra, tài liệu, PR

- [ ] `dotnet build StoryPlatform.sln`, `dotnet test StoryPlatform.sln`; frontend `npm test`, `tsc`, lint.
- [ ] Cập nhật spec (trạng thái), sinh lại `docs/core-api-coverage.json` nếu `coreCompletion` test đòi.
- [ ] Commit, push, mở PR backend trước rồi frontend (frontend chịu được backend cũ: thiếu `disabledFeatures` = không ẩn gì).
