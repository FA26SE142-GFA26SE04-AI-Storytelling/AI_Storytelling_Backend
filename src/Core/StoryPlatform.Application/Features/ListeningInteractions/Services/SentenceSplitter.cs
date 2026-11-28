namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public static class SentenceSplitter
{
    public static IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var sentences = new List<string>();
        var start = 0;
        var quoted = false;
        var quoteStart = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                quoted = !quoted;
                if (quoted) quoteStart = index;
                else if (index > 0 && IsTerminator(text[index - 1]) && quoteStart > start &&
                         text.AsSpan(start, quoteStart - start).Contains(':') && NextStartsUpper(text, index + 1))
                {
                    Add(index + 1);
                }
                continue;
            }
            if (!quoted && IsTerminator(text[index]))
            {
                while (index + 1 < text.Length && IsTerminator(text[index + 1])) index++;
                if (index + 1 < text.Length && text[index + 1] == '"') continue;
                Add(index + 1);
            }
        }
        Add(text.Length);
        return sentences;

        void Add(int end)
        {
            var sentence = text[start..end].Trim();
            if (sentence.Length > 0) sentences.Add(sentence);
            start = end;
        }
    }

    private static bool IsTerminator(char value) => value is '.' or '!' or '?' or '…';

    private static bool NextStartsUpper(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index < text.Length && char.IsUpper(text[index]);
    }
}
