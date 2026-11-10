// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline.Tests;

using FluentAssertions;
using NUnit.Framework;

public class BlockMergerTests
{
    private static readonly MarkerSyntax Markdown = MarkerSyntax.ForFile("AGENTS.md")!;

    [Test]
    public void Apply_InsertsMissingEndBlockAfterLocalContent()
    {
        var merged = Merge("# Local\n", new ExpectedBlock("rules", "## Rules\n", BlockPosition.End));

        merged.Should().Be("# Local\n\n<!-- BEGIN repo-baseline: rules -->\n## Rules\n<!-- END repo-baseline: rules -->\n");
    }

    [Test]
    public void Apply_RemovesBlockThatIsNotExpected()
    {
        var merged = Merge("# Local\n\n<!-- BEGIN repo-baseline: old -->\nOld.\n<!-- END repo-baseline: old -->\n");

        merged.Should().Be("# Local\n");
    }

    [Test]
    public void Apply_ReplacesBlockContentAndKeepsLocalContent()
    {
        const String text = "# Local\n\n<!-- BEGIN repo-baseline: rules -->\nRules v1.\n<!-- END repo-baseline: rules -->\n\nMore local text.\n";

        var merged = Merge(text, new ExpectedBlock("rules", "Rules v2.\n", BlockPosition.End));

        merged.Should().Be("# Local\n\n<!-- BEGIN repo-baseline: rules -->\nRules v2.\n<!-- END repo-baseline: rules -->\n\nMore local text.\n");
    }

    private static String Merge(String text, params ExpectedBlock[] blocks)
    {
        var managed = new ManagedFile("AGENTS.md", Markdown, blocks);
        var parsed = ParsedFile.Parse(TextFile.SplitLines(text), Markdown);
        return BlockMerger.Apply(text, parsed, managed);
    }
}
