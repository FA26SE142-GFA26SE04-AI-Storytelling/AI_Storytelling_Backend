# Phase 5 TTS Production — Decision-Gated Agent Guide

> Phạm vi: backend Luồng 2, Phase 5 — narration và đồng bộ read-aloud theo token tiếng Việt. Frontend, multi-speaker và thay đổi Phase 1–4 ngoài phạm vi.
>
> Trạng thái: **kế hoạch, chưa phải tính năng đã triển khai**. Không chạy tuần tự P0–P37 của bản guide cũ.
>
> Quy tắc migration: chỉ khảo sát/thiết kế; **không tạo, apply, rollback, seed hoặc reset database** nếu yêu cầu hiện tại không nêu đích danh thao tác đó.
>
> Provider narration dự kiến: Vertex Gemini-TTS, model mặc định `gemini-2.5-flash-tts`, một narrator cho mỗi `StoryVersion`. Không tự chuyển model mới hoặc fallback sang Gemini API key trong production.

## 1. Mục tiêu và ranh giới

Pipeline đích:

```text
Approved StoryVersion
  → StoryScene → StorySegment (source text bất biến)
  → text/coverage/byte gates
  → Vertex Gemini-TTS → kiểm định audio
  → lưu audio và checkpoint bền vững
  → STT word offsets → sequence alignment với source tokens
  → timing quality gate → MediaAsset(TtsAudio) Ready
  → MediaReadinessService → StoryStatus.Ready
```

`StorySegment.TextContent` là nội dung phải đọc. Transcript của STT **chỉ là bằng chứng căn chỉnh**, không bao giờ ghi đè nội dung truyện. Một `StoryScene` có thể có 1–3 `IllustrationBeat` nhưng số ảnh không quyết định số audio: một `StorySegment` có một audio hiện hành. Giữ tách segment theo đoạn văn; không tự đổi sang câu hoặc chia đoạn chỉ để tạo thêm ảnh.

Hai vòng đời phải tách biệt:

```text
Audio:      Queued → Synthesizing → AudioValidated → AudioStored
Alignment:  NotStarted → Pending → Aligning → Pass | Uncertain | Fail
```

Đây là **logical states**, không phải chỉ thị thêm ngay các giá trị vào enum `MediaStatus` hay tạo bảng. Mapping sang cột/status hiện có hoặc schema mới cần được duyệt tại Decision Gate C. Audio hợp lệ nhưng alignment lỗi phải có thể tiếp tục alignment sau worker restart mà không gọi TTS lần nữa.

## 2. Baseline source đã xác nhận

- Phase 4 approval tạo `GenerateMediaPackage` job pin `StoryVersion`; Core `MediaGenerationWorker` xử lý Phase 5. [StoryReviewService](../Core/StoryPlatform.Application/Features/StoryReview/Services/StoryReviewService.cs), [MediaGenerationWorker](../Core/StoryPlatform.Infrastructure/BackgroundServices/MediaGenerationWorker.cs).
- `StorySegmentService` chia `SceneText` theo dòng trống, giữ source offsets. `EnsureSegmentAudioAsync` gọi `ITtsProvider.GenerateAsync(segment.TextContent)`. [StorySegmentService](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/StorySegmentService.cs), [MediaGenerationService](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs).
- `AI:TTS:Provider` là khóa DI chọn provider; tên đang hỗ trợ là `GeminiTTS` và `GoogleCloudTTS`. `AI:MediaGeneration:TtsProvider` hiện không phải khóa chọn DI. Option hiện dùng `VoiceName`, `LanguageCode`; không thay bằng `VertexGeminiTTS`, `Voice`, `Language` nếu chưa đổi code và test tương ứng. [DependencyInjection](../Core/StoryPlatform.Infrastructure/DependencyInjection.cs), [TtsServiceOptions](../Core/StoryPlatform.Infrastructure/AI/TtsServiceOptions.cs).
- Gemini TTS đã có chuyển PCM thành WAV khi cần, nhưng trả `hasWordTimings=false`. `BinaryAudioQualityGate` mới kiểm binary signature/đếm timings nếu có; chưa kiểm duration, silence, transcript. [GeminiTtsProvider](../Core/StoryPlatform.Infrastructure/AI/GeminiTtsProvider.cs), [PcmAudioConverter](../Core/StoryPlatform.Infrastructure/AI/PcmAudioConverter.cs), [BinaryAudioQualityGate](../Core/StoryPlatform.Infrastructure/AI/BinaryAudioQualityGate.cs).
- `MediaAsset.WordTimings` tồn tại; chưa có cột chuyên biệt cho duration, alignment status/version, narration profile. `MediaReadinessService` mới kiểm audio `Ready` cho các segment **đã lưu**, nên scene có 0 segment là rủi ro readiness rỗng. [MediaAsset](../Core/StoryPlatform.Domain/Entities/MediaAsset.cs), [MediaReadinessService](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaReadinessService.cs).
- `IMediaStorage` hiện chỉ upload, signed URL và delete; chưa có đọc binary. `SupabaseMediaStorage` trả object path và upload với upsert. Audio path hiện có thể tái sử dụng khi attempt counter reset. `/media/package` chỉ trả scene/ảnh. `RegenerateSegmentAudioAsync` có trong concrete service nhưng chưa nằm trong public service interface/controller và chưa an toàn về ownership/version. [IMediaStorage](../Core/StoryPlatform.Application/Features/MediaStorage/Interfaces/IMediaStorage.cs), [SupabaseMediaStorage](../Core/StoryPlatform.Infrastructure/Storage/SupabaseMediaStorage.cs), [MediaGenerationController](../Core/StoryPlatform.Api/Controllers/MediaGenerationController.cs).

