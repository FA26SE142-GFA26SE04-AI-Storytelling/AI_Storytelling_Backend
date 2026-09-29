# Phase 1 TTS/STT feasibility prototype

Đây là console tool **cô lập**, không chạy trong Core worker, không kết nối PostgreSQL/Supabase và không thay đổi story hay migration. Nó dùng `GeminiTtsProvider` thật từ Core để tạo audio qua Vertex (khi có `--live`) rồi thử STT V2 `short`/`long`. Report chỉ có ID mẫu và số đo; không ghi text hoặc transcript. WAV mẫu được lưu trong `output/`, thư mục này được gitignore. Chỉ sử dụng dataset mà bạn có quyền gửi đến Google Cloud.

## Chạy an toàn, không gọi cloud

```powershell
dotnet run --project tools/TtsFeasibility/TtsFeasibility.csproj -- --self-test
dotnet run --project tools/TtsFeasibility/TtsFeasibility.csproj
```

Mặc định tool đo 20 mẫu **tổng hợp** trong `dataset.sample.json`, chỉ thống kê UTF-8 bytes và read-along tokens. Dataset này không thay thế 20–50 `StorySegment` thực tế cần cho Decision Gate A.

## Chạy provider thật (có thể phát sinh phí)

Đảm bảo Vertex AI, Speech-to-Text V2, billing, quota và ADC/IAM đều được cấu hình và người vận hành đồng ý chi phí. Chạy **một mẫu trước**:

```powershell
dotnet run --project tools/TtsFeasibility/TtsFeasibility.csproj -- --live --take 1 --project <project-id>
```

Sau khi kiểm tra sample đầu và kết quả STT, mới tăng `--take` tối đa 50. `--location` mặc định `global`. Để thử STT lại trên audio WAV đã tạo mà **không gọi lại TTS**:

```powershell
dotnet run --project tools/TtsFeasibility/TtsFeasibility.csproj -- --live --stt-only --take 1 --project <project-id>
```

Report nằm ở `tools/TtsFeasibility/output/prototype-results-<timestamp>-<id>.json`; mỗi lượt chạy ghi report riêng. WAV cũ không bị ghi đè: nếu chạy live cùng sample ID lần nữa, hãy truyền `--output <folder-mới-bên-trong-repo>`; `--stt-only` vẫn đọc WAV từ folder đã chỉ định. `--dataset` và `--output` chỉ chấp nhận đường dẫn bên trong repository, không đi qua symlink/junction; sample ID chỉ chấp nhận ký tự ASCII an toàn cho tên file. Với dataset riêng, không đưa text nhạy cảm vào file được commit. Không in access token, credentials hay full HTTP error body.

## Phạm vi số liệu và giới hạn

- Report có độ dài text UTF-8, số token nguồn, latency TTS/STT, audio bytes/duration/MIME, số recognized word/offset, match/insert/delete/substitute, `textCoverage` và `coverage` cho mỗi model. `coverage` chỉ đếm token nguồn khớp **và** có cặp offset hợp lệ; `textCoverage` đếm token khớp dù thiếu offset.
- So khớp token đã lowercase/NFC và offset hợp lệ chỉ là phép đo prototype, **chưa phải thuật toán production hoặc threshold readiness**. Dữ liệu STT có thể không ổn định; cần nghe và kiểm timing thủ công.
- Tool không tự tính cost vì giá thay đổi; lấy usage thật và bảng giá tại thời điểm nghiệm thu.
- Nếu TTS audio vượt 60 giây/10 MB, tool không gọi STT đồng bộ và báo `STT_SYNC_LIMIT_EXCEEDED`. Chưa hỗ trợ batch/GCS staging hoặc chunking; Decision Gate B sẽ chốt.
- Tool chưa persist audio checkpoint; đó là Phase 3/4 sau Decision Gate A/B/C.
