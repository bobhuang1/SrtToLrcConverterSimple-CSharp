# SrtToLrcConverterSimple

Converts caption/subtitle files into `.lrc` synchronized-lyrics files — the
format most music players use for scrolling lyrics. Available as both a
**WPF desktop app** and a **command-line tool**, sharing the same conversion
core.

## Features

- Batch-convert a whole folder (recursively) or a single subtitle file.
- **8 input formats, auto-detected by content** (no extension guessing needed):
  | Format | Extensions |
  |---|---|
  | SubRip (SRT) | `.srt` |
  | WebVTT | `.vtt` |
  | SubStation Alpha (SSA/ASS) | `.ass`, `.ssa` |
  | SAMI | `.smi` |
  | MicroDVD (frame-based, configurable FPS) | `.sub` |
  | MPL2 (decisecond timings, continuation lines) | `.mpl2` |
  | PJS (Phoenix Japanimation) | `.pjs` |
  | TTML / DFXP (XML) | `.ttml`, `.dfxp` |
- Html tag stripping: styled captions like `<i>Hello</i>` become `Hello`
  (SSA/ASS `{\i1}` overrides stripped too).
- Auto-generated/auto-translated caption suffixes (e.g. `_中文（自动翻译）`) are
  stripped from output filenames; the suffix list is user-editable.
- **Auto-detect file encoding** (BOM, then UTF-8, then system ANSI fallback),
  or pin an explicit one: UTF-8, UTF-8 with BOM, UTF-16 LE/BE, UTF-32, ANSI.
- Optional output folder override (default: same folder as the source file).
- Robust per-file handling: a bad/unparseable file is reported and skipped,
  never aborting the batch.
- LRC timestamps rendered as `[mm:ss.ff]`.

## Project layout

```
SrtToLrcConverterSimple.sln
├── src/
│   ├── SrtToLrcConverterSimple.Core   library — subtitle parsing (8 formats),
│   │                                   content/encoding auto-detection, LRC
│   │                                   rendering & naming options
│   ├── SrtToLrcConverterSimple.Cli    console app — batch-converts a folder
│   └── SrtToLrcConverterSimple.Ui     WPF desktop app — folder/file picker,
│                                       options, results list, LRC preview
└── tests/
    └── SrtToLrcConverterSimple.Tests  xUnit — per-format parser, detection,
                                        rendering & encoding tests
```

All projects target **.NET 10** and are referenced from
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
   **Browse…** and pick the input. Folder mode converts every supported
   subtitle file nested in that folder; single-file mode converts just the
   chosen file.
2. **Options** — all optional:
   - *Output encoding*: *Auto-detect (recommended)* sniffs each file's BOM,
     then strict UTF-8, then the system ANSI codepage. Pick an explicit
     encoding to force the same one for every file. The LRC is written in the
     selected encoding (UTF-8 when auto-detect is in effect).
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
SrtToLrcConverterSimple.Cli <path to folder containing subtitle files>
```

Supported input formats: SRT, VTT, SSA/ASS, SAMI, MicroDVD, MPL2, PJS,
TTML/DFXP.

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

- Found recursively: every file with a supported subtitle extension under the
  given folder. Each file's format is detected from its content.
- Each output `.lrc` is written next to its source (the CLI always uses the
  core's defaults: auto-detect input encoding, UTF-8 output, no output-folder
  override, built-in suffix strip list).
- Per-file progress is printed, showing the detected format:

  ```
  Input directory: C:\Music\Lyrics
    happy_中文（自动翻译）.srt [SubRip (SRT)] -> happy.lrc
    outro.vtt [WebVTT] -> outro.lrc
    (3) Could not parse line, expecting from/to timestamps: ...
  Done. Converted 2 of 3 file(s).
  ```

  Lines in parentheses are parse warnings for the file above them; a skipped
  file prints `no subtitles parsed, skipped.` and a broken one
  `failed to convert - <message>`.
- Exit codes: `0` on success (even if some files were skipped), `1` on bad
  usage (missing argument, nonexistent folder).

## Shared conversion behaviour

- LRC timestamps are `[mm:ss.ff]`.
- Input format is always detected from content; a filename/extension is only a
  tie-breaker fallback.
- Html tags are removed from caption text; SSA/ASS override tags too.
- Multi-line captions are joined into one lyrics line.
- Input encoding auto-detect: BOM → strict UTF-8 → system ANSI; output is
  UTF-8 unless an explicit encoding is chosen.
- MicroDVD timing is frame-based (25 FPS default; a `{1}{1}fps` header line is
  honoured). MPL2 timings are deciseconds. Continuation lines in MPL2 and
  open-ended captions in SAMI/MicroDVD roll into the next cue's start.
- Default output location is the source file's folder (UI can override).
- To change CLI behaviour (encodings, output folder, suffix list), use the
  core options class `LrcConversionOptions` in `src/SrtToLrcConverterSimple.Core`.

## Build & test

```
dotnet build SrtToLrcConverterSimple.sln
dotnet test
```

## License

GPL-3.0 - see [LICENSE](LICENSE).