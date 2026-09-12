using System.Threading;
using System.Threading.Tasks;

namespace StoryPlatform.Application.Abstractions.Communication;

public interface IEmailSender
{
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string rawResetToken, CancellationToken cancellationToken = default);
    Task SendEmailVerificationEmailAsync(string toEmail, string toName, string rawVerificationToken, CancellationToken cancellationToken = default);
}
