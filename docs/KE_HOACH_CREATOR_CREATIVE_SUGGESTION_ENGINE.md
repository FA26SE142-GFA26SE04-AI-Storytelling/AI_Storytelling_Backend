# Kế hoạch triển khai Creative Suggestion Engine cho Creator

Ngày lập: 16/10/2026. Phạm vi: backend Core, AI service và hợp đồng tích hợp frontend.

Trạng thái: đã bổ sung source Core/AI, API và test; đã tạo/apply migration 20261016064348_AddStoryAssistanceTasksAndVersionProvenance lên AIStorytellingDB local theo yêu cầu riêng ngày 16/10/2026. Đã sửa phân loại cancellation, đo từng công đoạn, giới hạn SDK retry và điều chỉnh ngân sách chờ. Kiểm chứng bản sửa: 70 test Core, 46 test AI qua; ba lượt SupportSelection thật và Cancel/disconnect qua, còn một lượt ReviewDraft bị AI_OUTPUT_INVALID. Hợp đồng API triển khai, kết quả từng đợt test và các giới hạn còn lại nằm ở CREATOR_ASSISTANCE_API_FRONTEND_GUIDE.md. Các mô tả source "chưa có" phía dưới là mốc khảo sát ban đầu.

Bản hiệu chỉnh ngày 16/10/2026: chốt bảy hợp đồng sau review: Áp dụng/Lưu, ReviewReady, Cancel/Close, DTO liên kết bước, snapshot cũ, giới hạn wholeDraft và lịch sử provenance. Đây là hợp đồng mục tiêu để coding, chưa phải tính năng đã có.

## 1. Mục tiêu và phạm vi

Cho phép Creator gọi AI trong khi viết hoặc nhập truyện, quyết định hướng sáng tác rồi tự viết tiếp hoặc nhờ AI phát triển một đoạn thử. Creator vẫn có thể hoàn thành tác phẩm hoàn toàn bằng tay.

Prototype có ba điểm vào:

1. **Hỗ trợ phần đã chọn**: xử lý một đoạn cụ thể; hỏi làm rõ nếu mục tiêu chưa rõ.
2. **Gợi ý viết tiếp**: đề xuất 2–3 hướng từ vị trí con trỏ; Creator chọn hoặc nhập hướng riêng.
3. **Rà soát bản nháp**: trả nhận xét có vị trí; chọn nhận xét để mở nhiệm vụ hỗ trợ tương ứng.

Các bước hỏi, chọn hướng và sinh đoạn thử dùng chung cơ chế nhiệm vụ. Không xây một chatbot tự do hoặc bắt Creator thực hiện outline trước khi được sửa nội dung.

Không thuộc phạm vi: frontend implementation, gói người dùng, thanh toán/quota thương mại, thay đổi role, sinh lại toàn truyện, publish/final approval, media, quiz và pipeline phase 1–5. Tái sử dụng kỹ thuật AI không đồng nghĩa dùng lại nghiệp vụ Parent.

## 2. Cơ sở từ source hiện tại

- `src/Core/StoryPlatform.Api/Controllers/CreatorAuthoringController.cs`: đã có tạo draft, lưu manual version, đọc lịch sử, restore và import; chưa có endpoint hỗ trợ AI.
- `src/Core/StoryPlatform.Application/Features/CreatorAuthoring/DTOs/CreatorAuthoringDtos.cs`: lệnh lưu hiện nhận `operationId`, `revisionToken`, `baseVersionId`, title/content và khai báo nguồn/quyền; chưa có tham chiếu nhiệm vụ AI.
- `src/Core/StoryPlatform.Application/Features/CreatorAuthoring/Services/CreatorAuthoringService.cs`: lưu tạo version mới, đổi current, cập nhật nội dung Story và revision token; đã có concurrency/idempotency cho lệnh authoring.
- `src/Core/StoryPlatform.Domain/Entities/StoryVersion.cs`: đang có provenance, base/restored version và receipt của lệnh lưu. Không dùng các trường duyệt outline/review để đánh dấu đã áp dụng AI.
- `src/Core/StoryPlatform.Infrastructure/AI/AIStoryGenerationClient.cs`: Core gọi AI service qua HTTP với cơ chế xác thực nội bộ.
- `src/AI/StoryPlatform.AI.Application/Abstractions/LLM/ILlmClient.cs`: có abstraction sinh kết quả theo JSON schema để xem xét tái sử dụng.
- Contract liên service nằm trong `src/Shared/StoryPlatform.Contracts`; kiểm thử hiện nằm ở các project trong `tests`.

Giữ nguyên quyền truy cập authoring hiện hành, kiểm tra người dùng, quyền sở hữu Story và `Scope = Creator`. Không bổ sung điều kiện mua gói hoặc công tắc bật Creator authoring.

## 3. Các quyết định nghiệp vụ cho bản đầu

