using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Contracts.AI.Moderation;

namespace StoryPlatform.Infrastructure.AI;

public sealed class GrayZoneProviderException(string code, int status = 502) : AppException(code, status);

public sealed class VertexGrayZoneModerationProvider : IModelModerationProvider
{
    private readonly HttpClient _http;
    public string Name => "Vertex";
    public VertexGrayZoneModerationProvider(HttpClient http, IOptions<AIServiceOptions> options)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds, 70, 300));
        if (!string.IsNullOrWhiteSpace(options.Value.InternalApiKey))
            _http.DefaultRequestHeaders.Add("X-Internal-Api-Key", options.Value.InternalApiKey);
    }

    public async Task<ModelModerationResult> EvaluateAsync(GrayZoneScan scan, CancellationToken ct)
    {
        var request = new EvaluateGrayZoneRequest
        {
            RequestId = "gz-" + Guid.NewGuid().ToString("N"), SourceFingerprint = GrayZoneEvaluator.Fingerprint(scan),
            Phase = scan.Phase, AgeBand = scan.AgeBand, Language = scan.Language, ReadingLevel = scan.ReadingLevel,
            RubricVersion = scan.RubricVersion, Parts = scan.Parts.Select(x => new GrayZonePart(x.Kind, x.Name, x.Text)).ToArray(),
            Rubric = scan.Rubric,
            BlockedTerms = scan.BlockedTerms
        };
        try
        {
            GrayZoneRules.ValidateRequest(request);
            using var message = await _http.PostAsJsonAsync("api/ai/moderation/gray-zone/evaluate", request, ct);
            await message.Content.LoadIntoBufferAsync(150_000, ct);
            if (!message.IsSuccessStatusCode)
            {
                var error = await message.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: ct);
                var code = error?.ErrorCode;
                if (code is not ("GRAY_ZONE_TIMEOUT" or "GRAY_ZONE_VERTEX_NOT_CONFIGURED" or "GRAY_ZONE_PROVIDER_FAILED" or "GRAY_ZONE_OUTPUT_INVALID"))
                    code = "GRAY_ZONE_SERVICE_FAILED";
                throw new GrayZoneProviderException(code, code == "GRAY_ZONE_TIMEOUT" ? 504
                    : code == "GRAY_ZONE_VERTEX_NOT_CONFIGURED" ? 503 : 502);
            }
            var response = await message.Content.ReadFromJsonAsync<EvaluateGrayZoneResponse>(cancellationToken: ct)
                ?? throw new GrayZoneProviderException("GRAY_ZONE_OUTPUT_INVALID");
            GrayZoneRules.ValidateResponse(request, response);
            return new ModelModerationResult(response.RiskScore, response.Model, response.Findings.Select(x =>
                new ModerationFinding(x.Kind, x.Name, x.CriterionCode, x.Reason)
                { EvidenceQuote = x.EvidenceQuote, ContextNote = x.ContextNote }).ToArray())
            { RubricVersion = response.RubricVersion, Scores = response.Scores };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new GrayZoneProviderException("GRAY_ZONE_TIMEOUT", 504); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        { throw new GrayZoneProviderException("GRAY_ZONE_OUTPUT_INVALID"); }
        catch (HttpRequestException) { throw new GrayZoneProviderException("GRAY_ZONE_SERVICE_FAILED"); }
    }
    private sealed record ErrorBody(string? ErrorCode);
}
