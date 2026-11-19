using System.Text.RegularExpressions;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

/// <summary>
/// Deterministic regex-based outbound privacy guard with bounded input length and match timeouts.
/// Rejects recognizable email addresses, structured phone numbers, credentials/tokens, private keys,
/// and credential-bearing URLs before HTTP dispatch. Does not detect arbitrary person names, street
/// addresses, or health text without explicit patterns, and does not scan image/audio bytes.
/// </summary>
public sealed partial class DeterministicMediaOutboundPrivacyGuard : IMediaOutboundPrivacyGuard
{
    public const int MaxSegmentCharacters = 262_144;
    private const int RegexTimeoutMs = 100;

    public void EnsureSafeForProvider(string channel, params string?[] textSegments) =>
        EnsureSafeForProvider(channel, (IEnumerable<string?>)textSegments);

    public void EnsureSafeForProvider(string channel, IEnumerable<string?> textSegments)
    {
        ArgumentNullException.ThrowIfNull(textSegments);

        foreach (var segment in textSegments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            if (segment.Length > MaxSegmentCharacters)
                throw new MediaProviderPrivacyException(MediaProviderPrivacyReason.InputExceedsScanBudget);

            try
            {
                if (PrivateKeyPattern().IsMatch(segment))
                    throw new MediaProviderPrivacyException(MediaProviderPrivacyReason.PrivateKeyDetected);

                if (UrlWithUserInfoPattern().IsMatch(segment) || UrlWithSecretQueryPattern().IsMatch(segment))
                    throw new MediaProviderPrivacyException(MediaProviderPrivacyReason.UrlWithCredentialDetected);

                if (CredentialOrTokenPattern().IsMatch(segment) || KeyValueSecretPattern().IsMatch(segment))
                    throw new MediaProviderPrivacyException(MediaProviderPrivacyReason.CredentialOrTokenDetected);

                if (EmailPattern().IsMatch(segment))
                    throw new MediaProviderPrivacyException(MediaProviderPrivacyReason.EmailDetected);

                if (VietnamesePhonePattern().IsMatch(segment) ||
                    InternationalPhonePattern().IsMatch(segment) ||
                    AreaCodePhonePattern().IsMatch(segment) ||
                    ContextualPhonePattern().IsMatch(segment))
                {
                    throw new MediaProviderPrivacyException(MediaProviderPrivacyReason.PhoneNumberDetected);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                throw new MediaProviderPrivacyException(MediaProviderPrivacyReason.InputExceedsScanBudget);
            }
        }
    }

    [GeneratedRegex(@"-----BEGIN\s+(?:[A-Z0-9 ]+\s+)?PRIVATE KEY-----", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex PrivateKeyPattern();

    [GeneratedRegex(@"https?://[^/\s:@]+:[^/\s@]+@[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex UrlWithUserInfoPattern();

    [GeneratedRegex(@"https?://[^\s]+[?&](?:token|access_token|id_token|api_key|apikey|key|secret|password|sig|signature|x-amz-signature|x-goog-signature|auth)=[^\s&]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex UrlWithSecretQueryPattern();

    [GeneratedRegex(@"(?:\bBearer\s+[A-Za-z0-9._~+/=-]{12,}|\beyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\b|\bAIza[0-9A-Za-z_-]{20,}\b|\b(?:sk-[A-Za-z0-9_-]{16,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9-]{10,}|sb_(?:secret|publishable)_[A-Za-z0-9_-]{16,})\b)", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex CredentialOrTokenPattern();

    [GeneratedRegex(@"\b(?:api[_-]?key|secret[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|password|connectionstring)\s*[:=]\s*[^\s,;""'`]{6,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex KeyValueSecretPattern();

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)(?:[ .-]?\d){9,10}(?!\d)", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex VietnamesePhonePattern();

    [GeneratedRegex(@"(?<!\d)\+\d{1,3}[ .-]?(?:\(\d{1,4}\)[ .-]?)?(?:\d[ .-]?){7,12}\d(?!\d)", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex InternationalPhonePattern();

    [GeneratedRegex(@"\(\d{2,4}\)[ .-]?\d{3,4}[ .-]?\d{3,4}(?!\d)", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex AreaCodePhonePattern();

    [GeneratedRegex(@"(?:điện thoại|sđt|sdt|hotline|phone|tel|call|liên hệ|gọi số)\s*[:\-]?\s*\+?\d(?:[ .-]?\d){6,12}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex ContextualPhonePattern();
}
