# Mốc từng chữ cho audio Gemini TTS bằng Google Cloud STT — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Giữ giọng Gemini TTS nhưng vẫn có mốc thời gian từng chữ (`WordTimings`) để frontend tô chữ theo giọng đọc, bằng cách gửi audio vừa tạo qua Google Cloud Speech-to-Text V2 rồi căn các chữ nghe được vào văn bản gốc.

**Architecture:** Một decorator `SttAlignedTtsProvider` bọc `GeminiTtsProvider`: sau khi Gemini trả audio, gọi `IAudioWordAligner` (STT V2, bật `enableWordTimeOffsets`), rồi `WordTimingAligner` (thuần, không IO) căn chuỗi chữ nghe được với các token chữ của `SsmlTokenizer`. Kết quả đặt vào metadata `wordTimingsJson` (cùng định dạng `[{word,start,end}]` của Google Cloud TTS) và `hasWordTimings=true`, nên `MediaGenerationService`, listening media và `BinaryAudioQualityGate` hiện có chạy nguyên không đổi. Lỗi STT hoặc căn không đạt thì trả audio gốc không có mốc (không bao giờ chặn tạo audio).

**Tech Stack:** .NET, xUnit, Google Cloud Speech-to-Text V2 qua REST (đã có `GoogleSpeechToTextRecognizer`, `SpeechToTextAuthHandler`, `SpeechToTextOptions` mục `AI:STT`).

**Spec:** `src/AI/Phase5_TTS_Production_WordLevel_Agent_Guide.md` (mục 3 quy tắc 4, Phase 5) và `src/AI/TTS_Current_Implementation_Survey.md`. Plan này là phiên bản thu gọn: không đổi schema, không migration, không đổi API.

## Global Constraints

- Không timestamp bịa từ `duration / tokenCount`; mốc chỉ lấy từ bằng chứng STT. Chữ không nghe khớp chỉ được nội suy **giữa hai chữ đã khớp** và chỉ khi độ phủ ≥ `MinAlignmentCoverage`.
- Transcript STT không bao giờ thay văn bản truyện; chỉ dùng làm bằng chứng căn.
- Không ghi log âm thanh, nội dung văn bản trẻ, token hay credential.
- Mặc định `AlignWordTimingsWithStt = false`: không đổi hành vi nếu chưa bật.
- Lỗi STT / audio quá dài / căn không đạt → trả audio gốc, `hasWordTimings=false`; `OperationCanceledException` vẫn phải ném lên.
- Giữ tương thích định dạng `wordTimingsJson`: mảng `{ "word": string, "start": number, "end": number|null }`, camelCase, số làm tròn 3 chữ số, số phần tử = số token `IsWord` của `SsmlTokenizer.Tokenize(text)`.
- Không thêm dependency NuGet; không migration; không đổi schema DB.

## File Structure

| Hành động | File | Trách nhiệm |
|---|---|---|
| CREATE | `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/WordTimingAligner.cs` | Thuật toán thuần: căn chuỗi chữ nghe được vào token nguồn, nội suy, sinh JSON |
| MODIFY | `src/Core/StoryPlatform.Application/Features/MediaGeneration/Interfaces/IMediaGenerationServices.cs` | Thêm `TimedWord` và `IAudioWordAligner` cạnh `ITtsProvider` |
| CREATE | `src/Core/StoryPlatform.Infrastructure/AI/GoogleSpeechWordAligner.cs` | Gọi STT V2, parse `startOffset/endOffset` → `TimedWord` |
| CREATE | `src/Core/StoryPlatform.Infrastructure/AI/SttAlignedTtsProvider.cs` | Decorator `ITtsProvider` |
| MODIFY | `src/Core/StoryPlatform.Infrastructure/AI/TtsServiceOptions.cs` | 3 option mới |
| MODIFY | `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs` | Đăng ký aligner + bọc Gemini khi bật |
| CREATE | `tests/StoryPlatform.UnitTests/Features/MediaGeneration/WordTimingAlignerTests.cs` | Test thuật toán |
| CREATE | `tests/StoryPlatform.UnitTests/Features/MediaGeneration/GoogleSpeechWordAlignerTests.cs` | Test gọi/parse STT |
| CREATE | `tests/StoryPlatform.UnitTests/Features/MediaGeneration/SttAlignedTtsProviderTests.cs` | Test decorator + qua `BinaryAudioQualityGate` |

Bước đầu thực thi: kiểm tra thư mục `tests/StoryPlatform.UnitTests/Features/MediaGeneration/` đã tồn tại chưa; nếu chưa, đặt 3 file test vào thư mục test MediaGeneration hiện có (tìm bằng `Glob **/*MediaGeneration*Tests.cs`) và đổi namespace cho khớp.

---

### Task 1: Thuật toán căn mốc `WordTimingAligner`

**Việc này làm gì (nói dễ hiểu):** Khi Gemini đọc xong, ta chỉ có file âm thanh. Dịch vụ nhận dạng giọng nói sẽ "nghe lại" và cho biết mỗi chữ nghe được bắt đầu/kết thúc lúc nào. Nhưng chữ nghe được có thể sai vài chữ, thiếu hoặc thừa. Task này viết phần "ghép" chữ nghe được với văn bản truyện gốc để mỗi chữ trong truyện có một mốc thời gian, và từ chối khi ghép quá kém để không hiện mốc sai cho bé.

