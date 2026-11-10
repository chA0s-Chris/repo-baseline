// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Text.RegularExpressions;

/// <summary>
/// The comment syntax of block markers in one file type.
/// </summary>
internal sealed record MarkerSyntax(String Prefix, String Suffix, Boolean Fenced)
{
    private static readonly MarkerSyntax Hash = new("# ", "", Fenced: false);
    private static readonly MarkerSyntax Html = new("<!-- ", " -->", Fenced: true);
    private static readonly Regex MarkerWords = new(@"\b(BEGIN|END)\s+repo-baseline\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static MarkerSyntax? ForFile(String path) =>
        Path.GetFileName(path) switch
        {
            ".editorconfig" or ".gitignore" => Hash,
            var name when name.EndsWith(".md", StringComparison.Ordinal) => Html,
            _ => null
        };

    public String Begin(String id) => $"{Prefix}BEGIN repo-baseline: {id}{Suffix}";

    public String End(String id) => $"{Prefix}END repo-baseline: {id}{Suffix}";

    /// <summary>
    /// Returns the block ID if <paramref name="line"/> is a marker of the given kind (<c>BEGIN</c> or <c>END</c>).
    /// </summary>
    public String? Match(String line, String kind)
    {
        var text = line.TrimEnd();
        var start = $"{Prefix}{kind} repo-baseline: ";
        if (!text.StartsWith(start, StringComparison.Ordinal) || !text.EndsWith(Suffix, StringComparison.Ordinal)
                                                              || (text.Length < start.Length + Suffix.Length))
        {
            return null;
        }

        // Anything else, such as `foo --> trailing` or an ID with extra spaces around it, is a near miss, so the file counts as malformed.
        var id = text[start.Length..^Suffix.Length];
        return BlockIds.IsValid(id) ? id : null;
    }

    /// <summary>
    /// Whether <paramref name="line"/> is a marker or a near miss: a comment that mentions <c>BEGIN repo-baseline:</c>
    /// or <c>END repo-baseline:</c> without being an exact marker, for example in lower case, with extra spaces or indented.
    /// </summary>
    public Boolean LooksLikeMarker(String line)
    {
        var text = line.Trim();
        return text.StartsWith(Prefix.Trim(), StringComparison.Ordinal) && MarkerWords.IsMatch(text);
    }
}

/// <summary>
/// Fenced code blocks in Markdown, whose lines are content and never block markers.
/// </summary>
internal static class MarkdownFences
{
    private static readonly Regex Delimiter = new("^ {0,3}(`{3,}|~{3,})(.*)$");

    /// <summary>
    /// Returns which lines belong to a fenced code block, including the fence lines. <paramref name="unclosedLine"/> is the
    /// opening line of a fence that is never closed.
    /// </summary>
    public static Boolean[] Find(IReadOnlyList<String> lines, Boolean enabled, out Int32? unclosedLine)
    {
        var fenced = new Boolean[lines.Count];
        unclosedLine = null;
        if (!enabled)
        {
            return fenced;
        }

        String? fence = null;
        for (var index = 0; index < lines.Count; index++)
        {
            var match = Delimiter.Match(lines[index]);
            var run = match.Groups[1].Value;
            var rest = match.Groups[2].Value;
            if (fence is null)
            {
                // A backtick fence's info string cannot contain backticks.
                if (match.Success && !((run[0] == '`') && rest.Contains('`')))
                {
                    fence = run;
                    unclosedLine = index;
                    fenced[index] = true;
                }
            }
            else
            {
                fenced[index] = true;
                if (match.Success && (run[0] == fence[0]) && (run.Length >= fence.Length) && (rest.Trim().Length == 0))
                {
                    fence = null;
                    unclosedLine = null;
                }
            }
        }

        return fenced;
    }
}

internal static class BlockIds
{
    // `\z` instead of `$`, which also matches before a final newline.
    private static readonly Regex Pattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z");

    public static Boolean IsValid(String id) => Pattern.IsMatch(id);
}
