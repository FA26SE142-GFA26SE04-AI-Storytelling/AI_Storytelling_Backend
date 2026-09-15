# Phase 1 — Lấy cấu hình trẻ và chính sách an toàn để kiểm tra đầu vào

Ngày lập: 15/09/2026. Trạng thái: kế hoạch triển khai, chưa thay đổi code hoặc database.

## 1. Mục tiêu và phạm vi

Parent/Teacher chọn một Child Profile. Core lấy cấu hình có hiệu lực từ hồ sơ và Safety Policy, cung cấp context cho form, kiểm tra input theo context đó, rồi bàn giao snapshot đã được chấp nhận sang bước sinh outline.

Kế hoạch này hoàn thiện feature `AIStoryInput` đang tồn tại. Không tạo lại module Phase 1, không tạo entity child/safety trùng trong AI service. Sinh nội dung, ảnh, TTS và xuất bản nằm ngoài phạm vi.

Nguồn đối chiếu: `diagram_2_phase1.solve.txt`, source `AIStoryInput`, các entity Core và consumer `OutlineService` hiện tại. Các quy tắc mới bên dưới là đề xuất triển khai; không khẳng định đã được BRD phê duyệt nếu chưa có bằng chứng trong source/tài liệu.

## 2. Hiện trạng đã kiểm tra

- `AIStoryInputService.ResolveContextAsync` đã đọc ChildProfile, LearningProfile, SafetyPolicy, SafetyPolicyCategory, chính sách tổ chức và FavoriteTopic.
- Có kiểm tra quyền GenerateStory, quan hệ giám sát, Child Profile Active; có gộp category theo mức nghiêm ngặt hơn và lấy giới hạn độ dài nhỏ hơn giữa hai policy.
- Có GET context, submit, progress, retry; có fingerprint, accepted snapshot và durable handoff job.
- Trước khi chấp nhận input, service đã đọc lại context/quyền và đối chiếu fingerprint.
- `OutlineService` đã đọc accepted/context snapshot để dựng GenerateOutlineRequest. README Phase 1 còn nói consumer chưa triển khai; cần cập nhật mô tả này.
- `ResolveContextAsync` chưa kiểm tra ConsentRecorded/OrgConsentRecord; chưa đưa các ngưỡng safety/readability và dấu nhận diện policy vào snapshot.
- MaximumLength hiện lấy từ policy, chưa áp trần theo reading level. ReadingLevel chấp nhận 1–5, VocabularyLevel có thể chọn bất kỳ level_1 đến level_5.
- Guardrail hiện kiểm tra Topic, Characters, Setting, Lesson; Genre chưa được đưa vào contract kiểm tra.
- Guardrail là rule-based: marker injection, email/số điện thoại và từ khóa category. Nó chưa đánh giá ngữ nghĩa hay độ tuổi; không có từ khóa trùng không chứng minh input an toàn.
- Logic từ khóa hiện chỉ xét match đầu tiên và có ngoại lệ theo tiền tố như “phòng chống”. Cần kiểm thử trường hợp một đoạn giáo dục che khuất đoạn nguy hiểm phía sau.

## 3. Nguồn dữ liệu và trách nhiệm

### 3.1. Core là nguồn cấu hình có thẩm quyền

- ChildProfile: ChildProfileId, AgeBand, Language, Status, Scope, OrganizationId. Nickname chỉ phục vụ hiển thị form khi cần; không mặc định đưa định danh trẻ vào prompt.
- LearningProfile: ReadingLevel và ComprehensionGoal. LearningProfileTopic cung cấp FavoriteTopic để cá nhân hóa; sở thích không tự trở thành danh sách chủ đề được phép.
- SafetyPolicy: MaxStoryLength, RequiredApprovalMode, ConsentRecorded, ConsentRecordedAt, ConsentPolicyVersion, ParentalGateEnabled, SafetyScoreThreshold, ReadabilityScoreThreshold.
- SafetyPolicyCategory + ContentCategory: category code, tên hiển thị, trạng thái và rule.
- OrgSafetyPolicyTemplate + OrgSafetyPolicyCategory: mức trần và ràng buộc bổ sung cho hồ sơ tổ chức.
- OrgConsentRecord: trạng thái đồng ý và RevokedAt khi áp dụng cho hồ sơ tổ chức.
- SupervisionRelationship + SupervisionPermission: kiểm tra người gửi được tạo truyện cho đúng trẻ.