**Cần thêm / còn thiếu:**
- Đã có: `SsmlTokenizer.Tokenize` + `TimingMark.IsWord` (token chữ nguồn), định dạng `wordTimingsJson` của Google Cloud TTS.
- Cần thêm: lớp thuần `WordTimingAligner` (chuẩn hóa so sánh, LCS, nội suy giữa hai mốc khớp, ngưỡng độ phủ, sinh JSON) và record `TimedWord`.
- Còn thiếu / ngoài phạm vi: gọi STT thật (Task 2), gắn vào luồng TTS (Task 3).

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/WordTimingAligner.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/MediaGeneration/Interfaces/IMediaGenerationServices.cs` (thêm `TimedWord`, `IAudioWordAligner` sau `ITtsProvider`)
- Test: `tests/StoryPlatform.UnitTests/Features/MediaGeneration/WordTimingAlignerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TimedWord(string Text, double StartSeconds, double EndSeconds);`
  - `public interface IAudioWordAligner { Task<IReadOnlyList<TimedWord>> RecognizeWordsAsync(byte[] audio, CancellationToken cancellationToken = default); }`
  - `public static class WordTimingAligner { public static string? BuildWordTimingsJson(IReadOnlyList<TimingMark> tokens, IReadOnlyList<TimedWord> heard, double minCoverage); }` — trả `null` khi không đủ độ phủ hoặc không có chữ nào để căn.

- [ ] **Step 1: Thêm hợp đồng vào `IMediaGenerationServices.cs`**

Chèn ngay sau `interface ITtsProvider { ... }`:

```csharp
/// <summary>Một chữ nghe được trong audio, kèm mốc bắt đầu/kết thúc (giây).</summary>
public sealed record TimedWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>Nghe lại audio đã tạo và trả mốc từng chữ nghe được. Không đổi văn bản truyện.</summary>
public interface IAudioWordAligner
{
    Task<IReadOnlyList<TimedWord>> RecognizeWordsAsync(byte[] audio, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Viết test thất bại**

```csharp
using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using Xunit;

namespace StoryPlatform.UnitTests.Features.MediaGeneration;

public sealed class WordTimingAlignerTests
{
    private static IReadOnlyList<TimedWord> Heard(params (string W, double S, double E)[] words) =>
        words.Select(w => new TimedWord(w.W, w.S, w.E)).ToList();

    private static List<(string Word, double Start, double? End)> Parse(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Select(e => (e.GetProperty("word").GetString()!, e.GetProperty("start").GetDouble(),
                e.GetProperty("end").ValueKind == JsonValueKind.Null ? (double?)null : e.GetProperty("end").GetDouble()))
            .ToList();

    [Fact]
    public void ExactMatch_GivesEachSourceWordItsHeardTiming()
    {
        var tokens = SsmlTokenizer.Tokenize("Thỏ con đi lạc.");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("thỏ", 0.0, 0.4), ("con", 0.4, 0.7), ("đi", 0.7, 0.9), ("lạc", 0.9, 1.3)), 0.8);

        var timings = Parse(json!);
        Assert.Equal(["Thỏ", "con", "đi", "lạc"], timings.Select(t => t.Word));
        Assert.Equal([0.0, 0.4, 0.7, 0.9], timings.Select(t => t.Start));
        Assert.Equal([0.4, 0.7, 0.9, 1.3], timings.Select(t => t.End!.Value));
    }

    [Fact]
    public void CaseDiacriticsPunctuationAndNormalizationForm_AreIgnoredWhenMatching()
    {
        var tokens = SsmlTokenizer.Tokenize("Thỏ, con!");
        var decomposed = "Thỏ".Normalize(System.Text.NormalizationForm.FormD);   // "Thỏ" dạng NFD
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard((decomposed.ToUpperInvariant(), 0.0, 0.5), ("CON.", 0.5, 0.9)), 1.0);

        Assert.NotNull(json);
        Assert.Equal(2, Parse(json).Count);
    }

    [Fact]
    public void RepeatedWords_AreMatchedInOrder()
    {
        var tokens = SsmlTokenizer.Tokenize("ơi ơi ơi");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("ơi", 0.0, 0.3), ("ơi", 0.5, 0.8), ("ơi", 1.0, 1.3)), 1.0);

        Assert.Equal([0.0, 0.5, 1.0], Parse(json!).Select(t => t.Start));
    }

    [Fact]
    public void UnheardWordBetweenTwoMatches_IsInterpolatedInsideTheGap()
    {
        // STT bỏ sót "con": nằm giữa "thỏ" (kết thúc 0.4) và "đi" (bắt đầu 1.0).
        var tokens = SsmlTokenizer.Tokenize("thỏ con đi lạc");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("thỏ", 0.0, 0.4), ("đi", 1.0, 1.2), ("lạc", 1.2, 1.6)), 0.7);

        var timings = Parse(json!);
        Assert.Equal(4, timings.Count);
        Assert.InRange(timings[1].Start, 0.4, 1.0);
        Assert.InRange(timings[1].End!.Value, timings[1].Start, 1.0);
    }

    [Fact]
    public void ExtraHeardWord_IsIgnored()
    {
        var tokens = SsmlTokenizer.Tokenize("thỏ con");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("thỏ", 0.0, 0.4), ("ừm", 0.4, 0.6), ("con", 0.6, 0.9)), 1.0);

        Assert.Equal([0.0, 0.6], Parse(json!).Select(t => t.Start));
    }

    [Fact]
    public void CoverageBelowMinimum_ReturnsNull()
    {
        var tokens = SsmlTokenizer.Tokenize("thỏ con đi lạc vào rừng");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens, Heard(("thỏ", 0.0, 0.4), ("rừng", 2.0, 2.4)), 0.8);

        Assert.Null(json);
    }

    [Fact]
    public void NothingHeardOrNoSourceWords_ReturnsNull()
    {
        Assert.Null(WordTimingAligner.BuildWordTimingsJson(SsmlTokenizer.Tokenize("thỏ con"), Heard(), 0.5));
        Assert.Null(WordTimingAligner.BuildWordTimingsJson(SsmlTokenizer.Tokenize("..."), Heard(("thỏ", 0, 1)), 0.5));
    }

    [Fact]
    public void Output_IsMonotonicAndHasOneEntryPerSourceWord()
    {
        var tokens = SsmlTokenizer.Tokenize("a b c d e f");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("a", 0.0, 0.2), ("c", 0.5, 0.7), ("e", 1.0, 1.2), ("f", 1.2, 1.4)), 0.6);

        var timings = Parse(json!);
        Assert.Equal(6, timings.Count);
        for (var i = 0; i < timings.Count; i++)
        {
            Assert.True(timings[i].End >= timings[i].Start);
            if (i > 0) Assert.True(timings[i].Start >= timings[i - 1].End - 1e-9);
        }
    }
}
```

- [ ] **Step 3: Chạy test, xác nhận thất bại (chưa có `WordTimingAligner`)**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~WordTimingAlignerTests"`
Expected: lỗi biên dịch "WordTimingAligner does not exist".

- [ ] **Step 4: Cài đặt `WordTimingAligner.cs`**

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

/// <summary>
/// Căn chữ nghe được trong audio (từ STT) vào các token chữ của văn bản gốc để lấy mốc từng chữ.
/// Văn bản truyện không bao giờ bị đổi; transcript chỉ là bằng chứng. Chữ không nghe khớp chỉ được nội suy
/// giữa hai chữ đã khớp, và chỉ khi độ phủ đủ cao, nên không bao giờ có mốc "bịa" từ độ dài tổng.
/// </summary>
public static class WordTimingAligner
{
    private sealed record Entry(string Word, double Start, double? End);

    /// <returns>JSON <c>[{word,start,end}]</c> (một phần tử cho mỗi token chữ), hoặc null nếu không đủ độ phủ.</returns>
    public static string? BuildWordTimingsJson(
        IReadOnlyList<TimingMark> tokens, IReadOnlyList<TimedWord> heard, double minCoverage)
    {
        var source = tokens.Where(t => t.IsWord).ToList();
        if (source.Count == 0 || heard.Count == 0) return null;

        var sourceKeys = source.Select(t => Normalize(t.Text)).ToArray();
        var heardKeys = heard.Select(h => Normalize(h.Text)).ToArray();
        var match = LongestCommonSubsequence(sourceKeys, heardKeys);   // match[i] = chỉ số heard khớp với source i, hoặc -1

        var matched = match.Count(m => m >= 0);
        if (matched == 0 || (double)matched / source.Count < minCoverage) return null;

        var start = new double[source.Count];
        var end = new double[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            if (match[i] < 0) continue;
            start[i] = heard[match[i]].StartSeconds;
            end[i] = Math.Max(heard[match[i]].EndSeconds, heard[match[i]].StartSeconds);
        }

        FillGaps(source, match, heard, start, end);

        var entries = new List<Entry>(source.Count);
        var previousEnd = 0.0;
        for (var i = 0; i < source.Count; i++)
        {
            var s = Math.Max(start[i], previousEnd);
            var e = Math.Max(end[i], s);
            previousEnd = e;
            entries.Add(new Entry(source[i].Text, Math.Round(s, 3), Math.Round(e, 3)));
        }

        return JsonSerializer.Serialize(
            entries.Select(x => new { word = x.Word, start = x.Start, end = x.End }),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    /// <summary>Nội suy các chữ chưa khớp theo độ dài chữ, trong khoảng giữa hai chữ đã khớp (hoặc đầu/cuối audio nghe được).</summary>
    private static void FillGaps(List<TimingMark> source, int[] match, IReadOnlyList<TimedWord> heard, double[] start, double[] end)
    {
        var i = 0;
        while (i < source.Count)
        {
            if (match[i] >= 0) { i++; continue; }
            var from = i;
            while (i < source.Count && match[i] < 0) i++;
            var to = i;   // [from, to) là đoạn chưa khớp

            var gapStart = from > 0 ? end[from - 1] : heard[0].StartSeconds;
            var gapEnd = to < source.Count ? start[to] : heard[^1].EndSeconds;
            if (gapEnd < gapStart) gapEnd = gapStart;

            var weights = Enumerable.Range(from, to - from).Select(k => Math.Max(1, source[k].Text.Length)).ToArray();
            var total = weights.Sum();
            var cursor = gapStart;
            for (var k = from; k < to; k++)
            {
                var span = (gapEnd - gapStart) * weights[k - from] / total;
                start[k] = cursor;
                end[k] = cursor + span;
                cursor += span;
            }
        }
    }

    /// <summary>Chuẩn hóa chỉ để so sánh: NFC, chữ thường, bỏ dấu câu/khoảng trắng; giữ dấu thanh tiếng Việt.</summary>
    internal static string Normalize(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormC).ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    private static int[] LongestCommonSubsequence(string[] a, string[] b)
    {
        var n = a.Length; var m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = a[i].Length > 0 && a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var result = Enumerable.Repeat(-1, n).ToArray();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x].Length > 0 && a[x] == b[y]) { result[x] = y; x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) x++;
            else y++;
        }
        return result;
    }
}
```

- [ ] **Step 5: Chạy test, xác nhận đạt**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~WordTimingAlignerTests"`
Expected: PASS (8 test).

---

### Task 2: Gọi STT lấy mốc từng chữ `GoogleSpeechWordAligner`

**Việc này làm gì (nói dễ hiểu):** Task này làm bước "nghe lại": gửi file âm thanh lên dịch vụ nhận dạng giọng nói của Google, nhận về danh sách chữ nghe được kèm giây bắt đầu/kết thúc. Hệ thống đã có kết nối STT cho chức năng bé đọc to, nhưng phần đó bỏ đi mốc thời gian; ta thêm một lớp riêng dùng lại cấu hình, xác thực và cách báo lỗi sẵn có.

**Cần thêm / còn thiếu:**
- Đã có: `SpeechToTextOptions` (mục `AI:STT`), `SpeechToTextAuthHandler`, `GoogleSpeechToTextRecognizer.RecognizeUri/DescribeError`, `SpeechRecognitionUnavailableException`.
- Cần thêm: `GoogleSpeechWordAligner : IAudioWordAligner` đọc `startOffset`/`endOffset` (chuỗi dạng `"1.200s"`), đăng ký typed HttpClient.
- Còn thiếu / ngoài phạm vi: không đổi `GoogleSpeechToTextRecognizer` (đọc to của bé không đổi); không dùng batch STT cho audio dài (xử lý bằng giới hạn ở Task 3).

**Files:**
- Create: `src/Core/StoryPlatform.Infrastructure/AI/GoogleSpeechWordAligner.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs` (cạnh `GoogleSpeechToTextRecognizer`, dòng ~240)
- Test: `tests/StoryPlatform.UnitTests/Features/MediaGeneration/GoogleSpeechWordAlignerTests.cs`

**Interfaces:**
- Consumes: `IAudioWordAligner`, `TimedWord` (Task 1); `GoogleSpeechToTextRecognizer.RecognizeUri(SpeechToTextOptions)` và `DescribeError(string)` (internal static, cùng assembly — thêm `InternalsVisibleTo` đã có sẵn cho test project vì test hiện tại gọi chúng).
- Produces: `public sealed class GoogleSpeechWordAligner(HttpClient http, IOptions<SpeechToTextOptions> options, ILogger<GoogleSpeechWordAligner> logger) : IAudioWordAligner`; `internal static IReadOnlyList<TimedWord> ParseTimedWords(string json)`; `internal static double ParseOffsetSeconds(string? offset)`.

- [ ] **Step 1: Viết test thất bại**

```csharp
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Features.MediaGeneration;

public sealed class GoogleSpeechWordAlignerTests
{
    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }

    private static GoogleSpeechWordAligner Create(Transport transport) =>
        new(new HttpClient(transport),
            Microsoft.Extensions.Options.Options.Create(new SpeechToTextOptions { ProjectId = "demo", Location = "global", Model = "long", LanguageCode = "vi-VN" }),
            NullLogger<GoogleSpeechWordAligner>.Instance);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("1.200s", 1.2)]
    [InlineData("0s", 0.0)]
    [InlineData("12.5s", 12.5)]
    [InlineData(null, 0.0)]
    [InlineData("bad", 0.0)]
    public void ParseOffsetSeconds_ReadsProtoDurations(string? input, double expected) =>
        Assert.Equal(expected, GoogleSpeechWordAligner.ParseOffsetSeconds(input), 3);

    [Fact]
    public void ParseTimedWords_ReadsWordsAndOffsetsAcrossResults()
    {
        var json = """
            {"results":[
              {"alternatives":[{"words":[{"word":"thỏ","startOffset":"0s","endOffset":"0.400s"},{"word":"con","startOffset":"0.400s","endOffset":"0.700s"}]}]},
              {"alternatives":[{"words":[{"word":"đi","startOffset":"0.900s","endOffset":"1.100s"}]}]}
            ]}
            """;
        var words = GoogleSpeechWordAligner.ParseTimedWords(json);
        Assert.Equal(["thỏ", "con", "đi"], words.Select(w => w.Text));
        Assert.Equal([0.0, 0.4, 0.9], words.Select(w => w.StartSeconds));
        Assert.Equal(1.1, words[2].EndSeconds, 3);
    }

    [Fact]
    public void ParseTimedWords_NoResultsOrNoWords_ReturnsEmpty()
    {
        Assert.Empty(GoogleSpeechWordAligner.ParseTimedWords("{}"));
        Assert.Empty(GoogleSpeechWordAligner.ParseTimedWords("""{"results":[{"alternatives":[{"transcript":"x"}]}]}"""));
    }

    [Fact]
    public async Task RecognizeWordsAsync_RequestsWordTimeOffsets()
    {
        var transport = new Transport(_ => Json("""{"results":[{"alternatives":[{"words":[{"word":"a","startOffset":"0s","endOffset":"1s"}]}]}]}"""));
        var words = await Create(transport).RecognizeWordsAsync([1, 2, 3]);

        Assert.Single(words);
        Assert.Contains("\"enableWordTimeOffsets\":true", transport.Bodies.Single());
        Assert.Contains("\"languageCodes\":[\"vi-VN\"]", transport.Bodies.Single());
    }

    [Fact]
    public async Task RecognizeWordsAsync_HttpFailure_ThrowsSpeechRecognitionUnavailable()
    {
        var transport = new Transport(_ => Json("""{"error":{"status":"PERMISSION_DENIED"}}""", HttpStatusCode.Forbidden));
        await Assert.ThrowsAsync<SpeechRecognitionUnavailableException>(() => Create(transport).RecognizeWordsAsync([1]));
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~GoogleSpeechWordAlignerTests"`
Expected: lỗi biên dịch (chưa có `GoogleSpeechWordAligner`).

- [ ] **Step 3: Cài đặt `GoogleSpeechWordAligner.cs`**

```csharp
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Nghe lại audio đã tạo bằng Google Cloud Speech-to-Text V2 (recognize đồng bộ qua REST) và trả mốc từng chữ.
/// Âm thanh chỉ nằm trong bộ nhớ; khi lỗi chỉ log mã HTTP và lý do cấu hình Google trả về, không log âm thanh hay văn bản.
/// </summary>
public sealed class GoogleSpeechWordAligner(
    HttpClient http, IOptions<SpeechToTextOptions> options, ILogger<GoogleSpeechWordAligner> logger)
    : IAudioWordAligner
{
    private readonly SpeechToTextOptions _options = options.Value;

    public async Task<IReadOnlyList<TimedWord>> RecognizeWordsAsync(byte[] audio, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            config = new
            {
                autoDecodingConfig = new { },
                languageCodes = new[] { _options.LanguageCode },
                model = _options.Model,
                features = new { enableWordTimeOffsets = true }
            },
            content = Convert.ToBase64String(audio)
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 120)));
        try
        {
            using var response = await http.PostAsJsonAsync(GoogleSpeechToTextRecognizer.RecognizeUri(_options), body, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var (googleStatus, reason, _) = GoogleSpeechToTextRecognizer.DescribeError(await SafeReadAsync(response, timeout.Token));
                logger.LogWarning("TTS word alignment STT failed Status={Status} GoogleStatus={GoogleStatus} Reason={Reason}",
                    (int)response.StatusCode, googleStatus, reason);
                throw new SpeechRecognitionUnavailableException();
            }
            return ParseTimedWords(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("TTS word alignment STT unavailable Error={Error}", exception.GetType().Name);
            throw new SpeechRecognitionUnavailableException();
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException) { return string.Empty; }
    }

    internal static IReadOnlyList<TimedWord> ParseTimedWords(string json)
    {
        var words = new List<TimedWord>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results)) return words;

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("alternatives", out var alternatives) || alternatives.GetArrayLength() == 0) continue;
            if (!alternatives[0].TryGetProperty("words", out var list)) continue;
            foreach (var word in list.EnumerateArray())
            {
                var text = word.TryGetProperty("word", out var value) ? value.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;
                words.Add(new TimedWord(text,
                    ParseOffsetSeconds(word.TryGetProperty("startOffset", out var s) ? s.GetString() : null),
                    ParseOffsetSeconds(word.TryGetProperty("endOffset", out var e) ? e.GetString() : null)));
            }
        }
        return words;
    }

    /// <summary>Duration của proto JSON có dạng "1.200s".</summary>
    internal static double ParseOffsetSeconds(string? offset)
    {
        if (string.IsNullOrWhiteSpace(offset) || !offset.EndsWith('s')) return 0;
        return double.TryParse(offset[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
    }
}
```

- [ ] **Step 4: Đăng ký trong `DependencyInjection.cs`**

Ngay sau dòng `services.AddScoped<ISpeechRecognizer>(...)` (~242) thêm:

```csharp
services.AddHttpClient<GoogleSpeechWordAligner>(client => client.Timeout = TimeSpan.FromSeconds(60))
    .AddHttpMessageHandler<SpeechToTextAuthHandler>();
services.AddScoped<IAudioWordAligner>(sp => sp.GetRequiredService<GoogleSpeechWordAligner>());
```

- [ ] **Step 5: Chạy test, xác nhận đạt và build sạch**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~GoogleSpeechWordAlignerTests"` → PASS (9 case).
Run: `dotnet build` → 0 lỗi.

---

### Task 3: Decorator `SttAlignedTtsProvider` + cấu hình + DI

**Việc này làm gì (nói dễ hiểu):** Đây là chỗ nối mọi thứ lại. Mỗi khi Gemini tạo xong một đoạn audio, nếu tính năng được bật thì hệ thống tự gửi đoạn audio đó đi "nghe lại" (Task 2), ghép mốc vào văn bản gốc (Task 1) và đính kèm vào audio. Phần còn lại của hệ thống (lưu, hiển thị cho bé, kiểm chất lượng) không cần sửa vì đã quen dùng mốc từ Google Cloud TTS. Nếu có lỗi hoặc ghép quá kém, audio vẫn dùng bình thường, chỉ không tô chữ.

**Cần thêm / còn thiếu:**
- Đã có: `GatedTtsProvider`, `ConfigurationFingerprint`, `BinaryAudioQualityGate` (đòi số mốc = số chữ khi `hasWordTimings` khác `false`), `MediaGenerationService`/`ListeningMedia` đã lưu `wordTimingsJson` vào `MediaAsset.WordTimings`.
- Cần thêm: decorator, 3 option (`AlignWordTimingsWithStt=false`, `MinAlignmentCoverage=0.8`, `MaxAlignedAudioBytes=2_800_000`), bọc trong DI chỉ khi provider là Gemini và option bật, và dấu vân tay `+stt1` để audio cũ không có mốc không bị dùng lại.
- Còn thiếu / ngoài phạm vi: audio Gemini đã tạo trước đây vẫn không có mốc (cần chạy lại tạo audio hoặc làm backfill riêng sau); mọi audio Gemini đều tốn một lần gọi STT kể cả câu phản hồi không cần tô chữ (xem Rủi ro).

**Files:**
- Create: `src/Core/StoryPlatform.Infrastructure/AI/SttAlignedTtsProvider.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/AI/TtsServiceOptions.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs` (khối `ITtsProvider`, ~dòng 226)
- Test: `tests/StoryPlatform.UnitTests/Features/MediaGeneration/SttAlignedTtsProviderTests.cs`

**Interfaces:**
- Consumes: `IAudioWordAligner`, `TimedWord`, `WordTimingAligner.BuildWordTimingsJson` (Task 1–2); `GeneratedMedia(byte[] Content, string MimeType, IReadOnlyDictionary<string,string>? Metadata)`; `SsmlTokenizer.Tokenize`.
- Produces: `internal sealed class SttAlignedTtsProvider(ITtsProvider inner, IServiceScopeFactory scopes, IOptions<TtsServiceOptions> options, ILogger<SttAlignedTtsProvider> logger) : ITtsProvider`; option `TtsServiceOptions.AlignWordTimingsWithStt`, `MinAlignmentCoverage`, `MaxAlignedAudioBytes`.

- [ ] **Step 1: Thêm option vào `TtsServiceOptions.cs`** (sau `EnableWordTimings`)

```csharp
    /// <summary>Gemini TTS không có mốc từng chữ: bật thì nghe lại audio bằng STT để lấy mốc (mặc định tắt).</summary>
    public bool AlignWordTimingsWithStt { get; set; } = false;
    /// <summary>Tỷ lệ chữ nguồn phải khớp chữ nghe được thì mới dùng mốc.</summary>
    public double MinAlignmentCoverage { get; set; } = 0.8;
    /// <summary>STT đồng bộ giới hạn ~1 phút: audio lớn hơn ngưỡng này (WAV 24 kHz 16-bit mono ≈ 58 giây) thì bỏ qua căn mốc.</summary>
    public int MaxAlignedAudioBytes { get; set; } = 2_800_000;
```

Không thêm `AlignWordTimingsWithStt` vào `Fingerprint(...)` của options (dấu vân tay xử lý ở decorator).

- [ ] **Step 2: Viết test thất bại**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Features.MediaGeneration;

public sealed class SttAlignedTtsProviderTests
{
    private static readonly byte[] Wav = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74, 0x20, 0, 0, 0, 0];

