# AI Service Architecture

## 1. Mục đích

Thư mục `src/AI` chứa **AI Generation Service** của hệ thống AI Storytelling. Service này chịu trách nhiệm xử lý các tác vụ liên quan đến AI, bao gồm:

- Sinh tiêu đề và dàn ý truyện.
- Mở rộng dàn ý thành nội dung truyện có cấu trúc.
- Sinh bài học, từ vựng, câu hỏi đọc hiểu và câu hỏi thảo luận.
- Kiểm tra cấu trúc, giới hạn độ dài và các quy tắc an toàn cơ bản.
- Tự động tinh chỉnh kết quả không đạt yêu cầu.
- Ghi nhận metadata kỹ thuật như model, phiên bản prompt, token và độ trễ.

AI Service là một **processing boundary** độc lập. Nó không sở hữu tài khoản, hồ sơ trẻ, truyện đã lưu hoặc quy trình phê duyệt của hệ thống.

## 2. Nguyên tắc ownership

### Core sở hữu nghiệp vụ và dữ liệu

Core là nơi sở hữu:

- Các domain entity như `Story`, `ChildProfile`, `LearningProfile`, `SafetyPolicy`, `StoryVersion` và `StoryGenerationJob`.
- Database nghiệp vụ và `DbContext`.
- Repository, Unit of Work và transaction.
- Xác thực người dùng, phân quyền và quan hệ giám sát.
- Trạng thái phê duyệt, publish, archive và quyền truy cập của trẻ.
- Database migration cho các bảng nghiệp vụ.

### AI sở hữu năng lực xử lý AI

AI Service sở hữu:

- Prompt template và phiên bản prompt.
- Cách gọi LLM provider.
- JSON Schema dùng để bắt buộc structured output.
- Logic generation, evaluation và refinement.
- Model nội bộ phục vụ quá trình xử lý AI.

AI Service **không**:

- Tham chiếu project `StoryPlatform.Domain` hoặc `StoryPlatform.Infrastructure` của Core.
- Nhận hoặc trả EF Core entity qua HTTP.
- Truy cập trực tiếp database của Core.
- Tạo lại Core entity trong AI Domain.
- Tạo migration cho các bảng nghiệp vụ thuộc Core.
- Tự quyết định truyện được publish hoặc hiển thị cho trẻ.

Một bảng dữ liệu chỉ nên có một service sở hữu. Với kiến trúc hiện tại, toàn bộ dữ liệu nghiệp vụ được Core sở hữu.

## 3. Giao tiếp Core và AI

Core và AI giao tiếp qua HTTP bằng các DTO trong project dùng chung:

```text
src/Shared/StoryPlatform.Contracts
```

Luồng dữ liệu chuẩn:

```text
Core Entity / Database
        |
        | Core kiểm tra quyền và map dữ liệu
        v
Shared Request DTO
        |
        | HTTP + X-Internal-Api-Key
        v
AI Service
        |
        | Generation / Evaluation / Refinement
        v
Shared Response DTO
        |
        | Core map kết quả
        v
Core Entity / Database
```

Ví dụ khi sinh truyện:

1. Core nhận yêu cầu từ Parent hoặc Teacher.
2. Core kiểm tra người dùng có quyền tạo truyện cho hồ sơ trẻ hay không.
3. Core đọc `ChildProfile`, `LearningProfile` và `SafetyPolicy` từ database.
4. Core tạo `GenerateStoryRequest` chỉ với dữ liệu AI thực sự cần.
5. Core gọi AI Service thông qua `IAIStoryGenerationClient`.
6. AI Service sinh truyện, đánh giá và refinement nếu cần.
7. AI Service trả `GenerateStoryResponse`; không ghi database Core.
8. Core kiểm tra kết quả và map thành `Story`, `StoryVersion` hoặc `StoryGenerationJob`.
9. Core lưu dữ liệu bằng transaction của Core.

Không truyền domain entity trực tiếp qua service boundary. DTO giúp tránh coupling giữa persistence model của Core và quá trình xử lý AI.

## 4. Cấu trúc project

