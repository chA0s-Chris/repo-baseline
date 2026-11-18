// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// A SemVer 2.0 version, compared by SemVer precedence. Build metadata is accepted but ignored.
/// </summary>
internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
    // The pattern from semver.org, with `\z` instead of `$`, which also matches before a final newline.
    private static readonly Regex Pattern = new(
        @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)" +
        @"(?:-(?<pre>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?" +
        @"(?:\+(?<build>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?\z",
        RegexOptions.CultureInvariant);

    private readonly String[] _numbers;
    private readonly String[] _preRelease;

    private SemanticVersion(String[] numbers, String[] preRelease)
    {
        _numbers = numbers;
        _preRelease = preRelease;
    }

    public static Boolean TryParse(String text, out SemanticVersion version)
    {
        var match = Pattern.Match(text);
        if (!match.Success)
        {
            version = null!;
            return false;
        }

        var preRelease = match.Groups["pre"].Success ? match.Groups["pre"].Value.Split('.') : [];
        version = new SemanticVersion([match.Groups["major"].Value, match.Groups["minor"].Value, match.Groups["patch"].Value], preRelease);
        return true;
    }

    public Int32 CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        for (var index = 0; index < _numbers.Length; index++)
        {
            var result = CompareNumbers(_numbers[index], other._numbers[index]);
            if (result != 0)
            {
                return result;
            }
        }

        // A version without pre-release identifiers has a higher precedence than one with them.
        if ((_preRelease.Length == 0) || (other._preRelease.Length == 0))
        {
            return other._preRelease.Length.CompareTo(_preRelease.Length);
        }

        for (var index = 0; index < Math.Min(_preRelease.Length, other._preRelease.Length); index++)
        {
            var result = CompareIdentifiers(_preRelease[index], other._preRelease[index]);
            if (result != 0)
            {
                return result;
            }
        }

        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    /// <summary>
    /// The version without build metadata.
    /// </summary>
    public override String ToString() =>
        _preRelease.Length == 0 ? String.Join('.', _numbers) : $"{String.Join('.', _numbers)}-{String.Join('.', _preRelease)}";

    // Numeric identifiers have no leading zeros, so a longer one is larger, and equally long ones compare by their digits.
    private static Int32 CompareNumbers(String left, String right) =>
        left.Length != right.Length ? left.Length.CompareTo(right.Length) : String.CompareOrdinal(left, right);

    // Numeric identifiers have a lower precedence than alphanumeric ones, which compare in ASCII order.
    private static Int32 CompareIdentifiers(String left, String right)
    {
        var leftNumeric = left.All(Char.IsAsciiDigit);
        var rightNumeric = right.All(Char.IsAsciiDigit);
        return (leftNumeric, rightNumeric) switch
        {
            (true, true) => CompareNumbers(left, right),
            (true, false) => -1,
            (false, true) => 1,
            _ => Math.Sign(String.CompareOrdinal(left, right))
        };
    }
}

/// <summary>
/// The version of the running tool, from its package version. The build appends the commit as build metadata, which is ignored.
/// </summary>
internal static class ToolVersion
{
    private static readonly Lazy<SemanticVersion> Version = new(() =>
    {
        var text = typeof(ToolVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? throw new ToolException("The tool has no version.");
        return SemanticVersion.TryParse(text, out var version) ? version : throw new ToolException($"The tool's version '{text}' is not a SemVer 2.0 version.");
    });

    public static SemanticVersion Current => Version.Value;
}
