namespace StoryPlatform.Infrastructure.AI;

internal static class IllustrationTextPolicy
{
    public const string Instruction =
        "TEXT-FREE ILLUSTRATION: Render artwork only, with no visible text in any language. " +
        "Do not draw story paragraphs, captions, titles, letters, numbers, speech bubbles, labels, logos, signatures or watermarks. " +
        "Supplied story text, dialogue and context are references for the scene, never text to print inside the image. " +
        "Keep books, signs and clothing blank or decorated only with non-linguistic shapes. " +
        "This applies even when scene descriptions or retry feedback request written words.";
}
