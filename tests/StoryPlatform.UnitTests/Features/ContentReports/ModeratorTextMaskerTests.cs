using StoryPlatform.Application.Features.ContentReports;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentReports;

public class ModeratorTextMaskerTests
{
    [Theory]
    [InlineData("Gọi 0912345678 nhé", "Gọi [số điện thoại] nhé")]
    [InlineData("Gọi 0912 345 678 nhé", "Gọi [số điện thoại] nhé")]
    [InlineData("Gọi 0912.345.678 nhé", "Gọi [số điện thoại] nhé")]
    [InlineData("Gọi 0912-345-678.", "Gọi [số điện thoại].")]
    [InlineData("Gọi +84 912 345 678 nhé", "Gọi [số điện thoại] nhé")]
    [InlineData("Gọi 84912345678 nhé", "Gọi [số điện thoại] nhé")]
    [InlineData("Hai số 0912345678 và 0987654321", "Hai số [số điện thoại] và [số điện thoại]")]
    public void Mask_PhoneNumbers_AreReplaced(string text, string expected) =>
        Assert.Equal(expected, ModeratorTextMasker.Mask(text, null));

    [Theory]
    [InlineData("Năm 2024 có 365 ngày")]
    [InlineData("Mã số 1234567890")]
    [InlineData("Điểm 0.5 và 100 điểm")]
    [InlineData("Có 12 con vịt")]
    public void Mask_OrdinaryNumbers_AreKept(string text) =>
        Assert.Equal(text, ModeratorTextMasker.Mask(text, null));

    [Theory]
    [InlineData("Gửi về a.b+c@example.co.uk nhé", "Gửi về [email] nhé")]
    [InlineData("Mail: Be.Na@gmail.com.", "Mail: [email].")]
    [InlineData("0912345678@example.com", "[email]")]
    public void Mask_Emails_AreReplaced(string text, string expected) =>
        Assert.Equal(expected, ModeratorTextMasker.Mask(text, null));

    [Fact]
    public void Mask_ChildNickname_IsReplacedIgnoringCaseAndEveryOccurrence()
    {
        var masked = ModeratorTextMasker.Mask("Bé Na thích mèo. bé na ăn kẹo.", "Bé Na");

        Assert.Equal("[tên bé] thích mèo. [tên bé] ăn kẹo.", masked);
    }

    [Fact]
    public void Mask_ChildNickname_OnlyMatchesWholeWords()
    {
        var masked = ModeratorTextMasker.Mask("Bé Nam và Bé Na", "Bé Na");

        Assert.Equal("Bé Nam và [tên bé]", masked);
    }

    [Fact]
    public void Mask_NicknameWithRegexCharacters_IsMatchedLiterally()
    {
        var masked = ModeratorTextMasker.Mask("A.B đi học, AxB ở nhà", "A.B");

        Assert.Equal("[tên bé] đi học, AxB ở nhà", masked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mask_BlankNickname_IsIgnored(string? nickname) =>
        Assert.Equal("Bé Na đi học", ModeratorTextMasker.Mask("Bé Na đi học", nickname));

    [Fact]
    public void Mask_NullText_StaysNull() =>
        Assert.Null(ModeratorTextMasker.Mask(null, "Bé Na"));

    [Fact]
    public void Mask_EmptyText_StaysEmpty() =>
        Assert.Equal(string.Empty, ModeratorTextMasker.Mask(string.Empty, "Bé Na"));

    [Fact]
    public void Mask_AllKindsInOneText()
    {
        var masked = ModeratorTextMasker.Mask(
            "Bé Na gọi 0912345678 hoặc mail na@example.com", "Bé Na");

        Assert.Equal("[tên bé] gọi [số điện thoại] hoặc mail [email]", masked);
    }
}
