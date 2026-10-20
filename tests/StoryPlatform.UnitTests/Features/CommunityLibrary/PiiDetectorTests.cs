using StoryPlatform.Application.Features.CommunityLibrary;
using Xunit;

namespace StoryPlatform.UnitTests.Features.CommunityLibrary;

public class PiiDetectorTests
{
    private static List<PiiFinding> Scan(string text, string? nickname = null, string location = "content") =>
        PiiDetector.Scan(new[] { (location, (string?)text) }, nickname);

    private static string[] Kinds(string text, string? nickname = null) =>
        Scan(text, nickname).Select(finding => finding.Kind).Distinct().OrderBy(kind => kind).ToArray();

    [Theory]
    [InlineData("Gọi số 0912345678 nhé")]
    [InlineData("Liên hệ 091 234 5678 ngay")]
    [InlineData("SĐT: 0912.345.678")]
    [InlineData("Zalo +84912345678 nha")]
    [InlineData("Số 84-912-345-678")]
    public void Scan_DetectsVietnamesePhoneNumbers(string text)
    {
        Assert.Contains(PiiKinds.Phone, Kinds(text));
    }

    [Theory]
    [InlineData("Năm 2020 có 1234 con thỏ")]
    [InlineData("Chú thỏ chạy 100 mét")]
    [InlineData("Điểm số là 0.5 và 12345")]
    public void Scan_IgnoresOrdinaryNumbers(string text)
    {
        Assert.Empty(Scan(text));
    }

    [Fact]
    public void Scan_DetectsEmailAndDoesNotAlsoCallItAHandleOrPhone()
    {
        var findings = Scan("Viết thư cho an.nguyen99@example.com nhé");

        Assert.Equal(new[] { PiiKinds.Email }, findings.Select(finding => finding.Kind).ToArray());
    }

    [Theory]
    [InlineData("Xem tại https://facebook.com/be.an")]
    [InlineData("Vào www.example.com/abc đi")]
    [InlineData("Kênh youtube.com/c/bean của bé")]
    [InlineData("Tik tok: tiktok.com/@bean")]
    public void Scan_DetectsUrlsAndSocialDomains(string text)
    {
        Assert.Contains(PiiKinds.Url, Kinds(text));
    }

    [Fact]
    public void Scan_DetectsSocialHandles()
    {
        Assert.Contains(PiiKinds.SocialHandle, Kinds("Theo dõi @be_an2015 nhé"));
    }

    [Theory]
    [InlineData("Nhà bé ở 12 đường Lê Lợi")]
    [InlineData("Bé sống tại 45/6 ngõ Hoa Sen")]
    [InlineData("Địa chỉ 7A phố Huế")]
    [InlineData("Ở phường 5 cạnh chợ")]
    [InlineData("Ở Quận Ba Đình xa lắm")]
    [InlineData("Xã Tân Lập có con sông")]
    public void Scan_DetectsAddresses(string text)
    {
        Assert.Contains(PiiKinds.Address, Kinds(text));
    }

    [Theory]
    [InlineData("Con học Trường Tiểu học Nguyễn Du")]
    [InlineData("Bé ở trường mầm non Hoa Hồng")]
    [InlineData("Cô giáo trường THCS Lê Quý Đôn")]
    [InlineData("Học trường mẫu giáo Sao Mai")]
    public void Scan_DetectsSchoolNames(string text)
    {
        Assert.Contains(PiiKinds.School, Kinds(text));
    }

    [Theory]
    [InlineData("Bé học lớp 2A")]
    [InlineData("Các bạn lớp 10 A1 rất vui")]
    [InlineData("Lớp 3b hôm nay")]
    public void Scan_DetectsClassNames(string text)
    {
        Assert.Contains(PiiKinds.ClassName, Kinds(text));
    }

    [Theory]
    [InlineData("Thỏ con đi qua con đường làng")]
    [InlineData("Trường kỳ kháng chiến")]
    [InlineData("Lớp học rất đông vui")]
    [InlineData("Đường đến trường thật vui")]
    [InlineData("Hội xã hội của loài vật")]
    [InlineData("Chú voi ở phường nhỏ")]
    public void Scan_DoesNotFlagOrdinaryStoryWords(string text)
    {
        Assert.Empty(Scan(text));
    }

    [Fact]
    public void Scan_DetectsTheChildNicknameAsAWholeWordIgnoringCase()
    {
        var findings = Scan("Hôm nay bé BIN đi chơi, bin rất vui", nickname: "Bin");

        Assert.Equal(2, findings.Count(finding => finding.Kind == PiiKinds.ChildNickname));
    }

    [Fact]
    public void Scan_NicknameInsideAnotherWord_IsNotAMatch()
    {
        Assert.Empty(Scan("Chiếc binh đựng nước và cabin xe", nickname: "Bin"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Scan_NoNickname_SkipsTheNicknameCheck(string? nickname)
    {
        Assert.Empty(Scan("Bin đi chơi", nickname));
    }

    [Fact]
    public void Scan_ReportsTheLocationOfEachFinding()
    {
        var findings = PiiDetector.Scan(new (string, string?)[]
        {
            ("title", "Chú thỏ"),
            ("scene 2", "Gọi 0912345678 đi"),
            ("quiz 1", "Học lớp 2A không?")
        }, null);

        Assert.Contains(findings, finding => finding.Location == "scene 2" && finding.Kind == PiiKinds.Phone);
        Assert.Contains(findings, finding => finding.Location == "quiz 1" && finding.Kind == PiiKinds.ClassName);
        Assert.DoesNotContain(findings, finding => finding.Location == "title");
    }

    [Fact]
    public void Scan_ExcerptIsShortAndContainsTheMatch()
    {
        var text = new string('a', 100) + " 0912345678 " + new string('b', 100);

        var finding = Scan(text).Single();

        Assert.True(finding.Excerpt.Length <= 80);
        Assert.Contains("0912345678", finding.Excerpt);
    }

    [Fact]
    public void Scan_NullOrEmptyParts_AreIgnored()
    {
        var findings = PiiDetector.Scan(new (string, string?)[] { ("a", null), ("b", ""), ("c", "   ") }, "Bin");

        Assert.Empty(findings);
    }

    [Fact]
    public void Scan_SeveralKindsInOneText_AreAllReported()
    {
        var kinds = Kinds("Bin học lớp 2A trường Tiểu học Hoa Mai, gọi 0912345678");

        Assert.Equal(
            new[] { PiiKinds.ClassName, PiiKinds.Phone, PiiKinds.School }.OrderBy(kind => kind).ToArray(),
            Kinds("học lớp 2A trường Tiểu học Hoa Mai, gọi 0912345678"));
        Assert.Contains(PiiKinds.ChildNickname, Kinds("Bin học lớp 2A", "Bin"));
        Assert.NotEmpty(kinds);
    }
}
