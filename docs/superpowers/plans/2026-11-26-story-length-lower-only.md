# Story Length: Lower-Only Parent Override Implementation Plan

## Backend execution status (2026-11-26)

- Tasks 1–2 implemented, including the DTO minimum of 300 and regression coverage for saving and generation.
- Red check: 8 failures / 9 passes before implementation. Focused check after implementation: 170 passed.
- Full Core unit suite: 4,907 passed, 0 failed, 0 skipped (`--artifacts-path artifacts --no-build --no-restore`). Build emitted existing package/analyzer warnings and NU1900 because NuGet vulnerability data was unavailable.
- Generation tests now expect the configured default's upper bound instead of preserving a stored cap of 2,000; the valid 1,200-word override test uses reading level 4.
- Task 3 pending: no migration created or applied; `ClampStoryLengthOverrides` requires separate explicit migration authorization under repository `AGENTS.md`.
- Task 4 pending: `E:\Capstone\AI_Storytelling_Frontend` is outside the permitted backend workspace and was not accessed.
- Task 5 pending: no API restart, database update, or live application writes performed.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Story length defaults stay at 900 words (6–8, reading level 1–3) and 1,500 words (9–10, or reading level 4–5); a parent can only lower it (minimum 300), and one story's length can be at most the effective length + 10 %.

**Architecture:** The backend is the source of truth at generation time: `StoryLengthRules.ForGeneration` clamps the parent's number to the level default and derives the per-story cap as `UpperBound(effective target)` (110 %). `AIStoryInputService` already rejects a per-story `TargetLength` above that cap, so the "Dài" preset is enforced server-side for free. `SafetyPolicyService` rejects saving a number outside 300…default. The frontend mirrors the same rule in its sliders. A one-off migration resets saved numbers that are now above the default.

**Tech Stack:** .NET 10 / EF Core / xUnit + Moq (backend), Next.js + TypeScript / Vitest (frontend repo `AI_Storytelling_Frontend`).

**Spec:** Decisions agreed in chat on 2026-11-26:
- Defaults unchanged: 900 / 1,500 (`LearningBatchOptions.YoungerTargetWords` / `OlderTargetWords`; 1,500 also for a 6–8 child on reading level ≥ 4).
- Parent override: lower only, range **300 … default**.
- Per-story length ("Ngắn / Vừa / Dài" in the AI brief form): at most **effective length + 10 %**.
- Existing overrides above the default (e.g. Bé Khánh 2,100) go back to "theo mức đọc".

## Global Constraints

- Minimum story length: **300** words. Per-story ceiling: **`StoryLengthRules.UpperBound(effective target)`** (= ceil(target × 1.1)).
- Default comes only from `LearningBatchOptions.Profile(ageBand, readingLevel: level).TargetWords`; never hard-code 900/1500 in backend logic.
- Reading level or age band can change after the parent saves a number: generation must clamp at run time, not only at save time.
- The migration in Task 3 changes saved data: run it only after the user confirms (CLAUDE.md §6).
- Backend tests: `dotnet test tests/StoryPlatform.UnitTests --artifacts-path artifacts` (Core API may be running in Visual Studio); delete `artifacts/` afterwards.
- Frontend tests run from `AI_Storytelling_Frontend/src`: `npx vitest run tests`, `npx tsc --noEmit -p tsconfig.json`.
- No commit steps.

---

### Task 1: Clamp the length at generation time (backend rule)

**Việc này làm gì (nói dễ hiểu):**
Đây là "chốt chặn" cuối cùng: mỗi lần tạo truyện, hệ thống tự lấy **số nhỏ hơn** giữa con số phụ huynh đặt và độ dài mặc định của bé. Vì vậy, kể cả khi dữ liệu cũ đang lưu 2.100 từ, hoặc bé vừa đổi nhóm tuổi hay mức đọc, truyện vẫn không dài hơn mức mặc định. Trần cho mỗi truyện là độ dài đó cộng 10%, nên thanh "Dài" ở form tạo truyện cũng tự bị giới hạn theo.

