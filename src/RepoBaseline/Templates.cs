// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

/// <summary>
/// A template with its placeholders filled in, at its final target path.
/// </summary>
internal sealed record RenderedTemplate(String Path, String Text, Boolean Executable, String Source);

/// <summary>
/// Placeholders in created-once templates: <c>{{Name}}</c> and the other names that <see cref="Values"/> defines.
/// </summary>
internal static class Templates
{
    public const String Name = "Name";

    private const String LicenseNotice = @"\nThis file is licensed under the MIT license. See LICENSE in the project root for more information.";
    private static readonly Regex Placeholder = new(@"\{\{(?<name>[A-Za-z]+)\}\}");

    public static IReadOnlyDictionary<String, String> Values(InitOptions options)
    {
        var frameworks = InitOptions.ByVersionDescending(options.TargetFrameworks).ToList();
        var targetFrameworks = frameworks.Count == 1
            ? $"<TargetFramework>{frameworks[0]}</TargetFramework>"
            : $"<LatestTarget>{frameworks[0]}</LatestTarget>\n<MainTargets>$(LatestTarget);{String.Join(';', frameworks.Skip(1))}</MainTargets>\n" +
              "<TargetFrameworks>$(MainTargets)</TargetFrameworks>";

        // The .NET SDK knows gitlab.com; a self-hosted instance needs its host declared (BLD-6).
        var selfHostedGitLab = (options.Forge == InitOptions.GitLab) && !String.Equals(options.Repository.Host, "gitlab.com", StringComparison.OrdinalIgnoreCase);
        return new Dictionary<String, String>(StringComparer.Ordinal)
        {
            [Name] = options.Name,
            ["RepositoryUrl"] = options.RepositoryUrl,
            ["Year"] = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture),
            // ReSharper writes layer GUIDs as 32 uppercase hexadecimal digits.
            ["LayerGuid"] = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            ["TargetFrameworkProperties"] = targetFrameworks,
            ["SourceLinkGitLabHost"] = selfHostedGitLab ? $"<ItemGroup>\n  <SourceLinkGitLabHost Include=\"{GitLabHost(options.Repository)}\" />\n</ItemGroup>" : String.Empty,
            ["LicenseNotice"] = options.Private ? String.Empty : LicenseNotice
        };
    }

    // The ASCII form of an internationalized host name, and the port only when it is not the default.
    private static String GitLabHost(Uri repository) => repository.IsDefaultPort ? repository.IdnHost : $"{repository.IdnHost}:{repository.Port}";

    /// <summary>
    /// Returns the line and text of the first <c>{{</c> in <paramref name="text"/> that does not start one of the
    /// <paramref name="allowed"/> placeholders, or <c>null</c> if there is none.
    /// </summary>
    public static (Int32 Line, String Token)? FindInvalid(String text, IEnumerable<String> allowed)
    {
        var names = allowed.ToHashSet(StringComparer.Ordinal);
        for (var index = text.IndexOf("{{", StringComparison.Ordinal); index >= 0; index = text.IndexOf("{{", index + 2, StringComparison.Ordinal))
        {
            var match = Placeholder.Match(text, index);
            var placeholder = match.Success && (match.Index == index);
            if (!placeholder || !names.Contains(match.Groups["name"].Value))
            {
                return (text[..index].Count(character => character == '\n') + 1, placeholder ? match.Value : "{{");
            }
        }

        return null;
    }

    /// <summary>
    /// Fills in every placeholder. A placeholder on a line of its own takes the line's indentation for every line of its
    /// value, and the line is removed when the value is empty. The text must contain only known placeholders.
    /// </summary>
    public static String Fill(String text, IReadOnlyDictionary<String, String> values)
    {
        var result = new List<String>();
        foreach (var line in TextFile.SplitLines(text))
        {
            var trimmed = line.Trim();
            var whole = Placeholder.Match(trimmed);
            if (whole.Success && (whole.Length == trimmed.Length))
            {
                var indent = line[..(line.Length - line.TrimStart().Length)];
                var value = values[whole.Groups["name"].Value];
                if (value.Length > 0)
                {
                    result.AddRange(value.Split('\n').Select(valueLine => valueLine.Length == 0 ? valueLine : indent + valueLine));
                }
            }
            else
            {
                result.Add(Placeholder.Replace(line, match => values[match.Groups["name"].Value]));
            }
        }

        return String.Join("\n", result) + (text.EndsWith('\n') && (result.Count > 0) ? "\n" : String.Empty);
    }
}

internal static class StrongNameKey
{
    /// <summary>
    /// Generates a 1024-bit RSA key pair as a CAPI private key blob, the format of the <c>.snk</c> files that <c>sn -k</c> writes.
    /// </summary>
    public static Byte[] Generate()
    {
        using var rsa = new RSACryptoServiceProvider(1024);
        return rsa.ExportCspBlob(includePrivateParameters: true);
    }
}
