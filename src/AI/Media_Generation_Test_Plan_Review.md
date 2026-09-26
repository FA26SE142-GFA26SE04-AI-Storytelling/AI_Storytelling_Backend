# Code Review — Kế Hoạch Kiểm Tra Cấu Hình Image & Audio Generation

**Document:** Kế Hoạch Cấu Hình Hoàn Chỉnh Model Generation Image và Audio  
**Reviewer:** Claude Code  
**Date:** September 2026  
**Status:** Review Comments

---

## Executive Summary

Kế hoạch đề xuất tốt, bao gồm phân tích hiện trạng và đề xuất thay đổi rõ ràng. Tuy nhiên, còn thiếu **test plan chi tiết**, **error handling scenarios**, và **risk assessment**. Dưới đây là đánh giá chi tiết theo từng phần.

---

## 1. Strengths (Điểm mạnh)

| # | Strength | Detail |
|---|----------|--------|
| 1 | **Khảo sát kỹ thuật đầy đủ** | Đã xác minh trực tiếp với Google Cloud API, có HTTP 200 OK response |
| 2 | **Cấu hình chi tiết** | Tất cả parameters đều có giá trị cụ thể và có giải thích |
| 3 | **Credentials handling** | Chú ý đến QuotaProject, ADC, Service Account - đúng approach |
| 4 | **Model validation** | Đã verify model names (`gemini-2.5-flash-image`, `gemini-2.5-flash`) |

---

## 2. Gaps & Missing Items (Các thiếu sót)

### 2.1 Missing: Test Plan Chi Tiết

| Missing Item | Current | Recommended |
|--------------|---------|-------------|
| **Unit tests cho providers** | ❌ Không đề cập | Cần viết unit tests cho `GeminiImageGenerationProvider`, `GoogleCloudTtsProvider` |
| **Mock strategy** | ❌ Không đề cập | Cần xác định cách mock LLM providers trong tests |
| **Integration test scenarios** | ⚠️ Sơ lược | Cần chi tiết hơn: Happy path, Error path, Retry path |
| **Test data setup** | ❌ Không đề cập | Cần sample story content, expected images, expected audio |

### 2.2 Missing: Error Handling Scenarios

| Error Scenario | Impact | Current Coverage |
|----------------|--------|------------------|
| **Invalid API credentials** | Service không hoạt động | ❌ Không test |
| **Quota exceeded** | Media generation fail | ⚠️ Chỉ đề cập quota project nhưng không test |
| **Network timeout** | Transient failure | ❌ Không test retry logic |
| **Invalid model response** | JSON parse error | ❌ Không test |
| **Rate limiting (429)** | Throttle requests | ❌ Không test |
| **Large content OOM** | Memory crash | ❌ Không test |

### 2.3 Missing: Security & Compliance

| Item | Status | Recommendation |
|------|--------|----------------|
| **Credentials in config** | ⚠️ `CredentialsPath: ""` | Không commit credentials thật, dùng secrets manager |
| **Audit logging** | ❌ Không đề cập | Log media generation requests (who, when, what) |
| **PII handling** | ❌ Không đề cập | Story content có thể chứa PII - cần review |

### 2.4 Missing: Performance & Scale

| Item | Status | Recommendation |
|------|--------|----------------|
| **Concurrent requests** | ❌ Không test | 10, 50, 100 concurrent media generation |
| **Response time SLAs** | ❌ Không đề cập | Define SLO cho image/TTS generation |
| **Cost estimation** | ❌ Không đề cập | Ảnh hưởng quota/cost khi scale |

---

## 3. Detailed Review Comments

### 3.1 Configuration Review

#### ✅ Configuration Structure
```json
"ImageGeneration": {
  "Model": "gemini-2.5-flash-image",
  "AspectRatio": "16:9"
}
```
**Comment:** Cấu hình đúng cấu trúc, aspect ratio phù hợp cho story illustrations.

#### ⚠️ Cần bổ sung:
```json
"ImageGeneration": {
  "Model": "gemini-2.5-flash-image",
  "AspectRatio": "16:9",
  "NumberOfImages": 1,           // ← THIẾU
  "SafetySetting": "block_some", // ← THIẾU
  "PersonGeneration": "allow_adult" // ← THIẾU
}
```

### 3.2 TTS Configuration Review

#### ✅ Good:
```csharp
.EnableWordTimings(true)  // Hỗ trợ Karaoke feature
.QuotaProject = "gen-lang-client-0675088605"  // Đúng approach
```

