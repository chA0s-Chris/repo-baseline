#:property TargetFramework=net10.0
#:property RollForward=Major

// repo-baseline tool: `sync` writes the synced content of a pinned release into a repository, and
// `check` reports where that content has been changed locally. docs/design.md describes profiles,
// managed blocks and the manifest; the README describes how to run the tool.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

return Cli.Run(args);

internal static class ExitCodes
{
    public const Int32 Success = 0;
    public const Int32 Findings = 1;
    public const Int32 Error = 2;
}

/// <summary>
/// A usage, manifest or source error. The tool prints every message and exits with code 2.
/// </summary>
internal class ToolException : Exception
{
    public ToolException(String message) : this([message]) { }

    public ToolException(IReadOnlyList<String> messages) : base(String.Join(Environment.NewLine, messages))
    {
        Messages = messages;
    }

    public IReadOnlyList<String> Messages { get; }
}

internal static class Cli
{
    private const String Usage = """
        Usage:
          dotnet run tool/sync.cs -- sync  <path> [<version>] [--ref <git-ref>]
          dotnet run tool/sync.cs -- check <path> [--ref <git-ref>]

        sync   writes the synced content of the release pinned in <path>/.repo-baseline.json.
               With <version>, it syncs that release and records it in the manifest.
        check  reports synced content that differs from the pinned release.
        --ref  reads the profile content of any Git ref of this repo-baseline checkout instead.

        Exit codes: 0 success, 1 check findings, 2 usage, manifest or source errors.
        """;

    public static Int32 Run(String[] args)
    {
        try
        {
            if (args.Any(argument => argument is "-h" or "--help"))
            {
                Console.WriteLine(Usage);
                return ExitCodes.Success;
            }

            var options = Options.Parse(args);
            var target = ResolveTarget(options.TargetPath);
            var manifest = Manifest.Load(target);
            var checkout = BaselineCheckout.Locate();
            var version = options.Version ?? manifest.Version;
            var source = options.Ref is { } gitRef
                ? ReleaseSource.Load(checkout, gitRef, label: gitRef, kind: "ref")
                : ReleaseSource.Load(checkout, $"refs/tags/v{version}", label: $"v{version}", kind: "tag");
            var expected = ExpectedContent.Compose(source, manifest.Profiles);

            return options.Command == Options.SyncCommand
                ? SyncCommand.Run(target, manifest, options.Version, expected)
                : CheckCommand.Run(target, expected);
        }
        catch (ToolException exception)
        {
            foreach (var message in exception.Messages)
            {
                Console.Error.WriteLine($"error: {message}");
            }

            if (exception is UsageException)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(Usage);
            }

            return ExitCodes.Error;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return ExitCodes.Error;
        }
    }

    private static String ResolveTarget(String path)
    {
        var target = Path.GetFullPath(path);
        if (!Directory.Exists(target))
        {
            throw new ToolException($"Target repository {target} does not exist.");
        }

        return target;
    }
}

internal sealed class UsageException : ToolException
{
    public UsageException(String message) : base(message) { }
}

internal sealed record Options(String Command, String TargetPath, String? Version, String? Ref)
{
    public const String SyncCommand = "sync";
    public const String CheckCommand = "check";

