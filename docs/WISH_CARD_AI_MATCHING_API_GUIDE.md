# Bé ước: Parent tạo truyện AI và AI gắn thẻ truyện có sẵn

## Phạm vi

`ParentStoryWishService.GetGenerationFormAsync` đã lấy lựa chọn thẻ của trẻ để điền form.
`CreateStoryAsync` đã gọi `IAIStoryInputService.SubmitAsync`: phụ huynh xác nhận form rồi dùng
pipeline input → outline → content → học liệu/media → xác nhận hiện có.

Phần bổ sung này là AI đối chiếu **nội dung truyện có sẵn** với danh mục WishCard,
để `StoryWishSuggestionService.ComputeAsync` có thêm dữ liệu gợi ý truyện cho điều ước.
Thuật toán xếp hạng, kiểm tra độ tuổi, quyền đọc và safety policy hiện có vẫn được sử dụng.

## Cách gọi

Moderator/Administrator đăng nhập và chọn current version của truyện Creator đã `Ready`:

```http
POST /api/v1/moderation/stories/{storyId}/wish-cards/ai-match
Authorization: Bearer <JWT>
Content-Type: application/json
```

```json
{
  "storyVersionId": 60
}
```

Backend đọc nội dung đúng phiên bản, lọc thẻ đang bật theo nhóm tuổi và bốn nhóm
`Character`, `Place`, `Challenge`, `Lesson`. Danh mục tối đa 128 thẻ, nội dung tối đa
100.000 ký tự. Truyện riêng của Parent không thuộc endpoint phân loại này.

Core gọi nội bộ `POST /api/ai/wish-cards/match` với `X-Internal-Api-Key`.
AI dùng `ILlmClient` đã cấu hình (Vertex/Gemini), một lần gọi có timeout mặc định 60 giây
(`AI:WishCardMatching:TimeoutSeconds`, giới hạn 1–120), không tự retry.
Prompt và response đầy đủ không được ghi log ở Vertex cho tác vụ này.

AI chỉ chọn ID trong danh mục, mỗi lựa chọn có `sourceQuote` nguyên văn từ body và `reason`.
Không tìm được thẻ phù hợp thì được trả mảng rỗng. Backend kiểm tra lại bằng chứng và ID.
Trích dẫn là bằng chứng hỗ trợ người quản lý, không phải chứng nhận ngữ nghĩa hay quyết định safety.

Trước khi lưu, Core lấy story lock và đọc lại tài khoản, current version, nội dung và danh mục.
Trước lời gọi AI, một transaction ngắn chụp mapping và mốc ID audit dưới cùng story lock.
Mọi thao tác thực sự thêm/gỡ mapping ghi audit `WishCardMappingsChanged` với `EntityType=Story`
và `EntityId=storyId`, cùng transaction với mapping. Khi AI trả về, Core so sánh mapping và tìm
audit thay đổi của đúng truyện sau mốc đã chụp. Vì vậy, thêm rồi gỡ thẻ trong lúc AI chạy vẫn
được phát hiện dù danh sách cuối giống ban đầu. Không giữ transaction/lock trong lúc gọi provider.
Kết quả cũ bị từ chối. Các thẻ chưa có được thêm vào `story_wish_card_matches` với `Source=Ai`;
thẻ gắn tay hoặc thẻ đã có giữ nguyên. Gọi lại không tạo trùng mapping.
Lưu mapping và audit trong cùng transaction; phản hồi gồm:

```json
{
  "success": true,
  "data": {
    "storyId": 20,
    "storyVersionId": 60,
    "addedCount": 1,
    "matches": [{"cardId": 1, "sourceQuote": "Mèo bước vào rừng.", "reason": "Mèo là nhân vật của truyện."}],
    "sourceFingerprint": "<backend-resolved-hash>"
  }
}
```

AI có thể đề xuất các thẻ khác nhau giữa các lần gọi. Endpoint bổ sung mapping,
không tự xóa mapping cũ; Moderator xem bằng `GET .../wish-cards` và gỡ thẻ không phù hợp
bằng `DELETE .../wish-cards/{wishCardId}`. Audit chỉ lưu ID, fingerprint và model;
bằng chứng trả trong response, không có cột mới để lưu bằng chứng.

## Thời điểm gợi ý cho trẻ

Đây là thao tác Moderator **chủ động gọi**; chưa tự quét toàn bộ thư viện hoặc chạy khi phát hành.
Sau khi mapping được lưu, lần trẻ gửi điều ước tiếp theo sẽ dùng dữ liệu này để xếp hạng.
Danh sách gợi ý đã chốt trước đó không được tự tính lại.
Không tự tạo truyện, gọi AI từ phiên trẻ, chuyển trạng thái xuất bản hoặc bỏ qua duyệt Moderator.

