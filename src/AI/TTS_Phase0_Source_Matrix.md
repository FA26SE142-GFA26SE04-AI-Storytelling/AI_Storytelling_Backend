# TTS Phase 0 — Source freeze và implementation matrix

Ngày khảo sát: 29/09/2026. Phạm vi: code trong repository, không kiểm tra trạng thái PostgreSQL, Supabase hay Google Cloud đang chạy. Phase 0 của [decision-gated guide](Phase5_TTS_Production_WordLevel_Agent_Guide.md); chưa qua Decision Gate A/B/C.

## KEEP

- Phase 4 approval tạo media job pin `StoryVersion`; worker Core xử lý Phase 5. Không chuyển TTS sang `StoryPlatform.AI.Api`. [StoryReviewService](../Core/StoryPlatform.Application/Features/StoryReview/Services/StoryReviewService.cs), [MediaGenerationWorker](../Core/StoryPlatform.Infrastructure/BackgroundServices/MediaGenerationWorker.cs).
- `StorySegmentService` tách paragraph và giữ offset `[StartOffset, EndOffset)`; một segment tương ứng một TTS asset. `IllustrationBeat` độc lập với TTS. [StorySegmentService](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/StorySegmentService.cs), [MediaGenerationService](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs).
- `ITtsProvider` abstraction và `GeminiTtsProvider` hiện có. `PcmAudioConverter` đã đóng gói raw PCM thành WAV; không viết lại bộ chuyển đổi chỉ để phục vụ prototype. [IMediaGenerationServices](../Core/StoryPlatform.Application/Features/MediaGeneration/Interfaces/IMediaGenerationServices.cs), [GeminiTtsProvider](../Core/StoryPlatform.Infrastructure/AI/GeminiTtsProvider.cs), [PcmAudioConverter](../Core/StoryPlatform.Infrastructure/AI/PcmAudioConverter.cs).
- `MediaAsset.WordTimings`, provider/model metadata và `IMediaStorage` abstraction. [MediaAsset](../Core/StoryPlatform.Domain/Entities/MediaAsset.cs), [IMediaStorage](../Core/StoryPlatform.Application/Features/MediaStorage/Interfaces/IMediaStorage.cs).
- Active solution `StoryPlatform.sln` và `.slnx`, Core unit tests cho Gemini TTS, segment service và media orchestration. [GeminiTtsProviderTests](../../tests/StoryPlatform.UnitTests/Infrastructure/AI/GeminiTtsProviderTests.cs), [StorySegmentServiceTests](../../tests/StoryPlatform.UnitTests/StorySegmentServiceTests.cs).

## MODIFY — chỉ sau các Decision Gate tương ứng

- `GeminiTtsProvider` và `TtsServiceOptions`: guard UTF-8 byte length của Vertex `contents`, narration profile pinning và production-only Vertex validation. Hiện chỉ có `MaxTextLength` theo `string.Length`, mặc định 5000; `AI:TTS:Provider` mới là DI source of truth. [GeminiTtsProvider](../Core/StoryPlatform.Infrastructure/AI/GeminiTtsProvider.cs), [DependencyInjection](../Core/StoryPlatform.Infrastructure/DependencyInjection.cs).
- `BinaryAudioQualityGate`: đang chỉ kiểm rỗng/magic bytes/count timing nếu có; cần audio parsing, duration, silence/truncation tests. [BinaryAudioQualityGate](../Core/StoryPlatform.Infrastructure/AI/BinaryAudioQualityGate.cs).
- `MediaGenerationService`: hiện tạo audio rồi upload và đánh `Ready` trong cùng lượt; alignment failure sau này cần checkpoint `AudioStored` bền vững, retry alignment riêng, immutable path và text/version integrity gates. [MediaGenerationService](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs).
- `MediaReadinessService`: đang xét các segment đã lưu, chưa chặn scene không có segment; chưa có alignment/timing readiness. [MediaReadinessService](../Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaReadinessService.cs).
- `IMediaStorage`/`SupabaseMediaStorage`: chưa đọc lại binary; upload đang upsert. Chỉ mở rộng khi checkpoint design được duyệt. [IMediaStorage](../Core/StoryPlatform.Application/Features/MediaStorage/Interfaces/IMediaStorage.cs), [SupabaseMediaStorage](../Core/StoryPlatform.Infrastructure/Storage/SupabaseMediaStorage.cs).
- API media: `/package` mới trả scene/illustration; `/progress` mới có `ReadyAudio`/`MissingAudioSegmentIds`; regenerate segment audio concrete method chưa có controller/interface và chưa check ownership/version đầy đủ. [MediaGenerationController](../Core/StoryPlatform.Api/Controllers/MediaGenerationController.cs), [MediaGenerationModels](../Core/StoryPlatform.Application/Features/MediaGeneration/Models/MediaGenerationModels.cs).

## CREATE — không làm trước prototype/gate

- Phase 1 prototype **cô lập**, dataset hợp lệ, kết quả đo và report; không cấy STT vào worker production trước Gate A.
- Sau Gate A/B: `ReadAlongToken` policy, deterministic sequence alignment, STT V2 adapter, audio/timing gates, crash/retry tests.
- Sau Gate C: schema/status/DTO/migration được duyệt riêng nếu các cột hiện có không đủ cho checkpoint và operational queries. Không tự tạo/apply migration trong nhiệm vụ này.

## DELETE / DEPRECATE — chưa xóa source trong Phase 0

- Xem xét loại bỏ `AI:MediaGeneration:TtsProvider` vì DI chọn theo `AI:TTS:Provider`, nhưng phải kiểm tra consumer và migration cấu hình trước khi xóa.
- `GoogleCloudTtsProvider` vẫn là source hiện có; không xóa chỉ vì production plan chọn Gemini. Không tự thay đổi provider ở môi trường khác.

## Rủi ro và việc chưa xác minh

1. Repo seed `story_versions` phần lớn là ví dụ rất ngắn/không dấu; chưa phải bộ mẫu 20–50 segment tiếng Việt đại diện. Cần dữ liệu truyện được phép dùng hoặc fixture được chủ sản phẩm chấp nhận trước khi Gate A.
2. Chưa xác minh project/region/quota/billing/IAM của Vertex và STT. `appsettings` cho biết cấu hình mong muốn, không chứng minh request thật thành công.
3. Chưa kiểm tra database migration đã apply; worktree có migration beat đang chưa commit. Không chạy `dotnet ef database update`, reset hoặc seed.
4. STT synchronous bị giới hạn 1 phút/10 MB; `vi-VN` và model/region phải xác nhận từ tài liệu và prototype thật. [STT quotas](https://docs.cloud.google.com/speech-to-text/docs/quotas), [supported languages](https://docs.cloud.google.com/speech-to-text/docs/speech-to-text-supported-languages).
5. Decision Gate A/B/C chưa có đủ evidence/approval. Không chọn threshold, chiến lược segment dài hoặc schema bằng suy đoán.