1. Story/StoryVersion tiếp tục là nguồn lưu tác phẩm; không tạo lại CreatorProduct/CreatorContentVersion.
2. Gọi AI, chọn phương án, tạo bản thử và áp dụng vào editor đều chưa tạo StoryVersion.
3. Chỉ lệnh lưu nội dung thành công mới tạo StoryVersion. Áp dụng không phải duyệt hoặc xuất bản.
4. Mỗi nhiệm vụ gắn với một snapshot editor bất biến, gồm cả nội dung chưa lưu. `baseVersionId` là ID version nền, không phải `versionNo` và không đại diện đủ cho snapshot.
5. Các phương án AI chưa được chọn không trở thành sự kiện hoặc quyết định đã xác nhận.
6. Creator có thể chọn hướng rồi tự viết, không bắt buộc gọi sinh đoạn thử.
7. Để đơn giản bản đầu, ràng buộc xác nhận có phạm vi **nhiệm vụ hiện tại**. Chưa có story-wide memory tự động dùng cho mọi nhiệm vụ; muốn dùng lại phải gửi và xác nhận lại trong nhiệm vụ mới.
8. Một Story có thể có nhiều nhiệm vụ. Mỗi nhiệm vụ chỉ có một thao tác làm thay đổi trạng thái đang chạy tại một thời điểm.
9. Tác phẩm khởi tạo thủ công tiếp tục giữ `Story.Source = Manual`; dấu vết hỗ trợ ghi ở từng version. Không đổi Source chỉ vì dùng AI.

### 3.1 Thuật ngữ Áp dụng/Lưu và đồng bộ tài liệu Creator

- **Đưa vào bản nháp (Apply)**: sửa nội dung editor, có thể undo, chưa ghi Story/StoryVersion.
- **Lưu phiên bản (Save)**: ghi nội dung cuối, provenance và receipt trong transaction.
- **Bỏ đề xuất (Discard)**: không đổi editor hoặc tác phẩm đã lưu; vẫn giữ lịch sử nhiệm vụ.

Trong phạm vi tính năng này, kế hoạch chọn quy tắc trên thay cho hợp đồng cũ “Apply tạo StoryVersion”. Review người dùng cung cấp cho biết P04 có quy tắc cũ; chưa tìm thấy nguyên bản P04 để kiểm chứng nên chưa khẳng định đã cập nhật tài liệu đó. Mốc 1 phải đối chiếu và đồng bộ P04/CC03, API contract và nghiệm thu trước coding tích hợp frontend. Không giữ đồng thời hai nghĩa của Apply hoặc để frontend chờ version mới sau Apply.

## 4. Luồng chi tiết

### 4.1 Mở nhiệm vụ và đóng băng ngữ cảnh

Frontend tương lai gửi title/content đang hiển thị, `baseVersionId`, `revisionToken`, entry point, target, mục tiêu và điều muốn giữ/tránh. Backend xác thực quyền, revision DB và giới hạn đầu vào, tự tính snapshot hash rồi lưu nhiệm vụ.

Target giới hạn theo entry point: SupportSelection nhận selection không rỗng và nhỏ hơn toàn bộ body; ContinueWriting nhận cursor; ReviewDraft nhận wholeDraft. Chưa hỗ trợ chọn nhân vật/cảnh độc lập. Có thể nêu tên nhân vật trong instruction hoặc chọn đoạn có nhân vật đó.

Language, age band và audience revision lấy từ Story phía server; không cho client ghi đè bằng metadata không kiểm chứng. Nội dung chưa lưu lấy từ editor, không thay bằng `Story.Content` trong database.

### 4.2 Xác định yêu cầu và làm rõ

Engine dùng entry point và mục tiêu explicit làm tín hiệu chính. Nếu đã rõ thì trả phương án ngay. Nếu thiếu một quyết định ảnh hưởng trực tiếp đến nhiệm vụ, trả một câu hỏi có lựa chọn, ô ý riêng và lựa chọn “Để AI đề xuất”.

Bản đầu hỏi tối đa một lượt. Sau câu trả lời hoặc lựa chọn để AI đề xuất, engine đưa phương án kèm giả định; không hỏi lặp vô hạn. Những động cơ/chi tiết suy ra từ truyện phải được gắn nhãn giả thuyết, không ghi vào confirmed decisions nếu Creator chưa xác nhận.

Lưu câu trả lời làm rõ là lệnh không gọi AI. Lệnh suggestions tiếp theo tham chiếu answer step để tạo phương án. Inference lỗi không xóa câu trả lời đã commit; retry bằng operation mới trên cùng answer step, không hỏi lại. Mỗi question chỉ có một câu trả lời được chấp nhận ở prototype; đổi câu trả lời bằng nhiệm vụ mới.

### 4.3 Đề xuất và chọn hướng

Trả 2–3 phương án ngắn, mỗi phương án gồm ID, tóm tắt, diễn biến và lưu ý về ràng buộc. Creator có thể chọn, sửa phương án hoặc gửi hướng riêng.

Backend lưu **nội dung quyết định cuối cùng** và các ràng buộc được xác nhận, không chỉ lưu option ID. Yêu cầu đổi hướng sau đó tạo một bước mới; bản thử từ hướng cũ vẫn đọc được nhưng không còn là bản thử active của hướng hiện tại.

### 4.4 Phát triển bản thử tùy chọn

Chỉ khi Creator yêu cầu, AI phát triển hướng đã chọn thành đoạn văn. Đầu vào gồm snapshot, target, quyết định đã xác nhận và ràng buộc. Đầu ra chỉ thay vùng mục tiêu, không tự viết lại toàn truyện hoặc thêm sự kiện vào phần ngoài phạm vi.

Trial chỉ `insert` tại cursor của ContinueWriting hoặc `replace` đúng selection cục bộ của SupportSelection. ReviewDraft/wholeDraft không gọi trials trực tiếp. Backend từ chối selection bao trùm body và patch ngoài target, không chỉ nhắc model trong prompt. Trả patch, văn bản thử, anchor và cảnh báo. Creator được thử lại bằng operation mới hoặc tiếp tục tự viết.

### 4.5 Áp dụng và lưu

