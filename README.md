# Hướng Dẫn Kiến Trúc Backend C# (.NET 8)
## AI Storytelling Platform - 3-Layer Architecture with Modular BLL & Generic Repository

Dự án backend được xây dựng hoàn chỉnh theo chuẩn **Kiến trúc 3 Layer (3-Tier)** kết hợp:
1. **Presentation Layer (Web API):** Quản lý Controllers, Middlewares, Authentication, Swagger.
2. **Business Logic Layer (BLL):** Thiết kế theo **Modular / Feature-based** (tách biệt theo từng domain module: `Auth`, `Story`...).
3. **Data Access Layer (DAL):** Áp dụng **Generic Repository Pattern** và **Unit of Work Pattern** kết hợp Entity Framework Core 8.

---

## 1. Cấu trúc thư mục dự án

```text
backend/
├── StoryPlatform.sln
├── StoryPlatform.slnx
├── StoryPlatform.DAL/                           # [TẦNG 3: DATA ACCESS LAYER]
│   ├── Configurations/                          # Fluent API Entity Configurations
│   │   ├── UserAccountConfiguration.cs
│   │   └── StoryConfiguration.cs
│   ├── Context/                                 # EF Core DbContext & DesignTime Factory
│   │   ├── ApplicationDbContext.cs
│   │   └── DesignTimeDbContextFactory.cs        # Hỗ trợ migration từ CLI không cần chạy API
│   ├── Entities/                                # Domain Entities & Base Entity
│   │   ├── BaseEntity.cs                        # Id, CreatedAt, UpdatedAt, IsDeleted
│   │   ├── UserAccount.cs
│   │   ├── Story.cs
│   │   └── Enums/                               # UserRole, AccountStatus, StoryStatus, StorySource
│   ├── Interfaces/                              # Chỉ giữ các Interfaces cốt lõi
│   │   ├── IGenericRepository.cs
│   │   └── IUnitOfWork.cs
│   ├── Repositories/                            # Chỉ giữ các Implementations cốt lõi
│   │   ├── GenericRepository.cs
│   │   └── UnitOfWork.cs
│   ├── ConnectionStringHelper.cs                # Lớp helper lấy ConnectionString linh hoạt
│   ├── DependencyInjection.cs                   # Đăng ký DAL vào IServiceCollection
│   └── StoryPlatform.DAL.csproj
│
├── StoryPlatform.BLL/                           # [TẦNG 2: BUSINESS LOGIC LAYER]
│   ├── Common/                                  # Dùng chung cho toàn bộ BLL
│   │   ├── Exceptions/                          # AppException, NotFoundException, BadRequestException...
│   │   ├── Models/                              # ApiResponse<T>, PagedResult<T>, PageRequest
│   │   └── Security/                            # BCrypt PasswordHasher, JwtTokenGenerator, JwtOptions
│   ├── Modules/                                 # [TÁCH THEO TỪNG MODULE NGHIỆP VỤ]
│   │   ├── Auth/                                # [MODULE AUTHENTICATE]
│   │   │   ├── DTOs/                            # LoginRequestDto, RegisterRequestDto, AuthResponseDto...
│   │   │   ├── Interfaces/                      # IAuthService
│   │   │   └── Services/                        # AuthService
│   │   └── Story/                               # [MODULE STORY]
│   │       ├── DTOs/                            # StoryDto, CreateStoryDto, UpdateStoryDto, FilterDto...
│   │       ├── Interfaces/                      # IStoryService
│   │       └── Services/                        # StoryService
│   └── DependencyInjection.cs                   # AddBusinessLogicLayer()
│
└── StoryPlatform.API/                           # [TẦNG 1: PRESENTATION LAYER]
    ├── Controllers/
    │   ├── BaseApiController.cs                 # Base Controller trả về ApiResponse<T>, helper GetCurrentUserId()
    │   ├── AuthController.cs                    # POST /register, POST /login, GET /me
    │   └── StoryController.cs                   # CRUD /api/v1/story, Pagination, Publish, Soft-delete
    ├── Middlewares/
    │   └── ExceptionHandlingMiddleware.cs       # Bắt lỗi toàn cục và trả về JSON chuẩn
    ├── Extensions/
    │   └── ServiceExtensions.cs                 # JWT Bearer, Swagger Bearer Auth, CORS
    ├── appsettings.json                         # ConnectionStrings, JwtSettings
    └── Program.cs                               # Entry point, cấu hình Middleware pipeline
```

