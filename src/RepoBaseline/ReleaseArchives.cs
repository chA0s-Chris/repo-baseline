// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Net;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Downloads the <c>profiles.tar.gz</c> of a release from GitHub, verifies it against its <c>profiles.tar.gz.sha256</c>, and
/// caches the verified archive per version, so that later runs do not download it again.
/// </summary>
internal sealed class ReleaseArchives
{
    public const String ArchiveName = "profiles.tar.gz";
    public const String ChecksumName = "profiles.tar.gz.sha256";

    private static readonly Uri GitHubReleases = new("https://github.com/chA0s-Chris/repo-baseline/releases/download/");

    private readonly Uri _baseUri;
    private readonly String _cacheDirectory;
    private readonly HttpClient _client;
    private readonly TextWriter _warnings;

    public ReleaseArchives(String cacheDirectory, HttpMessageHandler handler, TextWriter? warnings = null, Uri? baseUri = null)
    {
        _cacheDirectory = cacheDirectory;
        _warnings = warnings ?? Console.Error;
        _baseUri = baseUri ?? GitHubReleases;
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"repo-baseline/{ToolVersion.Current}");
    }

    public static ReleaseArchives CreateDefault() => new(CacheDirectory.Resolve(Environment.GetEnvironmentVariable), new SocketsHttpHandler());

    /// <summary>
    /// Returns the verified archive of <paramref name="version"/>, from the cache or downloaded.
    /// </summary>
    public Byte[] Get(String version)
    {
        var directory = Path.Combine(_cacheDirectory, version);
        if (TryReadCached(directory) is { } cached)
        {
            return cached;
        }

        var checksumFile = Download(version, ChecksumName);
        var expected = ParseChecksum(checksumFile) ?? throw new ToolException($"v{version}: {ChecksumName} is not a SHA-256 checksum of {ArchiveName} in the format of sha256sum.");
        var archive = Download(version, ArchiveName);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(archive), expected))
        {
            throw new ToolException($"v{version}: {ArchiveName} does not match {ChecksumName}; nothing was cached.");
        }

        // The archive is verified, so a cache that cannot be written only costs a download on the next run.
        try
        {
            Store(directory, ArchiveName, archive);
            Store(directory, ChecksumName, checksumFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _warnings.WriteLine($"warning: cannot cache v{version} in {directory}: {exception.Message}");
        }

        return archive;
    }

    /// <summary>
    /// Parses a checksum file in the format of <c>sha256sum</c> that has exactly one line, for <c>profiles.tar.gz</c>.
    /// </summary>
    public static Byte[]? ParseChecksum(Byte[] content)
    {
        var text = Encoding.UTF8.GetString(content).TrimEnd('\n');
        if (text.Contains('\n') || (text.Length < 66))
        {
            return null;
        }

        // `<hash>  <name>` in text mode, `<hash> *<name>` in binary mode.
        var hash = text[..64];
        var separator = text[64..66];
        var name = text[66..];
        if ((separator is not ("  " or " *")) || (name != ArchiveName) || !hash.All(Char.IsAsciiHexDigitLower))
        {
            return null;
        }

        return Convert.FromHexString(hash);
    }

    // A cached archive is verified on every use: an interrupted copy or a damaged disk must not reach a repository.
    private static Byte[]? TryReadCached(String directory)
    {
        var archivePath = Path.Combine(directory, ArchiveName);
        var checksumPath = Path.Combine(directory, ChecksumName);
        if (!File.Exists(archivePath) || !File.Exists(checksumPath))
        {
            return null;
        }

        var expected = ParseChecksum(File.ReadAllBytes(checksumPath));
        var archive = File.ReadAllBytes(archivePath);
        return (expected is not null) && CryptographicOperations.FixedTimeEquals(SHA256.HashData(archive), expected) ? archive : null;
    }

    // Written to a temporary file and renamed, so that a concurrent run never reads a partial file.
    private static void Store(String directory, String name, Byte[] content)
    {
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"{name}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, Path.Combine(directory, name), overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private Byte[] Download(String version, String asset)
    {
        var uri = new Uri(_baseUri, $"v{Uri.EscapeDataString(version)}/{asset}");
        try
        {
            using var response = _client.GetAsync(uri).GetAwaiter().GetResult();
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ToolException($"v{version} has no {asset} on GitHub; releases up to v0.4.0 have no profile archive. " +
                                        "Use --checkout to read the profiles from a repo-baseline checkout.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ToolException($"Downloading {uri} failed: {(Int32)response.StatusCode} {response.ReasonPhrase}.");
            }

            return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }
        catch (HttpRequestException exception)
        {
            throw new ToolException($"Downloading {uri} failed: {exception.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new ToolException($"Downloading {uri} timed out.");
        }
    }
}

/// <summary>
/// The directory of the archive cache: <c>REPO_BASELINE_CACHE</c>, or the user's cache directory of the platform.
/// </summary>
internal static class CacheDirectory
{
    public const String Variable = "REPO_BASELINE_CACHE";

    public static String Resolve(Func<String, String?> environment)
    {
        if (environment(Variable) is { Length: > 0 } configured)
        {
            return Path.GetFullPath(configured);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "repo-baseline", "cache");
        }

        // The XDG specification ignores a relative XDG_CACHE_HOME.
        if (environment("XDG_CACHE_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg))
        {
            return Path.Combine(xdg, "repo-baseline");
        }

        return environment("HOME") is { Length: > 0 } home
            ? Path.Combine(home, ".cache", "repo-baseline")
            : throw new ToolException($"Cannot find a cache directory: set {Variable} or HOME.");
    }
}