    private sealed class FakeInner(byte[]? bytes = null, string? fingerprint = "fp") : ITtsProvider
    {
        public int Calls;
        public string? ConfigurationFingerprint => fingerprint;
        public Task<GeneratedMedia> GenerateAsync(string text, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new GeneratedMedia(bytes ?? Wav, "audio/wav",
                new Dictionary<string, string> { ["provider"] = "GeminiTTS", ["hasWordTimings"] = "false" }));
        }
    }

    private sealed class FakeAligner(Func<IReadOnlyList<TimedWord>> heard) : IAudioWordAligner
    {
        public int Calls;
        public Task<IReadOnlyList<TimedWord>> RecognizeWordsAsync(byte[] audio, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(heard());
        }
    }

    private static SttAlignedTtsProvider Create(ITtsProvider inner, IAudioWordAligner aligner, TtsServiceOptions? options = null)
    {
        var services = new ServiceCollection().AddScoped(_ => aligner).BuildServiceProvider();
        return new SttAlignedTtsProvider(inner, services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(options ?? new TtsServiceOptions { AlignWordTimingsWithStt = true }),
            NullLogger<SttAlignedTtsProvider>.Instance);
    }

    private static IReadOnlyList<TimedWord> Heard(params string[] words) =>
        words.Select((w, i) => new TimedWord(w, i * 0.5, i * 0.5 + 0.4)).ToList();

    [Fact]
    public async Task AddsWordTimings_AndPassesTheQualityGate()
    {
        var provider = Create(new FakeInner(), new FakeAligner(() => Heard("thỏ", "con", "đi", "lạc")));

        var audio = await provider.GenerateAsync("Thỏ con đi lạc.");

        Assert.Equal("true", audio.GetMetadata("hasWordTimings"));
        Assert.Equal("GeminiTTS", audio.GetMetadata("provider"));
        Assert.NotNull(audio.GetMetadata("wordTimingsJson"));
        var gate = new BinaryAudioQualityGate().Validate(audio, SsmlTokenizer.Tokenize("Thỏ con đi lạc."));
        Assert.True(gate.IsPass, gate.Reason);
    }

    [Fact]
    public async Task AlignerFailure_ReturnsOriginalAudioWithoutTimings()
    {
        var aligner = new FakeAligner(() => throw new InvalidOperationException("stt down"));
        var audio = await Create(new FakeInner(), aligner).GenerateAsync("Thỏ con");

        Assert.Equal("false", audio.GetMetadata("hasWordTimings"));
        Assert.Null(audio.GetMetadata("wordTimingsJson"));
        Assert.Equal(Wav, audio.Content);
    }

    [Fact]
    public async Task PoorAlignment_ReturnsOriginalAudioWithoutTimings()
    {
        var audio = await Create(new FakeInner(), new FakeAligner(() => Heard("xe", "buýt")))
            .GenerateAsync("Thỏ con đi lạc vào rừng");

        Assert.Equal("false", audio.GetMetadata("hasWordTimings"));
        Assert.Null(audio.GetMetadata("wordTimingsJson"));
    }

    [Fact]
    public async Task Disabled_DoesNotCallTheAligner()
    {
        var aligner = new FakeAligner(() => Heard("thỏ"));
        var audio = await Create(new FakeInner(), aligner, new TtsServiceOptions { AlignWordTimingsWithStt = false })
            .GenerateAsync("Thỏ");

        Assert.Equal(0, aligner.Calls);
        Assert.Equal("false", audio.GetMetadata("hasWordTimings"));
    }

    [Fact]
    public async Task AudioLargerThanTheLimit_SkipsAlignment()
    {
        var aligner = new FakeAligner(() => Heard("thỏ"));
        var big = new byte[100];
        Wav.CopyTo(big, 0);
        var options = new TtsServiceOptions { AlignWordTimingsWithStt = true, MaxAlignedAudioBytes = 50 };

        var audio = await Create(new FakeInner(big), aligner, options).GenerateAsync("Thỏ");

        Assert.Equal(0, aligner.Calls);
        Assert.Equal("false", audio.GetMetadata("hasWordTimings"));
    }

    [Fact]
    public async Task TextWithoutWords_SkipsAlignment()
    {
        var aligner = new FakeAligner(() => Heard("x"));
        await Create(new FakeInner(), aligner).GenerateAsync("...");
        Assert.Equal(0, aligner.Calls);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var aligner = new FakeAligner(() => throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Create(new FakeInner(), aligner).GenerateAsync("Thỏ con", new CancellationToken(true)));
    }

    [Fact]
    public void Fingerprint_ChangesWhenAligned_AndStaysNullWhenInnerHasNone()
    {
        Assert.Equal("fp+stt1", Create(new FakeInner(), new FakeAligner(() => Heard())).ConfigurationFingerprint);
        Assert.Null(Create(new FakeInner(fingerprint: null), new FakeAligner(() => Heard())).ConfigurationFingerprint);
    }
}
```

- [ ] **Step 3: Chạy test, xác nhận thất bại**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~SttAlignedTtsProviderTests"`
Expected: lỗi biên dịch (chưa có `SttAlignedTtsProvider`).

