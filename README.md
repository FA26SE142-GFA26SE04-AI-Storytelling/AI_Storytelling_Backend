# AI Storytelling Platform for Children
### Nền tảng kể chuyện thông minh cho trẻ em

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16+-336791?style=flat&logo=postgresql)](https://www.postgresql.org/)
[![Entity Framework Core](https://img.shields.io/badge/EF%20Core-8.0-blue)](https://docs.microsoft.com/ef/core/)
[![JWT Authentication](https://img.shields.io/badge/Auth-JWT%20%2B%20RBAC-green)](https://jwt.io/)
[![Swagger](https://img.shields.io/badge/Docs-Swagger%20OpenAPI-85EA2D?style=flat&logo=swagger)](http://localhost:5259)

Dự án Capstone Project: **Nền tảng kể chuyện thông minh cho trẻ em (AI Storytelling Platform for Children)**. Repository chứa hai process .NET 10 có thể triển khai độc lập: **Core Backend** sở hữu nghiệp vụ/dữ liệu và **AI Generation Service** sở hữu pipeline gọi LLM, prompt, đánh giá và refinement. Hai process giao tiếp qua DTO trong `StoryPlatform.Contracts`.

---

## 1. Tổng quan đề tài (Capstone Project Overview)

- **Tên đề tài tiếng Anh (English):** AI Storytelling Platform for Children
- **Tên đề tài tiếng Việt (Vietnamese):** Nền tảng kể chuyện thông minh cho trẻ em
- **Đối tượng & Phạm vi (Scope):** Trẻ em từ **6 đến 12 tuổi** trong môi trường gia đình và trường tiểu học.

### 1.1. Bối cảnh (Context)
Trẻ em cần những tài liệu đọc hấp dẫn, kích thích trí tưởng tượng và phù hợp lứa tuổi nhằm trau dồi ngôn ngữ, hình thành thói quen đọc sách và xây dựng nhận thức đạo đức ban đầu. Tuy nhiên, sách truyền thống thường khó cá nhân hóa theo sở thích hoặc trình độ tiếp thu riêng của từng bé. Đồng thời, phụ huynh và giáo viên mất rất nhiều thời gian để chọn lọc câu chuyện an toàn, soạn câu hỏi đọc hiểu và đánh giá mức độ tiếp thu.

Mặc dù các công cụ AI tạo sinh hiện nay có thể sáng tác truyện rất nhanh, việc để trẻ tiếp xúc trực tiếp với các chatbot mở tiềm ẩn nhiều rủi ro: nội dung vượt lứa tuổi, từ ngữ phức tạp, thiếu kiểm soát an toàn hoặc thiếu giá trị giáo dục. Vì vậy, hệ thống cần thiết lập quy tắc an toàn nội dung, sự giám sát chặt chẽ từ người lớn và một quy trình tạo truyện có định hướng.

### 1.2. Giải pháp đề xuất (Proposed Solutions)
Xây dựng nền tảng kể chuyện thông minh trên nền tảng Web kết hợp:
1. **Quản lý hồ sơ trẻ em & lớp học:** Phụ huynh và giáo viên thiết lập độ tuổi, trình độ đọc, sở thích và chủ đề cần hạn chế.
2. **Quy trình sinh truyện AI có kiểm soát:** Sử dụng Prompt Engineering và Story Templates theo các ràng buộc sư phạm; hệ thống **không** hoạt động như chatbot mở đối với trẻ em.
3. **Kiểm duyệt an toàn nội dung (Content Moderation):** Áp dụng bộ lọc an toàn đa lớp, gắn cờ cảnh báo rủi ro (bạo lực, nhạy cảm, nội dung tiêu cực) và cung cấp cơ chế duyệt (Approval Workflow) trước khi hiển thị cho trẻ.
4. **Hoạt động tương tác & Đọc hiểu:** Tự động tạo câu hỏi trắc nghiệm, danh sách từ vựng giải thích đơn giản, bài học đạo đức và thống kê điểm số/huy hiệu.
5. **Dashboard giám sát cho phụ huynh & giáo viên:** Theo dõi tiến độ đọc sách, quản lý truyện, giao bài đọc và phân tích thói quen đọc của trẻ.

---

## 2. Yêu cầu hệ thống (System Requirements)

### 2.1. Yêu cầu chức năng (Functional Requirements)

| Nhóm chức năng | Mô tả chi tiết |
| :--- | :--- |
| **1. Quản lý người dùng & Hồ sơ trẻ (User & Profile Management)** | - Đăng ký, đăng nhập, đặt lại mật khẩu cho Phụ huynh (Parent), Giáo viên (Teacher), Quản trị viên (Admin).<br>- Phân quyền dựa trên vai trò (Role-Based Access Control - RBAC).<br>- Tạo và quản lý hồ sơ trẻ em: biệt danh, nhóm tuổi, trình độ đọc, ngôn ngữ ưu tiên, chủ đề yêu thích, chủ đề hạn chế.<br>- Quản lý nhiều trẻ hoặc quản lý theo nhóm lớp học.<br>- Ghi nhật ký (Audit Log) các hành vi quan trọng: tạo truyện, phê duyệt, nộp bài trắc nghiệm, cảnh báo an toàn. |
| **2. Sáng tạo truyện & Sinh truyện bằng AI (Story Creation & AI Generation)** | - Thiết lập yêu cầu tạo truyện: độ tuổi, ngôn ngữ, độ dài, thể loại, nhân vật, bối cảnh, bài học đạo đức, cấp độ từ vựng.<br>- Sinh tiêu đề, dàn ý ngắn gọn, nội dung truyện và đoạn kết dựa trên prompts kiểm soát và story templates.<br>- Hỗ trợ tái tạo (regenerate) hoặc tinh chỉnh truyện (ngắn hơn, đơn giản hơn, giàu tính giáo dục hơn).<br>- Cho phép phụ huynh/giáo viên chỉnh sửa nội dung trước khi phát hành (Publish).<br>- Lưu vết các phiên bản truyện (Story Versioning).<br>- *(Mở rộng)* Sinh gợi ý hình minh họa / image prompts theo từng phân cảnh. |
| **3. Thư viện truyện & Trải nghiệm đọc (Library & Reading Experience)** | - Thư viện gồm truyện do AI sinh, truyện thủ công và kho truyện mẫu chuẩn.<br>- Tìm kiếm & lọc: chủ đề, độ tuổi, ngôn ngữ, cấp độ đọc, độ dài, bài học đạo đức, thời gian.<br>- Giao diện đọc sách thân thiện với trẻ em: cỡ chữ to, giao diện tối giản, thanh tiến trình đọc, bookmark, truyện yêu thích, đọc tiếp.<br>- Lưu lịch sử đọc, thời gian đọc và số truyện hoàn thành.<br>- *(Mở rộng)* Hỗ trợ Text-to-Speech (TTS) đọc truyện thành tiếng cho trẻ nhỏ. |
| **4. Hoạt động học tập & Đọc hiểu (Learning Activities & Quizzes)** | - Tự động tạo câu hỏi đọc hiểu bám sát câu chuyện (trắc nghiệm, đúng/sai, câu trả lời ngắn).<br>- Tự động trích xuất danh sách từ vựng mới kèm giải nghĩa dễ hiểu.<br>- Tạo gợi ý thảo luận bài học đạo đức sau khi đọc.<br>- Trẻ làm quiz, nhận phản hồi ngay lập tức, tích lũy điểm số, chuỗi đọc (streak) và huy hiệu khen thưởng. |
| **5. Bảng điều khiển Giám sát (Parent & Teacher Dashboard)** | - Theo dõi tiến độ đọc, số truyện đã đọc, kết quả quiz, chủ đề yêu thích, thời lượng đọc của từng bé.<br>- Phê duyệt (Approve), từ chối (Reject), chỉnh sửa hoặc lưu trữ truyện trước khi hiển thị cho trẻ.<br>- Giao truyện và bài tập đọc cho trẻ hoặc cả lớp.<br>- Cấu hình an toàn: danh sách chủ đề được phép/chặn, giới hạn độ dài, chế độ bắt buộc duyệt.<br>- Đề xuất chủ đề truyện phù hợp dựa trên lịch sử và sở thích của trẻ. |
| **6. An toàn AI & Kiểm duyệt nội dung (AI Safety & Moderation)** | - Áp dụng Prompt Guardrails ngăn chặn hành vi bẻ khóa hoặc sinh nội dung nguy hại.<br>- Tự động lọc/gắn cờ nội dung bạo lực, sợ hãi, tiêu cực, người lớn, dữ liệu cá nhân.<br>- Tính điểm tin cậy/mức độ an toàn để tự động quyết định cho phép xem, cần sửa hay bắt buộc người lớn duyệt.<br>- Cơ chế phản hồi an toàn (fallback messages) khi prompt vi phạm quy chuẩn.<br>- Ghi nhật ký sinh AI phục vụ đánh giá và nghiệm thu. |
| **7. Báo cáo & Thống kê (Reporting & Analytics)** | - Thống kê số lượng truyện sinh, duyệt, từ chối, phiên đọc hoàn tất và kết quả trắc nghiệm.<br>- Phân tích các chủ đề truyện phổ biến và tiến độ đọc theo cá nhân/lớp học.<br>- Đo lường chỉ số chất lượng AI: tỷ lệ sinh thành công, tỷ lệ gắn cờ kiểm duyệt, tỷ lệ yêu cầu tái tạo, tỷ lệ phê duyệt của phụ huynh. |

### 2.2. Yêu cầu phi chức năng (Non-Functional Requirements)
- **Hiệu năng & Khả năng mở rộng:** API xử lý nhanh chóng, phân trang (pagination) chặt chẽ cho danh sách lớn, thời gian phản hồi đạt chuẩn môi trường demo học thuật.
- **Tính toàn vẹn dữ liệu:** Lưu trữ chính xác thông tin người dùng, hồ sơ trẻ, phiên bản truyện, điểm quiz và lịch sử đọc thông qua giao dịch ACID.
- **Bảo mật:** Mã hóa mật khẩu an toàn với BCrypt, xác thực phân quyền Token JWT Bearer, lọc đầu vào chống SQL Injection và XSS.

---

## 3. Công nghệ & Kỹ thuật áp dụng (Technology Stack)

- **Ngôn ngữ & Nền tảng:** C# (.NET 10.0 SDK, ASP.NET Core Web API).
- **Kiến trúc:** Modular Clean Architecture trong monorepo; Core và AI là hai architectural boundary độc lập, dùng Contracts chung.
- **Cơ sở dữ liệu:** PostgreSQL (kết nối qua Npgsql.EntityFrameworkCore.PostgreSQL).
- **ORM & Quản lý Schema:** Entity Framework Core 8, EF Core Code-First Migrations, Design-Time Factory.
- **Xác thực & Ủy quyền:** JWT (JSON Web Tokens) với ASP.NET Core Authentication & Role-Based Authorization.
- **Mã hóa mật khẩu:** BCrypt.Net-Next.
- **Tài liệu hóa API:** Swagger / OpenAPI UI tích hợp Authorize Bearer Token.
- **Logging & Bắt lỗi:** Global Exception Handling Middleware chuẩn hóa định dạng JSON phản hồi (`ApiResponse<T>`).
- **Tích hợp AI (Roadmap):** LLM API (OpenAI/Anthropic/Gemini) với Prompt Engineering, Safety Guardrails, Text-to-Speech (TTS) & Image Generation.

---

## 4. Kế hoạch gói công việc (Capstone Task Packages)

1. **Task Package 1:** Phân tích yêu cầu, xác định phạm vi, thiết kế prototype UI/UX thân thiện với trẻ em và lập kế hoạch quản lý dự án.
2. **Task Package 2:** Thiết kế cơ sở dữ liệu, kiến trúc hệ thống, đặc tả RESTful API, hoàn thiện xác thực và phân quyền, xây dựng mô-đun quản lý hồ sơ trẻ và quản lý truyện.
3. **Task Package 3:** Phát triển giao diện Web Frontend cho phụ huynh, giáo viên, quản trị viên và chế độ đọc tương tác của trẻ.
4. **Task Package 4:** Phát triển mô-đun AI: xây dựng bộ prompt mẫu, luồng sinh truyện, chỉnh sửa truyện, tự động sinh quiz & từ vựng, hệ thống gợi ý và luồng kiểm duyệt an toàn nội dung.
5. **Task Package 5:** Phát triển tính năng theo dõi tiến độ đọc, làm bài trắc nghiệm, luồng duyệt truyện, dashboard thống kê và cơ chế thông báo.
6. **Task Package 6:** Đóng gói, triển khai (deployment), kiểm thử toàn diện, đánh giá chất lượng kết quả AI, khắc phục lỗi và hoàn thiện tài liệu báo cáo.

---

## 5. Kiến trúc mã nguồn Backend (Backend Architecture)

Backend được tổ chức theo hai boundary triển khai độc lập. Core không gọi OpenAI trực tiếp và AI không tham chiếu Domain/Infrastructure của Core:

```text
AI_Storytelling_Backend/
├── StoryPlatform.sln
├── StoryPlatform.slnx
├── src/
│   ├── Core/
│   │   ├── StoryPlatform.Api/                   # HTTP, JWT, controllers, middleware
│   │   ├── StoryPlatform.Application/           # Use cases và abstraction ports
│   │   ├── StoryPlatform.Domain/                # Business entities và enums
│   │   └── StoryPlatform.Infrastructure/        # EF Core, repository, Core → AI HTTP client
│   ├── AI/
│   │   ├── StoryPlatform.AI.Api/                # Internal AI HTTP API
│   │   ├── StoryPlatform.AI.Application/        # Generation/evaluation/refinement orchestration
│   │   ├── StoryPlatform.AI.Domain/             # AI-specific domain model
│   │   └── StoryPlatform.AI.Infrastructure/     # OpenAI adapter, prompt catalog, evaluators
│   └── Shared/
│       └── StoryPlatform.Contracts/              # Core ↔ AI request/response contracts
└── tests/                                        # Core/AI unit và integration tests
```

---

## 6. Hướng dẫn cài đặt & Khởi chạy (Getting Started)

### 6.1. Yêu cầu môi trường (Prerequisites)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PostgreSQL](https://www.postgresql.org/download/) (phiên bản 14 trở lên)
- [Git](https://git-scm.com/)

### 6.2. Cấu hình ứng dụng
Tạo file cấu hình local `src/Core/StoryPlatform.Api/appsettings.json` (file này đã được chặn bởi `.gitignore`):
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=AIStorytellingDb;Username=postgres;Password=your_password"
  },
  "JwtSettings": {
    "SecretKey": "YOUR_SUPER_SECRET_KEY_AT_LEAST_32_CHARACTERS_LONG",
    "Issuer": "StoryPlatform",
    "Audience": "StoryPlatformClient",
    "ExpiryMinutes": 120,
    "RefreshTokenExpiryDays": 7
  },
  "AIService": {
    "BaseUrl": "http://localhost:5260",
    "InternalApiKey": "local-internal-key"
  }
}
```

AI Service đọc cấu hình nhạy cảm từ environment variables:

```powershell
$env:AI__InternalApiKey = "local-internal-key"
$env:AI__OpenAI__ApiKey = "your-api-key"
$env:AI__OpenAI__Model = "gpt-5-mini"
```

### 6.3. Cập nhật Cơ sở dữ liệu (EF Core Migration)
Chạy lệnh migration để tự động sinh cấu trúc bảng trên PostgreSQL:
```powershell
dotnet ef database update --project src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj --startup-project src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj
```

### 6.4. Khởi chạy Web API
Khởi động dự án Web API:
```powershell
dotnet run --project src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj
```

Chạy AI API ở terminal riêng:

```powershell
dotnet run --project src/AI/StoryPlatform.AI.Api/StoryPlatform.AI.Api.csproj
```

Sau khi ứng dụng khởi chạy thành công, truy cập Swagger UI để kiểm thử API:
- URL: `http://localhost:5259/` (Swagger UI đang được cấu hình tại route gốc).

---

## 7. Quy ước & Chuẩn phản hồi API (API Conventions)

Mọi API phản hồi đều tuân theo cấu trúc đối tượng JSON thống nhất qua `ApiResponse<T>`:
```json
{
  "success": true,
  "message": "Thao tác thành công.",
  "data": { ... },
  "errors": null
}
```

Nếu xảy ra lỗi nghiệp vụ (`AppException`), `ExceptionHandlingMiddleware` sẽ chặn và trả về mã lỗi HTTP tương ứng (400, 401, 403, 404, 500) mà không để lộ stack trace ra ngoài:
```json
{
  "success": false,
  "message": "Chi tiết thông báo lỗi.",
  "data": null,
  "errors": [ ... ]
}
```
