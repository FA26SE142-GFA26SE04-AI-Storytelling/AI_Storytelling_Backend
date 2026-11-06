using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>Real controllers/JWT/EF/transactions against temporary SQLite; AI is fake, not Vertex or PostgreSQL.</summary>
public sealed class ParentStoryPackagesApiTests(ParentPackagesApiFactory factory) : IClassFixture<ParentPackagesApiFactory>
{
    [Fact]
    public async Task Manual_story_can_start_without_child_and_deliver_two_packages_atomically()
    {
        var parent = await factory.AddUserAsync(UserRole.User); var client = factory.ClientFor(parent);
        var children = await factory.WithDbAsync(async db =>
        {
            var rows = new[] { new ChildProfile { OwnerUserId = parent.Id, Nickname = "Test A", Status = ChildProfileStatus.Active, Language = "en", AgeBand = AgeBand.Age_6_8 },
                new ChildProfile { OwnerUserId = parent.Id, Nickname = "Test B", Status = ChildProfileStatus.Active, Language = "en", AgeBand = AgeBand.Age_9_10 } };
            db.ChildProfiles.AddRange(rows); await db.SaveChangesAsync();
            foreach (var child in rows)
            {
                db.SafetyPolicies.Add(new() { ChildProfileId = child.Id, MaxStoryLength = 1000, ReadabilityScoreThreshold = 0,
                    ConsentRecorded = true, ConsentRecordedAt = DateTime.UtcNow, ConsentPolicyVersion = 1, ConsentedByUserId = parent.Id });
                db.LearningProfiles.Add(new() { ChildProfileId = child.Id, ReadingLevel = child.AgeBand == AgeBand.Age_6_8 ? 1 : 3 });
            }
            return rows.Select(x => x.Id).ToArray();
        });
        async Task<JsonElement> Post(string path, object body, HttpStatusCode code)
        {
            var response = await client.PostAsJsonAsync(path, body);
            Assert.True(code == response.StatusCode, $"{path}: expected {code}, received {response.StatusCode}. " + string.Join("\n", factory.Errors));
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
        }
        var draft = await Post("/api/v1/parent/stories/manual", new { operationId = "create", language = "en", title = "Tom and the dog" }, HttpStatusCode.Created);
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("childProfileId").ValueKind);
        var id = draft.GetProperty("story").GetProperty("storyId").GetInt32(); var path = $"/api/v1/parent/stories/{id}";
        var saved = await Post(path + "/manual/versions", new { operationId = "save", revisionToken = draft.GetProperty("story").GetProperty("revisionToken").GetString(),
            title = "Tom and the dog", content = "Tom has a red ball. He can run and play. He sees a small dog.\n\nTom gives the dog a drink. The dog is glad. Tom waves and goes home." }, HttpStatusCode.Created);
        var revision = saved.GetProperty("story").GetProperty("revisionToken").GetString(); var version = saved.GetProperty("version").GetProperty("versionId").GetInt32();
        await Post(path + "/content/finalize", new { operationId = "finalize", revisionToken = revision, baseVersionId = version }, HttpStatusCode.OK);
        var packages = await Post(path + "/packages", new { operationId = "packages", revisionToken = revision, baseVersionId = version,
            recipients = children.Select(childProfileId => new { childProfileId, components = new { } }).ToArray() }, HttpStatusCode.Accepted);
        for (var i = 0; i < 2; i++)
        {
            using var scope = factory.Services.CreateScope();
            Assert.True(await scope.ServiceProvider.GetRequiredService<ParentStoryPackageProcessor>().ProcessNextAsync(default));
        }
        var targets = new List<object>();
        foreach (var p in packages.EnumerateArray())
        {
            var packageId = p.GetProperty("packageId").GetInt32();
            var response = await client.GetFromJsonAsync<JsonElement>(path + $"/packages/{packageId}");
            var current = response.GetProperty("data"); Assert.Equal("Review", current.GetProperty("status").GetString());
            var confirmed = await Post(path + $"/packages/{packageId}/confirm", new { operationId = "confirm-" + packageId,
                packageToken = current.GetProperty("packageToken").GetString() }, HttpStatusCode.OK);
            targets.Add(new { packageId, packageToken = confirmed.GetProperty("packageToken").GetString() });
        }
        var rejected = await client.PostAsJsonAsync(path + "/family-confirmations/batch", new { operationId = "bad-batch", packages = new[]
            { targets[0], new { packageId = packages[1].GetProperty("packageId").GetInt32(), packageToken = "stale" } } });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(0, await factory.WithDbAsync(db => db.ParentStoryOperations.CountAsync(x => x.StoryId == id && x.Kind == "FamilyConfirmation")));
        var grantRequest = new { operationId = "deliver", packages = targets };
        var grants = await Post(path + "/family-confirmations/batch", grantRequest, HttpStatusCode.Created);
        var replay = await Post(path + "/family-confirmations/batch", grantRequest, HttpStatusCode.Created);
        Assert.Equal(grants.GetRawText(), replay.GetRawText());
        Assert.Equal(2, await factory.WithDbAsync(db => db.ParentStoryOperations.CountAsync(x => x.StoryId == id && x.Kind == "FamilyConfirmation")));
        Assert.Equal(1, await factory.WithDbAsync(db => db.StoryVersions.CountAsync(x => x.StoryId == id)));
    }

    [Fact]
    public async Task Creator_cannot_enter_parent_package_routes()
    {
        using var client = await factory.ClientForNewUserAsync(UserRole.ContentCreator);
        var response = await client.PostAsJsonAsync("/api/v1/parent/stories/1/packages", new { operationId = "denied", recipients = new[] { new { childProfileId = 1, components = new { } } } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

public sealed class ParentPackagesApiFactory : ApiFactory
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Errors { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.AddProvider(new ErrorCapture(Errors)));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(x => x.ServiceType == typeof(IAIStoryGenerationClient) || x.ServiceType == typeof(IGrayZoneEvaluator));
            services.AddSingleton<IAIStoryGenerationClient, PackageAi>(); services.AddSingleton<IGrayZoneEvaluator, PackageModeration>();
        });
    }
    private sealed class ErrorCapture(System.Collections.Concurrent.ConcurrentQueue<string> errors) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Capture(errors);
        public void Dispose() { }
        private sealed class Capture(System.Collections.Concurrent.ConcurrentQueue<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { if (level >= LogLevel.Error && exception is not null) errors.Enqueue(exception.ToString()); }
        }
    }
    private sealed class PackageAi : IAIStoryGenerationClient
    {
        public Task<EvaluateContentSafetyResponse> EvaluateContentSafetyAsync(EvaluateContentSafetyRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EvaluateContentSafetyResponse { IsAllowed = true, SafetyScore = 100, RiskLevel = "none", ScoreRuleVersion = ContentSafetyScoring.Version });
        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class PackageModeration : IGrayZoneEvaluator
    {
        public bool IsConfigured => true;
        public Task<GrayZoneAssessment> EvaluateAsync(GrayZoneScan scan, CancellationToken ct) =>
            Task.FromResult(new GrayZoneAssessment(ModerationZone.Safe, GrayZoneEvaluator.Fingerprint(scan), scan.Phase, "test", "fake", "fake", 0, []));
    }
}
