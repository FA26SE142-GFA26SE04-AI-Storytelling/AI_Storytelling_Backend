using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class GeminiTtsProviderTests
{
    // Valid 12-byte WAV header (RIFF....WAVE)
    private static readonly byte[] ValidWavBytes = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45];

    private static string CreateGeminiAudioResponseJson(byte[] audioBytes, string mimeType = "audio/wav")
    {
        var base64 = Convert.ToBase64String(audioBytes);
        return $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "inlineData": {
                      "mimeType": "{{mimeType}}",
                      "data": "{{base64}}"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;
    }

    private static GeminiTtsProvider CreateProvider(
        HttpMessageHandler handler,
        TtsServiceOptions? ttsOptions = null,
        VertexOptions? vertexOptions = null,
        GeminiOptions? geminiOptions = null,
        bool useResiliencePipeline = false)
    {
        var resolvedTtsOptions = ttsOptions ?? new TtsServiceOptions();
        HttpClient client;
        if (useResiliencePipeline)
        {
            var services = new ServiceCollection();
            var builder = services.AddHttpClient("media-tts-test")
                .ConfigurePrimaryHttpMessageHandler(() => handler);
            builder.AddMediaResiliencePipeline(
                new MediaGenerationOptions(),
                resolvedTtsOptions.TransportRetryCount,
                resolvedTtsOptions.TransportRetryBaseDelayMs);
            client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>()
                .CreateClient("media-tts-test");
        }
        else
        {
            client = new HttpClient(handler);
        }
        var ttsOpts = Options.Create(resolvedTtsOptions);
        var vtxOpts = Options.Create(vertexOptions ?? new VertexOptions { UseVertex = true, ProjectId = "test-proj", Location = "global" });
        var gemOpts = Options.Create(geminiOptions ?? new GeminiOptions());
        return new GeminiTtsProvider(client, gemOpts, vtxOpts, ttsOpts, NullLogger<GeminiTtsProvider>.Instance);
    }

    [Fact]
    public async Task GenerateAsync_ValidText_RequestsAudioModalitiesAndPrebuiltVoice()
    {
        string? capturedBody = null;
        var json = CreateGeminiAudioResponseJson(ValidWavBytes);
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var provider = CreateProvider(handler, new TtsServiceOptions { VoiceName = "Kore", LanguageCode = "vi-VN" });
        await provider.GenerateAsync("Xin chào các bạn nhỏ!");

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        var genConfig = doc.RootElement.GetProperty("generationConfig");
        var modalities = genConfig.GetProperty("responseModalities");
        Assert.Equal("AUDIO", modalities[0].GetString());

        var speechConfig = genConfig.GetProperty("speechConfig");
        Assert.Equal("vi-VN", speechConfig.GetProperty("languageCode").GetString());
        var voiceName = speechConfig
            .GetProperty("voiceConfig")
            .GetProperty("prebuiltVoiceConfig")
            .GetProperty("voiceName")
            .GetString();
        Assert.Equal("Kore", voiceName);
    }

    [Fact]
    public async Task GenerateAsync_EmptyText_ThrowsPermanentException()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<PermanentMediaGenerationException>(() =>
            provider.GenerateAsync("   "));

        Assert.Contains("TTS_EMPTY_TEXT", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_TextOverConfiguredLimit_FailsBeforeHttpCall()
    {
        var callCount = 0;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var provider = CreateProvider(handler, new TtsServiceOptions { MaxTextLength = 5 });

        var ex = await Assert.ThrowsAsync<PermanentMediaGenerationException>(() =>
            provider.GenerateAsync("123456"));

        Assert.Equal("TTS_TEXT_TOO_LONG", ex.ErrorCode);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task GenerateAsync_ValidAudioResponse_ReturnsGeneratedMediaWithHasWordTimingsFalse()
    {
        var json = CreateGeminiAudioResponseJson(ValidWavBytes);
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        var provider = CreateProvider(handler);
        var result = await provider.GenerateAsync("Ngày xửa ngày xưa, trong một khu rừng nọ...");

        Assert.NotNull(result);
        Assert.Equal("audio/wav", result.MimeType);
        Assert.Equal(ValidWavBytes, result.Content);
        Assert.Equal("GeminiTTS", result.GetMetadata("provider"));
        Assert.Equal("false", result.GetMetadata("hasWordTimings")); // Single TTS mode contract
        Assert.Equal("gemini-2.5-flash-tts", result.GetMetadata("model"));
    }

    [Fact]
    public async Task GenerateAsync_Linear16Pcm_WrapsAudioAsValidWav()
    {
        var pcm = Enumerable.Range(0, 64).Select(index => (byte)index).ToArray();
        var json = CreateGeminiAudioResponseJson(pcm, "audio/L16;codec=pcm;rate=24000");
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        var result = await CreateProvider(handler).GenerateAsync("Ngày xửa ngày xưa...");

        Assert.Equal("audio/wav", result.MimeType);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(result.Content, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(result.Content, 8, 4));
        Assert.Equal(pcm, result.Content[44..]);
    }

    [Fact]
    public async Task GenerateAsync_Http400_ThrowsPermanentExceptionWithoutRetry()
    {
        var callCount = 0;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"Invalid argument\"}")
            });
        });

        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<PermanentMediaGenerationException>(() =>
            provider.GenerateAsync("Câu chuyện thần tiên"));

        Assert.Equal(1, callCount); // Must fail fast
        Assert.Contains("GEMINI_TTS_HTTP_400", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_Http503_RetriesCorrectly()
    {
        var callCount = 0;
        var successJson = CreateGeminiAudioResponseJson(ValidWavBytes);
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            if (callCount < 2)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\":\"Overloaded\"}")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(successJson, Encoding.UTF8, "application/json")
            });
        });

        var provider = CreateProvider(handler, new TtsServiceOptions
        {
            TransportRetryCount = 3,
            TransportRetryBaseDelayMs = 10
        }, useResiliencePipeline: true);

        var result = await provider.GenerateAsync("Một sớm mùa thu...");

        Assert.NotNull(result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GenerateAsync_CallerCancellationToken_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var callCount = 0;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var provider = CreateProvider(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GenerateAsync("Test", cts.Token));

        Assert.Equal(1, callCount); // Must propagate immediately after 1 attempt without retries
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsyncFunc)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            sendAsyncFunc(request, cancellationToken);
    }
}