## Lỗi và kiểm thử

- `401/403`: thiếu token, sai role hoặc tài khoản bị khóa/mất quyền.
- `404`: truyện không tồn tại.
- `409`: truyện chưa Ready, sai current version hoặc nội dung/danh mục đã đổi trong lúc AI chạy.
- `409`, message `WISH_CARD_MAPPING_STALE`: mapping đã thay đổi trong lúc AI chạy.
  Frontend tải lại `GET .../wish-cards`, hiển thị lựa chọn hiện tại và để Moderator chủ động gọi lại AI;
  không tự retry request cũ hoặc âm thầm gắn lại thẻ vừa gỡ.
- `400`: danh mục/nội dung không đáp ứng giới hạn đầu vào.
- `502`: provider lỗi hoặc kết quả không qua kiểm tra; mapping cũ giữ nguyên.
- `504`: timeout; có thể chủ động gọi lại sau khi kiểm tra trạng thái hệ thống.

Ca kiểm thử: gắn bằng AI, giữ mapping Moderator, gọi lại không trùng, truyện riêng bị chặn,
actor bị thu hồi trong lúc xử lý, nội dung/current version/thẻ thay đổi trả 409,
ID bịa hoặc trích dẫn không có trong truyện bị từ chối, provider lỗi không lộ raw response,
và mapping AI được dùng bởi thuật toán gợi ý hiện có.

Không thêm entity hoặc migration. Kiểm thử giả lập không thay thế kiểm thử PostgreSQL/provider thật.

## Parent tạo truyện từ điều ước

Trẻ chọn các thẻ và gửi điều ước. Việc gửi của trẻ không tự gọi LLM. Parent có quyền
`ApproveStory` xem điều ước và chọn dùng truyện có sẵn hoặc tạo truyện mới.
Tạo mới còn yêu cầu `GenerateStory`, hồ sơ Active, consent hiện hành, policy và quota hợp lệ.

1. `GET /api/v1/parent/wishes?childProfileId=100` hoặc `GET /api/v1/parent/wishes/9`.
2. `GET /api/v1/parent/wishes/9/generation-form`: lấy `data.request`, hiển thị cho Parent chỉnh.
   Chủ đề/nhân vật/bối cảnh/bài học được điền từ thẻ; nếu trẻ để Parent chọn bài học thì
   Parent phải điền `lesson`. Độ dài mặc định lấy mức khuyến nghị theo tuổi, giới hạn bởi policy của trẻ.
3. `POST /api/v1/parent/wishes/9/create-story` với chính request đã chỉnh. Ví dụ dưới đây
   phải thay `childProfileId`, dùng đúng form và policy của hồ sơ thực tế:

```json
{
  "childProfileId": 100,
  "idempotencyKey": "story-wish-000009",
  "topic": "Chú mèo tìm đường về nhà trong rừng",
  "characterMode": "specified",
  "characters": ["Chú mèo"],
  "settingMode": "specified",
  "setting": "Khu rừng gần nhà",
  "lesson": "Bình tĩnh tìm sự giúp đỡ khi gặp khó khăn",
  "vocabularyLevel": "level_2",
  "language": "vi",
  "targetLength": 900,
  "outputMode": "learning"
}
```

Backend tự quyết định key `story-wish-{wishId:D6}`, không tin key tùy ý từ client.
Không truyền `existingStoryId`; không truyền `sourceWishId` vì đây là trường nội bộ không bind từ JSON.
Truyện mới, request Phase 1, lượt quota và các liên kết `StoryWish.GeneratedStoryId` /
`GenerationRequestId` được lưu trong cùng transaction. AI guardrail chạy sau commit, ngoài lock.
Điều ước chuyển `Accepted` nghĩa là Parent đã nhận xử lý; chưa có nghĩa truyện đã hoàn tất.

Response có `data.wish` và `data.generation`. `wish` trong list/detail cũng có thêm
`generationRequestId` để có thể mở lại tiến độ sau khi reload trang.

4. `GET /api/v1/parent/wishes/9/generation`: tiến độ Phase 1 được đọc từ ID đã lưu;
   không cần frontend tự suy ra request ID. Đây không phải tiến độ toàn bộ năm phase.
   Khi `input_accepted`, `handoffJobId` là job outline; tiếp tục API outline hiện có:
   `GET /api/v1/stories/{storyId}/outline`, xem/sửa và xác nhận outline rồi phát triển nội dung,
   học liệu/media theo pipeline chung. AI service dùng provider đã cấu hình, gồm Vertex/Gemini.
5. Nếu `input_check_failed` và `canRetry=true`, dùng endpoint hiện có
   `POST /api/v1/ai-story-input/stories/{storyId}/requests/{requestId}/retry`
   với creative input và `retryKey` mới. POST lại `create-story` cùng input chỉ trả request đã có,
   không tự gọi lại guardrail. Nếu timeout ở client, đọc endpoint tiến độ trước khi thử lại.

