# SrtToLrcConverterSimple

A small .NET command-line tool that batch-converts `.srt` subtitle files into
`.lrc` synchronized-lyrics files (the format most music players use for
scrolling lyrics), recursively through a folder.

## Usage

```
SrtToLrcConverterSimple <path to folder containing .srt files>
```

Every `.srt` file found under that folder (recursively) is converted to a
sibling `.lrc` file with the same name. HTML tags embedded in subtitle text
(e.g. `<b>`, `<i>` from styled captions) are stripped. One bad/unparseable
file is skipped with a message rather than aborting the whole batch.

If your subtitles come from a source that appends a fixed suffix to auto-generated
filenames (e.g. an auto-translation export), edit the
`KnownFilenameSuffixesToStrip` array in `Program.cs` to have it stripped from
the output filename.

## Build & run

```
dotnet build
dotnet run -- "C:\Path\To\SrtFiles"
```

Targets .NET 8.

## License

GPL-3.0 - see [LICENSE](LICENSE).
