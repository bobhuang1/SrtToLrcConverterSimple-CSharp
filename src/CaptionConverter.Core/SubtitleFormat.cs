using System.Text;
using System.Text.RegularExpressions;

namespace SrtToLrcConverter;

/// <summary>Supported subtitle container formats.</summary>
public enum SubtitleFormatKind
{
    Srt,
    Vtt,
    SsaAss,
    Smi,
    MicroDvd,
    Mpl2,
    Pjs,
    Ttml,
}

/// <summary>Recognised subtitle file extensions and content sniffing.</summary>
public static class SubtitleFormat
{
    /// <summary>File extensions that may hold subtitle content.</summary>
    public static readonly string[] SupportedExtensions =
    {
        ".srt", ".vtt", ".ass", ".ssa", ".smi", ".sub", ".mpl2", ".pjs", ".ttml", ".dfxp",
    };

    /// <summary>Human-readable friendly name for a format kind.</summary>
    public static string DisplayName(SubtitleFormatKind kind) => kind switch
    {
        SubtitleFormatKind.Srt => "SubRip (SRT)",
        SubtitleFormatKind.Vtt => "WebVTT",
        SubtitleFormatKind.SsaAss => "SubStation Alpha (SSA/ASS)",
        SubtitleFormatKind.Smi => "SAMI",
        SubtitleFormatKind.MicroDvd => "MicroDVD",
        SubtitleFormatKind.Mpl2 => "MPL2",
        SubtitleFormatKind.Pjs => "Phoenix Japanimation (PJS)",
        SubtitleFormatKind.Ttml => "TTML/DFXP",
        _ => kind.ToString(),
    };

    private static readonly Regex XmlRoot = new(
        "<\\s*(?:tt|tt1|div|smil)\\b[^>]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MicroDvdLine = new(
        @"^\s*\{\s*-?\d+\s*\}\s*\{\s*-?\d+\s*\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Mpl2Line = new(
        @"^\s*\[\s*-?\d+\]\s*\[\s*-?\d+\]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PjsLine = new(
        @"^\d{2}:\d{2}:\d{2}[.:]\d{2}[:\s]\d{2}:\d{2}:\d{2}[.:]\d{2}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Sniffs which subtitle format a file holds. Content signatures take
    /// priority; the extension is only consulted for XML-ish content.
    /// </summary>
    public static SubtitleFormatKind Detect(string filename, string content)
    {
        var text = content.TrimStart('\uFEFF', '\uFFFE', ' ', '\t', '\r', '\n');

        if (text.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleFormatKind.Vtt;
        }

        if (LooksLikeTtml(text))
        {
            return SubtitleFormatKind.Ttml;
        }

        if (text.StartsWith("<SAMI", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<SYNC", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleFormatKind.Smi;
        }

        bool hasAss = false;
        bool hasDvd = false;
        bool hasMpl2 = false;
        bool hasPjs = false;
        bool hasArrow = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[Script Info]", StringComparison.OrdinalIgnoreCase))
            {
                hasAss = true;
            }
            else if (MicroDvdLine.IsMatch(line))
            {
                hasDvd = true;
            }
            else if (Mpl2Line.IsMatch(line))
            {
                hasMpl2 = true;
            }
            else if (PjsLine.IsMatch(line))
            {
                hasPjs = true;
            }
            else if (line.Contains("-->", StringComparison.Ordinal))
            {
                hasArrow = true;
            }
        }

        if (hasAss) return SubtitleFormatKind.SsaAss;
        if (hasDvd && !hasArrow) return SubtitleFormatKind.MicroDvd;
        if (hasMpl2 && !hasArrow) return SubtitleFormatKind.Mpl2;
        if (hasPjs && !hasArrow) return SubtitleFormatKind.Pjs;
        if (hasArrow) return SubtitleFormatKind.Srt;

        if (filename.Length > 0)
        {
            var ext = Path.GetExtension(filename).ToLowerInvariant();
            if (ext is ".vtt") return SubtitleFormatKind.Vtt;
            if (ext is ".ass" or ".ssa") return SubtitleFormatKind.SsaAss;
            if (ext is ".smi" or ".sami") return SubtitleFormatKind.Smi;
            if (ext is ".sub") return SubtitleFormatKind.MicroDvd;
            if (ext is ".mpl2") return SubtitleFormatKind.Mpl2;
            if (ext is ".pjs") return SubtitleFormatKind.Pjs;
            if (ext is ".ttml" or ".dfxp") return SubtitleFormatKind.Ttml;
        }

        return SubtitleFormatKind.Srt;
    }

    private static bool LooksLikeTtml(string text)
    {
        if (!text.StartsWith("<", StringComparison.Ordinal) || !XmlRoot.IsMatch(text))
        {
            return false;
        }

        return text.Contains("w3.org/ns/ttml", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ttaf1", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("dfxp", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("xmlns", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Reads subtitle text with BOM/UTF-8 sniffing plus a fallback.</summary>
public static class SubtitleTextDecoder
{
    /// <summary>
    /// Decodes subtitle bytes: honours BOMs, then strict UTF-8, then the system
    /// ANSI codepage as a last resort.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        var text = DecodeWithBom(bytes);
        if (text is not null)
        {
            return text;
        }

        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            if (!utf8.Contains('\uFFFD'))
            {
                return utf8;
            }
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8; fall through to the ANSI codepage.
        }

        return Encoding.Default.GetString(bytes);
    }

    /// <summary>Reads a file path as text using <see cref="Decode"/>.</summary>
    public static string ReadAllText(string path)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        return bytes.Length == 0 ? string.Empty : Decode(bytes);
    }

    private static string? DecodeWithBom(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true).GetString(bytes, 4, bytes.Length - 4);
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return null;
    }
}