using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using File = System.IO.File;
using System;
using System.Security.Cryptography;

namespace SrtToLrcConverterSimple;

class SrtToLrcConverterSimple
{
    private static readonly string InputFileExtension = "SRT";
    private static readonly string OutputFileExtenstion = "LRC";
    private static readonly Encoding DefaultEncoding = Encoding.UTF8;
    private static readonly string[] RemoveText = { "_中文（自动翻译）", "_中文（自动生成）", "（自动生成）" };

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
            // No parameter found
            Console.WriteLine("Usage: Add path of SRT files as input parameter");
            return 0;
        }

        var pathToSrtFiles = CleanText(args[0]);
        if (string.IsNullOrEmpty(pathToSrtFiles))
        {
            // Wrong parameter found
            Console.WriteLine("Usage: Add path of SRT files as input parameter");
            return -1;
        }

        // Take a snapshot of the file system.  
        var dir = new DirectoryInfo(pathToSrtFiles);

        if (dir is null)
        {
            // Wrong parameter found
            Console.WriteLine("Input directory does not exist");
            return -1;
        }

        Console.WriteLine($"Input directory: {pathToSrtFiles}, list of SRT files: ");
        // This method assumes that the application has discovery permissions  
        // for all folders under the specified path.  
        var fileList = dir.GetFiles("*" + InputFileExtension, SearchOption.AllDirectories);
        //Create the query  
        foreach (var file in fileList)
        {
            var lrcFileContent = ParseSrtToLrc(file.FullName, DefaultEncoding);
            if (string.IsNullOrEmpty(lrcFileContent))
            {
                Console.WriteLine("Unable to parse SRT into LRC. Skipped.");
                continue;
            }
            var lrcFileName = string.Concat(file.FullName.AsSpan(0, file.FullName.Length - 3), OutputFileExtenstion);
            foreach (var remove in RemoveText)
            {
                lrcFileName = lrcFileName.Replace(remove, string.Empty);
            }
            Console.WriteLine(lrcFileName);
            // Console.WriteLine(lrcFileContent);
            WriteLrcFile(lrcFileName, lrcFileContent, DefaultEncoding);

        }
        return 1;
    }

    private static string ParseSrtToLrc(string fileFullName, Encoding encoding)
    {
        Console.WriteLine($"Input File Name: {fileFullName}");
        using StreamReader sr = new(fileFullName, encoding);
        var subTitles = new List<SubTitle>();
        while (sr.Peek() >= 0)
        {
            string? text = sr.ReadLine();
            while (string.IsNullOrEmpty(text))
            {
                text = sr.ReadLine();
            }

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }
            if (int.TryParse(text, out var result))
            {
                var subTitle = new SubTitle
                {
                    Sequence = result
                };
                text = sr.ReadLine();
                string[]? array = text?.Split(new[] { "-->" }, StringSplitOptions.RemoveEmptyEntries);
                if (array?.Length == 2)
                {
                    string input = array[0].Trim();
                    string input2 = array[1].Trim();
                    if (TimeSpan.TryParseExact(input, "hh\\:mm\\:ss\\,fff", CultureInfo.CurrentCulture, out var result2) && TimeSpan.TryParseExact(input2, "hh\\:mm\\:ss\\,fff", CultureInfo.CurrentCulture, out var result3))
                    {
                        subTitle.From = result2;
                        subTitle.To = result3;
                        subTitle.Text = string.Empty;
                        while (!sr.EndOfStream)
                        {
                            text = sr.ReadLine();
                            if (string.IsNullOrEmpty(text))
                            {
                                break;
                            }

                            subTitle.Text += CleanHtmlTags(text) + " ";
                        }
                        subTitles.Add(subTitle);
                        continue;
                    }
                    Console.WriteLine($"Could not parse line, expecting from and to timestamps: {text}");
                }
                Console.WriteLine($"Could not parse line, expecting from and to timestamps: {text}");
            }
            Console.WriteLine($"Could not parse line, expecting a sequence number: {text}");
        }

        if (subTitles is null || subTitles.Count < 1)
        {
            Console.WriteLine("No subtitle found in input file, returning empty.");
            return string.Empty;
        }

        var lrcFileContent = new StringBuilder();
        foreach (var subTitle in subTitles)
        {
            var currentLine = "[";
            int day = CleanInt(subTitle.From.ToString("%d"));
            int hour = CleanInt(subTitle.From.ToString("%h"));
            int minute = CleanInt(subTitle.From.ToString("%m"));
            int totalMinutes = day * 1440 + hour * 60 + minute;
            currentLine += (totalMinutes > 9) ? totalMinutes.ToString() : "0" + totalMinutes.ToString();
            currentLine += subTitle.From.ToString("\\:ss\\.ff");
            currentLine += "]";
            if (subTitle.Text != null)
            {
                currentLine += CleanText(subTitle.Text);
                lrcFileContent.AppendLine(currentLine);
            }
        }

        lrcFileContent.AppendLine();
        return lrcFileContent.ToString();
    }

    private static void WriteLrcFile(string fileFullName, string content, Encoding encoding)
    {
        File.WriteAllText(fileFullName, content + Environment.NewLine, encoding);
    }

    private static string CleanHtmlTags(string input)
    { 
        var stripHtmlExpression = new Regex("<\\S[^><]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        return stripHtmlExpression.Replace(CleanText(input), string.Empty);
    }

    private static int CleanInt(string input)
    {
        if (string.IsNullOrEmpty(input)) return 0;
        if (int.TryParse(input, out int result))
            return result;
        return 0;
    }

    private static string CleanText(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Trim();
    }
}