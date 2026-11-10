// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Text;

internal static class SyncCommand
{
    public static Int32 Run(String target, Manifest manifest, String? version, ExpectedContent expected)
    {
        var writes = new List<PendingWrite>();
        var problems = new List<String>();
        foreach (var file in expected.Files)
        {
            if (TargetPath.Problem(target, file.Path) is { } problem)
            {
                problems.Add(problem);
                continue;
            }

            if (TryFindVariant(target, file.Path, problems, out var variant))
            {
                writes.AddRange(PlanFile(target, file, variant));
            }
        }

        foreach (var managed in expected.ManagedFiles)
        {
            if (PlanManagedFile(target, managed, problems) is { } write)
            {
                writes.Add(write);
            }
        }

        if ((version is not null) && (version != manifest.Version))
        {
            if (TargetPath.Problem(target, Manifest.FileName) is { } problem)
            {
                problems.Add(problem);
            }
            else
            {
                writes.Add(new PendingWrite(manifest.Path, manifest.WithVersion(version), Executable: null, $"version {version}"));
            }
        }

        if (problems.Count > 0)
        {
            throw new ToolException([..problems, "sync wrote nothing because of the problems above."]);
        }

        PendingWrite.ApplyAll(target, writes, "sync");

        if (writes.Count == 0)
        {
            Console.WriteLine($"{target} is up to date with {expected.Label}.");
        }
        else
        {
            Console.WriteLine($"Synced {target} with {expected.Label}:");
            foreach (var write in writes)
            {
                Console.WriteLine($"  {TextFile.Display(Path.GetRelativePath(target, write.Path))}: {write.Description}");
            }
        }

        return ExitCodes.Success;
    }

    private static IEnumerable<PendingWrite> PlanFile(String target, SyncedFile file, String? variant)
    {
        var path = Path.Combine(target, file.Path);
        if (variant is not null)
        {
            var variantPath = Path.Combine(target, variant);
            var differs = !File.ReadAllBytes(variantPath).AsSpan().SequenceEqual(file.Content);
            var renamed = $"renamed from {TextFile.Display(variant)}";
            yield return new PendingWrite(path, differs ? file.Content : null, file.Executable, differs ? $"updated, {renamed}" : renamed,
                RenamedFrom: variantPath);
            yield break;
        }

        if (!File.Exists(path))
        {
            yield return new PendingWrite(path, file.Content, file.Executable, "created");
            yield break;
        }

        var contentDiffers = !File.ReadAllBytes(path).AsSpan().SequenceEqual(file.Content);
        var modeDiffers = FileModes.SupportsExecutableBit && (FileModes.IsExecutable(path) != file.Executable);
        if (contentDiffers || modeDiffers)
        {
            var description = contentDiffers ? "updated" : file.Executable ? "executable bit set" : "executable bit cleared";
            yield return new PendingWrite(path, contentDiffers ? file.Content : null, file.Executable, description);
        }
    }

    private static PendingWrite? PlanManagedFile(String target, ManagedFile managed, List<String> problems)
    {
        var path = Path.Combine(target, managed.Path);
        if (IsUnusedTarget(target, managed, path))
        {
            return null;
        }

        if (TargetPath.Problem(target, managed.Path) is { } problem)
        {
            problems.Add(problem);
            return null;
        }

        if (!TryFindVariant(target, managed.Path, problems, out var variant))
        {
            return null;
        }

        // A case variant is read, reported and replaced under the expected name.
        var shown = variant ?? managed.Path;
        var readPath = Path.Combine(target, shown);
        var exists = File.Exists(readPath);

        if (!TextFile.TryDecode(exists ? File.ReadAllBytes(readPath) : [], out var text))
        {
            // Without selected blocks, nothing would be written to it, and it cannot contain blocks in this encoding.
            if (managed.Blocks.Count > 0)
            {
                problems.Add($"{TextFile.Display(shown)}: not valid UTF-8");
            }

            return null;
        }

        var parsed = ParsedFile.Parse(TextFile.SplitLines(text), managed.Syntax);
        var fileProblems = parsed.ProblemsFor(managed);
        if (fileProblems.Count > 0)
        {
            problems.AddRange(fileProblems.Select(problem => $"{TextFile.Display(shown)}: {problem}"));
            return null;
        }

        var merged = BlockMerger.Apply(text, parsed, managed);
        var current = exists && (merged == text);
        if (variant is not null)
        {
            var renamed = $"renamed from {TextFile.Display(variant)}";
            return new PendingWrite(path, current ? null : Encoding.UTF8.GetBytes(merged), Executable: null, current ? renamed : $"updated, {renamed}",
                RenamedFrom: readPath);
        }

        // A file whose blocks are already current is left alone, even if it has a BOM or CRLF line endings.
        if (current)
        {
            return null;
        }

        return new PendingWrite(path, Encoding.UTF8.GetBytes(merged), Executable: null, exists ? "updated" : "created");
    }

    /// <summary>
    /// Finds the one file whose name differs from <paramref name="relativePath"/> only in case, when no file has exactly
    /// its name. Several such files, or a variant that is not a regular file inside the repository, are a problem.
    /// </summary>
    private static Boolean TryFindVariant(String target, String relativePath, List<String> problems, out String? variant)
    {
        variant = null;
        var variants = TargetPath.CaseVariants(target, relativePath);
        if (variants.Count > 1)
        {
            problems.Add(TargetPath.Ambiguous(relativePath, variants));
            return false;
        }

        if (variants.Count == 1)
        {
            if (TargetPath.Problem(target, variants[0]) is { } problem)
            {
                problems.Add(problem);
                return false;
            }

            variant = variants[0];
        }

        return true;
    }

    /// <summary>
    /// A block target that no selected profile uses is only scanned for leftover blocks when it is a regular file inside the repository.
    /// </summary>
    public static Boolean IsUnusedTarget(String target, ManagedFile managed, String path) =>
        (managed.Blocks.Count == 0) && ((TargetPath.Problem(target, managed.Path) is not null) || !File.Exists(path));
}
