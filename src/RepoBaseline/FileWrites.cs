// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

internal static class FileModes
{
    private const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    public static Boolean IsExecutable(String path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }

    public static Boolean SupportsExecutableBit => !OperatingSystem.IsWindows();

    public static void SetExecutable(String path, Boolean executable)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path) & ~Execute;
        if (executable)
        {
            // Grant execute wherever read is granted, as `chmod +x` does with the usual umask.
            mode |= UnixFileMode.UserExecute;
            mode |= (mode & UnixFileMode.GroupRead) != 0 ? UnixFileMode.GroupExecute : 0;
            mode |= (mode & UnixFileMode.OtherRead) != 0 ? UnixFileMode.OtherExecute : 0;
        }

        File.SetUnixFileMode(path, mode);
    }
}

/// <summary>
/// A file that <c>sync</c> or <c>init</c> writes once every planned write is known to be possible.
/// A secret is created only readable and writable by its owner, and never replaces an existing file.
/// A file whose name differs only in case is renamed to <paramref name="Path"/> before the content is written; writing first
/// and deleting it afterwards would delete the written file on a case-insensitive file system.
/// </summary>
internal sealed record PendingWrite(
    String Path,
    Byte[]? Content,
    Boolean? Executable,
    String Description,
    Boolean Secret = false,
    String? RenamedFrom = null)
{
    public void Apply()
    {
        if (RenamedFrom is not null)
        {
            File.Move(RenamedFrom, Path);
        }

        if (Content is not null)
        {
            if (System.IO.Path.GetDirectoryName(Path) is { } directory)
            {
                Directory.CreateDirectory(directory);
            }

            if (Secret)
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows())
                {
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }

                using var stream = new FileStream(Path, options);
                stream.Write(Content);
            }
            else
            {
                File.WriteAllBytes(Path, Content);
            }
        }

        if (Executable is { } executable)
        {
            FileModes.SetExecutable(Path, executable);
        }
    }

    /// <summary>
    /// Applies the writes in order. A failed write stops the command and reports how many files it wrote before.
    /// </summary>
    public static void ApplyAll(String target, IReadOnlyList<PendingWrite> writes, String command)
    {
        for (var index = 0; index < writes.Count; index++)
        {
            try
            {
                writes[index].Apply();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var file = TextFile.Display(System.IO.Path.GetRelativePath(target, writes[index].Path));
                throw new ToolException($"Cannot write {file}: {exception.Message} {command} stopped after writing {index} other file(s).");
            }
        }
    }
}