ComprehensionThresholdPercent và ComprehensionWindowSize phục vụ đánh giá đọc hiểu về sau; không biến thành điều kiện loại ý tưởng đầu vào. Safety/readability threshold được chuyển tới đúng bước đánh giá output khi contract hỗ trợ, không so sánh trực tiếp với điểm input chưa được định nghĩa cùng thang đo.

### 3.2. Phân chia Core và AI

Core truy cập database qua repository/UnitOfWork hiện có. AI nhận dữ liệu tối thiểu qua contract nội bộ; không truy cập trực tiếp bảng Core và không tự tạo migration cho hồ sơ trẻ.

Client gửi ChildProfileId, ý tưởng và lựa chọn hợp lệ. Core tự quyết định tuổi, reading level, policy, owner và trạng thái; payload client không thể ghi đè các giá trị này.

AI hoặc moderation provider, nếu tích hợp, chỉ trả kết quả kiểm tra có cấu trúc. Core quyết định accepted/blocked/failed và việc tạo job dựa trên tất cả điều kiện bắt buộc.

## 4. Luồng đích

1. GET context: xác thực người dùng và quyền trên trẻ; đọc profile, learning, consent/policy; tính cấu hình có hiệu lực; trả giá trị mặc định và lựa chọn hợp lệ. Không tạo dữ liệu nghiệp vụ.
2. Submit: đọc lại cấu hình hiện hành, không sử dụng context từ form như nguồn tin cậy; chuẩn hóa input và kiểm tra field/giới hạn.
3. Sau validation cơ bản: giữ cơ chế tạo draft/request và fingerprint hiện có; raw creative input chưa đạt chỉ tồn tại trong bộ nhớ xử lý.
4. Input guardrail: kiểm tra tất cả trường tự do theo policy và ngữ cảnh trẻ; trả Allow, Block, Inconclusive hoặc Error.
5. Trước finalize: kiểm tra lại quyền, child, story, consent, fingerprint và attempt token. Context thay đổi thì không bàn giao.
6. Với Allow hợp lệ: cùng một transaction lưu accepted snapshot, trạng thái request và handoff job. Story vẫn Draft; Phase 1 không tạo StoryVersion.
7. Outline consumer lấy dữ liệu sáng tác từ snapshot. Nếu quyền/consent/policy đã bị thu hồi hoặc siết lại khi job chờ, phải chặn/revalidate theo quy tắc thống nhất, không lặng lẽ thay snapshot để tiếp tục sinh.

Snapshot giúp tái hiện quyết định, nhưng không cho phép bỏ qua một lần thu hồi quyền. Việc đọc lại context trước transaction hiện có cũng chưa tự bảo đảm chống thay đổi policy đồng thời; cần kiểm chứng cơ chế concurrency ở cả luồng đọc và luồng sửa policy.

## 5. Quy tắc cấu hình có hiệu lực

### 5.1. Điều kiện hồ sơ và consent

- Thiếu LearningProfile hoặc SafetyPolicy: trả lỗi cấu hình cụ thể; không dùng dữ liệu mẫu để tiếp tục.
- Hồ sơ tổ chức thiếu OrganizationId hoặc policy bắt buộc: trả lỗi cấu hình, không tự hạ xuống personal policy.
- Đề xuất yêu cầu consent hợp lệ trước khi submit tạo truyện. Cần thống nhất hồ sơ tổ chức dùng OrgConsentRecord thay thế hay bổ sung ConsentRecorded của SafetyPolicy; không tự bắt buộc đồng thời cả hai nếu nghiệp vụ không quy định.
- ParentalGateEnabled là cấu hình yêu cầu xác nhận; không phải bằng chứng người lớn vừa xác thực. Nếu gate áp dụng ở thao tác tạo truyện, tích hợp bằng chứng xác thực từ cơ chế hiện có thay vì coi boolean là đủ.
- Category bị inactive/missing nhưng vẫn được policy tham chiếu cần có kết quả rõ ràng: lỗi cấu hình hoặc áp quy tắc quản trị đã chốt. Không âm thầm làm mất một restriction.