Replay cùng Parent và input trả lại cùng request, không tạo job/truyện hoặc trừ thêm lượt.
Đổi input sau khi điều ước đã nhận trả `409`; Parent khác không tạo thêm truyện từ cùng điều ước.
Quyền và liên kết được kiểm tra lại ở service. Nếu chưa có request, GET generation trả `409`.
Khi Parent chọn truyện có sẵn, thao tác thêm vào lộ trình và hoàn tất điều ước cũng được lưu
trong cùng transaction. Tạo mới, dùng truyện có sẵn, từ chối và worker sweep dùng chung lock
theo hồ sơ trẻ, đọc lại trạng thái sau lock; không giữ lock trong lúc gọi AI.

## Trẻ thấy gì và lúc nào

Parent xem được request/outline/bản nội dung trong lúc biên tập theo quyền hiện có.
Trẻ thấy trạng thái điều ước đang được thực hiện; chưa đọc được bản nháp do AI vừa sinh.
Worker vòng đời hiện có chỉ gắn `FulfilledStoryId` khi truyện thành `Ready`.
Việc đọc tiếp tục chịu các bước xác nhận/giao truyện, quyền đọc và policy của luồng hiện có.
AI gắn thẻ không tự mở quyền đọc, không thay Moderator kiểm duyệt thư viện công khai.
Từ chối điều ước đã nhận chỉ đóng điều ước theo nghiệp vụ hiện có, không phải API hủy job AI.

## Checklist kiểm thử tích hợp

- Tạo điều ước → lấy form → chỉnh bài học → create → GET generation → tiếp tục outline.
- POST cùng input hai lần: cùng `storyId/requestId`, quota chỉ tăng một lần.
- Hai Parent cùng bấm tạo: một liên kết được chấp nhận; không có hai truyện cho một điều ước.
- Provider guardrail lỗi: request vẫn liên kết, `canRetry/reasonCode` hiển thị đúng; không tự giao cho trẻ.
- Thiếu consent/GenerateStory/ApproveStory: bị chặn, không tạo truyện.
- Sau khi truyện đủ điều kiện Ready và được giao: trẻ đúng hồ sơ đọc được, trẻ khác không đọc được.
- Moderator gọi ai-match với truyện Creator Ready; xem bằng chứng, kiểm tra mapping;
  điều ước mới được gợi ý từ mapping và vẫn chịu eligibility hiện có.

Các test EF/SQLite xác minh persistence/rollback và ID thật trong database thử nghiệm trong bộ nhớ;
khóa advisory PostgreSQL và kết quả Vertex thật cần kiểm thử môi trường riêng.

## Tính nhất quán của thông báo nhắc hạn

Worker nhắc điều ước sắp hết hạn lưu các notification và `ExpiryWarnedAt` trong cùng transaction.
Chỉ sau commit mới phát SignalR. Lỗi lưu hoặc commit rollback cả hai và không phát realtime.
Lỗi SignalR sau commit được ghi log theo ID; dữ liệu không bị rollback và lượt sweep sau không
tạo lại thông báo đã nhắc. Parent vẫn thấy thông báo khi tải lại danh sách thông báo.
Mỗi lượt phát sau commit có giới hạn 5 giây; không có outbox hoặc retry realtime nền.
Nếu tiến trình dừng sau commit trước khi phát, thông báo vẫn nằm trong database.

Không thêm cột/bảng, không cần migration hoặc backfill audit. Các Core instance phải triển khai
đồng bộ để mọi đường ghi mapping đều phát dấu audit; không sửa mapping bằng SQL ngoài workflow.

## Dữ liệu seed để bắt đầu kiểm thử

Các file `61_wish_cards.sql` đến `64_story_wish_suggestions.sql` chứa 26 thẻ, 5 điều ước,
5 mapping Moderator và 1 gợi ý mẫu. Wish **4** thuộc child **1**: Mèo Mun, ngôi nhà,
giúp bạn và biết lắng nghe. Wish **5** thuộc child **3**, để Parent tự điền bài học.
Hai wish này bắt đầu `Pending`, chưa có request/job AI; gọi generation-form rồi create-story
bằng Parent có quyền trên hồ sơ tương ứng. Mapping `Source=Ai` chỉ được tạo khi gọi ai-match thật.

Trên database đang có dữ liệu, chỉ nạp bổ sung các file này trong transaction và cập nhật sequence;
không dùng `Database/Seed-Database.ps1` để bổ sung vì script đó TRUNCATE dữ liệu trước khi nạp lại.
Không dùng seed stories/version để ghi đè tác phẩm hoặc trạng thái đang được kiểm thử.
