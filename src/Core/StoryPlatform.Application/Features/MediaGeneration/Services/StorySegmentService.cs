using System.Collections.Generic;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

/// <summary>
/// Creates StorySegments from a StoryScene using paragraph-based splitting.
/// Offset convention: [StartOffset, EndOffset) — StartOffset inclusive, EndOffset exclusive.
/// TextContent == SceneText.Substring(StartOffset, EndOffset - StartOffset).
///
/// Separator detection: the method scans for either "\n\n" or "\r\n\r\n" between paragraphs and
/// uses the actual matched length so offsets stay correct for CRLF text. The captured separator
/// is stripped from TextContent so the audio pipeline never sees stray CR/LF.
/// </summary>
public sealed class StorySegmentService : IStorySegmentService
{
    public IReadOnlyList<StorySegment> CreateSegmentsForScene(StoryScene scene)
    {
        if (string.IsNullOrEmpty(scene.SceneText))
            return System.Array.Empty<StorySegment>();

        var segments = new List<StorySegment>();
        var cursor = 0;
        var length = scene.SceneText.Length;
        while (cursor < length)
        {
            var paragraphStart = cursor;
            var paragraphEnd = FindNextParagraphEnd(scene.SceneText, cursor);
            var paragraphLength = paragraphEnd - paragraphStart;

            // Skip whitespace-only paragraphs (no audio would be produced).
            if (!IsAllWhitespace(scene.SceneText, paragraphStart, paragraphLength))
            {
                var text = scene.SceneText.Substring(paragraphStart, paragraphLength);
                segments.Add(new StorySegment
                {
                    StorySceneId = scene.Id,
                    SegmentOrder = segments.Count + 1,
                    StartOffset = paragraphStart,
                    EndOffset = paragraphEnd,
                    TextContent = text,
                    CreatedAt = System.DateTime.UtcNow
                });
            }

            cursor = SkipParagraphSeparators(scene.SceneText, paragraphEnd);
            if (cursor == paragraphEnd)
            {
                // No separator consumed: end of string. Guard against infinite loop.
                break;
            }
        }
        return segments;
    }

    /// <summary>
    /// Returns the index of the first character that begins a paragraph separator, or the end of
    /// the string when no separator remains. Detects "\n\n" and "\r\n\r\n" exactly.
    /// </summary>
    private static int FindNextParagraphEnd(string text, int from)
    {
        var length = text.Length;
        var i = from;
        while (i < length)
        {
            var ch = text[i];
            if (ch != '\n' && ch != '\r') { i++; continue; }
            // We hit the start of a separator block. Probe the next non-separator boundary.
            if (TryMatchCrlfCrlf(text, i, length) || TryMatchLfLf(text, i, length))
            {
                return i;
            }
            // Single newline / CR without matching pair → not a paragraph boundary, keep scanning.
            i++;
        }
        return length;
    }

    private static bool TryMatchLfLf(string text, int i, int length) =>
        i + 1 < length && text[i] == '\n' && text[i + 1] == '\n';

    private static bool TryMatchCrlfCrlf(string text, int i, int length) =>
        i + 3 < length && text[i] == '\r' && text[i + 1] == '\n' &&
        text[i + 2] == '\r' && text[i + 3] == '\n';

    /// <summary>
    /// Advances past one full paragraph separator (either "\n\n" or "\r\n\r\n") and returns the new
    /// cursor. Returns the original index when no separator is present (caller guards loop).
    /// </summary>
    private static int SkipParagraphSeparators(string text, int from)
    {
        var length = text.Length;
        if (from >= length) return from;
        if (TryMatchCrlfCrlf(text, from, length)) return from + 4;
        if (TryMatchLfLf(text, from, length)) return from + 2;
        return from;
    }

    private static bool IsAllWhitespace(string text, int start, int length)
    {
        for (var k = 0; k < length; k++)
        {
            if (!char.IsWhiteSpace(text[start + k])) return false;
        }
        return true;
    }
}
