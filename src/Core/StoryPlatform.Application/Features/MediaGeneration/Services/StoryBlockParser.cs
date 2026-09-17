using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed class StoryBlockParser : IStoryBlockParser
{
    public IReadOnlyList<StoryTextBlock> Parse(string content)
    {
        if (string.IsNullOrEmpty(content)) throw new InvalidOperationException("EMPTY_CANONICAL_CONTENT");

        var blocks = new List<StoryTextBlock>();
        var start = 0;
        var index = 1;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n') continue;
            var end = i + 1;
            if (end < content.Length && content[end] == '\n')
            {
                while (end < content.Length && content[end] == '\n') end++;
                blocks.Add(new StoryTextBlock($"P{index++}", start, end, content[start..end]));
                start = end;
                i = end - 1;
            }
        }

        if (start < content.Length)
            blocks.Add(new StoryTextBlock($"P{index}", start, content.Length, content[start..]));
        if (blocks.Count == 0)
            blocks.Add(new StoryTextBlock("P1", 0, content.Length, content));
        return blocks;
    }
}