    public static Options Parse(IReadOnlyList<String> args)
    {
        var positional = new List<String>();
        String? gitRef = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument == "--ref" || argument.StartsWith("--ref=", StringComparison.Ordinal))
            {
                if (gitRef is not null)
                {
                    throw new UsageException("--ref is given more than once.");
                }

                gitRef = argument == "--ref"
                    ? (index + 1 < args.Count ? args[++index] : throw new UsageException("--ref needs a value."))
                    : argument["--ref=".Length..];
                if ((gitRef.Length == 0) || gitRef.StartsWith('-'))
                {
                    throw new UsageException($"'{gitRef}' is not a valid Git ref.");
                }
            }
            else if (argument.StartsWith('-'))
            {
                throw new UsageException($"Unknown option '{argument}'.");
            }
            else
            {
                positional.Add(argument);
            }
        }

        if (positional.Count == 0)
        {
            throw new UsageException("No command given.");
        }

        var command = positional[0];
        if (command is not (SyncCommand or CheckCommand))
        {
            throw new UsageException($"Unknown command '{command}'.");
        }

        if ((positional.Count < 2) || String.IsNullOrWhiteSpace(positional[1]))
        {
            throw new UsageException($"{command} needs the path of the target repository.");
        }

        var maximum = command == SyncCommand ? 3 : 2;
        if (positional.Count > maximum)
        {
            throw new UsageException($"Unexpected argument '{positional[maximum]}'.");
        }

        var version = positional.Count == 3 ? NormalizeVersion(positional[2]) : null;
        if ((version is not null) && (gitRef is not null))
        {
            throw new UsageException("<version> and --ref cannot be combined.");
        }

        return new Options(command, positional[1], version, gitRef);
    }

    private static String NormalizeVersion(String value)
    {
        var version = value.StartsWith('v') ? value[1..] : value;
        if (version.StartsWith('v') || !Versions.IsValid(version))
        {
            throw new UsageException($"'{value}' is not a valid version.");
        }

        return version;
    }
}

internal static class Versions
{
    // `\z` instead of `$`, which also matches before a final newline.
    private static readonly Regex Pattern = new(@"^[0-9A-Za-z][0-9A-Za-z.+-]*\z");

    public static Boolean IsValid(String version) => Pattern.IsMatch(version);
}

/// <summary>
/// The target repository's <c>.repo-baseline.json</c>.
/// </summary>
internal sealed class Manifest
{
    public const String FileName = ".repo-baseline.json";

    private readonly Byte[] _content;

    private Manifest(String path, Byte[] content, String version, IReadOnlyList<String> profiles)
    {
        Path = path;
        _content = content;
        Version = version;
        Profiles = profiles;
    }

    public String Path { get; }

    public IReadOnlyList<String> Profiles { get; }

    public String Version { get; }

    public static Manifest Load(String target)
    {
        var path = System.IO.Path.Combine(target, FileName);
        if (!File.Exists(path))
        {
            throw new ToolException($"{FileName} not found in {target}.");
        }

        var content = File.ReadAllBytes(path);
        try
        {
            using var document = JsonDocument.Parse(TextFile.WithoutBom(content));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("the root is not an object");
            }

            var keys = new HashSet<String>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                {
                    throw Invalid($"key '{property.Name}' appears more than once");
                }
            }

            if (!root.TryGetProperty("version", out var versionElement) || (versionElement.ValueKind != JsonValueKind.String)
                                                                       || String.IsNullOrEmpty(versionElement.GetString()))
            {
                throw Invalid("'version' must be a non-empty string");
            }

            var version = versionElement.GetString() ?? String.Empty;
            if (version.StartsWith('v') && Versions.IsValid(version[1..]))
            {
                throw Invalid($"'version' must not start with 'v'; write \"{version[1..]}\"");
            }

            if (version.StartsWith('v') || !Versions.IsValid(version))
            {
                throw Invalid($"'{version}' is not a valid version");
            }

            if (!root.TryGetProperty("profiles", out var profilesElement) || (profilesElement.ValueKind != JsonValueKind.Array)
                                                                         || (profilesElement.GetArrayLength() == 0))
            {
                throw Invalid("'profiles' must be a non-empty array");
            }

            var profiles = new List<String>();
            foreach (var element in profilesElement.EnumerateArray())
            {
                var profile = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
                if (String.IsNullOrEmpty(profile))
                {
                    throw Invalid("every entry of 'profiles' must be a non-empty string");
                }

                if (profiles.Contains(profile))
                {
                    throw Invalid($"profile '{profile}' is listed more than once");
                }

                profiles.Add(profile);
            }