### 5.2. Gộp policy

- Cùng category: Blocked ưu tiên hơn Restricted, Restricted ưu tiên hơn Allowed.
- Allowed ở một nguồn không được ghi đè Blocked/Restricted ở nguồn khác.
- Danh sách Allowed không mặc định là whitelist tuyệt đối: cần quyết định rõ hành vi với category chưa được cấu hình.
- Độ dài: EffectiveMaximumWords = min(trần reading level, trần cá nhân hợp lệ, trần tổ chức nếu có).
- MaxStoryLength cá nhân bắt buộc phải hợp lệ; giá trị 0/âm không được che khuất bằng trần hợp lệ của tổ chức.
- Approval mode: AlwaysManual được ưu tiên nếu một policy bắt buộc duyệt thủ công. Cấu hình này được giữ tới bước duyệt; không có tác dụng bỏ qua input guardrail.
- Null threshold không tự hiểu là 0 hoặc tự cho phép; sử dụng default đã được thống nhất và ghi nguồn của giá trị.

### 5.3. Reading level và vocabulary

Theo bảng người dùng đã cung cấp, đơn vị là số từ:

- Level 1: target khuyến nghị 400–600; hard maximum 700.
- Level 2: target khuyến nghị 600–900; hard maximum 1.100.
- Level 3: target khuyến nghị 900–1.300; hard maximum 1.600.

Target range là khuyến nghị, hard maximum là trần bắt buộc. TargetLength vượt EffectiveMaximumWords phải trả lỗi; không âm thầm cắt. Khi safety maximum thấp hơn target range, form hiển thị trần thực tế và không đề xuất giá trị vượt trần.

Source hiện cho phép ReadingLevel 4–5 nhưng chưa có bảng độ dài tương ứng từ người dùng. Trước triển khai cần xác nhận 1–3 có cùng thang đo với entity hay không và cách xử lý 4–5; không tự gộp về level 3 hoặc tạo số liệu mới.

Default vocabulary đang là level tương ứng ReadingLevel. Cần chốt các mức được phép người lớn chọn; tách khái niệm độ khó từ vựng khỏi giới hạn nội dung an toàn. Không tự giả định vocabulary luôn phải bằng ReadingLevel.

## 6. Các hạng mục triển khai theo thứ tự

### P1-01 — Chốt contract và quy tắc nghiệp vụ

- Chốt các điểm tại mục 5: consent theo scope, gate, category ngoài cấu hình, level 4–5, vocabulary override và ý nghĩa/thang đo threshold.
- Ghi quyết định cạnh kế hoạch; đánh dấu quy tắc chưa chốt là prerequisite, không dựng default tùy ý.
- Giữ route và error envelope hiện có; chỉ bổ sung field cần thiết theo hướng tương thích.
- Hoàn thành khi mỗi rule có nguồn cấu hình, điều kiện lỗi và case kiểm thử cụ thể.

### P1-02 — Hoàn thiện resolver trong Core

- Mở rộng ResolveContextAsync ngay trong feature hiện tại; tái sử dụng access guard và các abstraction sẵn có khi tương thích quyền GenerateStory.
- Kiểm tra scope, cấu hình thiếu/sai, consent theo quyết định đã chốt và xung đột category.
- Áp giới hạn reading level và policy; tính danh sách vocabulary hợp lệ.
- Tạo fingerprint xác định: sort/deduplicate category, chuẩn hóa chuỗi; bao gồm giá trị cấu hình tác động đến quyết định. Không đưa timestamp “thời điểm đọc” vào fingerprint khiến mọi lần resolve đều khác nhau.
- Hoàn thành khi GET/submit/retry/finalize dùng cùng phép tính context và test cho ra cùng kết quả.

### P1-03 — Bổ sung DTO và snapshot JSON