Baseline là tình trạng **source**; không suy ra migration đã apply, cloud credentials đã cấp hay provider thật đã hoạt động. Trước mỗi phase cần đối chiếu source lại vì worktree có thể thay đổi.

## 3. Quy tắc không được phá vỡ

1. `StoryScene.SceneText` phải bằng slice hợp lệ của `StoryVersion.Content`; `StorySegment.TextContent` phải bằng `SceneText[StartOffset..EndOffset]` theo ordinal comparison. Sai text/offset/version thì không gọi TTS và không đánh dấu `Ready`.
2. Job, scene, segment, media context và asset phải cùng current approved `StoryVersion`; kiểm lại ngay trước lưu kết quả/finalization. Asset cũ có thể giữ để audit nhưng không được làm phiên bản hiện hành `Ready`.
3. `WordTimings` chỉ gắn với **đúng audio binary/asset attempt** đã dùng để align. Tạo audio mới phải vô hiệu timing cũ; alignment-only retry giữ audio cũ và không gọi lại TTS.
4. Không tạo timestamp giả từ `duration / tokenCount`, không gán timing sai cho token không chắc chắn, không dùng STT transcript làm canonical text.
5. Không đưa credential, token, Supabase secret hoặc nội dung nhạy cảm của trẻ vào log. Public API chỉ trả playback URL phù hợp quyền truy cập, không trả secret hoặc coi object path là public URL.
6. Không sửa logic ảnh `IllustrationBeat` trừ phần readiness dùng chung. Giữ API cũ tương thích khi thêm fields; mọi thay đổi breaking phải được review riêng.

## 4. Lộ trình Phase 0 + 7 phase triển khai và 3 Decision Gate

### Phase 0 — Freeze current source

**Việc làm:** rà source/config/tests thật, đánh dấu KEEP / MODIFY / CREATE / DELETE cho `StorySegment`, `ITtsProvider`, providers, quality gate, worker, service, readiness, `MediaAsset`, storage và API. Xác nhận active solution là `StoryPlatform.sln`/`.slnx`, trạng thái migration và các thay đổi chưa commit. Không sửa business flow.

**Cấu hình baseline:** `AI:TTS:Provider=GeminiTTS`, `AI:Vertex:UseVertex=true` khi chạy Vertex, `AI:TTS:VoiceName=Kore`, `AI:TTS:LanguageCode=vi-VN`. Production validation phải chặn non-Vertex; không làm local ADC mode bị vô hiệu. Giữ một narrator/profile cho cả `StoryVersion` — chưa giả định đã có snapshot bền vững.

**Đầu ra:** source matrix và danh sách điểm chưa xác minh. Không đánh dấu Phase 0 hoàn thành chỉ vì guide này liệt kê baseline.

### Phase 1 — Feasibility prototype (trước production code)

