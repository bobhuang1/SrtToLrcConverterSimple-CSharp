using System.Text;
using SrtToLrcConverter;
using Xunit;

namespace CaptionConverter.Tests;

public class SubtitleFormatTests
{
    [Theory]
    [InlineData("1\n00:00:01,000 --> 00:00:04,000\nHello", SubtitleFormatKind.Srt)]
    [InlineData("WEBVTT\n\n00:01.500 --> 00:04.000\nHello", SubtitleFormatKind.Vtt)]
    [InlineData("[Script Info]\n[Events]\nFormat: Marked,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\nDialogue: 0,0:00:01.00,0:00:04.00,Default,,0,0,0,,Hello", SubtitleFormatKind.SsaAss)]
    [InlineData("<SAMI>\n<BODY>\n<SYNC Start=1000><P>Hello</P></SYNC>\n</BODY>\n</SAMI>", SubtitleFormatKind.Smi)]
    [InlineData("{0}{25}Hello", SubtitleFormatKind.MicroDvd)]
    [InlineData("[10][32]Hello", SubtitleFormatKind.Mpl2)]
    [InlineData("00:00:01.00:00:00:04.00:Hello", SubtitleFormatKind.Pjs)]
    [InlineData("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"00:00:01.000\" end=\"00:00:04.000\">Hello</p></div></body></tt>", SubtitleFormatKind.Ttml)]
    public void Detect_RecognisesEveryFormat(string content, SubtitleFormatKind expected) =>
        Assert.Equal(expected, SubtitleFormat.Detect(string.Empty, content));
}

public class SubtitleParserTests
{
    private const string SrtSample =
        "1\n00:00:01,000 --> 00:00:04,000\nHello world\n\n" +
        "2\n00:00:05,500 --> 00:00:08,000\n<i>Second</i> line\n";

