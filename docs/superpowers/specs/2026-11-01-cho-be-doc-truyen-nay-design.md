# "Cho bé đọc truyện này" — Thiết kế backend

**Ngày:** 01/11/2026
**Tài liệu nguồn:** S1 Mục 4–5 (Ba lối vào Child Session, Thu hồi phiên); WF-L1A UC-L1A-07 (Cho trẻ vào đọc); WF-L3A UC-L3A-01; S7 (J12, B5, F5/G12).

## 1. Mục tiêu và phạm vi

Phụ huynh đang đăng nhập bấm "Cho bé đọc truyện này" trên trang chi tiết truyện. Backend xác định bé nào đọc được truyện đó, sau đó cấp một Child Session gắn với phiên của phụ huynh (`EntryMethod = SupervisorPick`). App chuyển sang chế độ của trẻ và mở thẳng truyện. Bé đọc bằng token của chính bé, nên EXP, quiz và từ vựng được ghi cho đúng bé. Muốn quay về khu phụ huynh thì phải qua Parental Gate.

Trong phạm vi:

- API xem trước: các bé của phụ huynh, bé nào đọc được truyện, lý do không đọc được, và gợi ý chọn bé.
- API bàn giao: kiểm tra rồi tạo Child Session `SupervisorPick`, trả về token của bé kèm `storyId`.
- Tách phần tìm truyện trong `ChildReadingService` ra để dùng chung, giúp lúc bàn giao và lúc bé bấm đọc dùng cùng một luật.

Ngoài phạm vi:

- **Hold Mode.** Luồng này không kiểm tra Hold Mode, sẽ quyết định sau. `ChildReadingService.StartAsync` giữ nguyên kiểm tra Hold Mode sẵn có, vì đây là lớp an toàn và không được gỡ. Hiện chưa có code nào tạo ca Hold Mode (`InterventionCase` chỉ được đọc), nên thực tế bé luôn đọc được truyện phụ huynh đã chọn.
- API mở khóa Hold Mode và giới hạn 3 lượt retake trong 24 giờ (WF-L4D).
- Ghi `opened_from = supervisor_pick` / `selected_by_supervisor` trên `ReadingSession` cho telemetry và Luồng 6. Hiện chỉ `ChildSession.EntryMethod` ghi nhận việc này.
- Bàn giao truyện từ Thư viện cộng đồng (`communitySubmissionId`). Luồng này chỉ nhận `storyId`.

## 2. Quyết định đã chốt

| Chủ đề | Quyết định |
|---|---|
| Chọn bé | Có API xem trước để app bật/tắt nút, tự chọn bé hoặc hiện danh sách cho phụ huynh chọn |
| Độ tuổi | Chặn giống khi bé tự đọc (`IChildRoadmapEligibility.EvaluateAsync`). Truyện đã có trong lộ trình do phụ huynh thêm tay (`ParentStorySelection`) vẫn qua, theo luật sẵn có |
| Hold Mode | Không kiểm tra trong luồng này (mục 1) |
| Nơi đặt | `ChildAccessCredentialController` / `ChildAccessCredentialService`, đặt cạnh `handover` sẵn có |
| Luật dùng chung | Tạo static helper `ChildStoryLookup` (theo kiểu `HoldModeLookup`). `StartAsync` và luồng bàn giao cùng gọi helper này |
| Mã lý do | Dùng lại `ChildRoadmapSkipCodes`. Câu báo cho phụ huynh giống câu của lộ trình |
| Schema | Không đổi, không có migration. `ChildEntryMethod.SupervisorPick = 3` đã có |

## 3. Luật "bé đọc được truyện này"

Lần lượt kiểm tra như sau. Bước đầu tiên không đạt thì dừng.

1. **Hồ sơ**: `ChildProfile` chưa bị xóa và `Status == Active`. Nếu không đạt thì `profile_inactive`.
2. **Tìm truyện** (`ChildStoryLookup.FindAsync`, chuyển nguyên từ nhánh `storyId` của `ChildReadingService.ResolveTargetAsync`):
   - Truyện tồn tại và chưa bị xóa.
   - Nếu `Scope == Parent` và `ChildProfileId` khác bé này thì phải có `ParentStorySelection` (truyện đã nằm trong lộ trình thủ công của bé).
   - Có `StoryVersion` với `IsCurrent`, chưa bị xóa và đúng `StoryId`.
   - Không đạt thì `story_unavailable`.