Prototype tách khỏi business flow chính, dùng **20–50 `StorySegment` tiếng Việt thật/được phép sử dụng**, có đoạn ngắn/dài, hội thoại, tên riêng, số, ngày tháng, từ tiếng Anh, dấu câu, từ lặp. Không ghi nội dung trẻ vào log công khai. Gọi Vertex Gemini-TTS, sau đó STT V2 `vi-VN` với `short` và `long` ở region/model được tài liệu hỗ trợ. Chỉ prototype dùng provider thật sau khi môi trường, quota, chi phí và quyền gọi dịch vụ được xác nhận.

Với mỗi mẫu, đo và lưu trong dữ liệu khảo sát:

- `StorySegment` ID/nhãn ẩn danh, độ dài ký tự, **UTF-8 bytes của toàn `contents`** (text + mọi prompt/style nếu dùng).
- TTS model/voice/language, latency, MIME, sample rate, audio bytes, duration, lỗi/truncation.
- STT API/model/region, latency, recognized tokens, offsets có/không có, confidence nếu provider thực sự cung cấp.
- Source tokens, match/insert/delete/substitute, coverage, timing anomaly, từ không align, duration >60 giây và payload >10 MB.
- Chi phí ước tính trên mỗi segment/story dựa trên usage đo được, không đặt số giả.

