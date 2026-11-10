// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

internal static class CheckCommand
{
    private const String SharedReSharperSettings = ".repo-baseline/ReSharper.DotSettings";
    private const String InjectedLayersPrefix = "/Default/Environment/InjectedLayers/";
    private const String InjectionPath = @"..\.repo-baseline\ReSharper.DotSettings";
    private const String CentralPackageFile = "Directory.Packages.props";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly Regex InjectionKey = new(@"^/Default/Environment/InjectedLayers/FileInjectedLayer/=(?<guid>[^/]+)/RelativePath/@EntryValue\z");

    // Each synced .props file and the repository's own build file that imports it, as in the layout guide.
    private static readonly (String Synced, String ImportedBy)[] Imports =
    [
        (".repo-baseline/Common.props", "Directory.Build.props"),
        (".repo-baseline/Tests.props", "tests/Directory.Build.props"),
        (".repo-baseline/Packaging.props", "src/Directory.Build.props")
    ];

    public static Int32 Run(String target, ExpectedContent expected)
    {
        var findings = new List<String>();
        foreach (var file in expected.Files)
        {
            CheckFile(target, file, expected.Label, findings);
        }

        foreach (var managed in expected.ManagedFiles)
        {
            CheckManagedFile(target, managed, expected.Label, findings);
        }

        CheckReSharperSettings(target, expected, findings);
        CheckImports(target, expected, findings);
        CheckPackageVersions(target, expected, findings);

        foreach (var finding in findings)
        {
            Console.WriteLine(finding);
        }

        if (findings.Count == 0)
        {
            Console.WriteLine($"No findings: the synced content of {target} matches {expected.Label}.");
            return ExitCodes.Success;
        }

        Console.WriteLine();
        Console.WriteLine($"{findings.Count} finding(s) against {expected.Label}.");
        return ExitCodes.Findings;
    }

    private static void CheckFile(String target, SyncedFile file, String label, List<String> findings)
    {
        if (TargetPath.Problem(target, file.Path) is { } problem)
        {
            findings.Add(problem);
            return;
        }

        if (TargetPath.CaseVariants(target, file.Path) is { Count: > 0 } variants)
        {
            findings.Add(ExistsOnlyAs(file.Path, variants));
            return;
        }

        var path = Path.Combine(target, file.Path);
        if (!File.Exists(path))
        {
            findings.Add($"{file.Path}: missing");
            return;
        }

        // BOM and line endings are only normalized for text; a file that is not valid UTF-8 must match byte for byte.
        var content = File.ReadAllBytes(path);
        var equivalent = content.AsSpan().SequenceEqual(file.Content)
                         || (TextFile.TryDecode(file.Content, out var expectedText) && TextFile.TryDecode(content, out var actual) && (actual == expectedText));
        if (!equivalent)
        {
            findings.Add($"{file.Path}: differs from {label}");
        }

        if (FileModes.SupportsExecutableBit && (FileModes.IsExecutable(path) != file.Executable))
        {
            findings.Add($"{file.Path}: executable bit differs from {label}");
        }
    }

    private static void CheckManagedFile(String target, ManagedFile managed, String label, List<String> findings)
    {
        var path = Path.Combine(target, managed.Path);
        if (SyncCommand.IsUnusedTarget(target, managed, path))
        {
            return;
        }

        if (TargetPath.Problem(target, managed.Path) is { } problem)
        {
            findings.Add(problem);
            return;
        }

        if (TargetPath.CaseVariants(target, managed.Path) is { Count: > 0 } variants)
        {
            findings.Add(ExistsOnlyAs(managed.Path, variants));
            return;
        }

        if (!File.Exists(path))
        {
            findings.AddRange(managed.Blocks.Select(block => $"{managed.Path}: block {block.Id}: missing"));
            return;
        }

        if (!TextFile.TryDecode(File.ReadAllBytes(path), out var text))
        {
            if (managed.Blocks.Count > 0)
            {
                findings.Add($"{managed.Path}: not valid UTF-8");
            }

            return;
        }

        var lines = TextFile.SplitLines(text);
        var parsed = ParsedFile.Parse(lines, managed.Syntax);
        var problems = parsed.ProblemsFor(managed);
        if (problems.Count > 0)
        {
            findings.AddRange(problems.Select(problem => $"{managed.Path}: {problem}"));
            return;
        }

        foreach (var block in managed.Blocks)
        {
            var actual = parsed.Blocks.FirstOrDefault(candidate => candidate.Id == block.Id);
            if (actual is null)
            {
                findings.Add($"{managed.Path}: block {block.Id}: missing");
            }
            else if (ParsedFile.Content(lines, actual) != block.Content)
            {
                findings.Add($"{managed.Path}: block {block.Id}: differs from {label}");
            }
        }

        var defined = managed.Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
        findings.AddRange(parsed.Blocks
                                .Where(block => !defined.Contains(block.Id))
                                .Select(block => $"{managed.Path}: block {block.Id}: not defined by any selected profile"));
    }

