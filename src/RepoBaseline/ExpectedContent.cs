// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Text;

internal sealed record ExpectedBlock(String Id, String Content, BlockPosition Position);

/// <summary>
/// A file that can contain managed blocks: every block target file of the release, with the blocks the selected profiles define.
/// </summary>
internal sealed record ManagedFile(String Path, MarkerSyntax Syntax, IReadOnlyList<ExpectedBlock> Blocks);

/// <summary>
/// What the selected profiles of one release make the target repository contain.
/// </summary>
internal sealed class ExpectedContent
{
    private ExpectedContent(String label, IReadOnlyList<SyncedFile> files, IReadOnlyList<ManagedFile> managedFiles)
    {
        Label = label;
        Files = files;
        ManagedFiles = managedFiles;
    }

    public IReadOnlyList<SyncedFile> Files { get; }

    public String Label { get; }

    public IReadOnlyList<ManagedFile> ManagedFiles { get; }

    public static ExpectedContent Compose(ReleaseSource source, IReadOnlyList<String> profileNames)
    {
        var unknown = profileNames.Where(name => !source.Profiles.ContainsKey(name)).ToList();
        if (unknown.Count > 0)
        {
            throw new ToolException($"Unknown profile(s) {String.Join(", ", unknown)}; {source.Label} has {String.Join(", ", source.Profiles.Keys)}.");
        }

        var selected = profileNames.Select(name => source.Profiles[name]).ToList();
        var files = new Dictionary<String, SyncedFile>(StringComparer.Ordinal);
        foreach (var file in selected.SelectMany(profile => profile.Files))
        {
            if (files.TryGetValue(file.Path, out var existing))
            {
                throw new ToolException($"{source.Label}: profiles {existing.Profile} and {file.Profile} both provide {file.Path}.");
            }

            files[file.Path] = file;
        }

        RejectCollidingPaths(source.Label, files.Keys.Concat(source.Profiles.Values.SelectMany(profile => profile.Blocks).Select(block => block.File)));

        // Every block target file of the release is scanned, so blocks of a removed profile are found.
        var others = source.Profiles.Values.Where(profile => !profileNames.Contains(profile.Name));
        var managedPaths = selected.Concat(others).SelectMany(profile => profile.Blocks).Select(block => block.File).Distinct().ToList();
        var managedFiles = new List<ManagedFile>();
        foreach (var path in managedPaths)
        {
            var syntax = MarkerSyntax.ForFile(path)
                         ?? throw new ToolException($"{source.Label}: no block marker syntax is defined for {path}.");
            if (files.ContainsKey(path))
            {
                throw new ToolException($"{source.Label}: {path} is both a synced file and a block target.");
            }

            RejectMarkersInFragments(source, path, syntax);
            managedFiles.Add(new ManagedFile(path, syntax, ComposeBlocks(source.Label, selected, path)));
        }

        return new ExpectedContent(source.Label, files.Values.ToList(), managedFiles);
    }

    /// <summary>
    /// The manifest belongs to the target repository, and a target path cannot be a file and a directory at once. Paths are
    /// compared ignoring case and Unicode normalization, because the file systems of Windows and usually macOS would let them
    /// alias. The selected whole
    /// files are checked together with the block targets of every profile, because all block targets are scanned, and <c>init</c>
    /// adds its template targets.
    /// </summary>
    public static void RejectCollidingPaths(String label, IEnumerable<String> targetPaths)
    {
        var paths = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in targetPaths.Distinct(StringComparer.Ordinal))
        {
            if (!paths.TryAdd(path.Normalize(NormalizationForm.FormC), path))
            {
                throw new ToolException($"{label}: {paths[path.Normalize(NormalizationForm.FormC)]} and {path} differ only in case or Unicode normalization.");
            }
        }

        if (paths.ContainsKey(Manifest.FileName))
        {
            throw new ToolException($"{label}: a profile provides {Manifest.FileName}, which belongs to the target repository.");
        }

        foreach (var path in paths.Values)
        {
            var segments = path.Split('/');
            for (var count = 1; count < segments.Length; count++)
            {
                if (paths.TryGetValue(String.Join('/', segments[..count]).Normalize(NormalizationForm.FormC), out var parent))
                {
                    throw new ToolException($"{label}: {parent} is a file, but {path} needs it to be a directory.");
                }
            }
        }
    }

    /// <summary>
    /// A fragment line that looks like a block marker, or a fence the fragment does not close, would make the synced file malformed.
    /// </summary>
    private static void RejectMarkersInFragments(ReleaseSource source, String path, MarkerSyntax syntax)
    {
        foreach (var profile in source.Profiles.Values)
        {
            foreach (var block in profile.Blocks.Where(block => block.File == path))
            {
                var lines = TextFile.SplitLines(block.Content);
                var fenced = MarkdownFences.Find(lines, syntax.Fenced, out var unclosed);
                var fragment = $"the {profile.Name} fragment of block {block.Id} for {path}";
                if (unclosed is { } fenceLine)
                {
                    throw new ToolException($"{source.Label}: line {fenceLine + 1} of {fragment} opens a code fence that is never closed.");
                }

                var index = Enumerable.Range(0, lines.Count).FirstOrDefault(line => !fenced[line] && syntax.LooksLikeMarker(lines[line]), -1);
                if (index >= 0)
                {
                    throw new ToolException($"{source.Label}: line {index + 1} of {fragment} looks like a block marker.");
                }
            }
        }
    }

    /// <summary>
    /// Joins the fragments of each block in manifest profile order, separated by one empty line.
    /// The blocks are ordered by manifest profile order, then by <c>profile.json</c> order.
    /// </summary>
    private static List<ExpectedBlock> ComposeBlocks(String label, IEnumerable<Profile> selected, String path)
    {
        var order = new List<(String Id, BlockPosition Position, String Profile)>();
        var fragments = new Dictionary<String, List<String>>(StringComparer.Ordinal);
        foreach (var profile in selected)
        {
            foreach (var block in profile.Blocks.Where(block => block.File == path))
            {
                if (!fragments.TryGetValue(block.Id, out var list))
                {
                    fragments[block.Id] = list = [];
                    order.Add((block.Id, block.Position, profile.Name));
                }
                else if (order.First(entry => entry.Id == block.Id) is var first && (first.Position != block.Position))
                {
                    throw new ToolException($"{label}: block {block.Id} for {path} has position {Describe(first.Position)} in profile {first.Profile} " +
                                            $"but {Describe(block.Position)} in profile {profile.Name}.");
                }

                list.Add(block.Content);
            }
        }

        return order.Select(entry => new ExpectedBlock(entry.Id, String.Join("\n", fragments[entry.Id]), entry.Position)).ToList();

        static String Describe(BlockPosition position) => position == BlockPosition.Start ? "start" : "end";
    }
}