    [Fact]
    public void Srt_ParsesEntriesAndStripsMarkup()
    {
        var warnings = new List<SrtWarning>();
        var subs = SubtitleParsers.Parse(SubtitleFormatKind.Srt, SrtSample, warnings);

        Assert.Empty(warnings);
        Assert.Equal(2, subs.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), subs[0].From);
        Assert.Equal(TimeSpan.FromSeconds(4), subs[0].To);
        Assert.Equal("Hello world", subs[0].Text);
        Assert.Equal("Second line", subs[1].Text);
    }

    [Fact]
    public void Vtt_ParsesHeaderBlockNotesAndCueSettings()
    {
        const string vtt =
            "WEBVTT - Some captions\nKind: captions\nLanguage: en\n\n" +
            "NOTE this is a comment\nthat spans lines\n\n" +
            "00:01.500 --> 00:04.000\nHello\n\n" +
            "cue-id\n00:00:02.000 --> 00:02:03.000 position:5% align:start\nWorld";

        var warnings = new List<SrtWarning>();
        var subs = SubtitleParsers.Parse(SubtitleFormatKind.Vtt, vtt, warnings);

        Assert.Empty(warnings);
        Assert.Equal(2, subs.Count);
        Assert.Equal(TimeSpan.FromSeconds(1.5), subs[0].From);
        Assert.Equal(TimeSpan.FromMilliseconds(123000), subs[1].To);
        Assert.Equal("Hello", subs[0].Text);
        Assert.Equal("World", subs[1].Text);
    }

    [Fact]
    public void Ass_ParsesDialogueWithCommasInsideText()
    {
        const string ass =
            "[Script Info]\nTitle: Test\n" +
            "[Events]\n" +
            "Format: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:01.00,0:00:04.00,Default,,0,0,0,,{\\i1}Hello \\Nworld\n" +
            "Dialogue: 0,0:00:05.20,0:00:08.60,Default,,0,0,0,,a, b, c\n";

        var warnings = new List<SrtWarning>();
        var subs = SubtitleParsers.Parse(SubtitleFormatKind.SsaAss, ass, warnings);

        Assert.Empty(warnings);
        Assert.Equal(2, subs.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), subs[0].From);
        Assert.Equal(TimeSpan.FromMilliseconds(5200), subs[1].From);
        Assert.Equal("Hello world", subs[0].Text);
        Assert.Equal("a, b, c", subs[1].Text);
    }

    [Fact]
    public void Ass_OutOfOrderDialogue_IsSortedByStartTime()
    {
        const string ass =
            "[Events]\n" +
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:05.00,0:00:06.00,Default,,0,0,0,,second\n" +
            "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,first\n" +
            "Dialogue: 1,0:00:05.00,0:00:06.00,Sign,,0,0,0,,second sign\n";

        var subs = SubtitleParsers.Parse(SubtitleFormatKind.SsaAss, ass, new List<SrtWarning>());

        Assert.Equal(["first", "second", "second sign"], subs.Select(s => s.Text));
    }

    [Fact]
    public void Smi_ClosesEachSyncAtNextSync()
    {
        const string smi =
            "<SAMI>\n<HEAD></HEAD>\n<BODY>\n" +
            "<SYNC Start=1000><P class=ENCC>Hello</P></SYNC>\n" +
            "<SYNC Start=2000><P class=ENCC>\nWorld&nbsp;&amp;more</P></SYNC>\n" +
            "</BODY>\n</SAMI>";

        var warnings = new List<SrtWarning>();
        var subs = SubtitleParsers.Parse(SubtitleFormatKind.Smi, smi, warnings);

        Assert.Empty(warnings);
        Assert.Equal(2, subs.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), subs[0].From);
        Assert.Equal(TimeSpan.FromSeconds(2), subs[0].To);
        Assert.Equal("Hello", subs[0].Text);
        Assert.Equal("World &more", subs[1].Text);
    }

    [Theory]
    [InlineData("{0}{25}Hello", 0.0, 1.0)]   // 25 fps default
    [InlineData("{1}{1}23.976\n{0}{24}Hi", 0.0, 1.0)] // fps header line
    public void MicroDvd_TurnsFramesIntoSeconds(string content, double expectedFrom, double expectedTo)
    {
        var warnings = new List<SrtWarning>();
        var subs = SubtitleParsers.Parse(SubtitleFormatKind.MicroDvd, content, warnings);

        Assert.Empty(warnings);
        Assert.Single(subs);
        Assert.Equal(expectedFrom, subs[0].From.TotalSeconds, 2);
        Assert.Equal(expectedTo, subs[0].To.TotalSeconds, 2);
        Assert.True(subs[0].Text.Length > 0);
    }

    [Fact]
    public void Mpl2_DecisecondsAndContinuationLines()
    {
        const string mpl2 =
            "[10][32]Hello\n" +
            "[40][-1]World";

        var warnings = new List<SrtWarning>();
        var subs = SubtitleParsers.Parse(SubtitleFormatKind.Mpl2, mpl2, warnings);

        Assert.Empty(warnings);
        Assert.Single(subs);
        Assert.Equal(1.0, subs[0].From.TotalSeconds, 3);
        Assert.Equal(4.0, subs[0].To.TotalSeconds, 3);
        Assert.Equal("Hello World", subs[0].Text);
    }

    [Fact]
    public void Pjs_ParsesBackToBackTimestamps()
    {
        var warnings = new List<SrtWarning>();
        var subs = SubtitleParsers.Parse(SubtitleFormatKind.Pjs, "00:00:01.00:00:00:04.00:Hello", warnings);

        Assert.Empty(warnings);
        Assert.Single(subs);
        Assert.Equal(TimeSpan.FromSeconds(1), subs[0].From);
        Assert.Equal(TimeSpan.FromSeconds(4), subs[0].To);
        Assert.Equal("Hello", subs[0].Text);
    }

    [Theory]
    [InlineData(
        "<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"00:00:01.000\" end=\"00:00:04.000\">Hello <span>world</span></p></div></body></tt>")]
    [InlineData(
        "<tt xmlns=\"http://www.w3.org/2005/11/ttaf1\" xmlns:tts=\"http://www.w3.org/2006/04/ttaf1#styling\"><body><div><p begin=\"1s\" end=\"4s\">Hi</p></div></body></tt>")]
    public void Ttml_SniffsNamespaceAndParses(string ttml)
    {
        Assert.Equal(SubtitleFormatKind.Ttml, SubtitleFormat.Detect(string.Empty, ttml));

        var warnings = new List<SrtWarning>();
        var subs = SubtitleParsers.Parse(SubtitleFormatKind.Ttml, ttml, warnings);

        Assert.Empty(warnings);
        Assert.Single(subs);
        Assert.True(subs[0].Text.Length > 0);
    }

    [Fact]
    public void MissingEndTimesRollIntoNextStart()
    {
        var warnings = new List<SrtWarning>();
        var subs = SubtitleParsers.Parse(SubtitleFormatKind.MicroDvd, "{0}{0}A\n{25}{50}B", warnings);

        var a = Assert.Single(subs, s => s.Text == "A");
        var b = Assert.Single(subs, s => s.Text == "B");
        Assert.Equal(TimeSpan.FromSeconds(1), a.To);
        Assert.Equal(TimeSpan.FromSeconds(2), b.To);
    }
}

