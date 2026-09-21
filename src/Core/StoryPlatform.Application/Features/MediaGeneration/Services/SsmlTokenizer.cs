using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

/// <summary>
/// A timing mark produced by the SSML tokenizer. The provider injects an empty &lt;mark name="wN"/&gt;
/// element before each word token and Google Cloud TTS returns the start time of every mark.
///
/// IMPORTANT: text is preserved exactly (including whitespace and punctuation) and Vietnamese
/// grapheme clusters are kept as single tokens so that audio and the original text stay aligned.
/// </summary>
public readonly record struct TimingMark(int Index, string Text, int OriginalStartOffset, int OriginalEndOffset)
{
    /// <summary>True if this token contains at least one letter or digit (eligible for a &lt;mark/&gt;).</summary>
    public bool IsWord => HasLetterOrDigit(Text);

    private static bool HasLetterOrDigit(string s)
    {
        foreach (var rune in s.EnumerateRunes())
        {
            var c = (char)rune.Value;
            if (char.IsLetterOrDigit(c)) return true;
        }
        return false;
    }
}

/// <summary>
/// Tokenizer that produces SSML-safe tokens preserving whitespace, punctuation, Vietnamese Unicode
/// grapheme clusters, and the exact original text. NOT a Split(' ') splitter; whitespace-only runs
/// and standalone punctuation become their own tokens so the join reproduces the source exactly.
/// </summary>
public static class SsmlTokenizer
{
    public static IReadOnlyList<TimingMark> Tokenize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return System.Array.Empty<TimingMark>();
        var tokens = new List<TimingMark>();
        var sb = new StringBuilder();
        var tokenStart = 0;
        var cursor = 0;
        // Walk the original text one grapheme cluster at a time so that Vietnamese
        // precomposed/decomposed sequences (e.g. "ế", "ữ") remain single tokens.
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var elementKind = Classify(element);
            if (sb.Length == 0)
            {
                sb.Append(element);
                tokenStart = cursor;
            }
            else
            {
                // Compare the running-token kind to the new cluster; flush when the kind changes.
                var runningKind = Classify(sb.ToString());
                if (runningKind == elementKind)
                {
                    sb.Append(element);
                }
                else
                {
                    tokens.Add(new TimingMark(tokens.Count, sb.ToString(), tokenStart, cursor));
                    sb.Clear();
                    sb.Append(element);
                    tokenStart = cursor;
                }
            }
            cursor += element.Length;
        }
        if (sb.Length > 0)
        {
            tokens.Add(new TimingMark(tokens.Count, sb.ToString(), tokenStart, cursor));
        }
        return tokens;
    }

    /// <summary>
    /// Builds SSML text that places an empty &lt;mark name="wN"/&gt; before each word token while
    /// preserving whitespace and punctuation as-is. Caller must wrap the result in &lt;speak&gt;...
    /// </summary>
    public static string BuildSsmlWithMarks(IReadOnlyList<TimingMark> tokens)
    {
        var sb = new StringBuilder();
        foreach (var token in tokens)
        {
            if (token.IsWord)
            {
                sb.Append("<mark name=\"w").Append(token.Index).Append("\"/>");
            }
            sb.Append(EscapeXml(token.Text));
        }
        return sb.ToString();
    }

    public static string EscapeXml(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private enum TokenKind { Word, Whitespace, Punctuation, Symbol }

    private static TokenKind Classify(string element)
    {
        if (string.IsNullOrEmpty(element)) return TokenKind.Symbol;
        var hasLetter = false;
        var hasDigit = false;
        var hasSpace = false;
        var hasPunct = false;
        foreach (var rune in element.EnumerateRunes())
        {
            var c = (char)rune.Value;
            if (char.IsLetter(c)) hasLetter = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else if (char.IsWhiteSpace(c)) hasSpace = true;
            else if (char.IsPunctuation(c) || char.IsSymbol(c)) hasPunct = true;
        }
        if (hasLetter || hasDigit) return TokenKind.Word;
        if (hasSpace) return TokenKind.Whitespace;
        if (hasPunct) return TokenKind.Punctuation;
        return TokenKind.Symbol;
    }
}
