using System.Text;

namespace ShadowForge.GameData.Mods.Merge;

internal enum TextEncodingKind { ShiftJIS, UTF16BigEndian, UTF16LittleEndian }

/// <summary>
/// What a text file carries besides its lines: a leading byte order mark, and whether the last
/// line has no terminator.
/// </summary>
internal readonly record struct TextLayout(bool ByteOrderMark, bool MissingFinalNewline)
{
    /// <summary>
    /// Stock's layout, with each flag taken from the mods where they change it.
    /// </summary>
    public static TextLayout Merge(TextLayout stock, IReadOnlyList<TextLayout> versions) => new(
        versions.Any(v => v.ByteOrderMark != stock.ByteOrderMark) ? !stock.ByteOrderMark : stock.ByteOrderMark,
        versions.Any(v => v.MissingFinalNewline != stock.MissingFinalNewline) ? !stock.MissingFinalNewline : stock.MissingFinalNewline);
}

/// <summary>
/// A game text file split into lines for merging. Each line keeps its own terminator, so an
/// unchanged line is written back byte for byte. The byte order mark is kept apart from the
/// first line, and a last line with no terminator is given the file's usual one, so a mod
/// inserting before the first row or appending after the last is seen as an insertion rather
/// than an edit of that row.
/// </summary>
internal sealed class TextLines
{
    private const char ByteOrderMark = '﻿';

    private static readonly Encoding UTF16BigEndian = new UnicodeEncoding(true, false, true);
    private static readonly Encoding UTF16LittleEndian = new UnicodeEncoding(false, false, true);

    private TextLines(List<string> lines, TextLayout layout)
    {
        Lines = lines;
        Layout = layout;
    }

    public IReadOnlyList<string> Lines { get; }

    public TextLayout Layout { get; }

    /// <summary>
    /// UTF-16 for .u16 tables and for files starting with a UTF-16 byte order mark, otherwise
    /// Shift-JIS read byte for byte (which also carries ASCII and UTF-8 through unchanged).
    /// </summary>
    public static TextEncodingKind DetectEncoding(string fileName, byte[] data)
    {
        if (data is [0xFE, 0xFF, ..]) return TextEncodingKind.UTF16BigEndian;
        if (data is [0xFF, 0xFE, ..]) return TextEncodingKind.UTF16LittleEndian;
        return fileName.EndsWith(".u16", StringComparison.OrdinalIgnoreCase)
            ? TextEncodingKind.UTF16BigEndian
            : TextEncodingKind.ShiftJIS;
    }

    /// <summary>
    /// Null when the bytes are not valid text in that encoding.
    /// </summary>
    public static TextLines? Decode(byte[] data, TextEncodingKind encoding)
    {
        string text;
        if (encoding == TextEncodingKind.ShiftJIS)
        {
            text = EncodingExtensions.DecodeShiftJISRaw(data, 0, data.Length);
        }
        else
        {
            if (data.Length % 2 != 0) return null;
            try { text = UTF16For(encoding).GetString(data); }
            catch (DecoderFallbackException) { return null; }
        }

        bool byteOrderMark = encoding != TextEncodingKind.ShiftJIS && text.StartsWith(ByteOrderMark);
        return Split(byteOrderMark ? text[1..] : text, byteOrderMark);
    }

    /// <summary>
    /// Joins lines back into file bytes in the given layout.
    /// </summary>
    public static byte[] Encode(IReadOnlyList<string> lines, TextLayout layout, TextEncodingKind encoding)
    {
        string text = string.Concat(lines);
        if (layout.MissingFinalNewline)
        {
            if (text.EndsWith("\r\n", StringComparison.Ordinal)) text = text[..^2];
            else if (text.EndsWith('\n')) text = text[..^1];
        }
        if (layout.ByteOrderMark) text = ByteOrderMark + text;
        return encoding == TextEncodingKind.ShiftJIS ? text.EncodeShiftJIS() : UTF16For(encoding).GetBytes(text);
    }

    private static Encoding UTF16For(TextEncodingKind encoding) =>
        encoding == TextEncodingKind.UTF16LittleEndian ? UTF16LittleEndian : UTF16BigEndian;

    private static TextLines Split(string text, bool byteOrderMark)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }
        if (start == text.Length) return new TextLines(lines, new TextLayout(byteOrderMark, false));

        string usual = text.Contains("\r\n", StringComparison.Ordinal) || !text.Contains('\n') ? "\r\n" : "\n";
        lines.Add(text[start..] + usual);
        return new TextLines(lines, new TextLayout(byteOrderMark, true));
    }
}
