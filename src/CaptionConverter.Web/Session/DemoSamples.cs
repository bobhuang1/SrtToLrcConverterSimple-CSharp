namespace CaptionConverter.Web.Session;

/// <summary>A subtitle file bundled in <c>wwwroot/samples</c> that the demo can load.</summary>
/// <param name="Name">File name as served by the site.</param>
/// <param name="Label">Button text.</param>
public sealed record DemoSample(string Name, string Label);

/// <summary>The bundled samples offered by the picker.</summary>
public static class DemoSamples
{
    public static readonly DemoSample[] All =
    [
        new("demo.srt", "SubRip (.srt)"),
        new("demo.vtt", "WebVTT (.vtt)"),
        new("demo.ass", "SubStation (.ass)"),
    ];

    /// <summary>
    /// Whether <paramref name="name"/> is one of the bundled samples. The
    /// <c>?sample=</c> deep link is answered from the query string, so this is
    /// the allow-list that stops it being used to fetch an arbitrary path.
    /// </summary>
    public static bool IsBundled(string? name) =>
        name is not null
        && All.Any(sample => string.Equals(sample.Name, name, StringComparison.Ordinal));
}