- AIStoryInputContextDto: bổ sung target range, trần hiệu lực và lựa chọn hợp lệ; trả trạng thái thiếu cấu hình qua cơ chế lỗi hiện có. Không trả toàn bộ thông tin consent nhạy cảm ra form.
- AIStoryInputContextSnapshot: đề xuất thêm version cấu trúc JSON, SafetyPolicyId, OrgPolicyId khi có, ConsentPolicyVersion, dấu nhận diện consent hiệu lực, các ngưỡng cần downstream và cấu hình độ dài đã resolve.
- Tái sử dụng ContextSnapshotJson trong StoryGenerationRequest; đây là thay đổi payload JSON, chưa phải thêm cột entity.
- Phải test deserialize snapshot cũ. Với field an toàn bắt buộc nhưng thiếu ở snapshot cũ: dừng job có reason rõ hoặc yêu cầu kiểm tra lại; không mặc định coi consent hợp lệ.
- ComprehensionGoal nếu đưa vào prompt cũng là free text cần kiểm tra. Interests lấy từ DB không tự được xem là chỉ dẫn hệ thống đáng tin cậy.
- Hoàn thành khi snapshot giữ đủ bằng chứng quyết định và OutlineService đọc được version hỗ trợ.

### P1-04 — Hoàn thiện input guardrail

- Mở rộng InputGuardrailRequest để có Genre, age band, language và các ràng buộc cần kiểm tra.
- Quét tất cả trường tự do: Topic, Genre, từng Character, Setting, Lesson; đánh giá cả thông tin profile tự do được đưa vào prompt.
- Chuẩn hóa Unicode/whitespace, giới hạn độ dài/số lượng trước xử lý. Kiểm thử tiếng Việt có/không dấu, nhiều match trong một trường và nội dung nguy hiểm nằm sau đoạn giáo dục.
- Không coi tiền tố “phòng chống” là ngoại lệ an toàn tuyệt đối. Trường hợp không chắc trả Inconclusive hoặc chuyển bước đánh giá ngữ nghĩa.
- Tái sử dụng IInputGuardrail; nếu cần moderation ngữ nghĩa, triển khai adapter trong tầng phù hợp theo hạ tầng provider hiện có. Không thêm service/broker riêng chỉ cho nhiệm vụ này.
- Khi môi trường yêu cầu đánh giá ngữ nghĩa mà provider chưa sẵn sàng: Error/Inconclusive, không tự chuyển thành Allow rule-based.
- Rule-based vẫn hữu ích để kiểm tra xác định, nhưng chỉ nghiệm thu khả năng đánh giá tuổi/ngữ nghĩa sau khi adapter thật và bộ test đã được kiểm chứng.

### P1-05 — Vòng đời request, retry và handoff

- Bảo toàn idempotency, attempt token và transaction finalize sẵn có.
- Chỉ retry lỗi kỹ thuật có thể phục hồi; Block không được tự retry cùng input để tìm Allow.
- Không giữ transaction xuyên lời gọi provider. Kiểm tra race policy/consent thay đổi giữa recheck và commit; nếu cần lock, luồng cập nhật policy cũng phải tham gia cùng cơ chế.
- Bổ sung kiểm tra trước dispatch ở OutlineService cho quyền/consent/policy hiện hành; dữ liệu sinh vẫn phải gắn với snapshot đã kiểm tra. Thay đổi policy dẫn đến revalidation/request mới theo quyết định, không tự nâng quyền.
- Mọi nhánh không đạt đều không tạo handoff mới; raw rejected input không xuất hiện trong DB/log/error.

### P1-06 — Test và cập nhật tài liệu tích hợp

- Mở rộng test ngay tại AIStoryInputServiceTests, RuleBasedInputGuardrailTests và test OutlineService hiện có.
- Kiểm tra contract Core–AI và snapshot cũ/mới; cập nhật README Phase 1 để phản ánh consumer Phase 2 đã có.
- Ghi hướng dẫn FE sử dụng GET context, lỗi trường, retry và xử lý INPUT_CONTEXT_CHANGED; không tạo frontend mới trong repo backend.
- Kiểm thử concurrency/unique/transaction bằng môi trường DB kiểm thử được phê duyệt; repository mock không chứng minh tính nguyên tử thực tế.

