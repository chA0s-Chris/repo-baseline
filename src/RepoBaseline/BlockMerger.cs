// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

internal static class BlockMerger
{
    /// <summary>
    /// Returns <paramref name="text"/> with its blocks merged as <see cref="Merge"/> describes. <paramref name="parsed"/> must
    /// come from the lines of <paramref name="text"/> and have no problems.
    /// </summary>
    public static String Apply(String text, ParsedFile parsed, ManagedFile managed)
    {
        var lines = TextFile.SplitLines(text);
        var finalNewline = (text.Length == 0) || text.EndsWith('\n');
        finalNewline |= Merge(lines, parsed, managed);
        return String.Join("\n", lines) + ((lines.Count > 0) && finalNewline ? "\n" : String.Empty);
    }

    /// <summary>
    /// Replaces the content of defined blocks, removes unknown blocks and inserts missing ones in <paramref name="lines"/>.
    /// Returns whether the file now needs a final newline: a block ends it, or the line that ended it before a removed block now does.
    /// </summary>
    public static Boolean Merge(List<String> lines, ParsedFile parsed, ManagedFile managed)
    {
        var expected = managed.Blocks.ToDictionary(block => block.Id, StringComparer.Ordinal);
        var removedLastBlock = false;

        // From the last block to the first, so that earlier line numbers stay valid.
        foreach (var block in parsed.Blocks.OrderByDescending(block => block.BeginLine))
        {
            if (expected.TryGetValue(block.Id, out var definition))
            {
                lines.RemoveRange(block.BeginLine + 1, block.EndLine - block.BeginLine - 1);
                lines.InsertRange(block.BeginLine + 1, TextFile.SplitLines(definition.Content));
            }
            else
            {
                removedLastBlock |= block.EndLine == lines.Count - 1;
                Remove(lines, block);
            }
        }

        var present = parsed.Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
        var missing = managed.Blocks.Where(block => !present.Contains(block.Id)).ToList();
        var cleared = (missing.Count > 0) && lines.All(line => line.Trim().Length == 0);
        if (cleared)
        {
            lines.Clear();
        }

        InsertAtStart(lines, managed.Syntax, missing.Where(block => block.Position == BlockPosition.Start).ToList());
        var atEnd = missing.Where(block => block.Position == BlockPosition.End).ToList();
        foreach (var block in atEnd)
        {
            if ((lines.Count > 0) && (lines[^1].Length > 0))
            {
                lines.Add(String.Empty);
            }

            lines.AddRange(Render(managed.Syntax, block));
        }

        return (atEnd.Count > 0) || removedLastBlock || cleared;
    }

    /// <summary>
    /// Removes a block together with the empty line that separated it from local content.
    /// </summary>
    private static void Remove(List<String> lines, ParsedBlock block)
    {
        // A block directly below local text shares no empty line with it; removing the one after it would join two paragraphs.
        var separated = (block.BeginLine == 0) || (lines[block.BeginLine - 1].Length == 0);
        lines.RemoveRange(block.BeginLine, block.EndLine - block.BeginLine + 1);
        if (!separated)
        {
            return;
        }

        if ((block.BeginLine < lines.Count) && (lines[block.BeginLine].Length == 0))
        {
            lines.RemoveAt(block.BeginLine);
        }
        else if ((block.BeginLine > 0) && (block.BeginLine == lines.Count))
        {
            lines.RemoveAt(block.BeginLine - 1);
        }
    }

    private static void InsertAtStart(List<String> lines, MarkerSyntax syntax, IReadOnlyList<ExpectedBlock> blocks)
    {
        var inserted = new List<String>();
        foreach (var block in blocks)
        {
            if (inserted.Count > 0)
            {
                inserted.Add(String.Empty);
            }

            inserted.AddRange(Render(syntax, block));
        }

        if ((inserted.Count > 0) && (lines.Count > 0) && (lines[0].Length > 0))
        {
            inserted.Add(String.Empty);
        }

        lines.InsertRange(0, inserted);
    }

    private static IEnumerable<String> Render(MarkerSyntax syntax, ExpectedBlock block) =>
        [syntax.Begin(block.Id), ..TextFile.SplitLines(block.Content), syntax.End(block.Id)];
}
