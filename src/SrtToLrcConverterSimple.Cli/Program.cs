using SrtToLrcConverter;

// Batch-converts subtitle files (SRT, VTT, SSA/ASS, SAMI, MicroDVD, MPL2,
// PJS, TTML/DFXP) to .lrc lyric files (the format used by many music players
// for synchronized lyrics), recursively through a given directory.

if (args.Length == 0)
{
    Console.WriteLine(
        "Usage: SrtToLrcConverterSimple <path to folder containing subtitle files>");
    Console.WriteLine("Supported input formats: SRT, VTT, SSA/ASS, SAMI, MicroDVD, MPL2, PJS, TTML/DFXP.");
    return 1;
}

var pathToSubtitleFiles = args[0].Trim();
var dir = new DirectoryInfo(pathToSubtitleFiles);

if (string.IsNullOrEmpty(pathToSubtitleFiles) || !dir.Exists)
{
    Console.WriteLine($"Input directory does not exist: \"{pathToSubtitleFiles}\"");
    return 1;
}

Console.WriteLine($"Input directory: {pathToSubtitleFiles}");

var options = new LrcConversionOptions();
var converter = new SrtToLrcConverter.SrtToLrcConverter();

// Assumes discovery permissions for all folders under the specified path.
// Scans each supported extension union, de-duplicated by full path.
var fileList = options.InputExtensions
    .Select(ext => dir.GetFiles("*" + ext, SearchOption.AllDirectories))
    .SelectMany(files => files)
    .GroupBy(f => f.FullName)
    .Select(g => g.First())
    .ToArray();
var convertedCount = 0;

foreach (var file in fileList)
{
    var result = converter.ConvertFile(file.FullName, options);

    if (!result.Converted)
    {
        if (result.Error is not null)
        {
            Console.WriteLine($"  {file.Name}: failed to convert - {result.Error}");
        }
        else
        {
            Console.WriteLine($"  {file.Name}: no subtitles parsed, skipped.");
        }
        continue;
    }

    var format = SubtitleFormat.DisplayName(result.Format);
    Console.WriteLine($"  {file.Name} [{format}] -> {Path.GetFileName(result.OutputPath)}");

    foreach (var warning in result.Lrc?.Warnings ?? [])
    {
        Console.WriteLine($"      ({warning.Sequence}) {warning.Message}");
    }

    convertedCount++;
}

Console.WriteLine($"Done. Converted {convertedCount} of {fileList.Length} file(s).");
return 0;