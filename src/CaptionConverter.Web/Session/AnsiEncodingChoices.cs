using System.Text;

namespace CaptionConverter.Web.Session;

/// <summary>One entry in the legacy-encoding picker.</summary>
/// <param name="Label">Text shown in the picker.</param>
/// <param name="CodePage">
/// Encoding name to fall back on, or <see langword="null"/> for "let the
/// decoder decide" (BOM, then strict UTF-8, then the runtime default).
/// </param>
public sealed record AnsiEncodingChoice(string Label, string? CodePage);

/// <summary>
/// The legacy-encoding fallbacks the demo offers. Kept out of the markup so the
/// picker and the converter always agree on the same list.
/// </summary>
public static class AnsiEncodingChoices
{
    public static readonly AnsiEncodingChoice[] All =
    [
        new("Auto (BOM, then strict UTF-8)", null),
        new("Windows-1252 - Western European", "windows-1252"),
        new("GB18030 - Simplified Chinese", "gb18030"),
        new("Big5 - Traditional Chinese", "big5"),
        new("Shift-JIS - Japanese", "shift_jis"),
        new("EUC-KR - Korean", "euc-kr"),
    ];

    /// <summary>Turns a picker index into the encoding it stands for.</summary>
    /// <returns>
    /// <see langword="null"/> for the automatic option, for an out-of-range index,
    /// and for a code page this runtime has no tables for &mdash; in all three
    /// cases the converter falls back to its own sniffing.
    /// </returns>
    public static Encoding? Resolve(int index)
    {
        var codePage = index >= 0 && index < All.Length
            ? All[index].CodePage
            : null;

        if (codePage is null)
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception)
        {
            // No codepage tables in this runtime; Encoding.Default then applies.
            return null;
        }
    }
}
