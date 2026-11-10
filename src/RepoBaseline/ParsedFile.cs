// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

internal sealed record ParsedBlock(String Id, Int32 BeginLine, Int32 EndLine);

/// <summary>
/// The managed blocks found in a file, or the problems that make its markers unusable.
/// </summary>
internal sealed record ParsedFile(IReadOnlyList<ParsedBlock> Blocks, IReadOnlyList<String> Problems, Int32? UnclosedFence, Boolean MarkerAfterFence)
{
    /// <summary>
    /// The problems that make the file unusable for <paramref name="managed"/>. A fence left open until the end of the file is valid
    /// Markdown; it is a problem only when it hides a marker or a missing end-position block would be inserted after it.
    /// </summary>
    public IReadOnlyList<String> ProblemsFor(ManagedFile managed)
    {
        var insertsAtEnd = managed.Blocks.Any(block => (block.Position == BlockPosition.End) && Blocks.All(parsed => parsed.Id != block.Id));
        if ((UnclosedFence is not { } fenceLine) || (!MarkerAfterFence && !insertsAtEnd))
        {
            return Problems;
        }

        return [..Problems, $"line {fenceLine + 1} opens a code fence that is never closed, so block markers after it would be ignored"];
    }

    public static ParsedFile Parse(IReadOnlyList<String> lines, MarkerSyntax syntax)
    {
        var blocks = new List<ParsedBlock>();
        var problems = new List<String>();
        var fenced = MarkdownFences.Find(lines, syntax.Fenced, out var unclosedFence);
        (String Id, Int32 Line)? open = null;
        for (var index = 0; index < lines.Count; index++)
        {
            if (fenced[index])
            {
                continue;
            }

            if (syntax.Match(lines[index], "BEGIN") is { } beginId)
            {
                if (open is { } outer)
                {
                    problems.Add($"block {beginId}: BEGIN marker on line {index + 1} is nested in block {outer.Id} from line {outer.Line + 1}");
                    continue;
                }

                if (blocks.FirstOrDefault(block => block.Id == beginId) is { } earlier)
                {
                    problems.Add($"block {beginId}: defined twice, on lines {earlier.BeginLine + 1} and {index + 1}");
                }

                open = (beginId, index);
            }
            else if (syntax.Match(lines[index], "END") is { } endId)
            {
                if (open is not { } current)
                {
                    problems.Add($"block {endId}: END marker on line {index + 1} has no BEGIN marker");
                }
                else if (current.Id != endId)
                {
                    problems.Add($"block {current.Id}: BEGIN marker on line {current.Line + 1} is closed by the END marker of block {endId} on line {index + 1}");
                    open = null;
                }
                else
                {
                    blocks.Add(new ParsedBlock(current.Id, current.Line, index));
                    open = null;
                }
            }
            else if (syntax.LooksLikeMarker(lines[index]))
            {
                problems.Add($"line {index + 1} looks like a block marker but is not one; markers look like {syntax.Begin("<id>")}");
            }
        }

        if (open is { } unclosed)
        {
            problems.Add($"block {unclosed.Id}: BEGIN marker on line {unclosed.Line + 1} has no END marker");
        }

        // Markers after an unclosed fence would be ignored, and sync would insert their blocks again on every run.
        var markerAfterFence = unclosedFence is { } fenceLine
                               && Enumerable.Range(fenceLine + 1, lines.Count - fenceLine - 1).Any(line => syntax.LooksLikeMarker(lines[line]));
        return new ParsedFile(blocks, problems, unclosedFence, markerAfterFence);
    }

    public static String Content(IReadOnlyList<String> lines, ParsedBlock block) =>
        TextFile.JoinLines(lines.Skip(block.BeginLine + 1).Take(block.EndLine - block.BeginLine - 1).ToList());
}