            return new Manifest(path, content, version, profiles);
        }
        catch (JsonException exception)
        {
            throw Invalid(exception.Message);
        }

        ToolException Invalid(String reason) => new($"{path} is invalid: {reason}.");
    }

    /// <summary>
    /// Returns the manifest content with a different <c>version</c> value and everything else unchanged.
    /// </summary>
    public Byte[] WithVersion(String version)
    {
        var content = TextFile.WithoutBom(_content);
        var reader = new Utf8JsonReader(content);
        while (reader.Read())
        {
            if ((reader.TokenType == JsonTokenType.PropertyName) && (reader.CurrentDepth == 1) && reader.ValueTextEquals("version"))
            {
                reader.Read();
                var start = (Int32)reader.TokenStartIndex;
                var end = (Int32)reader.BytesConsumed;
                return [..content[..start], ..Encoding.UTF8.GetBytes($"\"{version}\""), ..content[end..]];
            }
        }

        throw new ToolException($"{Path} has no top-level 'version'.");
    }
}

/// <summary>
/// The <c>repo-baseline</c> checkout that contains this tool and the profile content.
/// </summary>
internal static class BaselineCheckout
{
    public static String Locate()
    {
        var entryPoint = AppContext.GetData("EntryPointFilePath") as String
                         ?? throw new ToolException("Cannot find the location of sync.cs. Run the tool with `dotnet run tool/sync.cs`.");
        var toolDirectory = Path.GetDirectoryName(Path.GetFullPath(entryPoint))
                            ?? throw new ToolException($"Cannot find the directory of {entryPoint}.");
        if (!Git.TryRun(toolDirectory, out var output, out var error, "rev-parse", "--show-toplevel"))
        {
            throw new ToolException($"{toolDirectory} is not inside a usable Git checkout of repo-baseline: {error.Trim()}");
        }

        return Encoding.UTF8.GetString(output).Trim();
    }
}

