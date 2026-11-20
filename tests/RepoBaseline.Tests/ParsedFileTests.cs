// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline.Tests;

using System.Text;
using FluentAssertions;
using NUnit.Framework;

public class ParsedFileTests
{
    private const String Header = "Managed by repo-baseline (https://github.com/chA0s-Chris/repo-baseline). Do not edit; change it there.";

    private static readonly MarkerSyntax Markdown = MarkerSyntax.ForFile("AGENTS.md")!;

    private static readonly ManagedFile CodeStyle = new("CODESTYLE.md", Markdown, [new ExpectedBlock("code-style", "## Code Style\n", BlockPosition.Start)]);

    [Test]
    public void IsFormerSyncedFile_AcceptsHeaderOnFirstLineInBothSyntaxes()
    {
        IsFormerSyncedFile(CodeStyle, $"<!-- {Header} -->\n\n## Code Style\n").Should().BeTrue();

        var editorConfig = new ManagedFile(".editorconfig", MarkerSyntax.ForFile(".editorconfig")!,
                                           [new ExpectedBlock("editorconfig", "root = true\n", BlockPosition.Start)]);
        IsFormerSyncedFile(editorConfig, $"# {Header}\nroot = true\n").Should().BeTrue();
    }

    [Test]
    public void IsFormerSyncedFile_AcceptsBomCrlfAndTrailingWhitespaceAfterDecoding()
    {
        Byte[] content = [0xEF, 0xBB, 0xBF, ..Encoding.UTF8.GetBytes($"<!-- {Header} -->  \r\n\r\n## Code Style\r\n")];

        TextFile.TryDecode(content, out var text).Should().BeTrue();

        IsFormerSyncedFile(CodeStyle, text).Should().BeTrue();
    }

    [TestCase("<!-- Managed by repo-baseline. -->")]
    [TestCase("<!-- Managed by repo-baseline? See the notes below. -->")]
    [TestCase("<!-- Managed by repo-baseline (). Do not edit; change it there. -->")]
    [TestCase("<!-- Managed by repo-baseline (a b). Do not edit; change it there. -->")]
    [TestCase("<!-- Managed by repo-baseline (a(b)). Do not edit; change it there. -->")]
    [TestCase("<!-- Managed by repo-baseline (https://example.com). Do not edit; change it there. Really. -->")]
    [TestCase("# Managed by repo-baseline (https://example.com). Do not edit; change it there.")]
    [TestCase(" <!-- Managed by repo-baseline (https://example.com). Do not edit; change it there. -->")]
    public void IsFormerSyncedFile_RejectsFirstLineThatIsNotTheCompleteHeader(String firstLine)
    {
        IsFormerSyncedFile(CodeStyle, $"{firstLine}\n## Code Style\n").Should().BeFalse();
    }

    [Test]
    public void IsFormerSyncedFile_RejectsHeaderOnLaterLine()
    {
        IsFormerSyncedFile(CodeStyle, $"# Local rules\n<!-- {Header} -->\n").Should().BeFalse();
    }

    [Test]
    public void IsFormerSyncedFile_RejectsHeaderTogetherWithBlockMarkers()
    {
        var text = $"<!-- {Header} -->\n<!-- BEGIN repo-baseline: code-style -->\n<!-- END repo-baseline: code-style -->\n";

        IsFormerSyncedFile(CodeStyle, text).Should().BeFalse();
    }

    [Test]
    public void IsFormerSyncedFile_RejectsHeaderWithMalformedMarker()
    {
        var text = $"<!-- {Header} -->\n<!-- BEGIN repo-baseline: code-style -->\n## Code Style\n";

        IsFormerSyncedFile(CodeStyle, text).Should().BeFalse();
    }

    [Test]
    public void IsFormerSyncedFile_RejectsHeaderWithMarkersHiddenByUnclosedFence()
    {
        var text = $"<!-- {Header} -->\n```\n<!-- BEGIN repo-baseline: code-style -->\n";

        IsFormerSyncedFile(CodeStyle, text).Should().BeFalse();
    }

    [Test]
    public void IsFormerSyncedFile_RejectsFileWithoutSelectedBlock()
    {
        var unused = CodeStyle with { Blocks = [] };

        IsFormerSyncedFile(unused, $"<!-- {Header} -->\n").Should().BeFalse();
    }

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

    private static Boolean IsFormerSyncedFile(ManagedFile managed, String text)
    {
        var lines = TextFile.SplitLines(text);
        return ParsedFile.Parse(lines, managed.Syntax).IsFormerSyncedFile(lines, managed);
    }
}