#### ⚠️ Cần bổ sung:
```json
"TTS": {
  "LanguageCode": "vi-VN",
  "VoiceName": "vi-VN-Wavenet-A",
  "AudioEncoding": "MP3",
  "SpeakingRate": 1.0,
  "EnableWordTimings": true,
  "Pitch": 0.0,              // ← THIẾU: Adjust pitch
  "VolumeGainDb": 0.0,      // ← THIẾU: Volume control
  "AudioEffects": []         // ← THIẾU: Post-processing effects
}
```

### 3.3 DependencyInjection.cs Review

#### ⚠️ Potential Issue:
```csharp
// Current approach sử dụng singleton factory
builder.QuotaProject = vertexOpts.ProjectId
```

**Concern:** QuotaProject nên được set ở `ClientBuilder` level, không phải instance level. Verify implementation không bị override bởi credentials chain.

---

## 4. Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **Credentials misconfigured** | Medium | High | Test credentials validation at startup |
| **Quota exhausted in production** | Medium | High | Monitor quota, implement circuit breaker |
| **Model deprecation** | Low | Medium | Pin model version, monitor announcements |
| **Regional latency** | Medium | Medium | Test from Vietnam region, consider CDN |
| **Content safety false positive** | Medium | High | Test various content types, tune thresholds |
| **TTS voice quality poor** | Medium | Medium | A/B test voices before production |

---

## 5. Recommended Test Plan

### 5.1 Unit Tests

#### GeminiImageGenerationProvider Tests

```csharp
public class GeminiImageGenerationProviderTests
{
    [Fact]
    public async Task GenerateAsync_ValidPrompt_ReturnsImageAsset()
    {
        // Setup: Valid prompt, mocked Vertex AI response
        // Verify: Returns MediaAsset with correct MIME type
    }

    [Fact]
    public async Task GenerateAsync_SafetyBlocked_ReturnsEmptyWithReason()
    {
        // Setup: Prompt that triggers safety filter
        // Verify: Returns empty result with safety classification
    }

    [Fact]
    public async Task GenerateAsync_InvalidCredentials_ThrowsAuthenticationException()
    {
        // Setup: Invalid/expired credentials
        // Verify: Throws specific exception, logs error
    }

    [Fact]
    public async Task GenerateAsync_QuotaExceeded_ThrowsQuotaException()
    {
        // Setup: Mock 403 quota exceeded response
        // Verify: Throws QuotaExceededException
    }

    [Fact]
    public async Task GenerateAsync_NetworkTimeout_RetriesWithBackoff()
    {
        // Setup: Mock timeout on first call
        // Verify: Retries, eventually succeeds or fails after max retries
    }
}
```

#### GoogleCloudTtsProvider Tests

```csharp
public class GoogleCloudTtsProviderTests
{
    [Fact]
    public async Task SynthesizeAsync_ValidText_ReturnsAudioWithWordTimings()
    {
        // Setup: Vietnamese text
        // Verify: MP3 audio + SSML marks for word sync
    }

    [Fact]
    public async Task SynthesizeAsync_EmptyText_ThrowsArgumentException()
    {
        // Setup: Empty string
        // Verify: Throws ArgumentException
    }

    [Fact]
    public async Task SynthesizeAsync_UnsupportedLanguage_ThrowsException()
    {
        // Setup: Language not in supported list
        // Verify: Throws LanguageNotSupportedException
    }

    [Fact]
    public async Task SynthesizeAsync_LongText_ChunksCorrectly()
    {
        // Setup: Text > 5000 characters
        // Verify: Chunks to multiple requests, concatenates correctly
    }
}
```

### 5.2 Integration Tests

#### Media Generation E2E Tests

```csharp
public class MediaGenerationE2ETests
{
    [Fact]
    public async Task HappyPath_ApprovedStory_GeneratesAllMedia()
    {
        // 1. Create approved story (via existing workflow)
        // 2. Trigger media generation
        // 3. Verify: All scenes have images + TTS
        // 4. Verify: Status = Ready
    }

    [Fact]
    public async Task ImageGenFailsOnce_RetriesAndSucceeds()
    {
        // 1. Mock image gen to fail once, then succeed
        // 2. Trigger media generation
        // 3. Verify: Eventually succeeds with retry
    }

    [Fact]
    public async Task TtsFailsPermanently_MarksSceneFailed()
    {
        // 1. Mock TTS to always fail
        // 2. Trigger media generation
        // 3. Verify: Scene marked failed, story not Ready
    }
}
```

### 5.3 Load Tests

