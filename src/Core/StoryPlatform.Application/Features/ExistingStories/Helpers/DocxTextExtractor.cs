using System.IO.Compression;
using System.Text;
using System.Xml;
using StoryPlatform.Application.Common.Exceptions;

namespace StoryPlatform.Application.Features.ExistingStories.Helpers;

internal static class DocxTextExtractor
{
    private const int MaximumExtractedCharacters = 200_000;
    private const long MaximumDocumentXmlBytes = 5_000_000;

    public static string Extract(Stream input)
    {
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var document = archive.GetEntry("word/document.xml")
                           ?? throw new BadRequestException("File DOCX không có nội dung word/document.xml hợp lệ.");
            if (document.Length > MaximumDocumentXmlBytes)
                throw new BadRequestException("Nội dung XML trong DOCX vượt quá giới hạn cho phép.");

            using var stream = document.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 2_000_000
            });
            var output = new StringBuilder();
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "p")
                {
                    if (output.Length > 0 && output[^1] != '\n')
                        output.AppendLine();
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                {
                    output.Append(reader.ReadElementContentAsString());
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "tab")
                {
                    output.Append('\t');
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "br" or "cr")
                {
                    output.AppendLine();
                }
                if (output.Length > MaximumExtractedCharacters)
                    throw new BadRequestException("Nội dung trích xuất từ DOCX vượt quá 200000 ký tự.");
            }

            return StoryContentNormalizer.Normalize(output.ToString()) ?? string.Empty;
        }
        catch (BadRequestException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new BadRequestException($"File DOCX không hợp lệ: {ex.Message}");
        }
        catch (XmlException ex)
        {
            throw new BadRequestException($"XML trong DOCX không hợp lệ: {ex.Message}");
        }
    }
}
