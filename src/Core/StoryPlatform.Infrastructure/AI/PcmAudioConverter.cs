using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace StoryPlatform.Infrastructure.AI;

internal static partial class PcmAudioConverter
{
    public static bool IsLinear16(string? mimeType) =>
        !string.IsNullOrWhiteSpace(mimeType) &&
        (mimeType.StartsWith("audio/L16", StringComparison.OrdinalIgnoreCase) ||
         mimeType.Contains("codec=pcm", StringComparison.OrdinalIgnoreCase));

    public static byte[] WrapAsWav(byte[] pcm, string? mimeType)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleRate = ParseSampleRate(mimeType) ?? 24_000;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var output = new byte[44 + pcm.Length];

        "RIFF"u8.CopyTo(output.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4, 4), 36 + pcm.Length);
        "WAVE"u8.CopyTo(output.AsSpan(8, 4));
        "fmt "u8.CopyTo(output.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(32, 2), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(34, 2), bitsPerSample);
        "data"u8.CopyTo(output.AsSpan(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(40, 4), pcm.Length);
        pcm.CopyTo(output, 44);
        return output;
    }

    private static int? ParseSampleRate(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return null;
        var match = SampleRateRegex().Match(mimeType);
        return match.Success && int.TryParse(match.Groups[1].Value, out var rate) && rate is >= 8_000 and <= 192_000
            ? rate
            : null;
    }

    [GeneratedRegex(@"(?:^|[;\s])rate=(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SampleRateRegex();
}