public class LrcRenderTests
{
    private readonly SrtToLrcConverter.SrtToLrcConverter _converter = new();

    private const string SrtSample =
        "1\n00:00:01,000 --> 00:00:04,000\nHello world\n\n" +
        "2\n00:01:00,000 --> 00:01:05,000\n<b>Minute</b> marker\n";

    [Fact]
    public void Srt_RendersExpectedLrcTimestamps()
    {
        var lrc = _converter.Parse(SrtSample);

        // The renderer joins cues with Environment.NewLine, so the expected text
        // has to follow the host platform rather than assume Windows line endings.
        var nl = Environment.NewLine;
        Assert.Equal(
            "[00:01.00]Hello world" + nl + "[01:00.00]Minute marker" + nl + nl,
            lrc.Text);
    }

    [Fact]
    public void AutoDetect_RendersVttAsLrc()
    {
        const string vtt = "WEBVTT\n\n00:01.500 --> 00:04.000\nHello";
        var lrc = _converter.Parse(vtt);

        Assert.Equal("[00:01.50]Hello\r\n\r\n", lrc.Text);
    }
}

public class ConversionTests
{
    [Fact]
    public void ConvertFile_AutoDetectsEncodingBomAndFormat()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SrtToLrcTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var srtPath = Path.Combine(tempDir, "sample.srt");
            var srt = "1\n00:00:01,000 --> 00:00:04,000\nCafé 中文\n";
            File.WriteAllText(srtPath, srt, Encoding.Unicode); // UTF-16 LE with BOM

            var vttPath = Path.Combine(tempDir, "clip.vtt");
            File.WriteAllText(vttPath, "WEBVTT\n\n00:01.500 --> 00:04.000\nHello");

            var options = new LrcConversionOptions { OutputDirectory = tempDir };
            var converter = new SrtToLrcConverter.SrtToLrcConverter();

            var srtResult = converter.ConvertFile(srtPath, options);
            Assert.True(srtResult.Converted);
            Assert.Contains("Café 中文", srtResult.Lrc!.Text);