```text
src/AI/
├── StoryPlatform.AI.Api/
│   ├── Controllers/
│   ├── Middleware/
│   └── Program.cs
├── StoryPlatform.AI.Application/
│   ├── Abstractions/
│   ├── Common/
│   ├── OutlineGeneration/
│   ├── StoryGeneration/
│   ├── Refinement/
│   └── Evaluation/
├── StoryPlatform.AI.Domain/
│   ├── Enums/
│   └── Generation/
└── StoryPlatform.AI.Infrastructure/
    ├── LLM/
    ├── PromptCatalog/
    └── Evaluation/
```

### `StoryPlatform.AI.Api`

Đây là HTTP entry point nội bộ của AI Service.

Trách nhiệm:

- Cung cấp các endpoint nội bộ cho Core.
- Xác thực `X-Internal-Api-Key`.
- Chuyển request đến application handler.
- Chuẩn hóa lỗi từ validation, cấu hình và LLM provider.
- Cung cấp health check.

Các endpoint hiện có:

| Method | Endpoint | Mục đích |
|---|---|---|
| `POST` | `/api/ai/outline` | Sinh tiêu đề và dàn ý ba phần |
| `POST` | `/api/ai/story` | Sinh story package hoàn chỉnh |
| `POST` | `/api/ai/refine` | Tinh chỉnh một story package |
| `POST` | `/api/ai/evaluate` | Đánh giá story bằng rule deterministic |
| `GET` | `/health` | Kiểm tra trạng thái process |

Các endpoint này không nên được frontend gọi trực tiếp. Core API là public boundary chịu trách nhiệm authentication và authorization.

### `StoryPlatform.AI.Application`

Đây là tầng điều phối các AI use case.

Trách nhiệm:

- Validate request ở mức application.
- Chọn prompt phù hợp.
- Compose prompt từ template và request DTO.
- Yêu cầu LLM trả structured output.
- Deserialize output thành contract.
- Chạy evaluation.
- Điều phối vòng lặp refinement.
- Map metadata vào response.

Các abstraction chính:

- `ILlmClient`: cổng giao tiếp với LLM provider.
- `IPromptTemplateProvider`: cung cấp prompt template đang hoạt động.
- `IStoryEvaluationService`: đánh giá chất lượng và an toàn của story.

Application chỉ phụ thuộc abstraction; nó không biết chi tiết HTTP của OpenAI và không truy cập database Core.

### `StoryPlatform.AI.Domain`

Đây là nơi chứa các khái niệm chỉ thuộc AI processing, ví dụ:

- Loại generation và prompt.
- Kết quả thô từ LLM.
- Model dùng trong quá trình điều phối AI.

AI Domain không phải bản sao của Core Domain. Không thêm `Story`, `ChildProfile` hoặc entity persistence của Core vào project này.

### `StoryPlatform.AI.Infrastructure`

Đây là nơi triển khai các adapter kỹ thuật.

Trách nhiệm:

- Gọi OpenAI Responses API.
- Gửi JSON Schema với chế độ structured output nghiêm ngặt.
- Trích xuất content và token usage từ phản hồi của provider.
- Cung cấp prompt catalog.
- Triển khai rule-based story evaluator.
- Đăng ký dependency injection cho các adapter.

Nếu bổ sung Anthropic hoặc Gemini, tạo implementation mới của `ILlmClient` tại tầng Infrastructure; không thay đổi application handler chỉ để đổi provider.

## 5. Luồng xử lý hiện tại

### Sinh dàn ý

```text
GenerateOutlineRequest
  -> RequestGuard
  -> PromptTemplateProvider
  -> PromptComposer
  -> ILlmClient với Outline JSON Schema
  -> Deserialize title + outline
  -> GenerateOutlineResponse
```

### Sinh truyện

```text
GenerateStoryRequest
  -> RequestGuard
  -> Story prompt
  -> ILlmClient với StoryPackage JSON Schema
  -> Deserialize StoryPackage
  -> Rule-based Evaluation
  -> Nếu không đạt: Refinement prompt
  -> Evaluation lại, tối đa 2 lần refinement
  -> GenerateStoryResponse
```