Trước Apply, **title và toàn bộ content phải khớp chính xác snapshot nguồn**, sau đó kiểm tra anchor và cho xem so sánh. Sửa một từ ở đầu truyện cũng làm kết quả sửa cuối truyện thành cũ cho mục đích Apply. Giữ kết quả để xem/copy tham khảo và cho mở task mới từ editor hiện tại; chưa tự ghép thay đổi. Sau Apply được sửa/undo; không yêu cầu nội dung cuối khi Save còn giống snapshot.

Khi lưu, dùng API manual/versions hiện tại với `assistanceReferences` tùy chọn. Server kiểm tra các tham chiếu thuộc đúng user/story, bước đã hoàn tất và đúng loại; tạo version, ghi provenance và receipt trong cùng transaction.

Người dùng có thể sửa tiếp sau khi áp dụng. Provenance chỉ ghi nhận Creator khai đã sử dụng hỗ trợ từ một kết quả thực của hệ thống; không khẳng định toàn bộ nội dung cuối do AI tạo hoặc chứng minh đoạn gợi ý còn nguyên trong văn bản.

### 4.6 Rà soát toàn truyện

Kết quả gồm nhận xét có ID, phạm vi, trích đoạn, loại vấn đề, giải thích và hành động gợi ý. Nhận xét là đề nghị biên tập; Creator có thể bỏ qua.

Review thành công, kể cả findings rỗng, chuyển ReviewReady. Trả reviewedSnapshotHash, coverage=FullSnapshot, checksPerformed và limitations. Rỗng nghĩa chưa tìm được vấn đề theo các kiểm tra đã chạy, không chứng nhận chất lượng/an toàn/xuất bản. Nếu toàn bộ finding bị loại vì anchor sai thì trả AI_OUTPUT_INVALID, không giả vờ review không có vấn đề.

Tạo nhiệm vụ con cần parentTaskId, sourceReviewStepId, findingId và snapshot hiện tại. Parent cùng user/story, review step thành công, finding tồn tại, toàn snapshot và audience còn khớp. Nếu đã đổi: review lại hoặc mở task cục bộ độc lập bằng đoạn chọn mới, không dùng liên kết finding cũ. Finding ở mức toàn truyện cần Creator chọn đoạn/cursor cục bộ trước; không tự biến thành patch wholeDraft.

### 4.7 Bốn loại revision và mở lại nhiệm vụ

- SourceRevisionToken: revision Story lúc tạo task. DB có version mới thì kết quả cũ chỉ đọc/tham khảo, chặn inference mới và tạo task mới với revision hiện tại. Save dùng revision Story hiện tại.
- TaskRevision: chống ghi đè quyết định/trạng thái task, không thay revision Story.
- SnapshotHash: đại diện title/content lúc mở task. Trước Apply client so toàn editor với snapshot gốc, không chỉ vùng chọn. Detail API trả snapshot để client đối chiếu, không yêu cầu tự suy ra hash.
- AudienceRevision: ngữ cảnh language/age từ server. Nếu đổi thì kết quả cũ chỉ xem với nhãn cũ; chặn inference, Apply và khai references mới khi Save cho đến khi tạo task phù hợp audience hiện tại. Backend enforce inference/Save, frontend enforce Apply. Provenance đã lưu vẫn giữ, không trở thành chứng nhận phù hợp audience mới.

GET task trả snapshot có nhãn “snapshot nhiệm vụ”, cờ context cũ và revision Story hiện tại. Không tự thay editor hoặc coi snapshot là tác phẩm đã lưu. Creator chọn phục hồi snapshot vào editor hay giữ bản đang mở; phục hồi cũng chưa tạo StoryVersion. Closed là terminal: “mở lại” nghĩa xem lịch sử, muốn tiếp tục thì tạo task mới.

## 5. Vòng đời và xử lý lỗi

Trạng thái nghiệp vụ: `Open`, `AwaitingClarification`, `OptionsReady`, `ReviewReady`, `DirectionConfirmed`, `TrialReady`, `Closed`.

Chuyển trạng thái sau thành công:

- Open + suggestions SupportSelection/ContinueWriting: AwaitingClarification hoặc OptionsReady.
- Open + suggestions ReviewDraft: ReviewReady, kể cả findings rỗng. Không gọi direction/trials trực tiếp từ ReviewReady.
- AwaitingClarification + clarification-answer: giữ AwaitingClarification, commit answer step. Suggestions dùng answerStepId đó: OptionsReady.
- OptionsReady + direction: DirectionConfirmed. Option phải thuộc activeOptionsStepId hoặc dùng hướng riêng.
- DirectionConfirmed/TrialReady + direction mới: DirectionConfirmed, đổi activeDirectionStepId, xóa activeTrialStepId. Trial cũ giữ lịch sử nhưng không active cho hướng mới.
- DirectionConfirmed/TrialReady + trials: TrialReady khi thành công. Thử lại cùng hướng chỉ đổi activeTrialStepId khi kết quả mới thành công.
- Cancel operation: giữ trạng thái nghiệp vụ/kết quả đã commit trước đó, xóa active operation.
- Close từ mọi trạng thái chưa đóng: Closed, hủy active operation nếu có. Cho read và replay receipt, chặn lệnh nghiệp vụ mới.
- Lỗi/Interrupted: giữ trạng thái trước bước lỗi. Retry dùng operation mới, giữ context và quyết định đã commit.

Sau OptionsReady, prototype không gọi suggestions lại trong task đó; đổi bằng direction tùy chỉnh hoặc tạo task mới. Quy tắc giữ question/options anchors ổn định.