- [ ] **Step 4: Cài đặt `SttAlignedTtsProvider.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Bọc nhà cung cấp TTS không có mốc từng chữ (Gemini): nghe lại audio bằng STT rồi căn vào văn bản gốc.
/// Mọi lỗi (trừ hủy) đều trả audio gốc không mốc; audio không bao giờ bị chặn vì căn mốc.
/// </summary>
internal sealed class SttAlignedTtsProvider(
    ITtsProvider inner, IServiceScopeFactory scopes, IOptions<TtsServiceOptions> options,
    ILogger<SttAlignedTtsProvider> logger) : ITtsProvider
{
    private readonly TtsServiceOptions _options = options.Value;

    public string? ConfigurationFingerprint =>
        inner.ConfigurationFingerprint is { } fingerprint && _options.AlignWordTimingsWithStt ? fingerprint + "+stt1" : inner.ConfigurationFingerprint;

    public async Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default)
    {
        var audio = await inner.GenerateAsync(exactSceneText, cancellationToken).ConfigureAwait(false);
        if (!_options.AlignWordTimingsWithStt
            || string.Equals(audio.GetMetadata("hasWordTimings"), "true", StringComparison.OrdinalIgnoreCase)
            || audio.Content.Length == 0 || audio.Content.Length > _options.MaxAlignedAudioBytes)
            return audio;

        var tokens = SsmlTokenizer.Tokenize(exactSceneText);
        if (!tokens.Any(t => t.IsWord)) return audio;

        try
        {
            using var scope = scopes.CreateScope();
            var aligner = scope.ServiceProvider.GetRequiredService<IAudioWordAligner>();
            var heard = await aligner.RecognizeWordsAsync(audio.Content, cancellationToken).ConfigureAwait(false);
            var json = WordTimingAligner.BuildWordTimingsJson(tokens, heard, _options.MinAlignmentCoverage);
            if (json is null)
            {
                logger.LogWarning("TTS word alignment rejected (coverage below {MinCoverage}); audio kept without timings",
                    _options.MinAlignmentCoverage);
                return audio;
            }

            var metadata = new Dictionary<string, string>(audio.Metadata ?? new Dictionary<string, string>())
            {
                ["hasWordTimings"] = "true",
                ["wordTimingsJson"] = json,
                ["timingSource"] = "stt"
            };
            return audio with { Metadata = metadata };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("TTS word alignment failed Error={Error}; audio kept without timings", exception.GetType().Name);
            return audio;
        }
    }
}
```