---

## 2. Điểm nổi bật trong kiến trúc

### 2.1. Tách Module độc lập ở tầng Business Logic Layer (BLL)
- Thay vì dồn chung toàn bộ DTOs và Services vào một thư mục khổng lồ, BLL được chia theo từng module tính năng: `Modules/Auth`, `Modules/Story`,...
- Mỗi module tự bao gói:
  - **DTOs**: Dữ liệu trao đổi đầu vào/đầu ra của module.
  - **Interfaces**: Hợp đồng dịch vụ (`IAuthService`, `IStoryService`).
  - **Services**: Triển khai logic nghiệp vụ cụ thể.
- **Dễ dàng mở rộng thêm module mới:** Khi muốn thêm module như `ChildProfile`, `ReadingSession`, `Assignment`, chỉ cần tạo thư mục mới `Modules/ChildProfile` mà không ảnh hưởng tới các module khác.

### 2.2. Generic Repository & Unit of Work ở tầng DAL
- **`IGenericRepository<T>`**: Cung cấp đầy đủ các thao tác CRUD tiêu chuẩn:
  - `GetByIdAsync(id)`
  - `GetAllAsync()`
  - `FindAsync(predicate)`
  - `GetPagedAsync(pageIndex, pageSize, filter, orderBy, includeProperties)`
  - `AddAsync()`, `AddRangeAsync()`
  - `Update()`, `Delete()`, `DeleteRange()`
  - `CountAsync()`, `ExistsAsync()`
  - `Query()`
- **`IUnitOfWork`**:
  - Tự động cấp phát Generic Repository cho bất kỳ entity nào: `_unitOfWork.Repository<T>()`.
  - Cung cấp các Custom Repository khi cần truy vấn phức tạp: `_unitOfWork.Users`, `_unitOfWork.Stories`.
  - Quản lý giao dịch nguyên tử (ACID Transaction): `BeginTransactionAsync()`, `CommitTransactionAsync()`, `RollbackTransactionAsync()`, `SaveChangesAsync()`.

### 2.3. Tầng Presentation (API) chuẩn hóa
- Mọi API endpoint trả về chuẩn phản hồi nhất quán:
  ```json
  {
    "success": true,
    "message": "Thao tác thành công.",
    "data": { ... },
    "errors": null
  }
  ```
- **`ExceptionHandlingMiddleware`**: Tự động bắt mọi `AppException` (`NotFoundException` -> 404, `BadRequestException` -> 400, `ForbiddenException` -> 403, `UnauthorizedException` -> 401) và chuyển thành định dạng JSON chuẩn mà không để lộ Exception thô ra ngoài client.
- **Swagger UI**: Đã tích hợp sẵn ô `Authorize (Bearer Token)` để test các endpoint bảo vệ.

---

## 3. Cách chạy ứng dụng

### 3.1. Cấu hình cơ sở dữ liệu
Chỉnh sửa file [appsettings.json](file:///e:/Capstone/backend/src/StoryPlatform.API/appsettings.json):
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=ai_storytelling_db;Username=postgres;Password=your_password"
}
```

### 3.2. Chạy ứng dụng
Mở terminal trong thư mục `backend/`:
```bash
dotnet run --project StoryPlatform.API/StoryPlatform.API.csproj
```
Truy cập Swagger UI tại trình duyệt:
- `http://localhost:5000` (hoặc cổng hiển thị trên console)

---

## 4. Hướng dẫn thêm Module mới trong tương lai
Ví dụ muốn thêm module **`ChildProfile`**:
1. **DAL**: Tạo `ChildProfile.cs` trong `StoryPlatform.DAL/Entities/`, thêm `DbSet<ChildProfile>` vào `ApplicationDbContext.cs`.
2. **BLL**: Tạo thư mục `StoryPlatform.BLL/Modules/ChildProfile/`:
   - `DTOs/` (CreateChildProfileDto, ChildProfileDto...)
   - `Interfaces/IChildProfileService.cs`
   - `Services/ChildProfileService.cs` (Inject `IUnitOfWork` và gọi `_unitOfWork.Repository<ChildProfile>()`)
3. **Đăng ký DI**: Thêm `services.AddScoped<IChildProfileService, ChildProfileService>()` vào `StoryPlatform.BLL/DependencyInjection.cs`.
4. **API**: Tạo `ChildProfileController.cs` kế thừa `BaseApiController`.