Trạng thái bước kỹ thuật tách riêng: `Running`, `Succeeded`, `Failed`, `Cancelled`, `Interrupted`. Không làm mất phương án/bản thử thành công trước đó khi một bước AI mới thất bại.

Mỗi mutation dùng expectedTaskRevision; tăng revision khi commit, kể cả bắt đầu/kết thúc inference hoặc hủy. Có active operation thì chặn lệnh nghiệp vụ cạnh tranh, **vẫn cho GET, cancel đúng operation và close theo kiểm tra concurrency**. Conflict yêu cầu GET trạng thái mới rồi gửi lệnh mới, không tự hủy operation khác. Người dùng vẫn viết ngoài task bình thường.

Bản đầu gọi AI có timeout trong request; không giữ transaction hoặc khóa database trong thời gian chờ model. Trước khi gọi, commit bước Running và active operation bằng compare-and-set; sau khi gọi, chỉ commit kết quả nếu operation/revision vẫn hợp lệ.

Nếu client mất kết nối sau khi đã commit kết quả, GET nhiệm vụ lấy lại kết quả. Nếu process chết khi Running, lease hết hạn thì chuyển Interrupted trong lần đọc/lệnh tiếp theo bằng cập nhật có điều kiện. Không tự gọi provider lại. Retry bước thất bại dùng operation mới; kết quả đến muộn từ lease cũ bị bỏ.

Hủy yêu cầu ghi Cancelled và vô hiệu commit kết quả cũ, đồng thời truyền cancellation best effort tới provider; không hứa provider đã ngừng hoặc không tính phí. Đóng nhiệm vụ cũng ngăn kết quả đang chạy trở thành active.

Không cam kết exactly-once inference qua HTTP. Bảo đảm một lần commit kết quả và không tạo trùng StoryVersion trong phạm vi operation idempotency.

## 6. Hợp đồng API công khai đề xuất

Các route dưới đây đã có source controller/service; chưa chạy với schema/provider thật. Dùng response envelope `ApiResponse<T>` và exception mapping hiện có.

Base route: `/api/v1/creator/stories/{storyId}/assistance-tasks`.

- `POST /`: tạo nhiệm vụ/snapshot, chưa gọi model; trả 201, task ID và task revision.
- `GET /?pageIndex=1&pageSize=20`: danh sách nhiệm vụ của Story, không trả full snapshot trong danh sách.
- `GET /{taskId}`: snapshot nguồn, revision, active operation/direction/trial, context stale flags và kết quả active; chỉ chủ sở hữu được đọc.
- `GET /{taskId}/steps?pageIndex=1&pageSize=20`: lịch sử bước, phương án và bản thử được phân trang.
- `POST /{taskId}/suggestions`: gọi engine; trả câu hỏi, phương án hoặc review findings tùy entry point.
- `POST /{taskId}/clarification-answer`: commit câu trả lời hoặc letAiPropose=true, không gọi AI; suggestions tiếp theo tham chiếu answerStepId để tạo phương án.
- `POST /{taskId}/direction`: chọn/sửa phương án hoặc nhập hướng riêng; chỉ ghi quyết định, không gọi AI.
- `POST /{taskId}/trials`: sinh đoạn thử từ direction hiện tại; không áp dụng vào Story.
- `POST /{taskId}/cancel`: hủy active operation được chỉ định.
- `POST /{taskId}/close`: đóng nhiệm vụ, giữ lịch sử.

Không thêm endpoint “apply” ghi nội dung Story. Save vẫn là `POST /api/v1/creator/stories/{storyId}/manual/versions`.

### 6.1 Ví dụ tạo nhiệm vụ

```json
{
  "operationId": "assist-na-001-create",
  "entryPoint": "ContinueWriting",
  "baseVersionId": 19,
  "revisionToken": "REVISION_THUC_TE_TU_GET_STORY",
  "editorSnapshot": {
    "title": "Tiệm sửa đồ của Na",
    "content": "Na muốn sửa xe."
  },
  "target": { "kind": "cursor", "start": 15, "length": 0 },
  "goal": "Khám phá mâu thuẫn giữa hai người bạn",
  "constraints": {
    "keep": ["Na và Nam tôn trọng nhau"],
    "avoid": ["Chế giễu bạn"]
  }
}
```

Ví dụ dùng vị trí cuối chuỗi `Na muốn sửa xe.` là 15 UTF-16 code units. Khi chạy thật, client tính vị trí theo chính text gửi lên; không hard-code offset từ tài liệu. `baseVersionId` và revision token phải lấy từ Story thực tế; Story chưa có version được phép gửi baseVersionId null.

Quy ước target: `start` tính từ 0, `length` theo UTF-16, khớp JavaScript/.NET. Cấm cắt giữa surrogate pair; không normalize whitespace/newline sau khi nhận snapshot. `wholeDraft` có start 0 và length bằng toàn bộ content. Backend kiểm tra target và lưu quoted text/anchor lấy từ snapshot, không tin quote client khai.

Hash do backend tính SHA-256 trên UTF-8 của tuple JSON cố định `[title, content]`; response trả hash để client giữ cùng snapshot. Không yêu cầu frontend tự đoán thuật toán hash. Ngữ cảnh Story như audience revision được lưu riêng và kiểm tra riêng.

### 6.2 DTO bước và kết quả

Mọi POST mutation sau tạo nhận operationId và expectedTaskRevision. DTO cụ thể:

