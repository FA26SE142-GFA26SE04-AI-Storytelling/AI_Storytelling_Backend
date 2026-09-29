# Khảo sát logic TTS hiện tại — Luồng 2, Phase 5

Ngày khảo sát: 28/09/2026. Phạm vi: source code hiện tại của Core API/Application/Infrastructure. Đây là khảo sát tĩnh; chưa gọi provider thật, chưa xác nhận audio trong database hoặc Supabase của môi trường đang chạy. Cấu hình runtime có thể bị ghi đè bởi biến môi trường hoặc secret store.

## Kết luận nhanh

- TTS chạy **sau khi truyện được duyệt ở Phase 4** và media job của Phase 5 được worker nhận. Đây là chức năng của **Core**, không phải endpoint trong `StoryPlatform.AI.Api`.
- Đơn vị đọc là `StorySegment`: mỗi đoạn văn trong `StoryScene` có **một** `MediaAsset` loại `TtsAudio`. Số `IllustrationBeat`/ảnh của scene không làm tăng số audio và không làm thay đổi đoạn văn được đọc.
- Provider được chọn qua `AI:TTS:Provider`. Cấu hình mặc định trong Core API hiện chọn `GeminiTTS`, model `gemini-2.5-flash-tts`, voice `Kore`, ngôn ngữ `vi-VN`. Source cũng có lựa chọn `GoogleCloudTTS`.
- Core gửi đúng `StorySegment.TextContent` cho provider, nhưng quality gate hiện **không xác minh lời nói thực tế** có khớp từng từ với văn bản. Nó kiểm tra file audio và, nếu có, số lượng word timings.
- `GET /api/v1/stories/{storyId}/media/progress` cho biết số audio `Ready`/segment còn thiếu; `GET .../media/package` hiện **chỉ trả scene và ảnh**, chưa trả audio/segment URL để client phát theo đoạn.

## 1. Điểm bắt đầu và thứ tự xử lý

1. Khi người duyệt approve một story ở `ContentReview`, `StoryReviewService` pin `StoryVersion` hiện hành vào `StoryGenerationJob` với operation `GenerateMediaPackage`, trạng thái `Pending`; story chuyển `Approved`. [StoryReviewService.cs](../Core/StoryPlatform.Application/Features/StoryReview/Services/StoryReviewService.cs)
2. `MediaGenerationWorker` (hosted service trong Core) lấy job nếu `AI:MediaGeneration:WorkerEnabled=true`. Khi claim, service yêu cầu story ở `Approved` hoặc `MediaProcessing`, kiểm tra phiên bản và chuyển job/story sang trạng thái xử lý. [MediaGenerationWorker.cs](../Core/StoryPlatform.Infrastructure/BackgroundServices/MediaGenerationWorker.cs), [MediaGenerationService.cs](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs)
3. Service xây `MediaContext`, xác định các `StoryScene`, rồi đi theo thứ tự scene. **Trong từng scene**, nó lập/xử lý các beat ảnh trước, sau đó mới gọi `EnsureAudioSegmentsAsync`. Nếu ảnh của scene lỗi và job dừng, TTS của scene đó chưa được gọi. Các scene trước có thể đã có audio `Ready`.
4. Sau tất cả scene, `FinalizeAsync` đánh giá độ đầy đủ và chỉ chuyển story sang `Ready` khi kiểm tra đạt.

Nguồn chính: [MediaGenerationService.cs](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs), các phương thức `ProcessClaimedAsync`, `EnsureAudioSegmentsAsync`, `FinalizeAsync`.

## 2. Văn bản nào được đưa vào TTS?

`StorySegmentService.CreateSegmentsForScene` đọc `StoryScene.SceneText` và tách tại dòng trống `\n\n` hoặc `\r\n\r\n`. Nó bỏ qua phần chỉ có khoảng trắng, giữ `SegmentOrder` và offset `[StartOffset, EndOffset)` tính trong scene. `TextContent` bằng đúng chuỗi con ở offset đó; dấu xuống dòng đơn không phải ranh giới segment. Nếu cả scene không có dòng trống thì thông thường đó là một segment. [StorySegmentService.cs](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/StorySegmentService.cs), [StorySegment.cs](../Core/StoryPlatform.Domain/Entities/StorySegment.cs)

