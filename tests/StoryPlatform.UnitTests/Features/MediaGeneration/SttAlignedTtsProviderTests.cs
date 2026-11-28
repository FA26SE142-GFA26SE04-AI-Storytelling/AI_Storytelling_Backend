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
        public string? ConfigurationFingerprint => fingerprint;
        public Task<GeneratedMedia> GenerateAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new GeneratedMedia(bytes ?? Wav, "audio/wav",
                new Dictionary<string, string> { ["provider"] = "GeminiTTS", ["hasWordTimings"] = "false" }));
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
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Create(new FakeInner(), aligner).GenerateAsync("Thỏ con", cts.Token));
    }

    [Fact]
    public void Fingerprint_ChangesWhenAligned_AndStaysNullWhenInnerHasNone()
    {
        Assert.Equal("fp+stt1", Create(new FakeInner(), new FakeAligner(() => Heard())).ConfigurationFingerprint);
        Assert.Null(Create(new FakeInner(fingerprint: null), new FakeAligner(() => Heard())).ConfigurationFingerprint);
    }
}