- Create: thêm parentTaskId, sourceReviewStepId, findingId nullable; cả ba cùng có hoặc cùng không. Server xác minh theo mục 4.6.
- ClarificationAnswer: clarificationStepId, questionId và đúng một trong choiceId, customAnswer, letAiPropose=true. Phải là active question.
- Suggestions: answerStepId nullable; bắt buộc sau clarification, thuộc đúng question/task và đã commit.
- Direction: optionsStepId, optionId tùy chọn, directionText và confirmedConstraints. Chọn nguyên option thì server lấy text; sửa hoặc nhập riêng thì directionText là nội dung chốt. Không có optionId thì directionText không rỗng. Response trả confirmedDirectionText, server lưu nội dung cuối chứ không chỉ ID.
- Trials: directionStepId phải là activeDirectionStepId; trial lưu liên kết đó, không đọc lại “hướng mới nhất” sau inference.
- Cancel: operationId là ID **lệnh hủy**; targetOperationId là ID **inference cần hủy**. Target đã hoàn tất trả 409 OPERATION_NOT_RUNNING và trạng thái hiện tại; không sửa kết quả thành cancelled. Replay lệnh hủy thành công trả receipt cũ.
- Close: command envelope; lần đầu hủy active operation bằng CAS và đóng. Lệnh close mới trên Closed trả trạng thái hiện tại, không tạo sự kiện đóng lặp.

Detail DTO trả taskRevision, status, snapshot, contextStaleness, currentStoryRevisionToken, currentAudienceRevision; activeOperation (operationId, stepId, status, leaseExpiresAt) nullable; activeClarificationStepId, acceptedAnswerStepId, activeOptionsStepId, activeReviewStepId, activeDirectionStepId, activeTrialStepId. Trả payload direction/trial active để dựng lại màn hình; lịch sử khác qua steps phân trang. Client không coi detail là editor đã lưu.

Mọi kết quả hoàn tất trả task revision mới và step ID. Kết quả AI có discriminator `resultType`:

- `Clarification`: questionId, question, choices và assumptions.
- `Options`: option IDs, mô tả ngắn và constraint warnings.
- `ReviewFindings`: finding IDs, anchor, quote, nhận xét, suggestedGoal, reviewedSnapshotHash, coverage, checksPerformed và limitations. Findings rỗng vẫn có thể thành công.
- `Trial`: directionStepId, patch, snapshotHash và warnings.

Patch bản đầu chỉ có một `insert` hoặc `replace`, gắn snapshot nguồn; không hỗ trợ chuỗi patch chồng lấn. Model chỉ trả nội dung/nhận xét, backend dựng và kiểm tra anchor/offset. Nếu model chỉ ra vị trí review không khớp quote thì bỏ finding đó hoặc trả lỗi schema có kiểm soát, không đánh dấu bừa vào editor.

Lệnh AI hoàn tất trả 200; retry cùng operation đang chạy trả 202 với URL GET nhiệm vụ để kiểm tra. Đây là trạng thái của request đã bắt đầu, không phải cam kết có hàng đợi background. Không cho GET tự khởi chạy inference.

Lỗi dự kiến: 400 đầu vào/selection sai; 401 chưa xác thực; 403 tài khoản không đủ điều kiện hiện hành; 404 story/task/step không thuộc người dùng hoặc không tồn tại; 409 revision cũ, operation payload khác hoặc đang bận; 429 giới hạn kỹ thuật; 502 AI trả sai/lỗi upstream; 504 AI timeout. Chốt mã máy đọc như `TASK_REVISION_CONFLICT`, `EDITOR_CONTEXT_STALE`, `AI_OUTPUT_INVALID` cùng envelope lỗi khi coding, không phụ thuộc message tiếng Việt.

### 6.3 Mở rộng lưu version, tương thích ngược

Thêm trường tùy chọn vào `SaveCreatorContentRequest`:

```json
{
  "assistanceReferences": [
    { "taskId": 101, "stepId": 205, "usage": "DirectionUsed" },
    { "taskId": 101, "stepId": 206, "usage": "TrialUsed" }
  ]
}
```

Payload lưu vẫn phải có toàn bộ các trường hiện hành. `usage` là khai báo của Creator; server xác minh step tương ứng thực sự tồn tại, đúng Story, đúng loại và đã thành công. Không nhận client tự khai tên provider/model hoặc tự dựng metadata tin cậy.

Không gửi references thì luồng manual giữ nguyên. Không yêu cầu snapshot AI trùng nội dung cuối vì Creator được sửa sau áp dụng; concurrency DB vẫn kiểm tra revision token hiện tại. Trước save, frontend phải xử lý việc áp dụng vào editor có khớp anchor hay không. Có references nhưng không hợp lệ thì từ chối toàn bộ lệnh lưu; cho phép bỏ references và lưu thủ công bằng operation mới, không tự làm mất provenance âm thầm.

References mới phải cùng AudienceRevision hiện tại. Step Succeeded trong task Closed vẫn có thể được khai dùng nếu đúng user/story/type/audience. Step không còn active do đổi hướng vẫn được tham chiếu nếu Creator chọn dùng bản cũ; active chỉ là trạng thái giao diện. Một step có thể được tham chiếu trong nhiều version, không bị đánh dấu “đã dùng hết”.

Không có trạng thái Applied đáng tin cậy ở server vì Apply trong editor có thể undo. Save ghi khai báo usage và bằng chứng kết quả tồn tại; không suy luận đã áp dụng từ việc xem/chọn trial.

## 7. Dữ liệu đề xuất và lý do cần lưu

### 7.1 Hai entity hỗ trợ, không lưu bản tác phẩm song song