**Cần thêm / còn thiếu:**
- Đã có: `StoryLengthRules.ForGeneration(storyLengthTarget, storedCap, levelDefaultTarget)` được gọi ở `AIStoryInputService` (dòng ~789); `AIStoryInputService.ValidateAgainstContext` từ chối `TargetLength > context.MaximumLength` (dòng ~879); `StoryLengthRulesTests`.
- Cần thêm: hằng `MinimumTarget = 300`. `ForGeneration` dùng `target = min(số phụ huynh, mặc định)` và `cap = UpperBound(target)`, không còn để `MaxStoryLength` đã lưu nới trần lên.
- Còn thiếu / ngoài phạm vi: luồng Creator (`ResolveCreatorContextAsync`) không dùng `ForGeneration`, nên không đổi.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ChildProfiles/Safety/StoryLengthRules.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ChildProfiles/Safety/StoryLengthRulesTests.cs`

**Interfaces:**
- Produces: `public const int StoryLengthRules.MinimumTarget = 300;` and the new `ForGeneration` semantics (same signature `(int Target, int Cap) ForGeneration(int? storyLengthTarget, int storedCap, int levelDefaultTarget)`).

- [x] **Step 1: Rewrite the `ForGeneration` tests**

Replace `ForGeneration_CustomTarget_UsesTargetAndRaisedCap` and `ForGeneration_FollowsLevelDefaultWhenNoTarget` with:

```csharp
    [Theory]
    [InlineData(600, 900, 600, 660)]     // phụ huynh hạ xuống: dùng số đó, trần +10%
    [InlineData(900, 900, 900, 990)]     // bằng mặc định
    [InlineData(2100, 900, 900, 990)]    // dữ liệu cũ cao hơn mặc định: kẹp về mặc định
    [InlineData(1200, 1500, 1200, 1320)] // bé 9–10 tuổi / mức 4–5 hạ xuống
    public void ForGeneration_CustomTarget_IsClampedToLevelDefault(int custom, int levelDefault, int expectedTarget, int expectedCap) =>
        Assert.Equal((expectedTarget, expectedCap), StoryLengthRules.ForGeneration(custom, 5000, levelDefault));

    [Theory]
    [InlineData(2000, 900, 900, 990)]    // trần cũ rộng không còn nới trần
    [InlineData(900, 900, 900, 990)]
    [InlineData(1500, 1500, 1500, 1650)]
    public void ForGeneration_FollowsLevelDefaultWhenNoTarget(int storedCap, int levelDefault, int expectedTarget, int expectedCap) =>
        Assert.Equal((expectedTarget, expectedCap), StoryLengthRules.ForGeneration(null, storedCap, levelDefault));
```

Keep `ForGeneration_LegacyCapBelowDefault_IsHonouredAsTargetAndCap` (700, 700) unchanged.

- [x] **Step 2: Run to verify failure**

Run: `dotnet test tests/StoryPlatform.UnitTests --artifacts-path artifacts --filter "FullyQualifiedName~StoryLengthRulesTests"`
Expected: the 2100 / 2000 / 600 cases FAIL (current code keeps 2100 and keeps cap 2000/5000).

- [x] **Step 3: Implement**

In `StoryLengthRules`:

```csharp
    /// <summary>Độ dài nhỏ nhất phụ huynh được đặt (đủ cho mở đầu, diễn biến, kết thúc).</summary>
    public const int MinimumTarget = 300;

    public static (int Target, int Cap) ForGeneration(int? storyLengthTarget, int storedCap, int levelDefaultTarget)
    {
        // Phụ huynh chỉ được hạ độ dài: số đã lưu cao hơn mặc định (dữ liệu cũ, hoặc bé vừa đổi tuổi/mức đọc) bị kẹp về mặc định.
        if (storyLengthTarget is > 0)
        {
            var target = Math.Min(storyLengthTarget.Value, levelDefaultTarget);
            return (target, UpperBound(target));
        }

        // Giới hạn thấp hơn mặc định do phụ huynh đặt từ trước khi có "độ dài truyện": giữ nguyên làm độ dài đích và trần.
        if (storedCap < levelDefaultTarget)
        {
            return (storedCap, storedCap);
        }

        return (levelDefaultTarget, UpperBound(levelDefaultTarget));
    }
