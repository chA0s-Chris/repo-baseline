// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

/// <summary>
/// Synced paths must be regular files inside the target repository, so that the tool never writes through a link.
/// </summary>
internal static class TargetPath
{
    public static String? Problem(String target, String relativePath)
    {
        var segments = relativePath.Split('/');
        var current = target;
        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            var isLast = index == segments.Length - 1;
            var segment = String.Join('/', segments[..(index + 1)]);
            if (new FileInfo(current).LinkTarget is not null)
            {
                var link = isLast ? "is a symbolic link" : $"{segment} is a symbolic link";
                return $"{relativePath}: {link}; synced files must be regular files inside the repository";
            }

            if (isLast ? Directory.Exists(current) : File.Exists(current))
            {
                return isLast ? $"{relativePath}: is a directory" : $"{relativePath}: {segment} is not a directory";
            }
        }

        return null;
    }

    /// <summary>
    /// The paths of the entries next to <paramref name="relativePath"/> whose names differ from its name only in case,
    /// compared by ordinal name so that the result is the same on case-sensitive and case-insensitive file systems.
    /// Empty when an entry has exactly the name. Only the last path component is compared.
    /// </summary>
    public static List<String> CaseVariants(String target, String relativePath)
    {
        var separator = relativePath.LastIndexOf('/');
        var directory = separator < 0 ? String.Empty : relativePath[..separator];
        var name = relativePath[(separator + 1)..];
        var fullDirectory = Path.Combine(target, directory);
        if (!Directory.Exists(fullDirectory))
        {
            return [];
        }

        var matches = Directory.EnumerateFileSystemEntries(fullDirectory)
                               .Select(entry => Path.GetFileName(entry))
                               .Where(entry => String.Equals(entry, name, StringComparison.OrdinalIgnoreCase))
                               .ToList();
        return matches.Contains(name, StringComparer.Ordinal)
            ? []
            : matches.Order(StringComparer.Ordinal).Select(entry => separator < 0 ? entry : $"{directory}/{entry}").ToList();
    }

    /// <summary>
    /// The problem when several files differ from <paramref name="relativePath"/> only in case and none has exactly its name.
    /// </summary>
    public static String Ambiguous(String relativePath, IReadOnlyList<String> variants) =>
        $"{relativePath}: {String.Join(" and ", variants)} differ from it only in case, so it is unclear which one to replace";
}