Service lưu các segment chưa có, sau đó duyệt theo `SegmentOrder` và gọi `ITtsProvider.GenerateAsync(segment.TextContent)`. Đây là ranh giới quan trọng: `IllustrationBeat` dùng để chọn khoảnh khắc minh họa; TTS **không** đọc theo beat và **không** nhận riêng `MediaContext` trong chữ ký provider. [MediaGenerationService.cs](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs), [IMediaGenerationServices.cs](../Core/StoryPlatform.Application/Features/MediaGeneration/Interfaces/IMediaGenerationServices.cs)

## 3. Provider và xác thực

`ITtsProvider` được đăng ký trong [DependencyInjection.cs](../Core/StoryPlatform.Infrastructure/DependencyInjection.cs):

- `GeminiTTS`/`Gemini` → [GeminiTtsProvider.cs](../Core/StoryPlatform.Infrastructure/AI/GeminiTtsProvider.cs). Provider gửi văn bản và cấu hình voice/language trong yêu cầu sinh `AUDIO`. Khi `AI:Vertex:UseVertex=true`, HTTP client dùng OAuth/ADC qua Vertex; khi false, provider cần Gemini API key. Kết quả PCM được chuyển thành WAV nếu cần; metadata trả về `hasWordTimings=false`.
- `GoogleCloudTTS`/`GoogleCloud` → [GoogleCloudTtsProvider.cs](../Core/StoryPlatform.Infrastructure/AI/GoogleCloudTtsProvider.cs). Provider dùng Google Cloud Text-to-Speech client và SSML. Khi bật `EnableWordTimings`, nó yêu cầu SSML marks/timepoints rồi xây metadata `wordTimingsJson`. Client dùng credentials file được cấu hình hoặc ADC.

Giá trị mặc định từ [appsettings.json](../Core/StoryPlatform.Api/appsettings.json) là `GeminiTTS` / `gemini-2.5-flash-tts` / `Kore` / `vi-VN`, `EnableWordTimings=false`, `MaxTextLength=5000`, timeout 30 giây. `AI:MediaGeneration:TtsProvider` cũng xuất hiện trong cấu hình, nhưng lựa chọn DI thực tế đọc **`AI:TTS:Provider`**; không nên coi hai khóa này là cùng một cơ chế chọn provider khi thay cấu hình.

## 4. Kiểm định, lưu trữ và trạng thái

Sau khi provider trả binary, `BinaryAudioQualityGate` kiểm tra:

1. Có bytes audio.
2. Magic bytes thuộc định dạng audio được nhận diện.
3. Nếu provider báo có word timings, số mục timing bằng số mark dự kiến từ tokenizer.

Đây là kiểm định cấu trúc, **không phải** kiểm tra phát âm, ngữ nghĩa, ngôn ngữ thực tế, transcript hoặc độ khớp từng từ. Với Gemini mặc định (`hasWordTimings=false`), bước so sánh timing được bỏ qua. [BinaryAudioQualityGate.cs](../Core/StoryPlatform.Infrastructure/AI/BinaryAudioQualityGate.cs)

Khi qua gate, service upload binary qua `IMediaStorage`. DI hiện ánh xạ interface này sang [SupabaseMediaStorage.cs](../Core/StoryPlatform.Infrastructure/Storage/SupabaseMediaStorage.cs). Đường dẫn audio có dạng `{storyId}/v{mediaContext.Revision}/audio-s{sceneIndex}-{segmentOrder}-a{attempt}.{extension}`. `MediaAsset.Url` lưu **object path** mà storage trả về, không phải signed URL hay bytes; record cũng lưu MIME, word timings nếu có, provider, model, số lần thử, trạng thái `Ready` và thời điểm hoàn thành. Upload sử dụng `x-upsert=true`. [MediaGenerationService.cs](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs), [MediaAsset.cs](../Core/StoryPlatform.Domain/Entities/MediaAsset.cs)

Mỗi segment có tối đa một `MediaAsset` TTS hiện hành theo unique index `(StorySegmentId, Type)` cho record chưa soft-delete. [MediaAssetConfiguration.cs](../Core/StoryPlatform.Infrastructure/Persistence/Configurations/MediaAssetConfiguration.cs)

## 5. Retry và điều kiện hoàn tất

