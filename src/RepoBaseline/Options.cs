// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Globalization;
using System.Text.RegularExpressions;

internal sealed record Options(String Command, String TargetPath, String? Version, String? Ref, InitOptions? Init)
{
    public const String InitCommand = "init";
    public const String SyncCommand = "sync";
    public const String CheckCommand = "check";

    private const String RefOption = "--ref";
    private static readonly String[] InitValueOptions = ["--name", "--type", "--forge", "--repository-url", "--target-frameworks"];
    private static readonly String[] InitFlags = ["--private", "--force"];

    public static Options Parse(IReadOnlyList<String> args)
    {
        var positional = new List<String>();
        var values = new Dictionary<String, String>(StringComparer.Ordinal);
        var flags = new HashSet<String>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            var name = argument.StartsWith("--", StringComparison.Ordinal) ? argument.Split('=', 2)[0] : argument;
            if ((positional.Count > 0) && (positional[0] != InitCommand) && (InitValueOptions.Contains(name) || InitFlags.Contains(argument)))
            {
                throw new UsageException($"Unknown option '{argument}'.");
            }

            if ((name == RefOption) || InitValueOptions.Contains(name))
            {
                if (values.ContainsKey(name))
                {
                    throw new UsageException($"{name} is given more than once.");
                }

                values[name] = argument == name
                    ? (index + 1 < args.Count ? args[++index] : throw new UsageException($"{name} needs a value."))
                    : argument[(name.Length + 1)..];
            }
            else if (InitFlags.Contains(argument))
            {
                if (!flags.Add(argument))
                {
                    throw new UsageException($"{argument} is given more than once.");
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

        var gitRef = values.GetValueOrDefault(RefOption);
        if ((gitRef is not null) && ((gitRef.Length == 0) || gitRef.StartsWith('-')))
        {
            throw new UsageException($"'{gitRef}' is not a valid Git ref.");
        }

        if (positional.Count == 0)
        {
            throw new UsageException("No command given.");
        }

        var command = positional[0];
        if (command is not (InitCommand or SyncCommand or CheckCommand))
        {
            throw new UsageException($"Unknown command '{command}'.");
        }

        if (command != InitCommand && (InitValueOptions.FirstOrDefault(values.ContainsKey) ?? flags.FirstOrDefault()) is { } initOption)
        {
            throw new UsageException($"Unknown option '{initOption}'.");
        }

        if ((positional.Count < 2) || String.IsNullOrWhiteSpace(positional[1]))
        {
            throw new UsageException($"{command} needs the path of the target repository.");
        }

        var maximum = command == CheckCommand ? 2 : 3;
        if (positional.Count > maximum)
        {
            throw new UsageException($"Unexpected argument '{positional[maximum]}'.");
        }

        var version = positional.Count == 3 ? NormalizeVersion(positional[2]) : null;
        if (command == InitCommand)
        {
            // The manifest needs a version even when the content comes from --ref, so init accepts both.
            return new Options(command, positional[1], version ?? throw new UsageException("init needs the version to record in the manifest."),
                               gitRef, InitOptions.Parse(values, flags));
        }

        if ((version is not null) && (gitRef is not null))
        {
            throw new UsageException("<version> and --ref cannot be combined.");
        }

        return new Options(command, positional[1], version, gitRef, Init: null);
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

/// <summary>
/// The repository-specific inputs of <c>init</c>, from which it derives the profiles and the template values.
/// </summary>
internal sealed record InitOptions(String Name, Boolean Library, String Forge, String RepositoryUrl, Uri Repository,
                                   IReadOnlyList<String> TargetFrameworks, Boolean Private, Boolean Force)
{
    public const String GitHub = "github";
    public const String GitLab = "gitlab";

    // Dot-separated C# identifiers; the name is also used in file names.
    private static readonly Regex NamePattern = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*\z");
    private static readonly Regex FrameworkPattern = new(@"^net(?<major>0|[1-9][0-9]{0,3})\.(?<minor>0|[1-9][0-9]{0,3})\z");

    /// <summary>
    /// The profiles of the combination that <c>docs/layout-dotnet.md</c> prescribes, in manifest order.
    /// </summary>
    public IReadOnlyList<String> Profiles => Library ? ["common", "dotnet", "dotnet-nuget", Forge] : ["common", "dotnet", Forge];

    public static InitOptions Parse(IReadOnlyDictionary<String, String> values, IReadOnlySet<String> flags)
    {
        String[] required = ["--name", "--type", "--forge", "--repository-url", "--target-frameworks"];
        var missing = required.Where(option => !values.ContainsKey(option)).ToList();
        if (missing.Count > 0)
        {
            throw new UsageException($"init needs {String.Join(", ", missing)}.");
        }

        var name = values["--name"];
        if (!NamePattern.IsMatch(name))
        {
            throw new UsageException($"'{name}' is not a valid name; use dot-separated C# identifiers such as Chaos.Example.");
        }

        if (!ReleaseSource.IsRelativePath(name))
        {
            throw new UsageException($"'{name}' is not a valid name; it cannot be used in file names on Windows.");
        }

        var library = values["--type"] switch
        {
            "application" => false,
            "library" => true,
            var other => throw new UsageException($"'{other}' is not a valid --type; use application or library.")
        };

        var forge = values["--forge"] is GitHub or GitLab ? values["--forge"] : throw new UsageException($"'{values["--forge"]}' is not a valid --forge; use github or gitlab.");

        // The URL is written as given into XML and Markdown files, so credentials, a query, a fragment and characters that
        // need escaping there are not accepted, and it must already be in the normalized form.
        var url = values["--repository-url"];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var repository) || (repository.Scheme != Uri.UriSchemeHttps) || (repository.Host.Length == 0)
            || (repository.UserInfo.Length > 0) || (repository.Query.Length > 0) || (repository.Fragment.Length > 0)
            || url.Any(character => Char.IsWhiteSpace(character) || Char.IsControl(character) || "&<>\"'".Contains(character)))
        {
            throw new UsageException($"'{url}' is not a valid --repository-url; use an absolute https URL without credentials, query, fragment, " +
                                     "whitespace or the characters & < > \" '.");
        }

        if (repository.AbsoluteUri != url)
        {
            throw new UsageException($"'{url}' is not a valid --repository-url; write it in its normalized form, {repository.AbsoluteUri}.");
        }

        var onGitHub = String.Equals(repository.Host, "github.com", StringComparison.OrdinalIgnoreCase);
        if ((forge == GitHub) != onGitHub)
        {
            throw new UsageException(forge == GitHub
                                         ? $"--forge github needs a repository URL on github.com, not {repository.Host}."
                                         : "--forge gitlab needs a repository URL on a GitLab host, not github.com.");
        }

        var frameworks = values["--target-frameworks"].Split(';');
        foreach (var framework in frameworks)
        {
            if (!FrameworkPattern.IsMatch(framework))
            {
                throw new UsageException($"'{framework}' is not a valid target framework; use monikers such as net10.0, separated by semicolons.");
            }
        }

        if (frameworks.Distinct(StringComparer.Ordinal).Count() != frameworks.Length)
        {
            throw new UsageException("--target-frameworks lists a framework more than once.");
        }

        return new InitOptions(name, library, forge, url, repository, frameworks, flags.Contains("--private"), flags.Contains("--force"));
    }

    /// <summary>
    /// Orders target framework monikers from the highest version to the lowest.
    /// </summary>
    public static IEnumerable<String> ByVersionDescending(IEnumerable<String> frameworks) =>
        frameworks.Select(framework => (Framework: framework, Match: FrameworkPattern.Match(framework)))
                  .OrderByDescending(entry => Int32.Parse(entry.Match.Groups["major"].Value, CultureInfo.InvariantCulture))
                  .ThenByDescending(entry => Int32.Parse(entry.Match.Groups["minor"].Value, CultureInfo.InvariantCulture))
                  .Select(entry => entry.Framework);
}