Đề xuất `StoryAssistanceTask`: StoryId, OwnerUserId, EntryPoint, SourceVersionId nullable, SourceRevisionToken, AudienceRevision, snapshot title/content/hash, target, goal, constraints, TaskRevision, Status, ActiveOperationId, LeaseExpiresAt và timestamps.

Bổ sung ParentTaskId, SourceReviewStepId, FindingId nullable; ActiveClarificationStepId, AcceptedAnswerStepId, ActiveOptionsStepId, ActiveReviewStepId, ActiveDirectionStepId, ActiveTrialStepId theo detail DTO. Liên kết phải cùng task/story phù hợp, không chỉ kiểm tra FK tồn tại.

Đề xuất `StoryAssistanceStep`: TaskId, Sequence, OperationId, RequestHash, Kind, Status, InputJson, OutputJson, ResultingTaskRevision, Provider/Model/PromptVersion khi có, ErrorCode và timestamps. Receipt response được giữ để replay. Step direction/answer là bước nghiệp vụ không có inference; trường provider nullable.

InputJson ghi source step/question/option/targetOperationId theo từng lệnh; Trial ghi directionStepId rõ ràng. Hủy/đóng có receipt riêng; tách ID lệnh khỏi ID operation bị tác động.

Hai entity phục vụ quay lại nhiệm vụ và quản lý inference/retry, không thay StoryVersion. JSON payload phải có schemaVersion, whitelist DTO và giới hạn kích thước; không lưu hội thoại/prompt vô hạn. Trạng thái và ID cần query/index lưu bằng cột, không chỉ nhét vào JSON.

Unique operation key theo owner/story cho tạo nhiệm vụ; unique `(TaskId, OperationId)` cho bước; unique `(TaskId, Sequence)` cho thứ tự; index StoryId/OwnerUserId/UpdatedAt cho danh sách. Cùng operation/payload replay kết quả trước khi kiểm tra expected revision mới; operation cũ với payload khác trả 409.

Đối chiếu sơ bộ source: StoryGenerationJob có lease, OperationKey, RequestedByUserId, nhưng Operation/Stage gắn Generation và chưa có vòng đời question/options/direction; StoryGenerationRequest theo Phase 1 validation, guardrail và handoff job. Chưa thể dùng nguyên trạng thay task sáng tác. Đề xuất giữ hai entity chuyên trách, tái sử dụng pattern lease/idempotency. Mốc 1 phải kiểm tra thêm service/config/finalizer trước khi chốt schema; không kết luận cần bỏ hoặc sửa pipeline cũ chỉ từ cấu trúc entity.

### 7.2 StoryVersion

Giữ nguyên ý nghĩa `AuthoringOrigin` đang có (`Write`, `Import`, `Restore`). Thêm metadata hỗ trợ có cấu trúc/JSON nullable chứa task ID, step ID, usage, snapshot/result hash và metadata model do server lấy từ step. Không dùng một giá trị origin duy nhất để làm mất thông tin đã nhập/khôi phục hoặc tự viết.

`CreatorVersionDto` bổ sung assistance tùy chọn, không đổi tên/xóa trường cũ. Phân biệt:

- `newReferences`: hỗ trợ được khai trong lần Save này, metadata do server lấy. Rỗng nghĩa không khai hỗ trợ mới, không kết luận truyện chưa từng dùng AI.
- `lineage`: BaseVersionId truy vết lịch sử soạn thảo; RestoredFromVersionId chỉ nguồn nội dung khi restore. Dùng quan hệ StoryVersion hiện có làm chuẩn, không cho client tự sửa.
- Nhãn tổng hợp là “có lịch sử hỗ trợ AI”, không khẳng định nội dung hiện tại do AI vì Creator có thể đã xóa/sửa toàn bộ phần đó. Không hợp nhất references cũ vào newReferences.

Ví dụ: V1 có newReferences A. V2 sửa vài chữ, không gửi references: V2.newReferences rỗng, BaseVersionId=V1; A còn ở V1 và truy xuất được qua lineage. V3 restore V1: newReferences rỗng, RestoredFromVersionId=V1, BaseVersionId=V2. Nguồn nội dung truy theo V1, không coi hỗ trợ riêng của V2 là nguồn của bản restore và không ghi inference mới.

Restore có thể lưu tóm tắt provenance nguồn dưới restoredSourceSummary để đọc lại khi task bị dọn; đây là dữ liệu kế thừa, không phải newReferences. Metadata null ở version cũ biểu thị chưa có dữ liệu theo schema mới, không khẳng định không AI. Receipt API cũ phải vẫn deserialize được.

Thông tin provenance trên version phải đủ để đọc lịch sử ngay cả khi task hết thời hạn lưu; không thiết kế FK cascade làm mất version khi dọn task. Với bản đầu, giữ nhiệm vụ cho đến khi Creator xóa Story; chưa tự động purge theo lịch. Cần thống nhất chính sách lưu/xóa snapshot trước vận hành production.

### 7.3 Migration

Khi triển khai schema sẽ cần migration tăng dần, tên đề xuất `AddStoryAssistanceTasksAndVersionProvenance`. Không sửa/xóa migration đã có. Theo AGENTS.md, việc tạo hoặc apply migration cần yêu cầu riêng cho đúng thao tác; kế hoạch này không thực hiện migration hay update database.

## 8. Tổ chức backend và AI service

Core API thêm `CreatorAssistanceController` theo convention controller hiện hành. Application đặt DTO/interface/service cạnh feature CreatorAuthoring, ví dụ thư mục `Features/CreatorAssistance`; Domain giữ task/step; Infrastructure cấu hình persistence và HTTP client.

