// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Text.Json;
using System.Text.RegularExpressions;

internal enum BlockPosition
{
    Start,
    End
}

internal sealed record BlockDefinition(String Id, String File, String Content, BlockPosition Position);

internal sealed record SyncedFile(String Path, Byte[] Content, Boolean Executable, String Profile);

internal sealed record Profile(String Name, IReadOnlyList<BlockDefinition> Blocks, IReadOnlyList<SyncedFile> Files);

/// <summary>
/// A created-once file of a profile, before its placeholders are filled in.
/// </summary>
internal sealed record TemplateFile(String Path, Byte[] Content, Boolean Executable, String Profile);

/// <summary>
/// A file of a release below <c>profiles/</c>: its Git file mode and its content, which is read only when needed.
/// The archive reader reports regular files with the modes that Git uses for them.
/// </summary>
internal sealed record SourceEntry(String Mode, Func<Byte[]> Read)
{
    public const String RegularMode = "100644";
    public const String ExecutableMode = "100755";
}

/// <summary>
/// The profiles of one release, read from the Git objects of a checkout (<see cref="GitSource"/>) or from a release
/// archive (<see cref="ArchiveSource"/>). Both give the same entries, so the profiles are validated the same way.
/// </summary>
internal sealed class ReleaseSource
{
    public const String ProfilesDirectory = "profiles/";
    private static readonly Regex ReservedName = new(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(\..*)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<String, SourceEntry> _entries;

    private ReleaseSource(String label, IReadOnlyDictionary<String, Profile> profiles, IReadOnlyDictionary<String, SourceEntry> entries)
    {
        Label = label;
        Profiles = profiles;
        _entries = entries;
    }

    public String Label { get; }

    public IReadOnlyDictionary<String, Profile> Profiles { get; }

    /// <summary>
    /// Reads the profiles from the entries below <c>profiles/</c>, keyed by their full paths.
    /// </summary>
    public static ReleaseSource Create(String label, IReadOnlyDictionary<String, SourceEntry> entries)
    {
        var names = entries.Keys
                           .Select(path => path[ProfilesDirectory.Length..])
                           .Where(path => path.Contains('/'))
                           .Select(path => path.Split('/')[0])
                           .Distinct()
                           .Order(StringComparer.Ordinal);
        var profiles = new Dictionary<String, Profile>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            profiles[name] = LoadProfile(label, name, entries);
        }

        return new ReleaseSource(label, profiles, entries);
    }

    /// <summary>
    /// Reads the templates of a profile. Only <c>init</c> needs them, so <c>sync</c> and <c>check</c> do not read the blobs.
    /// Their target paths may contain placeholders and are validated after they are filled in.
    /// </summary>
    public IReadOnlyList<TemplateFile> ReadTemplates(String profile)
    {
        var directory = $"{ProfilesDirectory}{profile}/templates/";
        return _entries.Where(pair => pair.Key.StartsWith(directory, StringComparison.Ordinal))
                       .Select(pair => new TemplateFile(pair.Key[directory.Length..], pair.Value.Read(),
                                                        IsExecutable(Label, pair.Key, pair.Value), profile))
                       .ToList();
    }

    private static Profile LoadProfile(String label, String name, IReadOnlyDictionary<String, SourceEntry> entries)
    {
        var directory = $"{ProfilesDirectory}{name}/";
        var definitionPath = $"{directory}profile.json";
        if (!entries.TryGetValue(definitionPath, out var definitionEntry))
        {
            throw new ToolException($"{label}: {definitionPath} is missing.");
        }

        var blocks = ParseDefinition(label, definitionPath, ReadRegular(label, definitionPath, definitionEntry))
                     .Select(block => block with { Content = ReadFragment(label, directory, block, entries) })
                     .ToList();

        var filesDirectory = $"{directory}files/";
        var files = new List<SyncedFile>();
        foreach (var (path, entry) in entries.Where(pair => pair.Key.StartsWith(filesDirectory, StringComparison.Ordinal)))
        {
            var executable = IsExecutable(label, path, entry);
            // Neither reader accepts `..` segments, but a backslash in a file name becomes a separator on Windows.
            var targetPath = path[filesDirectory.Length..];
            if (!IsRelativePath(targetPath))
            {
                throw new ToolException($"{label}: {path} is not a valid target path; whole files need relative paths inside the repository that are valid on Windows, too.");
            }

            files.Add(new SyncedFile(targetPath, entry.Read(), executable, name));
        }

        return new Profile(name, blocks, files);
    }

    private static String ReadFragment(String label, String directory, BlockDefinition block, IReadOnlyDictionary<String, SourceEntry> entries)
    {
        // While parsing, Content holds the fragment path from profile.json.
        var path = $"{directory}{block.Content}";
        if (!entries.TryGetValue(path, out var entry))
        {
            throw new ToolException($"{label}: fragment {path} of block {block.Id} is missing.");
        }

        if (!TextFile.TryDecode(ReadRegular(label, path, entry), out var text))
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
    public static Boolean IsRelativePath(String path) =>
        !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').All(segment => (segment.Length > 0) && !segment.EndsWith('.') && !segment.EndsWith(' ') && !ReservedName.IsMatch(segment)
                                          && !segment.Any(character => Char.IsControl(character) || "<>\"|?*".Contains(character)));

    private static Boolean IsExecutable(String label, String path, SourceEntry entry) =>
        entry.Mode switch
        {
            SourceEntry.RegularMode => false,
            SourceEntry.ExecutableMode => true,
            _ => throw new ToolException($"{label}: {path} has mode {entry.Mode}; only regular files are supported.")
        };

    // The blob of a symbolic link holds its target path, not the content it points to.
    private static Byte[] ReadRegular(String label, String path, SourceEntry entry) =>
        entry.Mode is SourceEntry.RegularMode or SourceEntry.ExecutableMode
            ? entry.Read()
            : throw new ToolException($"{label}: {path} has mode {entry.Mode}; only regular files are supported.");
}