```csharp
public class MediaGenerationLoadTests
{
    [Fact]
    public async Task ConcurrentMediaGen_Handles10SimultaneousRequests()
    {
        // 10 parallel media generation requests
        // Verify: All complete within 60s, no quota errors
    }

    [Fact]
    public async Task RateLimit_TripsCircuitBreaker()
    {
        // Rapid-fire requests to trigger rate limit
        // Verify: Circuit breaker opens after N failures
    }
}
```

---

## 6. Verification Checklist

### Pre-Deployment Checklist

| # | Item | Owner | Status |
|---|------|-------|--------|
| 1 | Credentials validated in DEV | Dev | ☐ |
| 2 | Credentials validated in STAGING | Dev | ☐ |
| 3 | Unit tests pass (≥80% coverage) | QA | ☐ |
| 4 | Integration tests pass | QA | ☐ |
| 5 | Load tests pass (10 concurrent) | QA | ☐ |
| 6 | Manual smoke test (image gen) | Dev | ☐ |
| 7 | Manual smoke test (TTS) | Dev | ☐ |
| 8 | Cost monitoring setup | DevOps | ☐ |
| 9 | Alerting configured | DevOps | ☐ |
| 10 | Rollback plan documented | DevOps | ☐ |

### Production Readiness Checklist

| # | Item | Owner | Status |
|---|------|-------|--------|
| 1 | Quota increase requested | PM | ☐ |
| 2 | Cost budget alert set | DevOps | ☐ |
| 3 | Runbook documented | Dev | ☐ |
| 4 | On-call team trained | DevOps | ☐ |

---

## 7. Recommended Additional Configuration

### appsettings.json bổ sung:

```json
{
  "AI": {
    "ImageGeneration": {
      "Model": "gemini-2.5-flash-image",
      "AspectRatio": "16:9",
      "SafetySetting": "block_some",
      "PersonGeneration": "allow_adult",
      "NumberOfImages": 1,
      "TimeoutSeconds": 60,
      "MaxRetries": 3
    },
    "TTS": {
      "LanguageCode": "vi-VN",
      "VoiceName": "vi-VN-Wavenet-A",
      "AudioEncoding": "MP3",
      "SpeakingRate": 1.0,
      "Pitch": 0.0,
      "VolumeGainDb": 0.0,
      "EnableWordTimings": true,
      "MaxTextLength": 5000,
      "TimeoutSeconds": 30,
      "MaxRetries": 3
    },
    "MediaGeneration": {
      "WorkerEnabled": true,
      "JobLeaseMinutes": 5,
      "AssetMaxAttempts": 3,
      "IdleDelaySeconds": 2,
      "TransientFailureDelaySeconds": 2,
      "PermanentFailureDelaySeconds": 300,
      "ImageModel": "gemini-2.5-flash-image",
      "TtsModel": "vi-VN-Wavenet-A",
      "EvaluatorModel": "gemini-2.5-flash",
      "ImageProvider": "VertexAI",
      "TtsProvider": "GoogleCloudTTS",
      "AlignmentEvaluator": "VertexAI",
      "SafetyEvaluator": "VertexAI",
      "LogLevel": "Summary",
      "CircuitBreaker": {
        "FailureThreshold": 5,
        "SamplingDurationSeconds": 60,
        "DurationOfBreakSeconds": 300
      },
      "RateLimit": {
        "RequestsPerMinute": 60,
        "BurstSize": 10
      }
    }
  }
}
```

---

## 8. Summary

| Category | Current Status | Recommendation |
|----------|---------------|----------------|
| **Technical Analysis** | ✅ Complete | Good as-is |
| **Configuration** | ⚠️ Good, missing some params | Add SafetySetting, RateLimit |
| **Test Plan** | ❌ Missing | Add Unit + Integration + Load tests |
| **Error Handling** | ❌ Missing | Add retry, circuit breaker tests |
| **Security** | ⚠️ Partial | Add credentials validation tests |
| **Performance** | ❌ Missing | Add load tests |
| **Monitoring** | ❌ Missing | Add cost + quota alerts |

### Priority Actions:

1. **Immediate (P0):**
   - Add unit tests cho `GeminiImageGenerationProvider`
   - Add unit tests cho `GoogleCloudTtsProvider`
   - Add credentials validation at startup

2. **High (P1):**
   - Add integration tests cho media generation E2E
   - Configure circuit breaker + rate limiting
   - Add cost monitoring alerts

3. **Medium (P2):**
   - Add load tests
   - Add security audit logging
   - Document runbook

---

*Generated by Claude Code*
