using System.Text;
using CaptionConverter.Web.Session;
using SrtToLrcConverter;
using SubtitleConverter = SrtToLrcConverter.SrtToLrcConverter;

namespace CaptionConverter.Tests;

/// <summary>
/// Covers the browser demo's conversion plumbing, which used to live inside
/// Home.razor and so had no test at all. Everything here is pure C# over bytes
/// already in memory, so none of it needs a browser or a WebAssembly runtime.
/// </summary>
public class WebSessionTests
{
    private const string TwoCueSrt =
        "1" + "\r\n" +
        "00:00:01,000 --> 00:00:02,500" + "\r\n" +
        "First line" + "\r\n" + "\r\n" +
        "2" + "\r\n" +
        "00:00:03,000 --> 00:00:04,000" + "\r\n" +
        "Second line" + "\r\n";

    private static SubtitleSession NewSession() => new(new SubtitleConverter());

    [Fact]
    public void Load_reports_the_format_the_parser_detected()
    {
        var session = NewSession();

        session.Load("lying.srt", Encoding.UTF8.GetBytes(TwoCueSrt));

        Assert.Equal(SubtitleFormatKind.Srt, session.Format);
    }

    [Fact]
    public void Load_detects_from_content_not_from_the_file_name()
    {
        var session = NewSession();

        // The name claims VTT; the content is SRT, and the badge follows the parser.
        session.Load("demo.txt", Encoding.UTF8.GetBytes(TwoCueSrt));

        Assert.Equal(SubtitleFormatKind.Srt, session.Format);
    }

    [Fact]
    public void Load_counts_cues_and_suggests_an_lrc_name()
    {
        var session = NewSession();

        session.Load("demo.srt", Encoding.UTF8.GetBytes(TwoCueSrt));

        Assert.Equal(2, session.CueCount);
        Assert.Equal("demo.lrc", session.SuggestedName);
        Assert.NotNull(session.Lrc);
        Assert.Contains("First line", session.Lrc, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_accepts_an_empty_file_without_inventing_cues()
    {
        var session = NewSession();

        session.Load("empty.srt", []);

        Assert.Equal(0, session.CueCount);
        Assert.True(string.IsNullOrWhiteSpace(session.Lrc));
    }

    [Fact]
    public void Changing_the_encoding_choice_reconverts_the_same_bytes()
    {
        var session = NewSession();
        session.Load("demo.srt", Encoding.UTF8.GetBytes(TwoCueSrt));
        var auto = session.Lrc;

        session.AnsiChoice = 1;
        var asWindows1252 = session.Lrc;

        // Same file, so the cue count cannot move; only the text can.
        Assert.Equal(2, session.CueCount);
        Assert.Equal("demo.lrc", session.SuggestedName);

        session.AnsiChoice = 0;
        Assert.Equal(auto, session.Lrc);
        Assert.NotNull(asWindows1252);
    }

    [Fact]
    public void Setting_the_encoding_choice_before_a_file_does_nothing_yet()
    {
        var session = NewSession();

        session.AnsiChoice = 2;

        Assert.Equal(2, session.AnsiChoice);
        Assert.Null(session.Lrc);

        session.Load("demo.srt", Encoding.UTF8.GetBytes(TwoCueSrt));

        Assert.Equal(2, session.CueCount);
    }

    [Fact]
    public void Setting_the_same_encoding_choice_twice_is_a_no_op()
    {
        var session = NewSession();
        session.Load("demo.srt", Encoding.UTF8.GetBytes(TwoCueSrt));

        session.AnsiChoice = 0;
        session.AnsiChoice = 0;

        Assert.Equal(2, session.CueCount);
    }

    [Fact]
    public void Clear_drops_the_result_but_keeps_the_encoding_choice()
    {
        var session = NewSession();
        session.AnsiChoice = 1;
        session.Load("demo.srt", Encoding.UTF8.GetBytes(TwoCueSrt));

        session.Clear();

        Assert.Null(session.Lrc);
        Assert.Null(session.SuggestedName);
        Assert.Empty(session.Warnings);
        Assert.Equal(0, session.CueCount);
        Assert.Equal(1, session.AnsiChoice);
    }

    [Theory]
    [InlineData("[00:01.50]Hello", 1)]
    [InlineData("[00:01.50]A" + "\r\n" + "[00:02.50]B", 2)]
    [InlineData("Hello", 0)]
    [InlineData(null, 0)]
    public void CountCues_counts_only_timestamp_lines(string? lrc, int expected) =>
        Assert.Equal(expected, SubtitleSession.CountCues(lrc));

    [Fact]
    public void Only_bundled_samples_are_accepted_by_the_deep_link()
    {
        Assert.All(DemoSamples.All, sample => Assert.True(DemoSamples.IsBundled(sample.Name)));

        Assert.False(DemoSamples.IsBundled(null));
        Assert.False(DemoSamples.IsBundled(""));
        Assert.False(DemoSamples.IsBundled("../secrets.env"));
        Assert.False(DemoSamples.IsBundled("Demo.SRT"));
    }

    [Fact]
    public void Bundled_sample_names_are_unique()
    {
        var names = DemoSamples.All.Select(sample => sample.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.DoesNotContain('/', name));
    }

    [Fact]
    public void The_automatic_encoding_choice_resolves_to_nothing_to_override()
    {
        Assert.Null(AnsiEncodingChoices.Resolve(0));
        Assert.Null(AnsiEncodingChoices.All[0].CodePage);
    }

    [Fact]
    public void An_out_of_range_encoding_choice_falls_back_to_automatic()
    {
        Assert.Null(AnsiEncodingChoices.Resolve(-1));
        Assert.Null(AnsiEncodingChoices.Resolve(AnsiEncodingChoices.All.Length));
    }

    [Fact]
    public void Every_encoding_choice_has_a_label_and_a_code_page_except_auto()
    {
        Assert.All(AnsiEncodingChoices.All, choice => Assert.False(string.IsNullOrWhiteSpace(choice.Label)));

        for (var i = 1; i < AnsiEncodingChoices.All.Length; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(AnsiEncodingChoices.All[i].CodePage));
        }
    }

    [Fact]
    public void A_named_code_page_resolves_to_an_encoding_when_tables_are_available()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var windows1252 = AnsiEncodingChoices.Resolve(1);

        Assert.NotNull(windows1252);
        Assert.Equal(1252, windows1252!.CodePage);
    }
}
