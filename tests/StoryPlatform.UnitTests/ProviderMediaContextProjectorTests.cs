using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ProviderMediaContextProjectorTests
{
    private readonly ProviderMediaContextProjector _projector = new();
    private readonly MediaContextBuilder _builder = new();

    [Fact]
    public void Project_SameFactsWithDifferentMetadata_ProducesIdenticalProjectionWithoutSentinels()
    {
        var contextA = _builder.Build(new MediaContextBuildRequest(
            101,
            "The Kind Rabbit",
            "Rabbit shared a carrot.\n\nFox smiled warmly.",
            "Sharing is caring",
            "Rabbit finds a carrot",
            "Rabbit meets hungry Fox",
            "They become friends",
            """{"creatorUserId":"SENTINEL_CREATOR_A","prompt":"write story","childProfileId":"SENTINEL_CHILD_A"}""",
            """{"childProfileId":"SENTINEL_CHILD_A","consentRecordedBy":"SENTINEL_GUARDIAN_A","consentPolicyVersion":"v2","requiredApprovalMode":"Strict","learningProfile":{"level":3}}"""));

        var contextB = _builder.Build(new MediaContextBuildRequest(
            202,
            "The Kind Rabbit",
            "Rabbit shared a carrot.\n\nFox smiled warmly.",
            "Sharing is caring",
            "Rabbit finds a carrot",
            "Rabbit meets hungry Fox",
            "They become friends",
            """{"creatorUserId":"SENTINEL_CREATOR_B","extraMetadata":true}""",
            """{"audience":{"childProfileId":"SENTINEL_CHILD_B","ageGroup":"6-8"},"ttsConfigurationFingerprint":"SENTINEL_TTS_B"}"""));

        var projectedA = _projector.Project(contextA);
        var projectedB = _projector.Project(contextB);

        var jsonA = ProviderMediaContextSerializer.Serialize(projectedA);
        var jsonB = ProviderMediaContextSerializer.Serialize(projectedB);

        Assert.Equal(jsonA, jsonB);
        Assert.DoesNotContain("SENTINEL_", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("acceptedInput", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("acceptedContext", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceStoryVersionId", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("childProfileId", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("creatorUserId", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("ttsConfigurationFingerprint", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("audience", jsonA, StringComparison.Ordinal);
        Assert.Contains("The Kind Rabbit", jsonA, StringComparison.Ordinal);
        Assert.Contains("Sharing is caring", jsonA, StringComparison.Ordinal);
        Assert.Contains("Rabbit shared a carrot.", jsonA, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ParentPackageNestedAudienceAndFingerprint_StripsAllInternalMetadata()
    {
        var nestedSnapshot = JsonSerializer.Serialize(new
        {
            audience = new
            {
                childProfileId = 9991,
                sentinel = "SENTINEL_PARENT_CHILD_9991",
                consentRecordedByUserId = 8881,
                consentRecordedAt = "2026-11-17T00:00:00Z",
                requiredApprovalMode = "ParentReview"
            },
            ttsConfigurationFingerprint = "SENTINEL_TTS_FP_7771"
        });

        var internalJson = _builder.Build(new MediaContextBuildRequest(
            42,
            "Moonlight Boat",
            "An folded a paper boat.",
            null,
            null,
            null,
            null,
            null,
            nestedSnapshot));

        var projected = _projector.Project(internalJson);
        var serialized = ProviderMediaContextSerializer.Serialize(projected);

        Assert.DoesNotContain("SENTINEL_PARENT_CHILD_9991", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL_TTS_FP_7771", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("audience", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("ttsConfigurationFingerprint", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("consentRecordedByUserId", serialized, StringComparison.Ordinal);
        Assert.Equal("Moonlight Boat", projected.StoryFacts.Title);
        Assert.Equal("An folded a paper boat.", projected.StoryFacts.CanonicalContent);
    }

    [Fact]
    public void Project_MissingVisualDesign_UsesMediaContextBuilderDefaults()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "storyFacts": {
            "title": "Little Star",
            "lesson": null,
            "approvedOutline": null,
            "canonicalContent": "A little star shone brightly."
          }
        }
        """;

        var projected = _projector.Project(json);

        Assert.Equal(ProviderVisualDesign.DefaultStyle, projected.VisualDesign.Style);
        Assert.Equal(ProviderVisualDesign.DefaultContinuityRule, projected.VisualDesign.ContinuityRule);
        Assert.Equal(ProviderVisualDesign.DefaultProhibitedChanges, projected.VisualDesign.ProhibitedChanges);
    }

    [Fact]
    public void Project_UnknownFieldsAtAnyLevel_AreDroppedByAllowlist()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "unexpectedRoot": "SENTINEL_ROOT_SECRET",
          "storyFacts": {
            "title": "Little Star",
            "canonicalContent": "A little star shone brightly.",
            "unexpectedFact": "SENTINEL_FACT_SECRET",
            "approvedOutline": {
              "outlineOpening": "Star wakes up",
              "unexpectedOutline": "SENTINEL_OUTLINE_SECRET"
            }
          },
          "visualDesign": {
            "style": "watercolor",
            "unexpectedVisual": "SENTINEL_VISUAL_SECRET"
          }
        }
        """;

        var serialized = ProviderMediaContextSerializer.Serialize(_projector.Project(json));

        Assert.DoesNotContain("SENTINEL_", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected", serialized, StringComparison.Ordinal);
        Assert.Equal("watercolor", _projector.Project(json).VisualDesign.Style);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("""{"storyFacts":{"title":"A","canonicalContent":"B"}}""")]
    [InlineData("""{"schemaVersion":1,"storyFacts":{"title":"A","canonicalContent":"B"}}""")]
    [InlineData("""{"schemaVersion":"2","storyFacts":{"title":"A","canonicalContent":"B"}}""")]
    [InlineData("""{"schemaVersion":2}""")]
    [InlineData("""{"schemaVersion":2,"storyFacts":{"title":"","canonicalContent":"B"}}""")]
    [InlineData("""{"schemaVersion":2,"storyFacts":{"title":"A","canonicalContent":""}}""")]
    [InlineData("""{"schemaVersion":2,"schemaVersion":2,"storyFacts":{"title":"A","canonicalContent":"B"}}""")]
    [InlineData("""{"schemaVersion":2,"storyFacts":{"title":"A","title":"C","canonicalContent":"B"}}""")]
    [InlineData("""{"schemaVersion":2,"storyFacts":{"title":"A","canonicalContent":"B"},"visualDesign":"bad"}""")]
    [InlineData("""{"schemaVersion":2,"storyFacts":{"title":"A","canonicalContent":"B"},"visualDesign":{"style":123}}""")]
    [InlineData("""{"schemaVersion":2,"storyFacts":{"title":"A","canonicalContent":"B"},"visualDesign":{"prohibitedChanges":"bad"}}""")]
    [InlineData("""{"schemaVersion":2,"storyFacts":{"title":"A","canonicalContent":"B"},"visualDesign":{"prohibitedChanges":[123]}}""")]
    public void Project_InvalidOrAmbiguousContext_ThrowsBoundaryExceptionWithoutLeakingRawJson(string invalidJson)
    {
        var ex = Assert.Throws<MediaProviderContextException>(() => _projector.Project(invalidJson));
        Assert.Equal("MEDIA_PROVIDER_CONTEXT_INVALID", ex.ErrorCode);
        Assert.DoesNotContain("canonicalContent", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("{", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneSpecification_CopyHelpers_PreserveProjectedDto()
    {
        var projected = _projector.Project(_builder.Build(new MediaContextBuildRequest(
            7, "Title", "Scene one.", null, null, null, null,
            """{"creatorUserId":"SENTINEL_CREATOR"}""", null)));

        var spec = new SceneSpecificationBuilder().Build(7, 1, 0, "Scene one.", "Focus", projected);
        var withFeedback = spec.WithFeedback("Keep clothing blue.");
        var forBeat = spec.ForBeat("Scene one.", "Rabbit smiling");
        var forOption = spec.ForListeningOption("Blue boat");

        Assert.Equal(projected, withFeedback.MediaContext);
        Assert.Equal(projected, forBeat.MediaContext);
        Assert.Equal(projected, forOption.MediaContext);
    }
}
