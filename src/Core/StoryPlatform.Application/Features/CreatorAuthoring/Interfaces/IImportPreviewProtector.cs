namespace StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;

public interface IImportPreviewProtector
{
    string Protect(string payload);
    string Unprotect(string token);
}
