# KHẢO SÁT DỰ ÁN — AI STORYTELLING BACKEND & VERTEX AI INTEGRATION

**Ngày khảo sát:** 2026-09-22  
**Phạm vi:** Backend `.NET`, module AI, cơ chế LLM hiện tại, tích hợp Google Vertex AI/Gemini, cấu hình Dev/Production  
**Mục tiêu:** Ghi nhận cấu trúc hiện tại, xác định điểm lệch kiến trúc, thống nhất hướng tích hợp Vertex AI mà không phá vỡ contract hiện có.

---

## 1. Mục tiêu hệ thống

Dự án **AI Storytelling** xây dựng hệ thống tạo truyện bằng AI cho trẻ em, trong đó backend chịu trách nhiệm:

- Nhận input từ Parent / Teacher.
- Kiểm tra Child Profile, quyền truy cập, safety policy.
- Sinh outline.
- Sinh nội dung truyện.
- Đánh giá chất lượng.
- Refine nội dung.
- Sinh Vocabulary / Quiz / Discussion Questions.
- Chia scene.
- Chuẩn bị prompt cho image generation.
- Sinh audio / media ở phase sau.
- Lưu version, trạng thái review, approval và audit trail.

Flow AI chính đang hướng tới:

```text
INPUT
  ↓
OUTLINE GENERATION
  ↓
CONTENT GENERATION
  ↓
QUALITY GATE
  ↓
REFINE
  ↓
VOCABULARY / QUIZ / DISCUSSION
  ↓
APPROVAL
  ↓
MEDIA
```

---

## 2. Cấu trúc solution hiện tại

Cấu trúc quan sát được:

```text
AI_Storytelling_Backend/
│
├── src/
│   │
│   ├── AI/
│   │   ├── StoryPlatform.AI.Api/
│   │   ├── StoryPlatform.AI.Application/
│   │   ├── StoryPlatform.AI.Domain/
│   │   └── StoryPlatform.AI.Infrastructure/
│   │       ├── Evaluation/
│   │       ├── LLM/
│   │       ├── PromptCatalog/
│   │       ├── DependencyInjection.cs
│   │       └── StoryPlatform.AI.Infrastructure.csproj
│   │
│   ├── Core/
│   │   ├── StoryPlatform.Api/
│   │   ├── StoryPlatform.Application/
│   │   └── StoryPlatform.Infrastructure/
│   │
│   └── Shared/
│       └── StoryPlatform.Llm/        ← được agent tạo thêm
│
├── tests/
├── docs/
├── Database/
├── tools/
├── StoryPlatform.sln
└── StoryPlatform.slnx
```

---

## 3. Kiến trúc AI mong muốn

AI module hiện tại đã được tách riêng theo hướng gần với Clean Architecture:

```text
StoryPlatform.AI.Domain
        ↑
StoryPlatform.AI.Application
        ↑
StoryPlatform.AI.Infrastructure
        ↑
StoryPlatform.AI.Api / Core API Host
```

Dependency cần giữ:

```text
Application
   ↓ abstraction
ILlmClient / ILLMProvider
   ↑ implementation
Infrastructure
   ↓
Google.GenAI
   ↓
Vertex AI
```

Nguyên tắc:

- `Domain` không biết Google / Gemini / Vertex.
- `Application` chỉ biết abstraction.
- `Infrastructure` chịu trách nhiệm gọi Vertex AI.
- `Api` chỉ làm host + DI + config.
- Không để business logic nằm trong provider.
- Không hard-code credential.
- Không commit service-account JSON vào repo.

---

## 4. Trạng thái Google Cloud hiện tại

Google Cloud project đang dùng cho Dev:

```text
Project Name: Ai Storytelling
Project ID: gen-lang-client-0675088605
```

Đã hoàn thành:

- Google Cloud CLI.
- `gcloud init`.
- Project selection.
- `gcloud auth application-default login`.
- ADC local.
- Billing Free Trial.
- Khoảng `$300` / ~7.8 triệu VND credit.
- Agent Platform API / `aiplatform.googleapis.com` đã enable.

Dev authentication hiện tại:

```text
Developer Google Account
        ↓
gcloud auth application-default login
        ↓
Application Default Credentials
        ↓
.NET Backend
        ↓
Vertex AI
```

---

## 5. Cơ chế authentication mong muốn

### 5.1 Development

Dùng:

```text
Application Default Credentials (ADC)
```

Thiết lập:

```powershell
gcloud auth application-default login
gcloud config set project gen-lang-client-0675088605
```

Không dùng:

- API key cho Vertex path.
- service-account JSON lưu trong repo.
- access token hard-code.
- Bearer token copy thủ công.

---

### 5.2 Production

Mục tiêu:

```text
Cloud Run / GCE / GKE
        ↓
Service Account / Workload Identity
        ↓
ADC runtime
        ↓
Vertex AI
```

Không dùng credential file dài hạn nếu không cần thiết.

---

## 6. Cấu hình Dev đề xuất

Host config nên đặt tại project thực sự chạy application.

Nếu host hiện tại là:

```text
src/Core/StoryPlatform.Api/
```

