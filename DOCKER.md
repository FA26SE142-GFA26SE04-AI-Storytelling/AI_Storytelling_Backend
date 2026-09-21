# Chạy backend bằng Docker Compose

Compose đóng gói ba service:

- `postgres`: PostgreSQL 17, lưu dữ liệu trong named volume `postgres-data`.
- `core-api`: API nghiệp vụ chính, public tại `http://localhost:5259`.
- `ai-api`: AI service nội bộ, public tại `http://localhost:5260`.

## 1. Cấu hình môi trường

Sao chép file mẫu thành `.env`:

```powershell
Copy-Item .env.docker.example .env
```

Thay các giá trị `change-me`. Không commit `.env`. Tối thiểu phải cấu hình:

- `POSTGRES_PASSWORD`
- `JWT_SECRET_KEY` (ít nhất 32 ký tự)
- `AI_INTERNAL_API_KEY`
- `GEMINI_API_KEY` nếu sử dụng Gemini

## 2. Build và khởi động

```powershell
docker compose build
docker compose up -d
docker compose ps
```

Swagger:

- Core API: `http://localhost:5259/`
- AI API: `http://localhost:5260/swagger`

Health check:

- Core API: `http://localhost:5259/health`
- AI API: `http://localhost:5260/health`

## 3. Migration database

Compose không tự chạy migration. Điều này tránh việc container âm thầm thay đổi schema.
Sau khi `postgres` healthy, chạy migration từ repository với connection string trỏ tới
port PostgreSQL của host (mặc định `5433`):

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5433;Database=AIStorytellingDB;Username=postgres;Password=<POSTGRES_PASSWORD>"
dotnet ef database update --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api
Remove-Item Env:\ConnectionStrings__DefaultConnection
```

Chỉ chạy seed sau khi migration thành công:

```powershell
.\Database\Seed-Database.ps1 -Port 5433 -Password "<POSTGRES_PASSWORD>"
```

**Lưu ý (2026-09-21):** migration `Init` đã được đổi tên từ `20260917041029_Init` thành
`20260916000000_Init` để sửa lỗi thứ tự (`Init` phải chạy trước
`AddPhase5MediaGenerationWorkflow`, không phải sau). Nếu bạn đã từng chạy
`dotnet ef database update` thành công với ID migration cũ trước ngày này, bảng
`__EFMigrationsHistory` cục bộ của bạn vẫn còn ghi `20260917041029_Init` — EF Core sẽ coi
`20260916000000_Init` là migration mới và cố chạy lại `CreateTable`, gây lỗi
"relation already exists". Xoá sạch volume Postgres cục bộ rồi migrate lại từ đầu:

```powershell
docker compose down --volumes
docker compose up -d postgres
# rồi chạy lại bước migration ở trên
```

## 4. Theo dõi và dừng

```powershell
docker compose logs -f core-api ai-api
docker compose down
```

`docker compose down` không xóa dữ liệu. Chỉ dùng `docker compose down --volumes`
khi thực sự muốn xóa toàn bộ database Docker.