    private static void CheckReSharperSettings(String target, ExpectedContent expected, List<String> findings)
    {
        var shared = expected.Files.FirstOrDefault(file => file.Path == SharedReSharperSettings);
        if (shared is null)
        {
            return;
        }

        var solutions = Directory.GetFiles(target, "*.slnx", SearchOption.TopDirectoryOnly)
                                 .Select(Path.GetFileName)
                                 .OfType<String>()
                                 .Where(name => name.EndsWith(".slnx", StringComparison.Ordinal))
                                 .Order(StringComparer.Ordinal)
                                 .ToList();
        if (solutions.Count != 1)
        {
            findings.Add(solutions.Count == 0
                             ? "*.slnx: no solution in the repository root, so the ReSharper settings cannot be checked"
                             : $"*.slnx: several solutions in the repository root ({String.Join(", ", solutions)}), so the ReSharper settings cannot be checked");
            return;
        }

        var localName = $"{solutions[0]}.DotSettings";
        var localPath = Path.Combine(target, localName);
        if (!File.Exists(localPath))
        {
            findings.Add($"{localName}: missing");
            return;
        }

        var (sharedKeys, sharedProblem) = ReadSettings(shared.Content);
        if (sharedProblem is not null)
        {
            throw new ToolException($"{expected.Label}: {SharedReSharperSettings}: {sharedProblem}.");
        }

        var (localSettings, localProblem) = ReadSettings(File.ReadAllBytes(localPath));
        if (localProblem is not null)
        {
            findings.Add($"{localName}: {localProblem}");
            return;
        }

        // ReSharper only registers a file layer whose index entry is defined for the same GUID as its path.
        var injected = localSettings
                       .Select(setting => InjectionKey.Match(setting.Key) is { Success: true } match && (setting.Value == InjectionPath) ? match.Groups["guid"].Value : null)
                       .OfType<String>()
                       .Any(guid => localSettings.Any(setting => (setting.Key == $"{InjectedLayersPrefix}FileInjectedLayer/={guid}/@KeyIndexDefined")
                                                                 && String.Equals(setting.Value, "True", StringComparison.OrdinalIgnoreCase)));
        if (!injected)
        {
            findings.Add($"{localName}: does not inject {InjectionPath}");
        }

        var sharedKeySet = sharedKeys.Select(setting => setting.Key).ToHashSet(StringComparer.Ordinal);
        findings.AddRange(localSettings
                          .Select(setting => setting.Key)
                          .Where(key => !key.StartsWith(InjectedLayersPrefix, StringComparison.Ordinal) && sharedKeySet.Contains(key))
                          .Distinct()
                          .Select(key => $"{localName}: key {key}: redefines a shared key"));
    }

    /// <summary>
    /// Reads the settings of a DotSettings file: the keyed direct children of its <c>wpf:ResourceDictionary</c> root.
    /// </summary>
    private static (List<(String Key, String Value)> Settings, String? Problem) ReadSettings(Byte[] content)
    {
        try
        {
            // Invalid UTF-8 counts as invalid XML, as it does for ReSharper; a lenient decode would hide it.
            if (!TextFile.TryDecode(content, out var text))
            {
                return ([], "not valid XML");
            }

            var root = XDocument.Parse(text).Root;
            if ((root is null) || (root.Name != Wpf + "ResourceDictionary"))
            {
                return ([], "has no wpf:ResourceDictionary root");
            }

            var settings = root.Elements()
                               .Select(element => (Key: (String?)element.Attribute(Xaml + "Key"), element.Value))
                               .Where(setting => setting.Key is not null)
                               .Select(setting => (setting.Key ?? String.Empty, setting.Value))
                               .ToList();
            return (settings, null);
        }
        catch (XmlException)
        {
            return ([], "not valid XML");
        }
    }

    private static void CheckImports(String target, ExpectedContent expected, List<String> findings)
    {
        foreach (var (synced, importedBy) in Imports.Where(import => expected.Files.Any(file => file.Path == import.Synced)))
        {
            // MSBuild expressions are not evaluated: any Import whose path ends with the synced file counts.
            var path = Path.Combine(target, importedBy);
            var projects = File.Exists(path) ? ReadAttributes(File.ReadAllBytes(path), "Import", "Project") : [];
            if (projects is null)
            {
                findings.Add($"{importedBy}: not valid XML");
            }
            else if (!projects.Any(project => project.Replace('\\', '/').EndsWith(synced, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add($"{importedBy}: does not import {synced}");
            }
        }
    }

    // TargetPath.CaseVariants compares ordinal names, so a case variant is reported on case-insensitive file systems too.
    private static String ExistsOnlyAs(String relativePath, IReadOnlyList<String> variants) =>
        $"{relativePath}: exists only as {String.Join(" and ", variants)}";

    private static void CheckPackageVersions(String target, ExpectedContent expected, List<String> findings)
    {
        var references = expected.Files
                                 .Where(file => file.Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
                                 .SelectMany(file => (ReadAttributes(file.Content, "PackageReference", "Include")
                                                      ?? throw new ToolException($"{expected.Label}: {file.Path} is not valid XML."))
                                                     .Select(package => (File: file.Path, Package: package)))
                                 .ToList();
        if (references.Count == 0)
        {
            return;
        }

        var centralPath = Path.Combine(target, CentralPackageFile);
        var versions = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(centralPath))
        {
            var items = ReadAttributes(File.ReadAllBytes(centralPath), "PackageVersion", "Include");
            if (items is null)
            {
                findings.Add($"{CentralPackageFile}: not valid XML");
                return;
            }

            versions.UnionWith(items);
        }

        findings.AddRange(references
                          .Where(reference => !versions.Contains(reference.Package))
                          .Select(reference => $"{reference.File}: package {reference.Package}: no PackageVersion in {CentralPackageFile}"));
    }

    private static List<String>? ReadAttributes(Byte[] content, String elementName, String attributeName)
    {
        try
        {
            if (!TextFile.TryDecode(content, out var text))
            {
                return null;
            }

            return XDocument.Parse(text)
                            .Descendants()
                            .Where(element => element.Name.LocalName == elementName)
                            .Select(element => (String?)element.Attribute(attributeName))
                            .OfType<String>()
                            .Where(value => value.Length > 0)
                            .ToList();
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