Core chịu trách nhiệm auth, ownership, snapshot, task state, idempotency và lưu provenance. AI service chịu trách nhiệm prompt, structured generation, kiểm tra schema và trả kết quả có kiểu. Không cho AI service trực tiếp cập nhật Story hoặc quyết định người dùng đã chấp nhận phương án.

Thêm abstraction riêng `ICreatorAssistanceAiClient` bên Core và contract trong `StoryPlatform.Contracts`. AI API thêm route nội bộ theo pattern xác thực service-to-service hiện tại cho suggestions/review/trial. Không ghép thêm child profile giả để gọi nghiệp vụ Parent và không expose API key ra frontend.

Tái sử dụng `ILlmClient`, cấu hình provider, logging/error mapping và prompt catalog khi tương thích. Không giả định handlers tạo outline/content hiện có hỗ trợ nhiệm vụ Creator; không refactor rộng các phase đang chạy.

## 9. Ngữ cảnh, chất lượng và giới hạn kỹ thuật

- Input gồm nội dung snapshot, target, language/age band, goal và confirmed decisions. Trích xuất nhân vật/sự kiện là dữ liệu tham khảo, tách với điều Creator đã xác nhận.
- Đánh dấu nội dung truyện/instruction tác giả là dữ liệu; không cho chúng thay thế system policy hoặc lệnh tool. Không cung cấp tool thực thi cho model trong prototype.
- Kiểm tra đầu ra JSON schema, số phương án, target, độ dài và ràng buộc an toàn phù hợp nhóm tuổi. Kiểm tra LLM không chứng minh tuyệt đối không có mâu thuẫn; trả warnings khi chưa chắc.
- Với truyện quá dài so với context budget, trả lỗi giới hạn rõ ràng hoặc yêu cầu chọn đoạn; không cắt bỏ phần cuối âm thầm rồi báo đã rà soát toàn truyện.
- Giới hạn cấu hình riêng cho snapshot, instruction, output tokens, timeout và số request đồng thời. Dùng giới hạn văn bản manual hiện hành làm trần; mức thấp hơn phụ thuộc model được đo ở mốc tích hợp, không hard-code theo giả định.
- Không log raw snapshot, prompt, nội dung bản thử, credentials hoặc token. Log task/step ID, latency, status, provider error code và usage nếu provider trả.
- Đo riêng số task mở, option chọn, trial sinh và version lưu có references. “Đã lưu có hỗ trợ” không đồng nghĩa gợi ý được giữ nguyên; chưa có số liệu để đặt cam kết chi phí hoặc tỷ lệ chấp nhận.

## 10. Các mốc triển khai và đầu ra

### Mốc 1 — Contract và mô hình nghiệp vụ

Chốt DTO cho ba entry point, target UTF-16, trạng thái/task revision, JSON schema kết quả, provenance và mã lỗi. Viết ví dụ request/response và fake AI client dùng trong test. Đầu ra: có thể chạy kịch bản nghiệp vụ bằng test mà chưa cần provider.

Điều kiện qua mốc: đồng bộ quy tắc P04/CC03 được xác minh; contract có ReviewReady, câu trả lời được commit riêng, cancel target và lineage; chốt dùng entity riêng sau đối chiếu job/request services. Fake AI chứng minh mọi chuyển trạng thái hợp lệ và từ chối lệnh sai trạng thái. Chưa ghi DB/provider.

### Mốc 2 — Persistence và API không gọi AI

Triển khai task/step, create/list/get, direction, close/cancel, repository configuration, isolation/ownership và operation receipts. Chuẩn bị thay đổi source cho schema; tạo/apply migration chỉ sau yêu cầu riêng. Đầu ra: task có thể đóng/mở lại và không chạm StoryVersion.

Điều kiện qua mốc: create/detail/steps trả đủ trường phục hồi; task Closed chỉ đọc lịch sử, không resume. Duplicate operation không sinh trùng; sai chủ sở hữu không đọc được snapshot. DB integration chỉ chạy sau khi schema được chuẩn bị theo yêu cầu riêng; trước đó nghiệm thu logic bằng fake persistence.

### Mốc 3 — Luồng suggestions và clarification

Thêm contract HTTP nội bộ, AI handlers, prompt/schema, bounded timeout, Running lease, lỗi/retry và kết quả Options/Clarification. Hoàn thành “Gợi ý viết tiếp” trước, sau đó “Hỗ trợ phần đã chọn” trên cùng engine. Đầu ra: Creator chọn hoặc sửa hướng rồi có thể tự viết.

Điều kiện qua mốc: questions/options bám snapshot; answer commit thành công dù suggestions tiếp theo lỗi; retry không bắt trả lời lại. Cancel/Close hoạt động khi Running, late result bị loại. Chỉ task/step thay đổi, Story/StoryVersion giữ nguyên.

### Mốc 4 — Review và trial

Thêm findings có anchor, nhiệm vụ con từ finding, sinh patch từ direction, thử lại và invalidation bản thử cũ. Đầu ra: đủ ba điểm vào và bước sinh văn bản tùy chọn; vẫn chưa tự ghi tác phẩm.

Điều kiện qua mốc: review rỗng vẫn ReviewReady có coverage; task con truy đúng review step/finding; wholeDraft không tạo trial; đổi hướng xóa active trial nhưng giữ lịch sử. Không có API auto-apply ghi tác phẩm.

### Mốc 5 — Tích hợp lưu StoryVersion

Mở rộng manual save/DTO, validate references và server-side provenance trong transaction hiện có. Giữ concurrency/idempotency, restore và API manual không AI tương thích. Đầu ra: lưu phiên bản hỗ trợ AI đúng một lần và đọc lại được nguồn hỗ trợ.

