using System.Globalization;
using System.Text;
using File = System.IO.File;

namespace SrtToLrcConverter;

/// <summary>A single parsed subtitle entry.</summary>
public sealed record SubTitle(int Sequence, TimeSpan From, TimeSpan To, string Text);

/// <summary>A non-fatal parse note (unparseable or skipped entry).</summary>
public sealed record SrtWarning(int Sequence, string Message);

/// <summary>Parsed subtitle content rendered as LRC text, plus any warnings.</summary>
public sealed record LrcOutput(string Text, IReadOnlyList<SrtWarning> Warnings);

/// <summary>Outcome of converting one file.</summary>
public sealed record FileConversionResult(
    string InputPath,
    string OutputPath,
    LrcOutput? Lrc,
    bool Converted,
    string? Error,
    SubtitleFormatKind Format = SubtitleFormatKind.Srt);

/// <summary>Tunables for a conversion batch.</summary>
public sealed class LrcConversionOptions
{
    /// <summary>Input file extension filters scanned in folder mode.</summary>
    public string[] InputExtensions { get; set; } = SubtitleFormat.SupportedExtensions;

    /// <summary>Output file extension, e.g. ".lrc".</summary>
    public string OutputExtension { get; set; } = ".lrc";

    /// <summary>
    /// Encoding used to read the input and write the LRC. When null (the
    /// default) the input is auto-detected from BOM/UTF-8/strictness and the
    /// output is written as UTF-8.
    /// </summary>
    public Encoding? Encoding { get; set; }

    /// <summary>
    /// Known filename suffixes (e.g. from auto-generated/auto-translated caption
    /// exports) to strip from the output filename.
    /// </summary>
    public string[] FilenameSuffixesToStrip { get; set; } =
        ["_中文（自动翻译）", "_中文（自动生成）", "（自动生成）"];

    /// <summary>
    /// Optional output directory. When null, each LRC is written next to its
    /// source subtitle file.
    /// </summary>
    public string? OutputDirectory { get; set; }
}

/// <summary>Converts subtitle text (many formats) to LRC lyric format.</summary>
public sealed class SrtToLrcConverter
{
    /// <summary>Parses subtitle text and renders it as LRC text, auto-detecting the format.</summary>
    public LrcOutput Parse(string text) => Parse(text, SubtitleFormat.Detect(string.Empty, text));

    /// <summary>Parses subtitle text of an explicit format and renders LRC text.</summary>
    public LrcOutput Parse(string text, SubtitleFormatKind kind)
    {
        var warnings = new List<SrtWarning>();
        var subtitles = SubtitleParsers.Parse(kind, text, warnings);
        return new LrcOutput(RenderLrc(subtitles), warnings);
    }

    /// <summary>Converts one subtitle file to an LRC file on disk.</summary>
    public FileConversionResult ConvertFile(string inputPath, LrcConversionOptions options)
    {
        var outputPath = GetOutputPath(inputPath, options);

        try
        {
            var text = options.Encoding is null
                ? SubtitleTextDecoder.ReadAllText(inputPath)
                : File.ReadAllText(inputPath, options.Encoding);

            var kind = SubtitleFormat.Detect(inputPath, text);
            var lrc = Parse(text, kind);

            if (lrc.Text.Length == 0)
            {
                // Matches the classic CLI behaviour: an input with no usable
                // subtitles is reported, not treated as an error.
                return new FileConversionResult(inputPath, outputPath, lrc, false, null, kind);
            }

            var outputEncoding = options.Encoding ?? Encoding.UTF8;
            File.WriteAllText(outputPath, lrc.Text + Environment.NewLine, outputEncoding);
            return new FileConversionResult(inputPath, outputPath, lrc, true, null, kind);
        }
        catch (Exception ex)
        {
            return new FileConversionResult(inputPath, outputPath, null, false, ex.Message);
        }
    }

    /// <summary>Computes the LRC output path for a given input path.</summary>
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

    private static string RenderLrc(IReadOnlyList<SubTitle> subtitles)
    {
        var lrc = new StringBuilder();
        foreach (var sub in subtitles)
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

            lrc.Append('[').Append(timestamp).Append(']').AppendLine(sub.Text.Trim());
        }

        lrc.AppendLine();
        return lrc.ToString();
    }
}