# SrtToLrcConverterSimple

Converts `.srt` subtitle files into `.lrc` synchronized-lyrics files — the
format most music players use for scrolling lyrics. Available as both a
**WPF desktop app** and a **command-line tool**, sharing the same conversion
core.

## Features

- Batch-convert a whole folder (recursively) or a single `.srt` file.
- Html tag stripping: styled captions like `<i>Hello</i>` become `Hello`.
- Auto-generated/auto-translated caption suffixes (e.g. `_中文（自动翻译）`) are
  stripped from output filenames; the suffix list is user-editable.
- Multiple output encodings: UTF-8, UTF-8 with BOM, UTF-16 LE/BE, UTF-32, ANSI.
- Optional output folder override (default: same folder as the source file).
- Robust per-file handling: a bad/unparseable file is reported and skipped,
  never aborting the batch.
- LRC timestamps rendered as `[mm:ss.ff]`.

## Project layout

```
SrtToLrcConverterSimple.sln
└── src/
    ├── SrtToLrcConverterSimple.Core   library — SRT→LRC parsing, rendering,
    │                                   encoding & naming options
    ├── SrtToLrcConverterSimple.Cli    console app — batch-converts a folder
    └── SrtToLrcConverterSimple.Ui     WPF desktop app — folder/file picker,
                                        options, results list, LRC preview
```

All three projects target **.NET 10** and are referenced from
`SrtToLrcConverterSimple.sln`.

## Prerequisites

- .NET 10 SDK (`dotnet --list-sdks` should include `10.0.x`). Windows is
  required for the WPF UI; the CLI and Core work cross-platform.

## WPF UI

### Launch

```
dotnet run --project src/SrtToLrcConverterSimple.Ui
```

or open `SrtToLrcConverterSimple.sln` in Visual Studio 2022+ and press **F5**,
or run the built executable
`src/SrtToLrcConverterSimple.Ui/bin/Debug/net10.0-windows/SrtToLrcConverterSimple.Ui.exe`.

### Using the app

1. **Source** — choose **Folder (recursive)** or **Single file**, then click
   **Browse…** and pick the input. Folder mode converts every `.srt` nested in
   that folder; single-file mode converts just the chosen file.
2. **Options** — all optional:
   - *Output encoding*: the encoding used to read the SRT and write the LRC
     (UTF-8 by default).
   - *Output to a different folder*: tick to send all `.lrc` files to one
     destination; unticked, each `.lrc` is written next to its source.
   - *Filename suffixes to strip*: one per line. Every matched substring is
     removed from the output filename. Pre-filled with the common
     `_中文（自动翻译）` / `_中文（自动生成）` / `（自动生成）` suffixes.
3. Click **Convert**. The results list fills with one row per file:
   - `OK` — converted; selecting the row previews the generated LRC.
   - `Skipped` — no usable subtitles found; preview shows the reason.
   - `Failed` — file could not be read/converted; preview shows the error.
4. The bottom **status bar** tallies the outcome
   (`Converted n of m file(s). X failed. Y skipped.`), and the progress bar
   animates while work is in flight.

## CLI

### Usage

```
SrtToLrcConverterSimple.Cli <path to folder containing .srt files>
```

### Examples

```
# from the repo root
dotnet run --project src/SrtToLrcConverterSimple.Cli -- "C:\Music\Lyrics"
dotnet run --project src/SrtToLrcConverterSimple.Cli -- "C:\Music\Songs with spaces"
```

or call the built executable directly:

```
src\SrtToLrcConverterSimple.Cli\bin\Debug\net10.0\SrtToLrcConverterSimple.Cli.exe "C:\Music"
```

### Behaviour

- Found recursively: every file matching `*.srt` under the given folder.
- Each output `.lrc` is written next to its source (the CLI always uses the
  core's defaults: UTF-8 encoding, no output-folder override, built-in suffix
  strip list).
- Per-file progress is printed:

  ```
  Input directory: C:\Music\Lyrics
    happy_中文（自动翻译）.srt -> happy.lrc
    (3) Could not parse line, expecting from/to timestamps: ...
  Done. Converted 1 of 2 file(s).
  ```

  Lines in parentheses are parse warnings for the file above them; a skipped
  file prints `no subtitles parsed, skipped.` and a broken one
  `failed to convert - <message>`.
- Exit codes: `0` on success (even if some files were skipped), `1` on bad
  usage (missing argument, nonexistent folder).

## Shared conversion behaviour

- LRC timestamps are `[mm:ss.ff]`.
- Html tags are removed from caption text.
- Multi-line captions are joined into one lyrics line.
- Default output encoding is UTF-8.
- Default output location is the source file's folder (UI can override).
- To change CLI behaviour (encodings, output folder, suffix list), use the
  core options class `LrcConversionOptions` in `src/SrtToLrcConverterSimple.Core`.

## Build

```
dotnet build SrtToLrcConverterSimple.sln
```

## License

GPL-3.0 - see [LICENSE](LICENSE).