```

Update the XML doc of `ForGeneration` to say the cap is always `UpperBound(target)` (each story ≤ +10 %).

- [x] **Step 4: Run tests**

Run: `dotnet test tests/StoryPlatform.UnitTests --artifacts-path artifacts --filter "FullyQualifiedName~StoryLengthRules|FullyQualifiedName~AIStoryInputService"`
Expected: PASS. If an `AIStoryInputService` test expected a cap above 110 % of the default, update its expectation to `UpperBound(default)` and note it in the report.

---

### Task 2: Reject saving a length above the default (backend validation)

**Việc này làm gì (nói dễ hiểu):**
Khi phụ huynh lưu cài đặt An toàn, hệ thống chỉ chấp nhận độ dài từ 300 đến mức mặc định của bé (900 hoặc 1.500 tùy tuổi và mức đọc). Nếu gửi số lớn hơn, API trả lỗi ghi rõ khoảng cho phép, để không ai vòng qua giao diện mà đặt truyện dài hơn.

**Cần thêm / còn thiếu:**
- Đã có: `SafetyPolicyService.ValidateRequest` (đang cho 100–9000), `ApplyRequest`, các test trong `SafetyPolicyServiceTests`; mẫu inject tùy chọn `LearningBatchOptions? learningOptions = null` (như `AIStoryInputService`).
- Cần thêm: kiểm tra `300 ≤ StoryLengthTarget ≤ mặc định`, với mặc định tính từ `child.AgeBand` và mức đọc trong `LearningProfile` (nếu có); thông báo lỗi tiếng Việt.
- Còn thiếu / ngoài phạm vi: không tự sửa số đã lưu khi bé đổi tuổi hoặc mức đọc sau này, vì Task 1 đã kẹp lúc tạo truyện và Task 4 kẹp lúc hiển thị.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ChildProfiles/Safety/Services/SafetyPolicyService.cs` (constructor; `ValidateRequest` → keep static checks, add async length check in `SetSafetyPolicyAsync` before `ApplyRequest`)
- Test: `tests/StoryPlatform.UnitTests/Features/ChildProfiles/Safety/SafetyPolicyServiceTests.cs`

**Interfaces:**
- Consumes: `StoryLengthRules.MinimumTarget` (Task 1); `LearningBatchOptions.Profile(string age, int requested = 0, int? readingLevel = null)`.
- Produces: constructor `SafetyPolicyService(IUnitOfWork, IChildProfileAccessGuard, IAuditLogWriter, LearningBatchOptions? learningOptions = null)`.

- [x] **Step 1: Write the failing tests**

In `SafetyPolicyServiceTests`, add a learning-profile repo mock and a helper that seeds the child:

```csharp
    private readonly Mock<IGenericRepository<LearningProfile>> _learningRepo = new();

    private void SeedChild(AgeBand band, int? readingLevel)
    {
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2, AgeBand = band, Status = ChildProfileStatus.Active });
        _unitOfWork.Setup(u => u.Repository<LearningProfile>()).Returns(_learningRepo.Object);
        _learningRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<LearningProfile, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(readingLevel is null ? null : new LearningProfile { ChildProfileId = 1, ReadingLevel = readingLevel.Value });
    }

    [Theory]
    [InlineData(AgeBand.Age_6_8, 2, 1200)]   // trên 900
    [InlineData(AgeBand.Age_9_10, 2, 1600)]  // trên 1500
    [InlineData(AgeBand.Age_6_8, 2, 250)]    // dưới 300
    public async Task SetSafetyPolicyAsync_StoryLengthOutsideLowerOnlyRange_IsRejected(AgeBand band, int level, int length)
    {
        AllowPermission();
        SeedChild(band, level);
        var request = ValidRequest();
        request.StoryLengthTarget = length;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetSafetyPolicyAsync(1, 2, request));

        Assert.Contains("300", error.Message);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

Use the repository interface type the existing mocks in this file already use for `_profileRepo` (match its declaration). Rewrite `SetSafetyPolicyAsync_CustomStoryLength_SavesTargetAndRaisesCapToUpperBound` to call `SeedChild(AgeBand.Age_6_8, 4)` (default 1,500 because level ≥ 4) and keep `StoryLengthTarget = 1200` / cap `1320` — it now proves "6–8 on reading level 4 may choose 1,200". Rewrite `SetSafetyPolicyAsync_SwitchBackToLevel_ClearsTargetAndDoesNotLowerCap` only if it now fails (it sends `null`, so it should not hit the new check).

- [x] **Step 2: Run to verify failure**

Run: `dotnet test tests/StoryPlatform.UnitTests --artifacts-path artifacts --filter "FullyQualifiedName~SafetyPolicyServiceTests"`
Expected: the three new cases FAIL (no BadRequest today).

- [x] **Step 3: Implement**

In `ValidateRequest`, change the range check to the absolute floor only:

```csharp
        if (request.StoryLengthTarget is < StoryLengthRules.MinimumTarget)
        {
            throw new BadRequestException(
                $"Độ dài truyện phải từ {StoryLengthRules.MinimumTarget} từ trở lên.");
        }