3. **Eligibility** (`IChildRoadmapEligibility.EvaluateAsync`, không đổi): editor story, `Status == Ready`, chưa archive, truyện Creator đã publish, độ tuổi, Safety Policy (chủ đề bị chặn), độ dài tối đa. Kết quả map sang `blocked_topic`, `wrong_age`, `too_long`, còn lại là `story_unavailable`.

`StartAsync` vẫn giữ thứ tự hiện tại: tìm truyện → Hold Mode → eligibility. Câu báo cho bé không đổi.

## 4. API

### 4.1 Xem trước

`GET /api/v1/ChildAccessCredential/story-handover?storyId={id}`, `[Authorize(Roles = "User")]`

- Bé được liệt kê: `OwnerUserId == currentUserId`, chưa bị xóa, `Status` khác `Archived` và `Deleted` (giống `ListMyChildProfilesAsync`).
- Mỗi bé được chạy qua luật ở mục 3.
- `suggestedChildProfileId`:
  - Truyện là `Scope == Parent` có `ChildProfileId` thuộc phụ huynh và bé đó đọc được thì chọn bé đó.
  - Nếu không, khi chỉ có đúng một bé đọc được thì chọn bé đó.
  - Các trường hợp khác để `null`.
- Truyện không tồn tại hoặc phụ huynh không có quyền thì mọi bé đều là `story_unavailable`. Không trả 404, để không lộ thông tin truyện của gia đình khác.

Response `StoryHandoverOptionsDto`:

```json
{
  "storyId": 42,
  "suggestedChildProfileId": 7,
  "children": [
    { "childProfileId": 7, "nickname": "Bin", "canRead": true, "skipCode": null, "message": null },
    { "childProfileId": 8, "nickname": "Na", "canRead": false, "skipCode": "wrong_age", "message": "Truyện không đúng độ tuổi của bé này." }
  ]
}
```

Câu báo theo `skipCode` (giống `ChildRoadmapService.MessageFor`):

| skipCode | message |
|---|---|
| `profile_inactive` | Hồ sơ của bé đang không hoạt động. |
| `blocked_topic` | Truyện thuộc chủ đề đã bị chặn với bé này. |
| `wrong_age` | Truyện không đúng độ tuổi của bé này. |
| `too_long` | Truyện dài hơn mức cho phép của bé này. |
| `story_unavailable` | Truyện hiện không còn đọc được. |

### 4.2 Bàn giao

`POST /api/v1/ChildAccessCredential/{childProfileId}/handover-story`, `[Authorize(Roles = "User")]`

Request `StartStoryHandoverRequestDto`:

```json
{ "storyId": 42, "supervisorRefreshToken": "..." }
```

- `storyId` bắt buộc và ≥ 1. `supervisorRefreshToken` bắt buộc (cùng thông báo validate như `StartSupervisedChildSessionRequestDto`).

Thứ tự xử lý:

1. `EnsureOwnerAsync(childProfileId, currentUserId)`: lỗi sẵn có.
2. `ResolveSupervisorSessionAsync`: 401 "Phiên Supervisor không hợp lệ…" như hiện tại.
3. `EnsureChildCanEnterAsync`: 403 `ChildEntryBlockedMessage` như hiện tại.
4. Luật truyện (mục 3, bước 2–3): không đạt thì `ForbiddenException` với câu báo cho phụ huynh theo `skipCode`.
5. Tạo `ChildSession` qua `StartChildSessionAsync` sẵn có, với `childAccessCredentialId = null`, `supervisorSessionId = phiên phụ huynh`, `EntryMethod = SupervisorPick`. `AvatarId` lấy từ credential đang hiệu lực, nếu không có thì để rỗng (giống `handover`).

Response `ChildStoryHandoverDto` kế thừa `ChildSessionDto` và thêm `storyId`:

```json
{ "childProfileId": 7, "avatarId": "avatar-default", "accessToken": "...", "expiresInSeconds": 3600, "storyId": 42 }
```

