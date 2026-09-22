using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>
/// Phase 5 tests for content-type detection from binary headers.
/// </summary>
public sealed class MagicByteValidatorsTests
{
    [Fact]
    public void ValidateImage_PngHeader_IsDetected()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
        var result = MagicByteValidators.ValidateImage(bytes);
        Assert.True(result.IsValid);
        Assert.Equal(MagicByteValidators.PngMime, result.DetectedMime);
    }

    [Fact]
    public void ValidateImage_JpegHeader_IsDetected()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
        var result = MagicByteValidators.ValidateImage(bytes);
        Assert.True(result.IsValid);
        Assert.Equal(MagicByteValidators.JpegMime, result.DetectedMime);
    }

    [Fact]
    public void ValidateImage_WebPHeader_IsDetected()
    {
        // RIFF????WEBP
        var bytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x10, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 };
        var result = MagicByteValidators.ValidateImage(bytes);
        Assert.True(result.IsValid);
        Assert.Equal(MagicByteValidators.WebpMime, result.DetectedMime);
    }

    [Fact]
    public void ValidateImage_UnknownBytes_Fails()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B };
        var result = MagicByteValidators.ValidateImage(bytes);
        Assert.False(result.IsValid);
        Assert.Equal("UNRECOGNIZED_IMAGE_HEADER", result.Reason);
    }

    [Fact]
    public void ValidateImage_TooShort_Fails()
    {
        var result = MagicByteValidators.ValidateImage(new byte[] { 0x89 });
        Assert.False(result.IsValid);
        Assert.Equal("CONTENT_TOO_SHORT", result.Reason);
    }

    [Fact]
    public void ValidateAudio_WavHeader_IsDetected()
    {
        // RIFF????WAVE
        var bytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x10, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 };
        var result = MagicByteValidators.ValidateAudio(bytes);
        Assert.True(result.IsValid);
        Assert.Equal(MagicByteValidators.WavMime, result.DetectedMime);
    }

    [Fact]
    public void ValidateAudio_Mp3Id3Tag_IsDetected()
    {
        // ID3
        var bytes = new byte[] { 0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var result = MagicByteValidators.ValidateAudio(bytes);
        Assert.True(result.IsValid);
        Assert.Equal(MagicByteValidators.Mp3Mime, result.DetectedMime);
    }

    [Fact]
    public void ValidateAudio_MpegSync_IsDetected()
    {
        // 0xFF 0xFB (MPEG audio frame sync)
        var bytes = new byte[] { 0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var result = MagicByteValidators.ValidateAudio(bytes);
        Assert.True(result.IsValid);
        Assert.Equal(MagicByteValidators.Mp3Mime, result.DetectedMime);
    }

    [Fact]
    public void ValidateAudio_UnknownBytes_Fails()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C };
        var result = MagicByteValidators.ValidateAudio(bytes);
        Assert.False(result.IsValid);
        Assert.Equal("UNRECOGNIZED_AUDIO_HEADER", result.Reason);
    }

    [Fact]
    public void Validate_EmptyContent_Fails()
    {
        Assert.False(MagicByteValidators.ValidateImage([]).IsValid);
        Assert.False(MagicByteValidators.ValidateAudio([]).IsValid);
    }
}