```

Add the field + constructor parameter (`_learningOptions = learningOptions ?? new LearningBatchOptions();`) and a check called in `SetSafetyPolicyAsync` right after `ValidateRequest(request)` and the owner check:

```csharp
    /// <summary>Phụ huynh chỉ được hạ độ dài truyện: từ 300 đến mặc định theo tuổi và mức đọc của bé.</summary>
    private async Task EnsureStoryLengthWithinDefaultAsync(int childProfileId, int? storyLengthTarget, CancellationToken cancellationToken)
    {
        if (storyLengthTarget is not > 0) return;
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var learning = await _unitOfWork.Repository<LearningProfile>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var ageBand = child.AgeBand == AgeBand.Age_9_10 ? "9-10" : "6-8";
        var levelDefault = _learningOptions.Profile(ageBand, readingLevel: learning?.ReadingLevel).TargetWords;
        if (storyLengthTarget > levelDefault)
            throw new BadRequestException(
                $"Độ dài truyện chỉ được từ {StoryLengthRules.MinimumTarget} đến {levelDefault} từ (mặc định theo tuổi và mức đọc của bé).");
    }
```

Register nothing new in DI if `LearningBatchOptions` is already a singleton (check `StoryPlatform.Application`/`Infrastructure` `DependencyInjection.cs`); if the container resolves `SafetyPolicyService` without it, the optional default keeps behaviour working.

- [x] **Step 4: Run tests**

Run: `dotnet test tests/StoryPlatform.UnitTests --artifacts-path artifacts --filter "FullyQualifiedName~SafetyPolicy"`
Expected: PASS.

---

### Task 3: Reset saved lengths that are now above the default (data migration)

**Việc này làm gì (nói dễ hiểu):**
Một số bé đã được đặt độ dài cao hơn mặc định từ trước, như Bé Khánh 2.100 từ. Task 1 đã khiến các con số này không còn tác dụng khi tạo truyện, nhưng tab An toàn vẫn sẽ hiện chúng. Task này đưa các bé đó về "theo mức đọc", và nâng các số nhỏ hơn 300 lên 300, để dữ liệu khớp với quy tắc mới.

**Cần thêm / còn thiếu:**
- Đã có: bảng `safety_policies` (`StoryLengthTarget`, `MaxStoryLength`), `child_profiles.AgeBand`, `learning_profiles.ReadingLevel`; mẫu migration chỉ chứa SQL (ví dụ `SoftDeleteRetiredNotifications`).
- Cần thêm: migration `ClampStoryLengthOverrides`.
- Còn thiếu / ngoài phạm vi: **chỉ chạy sau khi bạn xác nhận**, vì đây là thay đổi dữ liệu. Seed không có `StoryLengthTarget` nên không cần sửa seed.

**Files:**
- Create (via `dotnet ef migrations add ClampStoryLengthOverrides --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Infrastructure`): `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_ClampStoryLengthOverrides.cs` (+ `.Designer.cs`)

- [ ] **Step 1: Generate the empty migration** with the command above (Infrastructure is its own startup project via `DesignTimeDbContextFactory`, so the locked Api `bin/` is not touched). Expected: snapshot unchanged.

- [ ] **Step 2: Fill `Up`**

```csharp
            // Phụ huynh chỉ được hạ độ dài truyện (300 … mặc định). Số cũ cao hơn mặc định → về "theo mức đọc"; dưới 300 → 300.
            // Mặc định: 1500 nếu bé 9–10 tuổi hoặc mức đọc ≥ 4, còn lại 900 (LearningBatchOptions).
            migrationBuilder.Sql(
                """
                UPDATE safety_policies sp
                SET "StoryLengthTarget" = NULL, "UpdatedAt" = NOW()
                FROM child_profiles cp
                LEFT JOIN learning_profiles lp ON lp."ChildProfileId" = cp."Id" AND lp."IsDeleted" = FALSE
                WHERE sp."ChildProfileId" = cp."Id"
                  AND sp."StoryLengthTarget" IS NOT NULL
                  AND sp."StoryLengthTarget" > CASE WHEN cp."AgeBand" = 'Age_9_10' OR COALESCE(lp."ReadingLevel", 0) >= 4 THEN 1500 ELSE 900 END;

                UPDATE safety_policies SET "StoryLengthTarget" = 300, "UpdatedAt" = NOW()
                WHERE "StoryLengthTarget" IS NOT NULL AND "StoryLengthTarget" < 300;
                """);