- [ ] **Step 5: Bọc trong DI** — sửa khối `ITtsProvider` (~dòng 225–232):

```csharp
services.AddSingleton<GoogleCloudTtsProvider>();
services.AddSingleton<ITtsProvider>(sp =>
{
    ITtsProvider inner = ttsOptions.Provider.Trim().ToUpperInvariant() switch
    {
        "GEMINI" or "GEMINITTS" => sp.GetRequiredService<GeminiTtsProvider>(),
        "GOOGLECLOUD" or "GOOGLECLOUDTTS" => sp.GetRequiredService<GoogleCloudTtsProvider>(),
        _ => throw new InvalidOperationException(
            $"Unsupported AI:TTS:Provider '{ttsOptions.Provider}'. Supported values: GeminiTTS, GoogleCloudTTS.")
    };
    if (ttsOptions.AlignWordTimingsWithStt && inner is GeminiTtsProvider)
        inner = new SttAlignedTtsProvider(inner, sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TtsServiceOptions>>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<SttAlignedTtsProvider>());
    return new GatedTtsProvider(inner,
        new MediaBranchProviderGate(mediaOptions.Audio, sp.GetRequiredService<ILoggerFactory>().CreateLogger("MediaProviderTiming"), "Audio"));
});
```

Lưu ý: decorator đặt **trong** `GatedTtsProvider` nên một lượt gọi (Gemini + STT) dùng chung một lượt giữ chỗ của cổng; nếu hết thời gian của cổng, STT cũng bị hủy cùng token.

