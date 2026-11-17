# Lộ trình đọc theo chặng (nhiều truyện một chặng) — Thiết kế

Ngày: 2026-11-14. Nguồn: brainstorming với user (đường "architectural").

## 1. Mục tiêu

Hiện mỗi nút trên bản đồ lộ trình là **một truyện** và bé chỉ có **đúng một truyện đang mở** (BR-L4A-06, unique index trên `child_roadmap_items`). "Đọc tiếp theo" luôn chèn truyện vào nút ngay sau truyện đang mở, nên phụ huynh không cho bé đọc thêm truyện "trong hôm nay" được.

Sau thay đổi: mỗi nút là một **chặng** chứa một hoặc nhiều truyện. Mọi truyện trong chặng mở cùng lúc, bé đọc theo thứ tự tùy ý. Bé đọc hết chặng (hoặc các truyện còn lại bị bỏ qua / gỡ) thì chặng sau mới mở. Khi thêm truyện, phụ huynh chọn chặng muốn gộp vào hoặc tạo chặng mới.

## 2. Quyết định của user

| Câu hỏi | Chọn |
|---|---|
| Ý nghĩa "chọn ngày trước đó" | Nhiều truyện trong một chặng |
| Bé đọc trong chặng | Mở cùng lúc, đọc tùy thứ tự |
| Chọn chỗ khi thêm | Chọn số chặng (đang đọc / chặng chờ) + "Đọc tiếp theo" / "Cuối lộ trình" tạo chặng mới; không thêm vào chặng đã xong |
| Sắp xếp lại | Chuyển truyện sang chặng khác; API order nhận bố cục theo nhóm chặng |
| Phạm vi | Backend + frontend (`AI_Storytelling_Frontend`) |
| Cách lưu | Hướng A: dùng lại cột `Position` làm **số chặng**, cho phép trùng; không thêm cột/bảng |

## 3. Quy tắc số chặng

`ChildRoadmapItem.Position` đổi nghĩa: **số chặng** của truyện trong lộ trình của bé. Các truyện cùng chặng có cùng `Position`.

Gọi:
- **Truyện sống** = trạng thái `Completed`, `Open`, `Waiting`. `Skipped` / `Removed` giữ `Position` cũ và **không** tham gia đánh số (bản đồ không vẽ chúng; chúng chỉ nằm ở lịch sử).
- **S** (chặng hiện tại) = `Position` của các truyện `Open` nếu có; nếu không thì `max(Position)` của các truyện `Completed`; không có thì 0.

Bất biến sau mọi thao tác:
1. Mọi truyện `Open` có cùng `Position` = S.
2. Mọi truyện `Completed` có `Position` ≤ S.
3. Các truyện `Waiting` có `Position` liên tiếp S+1, S+2, … (không có chặng rỗng ở giữa).

Đánh số lại (`RenumberWaiting`): nhóm các truyện `Waiting` theo `Position` cũ (thứ tự `Position`, rồi `Id` nhỏ nhất của nhóm), gán lần lượt S+1, S+2, …

## 4. Hành vi backend

### 4.1 Mở chặng (`ChildRoadmapAdvancer`)
- Còn truyện `Open` → không làm gì (trả về danh sách truyện đang mở).
- Không còn truyện `Open` → lấy chặng chờ nhỏ nhất. Với từng truyện trong chặng: chạy `IChildRoadmapEligibility.EvaluateAsync`; không hợp lệ thì `Skipped` (như hiện nay), hợp lệ thì `Open`, `OpenedAt = now`, `ExpectedCompletionAt = now + ExpectedDays`.
- Cả chặng bị bỏ qua → thử chặng kế tiếp.
- Chặng được mở nhận số `M + 1` (M = `max(Position)` các truyện `Completed`, 0 nếu không có), sau đó đánh số lại các chặng chờ.
- Giữ chữ ký `Task<ChildRoadmapItem?> AdvanceAsync(...)`: trả truyện đang mở có `Id` nhỏ nhất (hoặc null). Không nơi nào trong `src` dùng giá trị trả về ngoài kiểm có / không, nên không cần đổi sang danh sách.
- Logic đánh số dùng chung đặt ở lớp tĩnh mới `RoadmapStages` (`Features/ChildRoadmaps/Services/RoadmapStages.cs`).

### 4.2 Thêm truyện (`POST /api/v1/ChildRoadmap/stories`)
- `RoadmapInsertPosition` thêm giá trị `Stage = 3`; `AddRoadmapStoryRequestDto` thêm `int? StageNumber`.
- `NextUp`: chặng mới S+1, các chặng chờ cũ dồn xuống. `End`: chặng mới sau chặng chờ cuối. (Như hiện nay, mỗi lần thêm là một chặng mới.)
- `Stage`:
  - Bắt buộc đúng 1 bé trong `ChildProfileIds`, nếu không → 400 `"Chỉ chọn được chặng khi thêm truyện cho một bé."`
  - Thiếu `StageNumber` → 400 `"Phải chọn chặng muốn thêm vào."`
  - `StageNumber` = S và đang có truyện `Open` → truyện mới tạo ở trạng thái `Open` ngay (`OpenedAt`, `ExpectedCompletionAt` tính từ lúc thêm).
  - `StageNumber` là một chặng chờ đang có → truyện mới `Waiting` với `Position = StageNumber`.
  - Chặng đã xong, không tồn tại, hoặc ≤ 0 → 400 `"Chặng {n} không còn nhận thêm truyện."`
