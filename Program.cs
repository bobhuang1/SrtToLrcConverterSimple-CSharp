using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using File = System.IO.File;
using System;

namespace SrtToLrcConverterSimple;

/// <summary>
/// Batch-converts .srt subtitle files to .lrc lyric files (the format used by many music
/// players for synchronized lyrics), recursively through a given directory.
/// </summary>
class SrtToLrcConverterSimple
{
    private const string InputFileExtension = ".srt";
    private const string OutputFileExtension = ".lrc";
    private static readonly Encoding DefaultEncoding = Encoding.UTF8;

    // Known filename suffixes (e.g. from auto-generated/auto-translated caption exports) to
    // strip from the output filename. Edit this list for your own naming convention.
    private static readonly string[] KnownFilenameSuffixesToStrip =
    {
        "_中文（自动翻译）", "_中文（自动生成）", "（自动生成）"
    };

    private static readonly Regex HtmlTagPattern = new(
        "<\\S[^><]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public class SubTitle
    {
        public int Sequence { get; set; }
        public TimeSpan From { get; set; }
        public TimeSpan To { get; set; }
        public string? Text { get; set; }
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: SrtToLrcConverterSimple <path to folder containing .srt files>");
            return 1;
        }

        var pathToSrtFiles = CleanText(args[0]);
        var dir = new DirectoryInfo(pathToSrtFiles);

        if (string.IsNullOrEmpty(pathToSrtFiles) || !dir.Exists)
        {
            Console.WriteLine($"Input directory does not exist: \"{pathToSrtFiles}\"");
            return 1;
        }

        Console.WriteLine($"Input directory: {pathToSrtFiles}");

        // Assumes discovery permissions for all folders under the specified path.
        var fileList = dir.GetFiles("*" + InputFileExtension, SearchOption.AllDirectories);
        var convertedCount = 0;

        foreach (var file in fileList)
        {
            try
            {
                var lrcFileContent = ParseSrtToLrc(file.FullName, DefaultEncoding);
                if (string.IsNullOrEmpty(lrcFileContent))
                {
                    Console.WriteLine($"  {file.Name}: no subtitles parsed, skipped.");
                    continue;
                }

                var outputName = file.Name;
                foreach (var suffix in KnownFilenameSuffixesToStrip)
                    outputName = outputName.Replace(suffix, string.Empty);

                var lrcFilePath = Path.Combine(
                    file.DirectoryName ?? string.Empty,
                    Path.ChangeExtension(outputName, OutputFileExtension));

                WriteLrcFile(lrcFilePath, lrcFileContent, DefaultEncoding);
                Console.WriteLine($"  {file.Name} -> {Path.GetFileName(lrcFilePath)}");
                convertedCount++;
            }
            catch (Exception ex)
            {
                // Don't let one bad file abort the whole batch.
                Console.WriteLine($"  {file.Name}: failed to convert - {ex.Message}");
            }
        }

        Console.WriteLine($"Done. Converted {convertedCount} of {fileList.Length} file(s).");
        return 0;
    }

    private static string ParseSrtToLrc(string fileFullName, Encoding encoding)
    {
        using StreamReader sr = new(fileFullName, encoding);
        var subTitles = new List<SubTitle>();

        while (sr.Peek() >= 0)
        {
            var text = ReadNextNonEmptyLine(sr);
            if (text is null) break; // reached end of file while skipping blank lines

            if (!int.TryParse(text, out var sequence))
            {
                Console.WriteLine($"    Could not parse line, expecting a sequence number: {text}");
                continue;
            }

            var subTitle = new SubTitle { Sequence = sequence };
            var timingLine = sr.ReadLine();
            var timingParts = timingLine?.Split(new[] { "-->" }, StringSplitOptions.RemoveEmptyEntries);

            if (timingParts?.Length != 2 ||
                !TimeSpan.TryParseExact(timingParts[0].Trim(), "hh\\:mm\\:ss\\,fff", CultureInfo.InvariantCulture, out var from) ||
                !TimeSpan.TryParseExact(timingParts[1].Trim(), "hh\\:mm\\:ss\\,fff", CultureInfo.InvariantCulture, out var to))
            {
                Console.WriteLine($"    Could not parse line, expecting from/to timestamps: {timingLine}");
                continue;
            }

            subTitle.From = from;
            subTitle.To = to;

            var textBuilder = new StringBuilder();
            string? line;
            while (!string.IsNullOrEmpty(line = sr.ReadLine()))
            {
                textBuilder.Append(CleanHtmlTags(line)).Append(' ');
            }

            subTitle.Text = textBuilder.ToString();
            subTitles.Add(subTitle);
        }

        if (subTitles.Count < 1)
        {
            Console.WriteLine("    No subtitle entries found in input file.");
            return string.Empty;
        }

        var lrcFileContent = new StringBuilder();
        foreach (var subTitle in subTitles)
        {
            if (string.IsNullOrEmpty(subTitle.Text)) continue;

            var totalMinutes =
                subTitle.From.Days * 1440 +
                subTitle.From.Hours * 60 +
                subTitle.From.Minutes;

            var timestamp = totalMinutes.ToString("D2", CultureInfo.InvariantCulture) +
                             subTitle.From.ToString("\\:ss\\.ff", CultureInfo.InvariantCulture);

            lrcFileContent.Append('[').Append(timestamp).Append(']').AppendLine(CleanText(subTitle.Text));
        }

        lrcFileContent.AppendLine();
        return lrcFileContent.ToString();
    }

    /// <summary>Reads lines until a non-empty one is found, or returns null at end of stream.</summary>
    private static string? ReadNextNonEmptyLine(StreamReader sr)
    {
        string? text;
        do
        {
            text = sr.ReadLine();
        } while (text is not null && string.IsNullOrEmpty(text) && sr.Peek() >= 0);

        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static void WriteLrcFile(string fileFullName, string content, Encoding encoding)
    {
        File.WriteAllText(fileFullName, content + Environment.NewLine, encoding);
    }

    private static string CleanHtmlTags(string input) => HtmlTagPattern.Replace(CleanText(input), string.Empty);

    private static string CleanText(string? input) => string.IsNullOrEmpty(input) ? string.Empty : input.Trim();
}
