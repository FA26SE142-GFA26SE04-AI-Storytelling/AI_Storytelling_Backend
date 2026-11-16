using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Abstractions.Payments;

namespace StoryPlatform.Infrastructure.Payments;

public sealed class SePayWebhookAuthenticator : ISePayWebhookAuthenticator
{
    private const string ApiKeyPrefix = "Apikey ";
    // Giá trị giữ chỗ của key SePayApiKey khi secret vừa được tạo trên AWS (infra/aws-cdk). Giá trị này nằm
    // trong source nên không được coi là khoá hợp lệ: chưa đặt khoá thật thì từ chối mọi webhook.
    private const string UnsetPlaceholder = "REPLACE_ME_POST_DEPLOY";
    private readonly SePayOptions _options;

    public SePayWebhookAuthenticator(IOptions<SePayOptions> options)
    {
        _options = options.Value;
    }

    public bool IsValid(string? authorizationHeaderValue)
    {
        if (string.IsNullOrEmpty(_options.WebhookApiKey)
            || _options.WebhookApiKey == UnsetPlaceholder
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