internal static class Git
{
    // Inherited from a Git hook or a wrapper, these would override `-C` and point git at another repository.
    private static readonly String[] RepositoryVariables =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE"
    ];

    public static Byte[] Run(String directory, params String[] arguments)
    {
        if (!TryRun(directory, out var output, out var error, arguments))
        {
            throw new ToolException($"git {String.Join(' ', arguments)} failed in {directory}: {error.Trim()}");
        }

        return output;
    }

    public static Boolean TryRun(String directory, out Byte[] output, out String error, params String[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var variable in RepositoryVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(directory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new ToolException("Cannot start git.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ToolException($"Cannot start git: {exception.Message}");
        }

        using (process)
        {
            var errorTask = process.StandardError.ReadToEndAsync();
            using var buffer = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(buffer);
            process.WaitForExit();
            output = buffer.ToArray();
            error = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
    }
}

internal enum BlockPosition
{
    Start,
    End
}

internal sealed record BlockDefinition(String Id, String File, String Content, BlockPosition Position);

internal sealed record SyncedFile(String Path, Byte[] Content, Boolean Executable, String Profile);

internal sealed record Profile(String Name, IReadOnlyList<BlockDefinition> Blocks, IReadOnlyList<SyncedFile> Files);

/// <summary>
/// The profiles of one release, read from the Git object store so that the working tree and <c>HEAD</c> do not matter.
/// </summary>
internal sealed class ReleaseSource
{
    private const String ProfilesDirectory = "profiles/";
    private static readonly Regex ReservedName = new(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(\..*)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private ReleaseSource(String label, IReadOnlyDictionary<String, Profile> profiles)
    {
        Label = label;
        Profiles = profiles;
    }

    public String Label { get; }

    public IReadOnlyDictionary<String, Profile> Profiles { get; }

    public static ReleaseSource Load(String checkout, String revision, String label, String kind)
    {
        if (!Git.TryRun(checkout, out var treeOutput, out var error, "rev-parse", "--verify", "--quiet", $"{revision}^{{tree}}"))
        {
            throw new ToolException(error.Trim().Length == 0
                                        ? $"{kind} {label} not found in {checkout}."
                                        : $"Cannot read {kind} {label} in {checkout}: {error.Trim()}");
        }

        var tree = Encoding.UTF8.GetString(treeOutput).Trim();
        var entries = ListTree(checkout, label, tree);
        var names = entries.Keys
                           .Select(path => path[ProfilesDirectory.Length..])
                           .Where(path => path.Contains('/'))
                           .Select(path => path.Split('/')[0])
                           .Distinct()
                           .Order(StringComparer.Ordinal);
        var profiles = new Dictionary<String, Profile>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            profiles[name] = LoadProfile(checkout, label, name, entries);
        }

        return new ReleaseSource(label, profiles);
    }

    private static Dictionary<String, TreeEntry> ListTree(String checkout, String label, String tree)
    {
        // A lenient decode would turn invalid bytes into U+FFFD and write the file under another name.
        if (!TextFile.TryDecodeExact(Git.Run(checkout, "ls-tree", "-r", "-z", tree, "--", ProfilesDirectory), out var output))
        {
            throw new ToolException($"{label}: a path under {ProfilesDirectory} is not valid UTF-8.");
        }

        var entries = new Dictionary<String, TreeEntry>(StringComparer.Ordinal);
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // <mode> SP <type> SP <object> TAB <path>
            var tab = record.IndexOf('\t');
            var fields = record[..tab].Split(' ');
            var path = record[(tab + 1)..];
            entries[path] = new TreeEntry(fields[0], fields[2]);
        }

        return entries;
    }

    private static Profile LoadProfile(String checkout, String label, String name, IReadOnlyDictionary<String, TreeEntry> entries)
    {
        var directory = $"{ProfilesDirectory}{name}/";
        var definitionPath = $"{directory}profile.json";
        if (!entries.TryGetValue(definitionPath, out var definitionEntry))
        {
            throw new ToolException($"{label}: {definitionPath} is missing.");
        }

        var blocks = ParseDefinition(label, definitionPath, ReadRegularBlob(checkout, label, definitionPath, definitionEntry))
                     .Select(block => block with { Content = ReadFragment(checkout, label, directory, block, entries) })
                     .ToList();

        var filesDirectory = $"{directory}files/";
        var files = new List<SyncedFile>();
        foreach (var (path, entry) in entries.Where(pair => pair.Key.StartsWith(filesDirectory, StringComparison.Ordinal)))
        {
            var executable = entry.Mode switch
            {
                "100644" => false,
                "100755" => true,
                _ => throw new ToolException($"{label}: {path} has mode {entry.Mode}; only regular files are supported.")
            };
            // Git trees cannot contain `..` segments, but a backslash in a file name becomes a separator on Windows.
            var targetPath = path[filesDirectory.Length..];
            if (!IsRelativePath(targetPath))
            {
                throw new ToolException($"{label}: {path} is not a valid target path; whole files need relative paths inside the repository that are valid on Windows, too.");
            }

            files.Add(new SyncedFile(targetPath, ReadBlob(checkout, entry), executable, name));
        }

        return new Profile(name, blocks, files);
    }

    private static String ReadFragment(String checkout, String label, String directory, BlockDefinition block,
                                       IReadOnlyDictionary<String, TreeEntry> entries)
    {
        // While parsing, Content holds the fragment path from profile.json.
        var path = $"{directory}{block.Content}";
        if (!entries.TryGetValue(path, out var entry))
        {
            throw new ToolException($"{label}: fragment {path} of block {block.Id} is missing.");
        }

        if (!TextFile.TryDecode(ReadRegularBlob(checkout, label, path, entry), out var text))
        {
            throw new ToolException($"{label}: fragment {path} of block {block.Id} is not valid UTF-8.");
        }

        return TextFile.EnsureFinalNewline(text);
    }

    private static List<BlockDefinition> ParseDefinition(String label, String path, Byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(TextFile.WithoutBom(content));
            if ((document.RootElement.ValueKind != JsonValueKind.Object) || !document.RootElement.TryGetProperty("blocks", out var blocksElement)
                                                                         || (blocksElement.ValueKind != JsonValueKind.Array))
            {
                throw Invalid("'blocks' must be an array");
            }

            var blocks = new List<BlockDefinition>();
            foreach (var element in blocksElement.EnumerateArray())
            {
                var id = ReadString(element, "id");
                var file = ReadString(element, "file");
                var source = ReadString(element, "source");
                var position = ReadString(element, "position") switch
                {
                    "start" => BlockPosition.Start,
                    "end" => BlockPosition.End,
                    var other => throw Invalid($"block {id} has position '{other}'; expected 'start' or 'end'")
                };
                if (!BlockIds.IsValid(id))
                {
                    throw Invalid($"'{id}' is not a valid block ID");
                }

                if (!IsRelativePath(file) || !IsRelativePath(source))
                {
                    throw Invalid($"block {id} needs relative 'file' and 'source' paths inside the repository");
                }

                if (blocks.Any(block => (block.Id == id) && (block.File == file)))
                {
                    throw Invalid($"block {id} for {file} is defined more than once");
                }

                blocks.Add(new BlockDefinition(id, file, source, position));
            }

            return blocks;
        }
        catch (JsonException exception)
        {
            throw Invalid(exception.Message);
        }

        String ReadString(JsonElement element, String property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
                                                      && value.ValueKind == JsonValueKind.String
                                                      && value.GetString() is { Length: > 0 } text
                ? text
                : throw Invalid($"every block needs a string '{property}'");

        ToolException Invalid(String reason) => new($"{label}: {path} is invalid: {reason}.");
    }

    // A colon would make a Windows drive path (`C:/…`) or an NTFS stream, which `Path.Combine` resolves outside the target.
    // Device names such as `NUL`, names ending in a dot or space, and the characters Windows forbids in file names do not
    // address a regular file on Windows.
    private static Boolean IsRelativePath(String path) =>
        !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').All(segment => (segment.Length > 0) && !segment.EndsWith('.') && !segment.EndsWith(' ') && !ReservedName.IsMatch(segment)
                                          && !segment.Any(character => Char.IsControl(character) || "<>\"|?*".Contains(character)));

    private static Byte[] ReadBlob(String checkout, TreeEntry entry) => Git.Run(checkout, "cat-file", "blob", entry.Object);

    // The blob of a symbolic link holds its target path, not the content it points to.
    private static Byte[] ReadRegularBlob(String checkout, String label, String path, TreeEntry entry) =>
        entry.Mode is "100644" or "100755"
            ? ReadBlob(checkout, entry)
            : throw new ToolException($"{label}: {path} has mode {entry.Mode}; only regular files are supported.");

    private sealed record TreeEntry(String Mode, String Object);
}

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

        RejectCollidingPaths(source.Label, source.Profiles.Values, files.Keys);

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
    /// files are checked together with the block targets of every profile, because all block targets are scanned.
    /// </summary>
    private static void RejectCollidingPaths(String label, IEnumerable<Profile> profiles, IEnumerable<String> selectedFiles)
    {
        var paths = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in selectedFiles.Concat(profiles.SelectMany(profile => profile.Blocks).Select(block => block.File)).Distinct(StringComparer.Ordinal))
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