### 4.3 Sau khi bàn giao (không đổi code)

- App gọi `POST /api/v1/child/reading/start` với `{ "storyId": 42 }` bằng token của bé. `StartAsync` kiểm tra lại toàn bộ luật (kể cả Hold Mode nếu có ca), tạo `ReadingSession` với `SupervisorSessionId` của phụ huynh.
- Thu hồi: `ChildSessionRevoker.RevokeBySupervisorAsync` lọc theo `SupervisorSessionId`, nên phiên `SupervisorPick` bị thu hồi khi phụ huynh "Đăng xuất mọi thiết bị", đổi mật khẩu (trừ phiên hiện tại), hoặc bị khóa tài khoản. Đúng như bảng S1 Mục 5.
- Quay về khu phụ huynh: `POST /api/v1/ParentalGate/verify` sẵn có.

## 5. Thay đổi code

| Loại | File | Nội dung |
|---|---|---|
| CREATE | `src/Core/StoryPlatform.Application/Features/ReadingSessions/ChildStoryLookup.cs` | Static `FindAsync(IUnitOfWork, childProfileId, storyId, ct)` trả `(Story, StoryVersion)?` |
| MODIFY | `.../ReadingSessions/Services/ChildReadingService.cs` | Nhánh `storyId` của `ResolveTargetAsync` gọi `ChildStoryLookup.FindAsync`. Hành vi không đổi |
| MODIFY | `.../ChildProfiles/AccessCredentials/DTOs/ChildAccessCredentialDtos.cs` | `StoryHandoverOptionsDto`, `StoryHandoverChildDto`, `StartStoryHandoverRequestDto`, `ChildStoryHandoverDto` |
| MODIFY | `.../ChildProfiles/AccessCredentials/Interfaces/IChildAccessCredentialService.cs` | `GetStoryHandoverOptionsAsync`, `StartStoryHandoverAsync` |
| MODIFY | `.../ChildProfiles/AccessCredentials/Services/ChildAccessCredentialService.cs` | Inject `IChildRoadmapEligibility`, thêm 2 hàm |
| MODIFY | `src/Core/StoryPlatform.Api/Controllers/ChildAccessCredentialController.cs` | 2 endpoint |
| MODIFY | `tests/.../ChildProfiles/AccessCredentials/ChildAccessCredentialServiceTests.cs` | Test service |
| MODIFY | `tests/.../Controllers/ChildAccessCredentialControllerTests.cs` | Test controller (delegate, role) |

DI: `IChildRoadmapEligibility` đã được đăng ký, chỉ thêm tham số constructor. Nếu file đăng ký DI hoặc harness test tạo `ChildAccessCredentialService` bằng tay thì cập nhật theo.

## 6. Test

Service, xem trước:
- Bé đọc được → `canRead = true`, `skipCode = null`.
- Bé Suspended → `profile_inactive`. Bé Archived/Deleted và bé của phụ huynh khác không có trong danh sách.
- Lệch tuổi → `wrong_age`. Lệch tuổi nhưng đã có trong lộ trình thủ công → đọc được.
- Truyện Parent của bé A: bé A đọc được, bé B là `story_unavailable`, `suggestedChildProfileId = A`.
- Hai bé cùng đọc được truyện thư viện → `suggestedChildProfileId = null`. Chỉ một bé đọc được → gợi ý bé đó.
- Truyện không tồn tại → mọi bé đều `story_unavailable`.

Service, bàn giao:
- Hợp lệ → tạo `ChildSession` với `EntryMethod = SupervisorPick`, `SupervisorSessionId` đúng, `ChildAccessCredentialId = null`. Response có `storyId` và token.
- Không phải Owner / refresh token sai / hồ sơ không Active → lỗi như `handover`, không tạo phiên.
- Truyện bị chặn (`wrong_age`, `blocked_topic`, `story_unavailable`) → `ForbiddenException` đúng câu báo, không tạo phiên.
- Bé đang có ca Hold Mode → vẫn bàn giao được (luồng này không kiểm tra Hold Mode).

Hồi quy: chạy lại `ChildReadingServiceTests` để chắc `StartAsync` không đổi. Sau đó chạy `dotnet build` và `dotnet test`.
