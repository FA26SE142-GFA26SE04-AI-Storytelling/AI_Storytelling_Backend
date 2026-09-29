using System.Diagnostics;
using System.Buffers.Binary;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Infrastructure.AI;

var toolDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    SelfTest();
    Console.WriteLine("Tokenization/alignment self-test passed.");
    return;
}
var workspaceRoot = Path.GetFullPath(Path.Combine(toolDirectory, "..", ".."));
var datasetPath = WithinWorkspace(ReadOption("--dataset") ?? Path.Combine(toolDirectory, "dataset.sample.json"));
var outputDirectory = WithinWorkspace(ReadOption("--output") ?? Path.Combine(toolDirectory, "output"));
var live = args.Contains("--live", StringComparer.OrdinalIgnoreCase);
var sttOnly = args.Contains("--stt-only", StringComparer.OrdinalIgnoreCase);
var projectId = ReadOption("--project") ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");
var location = ReadOption("--location") ?? "global";
var take = int.TryParse(ReadOption("--take"), out var requestedTake) ? requestedTake : 50;
if (take is < 1 or > 50) throw new ArgumentException("--take must be between 1 and 50.");

if (live && string.IsNullOrWhiteSpace(projectId))
    throw new ArgumentException("Live mode requires --project or GOOGLE_CLOUD_PROJECT.");
if (sttOnly && !live) throw new ArgumentException("--stt-only requires --live.");

