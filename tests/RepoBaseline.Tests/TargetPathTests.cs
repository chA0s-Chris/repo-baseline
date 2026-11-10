// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline.Tests;

using FluentAssertions;
using NUnit.Framework;

public class TargetPathTests
{
    private DirectoryInfo _target;

    [SetUp]
    public void SetUp()
    {
        _target = Directory.CreateTempSubdirectory("repo-baseline-tests-");
    }

    [TearDown]
    public void TearDown()
    {
        _target.Delete(recursive: true);
    }

    [Test]
    public void CaseVariants_IsEmptyWhenDirectoryIsMissing()
    {
        TargetPath.CaseVariants(_target.FullName, "docs/GUIDE.md").Should().BeEmpty();
    }

    [Test]
    public void CaseVariants_IsEmptyWhenFileHasExactlyTheName()
    {
        Touch("nuget.config");
        Touch("NuGet.config");

        TargetPath.CaseVariants(_target.FullName, "nuget.config").Should().BeEmpty();
    }

    [Test]
    public void CaseVariants_ListsEveryVariantInOrdinalOrder()
    {
        Touch("docs/guide.md");
        Touch("docs/Guide.md");
        Touch("docs/other.md");

        TargetPath.CaseVariants(_target.FullName, "docs/GUIDE.md").Should().Equal("docs/Guide.md", "docs/guide.md");
    }

    [Test]
    public void CaseVariants_ReturnsFileWhoseNameDiffersOnlyInCase()
    {
        Touch("NuGet.config");

        TargetPath.CaseVariants(_target.FullName, "nuget.config").Should().Equal("NuGet.config");
    }

    private void Touch(String relativePath)
    {
        var path = Path.Combine(_target.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, String.Empty);
    }
}