thì config nên nằm ở:

```text
src/Core/StoryPlatform.Api/appsettings.Development.json
```

Section đề xuất:

```json
{
  "AI": {
    "Google": {
      "AuthMode": "Adc",
      "ProjectId": "gen-lang-client-0675088605",
      "Location": "global",
      "Model": "gemini-2.5-flash"
    }
  }
}
```

Không đặt credential trong `appsettings`.

---

## 7. Package cần dùng

Package chính thức mong muốn:

```text
Google.GenAI
```

Nên được add vào đúng project:

```text
src/AI/StoryPlatform.AI.Infrastructure/
StoryPlatform.AI.Infrastructure.csproj
```

Command:

```powershell
dotnet add .\src\AI\StoryPlatform.AI.Infrastructure\StoryPlatform.AI.Infrastructure.csproj package Google.GenAI
```

Không cần add vào:

- `StoryPlatform.AI.Domain`
- `StoryPlatform.AI.Application`
- `StoryPlatform.AI.Api`
- Core Domain/Application

---

## 8. Provider mong muốn

Vị trí đề xuất:

```text
src/AI/StoryPlatform.AI.Infrastructure/
└── LLM/
    └── Gemini/
        ├── GeminiOptions.cs
        └── GeminiLlmClient.cs
```

Hoặc:

```text
LLM/
└── VertexAI/
    ├── VertexAiOptions.cs
    └── VertexAiProvider.cs
```

Tuy nhiên không nên tạo abstraction mới nếu project đã có:

```text
ILlmClient
```

hoặc interface tương đương.

---

## 9. Flow gọi LLM mục tiêu

```text
Flow 2
   ↓
Application Service
   ↓
ILlmClient
   ↓
GeminiLlmClient
   ↓
Google.GenAI.Client
   ↓
ADC
   ↓
Vertex AI
   ↓
Gemini
```

Provider chỉ chịu trách nhiệm:

- nhận prompt
- gọi model
- truyền CancellationToken
- trả response
- map error ở mức infrastructure

Provider không chịu trách nhiệm:

- build business prompt phức tạp
- validation story
- persistence
- approval
- safety workflow
- DB transaction
- review state

---

## 10. Kết quả agent implementation hiện tại

Agent trước đó đã tạo:

```text
src/Shared/StoryPlatform.Llm/
```

bao gồm:

- `GoogleAdcOptions.cs`
- `IAuthHeaderProvider.cs`
- `ResolvedAuth`
- `VertexEndpointBuilder.cs`
- `AdcTokenProvider.cs`
- `DefaultAuthHeaderProvider.cs`
- `LlmServiceCollectionExtensions.cs`

Agent cũng đã:

- dùng `Google.Apis.Auth`
- tự lấy access token
- cache token ~50 phút
- tự build Vertex endpoint
- tự attach Bearer token
- refactor `BaseGeminiClient`
- refactor `GeminiLlmClient`
- dùng chung auth provider giữa Core và AI module

Validation:

```text
dotnet build → 0 warnings / 0 errors
dotnet test  → 614 / 614 passed
```

---

## 11. Điểm tích cực của implementation hiện tại

### 11.1 Regression an toàn

```text
614 / 614 tests passed
```

Điều này cho thấy agent đã giữ được behavior hiện tại.

### 11.2 Authentication được tách khỏi business code

Các client không còn phải tự xử lý credential trực tiếp.

### 11.3 Có khả năng hỗ trợ cả API Key và ADC

Current pattern có thể switch giữa:

```text
Gemini Developer API
→ API key

Vertex AI
→ ADC / Bearer
```

Điều này có thể hữu ích nếu hệ thống thực sự cần dual provider.

---

## 12. Điểm lệch hướng cần xem xét

### 12.1 Không dùng `Google.GenAI`

Yêu cầu ban đầu là:

```text
Google.GenAI
```

nhưng implementation hiện tại dùng:

```text
Google.Apis.Auth
+
HttpClient
+
manual Vertex endpoint
```

Điều này làm tăng maintenance burden.

---

### 12.2 Tự quản lý Bearer token

Agent đang tự xử lý:

```text
token acquisition
token cache
token refresh
expiry
Bearer header
```

Trong khi SDK chính thức có thể xử lý authentication thông qua ADC.

---

### 12.3 Tự build endpoint Vertex

Agent đang tạo logic dạng:

```text
{location}-aiplatform.googleapis.com
```

Điều này làm code phụ thuộc vào transport detail.

---

### 12.4 Shared module đang chứa Google-specific infrastructure

`StoryPlatform.Llm` được đặt trong:

```text
src/Shared
```

nhưng đang chứa:

```text
GoogleAdcOptions
VertexEndpointBuilder
AdcTokenProvider
```

Đây là Google-specific infrastructure hơn là abstraction shared trung lập.

Nếu shared module tồn tại lâu dài, nên cân nhắc chỉ giữ abstraction chung thực sự.

---

## 13. Quyết định kiến trúc đề xuất

### Option A — Vertex only

Nếu hệ thống quyết định dùng Vertex AI làm provider chính:

