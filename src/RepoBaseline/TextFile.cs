// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace Chaos.RepoBaseline;

using System.Text;

internal static class TextFile
{
    private static readonly Byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Byte[] WithoutBom(Byte[] content) => content.AsSpan().StartsWith(Bom) ? content[Bom.Length..] : content;

    /// <summary>
    /// Decodes UTF-8 without a BOM and with LF line endings, which is how the tool compares content.
    /// </summary>
    public static String Decode(Byte[] content) => Encoding.UTF8.GetString(WithoutBom(content)).Replace("\r\n", "\n");

    /// <summary>
    /// Like <see cref="Decode"/>, but fails on invalid UTF-8 instead of replacing it, so local content is never corrupted.
    /// </summary>
    public static Boolean TryDecode(Byte[] content, out String text)
    {
        var decoded = TryDecodeExact(WithoutBom(content), out text);
        text = text.Replace("\r\n", "\n");
        return decoded;
    }

    /// <summary>
    /// Decodes strict UTF-8 without removing a BOM or changing line endings.
    /// </summary>
    public static Boolean TryDecodeExact(Byte[] content, out String text)
    {
        try
        {
            text = StrictUtf8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = String.Empty;
            return false;
        }
    }

    public static String EnsureFinalNewline(String text) => text.EndsWith('\n') ? text : $"{text}\n";

    public static List<String> SplitLines(String text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        return (text.EndsWith('\n') ? text[..^1] : text).Split('\n').ToList();
    }

    public static String JoinLines(IReadOnlyList<String> lines) => lines.Count == 0 ? String.Empty : $"{String.Join("\n", lines)}\n";

    public static String Display(String path) => path.Replace('\\', '/');
}