```

`Down`: comment only (`// Không khôi phục: số cũ vượt quy tắc mới.`). Before writing, confirm column names with `select column_name from information_schema.columns where table_name in ('safety_policies','learning_profiles','child_profiles')` and that `AgeBand` is stored as the enum name string.

- [ ] **Step 3: Verify on the local DB only after the user confirms** — run the app (EF applies migrations on start, or `dotnet ef database update`), then:

```sql
select sp."ChildProfileId", sp."StoryLengthTarget", sp."MaxStoryLength" from safety_policies sp where sp."ChildProfileId" = 14;
```

Expected: Bé Khánh (14) `StoryLengthTarget` is `NULL`.

---

### Task 4: Lower-only sliders in the frontend

**Việc này làm gì (nói dễ hiểu):**
Thanh "Độ dài truyện" ở tab An toàn và ở bước tạo hồ sơ bé giờ chỉ kéo được từ 300 đến mức mặc định của bé, nên phụ huynh không chọn được số mà backend sẽ từ chối. Cảnh báo "truyện dài" cũng được bỏ, vì không còn chọn được truyện dài hơn mặc định. Ở form tạo truyện, nút "Dài" bằng mức của bé cộng 10%.

**Cần thêm / còn thiếu:**
- Đã có: `storyLengthFor`, `effectiveStoryLength`, `storyLengthAdvice`, `MAX_STORY_LENGTH_RANGE` trong `src/app/lib/guardian.ts`; thanh trượt ở `SafetyWorkspace.tsx` (dòng ~325) và `CreateChildWizard.tsx` (dòng ~259); `lengthPresets` trong `aiStoryInputFlowUtils.ts` (Dài = Vừa × 1,2, kẹp theo `maximumLength`); test trong `tests/unit/guardian/guardian.test.ts`.
- Cần thêm: `STORY_LENGTH_MIN = 300`; thanh trượt `min = 300`, `max = storyLengthFor(...).target`; `effectiveStoryLength` kẹp số hiển thị về mặc định; bỏ `storyLengthAdvice` và các cảnh báo dùng nó; `lengthPresets` đặt Dài = Vừa × 1,1.
- Còn thiếu / ngoài phạm vi: `MAX_STORY_LENGTH_RANGE` / `isMaxStoryLength` vẫn dùng để kiểm bản nháp cũ, nên giữ nguyên.

**Files (repo `AI_Storytelling_Frontend`):**
- Modify: `src/app/lib/guardian.ts`
- Modify: `src/app/features/safety/SafetyWorkspace.tsx`
- Modify: `src/app/features/children/CreateChildWizard.tsx`
- Modify: `src/app/features/story-workflow/aiStoryInputFlowUtils.ts` (`lengthPresets`)
- Test: `src/tests/unit/guardian/guardian.test.ts`, `src/tests/unit/story-workflow/aiStoryInputFlowUtils.test.ts`

**Interfaces:**
- Consumes: backend rule from Task 1/2 (300 … default; per story ≤ +10 %).
- Produces: `export const STORY_LENGTH_MIN = 300;` in `guardian.ts`; `effectiveStoryLength` keeps its signature.

- [ ] **Step 1: Write the failing tests**

In `guardian.test.ts`, replace the `storyLengthAdvice` tests (the function is removed) and add:

```ts
describe('lower-only story length', () => {
  it('custom length above the level default is shown clamped to the default', () => {
    expect(effectiveStoryLength('Age_6_8', 2, 2100, 2310)).toEqual({ value: 900, custom: true });
    expect(effectiveStoryLength('Age_9_10', 2, 1200, 1320)).toEqual({ value: 1200, custom: true });
  });
  it('minimum is 300', () => {
    expect(STORY_LENGTH_MIN).toBe(300);
  });
});
```

Update the existing case `effectiveStoryLength('Age_6_8', 2, 1200, 1320)` to expect `{ value: 900, custom: true }`.