```text
AI.Infrastructure
   ↓
Google.GenAI
   ↓
ADC
   ↓
Vertex AI
```

Đây là hướng đơn giản nhất.

---

### Option B — Dual mode

Nếu cần giữ:

```text
Gemini Developer API
+
Vertex AI
```

thì nên giữ runtime switch, nhưng tách rõ:

```text
ILlmClient
   ↓
GeminiDeveloperApiClient
   ↓ API key
```

và:

```text
ILlmClient
   ↓
VertexGeminiClient
   ↓ Google.GenAI + ADC
```

Không nên làm một auth provider quá generic nếu nó khiến business path trở nên khó hiểu.

---

## 14. Recommendation cho project hiện tại

Đối với mục tiêu hiện tại:

> Kiểm tra AI Generation trong môi trường Dev bằng Vertex AI.

Nên ưu tiên:

```text
Vertex AI only
+
ADC local
+
Google.GenAI
```

Chưa cần dual mode nếu không có requirement rõ ràng.

---

## 15. Connectivity test cần có

Endpoint test Development-only:

```text
POST /api/dev/ai/vertex-test
```

Prompt:

```text
Return exactly: VERTEX_AI_OK
```

Expected:

```text
VERTEX_AI_OK
```

Flow:

```text
Swagger / Postman
      ↓
StoryPlatform.Api
      ↓
Application
      ↓
ILlmClient
      ↓
GeminiLlmClient
      ↓
Google.GenAI
      ↓
ADC
      ↓
Vertex AI
```

---

## 16. Test Flow 2 sau khi connectivity pass

Không test toàn pipeline ngay.

### Stage 1 — Connectivity

```text
Backend → Vertex → Gemini
```

### Stage 2 — Outline

```text
GenerateOutline
```

### Stage 3 — Story

```text
GenerateStory
```

### Stage 4 — Evaluation

```text
EvaluateStory
```

### Stage 5 — Refinement

```text
RefineStory
```

### Stage 6 — Enrichment

```text
Vocabulary
Quiz
Discussion Questions
```

### Stage 7 — Scene / Media prep

```text
Scene Segmentation
Image Prompt
TTS Input
```

---

## 17. Structured Output

Các nghiệp vụ như outline, story metadata, quiz nên ưu tiên JSON structured output.

Ví dụ:

```json
{
  "title": "The Brave Rabbit",
  "summary": "A rabbit learns to help friends.",
  "scenes": [
    {
      "order": 1,
      "content": "...",
      "imagePrompt": "..."
    }
  ]
}
```

Không nên phụ thuộc quá nhiều vào plain text parsing.

---

## 18. Git / Secret Rules

Repo không được chứa:

```text
service-account.json
google-credentials.json
access-token.txt
private-key.json
```

ADC local nằm ngoài repo.

`.gitignore` nên có rule cụ thể nếu cần:

```gitignore
service-account*.json
google-credentials*.json
gcp-credentials*.json
```

Không ignore toàn bộ `*.json`.

---

## 19. Checklist triển khai Dev

```text
[✓] Google Cloud Project
[✓] Billing / Free Trial
[✓] Agent Platform API enabled
[✓] gcloud installed
[✓] gcloud init
[✓] ADC login
[✓] Project selected
[ ] Google.GenAI package in AI.Infrastructure
[ ] Vertex/Gemini options
[ ] GeminiLlmClient using Google.GenAI
[ ] DI registration
[ ] Dev-only connectivity endpoint
[ ] VERTEX_AI_OK
[ ] GenerateOutline test
[ ] GenerateStory test
[ ] EvaluateStory test
[ ] RefineStory test
```

---

## 20. Checklist trước khi Production

```text
[ ] Service Account riêng cho backend
[ ] roles/aiplatform.user
[ ] Workload Identity / runtime identity
[ ] Không dùng Owner account
[ ] Không dùng local ADC
[ ] Không commit credential
[ ] Budget alert
[ ] Quota controls
[ ] Logging
[ ] Retry policy
[ ] Timeout policy
[ ] Rate limiting
[ ] Cost tracking
```

---

## 21. Kết luận

Project hiện tại đã có nền tảng tốt để tích hợp Vertex AI vì:

- AI module đã tách riêng.
- Có Infrastructure layer.
- Có `LLM` folder.
- Có DI riêng.
- Existing tests đang ổn định.
- ADC local đã được cấu hình.
- Billing / Free Trial đã active.

Vấn đề chính cần giải quyết là tránh việc tích hợp Vertex AI theo hướng quá low-level nếu không có requirement đặc biệt.

Kiến trúc đề xuất cuối cùng:

```text
StoryPlatform.Api
        ↓
AI.Application
        ↓
ILlmClient
        ↓
AI.Infrastructure
        ↓
GeminiLlmClient
        ↓
Google.GenAI
        ↓
Application Default Credentials
        ↓
Vertex AI
        ↓
Gemini
```

Đây là hướng phù hợp nhất cho giai đoạn Dev hiện tại và cũng dễ chuyển sang Production bằng Service Account / Workload Identity sau này.