internal static class TextFile
{
    private static readonly Byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Byte[] WithoutBom(Byte[] content) => content.AsSpan().StartsWith(Bom) ? content[Bom.Length..] : content;

    /// <summary>
    /// Decodes UTF-8 without a BOM and with LF line endings, which is how the tool compares content.
    /// </summary>
    public static String Decode(Byte[] content) => Encoding.UTF8.GetString(WithoutBom(content)).Replace("\r\n", "\n");

    /// <summary>
    /// Like <see cref="Decode"/>, but fails on invalid UTF-8 instead of replacing it, so local content is never corrupted.
    /// </summary>
    public static Boolean TryDecode(Byte[] content, out String text)
    {
        var decoded = TryDecodeExact(WithoutBom(content), out text);
        text = text.Replace("\r\n", "\n");
        return decoded;
    }

    /// <summary>
    /// Decodes strict UTF-8 without removing a BOM or changing line endings.
    /// </summary>
    public static Boolean TryDecodeExact(Byte[] content, out String text)
    {
        try
        {
            text = StrictUtf8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = String.Empty;
            return false;
        }
    }

    public static String EnsureFinalNewline(String text) => text.EndsWith('\n') ? text : $"{text}\n";

    public static List<String> SplitLines(String text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        return (text.EndsWith('\n') ? text[..^1] : text).Split('\n').ToList();
    }

