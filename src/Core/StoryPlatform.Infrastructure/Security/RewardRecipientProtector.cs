using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using StoryPlatform.Application.Features.Events.Interfaces;

namespace StoryPlatform.Infrastructure.Security;

public sealed class RewardRecipientProtector(IDataProtectionProvider provider) : IRewardRecipientProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Events.RewardRecipient.v1");

    public string Protect(string json) => _protector.Protect(json);

    public bool TryUnprotect(string stored, out string json)
    {
        try
        {
            json = _protector.Unprotect(stored);
            return true;
        }
        catch (CryptographicException)
        {
            json = string.Empty;
            return false;
        }
    }
}