            var vttResult = converter.ConvertFile(vttPath, options);
            Assert.True(vttResult.Converted);
            Assert.Equal(SubtitleFormatKind.Vtt, vttResult.Format);
            Assert.Contains("[00:01.50]Hello", vttResult.Lrc!.Text);
            Assert.True(File.Exists(Path.Combine(tempDir, "clip.lrc")));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ConvertFile_SameOutputNameInOneBatch_GetsANumberedSuffix()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SrtToLrcTests_" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(Path.Combine(tempDir, "a"));
        Directory.CreateDirectory(Path.Combine(tempDir, "b"));
        Directory.CreateDirectory(outDir);
        try
        {
            var first = Path.Combine(tempDir, "a", "song.srt");
            var second = Path.Combine(tempDir, "b", "song.srt");
            File.WriteAllText(first, "1\n00:00:01,000 --> 00:00:02,000\nfrom a\n");
            File.WriteAllText(second, "1\n00:00:01,000 --> 00:00:02,000\nfrom b\n");

            var converter = new SrtToLrcConverter.SrtToLrcConverter();
            var options = new LrcConversionOptions { OutputDirectory = outDir };
            var claimed = SrtToLrcConverter.SrtToLrcConverter.NewOutputSet();

            var r1 = converter.ConvertFile(first, options, claimed);
            var r2 = converter.ConvertFile(second, options, claimed);

            Assert.Equal(Path.Combine(outDir, "song.lrc"), r1.OutputPath);
            Assert.Equal(Path.Combine(outDir, "song (2).lrc"), r2.OutputPath);
            Assert.Contains("from a", File.ReadAllText(r1.OutputPath));
            Assert.Contains("from b", File.ReadAllText(r2.OutputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ConvertFile_FallsBackToAnsiForNonUtf8Bytes()
    {
        using var _ = new CultureScope("en-US");
        var tempDir = Path.Combine(Path.GetTempPath(), "SrtToLrcTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "latin.srt");
            var bytes = Encoding.Latin1.GetBytes("1\n00:00:01,000 --> 00:00:04,000\nCafé\n");
            File.WriteAllBytes(path, bytes);

            var converter = new SrtToLrcConverter.SrtToLrcConverter();
            var result = converter.ConvertFile(path, new LrcConversionOptions { OutputDirectory = tempDir });

            Assert.True(result.Converted);
            // Must be the real character, not "Caf" plus a U+FFFD replacement character.
            Assert.Contains("Café", result.Lrc!.Text);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

public class EncodingAndTimingTests
{
    [Fact]
    public void Decode_FallsBackToTheCultureAnsiCodePage_ForGbkBytes()
    {
        using var _ = new CultureScope("zh-CN");
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var gbk = System.Text.Encoding.GetEncoding(936).GetBytes("1\n00:00:01,000 --> 00:00:04,000\n你好世界\n");

        var text = SubtitleTextDecoder.Decode(gbk);

        Assert.Contains("你好世界", text);
        Assert.DoesNotContain('�', text);
    }

    [Theory]
    [InlineData("00:00:01.500", 1500)]
    [InlineData("00:00:01.5", 1500)]
    [InlineData("00:00:01.05", 1050)]
    [InlineData("00:01:02.1234567", 62123)]
    [InlineData("1.25s", 1250)]
    [InlineData("750ms", 750)]
    public void Ttml_Fractional_Times_Are_Milliseconds(string begin, int expectedMs)
    {
        var ttml = $"<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"{begin}\" end=\"00:00:09.000\">Hi</p></div></body></tt>";
        var warnings = new List<SrtWarning>();

        var subs = SubtitleParsers.Parse(SubtitleFormatKind.Ttml, ttml, warnings);

        Assert.Single(subs);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), subs[0].From);
    }
}

internal sealed class CultureScope : IDisposable
{
    private readonly System.Globalization.CultureInfo _previous = System.Globalization.CultureInfo.CurrentCulture;

    public CultureScope(string name) =>
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);

    public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = _previous;
}
public class AnsiEncodingOverrideTests
{
    [Fact]
    public void Decode_UsesExplicitAnsiEncodingInsteadOfTheCultureDefault()
    {
        // "Caf" + e-acute in Latin-1. The lone 0xE9 byte is not valid UTF-8,
        // so decoding always reaches the ANSI fallback. Passing the codepage
        // explicitly makes the outcome independent of the machine culture,
        // which is what the browser host needs: WebAssembly has no real
        // system code page to infer.
        var bytes = Encoding.Latin1.GetBytes("Caf" + (char)233);

        Assert.Equal("Caf" + (char)233, SubtitleTextDecoder.Decode(bytes, Encoding.Latin1));
    }

    [Fact]
    public void ConvertFile_HonoursExplicitAnsiEncoding()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SrtToLrcTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "latin.srt");
            var text = "Caf" + (char)233;
            File.WriteAllBytes(path, Encoding.Latin1.GetBytes("1\n00:00:01,000 --> 00:00:04,000\n" + text + "\n"));

            var converter = new SrtToLrcConverter.SrtToLrcConverter();
            var result = converter.ConvertFile(path, new LrcConversionOptions
            {
                OutputDirectory = tempDir,
                AnsiEncoding = Encoding.Latin1,
            });

            Assert.True(result.Converted);
            Assert.Contains(text, result.Lrc!.Text);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