    public static String JoinLines(IReadOnlyList<String> lines) => lines.Count == 0 ? String.Empty : $"{String.Join("\n", lines)}\n";

    public static String Display(String path) => path.Replace('\\', '/');
}

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
}

internal static class FileModes
{
    private const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    public static Boolean IsExecutable(String path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }

    public static Boolean SupportsExecutableBit => !OperatingSystem.IsWindows();

    public static void SetExecutable(String path, Boolean executable)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path) & ~Execute;
        if (executable)
        {
            // Grant execute wherever read is granted, as `chmod +x` does with the usual umask.
            mode |= UnixFileMode.UserExecute;
            mode |= (mode & UnixFileMode.GroupRead) != 0 ? UnixFileMode.GroupExecute : 0;
            mode |= (mode & UnixFileMode.OtherRead) != 0 ? UnixFileMode.OtherExecute : 0;
        }

        File.SetUnixFileMode(path, mode);
    }
}

internal sealed record ParsedBlock(String Id, Int32 BeginLine, Int32 EndLine);

/// <summary>
/// The managed blocks found in a file, or the problems that make its markers unusable.
/// </summary>
internal sealed record ParsedFile(IReadOnlyList<ParsedBlock> Blocks, IReadOnlyList<String> Problems, Int32? UnclosedFence, Boolean MarkerAfterFence)
{
    /// <summary>
    /// The problems that make the file unusable for <paramref name="managed"/>. A fence left open until the end of the file is valid
    /// Markdown; it is a problem only when it hides a marker or a missing end-position block would be inserted after it.
    /// </summary>
    public IReadOnlyList<String> ProblemsFor(ManagedFile managed)
    {
        var insertsAtEnd = managed.Blocks.Any(block => (block.Position == BlockPosition.End) && Blocks.All(parsed => parsed.Id != block.Id));
        if ((UnclosedFence is not { } fenceLine) || (!MarkerAfterFence && !insertsAtEnd))
        {
            return Problems;
        }

        return [..Problems, $"line {fenceLine + 1} opens a code fence that is never closed, so block markers after it would be ignored"];
    }

    public static ParsedFile Parse(IReadOnlyList<String> lines, MarkerSyntax syntax)
    {
        var blocks = new List<ParsedBlock>();
        var problems = new List<String>();
        var fenced = MarkdownFences.Find(lines, syntax.Fenced, out var unclosedFence);
        (String Id, Int32 Line)? open = null;
        for (var index = 0; index < lines.Count; index++)
        {
            if (fenced[index])
            {
                continue;
            }

            if (syntax.Match(lines[index], "BEGIN") is { } beginId)
            {
                if (open is { } outer)
                {
                    problems.Add($"block {beginId}: BEGIN marker on line {index + 1} is nested in block {outer.Id} from line {outer.Line + 1}");
                    continue;
                }

                if (blocks.FirstOrDefault(block => block.Id == beginId) is { } earlier)
                {
                    problems.Add($"block {beginId}: defined twice, on lines {earlier.BeginLine + 1} and {index + 1}");
                }

                open = (beginId, index);
            }
            else if (syntax.Match(lines[index], "END") is { } endId)
            {
                if (open is not { } current)
                {
                    problems.Add($"block {endId}: END marker on line {index + 1} has no BEGIN marker");
                }
                else if (current.Id != endId)
                {
                    problems.Add($"block {current.Id}: BEGIN marker on line {current.Line + 1} is closed by the END marker of block {endId} on line {index + 1}");
                    open = null;
                }
                else
                {
                    blocks.Add(new ParsedBlock(current.Id, current.Line, index));
                    open = null;
                }
            }
            else if (syntax.LooksLikeMarker(lines[index]))
            {
                problems.Add($"line {index + 1} looks like a block marker but is not one; markers look like {syntax.Begin("<id>")}");
            }
        }

        if (open is { } unclosed)
        {
            problems.Add($"block {unclosed.Id}: BEGIN marker on line {unclosed.Line + 1} has no END marker");
        }

        // Markers after an unclosed fence would be ignored, and sync would insert their blocks again on every run.
        var markerAfterFence = unclosedFence is { } fenceLine
                               && Enumerable.Range(fenceLine + 1, lines.Count - fenceLine - 1).Any(line => syntax.LooksLikeMarker(lines[line]));
        return new ParsedFile(blocks, problems, unclosedFence, markerAfterFence);
    }

