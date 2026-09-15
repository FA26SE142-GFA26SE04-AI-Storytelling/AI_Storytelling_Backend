using StoryPlatform.AI.Application.Common;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class ProfilePromptEnhancerTests
{
    [Fact]
    public void BuildSystemInstruction_Age6To8_ReturnsYoungReaderGuidance()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "6-8",
            ReadingLevel = "2",
            VocabularyLevel = "level_2",
            Interests = ["dinosaurs", "space"],
            StoryParameters = new StoryParametersDto { Topic = "friendship", Lesson = "sharing", RequestedLength = 500 },
            Constraints = new GenerationConstraintsDto { MaximumWords = 800 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        Assert.Contains("6-8", instruction);
        Assert.Contains("Young Readers", instruction);
        Assert.Contains("SHORT sentences", instruction);
        Assert.Contains("level_2", instruction);
        Assert.Contains("dinosaurs", instruction);
        Assert.Contains("space", instruction);
    }

    [Fact]
    public void BuildSystemInstruction_Age9To12_ReturnsOlderReaderGuidance()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "9-12",
            ReadingLevel = "4",
            VocabularyLevel = "level_4",
            Interests = ["mystery", "adventure", "science"],
            StoryParameters = new StoryParametersDto { Topic = "courage", Lesson = "bravery", RequestedLength = 800 },
            Constraints = new GenerationConstraintsDto { MaximumWords = 1200 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        Assert.Contains("9-12", instruction);
        Assert.Contains("Older Readers", instruction);
        Assert.Contains("VARIETY in sentence structure", instruction);
        Assert.Contains("level_4", instruction);
        Assert.Contains("mystery", instruction);
        Assert.Contains("science", instruction);
    }

    [Fact]
    public void BuildSystemInstruction_EmptyInterests_StillProducesValidInstruction()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "6-8",
            ReadingLevel = "1",
            VocabularyLevel = "level_1",
            Interests = [],
            StoryParameters = new StoryParametersDto { Topic = "animals", Lesson = "kindness", RequestedLength = 300 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        Assert.NotEmpty(instruction);
        Assert.Contains("6-8", instruction);
        Assert.Contains("Personalization", instruction);
    }

    [Fact]
    public void BuildSystemInstruction_NullInterests_StillProducesValidInstruction()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "9-12",
            ReadingLevel = "3",
            VocabularyLevel = "level_3",
            Interests = null!,
            StoryParameters = new StoryParametersDto { Topic = "nature", Lesson = "respect", RequestedLength = 600 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        Assert.NotEmpty(instruction);
        Assert.Contains("9-12", instruction);
    }

    [Fact]
    public void BuildSystemInstruction_ManyInterests_LimitsToFive()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "6-8",
            ReadingLevel = "1",
            VocabularyLevel = "level_1",
            Interests = ["dinosaurs", "space", "ocean", "animals", "trains", "cars", "planes", "robots"],
            StoryParameters = new StoryParametersDto { Topic = "exploration", Lesson = "curiosity", RequestedLength = 400 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        Assert.Contains("dinosaurs", instruction);
        Assert.Contains("space", instruction);
        Assert.Contains("ocean", instruction);
        Assert.Contains("animals", instruction);
        Assert.Contains("trains", instruction);
        Assert.Contains("(showing top 5", instruction);
    }

    [Fact]
    public void BuildSystemInstruction_DefaultAgeBand_AssumesOlderReaders()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "", // Empty/default
            ReadingLevel = "3",
            VocabularyLevel = "level_3",
            Interests = ["sports"],
            StoryParameters = new StoryParametersDto { Topic = "teamwork", Lesson = "collaboration", RequestedLength = 500 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        // Should fall back to 9-12 instructions
        Assert.Contains("9-12", instruction);
        Assert.Contains("Older Readers", instruction);
    }

    [Fact]
    public void BuildSystemInstruction_UnknownAgeBand_AssumesOlderReaders()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "13-15", // Unknown age band
            ReadingLevel = "5",
            VocabularyLevel = "level_5",
            Interests = [],
            StoryParameters = new StoryParametersDto { Topic = "leadership", Lesson = "responsibility", RequestedLength = 700 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        // Should fall back to 9-12 instructions
        Assert.Contains("9-12", instruction);
    }

    [Fact]
    public void BuildSystemInstruction_ContainsLengthGuidance()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "6-8",
            ReadingLevel = "2",
            VocabularyLevel = "level_2",
            Interests = [],
            StoryParameters = new StoryParametersDto { Topic = "family", Lesson = "love", RequestedLength = 450 },
            Constraints = new GenerationConstraintsDto { MaximumWords = 700 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        Assert.Contains("450", instruction); // RequestedLength
    }

    [Fact]
    public void BuildSystemInstruction_YoungReaders_NoScaryContent()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "6-8",
            ReadingLevel = "1",
            VocabularyLevel = "level_1",
            Interests = [],
            StoryParameters = new StoryParametersDto { Topic = "adventure", Lesson = "safety", RequestedLength = 300 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        Assert.Contains("NO scary villains", instruction);
        Assert.Contains("small challenges", instruction);
    }

    [Fact]
    public void BuildSystemInstruction_OlderReaders_IncludesCharacterGrowth()
    {
        var request = new GenerateOutlineRequest
        {
            AgeBand = "9-12",
            ReadingLevel = "4",
            VocabularyLevel = "level_4",
            Interests = [],
            StoryParameters = new StoryParametersDto { Topic = "friendship", Lesson = "loyalty", RequestedLength = 800 }
        };

        var instruction = ProfilePromptEnhancer.BuildSystemInstruction(request);

        Assert.Contains("character growth", instruction);
        Assert.Contains("internal conflict", instruction);
    }

    [Fact]
    public void SubstitutePlaceholders_ReplacesReadingLevel()
    {
        var instruction = "Reading level: {readingLevel}";
        var request = new GenerateOutlineRequest
        {
            AgeBand = "9-12",
            ReadingLevel = "3",
            VocabularyLevel = "level_3",
            StoryParameters = new StoryParametersDto { RequestedLength = 500 }
        };

        var result = ProfilePromptEnhancer.SubstitutePlaceholders(instruction, request);

        Assert.Contains("3", result);
        Assert.DoesNotContain("{readingLevel}", result);
    }

    [Fact]
    public void SubstitutePlaceholders_ReplacesVocabularyLevel()
    {
        var instruction = "Vocabulary: {vocabularyLevel}";
        var request = new GenerateOutlineRequest
        {
            AgeBand = "6-8",
            ReadingLevel = "1",
            VocabularyLevel = "level_1",
            StoryParameters = new StoryParametersDto { RequestedLength = 300 }
        };

        var result = ProfilePromptEnhancer.SubstitutePlaceholders(instruction, request);

        Assert.Contains("level_1", result);
        Assert.DoesNotContain("{vocabularyLevel}", result);
    }
}
