using Microsoft.Extensions.Options;
using StoryPlatform.Infrastructure.Payments;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Payments;

public class SePayQrUrlBuilderTests
{
    private static SePayQrUrlBuilder MakeBuilder(SePayOptions options) =>
        new(Options.Create(options));

    [Fact]
    public void BuildQrCodeUrl_IncludesAccountBankAmountAndContent()
    {
        var builder = MakeBuilder(new SePayOptions
        {
            BankAccountNumber = "0123456789",
            BankCode = "MBBank",
            AccountName = "CONG TY AI STORYTELLING"
        });

        var url = builder.BuildQrCodeUrl(49000, "SEPAYABC123");

        Assert.StartsWith("https://qr.sepay.vn/img", url);
        Assert.Contains("acc=0123456789", url);
        Assert.Contains("bank=MBBank", url);
        Assert.Contains("amount=49000", url);
        Assert.Contains("des=SEPAYABC123", url);
    }

    [Fact]
    public void BuildQrCodeUrl_EscapesSpacesInAccountName()
    {
        var builder = MakeBuilder(new SePayOptions
        {
            BankAccountNumber = "0123456789",
            BankCode = "MBBank",
            AccountName = "CONG TY AI STORYTELLING"
        });

        var url = builder.BuildQrCodeUrl(49000, "SEPAYABC123");

        Assert.DoesNotContain(" ", url);
    }
}