## 7. Bộ case nghiệm thu

1. GET context đúng trẻ, đúng quyền; không tạo Story/request/job.
2. Child inactive, tài khoản không hợp lệ, thiếu quyền hoặc quyền bị thu hồi: từ chối đúng chỗ.
3. Thiếu learning/safety/org policy; giá trị trần 0/âm; category reference hỏng: lỗi cấu hình có reason.
4. Consent thiếu/chưa chấp nhận/bị thu hồi theo scope: không submit hoặc handoff thành công.
5. Personal Allowed + Org Blocked; Personal Restricted + Org Allowed: giữ quy tắc nghiêm ngặt hơn.
6. Với level 1–3: trần đúng 700/1.100/1.600; trần policy thấp hơn thắng; bằng trần được chấp nhận, vượt một từ bị từ chối.
7. Level 4–5 xử lý theo quyết định chính thức; target range không bị biến thành hard minimum ngoài nghiệp vụ.
8. Client giả age/policy/approval; language khác profile; vocabulary ngoài danh sách: không áp dụng giá trị trái context.
9. Genre chứa injection/PII/category cấm phải được kiểm tra như các trường còn lại.
10. Một input có cả đoạn “phòng chống” và yêu cầu nguy hiểm phía sau không được bỏ lọt vì match đầu tiên.
11. Nội dung phù hợp giáo dục được phân biệt với cổ súy hành vi nguy hiểm bằng kiểm tra ngữ cảnh; không chỉ dựa vào tiền tố.
12. Restricted, response sai schema, timeout, provider thiếu cấu hình: không tạo job; reason và canRetry đúng loại.
13. Consent/policy thay đổi lúc form mở, lúc guardrail chạy, sát finalize hoặc trong thời gian job chờ: không tiếp tục dùng quyền đã thu hồi.
14. Hai submit cùng key; retry cùng key; kết quả attempt cũ về muộn: không nhân bản draft/request/job.
15. Accepted/context snapshot được handoff đúng; snapshot cũ thiếu field mới có hành vi rõ ràng, không tự Allow.
16. Lỗi transaction không để accepted request thiếu handoff; không ghi raw input bị chặn vào log/database.

## 8. Migration và tổ chức commit

Kế hoạch ưu tiên sử dụng entity hiện có và mở rộng DTO/record/JSON. Dự kiến không cần tạo entity, thêm cột, sửa Init hoặc ModelSnapshot cho các hạng mục này.

Vấn đề migration history đã phát hiện trước đây là công việc độc lập. Không dùng kế hoạch Phase 1 này làm lý do restore/xóa migration hay reset/seed database. Đối chiếu EF model với snapshot cũng không đủ chứng minh database thực tế đã có đủ bảng/cột.

Nếu triển khai phát hiện thiếu lưu trữ hoặc cần concurrency column mới: liệt kê chính xác entity, field, lý do và migration tăng dần đề xuất để Migration Owner xét riêng. Không chỉnh Init dùng chung.

Chia commit theo nhóm: resolver và test; DTO/snapshot và contract; guardrail và test; handoff/revalidation và test; tài liệu. Mỗi nhóm phải build được và không mang migration ngoài phạm vi.

## 9. Điều kiện hoàn tất

- Hoàn tất P1-01 trước khi coi các default nghiệp vụ là chính thức.
- Unit test liên quan và build solution thành công; kiểm chứng contract consumer với snapshot cũ/mới.
- Các case quyền, consent, policy merge, boundary length, mọi trường input, retry và policy change có bằng chứng test.
- Nêu rõ adapter kiểm tra ngữ nghĩa đã chạy thật hay chỉ test double; không báo lọc an toàn theo tuổi hoàn chỉnh khi mới có từ khóa.
- Database integration test chỉ báo đạt khi chạy trên đúng schema và môi trường được phê duyệt; ghi riêng blocker migration nếu còn.
- Handoff chỉ xảy ra với input đạt, đúng context và đúng quyền; trạng thái truyện vẫn Draft tại cuối Phase 1.
