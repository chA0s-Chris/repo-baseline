// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
