using Microsoft.AspNetCore.DataProtection;
using StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;

namespace StoryPlatform.Infrastructure.Security;

public sealed class ImportPreviewProtector(IDataProtectionProvider provider) : IImportPreviewProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Creator.ImportPreview.v1");
    public string Protect(string payload) => _protector.Protect(payload);
    public string Unprotect(string token) => _protector.Unprotect(token);
}
