using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using File = System.IO.File;

namespace SrtToLrcConverter;

/// <summary>A single parsed SRT subtitle entry.</summary>
public sealed record SubTitle(int Sequence, TimeSpan From, TimeSpan To, string Text);

/// <summary>A non-fatal parse note (unparseable or skipped entry).</summary>
public sealed record SrtWarning(int Sequence, string Message);

/// <summary>Parsed SRT content rendered as LRC text, plus any warnings.</summary>
public sealed record LrcOutput(string Text, IReadOnlyList<SrtWarning> Warnings);

/// <summary>Outcome of converting one file.</summary>
public sealed record FileConversionResult(
    string InputPath,
    string OutputPath,
    LrcOutput? Lrc,
    bool Converted,
    string? Error);

/// <summary>Tunables for a conversion batch.</summary>
public sealed class LrcConversionOptions
{
    /// <summary>Input file extension filter, e.g. ".srt".</summary>
    public string InputExtension { get; init; } = ".srt";

    /// <summary>Output file extension, e.g. ".lrc".</summary>
    public string OutputExtension { get; init; } = ".lrc";

    /// <summary>Encoding used to read the SRT and write the LRC. Defaults to UTF-8.</summary>
    public Encoding Encoding { get; set; } = Encoding.UTF8;

    /// <summary>
    /// Known filename suffixes (e.g. from auto-generated/auto-translated caption
    /// exports) to strip from the output filename.
    /// </summary>
    public string[] FilenameSuffixesToStrip { get; set; } =
        ["_中文（自动翻译）", "_中文（自动生成）", "（自动生成）"];

    /// <summary>
    /// Optional output directory. When null, each LRC is written next to its
    /// source SRT file.
    /// </summary>
    public string? OutputDirectory { get; set; }
}

/// <summary>Converts SRT subtitle text to LRC lyric format.</summary>
public sealed class SrtToLrcConverter
{
    private static readonly Regex HtmlTagPattern = new(
        "<\\S[^><]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed record SubTitleDraft(int Sequence, TimeSpan From, TimeSpan To, string? Text);

    /// <summary>Parses SRT text and renders it as LRC text.</summary>
    public LrcOutput Parse(string srtText)
    {
        var warnings = new List<SrtWarning>();
        var subTitles = new List<SubTitleDraft>();
        var lines = srtText.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var text = Clean(lines[i]);
            if (text.Length == 0)
            {
                continue; // blank line
            }

            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            {
                warnings.Add(new SrtWarning(0, $"Could not parse line, expecting a sequence number: {text}"));
                continue;
            }

            // timing line
            if (++i >= lines.Length)
            {
                warnings.Add(new SrtWarning(sequence, "Timing line missing for entry."));
                break;
            }

            var timingLine = lines[i];
            var timingParts = timingLine.Split(
                new[] { "-->" }, StringSplitOptions.RemoveEmptyEntries);

            if (timingParts.Length != 2 ||
                !TimeSpan.TryParseExact(timingParts[0].Trim(), "hh\\:mm\\:ss\\,fff", CultureInfo.InvariantCulture, out var from) ||
                !TimeSpan.TryParseExact(timingParts[1].Trim(), "hh\\:mm\\:ss\\,fff", CultureInfo.InvariantCulture, out var to))
            {
                warnings.Add(new SrtWarning(sequence, $"Could not parse line, expecting from/to timestamps: {timingLine}"));
                continue;
            }

            // caption text: one or more lines until the next blank line
            var textBuilder = new StringBuilder();
            while (++i < lines.Length)
            {
                var subLine = lines[i];
                if (subLine.Trim().Length == 0)
                {
                    break;
                }
                textBuilder.Append(RemoveHtmlTags(subLine)).Append(' ');
            }

            subTitles.Add(new SubTitleDraft(sequence, from, to, textBuilder.ToString()));
        }

        if (subTitles.Count < 1)
        {
            warnings.Add(new SrtWarning(0, "No subtitle entries found in input file."));
            return new LrcOutput(string.Empty, warnings);
        }

        var lrc = new StringBuilder();
        foreach (var sub in subTitles)
        {
            if (string.IsNullOrWhiteSpace(sub.Text))
            {
                continue;
            }

            var totalMinutes =
                sub.From.Days * 1440 +
                sub.From.Hours * 60 +
                sub.From.Minutes;

            var timestamp = totalMinutes.ToString("D2", CultureInfo.InvariantCulture) +
                            sub.From.ToString("\\:ss\\.ff", CultureInfo.InvariantCulture);

            lrc.Append('[').Append(timestamp).Append(']').AppendLine(Clean(sub.Text));
        }

        lrc.AppendLine();
        return new LrcOutput(lrc.ToString(), warnings);
    }

    /// <summary>Converts one SRT file to an LRC file on disk.</summary>
    public FileConversionResult ConvertFile(string inputPath, LrcConversionOptions options)
    {
        var outputPath = GetOutputPath(inputPath, options);

        try
        {
            var srtText = ReadAllText(inputPath, options.Encoding);
            var lrc = Parse(srtText);

            if (lrc.Text.Length == 0)
            {
                // Matches the classic CLI behaviour: an input with no usable
                // subtitles is reported, not treated as an error.
                return new FileConversionResult(inputPath, outputPath, lrc, false, null);
            }

            WriteAllText(outputPath, lrc.Text + Environment.NewLine, options.Encoding);
            return new FileConversionResult(inputPath, outputPath, lrc, true, null);
        }
        catch (Exception ex)
        {
            return new FileConversionResult(inputPath, outputPath, null, false, ex.Message);
        }
    }

    /// <summary>Computes the LRC output path for a given SRT input path.</summary>
    public string GetOutputPath(string inputPath, LrcConversionOptions options)
    {
        var fileName = Path.GetFileName(inputPath);
        foreach (var suffix in options.FilenameSuffixesToStrip)
        {
            fileName = fileName.Replace(suffix, string.Empty, StringComparison.Ordinal);
        }

        var targetName = Path.ChangeExtension(fileName, options.OutputExtension);
        var targetDir = !string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? options.OutputDirectory
            : Path.GetDirectoryName(inputPath);

        return Path.Combine(targetDir ?? string.Empty, targetName);
    }

    private static string ReadAllText(string path, Encoding encoding) => File.ReadAllText(path, encoding);

    private static void WriteAllText(string path, string content, Encoding encoding) =>
        File.WriteAllText(path, content, encoding);

    private static string RemoveHtmlTags(string input) => HtmlTagPattern.Replace(Clean(input), string.Empty);

    private static string Clean(string? input) =>
        string.IsNullOrEmpty(input) ? string.Empty : input.Trim();
}