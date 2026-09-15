using System.Linq;
using StoryPlatform.Contracts.AI.Requests;

namespace StoryPlatform.AI.Application.Common;

/// <summary>
/// Builds enhanced, profile-aware system instructions for story generation prompts.
/// </summary>
public static class ProfilePromptEnhancer
{
    /// <summary>
    /// Build enhanced system instruction based on child profile configuration.
    /// Placeholders in the template are automatically substituted with actual values.
    /// </summary>
    public static string BuildSystemInstruction(GenerateOutlineRequest request)
    {
        var ageBand = request.AgeBand ?? "9-12";

        var baseInstruction = ageBand.ToUpperInvariant() switch
        {
            "6-8" => GetAge6To8Instruction(),
            _ => GetAge9To12Instruction()
        };

        var personalization = BuildPersonalization(request);
        var combined = $"{baseInstruction}\n\n{personalization}";

        return SubstitutePlaceholders(combined, request);
    }

    /// <summary>
    /// Get system instruction for young readers (ages 6-8).
    /// </summary>
    private static string GetAge6To8Instruction() => """
        # SYSTEM INSTRUCTION - Young Readers (Ages 6-8)

        ## Profile Context
        - Target Age: 6-8 years old
        - Reading Level: {readingLevel}
        - Vocabulary Focus: Simple words appropriate for {vocabularyLevel}

        ## Story Structure Requirements

        ### Opening (2-3 simple sentences)
        - Introduce the main character(s) by name and age if appropriate
        - Describe the setting with 1-2 concrete details
        - Hint at what the story will be about

        ### Development (3-5 simple sentences)
        - Present ONE clear problem or simple challenge
        - Show how the main character tries to solve it
        - Keep actions and events simple and linear
        - Include a small moment of learning or discovery

        ### Ending (1-2 simple sentences)
        - Show a clear resolution to the problem
        - Include a simple lesson or positive outcome
        - End on a happy or satisfying note

        ## Language Guidelines
        - Use SHORT sentences (5-10 words each)
        - Use SIMPLE vocabulary matching {vocabularyLevel}
        - AVOID: complex metaphors, abstract concepts, scary imagery
        - USE: repetition of key phrases to reinforce learning
        - Include rhythmic or rhyming patterns if natural

        ## Character Guidelines
        - Main character should be: a child, friendly animal, or familiar object
        - Character emotions must be EXPLICITLY stated (e.g., "Minh felt happy", not just "Minh smiled")
        - NO scary villains or dangerous situations
        - Use small challenges: lost toy, misunderstanding, sharing, making friends

        ## Lesson Integration
        - Embed the lesson naturally through character actions
        - The lesson should emerge from how characters behave and choose
        - Keep the lesson positive and age-appropriate

        ## Length Target
        - Target total: approximately {requestedLength} words
        - Keep each section brief and focused
        - Quality over quantity for young readers
        """;

    /// <summary>
    /// Get system instruction for older readers (ages 9-12).
    /// </summary>
    private static string GetAge9To12Instruction() => """
        # SYSTEM INSTRUCTION - Older Readers (Ages 9-12)

        ## Profile Context
        - Target Age: 9-12 years old
        - Reading Level: {readingLevel}
        - Vocabulary Focus: Words appropriate for {vocabularyLevel}

        ## Story Structure Requirements

        ### Opening (3-5 sentences)
        - Introduce the main character with personality and background
        - Establish the setting with vivid, descriptive details
        - Create initial context that hooks the reader's interest
        - Hint at the central conflict or theme

        ### Development (Detailed narrative)
        - Build rising action with increasing tension or stakes
        - Present a clear challenge, conflict, or decision point
        - Show character growth, decision-making, and consequences
        - Include moments of doubt, friendship, or turning points
        - Explore the lesson through character choices and actions

        ### Ending (3-5 sentences)
        - Provide a meaningful resolution that reflects the lesson learned
        - Show how the character has grown or changed
        - End with emotional satisfaction and closure
        - Leave the reader with something to think about

        ## Language Guidelines
        - Use VARIETY in sentence structure (mix short and longer sentences)
        - Use vocabulary appropriate for {vocabularyLevel}
        - Include descriptive passages that build atmosphere and mood
        - Explore character motivations and emotions in depth
        - Use dialogue to reveal character and advance plot

        ## Character Guidelines
        - Characters can be: peers, mentors, complex individuals, or groups
        - Show genuine character growth and internal conflict
        - Conflicts may include: friendship challenges, moral dilemmas, peer pressure, family dynamics
        - Resolution should involve thought, choice, and effort (not luck)
        - Allow for nuanced ethical situations and gray areas

        ## Lesson Integration
        - The lesson should emerge organically through character actions and consequences
        - AVOID being preachy or overly moralistic
        - Let the story demonstrate values through experience
        - Explore the complexity of doing the right thing

        ## Length Target
        - Target total: approximately {requestedLength} words
        - Balance detail and description with reader engagement
        - Pacing matters: vary sentence length to control tempo
        """;

    /// <summary>
    /// Build personalization section based on child's interests and preferences.
    /// </summary>
    private static string BuildPersonalization(GenerateOutlineRequest request)
    {
        if (request.Interests is null || request.Interests.Count == 0)
        {
            return """
                ## Personalization
                - Create an engaging story that captures the child's imagination
                - Use relatable characters and situations
                """;
        }

        var interestsText = string.Join(", ", request.Interests.Take(5));
        var interestsCount = request.Interests.Count;
        var interestNote = interestsCount > 5
            ? $" (showing top 5 of {interestsCount} interests)"
            : string.Empty;

        return $"""
            ## Personalization - Child Interests{interestNote}
            The child is interested in: {interestsText}

            - Consider incorporating these themes naturally into the story setting
            - Character interests or hobbies can be woven into the plot
            - Use familiar topics to increase engagement and relatability
            """;
    }

    /// <summary>
    /// Substitute placeholders in the instruction template with actual values.
    /// </summary>
    public static string SubstitutePlaceholders(string instruction, GenerateOutlineRequest request)
    {
        var result = instruction
            .Replace("{readingLevel}", request.ReadingLevel ?? "3")
            .Replace("{vocabularyLevel}", request.VocabularyLevel ?? "level_3")
            .Replace("{requestedLength}", (request.StoryParameters?.RequestedLength ?? 500).ToString());

        // Substitute interests if present
        if (request.Interests is { Count: > 0 })
        {
            var interestsText = string.Join(", ", request.Interests.Take(5));
            result = result.Replace("{interests}", interestsText);
        }

        return result;
    }
}