    public static String Content(IReadOnlyList<String> lines, ParsedBlock block) =>
        TextFile.JoinLines(lines.Skip(block.BeginLine + 1).Take(block.EndLine - block.BeginLine - 1).ToList());
}

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

            writes.AddRange(PlanFile(target, file));
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

        for (var index = 0; index < writes.Count; index++)
        {
            try
            {
                writes[index].Apply();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var file = TextFile.Display(Path.GetRelativePath(target, writes[index].Path));
                throw new ToolException($"Cannot write {file}: {exception.Message} sync stopped after writing {index} other file(s).");
            }
        }

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

    private static IEnumerable<PendingWrite> PlanFile(String target, SyncedFile file)
    {
        var path = Path.Combine(target, file.Path);
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

        var exists = File.Exists(path);

        if (!TextFile.TryDecode(exists ? File.ReadAllBytes(path) : [], out var text))
        {
            // Without selected blocks, nothing would be written to it, and it cannot contain blocks in this encoding.
            if (managed.Blocks.Count > 0)
            {
                problems.Add($"{TextFile.Display(managed.Path)}: not valid UTF-8");
            }

            return null;
        }

        var lines = TextFile.SplitLines(text);
        var parsed = ParsedFile.Parse(lines, managed.Syntax);
        var fileProblems = parsed.ProblemsFor(managed);
        if (fileProblems.Count > 0)
        {
            problems.AddRange(fileProblems.Select(problem => $"{TextFile.Display(managed.Path)}: {problem}"));
            return null;
        }

        var finalNewline = (text.Length == 0) || text.EndsWith('\n');
        finalNewline |= BlockMerger.Merge(lines, parsed, managed);
        var merged = String.Join("\n", lines) + ((lines.Count > 0) && finalNewline ? "\n" : String.Empty);

        // A file whose blocks are already current is left alone, even if it has a BOM or CRLF line endings.
        if (exists && (merged == text))
        {
            return null;
        }

        return new PendingWrite(path, Encoding.UTF8.GetBytes(merged), Executable: null, exists ? "updated" : "created");
    }

    /// <summary>
    /// A block target that no selected profile uses is only scanned for leftover blocks when it is a regular file inside the repository.
    /// </summary>
    public static Boolean IsUnusedTarget(String target, ManagedFile managed, String path) =>
        (managed.Blocks.Count == 0) && ((TargetPath.Problem(target, managed.Path) is not null) || !File.Exists(path));

    private sealed record PendingWrite(String Path, Byte[]? Content, Boolean? Executable, String Description)
    {
        public void Apply()
        {
            if (Content is not null)
            {
                if (System.IO.Path.GetDirectoryName(Path) is { } directory)
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(Path, Content);
            }

            if (Executable is { } executable)
            {
                FileModes.SetExecutable(Path, executable);
            }
        }
    }
}

