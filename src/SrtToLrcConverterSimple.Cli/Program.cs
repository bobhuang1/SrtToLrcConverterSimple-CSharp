using SrtToLrcConverter;

// Batch-converts .srt subtitle files to .lrc lyric files (the format used by many
// music players for synchronized lyrics), recursively through a given directory.

if (args.Length == 0)
{
    Console.WriteLine("Usage: SrtToLrcConverterSimple <path to folder containing .srt files>");
    return 1;
}

var pathToSrtFiles = args[0].Trim();
var dir = new DirectoryInfo(pathToSrtFiles);

if (string.IsNullOrEmpty(pathToSrtFiles) || !dir.Exists)
{
    Console.WriteLine($"Input directory does not exist: \"{pathToSrtFiles}\"");
    return 1;
}

Console.WriteLine($"Input directory: {pathToSrtFiles}");

var options = new LrcConversionOptions();
var converter = new SrtToLrcConverter.SrtToLrcConverter();

// Assumes discovery permissions for all folders under the specified path.
var fileList = dir.GetFiles("*" + options.InputExtension, SearchOption.AllDirectories);
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

    Console.WriteLine($"  {file.Name} -> {Path.GetFileName(result.OutputPath)}");

    foreach (var warning in result.Lrc?.Warnings ?? [])
    {
        Console.WriteLine($"      ({warning.Sequence}) {warning.Message}");
    }

    convertedCount++;
}

Console.WriteLine($"Done. Converted {convertedCount} of {fileList.Length} file(s).");
return 0;