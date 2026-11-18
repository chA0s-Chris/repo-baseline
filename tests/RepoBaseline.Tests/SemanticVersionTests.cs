// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline.Tests;

using FluentAssertions;
using NUnit.Framework;

public class SemanticVersionTests
{
    [TestCase("1.0.0", "1.0.0", 0)]
    [TestCase("1.0.0+abc", "1.0.0+def", 0)]
    [TestCase("1.0.0", "2.0.0", -1)]
    [TestCase("2.0.0", "10.0.0", -1)]
    [TestCase("1.9.0", "1.10.0", -1)]
    [TestCase("1.0.9", "1.0.10", -1)]
    [TestCase("1.0.0-alpha", "1.0.0", -1)]
    [TestCase("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [TestCase("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [TestCase("1.0.0-alpha.beta", "1.0.0-beta", -1)]
    [TestCase("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [TestCase("1.0.0-beta.11", "1.0.0-rc.1", -1)]
    [TestCase("0.0.0-dev", "0.1.0", -1)]
    public void CompareTo_FollowsSemVerPrecedence(String left, String right, Int32 expected)
    {
        Parse(left).CompareTo(Parse(right)).Should().Be(expected);
        Parse(right).CompareTo(Parse(left)).Should().Be(-expected);
    }

    [Test]
    public void ToString_LeavesOutBuildMetadata()
    {
        Parse("1.2.3-rc.1+0123abcd").ToString().Should().Be("1.2.3-rc.1");
    }

    [TestCase("1.0")]
    [TestCase("01.0.0")]
    [TestCase("1.0.0-01")]
    [TestCase("1.0.0-")]
    [TestCase("v1.0.0")]
    [TestCase("1.0.0\n")]
    public void TryParse_RejectsInvalidVersions(String text)
    {
        SemanticVersion.TryParse(text, out _).Should().BeFalse();
    }

    private static SemanticVersion Parse(String text)
    {
        SemanticVersion.TryParse(text, out var version).Should().BeTrue();
        return version;
    }
}
