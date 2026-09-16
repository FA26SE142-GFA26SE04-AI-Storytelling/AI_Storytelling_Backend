using Microsoft.Extensions.Options;
using StoryPlatform.Application.Abstractions.Payments;

namespace StoryPlatform.Infrastructure.Payments;

public sealed class SePayQrUrlBuilder : ISePayQrUrlBuilder
{
    private readonly SePayOptions _options;

    public SePayQrUrlBuilder(IOptions<SePayOptions> options)
    {
        _options = options.Value;
    }

    public string BuildQrCodeUrl(int amount, string transferContent)
    {
        var parameters = new[]
        {
            ("acc", _options.BankAccountNumber),
            ("bank", _options.BankCode),
            ("amount", amount.ToString()),
            ("des", transferContent),
            ("accountName", _options.AccountName)
        };
        var query = string.Join('&', parameters.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));
        return $"https://qr.sepay.vn/img?{query}";
    }
}
