// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

internal static class ExitCodes
{
    public const Int32 Success = 0;
    public const Int32 Findings = 1;
    public const Int32 Error = 2;
}

/// <summary>
/// A usage, manifest or source error. The tool prints every message and exits with code 2.
/// </summary>
internal class ToolException : Exception
{
    public ToolException(String message) : this([message]) { }

    public ToolException(IReadOnlyList<String> messages) : base(String.Join(Environment.NewLine, messages))
    {
        Messages = messages;
    }

    public IReadOnlyList<String> Messages { get; }
}

public static class Cli
{
    private const String Usage = """
        Usage:
          dotnet run tool/sync.cs -- init  <path> <version> --name <Name> --type application|library
                                           --forge github|gitlab --repository-url <url>
                                           --target-frameworks <tfm>[;<tfm>...] [--private] [--force]
                                           [--ref <git-ref>]
          dotnet run tool/sync.cs -- sync  <path> [<version>] [--ref <git-ref>]
          dotnet run tool/sync.cs -- check <path> [--ref <git-ref>]

        init   sets up a repository: writes the manifest for release <version>, the synced content and
               the created-once files from the templates, and a strong-name key for libraries.
               It skips existing files and lists them; --force overwrites them, except a key.
               --private leaves out the LICENSE.
        sync   writes the synced content of the release pinned in <path>/.repo-baseline.json.
               With <version>, it syncs that release and records it in the manifest.
        check  reports synced content that differs from the pinned release.
        --ref  reads the profile content of any Git ref of this repo-baseline checkout instead.
               init still records <version> in the manifest.

        Exit codes: 0 success, 1 check findings, 2 usage, manifest or source errors.
        """;

    public static Int32 Run(String[] args)
    {
        try
        {
            if (args.Any(argument => argument is "-h" or "--help"))
            {
                Console.WriteLine(Usage);
                return ExitCodes.Success;
            }

            var options = Options.Parse(args);
            var target = ResolveTarget(options.TargetPath);
            if (options.Init is { } init)
            {
                var version = options.Version ?? throw new UsageException("init needs the version to record in the manifest.");
                return InitCommand.Run(target, version, init, LoadSource(options.Ref, version));
            }

            var manifest = Manifest.Load(target);
            var source = LoadSource(options.Ref, options.Version ?? manifest.Version);
            var expected = ExpectedContent.Compose(source, manifest.Profiles);

            return options.Command == Options.SyncCommand
                ? SyncCommand.Run(target, manifest, options.Version, expected)
                : CheckCommand.Run(target, expected);
        }
        catch (ToolException exception)
        {
            foreach (var message in exception.Messages)
            {
                Console.Error.WriteLine($"error: {message}");
            }

            if (exception is UsageException)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(Usage);
            }

            return ExitCodes.Error;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return ExitCodes.Error;
        }
    }

    private static ReleaseSource LoadSource(String? gitRef, String version)
    {
        var checkout = BaselineCheckout.Locate();
        return gitRef is not null
            ? ReleaseSource.Load(checkout, gitRef, label: gitRef, kind: "ref")
            : ReleaseSource.Load(checkout, $"refs/tags/v{version}", label: $"v{version}", kind: "tag");
    }

    private static String ResolveTarget(String path)
    {
        var target = Path.GetFullPath(path);
        if (!Directory.Exists(target))
        {
            throw new ToolException($"Target repository {target} does not exist.");
        }

        return target;
    }
}

internal sealed class UsageException : ToolException
{
    public UsageException(String message) : base(message) { }
}
