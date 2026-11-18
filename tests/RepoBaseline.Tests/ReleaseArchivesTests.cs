// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline.Tests;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NUnit.Framework;

public class ReleaseArchivesTests
{
    private static readonly Byte[] Archive = Encoding.UTF8.GetBytes("archive bytes");

    private DirectoryInfo _cache;
    private StubHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _cache = Directory.CreateTempSubdirectory("repo-baseline-cache-");
        _handler = new StubHandler();
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
        _cache.Delete(recursive: true);
    }

    [Test]
    public void Get_DownloadsAgainWhenCachedArchiveDoesNotMatch()
    {
        Serve("1.0.0", Archive);
        Archives().Get("1.0.0");
        File.WriteAllText(Path.Combine(_cache.FullName, "1.0.0", "profiles.tar.gz"), "damaged");

        Archives().Get("1.0.0").Should().Equal(Archive);

        _handler.Requests.Should().HaveCount(4);
        File.ReadAllBytes(Path.Combine(_cache.FullName, "1.0.0", "profiles.tar.gz")).Should().Equal(Archive);
    }

    [Test]
    public void Get_DownloadsFromTheReleaseOfTheVersion()
    {
        Serve("1.0.0", Archive);

        Archives().Get("1.0.0").Should().Equal(Archive);

        _handler.Requests.Should().Equal("https://github.com/chA0s-Chris/repo-baseline/releases/download/v1.0.0/profiles.tar.gz.sha256",
                                         "https://github.com/chA0s-Chris/repo-baseline/releases/download/v1.0.0/profiles.tar.gz");
    }

    [Test]
    public void Get_RefusesArchiveThatDoesNotMatchChecksumAndCachesNothing()
    {
        Serve("1.0.0", Archive, checksum: Checksum(Encoding.UTF8.GetBytes("other bytes")));

        var get = () => Archives().Get("1.0.0");

        get.Should().Throw<ToolException>().WithMessage("v1.0.0: profiles.tar.gz does not match profiles.tar.gz.sha256; nothing was cached.");
        Directory.Exists(Path.Combine(_cache.FullName, "1.0.0")).Should().BeFalse();
    }

    [Test]
    public void Get_RefusesInvalidChecksumFile()
    {
        Serve("1.0.0", Archive, checksum: "not a checksum\n");

        var get = () => Archives().Get("1.0.0");

        get.Should().Throw<ToolException>().WithMessage("v1.0.0: profiles.tar.gz.sha256 is not a SHA-256 checksum of profiles.tar.gz *");
    }

    [Test]
    public void Get_ReportsMissingReleaseAndCachesNothing()
    {
        var get = () => Archives().Get("0.4.0");

        get.Should().Throw<ToolException>().WithMessage("v0.4.0 has no profiles.tar.gz.sha256 on GitHub; *--checkout*");
        Directory.EnumerateFileSystemEntries(_cache.FullName).Should().BeEmpty();
    }

    [Test]
    public void Get_ReportsOtherHttpErrors()
    {
        _handler.Status = HttpStatusCode.InternalServerError;

        var get = () => Archives().Get("1.0.0");

        get.Should().Throw<ToolException>().WithMessage("Downloading https://github.com/*/v1.0.0/profiles.tar.gz.sha256 failed: 500 *");
    }

    [Test]
    public void Get_ReturnsVerifiedArchiveAndWarnsWhenCacheCannotBeWritten()
    {
        Serve("1.0.0", Archive);
        var blocked = Path.Combine(_cache.FullName, "blocked");
        File.WriteAllText(blocked, "A file where the cache needs a directory.");
        using var warnings = new StringWriter();

        new ReleaseArchives(blocked, _handler, warnings).Get("1.0.0").Should().Equal(Archive);

        warnings.ToString().Should().StartWith($"warning: cannot cache v1.0.0 in {Path.Combine(blocked, "1.0.0")}: ");
    }

    [Test]
    public void Get_ReusesTheCacheWithoutDownloading()
    {
        Serve("1.0.0", Archive);
        Archives().Get("1.0.0");
        _handler.Requests.Clear();

        Archives().Get("1.0.0").Should().Equal(Archive);

        _handler.Requests.Should().BeEmpty();
        Directory.GetFiles(Path.Combine(_cache.FullName, "1.0.0")).Select(Path.GetFileName).Should().BeEquivalentTo("profiles.tar.gz", "profiles.tar.gz.sha256");
    }

    [TestCase(" *profiles.tar.gz\n")]
    [TestCase("  profiles.tar.gz")]
    public void ParseChecksum_AcceptsTextAndBinaryMode(String suffix)
    {
        var hash = SHA256.HashData(Archive);

        ReleaseArchives.ParseChecksum(Encoding.UTF8.GetBytes(Convert.ToHexStringLower(hash) + suffix)).Should().Equal(hash);
    }

    [TestCase("  other.tar.gz\n")]
    [TestCase("  profiles.tar.gz\nextra  profiles.tar.gz\n")]
    [TestCase(" profiles.tar.gz\n")]
    public void ParseChecksum_RejectsOtherContent(String suffix)
    {
        ReleaseArchives.ParseChecksum(Encoding.UTF8.GetBytes(Convert.ToHexStringLower(SHA256.HashData(Archive)) + suffix)).Should().BeNull();
    }

    private static String Checksum(Byte[] content) => $"{Convert.ToHexStringLower(SHA256.HashData(content))}  profiles.tar.gz\n";

    private ReleaseArchives Archives() => new(_cache.FullName, _handler);

    private void Serve(String version, Byte[] archive, String? checksum = null)
    {
        var prefix = $"https://github.com/chA0s-Chris/repo-baseline/releases/download/v{version}/";
        _handler.Responses[prefix + "profiles.tar.gz"] = archive;
        _handler.Responses[prefix + "profiles.tar.gz.sha256"] = Encoding.UTF8.GetBytes(checksum ?? Checksum(archive));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<String> Requests { get; } = [];

        public Dictionary<String, Byte[]> Responses { get; } = new(StringComparer.Ordinal);

        public HttpStatusCode? Status { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.AbsoluteUri;
            Requests.Add(uri);
            var response = Status is { } status
                ? new HttpResponseMessage(status)
                : Responses.TryGetValue(uri, out var content)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
}

public class CacheDirectoryTests
{
    [Test]
    public void Resolve_FallsBackToHomeForRelativeXdgCacheHome()
    {
        Assume.That(!OperatingSystem.IsWindows());

        CacheDirectory.Resolve(Environment(("XDG_CACHE_HOME", "relative"), ("HOME", "/home/user"))).Should().Be("/home/user/.cache/repo-baseline");
    }

    [Test]
    public void Resolve_PrefersRepoBaselineCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cache");

        CacheDirectory.Resolve(Environment(("REPO_BASELINE_CACHE", directory), ("XDG_CACHE_HOME", "/xdg"))).Should().Be(directory);
    }

    [Test]
    public void Resolve_UsesXdgCacheHome()
    {
        Assume.That(!OperatingSystem.IsWindows());

        CacheDirectory.Resolve(Environment(("XDG_CACHE_HOME", "/xdg"), ("HOME", "/home/user"))).Should().Be("/xdg/repo-baseline");
    }

    private static Func<String, String?> Environment(params (String Name, String Value)[] variables) =>
        name => variables.FirstOrDefault(variable => variable.Name == name).Value;
}
