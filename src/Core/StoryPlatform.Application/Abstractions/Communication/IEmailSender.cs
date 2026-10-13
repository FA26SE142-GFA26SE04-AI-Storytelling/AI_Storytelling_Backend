using System.Threading;
using System.Threading.Tasks;

namespace StoryPlatform.Application.Abstractions.Communication;

public interface IEmailSender
{
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string rawResetToken, CancellationToken cancellationToken = default);
    Task SendEmailVerificationEmailAsync(string toEmail, string toName, string otp, CancellationToken cancellationToken = default);
    Task SendEmailChangeOtpEmailAsync(string toEmail, string toName, string otp, CancellationToken cancellationToken = default);
    Task SendAccountSecurityChangedEmailAsync(string toEmail, string toName, AccountSecurityChange change, CancellationToken cancellationToken = default);
    Task SendDataRequestCompletedEmailAsync(string toEmail, string toName, string requestType, string result, string? reason, CancellationToken cancellationToken = default);
    Task SendSupervisionInvitationEmailAsync(string toEmail, string inviterName, string invitationCode, CancellationToken cancellationToken = default);
    Task SendSupervisionInvitationOtpEmailAsync(string toEmail, string otp, CancellationToken cancellationToken = default);
    Task SendAccountProvisionedEmailAsync(string toEmail, string toName, string creatorName, string roleLabel, string rawSetPasswordToken, CancellationToken cancellationToken = default);
}
