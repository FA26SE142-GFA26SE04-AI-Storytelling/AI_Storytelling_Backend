using System;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Result of a magic-byte inspection.
/// DetectedMime is the canonical MIME type inferred from the bytes; when it disagrees with the
/// caller-supplied MIME the caller must treat the asset as invalid.
/// </summary>
public readonly record struct MagicByteValidationResult(bool IsValid, string? DetectedMime, string? Reason)
{
    public static MagicByteValidationResult Ok(string detectedMime) => new(true, detectedMime, null);
    public static MagicByteValidationResult Fail(string reason) => new(false, null, reason);
}

/// <summary>
/// Pure-function content-type detection from binary headers. Used by GeminiImageGenerationProvider
/// (validate Gemini image payload) and BinaryAudioQualityGate (validate Cloud TTS audio payload).
///
/// Detected formats:
///   - PNG: 89 50 4E 47 0D 0A 1A 0A
///   - JPEG: FF D8 FF
///   - WebP: RIFF....WEBP
///   - WAV:  RIFF....WAVE
///   - MP3:  ID3 or 0xFF 0xFB/0xFF 0xF3/0xFF 0xF2
/// </summary>
public static class MagicByteValidators
{
    public const string PngMime = "image/png";
    public const string JpegMime = "image/jpeg";
    public const string WebpMime = "image/webp";
    public const string WavMime = "audio/wav";
    public const string Mp3Mime = "audio/mpeg";

    public static MagicByteValidationResult ValidateImage(byte[] content)
    {
        if (content is null || content.Length < 12) return MagicByteValidationResult.Fail("CONTENT_TOO_SHORT");
        if (Png(content)) return MagicByteValidationResult.Ok(PngMime);
        if (Jpeg(content)) return MagicByteValidationResult.Ok(JpegMime);
        if (Webp(content)) return MagicByteValidationResult.Ok(WebpMime);
        return MagicByteValidationResult.Fail("UNRECOGNIZED_IMAGE_HEADER");
    }

    public static MagicByteValidationResult ValidateAudio(byte[] content)
    {
        if (content is null || content.Length < 12) return MagicByteValidationResult.Fail("CONTENT_TOO_SHORT");
        if (Wav(content)) return MagicByteValidationResult.Ok(WavMime);
        if (Mp3(content)) return MagicByteValidationResult.Ok(Mp3Mime);
        return MagicByteValidationResult.Fail("UNRECOGNIZED_AUDIO_HEADER");
    }

    private static bool Png(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E &&
        bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    private static bool Jpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    private static bool Webp(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
        bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;

    private static bool Wav(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
        bytes[8] == 0x57 && bytes[9] == 0x41 && bytes[10] == 0x56 && bytes[11] == 0x45;

    private static bool Mp3(ReadOnlySpan<byte> bytes)
    {
        // ID3v2 tag
        if (bytes.Length >= 3 && bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33) return true;
        // MPEG sync: 11 bits set, then 2 bits version, 2 bits layer, 1 bit error — strict check
        if (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0) return true;
        return false;
    }
}
