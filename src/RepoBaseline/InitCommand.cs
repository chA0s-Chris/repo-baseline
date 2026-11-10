// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Text;

internal static class InitCommand
{
    private const String GitIgnoreFile = ".gitignore";
    private const String LicenseFile = "LICENSE";

    public static Int32 Run(String target, String version, InitOptions options, ReleaseSource source)
    {
        var profiles = options.Profiles;
        var expected = ExpectedContent.Compose(source, profiles);
        var templates = RenderTemplates(source, options, Templates.Values(options));
        var keyPath = options.Library ? $"{options.Name}.snk" : null;
        RejectCollisions(source.Label, expected, templates, keyPath);

        // Block targets of unselected profiles get nothing written; a template on such a path is an ordinary template.
        var blockTargets = expected.ManagedFiles.Where(managed => managed.Blocks.Count > 0).ToList();
        RejectMalformedTemplates(source.Label, templates, blockTargets);

        if (File.Exists(Path.Combine(target, Manifest.FileName)) && !options.Force)
        {
            throw new ToolException($"{target} already has {Manifest.FileName}. Use sync to update its synced content, or init --force to overwrite its files.");
        }

        var plan = new Plan(target, options.Force);
        plan.Add(Manifest.FileName, ManifestContent(version, profiles), executable: false);
        foreach (var file in expected.Files)
        {
            plan.Add(file.Path, file.Content, file.Executable);
        }

        foreach (var template in templates.Values.Where(template => blockTargets.All(managed => managed.Path != template.Path)))
        {
            plan.Add(template.Path, Encoding.UTF8.GetBytes(template.Text), template.Executable);
        }

        foreach (var managed in blockTargets)
        {
            plan.AddManaged(managed, templates.GetValueOrDefault(managed.Path));
        }

        if (keyPath is not null)
        {
            // The key is ignored before it is written, so a failed write never leaves it unignored.
            plan.Ignore(keyPath, blockTargets.FirstOrDefault(managed => managed.Path == GitIgnoreFile));
            plan.AddKey(keyPath);
        }

        if (plan.Problems.Count > 0)
        {
            throw new ToolException([..plan.Problems, "init wrote nothing because of the problems above."]);
        }

        PendingWrite.ApplyAll(target, plan.Writes, "init");

        Console.WriteLine($"Initialized {target} with {source.Label}, profiles {String.Join(", ", profiles)}:");
        foreach (var write in plan.Writes)
        {
            Console.WriteLine($"  {TextFile.Display(Path.GetRelativePath(target, write.Path))}: {write.Description}");
        }

        if (plan.Skipped.Count > 0)
        {
            Console.WriteLine("Skipped existing files; --force overwrites them, except a strong-name key and names with several case variants:");
            foreach (var skipped in plan.Skipped)
            {
                Console.WriteLine($"  {skipped}");
            }

            if (plan.SkippedFiles)
            {
                Console.WriteLine("Run sync to update the synced content and blocks of skipped files.");
            }
        }

        return ExitCodes.Success;
    }

    private static Byte[] ManifestContent(String version, IReadOnlyList<String> profiles) =>
        Encoding.UTF8.GetBytes($$"""
                                 {
                                   "version": "{{version}}",
                                   "profiles": [{{String.Join(", ", profiles.Select(profile => $"\"{profile}\""))}}],
                                   "inspection": {
                                     "suppressedIds": []
                                   }
                                 }

                                 """);