- Truyện đã có trong lộ trình:
  - Đang `Waiting` → **chuyển** sang chỗ đã chọn (cờ `MoveExistingToNextUp` của `InsertRequest` đổi tên thành `MoveExisting`, áp dụng cho mọi vị trí trừ `End`). Kết quả `outcome`: `MovedToNext` với `NextUp` (giữ như cũ), `Moved` với `Stage`. Chuyển vào chặng đang đọc thì truyện thành `Open`.
  - Đang `Open`, hoặc `Waiting` mà chọn `End` → `Skipped` / `already_in_roadmap` (như cũ).
- Sau khi chèn: đánh số lại chặng chờ, gọi advancer (để mở chặng nếu lộ trình đang không có truyện mở).
- Audit / thông báo thêm khóa `stageNumber` vào payload.

### 4.3 Sắp xếp lại (`PUT /api/v1/ChildRoadmap/{childProfileId}/order`)
- `ReorderRoadmapRequestDto` đổi `List<int> ItemIds` → `List<List<int>> Stages`.
- Hợp lệ khi: gộp các nhóm lại đúng bằng tập truyện `Waiting`, mỗi truyện xuất hiện một lần, không nhóm nào rỗng. Sai → 400 `"Danh sách phải gồm đúng các truyện đang chờ, chia theo chặng, mỗi truyện một lần; truyện đang mở không kéo thả được."`
- Nhóm thứ i nhận `Position = S + i`.

### 4.4 Gỡ / làm trống
- Gỡ truyện: như cũ, sau đó đánh số lại chặng chờ (chặng rỗng biến mất). Gỡ truyện `Open` → gọi advancer; advancer chỉ mở chặng mới khi không còn truyện `Open` nào.
- Làm trống: như cũ (mọi `Open`/`Waiting` → `Removed`).

### 4.5 Bé đọc xong (`ChildRoadmapProgressRecorder`)
- Truyện `Open` đọc xong → `Completed` (giữ `Position` = S); gọi advancer (chỉ mở chặng mới khi chặng hiện tại hết truyện mở).
- Truyện `Waiting` được đọc xong sớm (bé đọc từ thư viện) → `Completed` và `Position = S` (tính vào chặng hiện tại), rồi đánh số lại chặng chờ.
- Kiểm "đã hoàn thành cả lộ trình" giữ nguyên.

### 4.6 Các luồng khác
- `ChildRoadmapArchiveHandler`: không đổi logic; khi truyện `Open` bị bỏ qua vì lưu trữ, advancer tự xử lý trường hợp còn truyện mở khác. Thêm bước đánh số lại chặng chờ sau khi bỏ qua truyện `Waiting` (để không còn chặng rỗng).
- Sao chép lộ trình mẫu (`RoadmapLibraryService.Copy`): mỗi truyện vẫn là một chặng riêng; `Position = Stage` trong request sao chép → 400 `"Lộ trình mẫu chỉ thêm được vào Đọc tiếp theo hoặc Cuối lộ trình."`
- Điều ước (`ChildRoadmapService.Wish`): giữ `NextUp`, một chặng riêng.
- `ChildStoryBrowseService`, `StoryUsageImpactReader`: không đổi.

## 5. Dữ liệu và migration

- Không thêm cột / bảng. Ghi chú lại nghĩa `Position` trong entity.
- `ChildRoadmapItemConfiguration`: bỏ unique index lọc `"Status" = 'Open'` (BR-L4A-06). Giữ unique `(ChildProfileId, StoryId)` cho truyện `Waiting`/`Open` và index `(ChildProfileId, Status, Position)`.
- Migration mới (EF, PostgreSQL):
  1. Drop unique index Open.
  2. SQL đánh số lại dữ liệu cũ cho từng bé (không đụng `Skipped`/`Removed`): `Completed` theo `CompletedAt`, `Id` → 1..c; `Open` → c+1; `Waiting` theo `Position`, `Id` → tiếp theo. Mỗi truyện cũ thành một chặng riêng nên hành vi đọc giữ nguyên.
  3. `Down`: tạo lại unique index (sẽ thất bại nếu lúc đó có bé nhiều hơn một truyện `Open`; ghi rõ trong migration). Không hoàn lại số `Position`.
- Seed (`Database/Seed`) kiểm lại: dữ liệu seed lộ trình phải thỏa bất biến mục 3.

## 6. API trả về

### 6.1 Phụ huynh — `GET /api/v1/ChildRoadmap/{childProfileId}` (`ChildRoadmapDto`)
- `Open` (một truyện) → `OpenItems: List<ChildRoadmapItemDto>`.
- Thêm `CurrentStage: int?` (= S khi có truyện `Open`, ngược lại `null`).
- `ChildRoadmapItemDto.Position` = số chặng (kể cả truyện trong `History`).
- `Waiting`, `History`, `CompletedCount`, `TotalCount` giữ nguyên ý nghĩa (đếm theo truyện).