`Prototype_Result.md` và dữ liệu đo là **đầu ra bắt buộc**, không phải code production. Có review bằng nghe và đối chiếu timing thủ công trên mẫu đại diện, vì STT word offsets không bảo đảm chính xác tuyệt đối. STT synchronous hiện bị giới hạn 1 phút hoặc 10 MB; Vertex `contents` bị giới hạn theo **byte**, không phải `.NET string.Length`. [Google STT quotas](https://docs.cloud.google.com/speech-to-text/docs/quotas), [STT WordInfo](https://docs.cloud.google.com/speech-to-text/docs/reference/rest/v2/projects.locations.recognizers/recognize), [Gemini-TTS](https://docs.cloud.google.com/text-to-speech/docs/gemini-tts).

### Decision Gate A — Alignment có khả thi?

Đọc report và mẫu nghe/nhìn; ghi quyết định `Go`, `Revise` hoặc `Stop` cùng người duyệt, ngày, số liệu và tiêu chí. Chỉ đi tiếp khi STT offsets + deterministic sequence alignment đủ ổn định cho read-aloud tiếng Việt theo mức chất lượng mà sản phẩm chấp nhận. Nếu không, đánh giá công nghệ alignment khác; **không ép STT thành forced aligner** và không tự hạ gate để được `Ready`.

### Phase 2 — Product policy

Định nghĩa domain concept `ReadAlongToken` trước khi code timing. Đề xuất v1: **orthographic whitespace token**, tức đơn vị chữ hiển thị phân cách bởi whitespace trong source text; giữ `StartOffset`/`EndOffset` nguyên gốc, phần punctuation không nhận timing riêng. Ví dụ `học sinh` được highlight `[học] sinh` rồi `học [sinh]`. Quy tắc punctuation nội bộ, viết tắt (`TP.HCM`), số/ngày, apostrophe, dấu gạch nối và Unicode phải được test và chốt trong decision record; không nhầm với “từ” ngôn ngữ học. Tên cột `WordTimings` có thể giữ vì tương thích, nhưng contract mới phải mô tả rõ đây là read-along token timing.

Chốt `AlignmentDecision = Pass | Uncertain | Fail`:

- `Pass`: đạt policy production; cho phép một số token unresolved **chỉ khi nằm trong ngưỡng đã duyệt**. Token unresolved không nhận timing giả; client sẽ không highlight token đó.
- `Uncertain` hoặc `Fail`: chặn story `Ready`; retry alignment nếu lỗi tạm thời, sau đó chuyển manual review/failure theo policy.

Chỉ lấy `MinCoverage`, `MinConfidence` (nếu confidence khả dụng/đáng tin), `MaxUnalignedTokens`, ngưỡng timing anomaly từ prototype. **Không đặt mặc định tùy ý trong guide.** Definition of Done là *mọi required segment có `AlignmentDecision=Pass`*, không phải cứng nhắc 100% token có timing.

**Đầu ra:** product decision record và version của tokenization/alignment policy; test fixtures tiếng Việt. Chưa sửa `StoryStatus` hoặc schema.

### Decision Gate B — Xử lý segment dài/overflow

Dựa trên phân bố thật, chọn **một** strategy và ghi lý do:

- **A — Bounded segment:** nếu đa số áp đảo ở dưới giới hạn, kiểm trước TTS/STT và có đường xử lý rõ cho ngoại lệ; không âm thầm bỏ qua nội dung.
- **B — Batch STT:** audio canonical ở Supabase, staging tạm sang Google Cloud Storage cho `BatchRecognize`, nhận kết quả rồi xóa staging theo lifecycle đã duyệt. Đây là thêm cloud storage/chi phí/IAM, không tự triển khai nếu chưa được phê duyệt.
- **C — Internal TTS chunks:** một `StorySegment` có nhiều TTS calls, ghép audio và offset timing; đây là thay đổi kiến trúc riêng, **không tự đổi paragraph segmentation**.

Đo cả `audioDuration > 60s`, `audioBytes > 10MB` và tổng UTF-8 bytes của Vertex `contents` so với giới hạn hiện hành. Với Vertex API, style instructions nếu có nằm cùng `contents`; không được giả định có trường prompt riêng như Cloud TTS API. Giới hạn phải được kiểm lại ở thời điểm triển khai. [STT quotas](https://docs.cloud.google.com/speech-to-text/docs/quotas), [Gemini-TTS API distinction](https://docs.cloud.google.com/text-to-speech/docs/gemini-tts).

### Phase 3 — Checkpoint, storage, persistence và API design

Thiết kế trước khi viết provider/retry:

```text
TTS → technical audio gate → immutable upload → persist AudioStored checkpoint
    → STT/alignment → timing gate → persist WordTimings + Pass → Ready
```

Nếu process chết sau upload nhưng trước DB commit, xác định cách phát hiện/thu hồi orphan object. Nếu DB checkpoint thành công nhưng alignment lỗi, worker phải lấy lại **cùng audio** từ storage và chỉ chạy alignment. Mở rộng `IMediaStorage` theo hướng `OpenReadAsync`/equivalent với kiểm soát quyền, giới hạn kích thước, timeout và `CancellationToken`; không để Application tự gọi HTTP signed URL của Supabase. Lưu object key bất biến chứa asset/attempt identity duy nhất; không dùng path phụ thuộc riêng attempt counter có thể reset. Không xóa audio cũ trong transaction trước khi audio mới được xác nhận; có lifecycle cleanup riêng.

Quyết định dữ liệu truy vấn vận hành đặt ở column hay metadata JSON. Đề xuất để review: `AudioDurationMs`, `AlignmentStatus`, `AlignmentVersion`, `NarrationProfileVersion`; `MediaAsset.WordTimings` chứa JSON có `schemaVersion`, `tokenization`, `coverage` và token offsets/timings. Cần chốt cách pin model/voice/style/pace/output format cho **toàn `StoryVersion`** và giữ ổn định qua retry/redeploy. Không tạo `WordTiming` table nếu chưa có nhu cầu query từng token. Mọi timing phải ràng buộc với đúng asset/audio revision/hash và bị vô hiệu khi audio được regenerate.

Thiết kế DTO mở rộng `/media/package` với segment order, source offsets, text, audio status, MIME, duration, alignment decision, valid timings và **signed playback URL**; `/media/progress` dùng chung readiness service để trả total/ready/pending/failed counts. Kiểm tra quyền story/current version trước khi ký URL; không trả URL cho asset chưa hợp lệ hoặc phiên bản stale.

**Đầu ra:** state transition diagram, schema/DTO contract, retry/crash matrix, backfill policy cho audio cũ **không có timing**, và kế hoạch migration riêng. Chưa tạo/chạy migration ở phase thiết kế này.

### Decision Gate C — Schema/API contract approved

Người có quyền chốt duyệt mapping logical state → entity/status, cột và JSON version, migration/backfill, chính sách asset cũ, API contract và khả năng tương thích. Nếu chưa chốt, **dừng trước source changes đòi schema**. Việc tạo hoặc apply migration vẫn cần yêu cầu đích danh theo `AGENTS.md` của repo.

### Phase 4 — Production TTS hardening

- Kiểm integrity trước mỗi provider call: offset bounds, exact ordinal slice, scene/job/current-version ownership, paragraph coverage. Non-empty scene với 0 persisted segment không được `Ready`.
- Dùng **UTF-8 byte guard cho toàn Vertex `contents`** trước request; reject/route overflow theo Decision B. Chỉ truyền story text làm nội dung cần đọc; style/pace không được làm model đọc thêm chỉ thị như lời truyện.
- Production phải buộc `GeminiTTS` + `AI:Vertex:UseVertex=true` bằng validation phù hợp môi trường. Local ADC vẫn hoạt động; không thêm API-key fallback cho production. Không đổi tên config nếu không migrate code và test cùng lúc.
- Pin narration profile cho cả version, không đổi giọng giữa các segment do runtime config thay đổi. Dùng lại `PcmAudioConverter`; kiểm WAV header, sample rate, channel, MIME, duration, silence/truncation plausibility, không đồng nhất HTTP 200 với audio hợp lệ.
- Upload object key bất biến, persist checkpoint bền vững, business retries hữu hạn; giữ transport retry/circuit breaker/rate limit theo hạ tầng hiện có, có giới hạn concurrent TTS calls.

### Phase 5 — Production alignment

- Tạo Application abstraction `IAudioTextAligner` và Infrastructure implementation STT V2 theo model/region/strategy từ Decision A/B; SDK types không rò sang Application.
- Normalize **chỉ để so sánh** (Unicode, case, punctuation, whitespace theo policy); không sửa canonical source. Dùng sequence alignment có `MATCH/INSERT/DELETE/SUBSTITUTE`, không zip mảng theo index. Từ lặp, tên riêng, số/ngày và code-switch phải có golden tests.
- `WordTiming` dùng source `ReadAlongToken` index/text/source offsets; timing đến từ STT audio evidence. Kiểm monotonicity, `start < end <= duration`, non-overlap theo policy, coverage và confidence khi thực sự có. Token không align giữ trạng thái unresolved, không có timing giả.
- Persist `AlignmentDecision`, policy/model version và WordTimings cho đúng audio đã lưu. Alignment lỗi hạ tầng không tự regenerate audio.

### Phase 6 — Recovery, retry và API

- Worker restart từ checkpoint `AudioStored` phải load đúng object rồi alignment-only retry; TTS call count = 0 trong nhánh này. Nếu audio thật sự hỏng, có quyết định rõ trước khi regenerate.
- Manual regenerate một segment qua operation an toàn: authenticate/authorize, segment thuộc story và current version, asset thuộc segment, không có job xử lý xung đột. Chỉ queue target audio, xóa/invalid timing cũ, tạo binary mới, align lại; các ảnh/segment `Ready` khác giữ nguyên. Không expose trực tiếp `RegenerateSegmentAudioAsync` hiện tại trước khi thêm ownership/version/job checks.
- `GET /api/v1/stories/{storyId}/media/package` trả playback URL được ký qua storage abstraction và timing có schema/version; `progress` trả số segment/audio/alignment còn thiếu từ **một** `MediaReadinessService`. Không mở public operation alignment-only nếu chưa có nhu cầu vận hành.
- Story chỉ `Ready` khi mọi ảnh bắt buộc đạt và mọi segment bắt buộc có valid audio + `AlignmentDecision=Pass` theo policy đã duyệt, scene coverage hợp lệ, không có blocking manual review, current version/context/job còn fresh.

### Phase 7 — Verification và release

1. Unit tests với fake provider/storage: text mismatch/out-of-bounds/stale version không gọi TTS; 0 segment non-empty không `Ready`; 1–3 beat không đổi audio count; WAV/MIME/duration/silence gate; alignment insert/delete/substitute/từ lặp/Unicode; unresolved token không fabricated timing; audio retry và alignment retry độc lập; checkpoint crash/restart; signed URL và authorization.
2. PostgreSQL integration tests **sau khi migration được duyệt và apply rõ ràng trong test DB**: unique constraints, checkpoint transaction, concurrent claims, backfill, invalidation khi regenerate, readiness query và stale finalization.
3. Supabase Storage recovery test trên môi trường được phép: upload → persist → process restart → read same object → align; retry không overwrite object cũ, signed URL phát được, orphan cleanup theo policy. Không dùng secret trong log/client.
4. Vietnamese golden dataset: dấu thanh, tên riêng, số/ngày, viết tắt, dấu câu, hội thoại, từ lặp, tiếng Anh xen tiếng Việt, ngắn/dài. So kết quả khi thay model/voice/STT/tokenization.
5. Real E2E được cấp quyền riêng: Approved version → Vertex TTS → audio gate → STT → alignment/timing gate → Supabase → PostgreSQL → package API → readiness → `StoryStatus.Ready`. Nghe và kiểm timing thủ công, kiểm một alignment failure không gọi lại TTS. Mock tests **không thay thế** real E2E.
6. Build/test bằng active solution sau code changes: `dotnet build StoryPlatform.sln`, `dotnet test StoryPlatform.sln`. Không chạy database update như bước verify mặc định. Rollout model mới cần golden regression, đánh giá phát âm, latency/chi phí và kiểm soát chuyển phiên bản.

## 5. Báo cáo bắt buộc sau mỗi phase/gate

```text
Phase/Gate:
Source/files inspected:
Files changed/created:
Evidence and measurements:
Decision + approver (nếu là gate):
Behavior before/after:
Tests/build result:
Known gaps and risks:
Migration required/approved/applied?:
External verification performed/remaining?:
```

Không đánh dấu phase hoàn thành chỉ vì build xanh. Nếu Gate A/B/C chưa có dữ liệu hoặc chưa được duyệt, báo trạng thái **chưa quyết định** và dừng các phần phụ thuộc; không tự chọn threshold, cloud strategy hay schema.

## 6. Definition of Done production

- Vertex Gemini-TTS dùng IAM/ADC-compatible auth, một narration profile đã pin cho mỗi version; input là exact `StorySegment.TextContent`, không rewrite.
- Scene/segment coverage và current-version ownership hợp lệ; byte/duration overflow có policy đã chốt.
- Audio qua technical gate, lưu object bất biến, có checkpoint đọc lại được, retry không ghi đè và không tạo duplicate.
- Alignment với source `ReadAlongToken`, có decision `Pass` theo ngưỡng **được đo và duyệt**; token unresolved không có timestamp bịa.
- Alignment retry không tạo lại audio; manual audio regeneration vô hiệu timing cũ và chỉ tác động target segment.
- Readiness chung chặn story khi audio/alignment/ảnh còn thiếu, khi 0 segment bất thường, hoặc khi version stale.
- Package API trả audio/timing và signed playback URL có authorization; progress phản ánh readiness thật.
- Unit, PostgreSQL integration và real Vertex/STT/Storage E2E đạt; voice/timing tiếng Việt được nghe và đối chiếu thủ công. Metrics/logs tách TTS và alignment, không lộ secrets hay nội dung trẻ không cần thiết.

## 7. Tài liệu nguồn và lưu ý triển khai

- [Google Cloud Speech-to-Text V2 quotas](https://docs.cloud.google.com/speech-to-text/docs/quotas): synchronous 1 phút hoặc 10 MB; batch cần Cloud Storage URI.
- [Speech-to-Text V2 WordInfo](https://docs.cloud.google.com/speech-to-text/docs/reference/rest/v2/projects.locations.recognizers/recognize): offsets chỉ khi bật `enableWordTimeOffsets`, độ chính xác có thể thay đổi.
- [STT V2 supported languages/models](https://docs.cloud.google.com/speech-to-text/docs/speech-to-text-supported-languages): kiểm lại `vi-VN` và region/model trước prototype.
- [Gemini-TTS API guide](https://docs.cloud.google.com/text-to-speech/docs/gemini-tts): phân biệt Vertex API `contents` với Cloud TTS API có trường `text`/`prompt` riêng; kiểm giới hạn byte/audio ở thời điểm thực hiện.
- [TTS current source survey](TTS_Current_Implementation_Survey.md): baseline chi tiết, không thay thế kiểm tra source khi bắt đầu mỗi phase.

Guide này thay **thứ tự tuyến tính P0–P37** của bản cũ bằng các phase/gate có bằng chứng. Nó không phê duyệt sẵn schema, migration, cloud resources, threshold hay provider-real test.
