using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SrtToLrcConverter;

/// <summary>Parses subtitle text of various formats into a common <see cref="SubTitle"/> list.</summary>
internal static class SubtitleParsers
{
    public static IReadOnlyList<SubTitle> Parse(
        SubtitleFormatKind kind, string text, List<SrtWarning> warnings) => kind switch
    {
        SubtitleFormatKind.Srt => ParseSrt(text, warnings),
        SubtitleFormatKind.Vtt => ParseVtt(text, warnings),
        SubtitleFormatKind.SsaAss => ParseAss(text, warnings),
        SubtitleFormatKind.Smi => ParseSmi(text, warnings),
        SubtitleFormatKind.MicroDvd => ParseMicroDvd(text, warnings),
        SubtitleFormatKind.Mpl2 => ParseMpl2(text, warnings),
        SubtitleFormatKind.Pjs => ParsePjs(text, warnings),
        SubtitleFormatKind.Ttml => ParseTtml(text, warnings),
        _ => ParseSrt(text, warnings),
    };

    private static readonly Regex HtmlTagPattern = new(
        "<\\S[^><]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AssFormatLine = new(
        @"^Format\s*:\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AssTime = new(
        @"^(\d+):(\d{2}):(\d{2})[.,](\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AssOverride = new(
        @"\{[^}]*\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VttTime = new(
        @"^(?:(\d+):)?(\d{2}):(\d{2})[.,](\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MicroDvdLine = new(
        @"^\s*\{(\d+)\}\s*\{(\d+)\}(.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MicroDvdFps = new(
        @"^\s*\{-?\d+\}\s*\{-?\d+\}\s*(\d+(?:\.\d+)?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Mpl2Line = new(
        @"^\s*\[(-?\d+)\]\s*\[(-?\d+)\](.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PjsLine = new(
        @"^(\d{2}):(\d{2}):(\d{2})[.:](\d{2})[:\s]+(\d{2}):(\d{2}):(\d{2})[.:](\d{2})[:\s=]+(.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SmiSync = new(
        @"<SYNC\s+Start\s*=\s*""?(\d+)""?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TtmlClock = new(
        @"^(?:(\d+):)?(\d{2}):(\d{2})(?:[.,](\d{1,7}))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // --- shared helpers -----------------------------------------------------

    private static IReadOnlyList<SubTitle> Finish(
        List<SubTitle> subs, List<SrtWarning>? warnings)
    {
        if (subs.Count == 0)
        {
            warnings?.Add(new SrtWarning(0, "No subtitle entries found in input file."));
            return [];
        }

        // Formats that lack an end time: hold each entry open until the next
        // start so short LRC timestamps still come out in order.
        for (int i = 0; i + 1 < subs.Count; i++)
        {
            if (subs[i].To == subs[i].From)
            {
                subs[i] = subs[i] with { To = subs[i + 1].From };
            }
        }

        return subs;
    }

    private static string CleanText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = HtmlTagPattern.Replace(input, string.Empty);
        text = text
            .Replace("\\N", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal);
        return Regex.Replace(text, @"\s{2,}", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static void Warn(List<SrtWarning> warnings, int sequence, string message) =>
        warnings.Add(new SrtWarning(sequence, message));

    private static bool TryVttTime(string token, out TimeSpan value)
    {
        var m = VttTime.Match(token);
        if (!m.Success)
        {
            value = default;
            return false;
        }

        var hours = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
        var minutes = int.Parse(m.Groups[2].Value);
        var seconds = int.Parse(m.Groups[3].Value);
        var ms = m.Groups[4].Length;
        var frac = int.Parse(m.Groups[4].Value.PadRight(3, '0')[..3]);
        value = TimeSpan.FromMilliseconds(hours * 3600000L + minutes * 60000L + seconds * 1000L + frac);
        return true;
    }

    private static bool TryTtmlTime(string token, out TimeSpan value)
    {
        var m = TtmlClock.Match(token);
        if (m.Success)
        {
            var hours = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
            var minutes = int.Parse(m.Groups[2].Value);
            var seconds = int.Parse(m.Groups[3].Value);
            long fracMs = 0;
            if (m.Groups[4].Success)
            {
                var f = m.Groups[4].Value;
                fracMs = long.Parse(f) * (1000L * (long)Math.Pow(10, Math.Max(0, 3 - f.Length)));
            }

            value = TimeSpan.FromMilliseconds(hours * 3600000L + minutes * 60000L + seconds * 1000L + fracMs);
            return true;
        }

        var plain = PlainClock.Match(token);
        if (plain.Success)
        {
            var number = double.Parse(plain.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            value = plain.Groups[2].Value == "ms"
                ? TimeSpan.FromMilliseconds(number)
                : TimeSpan.FromSeconds(number);
            return true;
        }

        value = default;
        return false;
    }

    private static readonly Regex PlainClock = new(
        @"^(\d+(?:\.\d+)?)\s*(ms|s)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string DecodeSmiEntities(string input)
    {
        if (!input.Contains('&', StringComparison.Ordinal))
        {
            return input;
        }

        var result = input
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("&apos;", "'", StringComparison.OrdinalIgnoreCase)
            .Replace("&#39;", "'", StringComparison.OrdinalIgnoreCase);
        return result;
    }

    // --- SubRip ------------------------------------------------------------

    private static IReadOnlyList<SubTitle> ParseSrt(string text, List<SrtWarning> warnings)
    {
        var subs = new List<SubTitle>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var firstLine = lines[i].Trim();
            if (firstLine.Length == 0)
            {
                continue;
            }

            if (!int.TryParse(firstLine, out var sequence))
            {
                Warn(warnings, 0, $"Could not parse line, expecting a sequence number: {firstLine}");
                continue;
            }

            if (++i >= lines.Length)
            {
                Warn(warnings, sequence, "Timing line missing for entry.");
                break;
            }

            var timingParts = lines[i].Split(
                new[] { "-->" }, StringSplitOptions.RemoveEmptyEntries);

            if (timingParts.Length != 2 ||
                !TimeSpan.TryParseExact(timingParts[0].Trim(), "hh\\:mm\\:ss\\,fff",
                    System.Globalization.CultureInfo.InvariantCulture, out var from) ||
                !TimeSpan.TryParseExact(timingParts[1].Trim(), "hh\\:mm\\:ss\\,fff",
                    System.Globalization.CultureInfo.InvariantCulture, out var to))
            {
                Warn(warnings, sequence, $"Could not parse line, expecting from/to timestamps: {lines[i]}");
                continue;
            }

            var sb = new StringBuilder();
            while (++i < lines.Length)
            {
                var subLine = lines[i];
                if (subLine.Trim().Length == 0)
                {
                    break;
                }
                sb.Append(CleanText(subLine)).Append(' ');
            }

            var caption = CleanText(sb.ToString());
            if (caption.Length > 0)
            {
                subs.Add(new SubTitle(sequence, from, to, caption));
            }
        }

        return Finish(subs, warnings);
    }

    // --- WebVTT ------------------------------------------------------------

    private static IReadOnlyList<SubTitle> ParseVtt(string text, List<SrtWarning> warnings)
    {
        var subs = new List<SubTitle>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sequence = 1;

        int i = 0;
        if (i < lines.Length && lines[i].TrimStart('\uFEFF').StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
        {
            i++; // skip the header block (metadata lines) until the first blank line
            while (i < lines.Length && lines[i].Trim().Length > 0)
            {
                i++;
            }
        }

        for (; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("STYLE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("REGION", StringComparison.OrdinalIgnoreCase))
            {
                // NOTE/STYLE/REGION blocks run until the next blank line.
                while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0)
                {
                    i++;
                }
                continue;
            }

            var marker = line.IndexOf("-->", StringComparison.Ordinal);
            if (marker < 0)
            {
                var cueId = line; // optional cue identifier; the timing line follows
                if (i + 1 >= lines.Length || lines[i + 1].IndexOf("-->", StringComparison.Ordinal) < 0)
                {
                    Warn(warnings, sequence, $"Could not parse cue, expecting a timing line: {cueId}");
                    continue;
                }
                i++;
                line = lines[i].Trim();
                marker = line.IndexOf("-->", StringComparison.Ordinal);
            }

            var startToken = line[..marker].Trim();
            var endToken = line[(marker + 3)..].Trim();
            var endIndex = endToken.IndexOf(' ');
            if (endIndex >= 0)
            {
                endToken = endToken[..endIndex]; // drop cue settings like position:5%
            }

            if (!TryVttTime(startToken, out var from) || !TryVttTime(endToken, out var to))
            {
                Warn(warnings, sequence, $"Could not parse cue timing: {line}");
                continue;
            }

            var sb = new StringBuilder();
            while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0)
            {
                i++;
                sb.Append(CleanText(lines[i])).Append(' ');
            }

            var caption = CleanText(sb.ToString());
            if (caption.Length > 0)
            {
                subs.Add(new SubTitle(sequence++, from, to, caption));
            }
        }

        return Finish(subs, warnings);
    }

    // --- SSA / ASS ---------------------------------------------------------

    private static IReadOnlyList<SubTitle> ParseAss(string text, List<SrtWarning> warnings)
    {
        var subs = new List<SubTitle>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sequence = 1;
        var inEvents = false;
        string[]? fields = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEvents)
            {
                continue;
            }

            var fmt = AssFormatLine.Match(line);
            if (fmt.Success)
            {
                fields = fmt.Groups[1].Value.Split(',').Select(f => f.Trim()).ToArray();
                continue;
            }

            if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = line[(line.IndexOf(':') + 1)..].TrimStart();
            var parts = payload.Split(',');

            int startIndex = fields is null ? 1 : Array.IndexOf(fields, "Start");
            int endIndex = fields is null ? 2 : Array.IndexOf(fields, "End");
            int textIndex = fields is null ? 9 : Array.IndexOf(fields, "Text");
            if (startIndex < 0) startIndex = 1;
            if (endIndex < 0) endIndex = 2;
            if (textIndex < 0) textIndex = parts.Length - 1;

            if (parts.Length <= textIndex)
            {
                Warn(warnings, sequence, $"Could not parse Dialogue line (too few fields): {line}");
                continue;
            }

            var startToken = startIndex < parts.Length ? parts[startIndex] : string.Empty;
            var endToken = endIndex < parts.Length ? parts[endIndex] : string.Empty;

            if (!TryAssTime(startToken, out var from) || !TryAssTime(endToken, out var to))
            {
                Warn(warnings, sequence, $"Could not parse Dialogue timestamps: {line}");
                continue;
            }

            var captionText = string.Join(",", parts.Skip(textIndex)).Trim();
            captionText = AssOverride.Replace(captionText, string.Empty);
            captionText = CleanText(captionText);
            if (captionText.Length > 0)
            {
                subs.Add(new SubTitle(sequence++, from, to, captionText));
            }
        }

        return Finish(subs, warnings);
    }

    private static bool TryAssTime(string token, out TimeSpan value)
    {
        var m = AssTime.Match(token);
        if (!m.Success)
        {
            value = default;
            return false;
        }

        var hours = int.Parse(m.Groups[1].Value);
        var minutes = int.Parse(m.Groups[2].Value);
        var seconds = int.Parse(m.Groups[3].Value);
        var frac = m.Groups[4].Value;
        var ms = frac.Length switch
        {
            2 => int.Parse(frac) * 10,              // centiseconds -> ms
            _ => int.Parse(frac.PadRight(3, '0')[..3]),
        };

        value = TimeSpan.FromMilliseconds(hours * 3600000L + minutes * 60000L + seconds * 1000L + ms);
        return true;
    }

    // --- SAMI --------------------------------------------------------------

    private static IReadOnlyList<SubTitle> ParseSmi(string text, List<SrtWarning> warnings)
    {
        var subs = new List<SubTitle>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sequence = 1;
        var pendingStart = TimeSpan.MinValue;
        var sb = new StringBuilder();

        void Flush()
        {
            if (pendingStart != TimeSpan.MinValue && sb.Length > 0)
            {
                var caption = CleanText(sb.ToString());
                if (caption.Length > 0)
                {
                    subs.Add(new SubTitle(sequence++, pendingStart, pendingStart, caption));
                }
            }
            sb.Clear();
            pendingStart = TimeSpan.MinValue;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            var sync = SmiSync.Match(line);
            if (sync.Success)
            {
                Flush();
                pendingStart = TimeSpan.FromMilliseconds(int.Parse(sync.Groups[1].Value));

                // SAMI sometimes puts the <P> text on the same line as <SYNC>.
                var sameLineText = line[(sync.Index + sync.Length)..].Trim().TrimStart('>');
                if (sameLineText.Length > 0 && !sameLineText.StartsWith("</", StringComparison.Ordinal))
                {
                    sb.Append(sameLineText).Append(' ');
                }
                continue;
            }

            if (line.Contains("<SYNC", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                Warn(warnings, sequence, $"Could not parse SYNC start value: {line}");
                continue;
            }

            if (pendingStart != TimeSpan.MinValue && !line.StartsWith("</", StringComparison.Ordinal))
            {
                sb.Append(DecodeSmiEntities(line)).Append(' ');
            }
        }

        Flush();
        return Finish(subs, warnings);
    }

    // --- MicroDVD ----------------------------------------------------------

    private static IReadOnlyList<SubTitle> ParseMicroDvd(string text, List<SrtWarning> warnings)
    {
        var subs = new List<SubTitle>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sequence = 1;
        var fps = 25.0;

        if (lines.Length > 0)
        {
            var fpsMatch = MicroDvdFps.Match(lines[0]);
            if (fpsMatch.Success)
            {
                fps = double.Parse(fpsMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || MicroDvdFps.IsMatch(line))
            {
                continue;
            }

            var m = MicroDvdLine.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var startFrame = int.Parse(m.Groups[1].Value);
            var endFrame = int.Parse(m.Groups[2].Value);
            var caption = CleanText(m.Groups[3].Value);
            if (caption.Length == 0)
            {
                continue;
            }

            var from = TimeSpan.FromSeconds(startFrame / fps);
            var to = endFrame > startFrame ? TimeSpan.FromSeconds(endFrame / fps) : from;
            subs.Add(new SubTitle(sequence++, from, to, caption));
        }

        return Finish(subs, warnings);
    }

    // --- MPL2 --------------------------------------------------------------

    private static IReadOnlyList<SubTitle> ParseMpl2(string text, List<SrtWarning> warnings)
    {
        var subs = new List<SubTitle>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sequence = 1;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var m = Mpl2Line.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var startDs = int.Parse(m.Groups[1].Value);
            var endDs = int.Parse(m.Groups[2].Value);
            var caption = CleanText(m.Groups[3].Value);
            if (caption.Length == 0)
            {
                continue;
            }

            if (endDs < 0)
            {
                // Negative end = continuation of the previous subtitle.
                if (subs.Count > 0)
                {
                    var previous = subs[^1];
                    subs[^1] = previous with
                    {
                        To = TimeSpan.FromSeconds(Math.Abs(startDs) / 10.0),
                        Text = (previous.Text + " " + caption).Trim(),
                    };
                }
                continue;
            }

            subs.Add(new SubTitle(
                sequence++,
                TimeSpan.FromSeconds(Math.Abs(startDs) / 10.0),
                TimeSpan.FromSeconds(endDs / 10.0),
                caption));
        }

        return Finish(subs, warnings);
    }

    // --- PJS (Phoenix Japanimation) ----------------------------------------

    private static IReadOnlyList<SubTitle> ParsePjs(string text, List<SrtWarning> warnings)
    {
        var subs = new List<SubTitle>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sequence = 1;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var m = PjsLine.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var from = time(m, 0);
            var to = time(m, 4);
            var caption = CleanText(m.Groups[9].Value);
            if (caption.Length > 0)
            {
                subs.Add(new SubTitle(sequence++, from, to, caption));
            }
        }

        return Finish(subs, warnings);

        static TimeSpan time(Match m, int offset) =>
            TimeSpan.FromSeconds(
                int.Parse(m.Groups[1 + offset].Value) * 3600 +
                int.Parse(m.Groups[2 + offset].Value) * 60 +
                int.Parse(m.Groups[3 + offset].Value)) +
            TimeSpan.FromMilliseconds(int.Parse(m.Groups[4 + offset].Value) * 10);
    }

    // --- TTML / DFXP -------------------------------------------------------

    private static IReadOnlyList<SubTitle> ParseTtml(string text, List<SrtWarning> warnings)
    {
        var subs = new List<SubTitle>();

        try
        {
            var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            foreach (var node in doc.Descendants())
            {
                if (!node.Name.LocalName.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var beginToken = node.Attribute("begin")?.Value;
                var endToken = node.Attribute("end")?.Value;

                if (beginToken is null || !TryTtmlTime(beginToken, out var from))
                {
                    Warn(warnings, 0, $"Could not parse TTML <p> timing: {beginToken}");
                    continue;
                }

                var resolvedTo = default(TimeSpan);
                if (endToken is null)
                {
                    resolvedTo = from; // resolved to the next subtitle's start in Finish
                }
                else if (!TryTtmlTime(endToken, out resolvedTo))
                {
                    Warn(warnings, 0, $"Could not parse TTML <p> timing: {beginToken}");
                    continue;
                }

                var caption = CleanText(node.Value);
                if (caption.Length > 0)
                {
                    subs.Add(new SubTitle(1, from, resolvedTo, caption));
                }
            }
        }
        catch (XmlException ex)
        {
            Warn(warnings, 0, $"Could not parse XML/TTML: {ex.Message}");
        }

        return Finish(subs, warnings);
    }
}