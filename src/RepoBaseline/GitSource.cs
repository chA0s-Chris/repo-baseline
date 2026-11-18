// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Text;

/// <summary>
/// Reads a release from the Git objects of a <c>repo-baseline</c> checkout, so that its working tree and <c>HEAD</c> do not matter.
/// </summary>
internal static class GitSource
{
    public static ReleaseSource Load(String checkout, String revision, String label, String kind)
    {
        if (!Git.TryRun(checkout, out var treeOutput, out var error, "rev-parse", "--verify", "--quiet", $"{revision}^{{tree}}"))
        {
            throw new ToolException(error.Trim().Length == 0
                                        ? $"{kind} {label} not found in {checkout}."
                                        : $"Cannot read {kind} {label} in {checkout}: {error.Trim()}");
        }

        var tree = Encoding.UTF8.GetString(treeOutput).Trim();
        return ReleaseSource.Create(label, ListTree(checkout, label, tree));
    }

    private static Dictionary<String, SourceEntry> ListTree(String checkout, String label, String tree)
    {
        // A lenient decode would turn invalid bytes into U+FFFD and write the file under another name.
        if (!TextFile.TryDecodeExact(Git.Run(checkout, "ls-tree", "-r", "-z", tree, "--", ReleaseSource.ProfilesDirectory), out var output))
        {
            throw new ToolException($"{label}: a path under {ReleaseSource.ProfilesDirectory} is not valid UTF-8.");
        }

        var entries = new Dictionary<String, SourceEntry>(StringComparer.Ordinal);
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // <mode> SP <type> SP <object> TAB <path>
            var tab = record.IndexOf('\t');
            var fields = record[..tab].Split(' ');
            var path = record[(tab + 1)..];
            var objectId = fields[2];
            entries[path] = new SourceEntry(fields[0], () => Git.Run(checkout, "cat-file", "blob", objectId));
        }

        return entries;
    }
}