Điều kiện qua mốc: API save cũ không references chạy nguyên; references sai/audience cũ bị từ chối atomically. V1 có AI, V2 không references vẫn truy được lịch sử qua base; restore truy đúng nguồn nội dung. Replay save không tạo thêm version.

### Mốc 6 — Kiểm thử tích hợp và bàn giao

Chạy regression manual + fake provider tests; sau khi schema/local API được chuẩn bị và có phạm vi gọi AI cho phép, chạy API thật xuống local với tác phẩm test riêng. Bàn giao OpenAPI, hướng dẫn frontend, kịch bản Swagger và báo cáo kết quả. Không gọi provider tính phí, seed hoặc migration trong bước viết kế hoạch này.

Điều kiện qua mốc: tất cả ca mục 11 có kết quả và bằng chứng tương ứng; Swagger mô tả snapshot/task/story/audience revision, hủy và hai lệnh answer/suggestions. Ghi rõ phần nào test fake/provider thật, không dùng kết quả fake để khẳng định chất lượng model.

Thứ tự trên thể hiện phụ thuộc, chưa phải cam kết ngày hoàn thành. Nhân lực đã biết: người dùng phụ trách backend/AI; chưa có số liệu provider và tốc độ triển khai để ấn định lịch chính xác.

## 11. Kịch bản nghiệm thu bắt buộc

1. Tạo task từ nội dung chưa lưu; model nhận đúng snapshot, không lấy nhầm bản DB.
2. Mục tiêu rõ đi thẳng Options; mục tiêu mơ hồ chỉ hỏi một lượt; “Để AI đề xuất” luôn có đường tiếp tục.
3. Chọn/sửa hướng hoặc nhập hướng riêng; trial dựa đúng quyết định mới, không dùng option cũ chưa chọn.
4. Chọn hướng rồi tự viết: lưu manual bình thường, không bắt có trial.
5. Gọi AI/bỏ gợi ý/áp dụng editor không đổi Story.Content, current version hoặc số lượng StoryVersion.
6. Rà soát trả anchor hợp lệ; finding sai quote bị loại; tạo task con không mất ràng buộc explicit.
7. Editor đổi trong khi AI chạy: kết quả không tự áp dụng; DB đổi version: save trả 409 và không mất bản đang sửa.
8. Save có references hợp lệ tạo đúng một version; current duy nhất; version trước nguyên nội dung; metadata lấy từ server.
9. Cùng operation/payload replay; khác payload bị từ chối; hai tab/task commands không ghi đè quyết định; hai save không tạo trùng version.
10. Người khác không đọc/ghi task, snapshot hoặc step; scope Parent không được đi qua Creator assistance.
11. Timeout, JSON sai, upstream lỗi, client disconnect, process interruption, cancel và late response không ghi đè kết quả đã hủy hoặc làm mất kết quả cũ.
12. Restore giữ provenance nội dung, không ghi nhận inference mới; save manual không AI/import/restore vẫn qua regression.
13. Unicode tiếng Việt, emoji, CRLF/LF và selection tại biên UTF-16 không làm sai vị trí; snapshot quá lớn bị từ chối rõ ràng.
14. Provider credentials/raw truyện không xuất hiện trong response lỗi/log; không có yêu cầu mua gói mới trong các endpoint.
15. Review không có findings vẫn ReviewReady, có coverage/checks/limitations; toàn finding sai anchor trả lỗi, không thành công rỗng giả.
16. Cancel khi Running với operationId khác targetOperationId; hủy sai target và revision cũ bị từ chối; replay cancel thành công không tác động operation khác.
17. Close khi Running thành Closed; late response không commit; mở lại chỉ đọc snapshot có nhãn, không tự ghi đè editor hoặc resume task.
18. Answer commit xong nhưng AI lỗi; GET còn answer, suggestions retry dùng cùng answerStepId, không hỏi lại và không tạo câu trả lời lặp.
19. Sửa từ đầu truyện khi AI xử lý cuối truyện: Apply bị chặn do toàn snapshot khác. Save sau khi đã Apply và chỉnh tiếp vẫn hợp lệ với Story revision hiện tại.
20. Audience đổi: chặn inference/references mới và báo stale; lịch sử version trước vẫn đọc được, không trở thành đánh giá audience mới.
21. V1 có hỗ trợ, V2 không reference mới, V3 restore V1: newReferences và lineage không lẫn nhau; không gắn nhãn “không có lịch sử AI” chỉ vì V2.newReferences rỗng.
22. Đổi direction vô hiệu active trial cũ, trial history còn đọc được; review không tạo patch thay toàn truyện; selection bao trùm body bị từ chối.

## 12. Hoàn thành khi nào?

Hoàn thành backend khi ba entry point dùng được qua API, task có thể mở lại, các quyết định dẫn đúng tới bản thử, chỉ save mới tạo version, các lỗi concurrency/retry được kiểm thử và tài liệu khớp OpenAPI thực tế.

Tài liệu bàn giao cần ghi rõ frontend tương lai chịu trách nhiệm selection/cursor, editor dirty state, so sánh bản thử, anchor checking, apply/undo và gửi nội dung cuối khi save. Đây là hợp đồng để frontend triển khai sau, không phải công việc frontend của giai đoạn này.

Cho đến khi các mốc trên được thực hiện và kiểm thử, không coi Creative Suggestion Engine là tính năng đã có chỉ vì hệ thống đã có AI Story Generation.
