using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Abstractions.Payments;

namespace StoryPlatform.Infrastructure.Payments;

public sealed class SePayWebhookAuthenticator : ISePayWebhookAuthenticator
{
    private const string ApiKeyPrefix = "Apikey ";
    private readonly SePayOptions _options;

    public SePayWebhookAuthenticator(IOptions<SePayOptions> options)
    {
        _options = options.Value;
    }

    public bool IsValid(string? authorizationHeaderValue)
    {
        if (string.IsNullOrEmpty(_options.WebhookApiKey)
            || string.IsNullOrEmpty(authorizationHeaderValue)
            || !authorizationHeaderValue.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var providedKey = authorizationHeaderValue[ApiKeyPrefix.Length..];
        var expectedBytes = Encoding.UTF8.GetBytes(_options.WebhookApiKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        return expectedBytes.Length == providedBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