var allSamples = JsonSerializer.Deserialize<Sample[]>(await File.ReadAllTextAsync(datasetPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
if (allSamples.Length is < 1 or > 50 || allSamples.Any(s => string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Text)))
    throw new ArgumentException("Dataset must contain 1–50 samples with non-empty id and text.");
if (allSamples.Any(s => !IsSafeSampleId(s.Id)))
    throw new ArgumentException("Sample ids must be 1–64 safe ASCII filename characters.");
if (allSamples.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != allSamples.Length)
    throw new ArgumentException("Sample ids must be unique.");
var samples = allSamples.Take(take).ToArray();

Directory.CreateDirectory(outputDirectory);
var report = new PrototypeReport
{
    Mode = sttOnly ? "LiveSttOnly" : live ? "LiveVertexAndStt" : "OfflinePreflight",
    DatasetName = Path.GetFileName(datasetPath),
    SampleCount = samples.Length,
    CreatedAtUtc = DateTimeOffset.UtcNow,
    Rows = []
};

VertexTokenProvider? tokenProvider = null;
HttpClient? vertexClient = null;
HttpClient? speechClient = null;
GeminiTtsProvider? ttsProvider = null;
if (live)
{
    var vertexOptions = new VertexOptions { UseVertex = true, ProjectId = projectId!, Location = location };
    tokenProvider = new VertexTokenProvider(Options.Create(vertexOptions), NullLogger<VertexTokenProvider>.Instance);
    vertexClient = new HttpClient(new VertexAuthDelegatingHandler(tokenProvider, Options.Create(vertexOptions))
    {
        InnerHandler = new HttpClientHandler()
    });
    speechClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    ttsProvider = new GeminiTtsProvider(vertexClient, Options.Create(new GeminiOptions()),
        Options.Create(vertexOptions), Options.Create(new TtsServiceOptions()),
        NullLogger<GeminiTtsProvider>.Instance);
}

try
{
    foreach (var sample in samples)
    {
        var tokens = Tokenize(sample.Text);
        var row = new SampleResult
        {
            Id = sample.Id,
            Characters = sample.Text.Length,
            TextUtf8Bytes = Encoding.UTF8.GetByteCount(sample.Text),
            ExpectedTokens = tokens.Count
        };
        report.Rows.Add(row);

        if (!live) continue;

        try
        {
            var stopwatch = new Stopwatch();
            var audioPath = Path.Combine(outputDirectory, sample.Id + ".wav");
            byte[] audioBytes;
            if (sttOnly)
            {
                audioBytes = await File.ReadAllBytesAsync(audioPath);
                row.AudioMimeType = "audio/wav";
            }
            else
            {
                stopwatch.Start();
                var audio = await ttsProvider!.GenerateAsync(sample.Text);
                stopwatch.Stop();
                row.TtsLatencyMs = stopwatch.ElapsedMilliseconds;
                audioBytes = audio.Content;
                row.AudioMimeType = audio.MimeType;
                audioPath = Path.Combine(outputDirectory, sample.Id + audio.SuggestedExtension);
                await using var audioFile = new FileStream(audioPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, useAsync: true);
                await audioFile.WriteAsync(audioBytes);
            }
            row.AudioBytes = audioBytes.Length;
            row.AudioDurationMs = WavDurationMs(audioBytes);
            if (row.AudioDurationMs is null)
            {
                row.ErrorCode = "INVALID_WAV";
                continue;
            }
            if (row.AudioDurationMs is > 60_000 || row.AudioBytes is > 10_000_000)
            {
                row.ErrorCode = "STT_SYNC_LIMIT_EXCEEDED";
                continue;
            }

            foreach (var model in new[] { "short", "long" })
            {
                var modelResult = new ModelResult { Model = model };
                row.Models.Add(modelResult);
                try
                {
                    stopwatch.Restart();
                    var recognized = await RecognizeAsync(speechClient!, tokenProvider!, projectId!, location,
                        model, audioBytes);
                    stopwatch.Stop();
                    modelResult.SttLatencyMs = stopwatch.ElapsedMilliseconds;
                    modelResult.RecognizedTokens = recognized.Count;
                    modelResult.WordsWithOffsets = recognized.Count(HasValidTiming);
                    var alignment = Align(tokens, recognized);
                    modelResult.Matches = alignment.Matches;
                    modelResult.TimedMatches = alignment.TimedMatches;
                    modelResult.Insertions = alignment.Insertions;
                    modelResult.Deletions = alignment.Deletions;
                    modelResult.Substitutions = alignment.Substitutions;
                    modelResult.TextCoverage = tokens.Count == 0 ? 0 : (double)alignment.Matches / tokens.Count;
                    modelResult.Coverage = tokens.Count == 0 ? 0 : (double)alignment.TimedMatches / tokens.Count;
                }
                catch (HttpRequestException exception)
                {
                    modelResult.ErrorCode = exception.Message;
                }
                catch (Exception exception)
                {
                    modelResult.ErrorCode = exception.GetType().Name;
                }
            }
        }
        catch (HttpRequestException exception)
        {
            row.ErrorCode = exception.StatusCode.HasValue
                ? $"TTS_HTTP_{(int)exception.StatusCode.Value}"
                : "TTS_TRANSPORT_ERROR";
        }
        catch (Exception exception)
        {
            row.ErrorCode = exception.GetType().Name;
        }
    }
}
finally
{
    vertexClient?.Dispose();
    speechClient?.Dispose();
    tokenProvider?.Dispose();
}

var reportPath = Path.Combine(outputDirectory,
    $"prototype-results-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report,
    new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
Console.WriteLine($"Mode={report.Mode} Samples={report.SampleCount} Results={reportPath}");
Console.WriteLine("The report does not contain source text, transcripts, credentials, or access tokens.");

string? ReadOption(string name)
{
    var index = Array.FindIndex(args, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0) return null;
    if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException($"{name} requires a value.");
    return args[index + 1];
}

string WithinWorkspace(string path)
{
    var fullPath = Path.GetFullPath(path);
    var relative = Path.GetRelativePath(workspaceRoot, fullPath);
    if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
        Path.IsPathRooted(relative))
        throw new ArgumentException("Dataset and output paths must be inside this repository.");
    var current = workspaceRoot;
    foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
    {
        current = Path.Combine(current, part);
        if ((Directory.Exists(current) || File.Exists(current)) &&
            (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Dataset and output paths cannot traverse a reparse point.");
    }
    return fullPath;
}

static bool IsSafeSampleId(string id) =>
    Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$");

static List<ReadAlongToken> Tokenize(string text)
{
    var result = new List<ReadAlongToken>();
    foreach (Match match in Regex.Matches(text, @"\S+"))
    {
        var start = 0;
        var end = match.Length;
        while (start < end && !char.IsLetterOrDigit(match.Value[start])) start++;
        while (end > start && !char.IsLetterOrDigit(match.Value[end - 1])) end--;
        if (start == end) continue;
        var value = match.Value[start..end];
        result.Add(new ReadAlongToken(value, match.Index + start, match.Index + end));
    }
    return result;
}

static string Normalize(string value)
{
    var normalized = value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
    var start = 0;
    var end = normalized.Length;
    while (start < end && !char.IsLetterOrDigit(normalized[start])) start++;
    while (end > start && !char.IsLetterOrDigit(normalized[end - 1])) end--;
    return normalized[start..end];
}

static AlignmentCounts Align(IReadOnlyList<ReadAlongToken> expected, IReadOnlyList<RecognizedWord> actual)
{
    var rows = expected.Count + 1;
    var columns = actual.Count + 1;
    var costs = new int[rows, columns];
    for (var i = 0; i < rows; i++) costs[i, 0] = i;
    for (var j = 0; j < columns; j++) costs[0, j] = j;
    for (var i = 1; i < rows; i++)
    for (var j = 1; j < columns; j++)
    {
        var substitutionCost = Normalize(expected[i - 1].Text) == Normalize(actual[j - 1].Text) ? 0 : 1;
        costs[i, j] = Math.Min(costs[i - 1, j - 1] + substitutionCost,
            Math.Min(costs[i - 1, j] + 1, costs[i, j - 1] + 1));
    }

    var counts = new AlignmentCounts();
    var x = expected.Count;
    var y = actual.Count;
    while (x > 0 || y > 0)
    {
        if (x > 0 && y > 0)
        {
            var equal = Normalize(expected[x - 1].Text) == Normalize(actual[y - 1].Text);
            if (costs[x, y] == costs[x - 1, y - 1] + (equal ? 0 : 1))
            {
                if (equal)
                {
                    counts.Matches++;
                    if (HasValidTiming(actual[y - 1])) counts.TimedMatches++;
                }
                else counts.Substitutions++;
                x--;
                y--;
                continue;
            }
        }
        if (x > 0 && costs[x, y] == costs[x - 1, y] + 1)
        {
            counts.Deletions++;
            x--;
        }
        else
        {
            counts.Insertions++;
            y--;
        }
    }
    return counts;
}

static void SelfTest()
{
    if (!IsSafeSampleId("sample_01") || IsSafeSampleId("../escape") ||
        IsSafeSampleId("a/b") || IsSafeSampleId("a\\b"))
        throw new InvalidOperationException("SAMPLE_ID_VALIDATION_INVALID");
    var source = "“Minh bước rất nhanh vào rừng.”";
    var tokens = Tokenize(source);
    if (tokens.Count != 6 || source[tokens[0].StartOffset..tokens[0].EndOffset] != "Minh" ||
        source[tokens[^1].StartOffset..tokens[^1].EndOffset] != "rừng")
        throw new InvalidOperationException("TOKEN_SOURCE_OFFSETS_INVALID");
    var deletion = Align(tokens, [new("Minh", null, null), new("bước", null, null),
        new("nhanh", null, null), new("vào", null, null), new("rừng", null, null)]);
    if (deletion.Matches != 5 || deletion.Deletions != 1 || deletion.Insertions != 0)
        throw new InvalidOperationException("ALIGNMENT_DELETION_INVALID");
    var insertion = Align(Tokenize("Hà Hà đi cùng Hà"), [new("Hà", null, null),
        new("Hà", null, null), new("ơi", null, null), new("đi", null, null),
        new("cùng", null, null), new("Hà", null, null)]);
    if (insertion.Matches != 5 || insertion.Insertions != 1)
        throw new InvalidOperationException("ALIGNMENT_REPEATED_WORD_INVALID");
    var substitution = Align(Tokenize("Nam đi học"), [new("Nam", null, null),
        new("về", null, null), new("học", null, null)]);
    if (substitution.Matches != 2 || substitution.Substitutions != 1)
        throw new InvalidOperationException("ALIGNMENT_SUBSTITUTION_INVALID");
    var punctuation = Align(Tokenize("Minh vào rừng."), [new("MINH", null, null),
        new("vào", null, null), new("rừng!", null, null)]);
    if (punctuation.Matches != 3)
        throw new InvalidOperationException("ALIGNMENT_PUNCTUATION_INVALID");
    var timed = Align(Tokenize("Nam đi học"), [new("Nam", 0, 100),
        new("đi", null, null), new("học", 200, 150)]);
    if (timed.Matches != 3 || timed.TimedMatches != 1)
        throw new InvalidOperationException("ALIGNMENT_TIMED_COVERAGE_INVALID");
    var wav = new byte[48];
    Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
    BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 40);
    Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
    BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
    BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
    BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), 4);
    Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
    BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), 4);
    if (WavDurationMs(wav) != 1000 || WavDurationMs(wav[..^1]) is not null)
        throw new InvalidOperationException("WAV_DURATION_VALIDATION_INVALID");
}