### 6.2 Bé — "Bố mẹ chọn cho em" (`MyRoadmapDto`)
- `Open` → `OpenStories: List<MyRoadmapOpenStoryDto>` (mỗi truyện có `Note`, `ExpectedBy` riêng).
- `MyRoadmapOpenStoryDto` và `MyRoadmapDoneDto` thêm `Stage` (= `Position`) để màn bé gộp truyện đã đọc với cùng chặng thành một nút.
- `MyRoadmapUpcomingDto.Order` = số thứ tự chặng tính từ chặng kế (1 = chặng kế tiếp); các truyện cùng chặng có cùng `Order`. Vẫn `IsLocked = true`.
- `IsFinished`, `CompletedCount`, `TotalCount` giữ nguyên.

## 7. Frontend (`AI_Storytelling_Frontend`)

- `app/types/backendContracts.ts`: cập nhật `ChildRoadmapDto`, `MyRoadmapDto`, `RoadmapInsertPositionName` (`'Stage'`), body add (`stageNumber`), body order (`stages`).
- `app/services/roadmapService.ts`, `guardianService.ts`: `reorder(childId, stages: number[][])`; add nhận `stageNumber`.
- `app/services/roadmapAdapters.ts`: đọc `openItems`; nhận outcome `Moved`.
- `app/features/roadmap/AddToRoadmapDialog.tsx`: khi đúng 1 bé (và không phải lộ trình mẫu), tải lộ trình của bé và hiện "Chặng N · đang đọc", các chặng chờ, cùng "Đọc tiếp theo", "Cuối lộ trình". Nhiều bé / lộ trình mẫu: chỉ 2 lựa chọn cũ.
- `app/features/learning/RoadmapTab.tsx` (+ `learningData.ts`, `OverviewTab.tsx`, `LearningWorkspace.tsx` nếu dùng `open` đơn): mỗi nút một chặng; chặng nhiều truyện hiện bìa xếp chồng + nhãn "×n"; bấm nút → panel liệt kê truyện của chặng, mỗi truyện có "Gỡ", truyện chờ có "Chuyển sang chặng…" (chặng đang đọc → API add với `Stage`; chặng chờ khác / tách chặng mới → API order). Thanh "Đã đọc x/y" vẫn đếm theo truyện.
- `app/features/kid/KidToday.tsx`, `ConnectedKidHome.tsx`: khu "Hôm nay" hiện mọi truyện đang mở (mỗi truyện một thẻ, nút "Đọc ngay"); bản đồ của bé nhóm theo chặng.
- Test: `src/tests/unit/services/backendIntegration.test.ts`, `src/tests/smoke/core-flows.mjs` cập nhật theo body / DTO mới.

## 8. Kiểm thử

Backend (`tests/StoryPlatform.UnitTests/Features/ChildRoadmaps/`):
- Inserter: `NextUp`/`End` tạo chặng mới và dồn số; `Stage` gộp vào chặng chờ; `Stage` vào chặng đang đọc tạo truyện `Open`; chuyển truyện `Waiting` sẵn có; chặng không hợp lệ.
- Advancer: mở cả chặng; bỏ qua từng truyện không hợp lệ; cả chặng bị bỏ qua → chặng sau; còn truyện `Open` thì không mở thêm; số chặng mở = M+1.
- Service: `Stage` với nhiều bé → 400; thiếu `StageNumber` → 400; reorder theo nhóm hợp lệ / sai tập / nhóm rỗng; gỡ truyện làm rỗng chặng → đánh số lại; `BuildRoadmap` trả `OpenItems`, `CurrentStage`.
- Progress recorder: đọc xong một truyện trong chặng 2 truyện → chặng sau chưa mở; đọc xong cả hai → mở chặng sau; đọc sớm truyện chờ → `Position = S`.
- `MyRoadmapService`: nhiều `OpenStories`; `Order` theo chặng.
- Copy lộ trình mẫu với `Stage` → 400.
- Chạy `dotnet build`, `dotnet test`; tạo migration và kiểm SQL backfill trên DB seed.

Frontend: `npm test` (unit), smoke flow; mở trình duyệt kiểm hộp thoại thêm, bản đồ phụ huynh, màn bé.

Tài liệu test API: cập nhật các test case lộ trình trong `docs/api/api-test-guide-buoc-1-7.html` (thêm `Stage`, body order mới, `openItems`) và kiểm `docs/api/sau-khi-doc-cay-tri-thuc-api-test-guide.html` (chỉ dùng `NextUp`/`End`, đổi nếu đọc `open`).

## 9. Ngoài phạm vi

- Hạn đọc theo chặng (vẫn theo từng truyện).
- Thêm truyện vào chặng đã xong.
- Thứ tự gợi ý trong một chặng.
- Chọn chặng khi thêm cho nhiều bé cùng lúc.
