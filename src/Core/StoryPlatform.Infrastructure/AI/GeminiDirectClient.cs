using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Direct Gemini API client - gọi Gemini trực tiếp không qua Flask server
/// </summary>
public sealed class GeminiDirectClient : IAIStoryGenerationClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    // Gemini API base URL
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

    // Model mặc định - gemini-2.5-flash free tier
    private const string DefaultModel = "gemini-2.5-flash";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public GeminiDirectClient(HttpClient httpClient, IOptions<AIServiceOptions> options)
    {
        var settings = options.Value;
        _httpClient = httpClient;
        _apiKey = settings.ApiKey ?? throw new ArgumentNullException(nameof(options), "GEMINI_API_KEY is required");
        _model = settings.Model ?? DefaultModel;

        // Timeout configuration
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 300));
    }

    #region Outline Generation

    public async Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildOutlinePrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseOutlineResponse(response, request.RequestId);
    }

    public async Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildRefineOutlinePrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseRefineStoryResponse(response, request.RequestId);
    }

    #endregion

    #region Story Generation

    public async Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildStoryPrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseStoryResponse(response, request.RequestId);
    }

    public async Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildEvaluateStoryPrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseEvaluateStoryResponse(response, request.RequestId);
    }

    #endregion

    #region Content Generation

    public async Task<GenerateStoryContentResponse> GenerateStoryContentAsync(GenerateStoryContentRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildStoryContentPrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseStoryContentResponse(response, request.RequestId);
    }

    public async Task<RefineStoryContentResponse> RefineStoryContentAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildRefineContentPrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseRefineStoryContentResponse(response, request.RequestId);
    }

    public async Task<GenerateVocabularyResponse> GenerateVocabularyAsync(GenerateVocabularyRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildVocabularyPrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseVocabularyResponse(response, request.RequestId);
    }

    public async Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildQuizPrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseQuizResponse(response, request.RequestId);
    }

    public async Task<GenerateDiscussionResponse> GenerateDiscussionAsync(GenerateDiscussionRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildDiscussionPrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseDiscussionResponse(response, request.RequestId);
    }

    public async Task<EvaluateContentSafetyResponse> EvaluateContentSafetyAsync(EvaluateContentSafetyRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = BuildSafetyPrompt(request);
        var response = await GenerateContentAsync(prompt, cancellationToken);
        return ParseSafetyResponse(response, request.RequestId);
    }

    #endregion

    #region Core Gemini API Call

    private async Task<string> GenerateContentAsync(string prompt, CancellationToken cancellationToken)
    {
        var endpoint = $"{GeminiBaseUrl}{_model}:generateContent?key={_apiKey}";

        var requestBody = new GeminiRequest
        {
            Contents = new[]
            {
                new GeminiContent
                {
                    Parts = new[] { new GeminiPart { Text = prompt } }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0.7,
                MaxOutputTokens = 8192,
                TopP = 0.95,
                TopK = 40
            }
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AIServiceRequestException(
                (int)response.StatusCode,
                "GEMINI_API_ERROR",
                $"Gemini API error: {response.StatusCode} - {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse Gemini response");

        // Extract text from response
        var text = geminiResponse.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? string.Empty;

        return text;
    }

    #endregion

    #region Prompt Builders

    private static string BuildOutlinePrompt(GenerateOutlineRequest request)
    {
        var ageSettings = GetAgeSettings(request.AgeBand);
        var storyParams = request.StoryParameters;

        return $@"Bạn là một chuyên gia viết dàn ý truyện thiếu nhi. Tạo dàn ý sáng tạo, phù hợp với lứa tuổi.

YÊU CẦU:
- Chủ đề: {storyParams.Topic}
- Độ tuổi: {request.AgeBand} tuổi
- Cấp độ đọc: {request.ReadingLevel}
- Ngôn ngữ: {request.Language}
{(!string.IsNullOrEmpty(storyParams.Genre) ? $"- Thể loại: {storyParams.Genre}" : "")}
{(!string.IsNullOrEmpty(storyParams.Lesson) ? $"- Bài học: {storyParams.Lesson}" : "")}
{(storyParams.Characters.Count > 0 ? $"- Nhân vật: {string.Join(", ", storyParams.Characters)}" : "")}

CẤU TRÚC:
- Độ phức tạp: {ageSettings["complexity"]}
- Số chương: {ageSettings["chapter_count"]}

Trả lời JSON theo format:
{{
    ""title"": ""Tiêu đề truyện"",
    ""opening"": ""Phần mở đầu câu chuyện"",
    ""development"": ""Phần phát triển câu chuyện (toàn bộ diễn biến các chương, viết dưới dạng chuỗi văn bản string liền mạch)"",
    ""ending"": ""Phần kết thúc câu chuyện""
}}

LƯU Ý QUAN TRỌNG:
- Tất cả các trường 'title', 'opening', 'development', 'ending' đều phải là chuỗi văn bản (string), TUYỆT ĐỐI KHÔNG dùng mảng (array) hay object cho bất kỳ trường nào.
- Chỉ trả về duy nhất JSON hợp lệ, không kèm văn bản giải thích.
";
    }

    private static string BuildRefineOutlinePrompt(RefineStoryRequest request)
    {
        var story = request.Story;
        var outline = story.Outline;
        var reasons = string.Join("; ", request.Reasons);

        return $@"Bạn là chuyên gia chỉnh sửa dàn ý truyện thiếu nhi. Sửa dàn ý dựa trên phản hồi.

DÀN Ý HIỆN TẠI:
- Tiêu đề: {story.Title}
- Mở đầu: {outline?.Opening ?? ""}
- Phát triển: {outline?.Development ?? ""}
- Kết thúc: {outline?.Ending ?? ""}

PHẢN HỒI:
{reasons}

YÊU CẦU:
- Giữ nguyên cấu trúc tổng thể
- Chỉ thay đổi phần cần thiết
- Giữ nội dung phù hợp lứa tuổi

Trả lời JSON:
{{
    ""title"": ""Tiêu đề mới"",
    ""opening"": ""Mở đầu mới"",
    ""development"": ""Phần phát triển mới (chuỗi string liền mạch)"",
    ""ending"": ""Kết thúc mới""
}}

LƯU Ý QUAN TRỌNG:
- Tất cả các trường 'title', 'opening', 'development', 'ending' đều phải là chuỗi văn bản (string), TUYỆT ĐỐI KHÔNG dùng mảng (array).
- Chỉ trả về duy nhất JSON hợp lệ.
";
    }

    private static string BuildStoryPrompt(GenerateStoryRequest request)
    {
        var outline = request.Outline;
        var storyParams = request.StoryParameters;
        var ageSettings = GetAgeSettings(request.AgeBand);

        return $@"Bạn là chuyên gia viết truyện thiếu nhi. Viết truyện hoàn chỉnh dựa trên dàn ý.

DÀN Ý:
- Mở đầu: {outline.Opening}
- Phát triển: {outline.Development}
- Kết thúc: {outline.Ending}

YÊU CẦU VIẾT:
- Độ tuổi: {request.AgeBand} tuổi
- Cấp độ đọc: {request.ReadingLevel}
- Ngôn ngữ: {request.Language}
- Độ phức tạp: {ageSettings["complexity"]}
{(!string.IsNullOrEmpty(storyParams.Genre) ? $"- Thể loại: {storyParams.Genre}" : "")}
{(!string.IsNullOrEmpty(storyParams.Lesson) ? $"- Bài học: {storyParams.Lesson}" : "")}

Trả lời JSON format:
{{
    ""title"": ""Tiêu đề truyện"",
    ""storySections"": [
        {{ ""order"": 1, ""heading"": ""Phần 1"", ""content"": ""Nội dung phần 1"" }},
        {{ ""order"": 2, ""heading"": ""Phần 2"", ""content"": ""Nội dung phần 2"" }}
    ],
    ""lesson"": ""Bài học rút ra""
}}

Chỉ trả về JSON.";
    }

    private static string BuildEvaluateStoryPrompt(EvaluateStoryRequest request)
    {
        var story = request.Story;
        var content = string.Join("\n\n", story.StorySections.Select(s => $"### {s.Heading}\n{s.Content}"));

        return $@"Đánh giá chất lượng truyện thiếu nhi này.

TRUYỆN:
Tiêu đề: {story.Title}
Nội dung:
{content}
Bài học: {story.Lesson}

ĐÁNH GIÁ THEO TIÊU CHÍ (thang 1-10):
1. Tuân thủ dàn ý
2. Phù hợp lứa tuổi
3. Giá trị giáo dục
4. Thu hút người đọc
5. Chất lượng ngôn ngữ

Trả lời JSON:
{{
    ""adherenceScore"": 8,
    ""appropriatenessScore"": 9,
    ""educationalScore"": 7,
    ""engagementScore"": 8,
    ""languageScore"": 8,
    ""strengths"": [""điểm mạnh 1""],
    ""weaknesses"": [""điểm yếu 1""],
    ""recommendations"": [""đề xuất 1""]
}}

Chỉ trả về JSON.";
    }

    private static string BuildStoryContentPrompt(GenerateStoryContentRequest request)
    {
        return BuildStoryPrompt(new GenerateStoryRequest
        {
            RequestId = request.RequestId,
            AgeBand = request.AgeBand,
            ReadingLevel = request.ReadingLevel,
            VocabularyLevel = request.VocabularyLevel,
            Language = request.Language,
            ApprovedOutlineReference = request.ApprovedOutlineReference,
            Outline = request.Outline,
            StoryParameters = request.StoryParameters,
            Constraints = request.Constraints
        });
    }

    private static string BuildRefineContentPrompt(RefineStoryContentRequest request)
    {
        var currentSections = string.Join("\n\n", request.Story.StorySections.Select(s => $"### {s.Heading}\n{s.Content}"));
        var reasons = string.Join("; ", request.Reasons);

        return $@"Chỉnh sửa nội dung truyện dựa trên phản hồi.

TRUYỆN HIỆN TẠI:
Tiêu đề: {request.Story.Title}
Nội dung:
{currentSections}

PHẢN HỒI:
{reasons}

YÊU CẦU:
- Giữ nguyên cấu trúc và bài học
- Chỉ cải thiện phần được yêu cầu
- Giữ nội dung phù hợp lứa tuổi

Trả lời JSON:
{{
    ""title"": ""Tiêu đề"",
    ""storySections"": [
        {{ ""order"": 1, ""heading"": ""Phần 1"", ""content"": ""Nội dung đã chỉnh sửa"" }}
    ],
    ""lesson"": ""Bài học""
}}

Chỉ trả về JSON.";
    }

    private static string BuildVocabularyPrompt(GenerateVocabularyRequest request)
    {
        var story = request.Story;
        var content = string.Join("\n", story.StorySections.Select(s => s.Content));
        var vocabCount = GetVocabularyCount(request.AgeBand);

        return $@"Tạo danh sách từ vựng từ truyện thiếu nhi.

TRUYỆN: {story.Title}
NỘI DUNG: {content}

YÊU CẦU:
- Độ tuổi: {request.AgeBand} tuổi
- Cấp độ từ vựng: {request.VocabularyLevel}
- Số từ: {vocabCount["min"]}-{vocabCount["max"]} từ
- Ngôn ngữ: {request.Language}

CHỌN TỪ:
- Quan trọng để hiểu truyện
- Phù hợp lứa tuổi nhưng mới với người đọc

Trả lời JSON:
{{
    ""vocabulary"": [
        {{ ""term"": ""từ"", ""definition"": ""định nghĩa"" }}
    ]
}}

Chỉ trả về JSON.";
    }

    private static string BuildQuizPrompt(GenerateQuizRequest request)
    {
        var story = request.Story;
        var content = string.Join("\n", story.StorySections.Select(s => s.Content));

        return $@"Tạo câu hỏi quiz từ truyện thiếu nhi.

TRUYỆN: {story.Title}
NỘI DUNG: {content}

YÊU CẦU:
- Độ tuổi: {request.AgeBand} tuổi
- Ngôn ngữ: {request.Language}
- BẮT BUỘC tạo đúng 3 câu hỏi với 3 loại (type) khác nhau:
  1. ""multiple_choice"": câu hỏi trắc nghiệm 4 lựa chọn (options có 4 đáp án, correctOptionIndex là 0-3, correctAnswer là đáp án đúng nằm trong options).
  2. ""true_false"": câu hỏi đúng/sai (options là [""Đúng"", ""Sai""], correctAnswer là ""true"" hoặc ""false"", correctOptionIndex là 0 nếu đúng, 1 nếu sai).
  3. ""short_answer"": câu hỏi trả lời ngắn (options là [], correctOptionIndex là 0, correctAnswer là câu trả lời ngắn gọn).
- Câu hỏi PHẢI chứa tên nhân vật hoặc chi tiết trực tiếp trong truyện.

Trả lời JSON:
{{
    ""quiz"": [
        {{
            ""type"": ""multiple_choice"",
            ""question"": ""Câu hỏi trắc nghiệm liên quan đến truyện?"",
            ""options"": [""Đáp án A"", ""Đáp án B"", ""Đáp án C"", ""Đáp án D""],
            ""correctOptionIndex"": 0,
            ""correctAnswer"": ""Đáp án A"",
            ""explanation"": ""Giải thích ngắn gọn""
        }},
        {{
            ""type"": ""true_false"",
            ""question"": ""Câu hỏi đúng sai liên quan đến sự việc trong truyện?"",
            ""options"": [""Đúng"", ""Sai""],
            ""correctOptionIndex"": 0,
            ""correctAnswer"": ""true"",
            ""explanation"": ""Giải thích ngắn gọn""
        }},
        {{
            ""type"": ""short_answer"",
            ""question"": ""Câu hỏi trả lời ngắn về nhân vật hoặc hành động trong truyện?"",
            ""options"": [],
            ""correctOptionIndex"": 0,
            ""correctAnswer"": ""Câu trả lời ngắn"",
            ""explanation"": ""Giải thích ngắn gọn""
        }}
    ]
}}

Chỉ trả về JSON.";
    }

    private static string BuildDiscussionPrompt(GenerateDiscussionRequest request)
    {
        var story = request.Story;
        var content = string.Join("\n", story.StorySections.Select(s => s.Content));

        return $@"Tạo câu hỏi thảo luận từ truyện thiếu nhi.

TRUYỆN: {story.Title}
NỘI DUNG: {content}

YÊU CẦU:
- Độ tuổi: {request.AgeBand} tuổi
- Số câu hỏi: 3
- Ngôn ngữ: {request.Language}
- Các câu hỏi PHẢI nhắc đến các nhân vật và sự việc trực tiếp trong truyện.

TẠO CÂU HỎI:
1. Hiểu câu chuyện
2. Kết nối cá nhân
3. Tư duy phản biện

Trả lời JSON:
{{
    ""discussionQuestions"": [
        {{ ""question"": ""Câu hỏi 1 nhắc đến nhân vật trong truyện?"" }},
        {{ ""question"": ""Câu hỏi 2 liên hệ bài học câu chuyện với bản thân em?"" }},
        {{ ""question"": ""Câu hỏi 3 về hành động của nhân vật trong truyện?"" }}
    ]
}}

Chỉ trả về JSON.";
    }

    private static string BuildSafetyPrompt(EvaluateContentSafetyRequest request)
    {
        var story = request.Story;
        var content = string.Join("\n", story.StorySections.Select(s => s.Content));

        return $@"Đánh giá an toàn nội dung truyện thiếu nhi.

TRUYỆN: {story.Title}
NỘI DUNG: {content}

YÊU CẦU:
- Độ tuổi: {request.AgeBand} tuổi
- Ngôn ngữ: {request.Language}
{(request.BlockedTopics.Count > 0 ? $"- Chủ đề bị chặn: {string.Join(", ", request.BlockedTopics)}" : "")}
{(request.RestrictedTopics.Count > 0 ? $"- Chủ đề hạn chế: {string.Join(", ", request.RestrictedTopics)}" : "")}

ĐÁNH GIÁ:
1. Bạo lực
2. Yếu tố kinh dị
3. Ngôn ngữ không phù hợp
4. Nội dung nhạy cảm

Trả lời JSON:
{{
    ""isAllowed"": true,
    ""canRefine"": false,
    ""reasonCode"": ""CONTENT_SAFETY_ALLOWED"",
    ""violations"": []
}}

Chỉ trả về JSON.";
    }

    #endregion

    #region Response Parsers

    private static GenerateOutlineResponse ParseOutlineResponse(string json, string requestId)
    {
        try
        {
            var cleaned = CleanJsonResponse(json);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var target = root;
            if (TryGetPropertyCaseInsensitive(root, "outline", out var outlineEl) && outlineEl.ValueKind == JsonValueKind.Object)
                target = outlineEl;
            else if (TryGetPropertyCaseInsensitive(root, "story", out var storyEl) && storyEl.ValueKind == JsonValueKind.Object)
                target = storyEl;

            var title = GetString(target, "title");
            if (string.IsNullOrWhiteSpace(title))
                title = GetString(root, "title");

            return new GenerateOutlineResponse
            {
                RequestId = requestId,
                Title = title,
                Outline = new Contracts.AI.Models.StoryOutlineDto(
                    GetString(target, "opening"),
                    GetString(target, "development"),
                    GetString(target, "ending"))
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse outline response: {ex.Message}", ex);
        }
    }

    private static RefineStoryResponse ParseRefineStoryResponse(string json, string requestId)
    {
        try
        {
            var cleaned = CleanJsonResponse(json);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            return new RefineStoryResponse
            {
                RequestId = requestId,
                Story = new Contracts.AI.Models.StoryPackageDto
                {
                    Title = GetString(root, "title"),
                    Outline = new Contracts.AI.Models.StoryOutlineDto(
                        GetString(root, "opening"),
                        GetString(root, "development"),
                        GetString(root, "ending"))
                }
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse refine story response: {ex.Message}", ex);
        }
    }

    private static GenerateStoryResponse ParseStoryResponse(string json, string requestId)
    {
        try
        {
            var cleaned = CleanJsonResponse(json);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var sections = new List<Contracts.AI.Models.StorySectionDto>();
            if (root.TryGetProperty("storySections", out var sectionsElement))
            {
                var order = 1;
                foreach (var section in sectionsElement.EnumerateArray())
                {
                    sections.Add(new Contracts.AI.Models.StorySectionDto(
                        GetInt(section, "order", order++),
                        GetString(section, "heading"),
                        GetString(section, "content")));
                }
            }

            return new GenerateStoryResponse
            {
                RequestId = requestId,
                Story = new Contracts.AI.Models.StoryPackageDto
                {
                    Title = GetString(root, "title"),
                    StorySections = sections,
                    Lesson = GetString(root, "lesson")
                }
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse story response: {ex.Message}", ex);
        }
    }

    private static GenerateStoryContentResponse ParseStoryContentResponse(string json, string requestId)
    {
        try
        {
            var cleaned = CleanJsonResponse(json);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var sections = new List<Contracts.AI.Models.StorySectionDto>();
            if (root.TryGetProperty("storySections", out var sectionsElement))
            {
                var order = 1;
                foreach (var section in sectionsElement.EnumerateArray())
                {
                    sections.Add(new Contracts.AI.Models.StorySectionDto(
                        GetInt(section, "order", order++),
                        GetString(section, "heading"),
                        GetString(section, "content")));
                }
            }

            return new GenerateStoryContentResponse
            {
                RequestId = requestId,
                Story = new Contracts.AI.Models.StoryContentDto
                {
                    Title = GetString(root, "title"),
                    StorySections = sections,
                    Lesson = GetString(root, "lesson")
                }
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse story content response: {ex.Message}", ex);
        }
    }

    private static RefineStoryContentResponse ParseRefineStoryContentResponse(string json, string requestId)
    {
        var contentResponse = ParseStoryContentResponse(json, requestId);
        return new RefineStoryContentResponse
        {
            RequestId = requestId,
            Story = contentResponse.Story
        };
    }

    private static GenerateVocabularyResponse ParseVocabularyResponse(string json, string requestId)
    {
        try
        {
            var cleaned = CleanJsonResponse(json);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var vocabulary = new List<Contracts.AI.Models.GeneratedVocabularyItemDto>();
            if (root.TryGetProperty("vocabulary", out var vocabElement))
            {
                foreach (var item in vocabElement.EnumerateArray())
                {
                    vocabulary.Add(new Contracts.AI.Models.GeneratedVocabularyItemDto(
                        GetString(item, "term"),
                        GetString(item, "definition")));
                }
            }

            return new GenerateVocabularyResponse
            {
                RequestId = requestId,
                Items = vocabulary
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse vocabulary response: {ex.Message}", ex);
        }
    }

    private static GenerateQuizResponse ParseQuizResponse(string json, string requestId)
    {
        try
        {
            var cleaned = CleanJsonResponse(json);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var quiz = new List<Contracts.AI.Models.QuizItemDto>();
            if (root.TryGetProperty("quiz", out var quizElement))
            {
                foreach (var item in quizElement.EnumerateArray())
                {
                    var options = new List<string>();
                    if (item.TryGetProperty("options", out var optionsElement))
                    {
                        foreach (var opt in optionsElement.EnumerateArray())
                        {
                            options.Add(opt.GetString() ?? "");
                        }
                    }

                    quiz.Add(new Contracts.AI.Models.QuizItemDto
                    {
                        Type = GetString(item, "type"),
                        Question = GetString(item, "question"),
                        Options = options,
                        CorrectOptionIndex = GetInt(item, "correctOptionIndex"),
                        CorrectAnswer = GetString(item, "correctAnswer"),
                        Explanation = GetString(item, "explanation")
                    });
                }
            }

            return new GenerateQuizResponse
            {
                RequestId = requestId,
                Items = quiz
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse quiz response: {ex.Message}", ex);
        }
    }

    private static GenerateDiscussionResponse ParseDiscussionResponse(string json, string requestId)
    {
        try
        {
            var cleaned = CleanJsonResponse(json);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var questions = new List<Contracts.AI.Models.DiscussionQuestionDto>();
            if (root.TryGetProperty("discussionQuestions", out var questionsElement))
            {
                foreach (var item in questionsElement.EnumerateArray())
                {
                    questions.Add(new Contracts.AI.Models.DiscussionQuestionDto(
                        GetString(item, "question")));
                }
            }

            return new GenerateDiscussionResponse
            {
                RequestId = requestId,
                Items = questions
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse discussion response: {ex.Message}", ex);
        }
    }

    private static EvaluateStoryResponse ParseEvaluateStoryResponse(string json, string requestId)
    {
        try
        {
            var cleaned = CleanJsonResponse(json);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            return new EvaluateStoryResponse
            {
                RequestId = requestId,
                Evaluation = new Contracts.AI.Models.EvaluationResultDto
                {
                    SchemaValid = true,
                    SafetyPassed = true,
                    ReadabilityPassed = true,
                    VocabularyPassed = true
                }
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse evaluation response: {ex.Message}", ex);
        }
    }

    private static EvaluateContentSafetyResponse ParseSafetyResponse(string json, string requestId)
    {
        try
        {
            var cleaned = CleanJsonResponse(json);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            return new EvaluateContentSafetyResponse
            {
                RequestId = requestId,
                IsAllowed = GetBool(root, "isAllowed"),
                CanRefine = GetBool(root, "canRefine"),
                ReasonCode = GetString(root, "reasonCode"),
                Violations = GetStringArray(root, "violations")
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse safety response: {ex.Message}", ex);
        }
    }

    #endregion

    #region Helper Methods

    private static string CleanJsonResponse(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```"))
            cleaned = cleaned[3..];
        if (cleaned.EndsWith("```"))
            cleaned = cleaned[..^3];
        cleaned = cleaned.Trim();

        var firstBrace = cleaned.IndexOf('{');
        var lastBrace = cleaned.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return cleaned;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string property)
    {
        if (!TryGetPropertyCaseInsensitive(element, property, out var prop))
            return string.Empty;

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join("\n\n", prop.EnumerateArray().Select(item =>
            {
                if (item.ValueKind == JsonValueKind.String)
                    return item.GetString() ?? string.Empty;
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var title = TryGetPropertyCaseInsensitive(item, "chapter_title", out var t) || TryGetPropertyCaseInsensitive(item, "heading", out t) || TryGetPropertyCaseInsensitive(item, "title", out t) ? t.GetString() : null;
                    var content = TryGetPropertyCaseInsensitive(item, "content", out var c) || TryGetPropertyCaseInsensitive(item, "text", out c) ? c.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(content))
                        return $"{title}: {content}";
                    return content ?? title ?? item.GetRawText();
                }
                return item.ToString();
            })),
            JsonValueKind.Number => prop.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _ => prop.GetRawText()
        };
    }

    private static int GetInt(JsonElement element, string property, int defaultValue = 0)
    {
        if (!TryGetPropertyCaseInsensitive(element, property, out var prop))
            return defaultValue;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
            return val;
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
            return parsed;
        return defaultValue;
    }

    private static double GetDouble(JsonElement element, string property)
    {
        if (!TryGetPropertyCaseInsensitive(element, property, out var prop))
            return 0;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var val))
            return val;
        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var parsed))
            return parsed;
        return 0;
    }

    private static bool GetBool(JsonElement element, string property)
    {
        if (!TryGetPropertyCaseInsensitive(element, property, out var prop))
            return false;

        if (prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return prop.GetBoolean();
        if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out var parsed))
            return parsed;
        return false;
    }

    private static string[] GetStringArray(JsonElement element, string property)
    {
        if (!TryGetPropertyCaseInsensitive(element, property, out var prop))
            return Array.Empty<string>();

        if (prop.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                result.Add(item.GetString() ?? "");
            else
                result.Add(item.ToString());
        }
        return result.ToArray();
    }

    private static Dictionary<string, string> GetAgeSettings(string ageBand) => ageBand switch
    {
        "6-8" => new() { ["complexity"] = "Đơn giản", ["chapter_count"] = "3-4" },
        "9-11" => new() { ["complexity"] = "Vừa", ["chapter_count"] = "4-6" },
        "12-14" => new() { ["complexity"] = "Phức tạp", ["chapter_count"] = "5-8" },
        _ => new() { ["complexity"] = "Đơn giản", ["chapter_count"] = "3-4" }
    };

    private static Dictionary<string, string> GetVocabularyCount(string ageBand) => ageBand switch
    {
        "6-8" => new() { ["min"] = "3", ["max"] = "5" },
        "9-11" => new() { ["min"] = "5", ["max"] = "8" },
        "12-14" => new() { ["min"] = "8", ["max"] = "12" },
        _ => new() { ["min"] = "3", ["max"] = "5" }
    };

    private static Dictionary<string, string> GetQuestionCount(string ageBand) => ageBand switch
    {
        "6-8" => new() { ["min"] = "3", ["max"] = "5" },
        "9-11" => new() { ["min"] = "5", ["max"] = "8" },
        "12-14" => new() { ["min"] = "8", ["max"] = "10" },
        _ => new() { ["min"] = "3", ["max"] = "5" }
    };

    #endregion

    #region Gemini API Models

    private sealed class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig GenerationConfig { get; set; } = new();
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }

        [JsonPropertyName("topP")]
        public double TopP { get; set; }

        [JsonPropertyName("topK")]
        public int TopK { get; set; }
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public GeminiCandidate[]? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    #endregion
}