static bool HasValidTiming(RecognizedWord word) =>
    word.StartMs is >= 0 and not double.NaN and not double.PositiveInfinity &&
    word.EndMs is > 0 and not double.NaN and not double.PositiveInfinity &&
    word.EndMs > word.StartMs;

static async Task<List<RecognizedWord>> RecognizeAsync(HttpClient client, VertexTokenProvider tokenProvider,
    string project, string location, string model, byte[] audio)
{
    var uri = $"https://speech.googleapis.com/v2/projects/{Uri.EscapeDataString(project)}/locations/{Uri.EscapeDataString(location)}/recognizers/_:recognize";
    var payload = new
    {
        config = new
        {
            autoDecodingConfig = new { },
            languageCodes = new[] { "vi-VN" },
            model,
            features = new { enableWordTimeOffsets = true, enableWordConfidence = true }
        },
        content = Convert.ToBase64String(audio)
    };
    using var request = new HttpRequestMessage(HttpMethod.Post, uri)
    {
        Content = JsonContent.Create(payload)
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
        await tokenProvider.GetAccessTokenAsync());
    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        var reason = "UNKNOWN";
        try
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (error.RootElement.ValueKind == JsonValueKind.Object &&
                error.RootElement.TryGetProperty("error", out var details) &&
                details.ValueKind == JsonValueKind.Object &&
                details.TryGetProperty("details", out var items) &&
                items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("reason", out var value) &&
                        value.ValueKind == JsonValueKind.String)
                    {
                        var candidate = value.GetString();
                        reason = candidate is not null && Regex.IsMatch(candidate, "^[A-Z_]{1,64}$")
                            ? candidate : "UNKNOWN";
                        break;
                    }
        }
        catch (JsonException) { }
        throw new HttpRequestException($"STT_HTTP_{(int)response.StatusCode}_{reason}");
    }

    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var words = new List<RecognizedWord>();
    if (!document.RootElement.TryGetProperty("results", out var results)) return words;
    foreach (var result in results.EnumerateArray())
    {
        if (!result.TryGetProperty("alternatives", out var alternatives) || alternatives.GetArrayLength() == 0)
            continue;
        if (!alternatives[0].TryGetProperty("words", out var wordArray)) continue;
        foreach (var word in wordArray.EnumerateArray())
        {
            var text = word.GetProperty("word").GetString() ?? string.Empty;
            words.Add(new RecognizedWord(text,
                word.TryGetProperty("startOffset", out var start) ? OffsetMs(start.GetString()) : null,
                word.TryGetProperty("endOffset", out var end) ? OffsetMs(end.GetString()) : null));
        }
    }
    return words;
}

