using System.Text;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.BulkEnrollment;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.ClassGroups.BulkEnrollment;

public class CsvBulkEnrollmentParserTests
{
    [Fact]
    public void Parse_ValidCsv_ReturnsOneRowPerDataLineWith1BasedRowNumberAfterHeader()
    {
        var csv = "Nickname,AgeBand,Language,InviteeEmail\n"
                  + "Bé An,Age_6_8,vi,an.parent@example.com\n"
                  + "Bé Bo,Age_9_12,vi,";

        var rows = CsvBulkEnrollmentParser.Parse(Encoding.UTF8.GetBytes(csv));

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].RowNumber);
        Assert.Equal("Bé An", rows[0].Nickname);
        Assert.Equal("Age_6_8", rows[0].AgeBandRaw);
        Assert.Equal("an.parent@example.com", rows[0].InviteeEmail);
        Assert.Equal(3, rows[1].RowNumber);
        Assert.Equal(string.Empty, rows[1].InviteeEmail);
    }

    [Fact]
    public void Parse_QuotedFieldWithEmbeddedComma_KeepsCommaInsideField()
    {
        var csv = "Nickname,AgeBand,Language,InviteeEmail\n"
                  + "\"Bé An, lớp 3\",Age_6_8,vi,";

        var rows = CsvBulkEnrollmentParser.Parse(Encoding.UTF8.GetBytes(csv));

        Assert.Equal("Bé An, lớp 3", rows[0].Nickname);
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1:A2)")]
    public void Parse_NicknameStartingWithFormulaTriggerChar_IsPrefixedWithApostrophe(string maliciousNickname)
    {
        var csv = $"Nickname,AgeBand,Language,InviteeEmail\n{maliciousNickname},Age_6_8,vi,";

        var rows = CsvBulkEnrollmentParser.Parse(Encoding.UTF8.GetBytes(csv));

        Assert.Equal("'" + maliciousNickname, rows[0].Nickname);
    }

    [Fact]
    public void Parse_FileLargerThanLimit_ThrowsBadRequest()
    {
        var oversized = new byte[CsvBulkEnrollmentParser.MaxFileSizeBytes + 1];

        Assert.Throws<BadRequestException>(() => CsvBulkEnrollmentParser.Parse(oversized));
    }

    [Fact]
    public void Parse_MoreDataRowsThanLimit_ThrowsBadRequest()
    {
        var csv = new StringBuilder("Nickname,AgeBand,Language,InviteeEmail\n");
        for (var i = 0; i <= CsvBulkEnrollmentParser.MaxDataRows; i++)
        {
            csv.Append($"Bé {i},Age_6_8,vi,\n");
        }

        Assert.Throws<BadRequestException>(() =>
            CsvBulkEnrollmentParser.Parse(Encoding.UTF8.GetBytes(csv.ToString())));
    }

    [Fact]
    public void Parse_InvalidUtf8Bytes_ThrowsBadRequest()
    {
        byte[] invalidUtf8 = [0x4E, 0x69, 0x63, 0x6B, 0xFF, 0xFE];

        Assert.Throws<BadRequestException>(() => CsvBulkEnrollmentParser.Parse(invalidUtf8));
    }

    [Fact]
    public void Parse_EmptyFile_ThrowsBadRequest()
    {
        Assert.Throws<BadRequestException>(() => CsvBulkEnrollmentParser.Parse([]));
    }
}
