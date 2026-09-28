# TTS Phase 1 — Prototype result (chưa qua Decision Gate A)

Ngày: 29/09/2026. Trạng thái: **partial / blocked by Speech-to-Text API disabled**. Đây không phải bằng chứng production-ready và chưa chọn threshold hoặc long-segment strategy.

## Đã thực hiện

- Phase 0 source freeze được ghi tại [TTS_Phase0_Source_Matrix.md](TTS_Phase0_Source_Matrix.md). Không thay đổi pipeline production, database, migration hay Supabase.
- Tạo [prototype cô lập](../../tools/TtsFeasibility/README.md) dùng `GeminiTtsProvider` hiện có, STT V2 REST `short` và `long`, tính UTF-8 bytes, token offsets và edit-distance counts. Output chỉ có metadata/số đo, không có source text/transcript/token.
- Offline preflight trên **20 mẫu tiếng Việt tổng hợp**, không phải dữ liệu `StorySegment` đại diện: UTF-8 text 20–596 bytes (trung bình 140,8); source tokens 4–103 (trung bình 24,15). Đây là preflight kỹ thuật, không dùng để chốt Gate A/B.
- Live smoke test **một mẫu** với project đã cấu hình: Vertex Gemini-TTS tạo `audio/wav` 248.250 bytes, duration khoảng **5,17 giây**, latency TTS khoảng **4,94 giây**. File WAV nằm ở `tools/TtsFeasibility/output/` (gitignored) để nghe kiểm thủ công.
- STT V2 `short` và `long` đều trả **HTTP 403 `SERVICE_DISABLED`**. Kiểm tra danh sách API đã bật bằng gcloud (read-only) không thấy `speech.googleapis.com`. Chưa có recognized words/offsets, coverage, timing stability, STT latency thành công hay cost để so sánh model.
- Verification cục bộ: `dotnet build StoryPlatform.sln --no-restore` thành công; `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --no-build --no-restore` đạt **815/815**; prototype tokenization/alignment self-test đạt. Build còn warning `NU1903` về SQLitePCLRaw trong unit-test dependency và `NU1900` do không tải được advisory feed; không sửa dependency ngoài phạm vi prototype.

## Rà soát strict sau prototype

- Sửa phép đo `coverage`: chỉ tính token nguồn khớp **và có cặp word offset hợp lệ**; `textCoverage` tách riêng khớp văn bản. Trước đó chỉ số coverage có thể đánh giá quá cao read-along dù STT thiếu timestamps.
- Khóa `--dataset`/`--output` trong repository, chặn đường dẫn đi qua reparse point, kiểm tra sample ID an toàn để không ghi file theo path traversal. Kiểm tra thiếu giá trị CLI option.
- Kiểm tra WAV đủ dữ liệu, không tính duration trên file bị cắt cụt; parser không phụ thuộc vị trí cố định của `fmt` chunk. Chuẩn hóa việc đọc lỗi STT khi payload lỗi thiếu/khác kiểu `details`.
- Report mỗi lượt có tên riêng; WAV cũ không bị ghi đè khi chạy live lại cùng ID. Chạy lại 20 mẫu offline và self-test thành công.
- `dotnet build StoryPlatform.sln --no-restore` với nullable warnings CS8600–CS8618 thành errors: **0 errors**. `dotnet test StoryPlatform.sln --no-build --no-restore`: **879/879 passed** (Core unit 815, Core integration 24, AI unit 23, AI integration 17). Test integration hiện dùng model-only/in-memory fixture, không update database ứng dụng. Hai warning NU1903/NU1900 vẫn còn; chưa thử lại live cloud do Speech-to-Text vẫn chưa được bật.

## Decision Gate A

**Chưa quyết định.** Không đủ dữ liệu để kết luận STT alignment khả thi/không khả thi cho tiếng Việt. Không được triển khai Phase 2 policy, chốt thresholds, sửa schema hoặc đưa alignment vào worker theo giả định.

Điều kiện để tiếp tục đo:

1. Chủ project xác nhận quyền sử dụng Speech-to-Text API, quota/billing và **tự phê duyệt việc bật `speech.googleapis.com`** theo [hướng dẫn chính thức](https://docs.cloud.google.com/speech-to-text/docs/setup). Agent không tự bật API trong lượt này.
2. Cung cấp/duyệt 20–50 `StorySegment` tiếng Việt thực tế được phép gửi cho Google Cloud; seed repo hiện chủ yếu rất ngắn/không dấu. Dataset tổng hợp chỉ dùng smoke test.
3. Chạy lại prototype `short`/`long`, đo duration/UTF-8/offset/coverage/latency, nghe mẫu đại diện, rồi lập Gate A decision record có số liệu. Nếu STT không đủ tốt, đánh giá aligner khác thay vì tự hạ ngưỡng.

## Chưa thực hiện

- Không bật cloud API/IAM, không tạo tài nguyên GCS, không gọi Supabase, không ghi database, không tạo/apply migration.
- Không chạy 20–50 live TTS/STT calls do STT đang disabled; không chọn threshold, `Ready` policy hay chiến lược >60 giây.
- Chưa có manual listening/timing visual QA và chưa có actual cost/usage report.

Tài liệu tham chiếu: [Google STT quotas](https://docs.cloud.google.com/speech-to-text/docs/quotas), [WordInfo](https://docs.cloud.google.com/speech-to-text/docs/reference/rest/v2/projects.locations.recognizers/recognize), [guide hiện hành](Phase5_TTS_Production_WordLevel_Agent_Guide.md).
