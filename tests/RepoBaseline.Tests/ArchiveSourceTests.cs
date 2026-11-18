// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline.Tests;

using System.Formats.Tar;
using FluentAssertions;
using NUnit.Framework;

public class ArchiveSourceTests
{
    private static readonly SemanticVersion Tool = Version("0.2.0");

    [Test]
    public void Load_AcceptsFormatOneWithoutMinimum()
    {
        var archive = ArchiveBuilder.WithProfile().Release(1, "0.5.0").Build();

        var source = ArchiveSource.Load(archive, "0.5.0", Tool);

        source.Label.Should().Be("v0.5.0");
        source.Profiles.Should().ContainKey("base");
    }

    [TestCase("0.1.0")]
    [TestCase("0.2.0")]
    [TestCase("0.2.0-rc.1")]
    public void Load_AcceptsMinimumUpToToolVersion(String minimum)
    {
        var archive = ArchiveBuilder.WithProfile().Release(2, "1.0.0", minimum).Build();

        ArchiveSource.Load(archive, "1.0.0", Tool).Profiles.Should().ContainKey("base");
    }

    [Test]
    public void Load_IgnoresFilesOutsideProfilesAndDirectories()
    {
        var archive = ArchiveBuilder.WithProfile().Release(2, "1.0.0", "0.1.0").File("LICENSE", "MIT\n")
                                    .Entry(TarEntryType.Directory, "profiles/base/").Build();

        ArchiveSource.Load(archive, "1.0.0", Tool).Profiles.Keys.Should().Equal("base");
    }

    [Test]
    public void Load_ReadsFilesWithTheirExecutableBit()
    {
        var archive = ArchiveBuilder.WithProfile().Release(2, "1.0.0", "0.1.0").Build();

        var files = ArchiveSource.Load(archive, "1.0.0", Tool).Profiles["base"].Files;

        files.Select(file => (file.Path, file.Executable, Text: System.Text.Encoding.UTF8.GetString(file.Content)))
             .Should().BeEquivalentTo([("docs/GUIDE.md", false, "Guide.\n"), ("tool.sh", true, "#!/bin/bash\n")]);
    }

    [Test]
    public void Load_RefusesArchiveForAnotherVersion()
    {
        var archive = ArchiveBuilder.WithProfile().Release(2, "1.0.1", "0.1.0").Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0: the archive is for version '1.0.1', not 1.0.0.");
    }

    [Test]
    public void Load_RefusesArchiveWithoutRelease()
    {
        var archive = ArchiveBuilder.WithProfile().Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0: the archive has no release.json.");
    }

    [Test]
    public void Load_RefusesDuplicateEntries()
    {
        var archive = ArchiveBuilder.WithProfile().Release(2, "1.0.0", "0.1.0").File("profiles/base/files/tool.sh", "Again.\n").Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0: the archive contains profiles/base/files/tool.sh more than once.");
    }

    [Test]
    public void Load_RefusesFormatTwoWithoutMinimum()
    {
        var archive = ArchiveBuilder.WithProfile().Release(2, "1.0.0").Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0: release.json is invalid: 'minimumToolVersion' must be a string.");
    }

    [Test]
    public void Load_RefusesInvalidData()
    {
        Load([1, 2, 3], "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0: profiles.tar.gz is not a valid archive: *");
    }

    [TestCase("profiles/base/../../outside.txt")]
    [TestCase("profiles/base//GUIDE.md")]
    [TestCase("profiles/./base/GUIDE.md")]
    public void Load_RefusesInvalidPaths(String name)
    {
        var archive = ArchiveBuilder.WithProfile().Release(2, "1.0.0", "0.1.0").File(name, "Text.\n").Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage($"v1.0.0: the archive contains the invalid path {name}.");
    }

    [Test]
    public void Load_RefusesMinimumAboveToolVersion()
    {
        var archive = ArchiveBuilder.WithProfile().Release(2, "1.0.0", "0.10.0").Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0 needs repo-baseline 0.10.0 or later; this is 0.2.0. *");
    }

    [Test]
    public void Load_RefusesSymbolicLinkUnderProfiles()
    {
        var archive = ArchiveBuilder.WithProfile().Release(2, "1.0.0", "0.1.0").Entry(TarEntryType.SymbolicLink, "profiles/base/files/link.md", "tool.sh")
                                    .Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0: profiles/base/files/link.md has mode 120000; only regular files are supported.");
    }

    [Test]
    public void Load_RefusesUnknownFormatAndNamesRequiredVersion()
    {
        var archive = ArchiveBuilder.WithProfile().Release(3, "1.0.0", "0.3.0").Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0 needs repo-baseline 0.3.0 or later; this is 0.2.0. Update the tool.");
    }

    [Test]
    public void Load_RefusesUnknownFormatWithMinimumTheToolMeets()
    {
        var archive = ArchiveBuilder.WithProfile().Release(3, "1.0.0", "0.1.0").Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0 has release format 3, which repo-baseline 0.2.0 cannot read. Update the tool.");
    }

    [Test]
    public void Load_RefusesUnknownFormatWithoutMinimum()
    {
        var archive = ArchiveBuilder.WithProfile().Release(3, "1.0.0").Build();

        Load(archive, "1.0.0").Should().Throw<ToolException>().WithMessage("v1.0.0 has release format 3, which repo-baseline 0.2.0 cannot read. Update the tool.");
    }

    private static Action Load(Byte[] archive, String version) => () => ArchiveSource.Load(archive, version, Tool);

    private static SemanticVersion Version(String text)
    {
        SemanticVersion.TryParse(text, out var version).Should().BeTrue();
        return version;
    }
}