In `aiStoryInputFlowUtils.test.ts`, assert the long preset is medium × 1.1 and never above the cap:

```ts
  it('nút Dài là mức của bé +10%, không vượt trần backend', () => {
    expect(lengthPresets(990, 1, 900).long).toBe(990);
    expect(lengthPresets(1650, 4, 1500).long).toBe(1650);
  });
```

- [ ] **Step 2: Run to verify failure**

Run (in `AI_Storytelling_Frontend/src`): `npx vitest run tests/unit/guardian/guardian.test.ts tests/unit/story-workflow/aiStoryInputFlowUtils.test.ts`
Expected: FAIL (no `STORY_LENGTH_MIN`, no clamp; long preset 1080/1800 before clamp → check the exact failing values).

- [ ] **Step 3: Implement**

`guardian.ts`:

```ts
/** Phụ huynh chỉ được hạ độ dài truyện: từ 300 đến mặc định theo tuổi và mức đọc (backend StoryLengthRules.MinimumTarget). */
export const STORY_LENGTH_MIN = 300;
```

In `effectiveStoryLength`, clamp the custom value:

```ts
  const byLevel = storyLengthFor(ageBand, level).target;
  if (typeof storyLengthTarget === "number" && storyLengthTarget > 0) return { value: Math.min(storyLengthTarget, byLevel), custom: true };
```

Delete `storyLengthAdvice`, `STORY_LENGTH_LONG_RATIO`, `STORY_LENGTH_VERY_LONG_RATIO` and their imports/usages (`SafetyWorkspace.tsx` `lenAdvice`, `CreateChildWizard.tsx` `lenAdvice`) after confirming with `grep -rn "storyLengthAdvice\|STORY_LENGTH_LONG_RATIO\|STORY_LENGTH_VERY_LONG_RATIO" src/app src/tests` that nothing else uses them.

`SafetyWorkspace.tsx` slider: `min={STORY_LENGTH_MIN}` `max={levelLen.target}` `step={100}`; when switching to "Tự chỉnh", start from `Math.min(shownLen, levelLen.target)`. Add a one-line hint under the slider: `Chỉ chỉnh ngắn hơn được, tối đa {levelLen.target} từ theo tuổi và mức đọc của bé.`

`CreateChildWizard.tsx` slider: `min={STORY_LENGTH_MIN}` `max={suggestedLen}` `step={100}`; clamp a restored draft value with `Math.min(draft.maxLength, suggestedLen)`.

`aiStoryInputFlowUtils.ts` `lengthPresets`: `long: clampTargetLength(Math.round(medium * 1.1 / 10) * 10, maximumLength)`.

- [ ] **Step 4: Run frontend checks**

Run: `npx tsc --noEmit -p tsconfig.json`, `npx eslint app/lib/guardian.ts app/features/safety/SafetyWorkspace.tsx app/features/children/CreateChildWizard.tsx app/features/story-workflow/aiStoryInputFlowUtils.ts`, `npx vitest run tests`
Expected: no type/lint errors; all tests PASS.

---

### Task 5: End-to-end check

**Việc này làm gì (nói dễ hiểu):**
Chạy thử trên app thật để chắc cả hai phía khớp nhau: tab An toàn không kéo được quá mặc định, API từ chối số lớn hơn, form tạo truyện có nút "Dài" đúng bằng mức của bé cộng 10%, và Bé Khánh sau migration quay về 900 từ.

**Cần thêm / còn thiếu:**
- Đã có: tài khoản `parent_demo1` (mật khẩu demo seed), Bé An (6–8), Bé Bình (9–10), Bé Khánh (14).
- Cần thêm: không có code.
- Còn thiếu / ngoài phạm vi: không có.

**Files:** none.

- [ ] **Step 1:** Restart Core API; open Hồ sơ bé → Bé An → An toàn → "Tự chỉnh": slider range 300–900.
- [ ] **Step 2:** Swagger or browser fetch: `PUT /api/v1/SafetyPolicy/{BéAnId}` with `storyLengthTarget: 1200` → `400` "Độ dài truyện chỉ được từ 300 đến 900 từ …".
- [ ] **Step 3:** Tạo truyện → Bé An: presets Ngắn ≈ 720, Vừa 900, Dài 990; slider max 990.
- [ ] **Step 4:** Bé Khánh (after Task 3 migration): tab An toàn shows "Theo mức đọc", 900 words; a new AI story targets 900.
