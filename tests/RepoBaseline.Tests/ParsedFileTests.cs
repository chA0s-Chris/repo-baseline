// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline.Tests;

using FluentAssertions;
using NUnit.Framework;

public class ParsedFileTests
{
    private static readonly MarkerSyntax Markdown = MarkerSyntax.ForFile("AGENTS.md")!;

    [Test]
    public void Parse_FindsBlockBetweenMarkers()
    {
        String[] lines = ["# Local", "<!-- BEGIN repo-baseline: rules -->", "## Rules", "<!-- END repo-baseline: rules -->"];

        var parsed = ParsedFile.Parse(lines, Markdown);

        parsed.Problems.Should().BeEmpty();
        parsed.Blocks.Should().Equal(new ParsedBlock("rules", BeginLine: 1, EndLine: 3));
        ParsedFile.Content(lines, parsed.Blocks[0]).Should().Be("## Rules\n");
    }

    [Test]
    public void Parse_ReportsBeginMarkerWithoutEndMarker()
    {
        String[] lines = ["<!-- BEGIN repo-baseline: rules -->", "## Rules"];

        var parsed = ParsedFile.Parse(lines, Markdown);

        parsed.Problems.Should().Equal("block rules: BEGIN marker on line 1 has no END marker");
    }

    [Test]
    public void Parse_TreatsMarkersInFencedCodeAsContent()
    {
        String[] lines = ["```markdown", "<!-- BEGIN repo-baseline: example -->", "<!-- END repo-baseline: example -->", "```"];

        var parsed = ParsedFile.Parse(lines, Markdown);

        parsed.Problems.Should().BeEmpty();
        parsed.Blocks.Should().BeEmpty();
    }
}
