using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;

namespace StoryPlatform.Infrastructure.Security;

public sealed class EasyLoginSecretProtector(IDataProtectionProvider provider) : IEasyLoginSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("ChildAccess.EasyLoginSecret.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string? TryUnprotect(string protectedSecret)
    {
        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
