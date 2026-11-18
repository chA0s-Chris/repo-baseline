// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline.Tests;

using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

/// <summary>
/// Builds a <c>profiles.tar.gz</c> in memory, like <c>tool/pack-profiles.sh</c> does.
/// </summary>
internal sealed class ArchiveBuilder
{
    private readonly List<TarEntry> _entries = [];

    public static ArchiveBuilder WithProfile() =>
        new ArchiveBuilder().File("profiles/base/profile.json", "{ \"blocks\": [] }\n")
                            .File("profiles/base/files/docs/GUIDE.md", "Guide.\n")
                            .File("profiles/base/files/tool.sh", "#!/bin/bash\n", executable: true);

    public ArchiveBuilder Release(String json) => File("release.json", json);

    public ArchiveBuilder Release(Int32 format, String version, String? minimumToolVersion = null) =>
        Release(minimumToolVersion is null
                    ? $"{{ \"format\": {format}, \"version\": \"{version}\" }}"
                    : $"{{ \"format\": {format}, \"version\": \"{version}\", \"minimumToolVersion\": \"{minimumToolVersion}\" }}");

    public ArchiveBuilder File(String name, String content, Boolean executable = false)
    {
        var entry = new GnuTarEntry(TarEntryType.RegularFile, name)
        {
            Mode = executable ? (UnixFileMode)0b111_101_101 : (UnixFileMode)0b110_100_100,
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
        };
        _entries.Add(entry);
        return this;
    }

    public ArchiveBuilder Entry(TarEntryType type, String name, String? linkName = null)
    {
        var entry = new GnuTarEntry(type, name);
        if (linkName is not null)
        {
            entry.LinkName = linkName;
        }

        _entries.Add(entry);
        return this;
    }

    public Byte[] Build()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Gnu))
        {
            foreach (var entry in _entries)
            {
                writer.WriteEntry(entry);
            }
        }

        return output.ToArray();
    }
}