    /// <summary>
    /// Reads and fills the templates of the selected profiles. A template replaces one of an earlier profile at the same
    /// target path. Placeholders are validated in every template, including a <c>LICENSE</c> that <c>--private</c> leaves out.
    /// </summary>
    private static OrderedDictionary<String, RenderedTemplate> RenderTemplates(ReleaseSource source, InitOptions options, IReadOnlyDictionary<String, String> values)
    {
        var rendered = new OrderedDictionary<String, RenderedTemplate>(StringComparer.Ordinal);
        foreach (var profile in options.Profiles)
        {
            foreach (var template in source.ReadTemplates(profile))
            {
                var origin = $"{profile}/templates/{template.Path}";
                if (Templates.FindInvalid(template.Path, [Templates.Name]) is { } invalidInPath)
                {
                    throw new ToolException($"{source.Label}: template {origin}: {invalidInPath.Token} is not allowed in a path; only {{{{{Templates.Name}}}}} is.");
                }

                var path = template.Path.Replace($"{{{{{Templates.Name}}}}}", options.Name, StringComparison.Ordinal);
                if (!ReleaseSource.IsRelativePath(path))
                {
                    throw new ToolException($"{source.Label}: template {origin} is not a valid target path as {path}; templates need relative paths inside the repository " +
                                            "that are valid on Windows, too.");
                }

                if (!TextFile.TryDecode(template.Content, out var text))
                {
                    throw new ToolException($"{source.Label}: template {origin} is not valid UTF-8.");
                }

                if (Templates.FindInvalid(text, values.Keys) is { } invalid)
                {
                    throw new ToolException(invalid.Token == "{{"
                                                ? $"{source.Label}: template {origin}: line {invalid.Line}: '{{{{' does not start a placeholder."
                                                : $"{source.Label}: template {origin}: line {invalid.Line}: unknown placeholder {invalid.Token}.");
                }

                if (options.Private && (path == LicenseFile))
                {
                    continue;
                }

                rendered[path] = new RenderedTemplate(path, Templates.Fill(text, values), template.Executable, origin);
            }
        }

        return rendered;
    }

