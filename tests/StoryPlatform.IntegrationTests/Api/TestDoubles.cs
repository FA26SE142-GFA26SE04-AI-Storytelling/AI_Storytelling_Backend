using System.Collections.Concurrent;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>Captures outgoing e-mails so tests can read OTPs without a mail provider.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Kind, string Secret)> Sent { get; } = new();

    public Task SendPasswordResetEmailAsync(string toEmail, string toName, string rawResetToken, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((toEmail, "reset", rawResetToken));
        return Task.CompletedTask;
    }

    public Task SendEmailVerificationEmailAsync(string toEmail, string toName, string otp, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((toEmail, "verify", otp));
        return Task.CompletedTask;
    }

    public Task SendEmailChangeOtpEmailAsync(string toEmail, string toName, string otp, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((toEmail, "change-email", otp));
        return Task.CompletedTask;
    }

    public Task SendAccountSecurityChangedEmailAsync(string toEmail, string toName, AccountSecurityChange change, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SendAccountProvisionedEmailAsync(string toEmail, string toName, string creatorName, string roleLabel, string rawSetPasswordToken, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SendStaffInviteEmailAsync(string toEmail, string toName, string inviterName, string roleLabel, string rawInviteToken, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>No endpoint under test may reach the AI service synchronously; fail loudly if one does.</summary>
public sealed class UnavailableAIStoryGenerationClient : IAIStoryGenerationClient
{
    public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw Unexpected();

    private static InvalidOperationException Unexpected() => new("AI client must not be called by these API tests.");
}
