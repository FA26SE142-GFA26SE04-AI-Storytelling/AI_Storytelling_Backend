namespace StoryPlatform.Application.Features.Events.Interfaces;

public interface IRewardRecipientProtector
{
    string Protect(string json);
    bool TryUnprotect(string stored, out string json);
}