AI Service vẫn trả `Evaluation` cho Core. Nếu kết quả không đạt sau số lần refinement tối đa, Core phải giữ truyện ở trạng thái cần review hoặc rejected; không được tự động cho trẻ truy cập.

### Đánh giá độc lập

Endpoint evaluation hiện sử dụng rule deterministic và không gọi LLM. Kết quả gồm:

- `SchemaValid`
- `SafetyPassed`
- `ReadabilityPassed`
- `VocabularyPassed`
- Danh sách `Issues`

## 6. Dependency direction

Dependency direction mong muốn:

```text
AI.Api
  -> AI.Application
  -> AI.Domain

AI.Infrastructure
  -> AI.Application abstractions
  -> AI.Domain

Core.Infrastructure
  -> Core.Application abstractions
  -> StoryPlatform.Contracts

Core và AI
  -> StoryPlatform.Contracts
```

Các project AI không được thêm project reference đến Core Domain hoặc Core Infrastructure.

## 7. Khi nào AI cần database riêng?

AI Service hiện được định hướng là stateless, vì vậy không cần database hoặc migration riêng.

Chỉ cân nhắc database riêng cho AI khi xuất hiện dữ liệu kỹ thuật do AI thực sự sở hữu, ví dụ:

- Vector embedding/index.
- Prompt cache.
- Provider-specific batch job state.
- Evaluation dataset dùng để nghiên cứu model.

Ngay cả trong trường hợp đó:

- AI chỉ tạo migration cho database kỹ thuật riêng của AI.
- AI không tạo migration cho các bảng Core.
- Core và AI không cùng ghi vào một bảng.
- Audit nghiệp vụ, story version và approval state vẫn thuộc Core.

Việc thêm database hoặc migration cho AI phải là một quyết định kiến trúc có chủ đích, không phải cách để AI truy cập entity của Core.

## 8. Cấu hình

AI Service đọc cấu hình theo các key:

```text
AI:InternalApiKey
AI:OpenAI:ApiKey
AI:OpenAI:Endpoint
AI:OpenAI:Model
AI:OpenAI:TimeoutSeconds
AI:Generation:MaxRefinementAttempts
```

Ví dụ environment variables khi chạy local:

```powershell
$env:AI__InternalApiKey = "local-internal-key"
$env:AI__OpenAI__ApiKey = "your-api-key"
$env:AI__OpenAI__Model = "gpt-5-mini"
$env:AI__Generation__MaxRefinementAttempts = "2"
```

Core phải sử dụng cùng internal API key khi gọi AI Service qua cấu hình `AIService`.

Không commit API key hoặc secret vào repository.

## 9. Quy tắc khi mở rộng AI module

Khi thêm tính năng mới:

1. Đặt request/response contract dùng giữa Core và AI trong `StoryPlatform.Contracts`.
2. Đặt orchestration use case trong `StoryPlatform.AI.Application`.
3. Đặt abstraction provider trong `Application/Abstractions`.
4. Đặt adapter OpenAI hoặc provider khác trong `StoryPlatform.AI.Infrastructure`.
5. Chỉ đặt model thật sự thuộc AI processing trong `StoryPlatform.AI.Domain`.
6. Để Core kiểm tra quyền, quản lý state machine và lưu dữ liệu nghiệp vụ.
7. Không đưa EF Core entity hoặc `DbContext` của Core qua AI boundary.
8. Bổ sung test cho happy path, validation, provider failure và refinement limit.

## 10. Phần chưa hoàn thiện

Kiến trúc AI hiện đã có pipeline xử lý nội bộ nhưng chưa hoàn thành toàn bộ Guided AI Story Generation flow:

- Core chưa có public AI generation use case/controller.
- Core chưa tự lấy Child/Learning/Safety Profile để tạo request đáng tin cậy.
- Chưa lưu outline approval, story version hoặc generation job.
- Chưa có approval state machine.
- Safety evaluator mới là rule-based cơ bản.
- Chưa có TTS, illustration queue hoặc media processing.
- Chưa có audit log và tổng hợp chi phí qua mọi refinement attempt.

Các phần trên nên được triển khai trong đúng boundary: Core sở hữu state và persistence; AI sở hữu generation, evaluation và provider integration.
