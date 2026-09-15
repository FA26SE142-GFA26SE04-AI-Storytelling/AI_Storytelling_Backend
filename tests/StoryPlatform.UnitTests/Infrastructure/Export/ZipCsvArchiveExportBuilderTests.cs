using System.IO.Compression;
using System.Text;
using StoryPlatform.Application.Abstractions.Export;
using StoryPlatform.Infrastructure.Export;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Export;

public class ZipCsvArchiveExportBuilderTests
{
    private readonly ZipCsvArchiveExportBuilder _sut = new();

    [Fact]
    public void BuildCsvArchive_SingleSheet_ProducesZipWithOneCsvEntryContainingHeaderAndRows()
    {
        var sheets = new List<ExportSheet>
        {
            new("child_profiles", ["Id", "Nickname"], [["5", "Bé An"], ["6", "Bé Bo"]])
        };

        var bytes = _sut.BuildCsvArchive(sheets);

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries, e => e.Name == "child_profiles.csv");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var content = reader.ReadToEnd();

        var lines = content.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        Assert.Equal("Id,Nickname", lines[0]);
        Assert.Equal("5,Bé An", lines[1]);
        Assert.Equal("6,Bé Bo", lines[2]);
    }

    [Fact]
    public void BuildCsvArchive_MultipleSheets_CreatesOneCsvEntryPerSheet()
    {
        var sheets = new List<ExportSheet>
        {
            new("child_profiles", ["Id"], [["5"]]),
            new("achievements", ["Id"], [["1"]])
        };

        var bytes = _sut.BuildCsvArchive(sheets);

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Contains(archive.Entries, e => e.Name == "child_profiles.csv");
        Assert.Contains(archive.Entries, e => e.Name == "achievements.csv");
    }

    [Fact]
    public void BuildCsvArchive_ValueContainingCommaAndQuote_IsRfc4180Escaped()
    {
        var sheets = new List<ExportSheet>
        {
            new("notes", ["Text"], [["a,b \"quoted\" c"]])
        };

        var bytes = _sut.BuildCsvArchive(sheets);

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.Entries.Single();
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var content = reader.ReadToEnd();
        var lines = content.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        Assert.Equal("Text", lines[0]);
        Assert.Equal("\"a,b \"\"quoted\"\" c\"", lines[1]);
    }

    [Fact]
    public void BuildCsvArchive_EmptySheetList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _sut.BuildCsvArchive([]));
    }
}
