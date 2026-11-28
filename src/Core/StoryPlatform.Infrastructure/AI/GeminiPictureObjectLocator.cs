using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

public sealed class GeminiPictureObjectLocator : IPictureObjectLocator
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _gemini;
    private readonly VertexOptions _vertex;
    private readonly MediaEvaluationOptions _evaluation;
    private readonly IMediaOutboundPrivacyGuard _privacyGuard;
    private readonly ILogger<GeminiPictureObjectLocator> _logger;

    public GeminiPictureObjectLocator(HttpClient httpClient, IOptions<GeminiOptions> gemini,
        IOptions<VertexOptions> vertex, IOptions<MediaEvaluationOptions> evaluation,
        IMediaOutboundPrivacyGuard privacyGuard, ILogger<GeminiPictureObjectLocator> logger)
    {
        _httpClient = httpClient;
        _gemini = gemini.Value;
        _vertex = vertex.Value;
        _evaluation = evaluation.Value;
        _privacyGuard = privacyGuard;
        _logger = logger;
    }

    public async Task<LocatedObject?> LocateAsync(GeneratedMedia picture, string objectLabel, CancellationToken ct = default)
    {
        if (!_vertex.UseVertex && string.IsNullOrWhiteSpace(_gemini.ApiKey)) return null;
        if (string.IsNullOrWhiteSpace(objectLabel)) return null;
        var prompt = $"Locate \"{objectLabel.Trim()}\" in this illustration. Reply only JSON " +
            "{\"found\":bool,\"confidence\":0..1,\"box_2d\":[ymin,xmin,ymax,xmax]} on a 0-1000 scale. " +
            "found=false if the object is not clearly visible.";
        _privacyGuard.EnsureSafeForProvider("PictureObjectLocator", objectLabel, prompt);
        var body = new
        {
            contents = new[] { new { role = "user", parts = new object[]
            {
                new { text = prompt },
                new { inlineData = new { mimeType = picture.MimeType, data = Convert.ToBase64String(picture.Content) } }
            } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, VertexUrlResolver.Resolve(_vertex, _gemini, _evaluation.Model));
        if (!_vertex.UseVertex) request.Headers.Add("x-goog-api-key", _gemini.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Picture object locator returned HTTP {StatusCode}", (int)response.StatusCode);
                return null;
            }
            var content = await response.Content.ReadAsStringAsync(ct);
            return ObjectLocatorResponse.Parse(GeminiResponseJson.ExtractFirstTextPayload(content));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(error, "Picture object locator failed");
            return null;
        }
    }
}
