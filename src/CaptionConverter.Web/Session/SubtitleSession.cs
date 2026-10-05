using SrtToLrcConverter;
using SubtitleConverter = SrtToLrcConverter.SrtToLrcConverter;

namespace CaptionConverter.Web.Session;

/// <summary>
/// Holds the subtitle file the page is working on and the conversion result.
/// </summary>
/// <remarks>
/// Everything here is synchronous work over bytes that are already in memory,
/// which is what keeps it out of the components and testable: the UI only has to
/// hand over a file once, after which changing the legacy-encoding choice
/// re-converts the same bytes instead of asking the browser for the file again.
/// </remarks>
public sealed class SubtitleSession
{
    private readonly SubtitleConverter _converter;
    private readonly LrcConversionOptions _options = new();

    private byte[]? _sourceBytes;
    private string? _sourceName;
    private int _ansiChoice;

    public SubtitleSession(SubtitleConverter converter)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
    }

    /// <summary>The converted lyric file, or <see langword="null"/> when there is nothing to show.</summary>
    public string? Lrc { get; private set; }

    /// <summary>File name to offer when downloading <see cref="Lrc"/>.</summary>
    public string? SuggestedName { get; private set; }

    public IReadOnlyList<SrtWarning> Warnings { get; private set; } = Array.Empty<SrtWarning>();

    /// <summary>Format the parser actually detected, which is not necessarily the file extension.</summary>
    public SubtitleFormatKind Format { get; private set; }

    public int CueCount { get; private set; }

    /// <summary>Index into <see cref="AnsiEncodingChoices.All"/>; setting it re-converts the current file.</summary>
    public int AnsiChoice
    {
        get => _ansiChoice;
        set
        {
            if (_ansiChoice == value)
            {
                return;
            }

            _ansiChoice = value;
            Recalculate();
        }
    }

    /// <summary>Converts <paramref name="bytes"/>, replacing any earlier file and result.</summary>
    public void Load(string name, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(bytes);

        _sourceName = name;
        _sourceBytes = bytes;
        Recalculate();
    }

    /// <summary>Forgets the current file and its result, for example after a failed load.</summary>
    public void Clear()
    {
        _sourceName = null;
        _sourceBytes = null;
        Lrc = null;
        SuggestedName = null;
        Warnings = Array.Empty<SrtWarning>();
        CueCount = 0;
    }

    private void Recalculate()
    {
        if (_sourceBytes is null || _sourceName is null)
        {
            Clear();
            return;
        }

        _options.AnsiEncoding = AnsiEncodingChoices.Resolve(_ansiChoice);

        var text = _sourceBytes.Length == 0
            ? string.Empty
            : SubtitleTextDecoder.Decode(_sourceBytes, _options.AnsiEncoding);

        // Parse() detects from content only, so report the same format it used
        // rather than letting the filename influence the badge.
        Format = SubtitleFormat.Detect(string.Empty, text);

        var result = _converter.Parse(text);
        Lrc = result.Text;
        Warnings = result.Warnings;
        CueCount = CountCues(result.Text);
        SuggestedName = Path.GetFileName(_converter.GetOutputPath(_sourceName, _options));
    }

    /// <summary>Counts the timestamp lines in a rendered lyric file.</summary>
    public static int CountCues(string? lrc) =>
        lrc is null
            ? 0
            : lrc.Split((char)10).Count(line => line.StartsWith("[", StringComparison.Ordinal));
}
