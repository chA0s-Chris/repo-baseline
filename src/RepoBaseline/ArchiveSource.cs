// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

/// <summary>
/// Reads a release from its verified <c>profiles.tar.gz</c>, in memory and without extracting it.
/// </summary>
internal static class ArchiveSource
{
    /// <summary>
    /// The highest <c>release.json</c> format this tool reads. Format 1 has no minimum tool version; format 2 adds it.
    /// </summary>
    public const Int32 LatestFormat = 2;

    private const String ReleaseFile = "release.json";

    public static ReleaseSource Load(Byte[] archive, String version, SemanticVersion toolVersion)
    {
        var label = $"v{version}";
        var entries = new Dictionary<String, SourceEntry>(StringComparer.Ordinal);
        Byte[]? release = null;
        try
        {
            using var gzip = new GZipStream(new MemoryStream(archive, writable: false), CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            while (reader.GetNextEntry(copyData: true) is { } entry)
            {
                var name = entry.Name;
                if (name == ReleaseFile)
                {
                    release = release is null ? ReadData(entry) : throw new ToolException($"{label}: the archive contains {ReleaseFile} more than once.");
                    continue;
                }

                if (!name.StartsWith(ReleaseSource.ProfilesDirectory, StringComparison.Ordinal) || (entry.EntryType == TarEntryType.Directory))
                {
                    continue;
                }

                // Git trees cannot hold such names; an archive can. U+FFFD stands for bytes that are not valid UTF-8.
                if (name.Split('/').Any(segment => segment is "" or "." or "..") || name.Contains('�'))
                {
                    throw new ToolException($"{label}: the archive contains the invalid path {TextFile.Display(name)}.");
                }

                var content = ReadData(entry);
                if (!entries.TryAdd(name, new SourceEntry(ModeOf(entry), () => content)))
                {
                    throw new ToolException($"{label}: the archive contains {name} more than once.");
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or EndOfStreamException)
        {
            throw new ToolException($"{label}: profiles.tar.gz is not a valid archive: {exception.Message}");
        }

        CheckRelease(label, version, release ?? throw new ToolException($"{label}: the archive has no {ReleaseFile}."), toolVersion);
        return ReleaseSource.Create(label, entries);
    }

    /// <summary>
    /// Refuses a release whose archive this tool cannot read: an unknown format, a newer minimum tool version, or another version.
    /// </summary>
    private static void CheckRelease(String label, String version, Byte[] content, SemanticVersion toolVersion)
    {
        Int32 format;
        String releaseVersion;
        String? minimum;
        try
        {
            using var document = JsonDocument.Parse(TextFile.WithoutBom(content));
            var root = document.RootElement;
            if ((root.ValueKind != JsonValueKind.Object) || !root.TryGetProperty("format", out var formatElement) || !formatElement.TryGetInt32(out format)
                || (format < 1))
            {
                throw Invalid("'format' must be a positive integer");
            }

            releaseVersion = root.TryGetProperty("version", out var versionElement) && (versionElement.ValueKind == JsonValueKind.String)
                ? versionElement.GetString() ?? String.Empty
                : throw Invalid("'version' must be a string");
            minimum = root.TryGetProperty("minimumToolVersion", out var minimumElement) && (minimumElement.ValueKind == JsonValueKind.String)
                ? minimumElement.GetString()
                : null;
        }
        catch (JsonException exception)
        {
            throw Invalid(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw Invalid(exception.Message);
        }

        SemanticVersion? required = null;
        if ((minimum is not null) && !SemanticVersion.TryParse(minimum, out required))
        {
            throw Invalid($"'{minimum}' is not a valid minimumToolVersion");
        }

        if (format > LatestFormat)
        {
            // The minimum names the tool to update to only when this tool is older; otherwise the release is inconsistent.
            throw new ToolException((required is not null) && (toolVersion.CompareTo(required) < 0)
                                        ? $"{label} needs repo-baseline {required} or later; this is {toolVersion}. Update the tool."
                                        : $"{label} has release format {format}, which repo-baseline {toolVersion} cannot read. Update the tool.");
        }

        if ((format >= 2) && (required is null))
        {
            throw Invalid("'minimumToolVersion' must be a string");
        }

        if ((required is not null) && (toolVersion.CompareTo(required) < 0))
        {
            throw new ToolException($"{label} needs repo-baseline {required} or later; this is {toolVersion}. Update the tool, " +
                                    "or use --checkout to read the profiles from a repo-baseline checkout.");
        }

        if (releaseVersion != version)
        {
            throw new ToolException($"{label}: the archive is for version '{releaseVersion}', not {version}.");
        }

        ToolException Invalid(String reason) => new($"{label}: {ReleaseFile} is invalid: {reason}.");
    }

    private static String ModeOf(TarEntry entry) =>
        entry.EntryType switch
        {
            TarEntryType.RegularFile or TarEntryType.V7RegularFile =>
                (entry.Mode & UnixFileMode.UserExecute) != 0 ? SourceEntry.ExecutableMode : SourceEntry.RegularMode,
            TarEntryType.SymbolicLink => "120000",
            var other => $"{other}"
        };

    private static Byte[] ReadData(TarEntry entry)
    {
        if (entry.DataStream is null)
        {
            return [];
        }

        using var buffer = new MemoryStream();
        entry.DataStream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
