using OtpNet;
using StoryPlatform.Infrastructure.Security;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Security;

public class TotpServiceTests
{
    private readonly TotpService _sut = new();

    [Fact]
    public void GenerateSecret_ReturnsNonEmptyBase32String()
    {
        var secret = _sut.GenerateSecret();

        Assert.NotEmpty(secret);
        // Base32Encoding.ToBytes throws on invalid input — confirms round-trippable Base32.
        Assert.NotEmpty(Base32Encoding.ToBytes(secret));
    }

    [Fact]
    public void GenerateSecret_CalledTwice_ReturnsDifferentValues()
    {
        var first = _sut.GenerateSecret();
        var second = _sut.GenerateSecret();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BuildProvisioningUri_ContainsIssuerAccountAndSecret()
    {
        var uri = _sut.BuildProvisioningUri("JBSWY3DPEHPK3PXP", "admin@example.com", "AI Storytelling");

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("secret=JBSWY3DPEHPK3PXP", uri);
        Assert.Contains("issuer=AI", uri);
        Assert.Contains("admin%40example.com", uri);
    }

    [Fact]
    public void VerifyCode_CurrentValidCode_ReturnsTrue()
    {
        var secret = _sut.GenerateSecret();
        var currentCode = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

        Assert.True(_sut.VerifyCode(secret, currentCode));
    }

    [Fact]
    public void VerifyCode_WrongCode_ReturnsFalse()
    {
        var secret = _sut.GenerateSecret();

        Assert.False(_sut.VerifyCode(secret, "000000"));
    }
}