internal static class BlockMerger
{
    /// <summary>
    /// Replaces the content of defined blocks, removes unknown blocks and inserts missing ones in <paramref name="lines"/>.
    /// Returns whether the file now needs a final newline: a block ends it, or the line that ended it before a removed block now does.
    /// </summary>
    public static Boolean Merge(List<String> lines, ParsedFile parsed, ManagedFile managed)
    {
        var expected = managed.Blocks.ToDictionary(block => block.Id, StringComparer.Ordinal);
        var removedLastBlock = false;

        // From the last block to the first, so that earlier line numbers stay valid.
        foreach (var block in parsed.Blocks.OrderByDescending(block => block.BeginLine))
        {
            if (expected.TryGetValue(block.Id, out var definition))
            {
                lines.RemoveRange(block.BeginLine + 1, block.EndLine - block.BeginLine - 1);
                lines.InsertRange(block.BeginLine + 1, TextFile.SplitLines(definition.Content));
            }
            else
            {
                removedLastBlock |= block.EndLine == lines.Count - 1;
                Remove(lines, block);
            }
        }

        var present = parsed.Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
        var missing = managed.Blocks.Where(block => !present.Contains(block.Id)).ToList();
        var cleared = (missing.Count > 0) && lines.All(line => line.Trim().Length == 0);
        if (cleared)
        {
            lines.Clear();
        }

        InsertAtStart(lines, managed.Syntax, missing.Where(block => block.Position == BlockPosition.Start).ToList());
        var atEnd = missing.Where(block => block.Position == BlockPosition.End).ToList();
        foreach (var block in atEnd)
        {
            if ((lines.Count > 0) && (lines[^1].Length > 0))
            {
                lines.Add(String.Empty);
            }

            lines.AddRange(Render(managed.Syntax, block));
        }

        return (atEnd.Count > 0) || removedLastBlock || cleared;
    }

    /// <summary>
    /// Removes a block together with the empty line that separated it from local content.
    /// </summary>
    private static void Remove(List<String> lines, ParsedBlock block)
    {
        // A block directly below local text shares no empty line with it; removing the one after it would join two paragraphs.
        var separated = (block.BeginLine == 0) || (lines[block.BeginLine - 1].Length == 0);
        lines.RemoveRange(block.BeginLine, block.EndLine - block.BeginLine + 1);
        if (!separated)
        {
            return;
        }

        if ((block.BeginLine < lines.Count) && (lines[block.BeginLine].Length == 0))
        {
            lines.RemoveAt(block.BeginLine);
        }
        else if ((block.BeginLine > 0) && (block.BeginLine == lines.Count))
        {
            lines.RemoveAt(block.BeginLine - 1);
        }
    }

    private static void InsertAtStart(List<String> lines, MarkerSyntax syntax, IReadOnlyList<ExpectedBlock> blocks)
    {
        var inserted = new List<String>();
        foreach (var block in blocks)
        {
            if (inserted.Count > 0)
            {
                inserted.Add(String.Empty);
            }

            inserted.AddRange(Render(syntax, block));
        }

        if ((inserted.Count > 0) && (lines.Count > 0) && (lines[0].Length > 0))
        {
            inserted.Add(String.Empty);
        }

        lines.InsertRange(0, inserted);
    }

    private static IEnumerable<String> Render(MarkerSyntax syntax, ExpectedBlock block) =>
        [syntax.Begin(block.Id), ..TextFile.SplitLines(block.Content), syntax.End(block.Id)];
}

internal static class CheckCommand
{
    private const String SharedReSharperSettings = ".repo-baseline/ReSharper.DotSettings";
    private const String InjectedLayersPrefix = "/Default/Environment/InjectedLayers/";
    private const String InjectionPath = @"..\.repo-baseline\ReSharper.DotSettings";
    private const String CentralPackageFile = "Directory.Packages.props";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly Regex InjectionKey = new(@"^/Default/Environment/InjectedLayers/FileInjectedLayer/=(?<guid>[^/]+)/RelativePath/@EntryValue\z");

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

    private static void CheckPackageVersions(String target, ExpectedContent expected, List<String> findings)
    {
        var references = expected.Files
                                 .Where(file => file.Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
                                 .SelectMany(file => (ReadItems(file.Content, "PackageReference")
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
            var items = ReadItems(File.ReadAllBytes(centralPath), "PackageVersion");
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

    private static List<String>? ReadItems(Byte[] content, String itemName)
    {
        try
        {
            if (!TextFile.TryDecode(content, out var text))
            {
                return null;
            }

            return XDocument.Parse(text)
                            .Descendants()
                            .Where(element => element.Name.LocalName == itemName)
                            .Select(element => (String?)element.Attribute("Include"))
                            .OfType<String>()
                            .Where(include => include.Length > 0)
                            .ToList();
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