- [ ] **Step 6: Chạy test mới, rồi build và toàn bộ test**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~SttAlignedTtsProviderTests"` → PASS (8 test).
Run: `dotnet build` → 0 lỗi; `dotnet test` → toàn bộ pass (trước đó: 5275 + 149 + 114).

- [ ] **Step 7: Kiểm thủ công (cần Gemini + STT thật, người dùng chạy)**

Đặt `AI:TTS:AlignWordTimingsWithStt=true` (và `AI:STT` đã bật API Speech-to-Text + quyền IAM), tạo audio một truyện mới; kiểm `media_assets."WordTimings"` của audio có JSON và số phần tử bằng số chữ; GET content tương tác tuổi 7 thấy `audioWordTimings.prompt`. Nếu chưa bật STT API, log có `TTS word alignment STT failed ... SERVICE_DISABLED` và audio vẫn `Ready` không có mốc.

---

## Self-Review

- **Phủ yêu cầu:** giữ giọng Gemini (decorator không đổi audio) ✔; mốc từng chữ bằng STT ✔ (Task 1–3); không chặn audio khi lỗi ✔; không bịa mốc ngoài khoảng giữa hai chữ khớp, có ngưỡng độ phủ ✔; tương thích `BinaryAudioQualityGate` (test đi qua cổng thật) ✔; mặc định tắt ✔; không schema/migration ✔.
- **Không placeholder:** mọi bước có mã/lệnh cụ thể.
- **Nhất quán kiểu:** `TimedWord`, `IAudioWordAligner.RecognizeWordsAsync`, `WordTimingAligner.BuildWordTimingsJson(tokens, heard, minCoverage)` dùng giống nhau ở Task 1–3; `ParseTimedWords/ParseOffsetSeconds` chỉ ở Task 2.
- **Quyết định cần bạn duyệt:**
  1. Nội suy chữ không nghe khớp (giữa hai chữ khớp) thay vì để trống: giữ cho số mốc = số chữ, đúng với cổng chất lượng và frontend; mốc nội suy có thể lệch nhẹ. Nếu muốn khắt khe hơn, nâng `MinAlignmentCoverage`.
  2. STT bản đồng bộ chỉ nhận ≤ ~1 phút: đoạn dài hơn bị bỏ qua mốc (không lỗi).

## Rủi ro đã biết

- Mỗi audio Gemini tốn thêm một lần gọi STT (kể cả câu phản hồi không tô chữ). Giảm sau nếu cần bằng cách cho `ITtsProvider` biết audio có cần mốc không (ngoài phạm vi plan này).
- Chất lượng STT tiếng Việt với giọng Gemini chưa được đo; cần nghe/đối chiếu thủ công vài truyện trước khi bật cho mọi người (Task 3 Step 7).
- Audio đã tạo trước đây không có mốc; cần tạo lại hoặc làm backfill riêng.
- Số liệu `MaxAlignedAudioBytes` giả định WAV 24 kHz 16-bit mono (Gemini TTS); nếu đổi định dạng cần chỉnh.
