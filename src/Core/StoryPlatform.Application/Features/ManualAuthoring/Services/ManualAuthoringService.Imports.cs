using StoryPlatform.Application.Features.CreatorAuthoring;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Helpers;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ManualAuthoring.Services;

public partial class ManualAuthoringService
{
    public async Task<CreatorImportPreviewDto> PreviewImportAsync(int userId, int storyId,
        PreviewCreatorImportRequest request, CancellationToken ct = default)
    {
        await RequireCreatorAsync(userId, ct);
        var raw = Content(request.Content, true);
        var text = Content(StoryContentNormalizer.Normalize(raw), true);
        return await CreatePreviewAsync(userId, storyId, request.RevisionToken, request.OperationId,
            text, "paste", null, Hash(Encoding.UTF8.GetBytes(raw)), ct);
    }

    public async Task<CreatorImportPreviewDto> PreviewFileAsync(int userId, int storyId,
        string revisionToken, string operationId, string fileName, Stream content, CancellationToken ct = default)
    {
        await RequireCreatorAsync(userId, ct);
        // Preview is read-only; verify owner and scope before parsing the upload.
        await RequireStoryAsync(userId, storyId, ct);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255)
            throw new BadRequestException("Tên tệp không hợp lệ.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".txt" or ".docx")) throw new BadRequestException("Chỉ hỗ trợ TXT UTF-8 hoặc DOCX.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > _options.MaximumImportBytes)
                throw new BadRequestException("Tệp vượt giới hạn dung lượng cho phép.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        if (buffer.Length == 0) throw new BadRequestException("Tệp không có nội dung.");
        buffer.Position = 0;
        string text;
        try
        {
            text = extension == ".docx" ? DocxTextExtractor.Extract(buffer)
                : StoryContentNormalizer.Normalize(new UTF8Encoding(false, true).GetString(buffer.ToArray())) ?? string.Empty;
        }
        catch (DecoderFallbackException)
        {
            throw new BadRequestException("Tệp TXT phải dùng mã hóa UTF-8 hợp lệ.");
        }
        return await CreatePreviewAsync(userId, storyId, revisionToken, operationId, Content(text, true),
            extension[1..], Path.GetFileName(fileName), Hash(buffer.ToArray()), ct);
    }

    private async Task<CreatorImportPreviewDto> CreatePreviewAsync(int userId, int storyId, string revisionToken,
        string operationId, string text, string method, string? fileName, string sourceHash, CancellationToken ct)
    {
        await RequireCreatorAsync(userId, ct);
        var story = await RequireStoryAsync(userId, storyId, ct);
        CheckRevision(story, revisionToken);
        var expires = DateTime.UtcNow.AddHours(_options.ImportPreviewLifetimeHours);
        var payload = new PreviewPayload(userId, storyId, revisionToken, Hash(Encoding.UTF8.GetBytes(text)),
            method, fileName, sourceHash, expires);
        var token = _previews.Protect(JsonSerializer.Serialize(payload, JsonOptions));
        return new CreatorImportPreviewDto(token, storyId, text, method, fileName, sourceHash, expires);
    }

    public Task<CreatorContentSavedDto> ConfirmImportAsync(int userId, int storyId,
        ConfirmCreatorImportRequest request, CancellationToken ct = default) =>
        CommandAsync(userId, storyId, "confirm-import", request.OperationId, request, async story =>
        {
            await CheckSourceAsync(story!, request, ct);
            var source = Limited(request.SourceDeclaration, 2000, "Nguồn");
            var rights = Limited(request.RightsDeclaration, 2000, "Quyền sử dụng");
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(rights))
                throw new BadRequestException("Cần khai báo nguồn và quyền sử dụng.");
            if (string.IsNullOrWhiteSpace(request.PreviewToken) || request.PreviewToken.Length > 5000)
                throw new BadRequestException("PreviewToken không hợp lệ.");
            PreviewPayload payload;
            try
            {
                payload = JsonSerializer.Deserialize<PreviewPayload>(_previews.Unprotect(request.PreviewToken), JsonOptions)
                    ?? throw new JsonException();
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
            { throw new BadRequestException("PreviewToken không hợp lệ."); }
            if (payload.UserId != userId || payload.StoryId != storyId)
                throw new NotFoundException("Import preview", storyId);
            if (payload.ExpiresAt <= DateTime.UtcNow) throw new ConflictException("Bản xem trước đã hết hạn.");
            if (payload.RevisionToken != story!.AuthoringRevisionToken) throw new ConflictException("Nguồn preview đã thay đổi.");
            var text = Content(request.Content, true);
            if (Hash(Encoding.UTF8.GetBytes(text)) != payload.TextHash)
                throw new BadRequestException("Nội dung khác bản xem trước; cần preview lại.");
            var tokenHash = Hash(Encoding.UTF8.GetBytes(request.PreviewToken));
            if (await _uow.Repository<StoryVersion>().ExistsAsync(x => x.StoryId == storyId && x.AuthoringMetadata.ImportPreviewHash == tokenHash, ct))
                throw new ConflictException("Preview đã được xác nhận.");
            return await AddVersionAsync(userId, story, new StoryVersion
            {
                Title = Limited(request.Title, 200, "Tên truyện"),
                Content = text,
                EditType = VersionEditType.HumanEdited,
                AuthoringMetadata = new StoryVersionAuthoringMetadata
                {
                    AuthoringOrigin = "Import",
                    SourceDeclaration = source,
                    RightsDeclaration = rights,
                    ImportFileName = payload.FileName,
                    ImportSourceHash = payload.SourceHash,
                    ImportPreviewHash = tokenHash
                }
            }, "creator_import_confirmed", ct);
        }, ct);

    private sealed record PreviewPayload(int UserId, int StoryId, string RevisionToken, string TextHash,
        string InputMethod, string? FileName, string SourceHash, DateTime ExpiresAt);
}