    private static void RejectCollisions(String label, ExpectedContent expected, OrderedDictionary<String, RenderedTemplate> templates, String? keyPath)
    {
        foreach (var template in templates.Values)
        {
            if (expected.Files.FirstOrDefault(file => file.Path == template.Path) is { } file)
            {
                throw new ToolException($"{label}: template {template.Source} has the target path of a synced file of profile {file.Profile}.");
            }

            if (String.Equals(template.Path, Manifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ToolException($"{label}: template {template.Source} has the target path of {Manifest.FileName}, which init writes itself.");
            }

            if (String.Equals(template.Path, keyPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ToolException($"{label}: template {template.Source} has the target path of the strong-name key.");
            }
        }

        if (expected.Files.FirstOrDefault(file => String.Equals(file.Path, keyPath, StringComparison.OrdinalIgnoreCase)) is { } keyFile)
        {
            throw new ToolException($"{label}: profile {keyFile.Profile} syncs {keyFile.Path}, the target path of the strong-name key.");
        }

        ExpectedContent.RejectCollidingPaths(label, expected.Files.Select(file => file.Path)
                                                            .Concat(expected.ManagedFiles.Select(managed => managed.Path))
                                                            .Concat(templates.Keys)
                                                            .Concat(keyPath is null ? [] : [keyPath, GitIgnoreFile]));
    }

    /// <summary>
    /// A template of a block target must have usable block markers, because init fills its blocks.
    /// </summary>
    private static void RejectMalformedTemplates(String label, OrderedDictionary<String, RenderedTemplate> templates, IReadOnlyList<ManagedFile> blockTargets)
    {
        foreach (var managed in blockTargets)
        {
            if (templates.GetValueOrDefault(managed.Path) is not { } template)
            {
                continue;
            }

            var problems = ParsedFile.Parse(TextFile.SplitLines(template.Text), managed.Syntax).ProblemsFor(managed);
            if (problems.Count > 0)
            {
                throw new ToolException(problems.Select(problem => $"{label}: template {template.Source}: {problem}").ToList());
            }
        }
    }

    /// <summary>
    /// Collects the writes of init and the files it skips. Nothing is written while problems remain.
    /// </summary>
    private sealed class Plan
    {
        private readonly Boolean _force;
        private readonly String _target;
        private Boolean _keySkipped;

        public Plan(String target, Boolean force)
        {
            _target = target;
            _force = force;
        }

        public List<String> Problems { get; } = [];

        public List<String> Skipped { get; } = [];

        /// <summary>
        /// Whether files other than an existing strong-name key were skipped, which <c>sync</c> can update.
        /// </summary>
        public Boolean SkippedFiles => Skipped.Count > (_keySkipped ? 1 : 0);

        public List<PendingWrite> Writes { get; } = [];

        public void Add(String path, Byte[] content, Boolean executable)
        {
            if (IsWritable(path, out var fullPath, out var existingPath))
            {
                Writes.Add(Write(fullPath, existingPath, content, executable, existingPath is null ? "created" : "overwritten"));
            }
        }

        /// <summary>
        /// Writes a block target from its template, or merges the blocks into the existing file or an empty one.
        /// </summary>
        public void AddManaged(ManagedFile managed, RenderedTemplate? template)
        {
            if (!IsWritable(managed.Path, out var fullPath, out var existingPath))
            {
                return;
            }

            String text;
            if (template is not null)
            {
                text = template.Text;
            }
            else if (existingPath is null)
            {
                text = String.Empty;
            }
            else if (!TextFile.TryDecode(File.ReadAllBytes(existingPath), out text))
            {
                Problems.Add($"{TextFile.Display(Path.GetRelativePath(_target, existingPath))}: not valid UTF-8");
                return;
            }

            var parsed = ParsedFile.Parse(TextFile.SplitLines(text), managed.Syntax);
            var problems = parsed.ProblemsFor(managed);
            if (problems.Count > 0)
            {
                var shown = existingPath is null ? managed.Path : Path.GetRelativePath(_target, existingPath);
                Problems.AddRange(problems.Select(problem => $"{TextFile.Display(shown)}: {problem}"));
                return;
            }

            var description = existingPath is null ? "created" : template is not null ? "overwritten" : "blocks merged";
            Writes.Add(Write(fullPath, existingPath, Encoding.UTF8.GetBytes(BlockMerger.Apply(text, parsed, managed)), template?.Executable, description));
        }

        /// <summary>
        /// A new key would change the strong-name identity of a published library, so an existing one is kept even with --force.
        /// </summary>
        public void AddKey(String path)
        {
            if (TargetPath.Problem(_target, path) is { } problem)
            {
                Problems.Add(problem);
            }
            else if (File.Exists(Path.Combine(_target, path)))
            {
                Skipped.Add($"{path}: an existing strong-name key is never replaced");
                _keySkipped = true;
            }
            else
            {
                Writes.Add(new PendingWrite(Path.Combine(_target, path), StrongNameKey.Generate(), Executable: null, "created; the strong-name key, never commit it", Secret: true));
            }
        }

        /// <summary>
        /// Adds the key to the local part of <c>.gitignore</c>, also to an existing file, so that it cannot be committed by mistake.
        /// An existing file with malformed block markers is a problem: the line could end up inside a block, which <c>sync</c>
        /// later replaces.
        /// </summary>
        public void Ignore(String keyPath, ManagedFile? managed)
        {
            var fullPath = Path.Combine(_target, GitIgnoreFile);
            var planned = Writes.FindIndex(write => write.Path == fullPath);
            if (planned >= 0)
            {
                var write = Writes[planned];
                if (!Lists(Encoding.UTF8.GetString(write.Content ?? []), keyPath, managed))
                {
                    Writes[planned] = write with { Content = Append(write.Content ?? [], keyPath) };
                }

                return;
            }

            if (TargetPath.Problem(_target, GitIgnoreFile) is { } problem)
            {
                AddProblem(problem);
                return;
            }

            if (TargetPath.CaseVariants(_target, GitIgnoreFile) is { Count: > 0 } variants)
            {
                AddProblem($"{GitIgnoreFile}: exists as {String.Join(" and ", variants)}, so {keyPath} cannot be added to it; rename it to {GitIgnoreFile}");
                return;
            }

            if (!File.Exists(fullPath))
            {
                Writes.Add(new PendingWrite(fullPath, Encoding.UTF8.GetBytes($"{keyPath}\n"), Executable: null, "created"));
                return;
            }

            var content = File.ReadAllBytes(fullPath);
            if (!TextFile.TryDecode(content, out var text))
            {
                AddProblem($"{GitIgnoreFile}: not valid UTF-8, so {keyPath} cannot be added to it");
                return;
            }

            if (managed is not null)
            {
                var problems = ParsedFile.Parse(TextFile.SplitLines(text), managed.Syntax).ProblemsFor(managed);
                if (problems.Count > 0)
                {
                    foreach (var parseProblem in problems)
                    {
                        AddProblem($"{GitIgnoreFile}: {parseProblem}");
                    }

                    return;
                }
            }

            if (!Lists(text, keyPath, managed))
            {
                Skipped.Remove(GitIgnoreFile);
                Writes.Add(new PendingWrite(fullPath, Append(content, keyPath), Executable: null, $"kept, {keyPath} added"));
            }
        }

        /// <summary>
        /// Appends a line with the line ending the content already uses.
        /// </summary>
        private static Byte[] Append(Byte[] content, String line)
        {
            var newline = content.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n";
            var separator = (content.Length > 0) && (content[^1] != (Byte)'\n') ? newline : String.Empty;
            return [..content, ..Encoding.UTF8.GetBytes($"{separator}{line}{newline}")];
        }

        // AddManaged may already have reported the same problem for a forced .gitignore.
        private void AddProblem(String problem)
        {
            if (!Problems.Contains(problem))
            {
                Problems.Add(problem);
            }
        }

        /// <summary>
        /// Whether a line outside the managed blocks equals <paramref name="line"/> and no negation (<c>!…</c>) follows it.
        /// A line inside a block does not count, because <c>sync</c> replaces the block content. A later negation may
        /// re-include the key, so the line is appended again rather than evaluating Git's patterns.
        /// </summary>
        private static Boolean Lists(String text, String line, ManagedFile? managed)
        {
            var lines = TextFile.SplitLines(text.Replace("\r\n", "\n"));
            var inBlock = new Boolean[lines.Count];
            foreach (var block in managed is null ? [] : ParsedFile.Parse(lines, managed.Syntax).Blocks)
            {
                Array.Fill(inBlock, true, block.BeginLine, block.EndLine - block.BeginLine + 1);
            }

            var last = lines.Count - 1;
            while ((last >= 0) && (inBlock[last] || (lines[last].TrimEnd() != line)))
            {
                last--;
            }

            return (last >= 0) && !lines.Skip(last + 1).Any(candidate => candidate.StartsWith('!'));
        }

        /// <summary>
        /// Whether <paramref name="path"/> may be written. <paramref name="existingPath"/> is the existing file it replaces:
        /// the file of that name or, when there is none, the one file whose name differs only in case. Several such files
        /// are skipped, or with --force a problem, because none of them is clearly the one to replace.
        /// </summary>
        private Boolean IsWritable(String path, out String fullPath, out String? existingPath)
        {
            fullPath = Path.Combine(_target, path);
            existingPath = null;
            if (TargetPath.Problem(_target, path) is { } problem)
            {
                Problems.Add(problem);
                return false;
            }

            var variants = TargetPath.CaseVariants(_target, path);
            if (variants.Count > 1)
            {
                var names = String.Join(" and ", variants);
                if (_force)
                {
                    Problems.Add(TargetPath.Ambiguous(path, variants));
                }
                else
                {
                    Skipped.Add($"{path}: exists as {names}");
                }

                return false;
            }

            if (variants.Count == 1)
            {
                if (TargetPath.Problem(_target, variants[0]) is { } variantProblem)
                {
                    Problems.Add(variantProblem);
                    return false;
                }

                existingPath = Path.Combine(_target, variants[0]);
            }
            else if (File.Exists(fullPath))
            {
                existingPath = fullPath;
            }

            if ((existingPath is not null) && !_force)
            {
                Skipped.Add(existingPath == fullPath ? path : $"{path}: exists as {variants[0]}");
                return false;
            }

            return true;
        }

        private PendingWrite Write(String fullPath, String? existingPath, Byte[] content, Boolean? executable, String description) =>
            (existingPath is null) || (existingPath == fullPath)
                ? new PendingWrite(fullPath, content, executable, description)
                : new PendingWrite(
                    fullPath,
                    content,
                    executable,
                    $"{description}, renamed from {TextFile.Display(Path.GetRelativePath(_target, existingPath))}",
                    RenamedFrom: existingPath);
    }
}
