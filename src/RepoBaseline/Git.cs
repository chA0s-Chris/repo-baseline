// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Diagnostics;
using System.Text;

/// <summary>
/// The <c>repo-baseline</c> checkout that contains this tool and the profile content.
/// </summary>
internal static class BaselineCheckout
{
    public static String Locate()
    {
        var entryPoint = AppContext.GetData("EntryPointFilePath") as String
                         ?? throw new ToolException("Cannot find the location of sync.cs. Run the tool with `dotnet run tool/sync.cs`.");
        var toolDirectory = Path.GetDirectoryName(Path.GetFullPath(entryPoint))
                            ?? throw new ToolException($"Cannot find the directory of {entryPoint}.");
        if (!Git.TryRun(toolDirectory, out var output, out var error, "rev-parse", "--show-toplevel"))
        {
            throw new ToolException($"{toolDirectory} is not inside a usable Git checkout of repo-baseline: {error.Trim()}");
        }

        return Encoding.UTF8.GetString(output).Trim();
    }
}

internal static class Git
{
    // Inherited from a Git hook or a wrapper, these would override `-C` and point git at another repository.
    private static readonly String[] RepositoryVariables =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE"
    ];

    public static Byte[] Run(String directory, params String[] arguments)
    {
        if (!TryRun(directory, out var output, out var error, arguments))
        {
            throw new ToolException($"git {String.Join(' ', arguments)} failed in {directory}: {error.Trim()}");
        }

        return output;
    }

    public static Boolean TryRun(String directory, out Byte[] output, out String error, params String[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var variable in RepositoryVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(directory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new ToolException("Cannot start git.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ToolException($"Cannot start git: {exception.Message}");
        }

        using (process)
        {
            var errorTask = process.StandardError.ReadToEndAsync();
            using var buffer = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(buffer);
            process.WaitForExit();
            output = buffer.ToArray();
            error = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
    }
}