- TTS asset `Ready` được bỏ qua khi job chạy lại. Asset chưa `Ready` được thử theo `AssetMaxAttempts` (cấu hình hiện là 3; code chặn trong khoảng 1–5). Lỗi binary rỗng/sai định dạng được xem là xác định, chuyển `ManualReview` và dừng sớm. Lỗi gate khác có thể thử lại; hết lượt thì chuyển `ManualReview` và fail job.
- HTTP provider còn có tầng retry vận chuyển riêng; cần phân biệt với số lượt thử asset ở tầng business.
- Endpoint `POST /api/v1/stories/{storyId}/media/retry` chỉ áp dụng khi story đang `MediaProcessing` và media job `Failed`; nó đưa các asset chưa `Ready` về `Queued`, giữ nguyên asset `Ready`.
- `MediaReadinessService` yêu cầu mọi `IllustrationBeat` có ảnh `Ready`/validation `Passed`, mọi **segment đã lưu** có audio `Ready`, và không có asset `ManualReview`. Khi đạt, service đánh dấu story `Ready` và job `Completed`. [MediaReadinessService.cs](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaReadinessService.cs)

## 6. API hiện có và các khoảng trống cần lưu ý

- [MediaGenerationController.cs](../Core/StoryPlatform.Api/Controllers/MediaGenerationController.cs) hiện có `GET /progress`, `GET /package`, `POST /retry`, `POST /illustration-beats/{beatId}/regenerate`. `progress` có `ReadyAudio` và `MissingAudioSegmentIds`, nhưng chưa có tổng số segment cần audio như một trường riêng. `package` chỉ trả `SceneMediaItem.Illustrations`, không có danh sách segment/audio/URL.
- `RegenerateSegmentAudioAsync` tồn tại trong `MediaGenerationService` nhưng chưa được expose qua controller/interface hiện hành. Phương thức này chỉ reset asset về `Queued`; bản thân nó không xếp lại job, và chưa kiểm tra segment/asset thuộc story hoặc version được truyền. **Không nên coi đây là endpoint regenerate TTS an toàn/hoàn chỉnh.**
- Quality gate của Google Cloud TTS hiện đối chiếu **số lượng** timing. Provider xây một timing cho mỗi word token ngay cả khi mark tương ứng không có trong response (giá trị bắt đầu mặc định `0.0`), nên kiểm tra số lượng không chứng minh timepoints có thật hoặc đúng thời gian.
- `MediaReadinessService` xét các segment **đã tồn tại**; nếu một scene không tạo được segment nào thì điều kiện “mọi segment có audio” đúng một cách rỗng. Worker thông thường tạo segment trước khi finalization, nhưng readiness chưa tự xác minh số segment kỳ vọng/coverage văn bản.
- Đường dẫn chứa `attempt`, song khi `POST /retry` reset `AttemptCount` về 0, một lần upload lại ở lượt business retry có thể dùng lại cùng object path; storage hiện bật upsert. Vì vậy chưa thể khẳng định mọi phiên bản audio thất bại/cũ luôn được bảo toàn.

Các khoảng trống trên là nhận định từ source hiện tại, **không phải** kết quả thử nghiệm provider thực hoặc bằng chứng dữ liệu sản xuất bị sai.

## 7. Kiểm thử đã thấy và kiểm chứng còn cần

[MediaGenerationServiceTests.cs](../../tests/StoryPlatform.UnitTests/MediaGenerationServiceTests.cs) có test với provider/storage giả cho thứ tự xử lý, văn bản gửi vào TTS, hai beat trong cùng đoạn vẫn giữ audio theo segment, deterministic audio failure và retry giữ asset `Ready`. Chúng xác nhận logic orchestration ở mức unit, không chứng minh giọng đọc ngoài đời đúng văn bản.

Để nghiệm thu TTS end-to-end cho một truyện cụ thể, cần: chạy Core worker với provider và storage thực; đối chiếu `StorySegment.TextContent`/offset với file nghe được cho từng segment; xác minh MIME, object path, phát audio qua signed URL; thử retry khi một audio lỗi; kiểm tra story không `Ready` trước khi mọi audio bắt buộc sẵn sàng. Bài kiểm thử này chưa được thực hiện trong khảo sát tĩnh.
