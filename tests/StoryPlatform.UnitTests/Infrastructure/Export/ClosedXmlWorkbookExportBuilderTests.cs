using ClosedXML.Excel;
using StoryPlatform.Application.Abstractions.Export;
using StoryPlatform.Infrastructure.Export;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Export;

public class ClosedXmlWorkbookExportBuilderTests
{
    private readonly ClosedXmlWorkbookExportBuilder _sut = new();

    [Fact]
    public void BuildWorkbook_SingleSheetWithRows_ProducesReadableXlsxWithHeaderAndData()
    {
        var sheets = new List<ExportSheet>
        {
            new("child_profiles", ["Id", "Nickname"], [["5", "Bé An"], ["6", "Bé Bo"]])
        };

        var bytes = _sut.BuildWorkbook(sheets);

        Assert.NotEmpty(bytes);
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = Assert.Single(workbook.Worksheets, ws => ws.Name == "child_profiles");
        Assert.Equal("Id", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Nickname", worksheet.Cell(1, 2).GetString());
        Assert.Equal("5", worksheet.Cell(2, 1).GetString());
        Assert.Equal("Bé An", worksheet.Cell(2, 2).GetString());
        Assert.Equal("Bé Bo", worksheet.Cell(3, 2).GetString());
    }

    [Fact]
    public void BuildWorkbook_MultipleSheets_CreatesOneWorksheetPerSheet()
    {
        var sheets = new List<ExportSheet>
        {
            new("child_profiles", ["Id"], [["5"]]),
            new("achievements", ["Id"], [["1"]])
        };

        var bytes = _sut.BuildWorkbook(sheets);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(2, workbook.Worksheets.Count);
    }

    [Fact]
    public void BuildWorkbook_EmptySheetList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _sut.BuildWorkbook([]));
    }
}
