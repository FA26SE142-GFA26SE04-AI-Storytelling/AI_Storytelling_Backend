using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>
/// Phase 5 tests for the binary audio quality gate.
/// </summary>
public sealed class BinaryAudioQualityGateTests
{
    private static readonly BinaryAudioQualityGate Gate = new();

    [Fact]
    public void Validate_WavContentWithMatchingWordTimings_Pass()
    {
        var wav = new byte[]
        {
            0x52, 0x49, 0x46, 0x46, 0x00, 0x10, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45
        };
        var metadata = new Dictionary<string, string>
        {
            ["wordTimingsJson"] = """[{"word":"Ngày","start":0,"end":0.5}]"""
        };
        var marks = new[] { new TimingMark(0, "Ngày", 0, 4) };
        var audio = new GeneratedMedia(wav, "audio/wav", metadata);

        var result = Gate.Validate(audio, marks);

        Assert.True(result.IsPass);
    }

    [Fact]
    public void Validate_EmptyContent_Fails()
    {
        var audio = new GeneratedMedia(Array.Empty<byte>(), "audio/wav");
        var result = Gate.Validate(audio, Array.Empty<TimingMark>());
        Assert.False(result.IsPass);
        Assert.Equal("AUDIO_CONTENT_EMPTY", result.Reason);
    }

    [Fact]
    public void Validate_BadMagicBytes_Fails()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var audio = new GeneratedMedia(bytes, "audio/wav");
        var result = Gate.Validate(audio, Array.Empty<TimingMark>());
        Assert.False(result.IsPass);
        Assert.Contains("AUDIO_MAGIC_BYTES_INVALID", result.Reason);
    }

    [Fact]
    public void Validate_MismatchedWordTimingCount_Fails()
    {
        var wav = new byte[]
        {
            0x52, 0x49, 0x46, 0x46, 0x00, 0x10, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45
        };
        var metadata = new Dictionary<string, string>
        {
            ["wordTimingsJson"] = """[{"word":"Ngày","start":0,"end":0.5}]"""
        };
        var marks = new[] { new TimingMark(0, "Ngày", 0, 4), new TimingMark(1, "xưa", 5, 8) };
        var audio = new GeneratedMedia(wav, "audio/wav", metadata);

        var result = Gate.Validate(audio, marks);

        Assert.False(result.IsPass);
        Assert.Contains("AUDIO_WORD_TIMINGS_COUNT_MISMATCH", result.Reason);
    }

    [Fact]
    public void Validate_MissingWordTimingsJsonWhenExpected_Fails()
    {
        var wav = new byte[]
        {
            0x52, 0x49, 0x46, 0x46, 0x00, 0x10, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45
        };
        var marks = new[] { new TimingMark(0, "Ngày", 0, 4) };
        var audio = new GeneratedMedia(wav, "audio/wav"); // no metadata

        var result = Gate.Validate(audio, marks);

        Assert.False(result.IsPass);
        Assert.Equal("AUDIO_WORD_TIMINGS_MISSING", result.Reason);
    }

    [Fact]
    public void Validate_NoExpectedMarks_SkipsWordTimingCheck()
    {
        var wav = new byte[]
        {
            0x52, 0x49, 0x46, 0x46, 0x00, 0x10, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45
        };
        var audio = new GeneratedMedia(wav, "audio/wav");

        var result = Gate.Validate(audio, Array.Empty<TimingMark>());

        Assert.True(result.IsPass);
    }
}