static double? OffsetMs(string? raw) => raw is not null && raw.EndsWith('s') &&
    double.TryParse(raw[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
        ? seconds * 1000
        : null;

static double? WavDurationMs(byte[] bytes)
{
    if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" ||
        Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE") return null;
    var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
    if ((ulong)riffSize + 8UL > (ulong)bytes.Length) return null;
    uint byteRate = 0;
    uint? dataSize = null;
    for (var position = 12; position + 8 <= bytes.Length;)
    {
        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4));
        if (chunkSize > bytes.Length - position - 8) return null;
        var kind = Encoding.ASCII.GetString(bytes, position, 4);
        if (kind == "fmt " && chunkSize >= 16)
            byteRate = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 16));
        if (kind == "data") dataSize = chunkSize;
        position += checked((int)(8 + chunkSize + (chunkSize & 1)));
    }
    return byteRate > 0 && dataSize.HasValue ? dataSize.Value * 1000d / byteRate : null;
}

sealed record Sample(string Id, string Text);
sealed record ReadAlongToken(string Text, int StartOffset, int EndOffset);
sealed record RecognizedWord(string Text, double? StartMs, double? EndMs);
sealed class AlignmentCounts
{
    public int Matches { get; set; }
    public int TimedMatches { get; set; }
    public int Insertions { get; set; }
    public int Deletions { get; set; }
    public int Substitutions { get; set; }
}
sealed class PrototypeReport
{
    public required string Mode { get; init; }
    public required string DatasetName { get; init; }
    public required int SampleCount { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required List<SampleResult> Rows { get; init; }
}
sealed class SampleResult
{
    public required string Id { get; init; }
    public required int Characters { get; init; }
    public required int TextUtf8Bytes { get; init; }
    public required int ExpectedTokens { get; init; }
    public long? TtsLatencyMs { get; set; }
    public int? AudioBytes { get; set; }
    public double? AudioDurationMs { get; set; }
    public string? AudioMimeType { get; set; }
    public string? ErrorCode { get; set; }
    public List<ModelResult> Models { get; } = [];
}
sealed class ModelResult
{
    public required string Model { get; init; }
    public long? SttLatencyMs { get; set; }
    public int? RecognizedTokens { get; set; }
    public int? WordsWithOffsets { get; set; }
    public int? Matches { get; set; }
    public int? TimedMatches { get; set; }
    public int? Insertions { get; set; }
    public int? Deletions { get; set; }
    public int? Substitutions { get; set; }
    public double? Coverage { get; set; }
    public double? TextCoverage { get; set; }
    public string? ErrorCode { get; set; }
}
