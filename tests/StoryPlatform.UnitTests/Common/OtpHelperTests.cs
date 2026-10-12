using System.Text.RegularExpressions;
using StoryPlatform.Application.Common.Security;
using Xunit;

namespace StoryPlatform.UnitTests.Common;

public class OtpHelperTests
{
    [Fact]
    public void Generate_ReturnsSixDigits() =>
        Assert.Matches(new Regex(@"^\d{6}$"), OtpHelper.Generate());

    [Fact]
    public void Matches_CorrectOtp_ReturnsTrueAndTrimsInput()
    {
        var hash = OtpHelper.Hash(
            OtpHelper.EmailVerifyPurpose, "a@b.com", "123456");

        Assert.True(OtpHelper.Matches(
            hash, OtpHelper.EmailVerifyPurpose, "a@b.com", " 123456 "));
    }

    [Theory]
    [InlineData("email-verify", "other@b.com", "123456")]
    [InlineData("email-change", "a@b.com", "123456")]
    [InlineData("email-verify", "a@b.com", "654321")]
    [InlineData("email-verify", "a@b.com", "")]
    public void Matches_WrongPurposeScopeOrOtp_ReturnsFalse(
        string purpose, string scope, string otp)
    {
        var hash = OtpHelper.Hash("email-verify", "a@b.com", "123456");

        Assert.False(OtpHelper.Matches(hash, purpose, scope, otp));
    }

    [Fact]
    public void Matches_NoStoredHash_ReturnsFalse() =>
        Assert.False(OtpHelper.Matches(
            null, OtpHelper.EmailVerifyPurpose, "a@b.com", "123456"));
}
