using System.Net;
using System.Text;
using Bunit;
using CaptionConverter.Web.Components;
using CaptionConverter.Web.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using SrtToLrcConverter;
using SubtitleConverter = SrtToLrcConverter.SrtToLrcConverter;

namespace CaptionConverter.Tests;

/// <summary>
/// Renders the browser demo in-process. These exist because a Razor parameter
/// binding can be wrong while the build stays clean and silent: passing
/// <c>Lrc="Session.Lrc"</c> without an <c>@</c> hands the panel the literal
/// text "Session.Lrc", which no compiler complains about and no unit test of the
/// conversion code would notice.
/// </summary>
public class WebDemoComponentTests : BunitContext
{
    private const string SampleSrt =
        "1" + "\r\n" +
        "00:00:01,000 --> 00:00:02,500" + "\r\n" +
        "First line" + "\r\n" + "\r\n" +
        "2" + "\r\n" +
        "00:00:03,000 --> 00:00:04,000" + "\r\n" +
        "Second line" + "\r\n";

    private readonly Uri _baseAddress = new("http://localhost/");
    private readonly SampleHandler _samples = new(Encoding.UTF8.GetBytes(WebDemoComponentTests.SampleSrt));

    public WebDemoComponentTests()
    {
        // The browser app registers this in Program.cs; the tests need it too,
        // for the legacy code pages the encoding picker offers.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Services.AddSingleton(_ => new SubtitleConverter());
        Services.AddSingleton(new HttpClient(_samples) { BaseAddress = _baseAddress });
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void Visit(string relativeUri)
    {
        Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>()
            .NavigateTo(new Uri(_baseAddress, relativeUri).ToString());
    }

    [Fact]
    public void The_page_shows_the_first_two_steps_and_no_result_yet()
    {
        var cut = Render<Home>();

        var headings = cut.FindAll("section.panel h2").Select(h => h.TextContent.Trim());

        Assert.Equal(["1 — Pick a subtitle file", "2 — Legacy text encoding"], headings);
        Assert.Empty(cut.FindAll(".lrc"));
        Assert.Empty(cut.FindAll(".status-error"));
    }

    [Fact]
    public void The_picker_offers_every_sample_and_encoding()
    {
        var cut = Render<Home>();

        Assert.Equal(3, cut.FindAll(".samples button").Count);
        Assert.Equal(6, cut.FindAll("select.select option").Count);
        Assert.Equal("Auto (BOM, then strict UTF-8)", cut.Find("select.select option").TextContent);
        Assert.Equal(
            ".srt,.vtt,.ass,.ssa,.smi,.sub,.mpl2,.pjs,.ttml,.dfxp",
            cut.Find("input[type=file]").GetAttribute("accept"));
    }

    [Fact]
    public void A_sample_deep_link_converts_and_shows_the_lyrics()
    {
        Visit("?sample=demo.srt");

        var cut = Render<Home>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Srt", cut.Find(".badge-format").TextContent.Trim());
            Assert.Equal("2 cue(s)", cut.FindAll(".badge")[1].TextContent.Trim());
            Assert.StartsWith("[00:01.00]First line", cut.Find(".lrc").TextContent, StringComparison.Ordinal);
            Assert.Equal("Download demo.lrc", cut.Find(".btn-primary").TextContent.Replace("\n", " ").Trim());
        });
    }

    [Fact]
    public void The_result_panel_shows_the_values_it_was_given()
    {
        // The regression this file exists for: these must be the values, not the
        // text "Lrc" and "SuggestedName".
        var cut = Render<ResultPanel>(parameters => parameters
            .Add(p => p.Lrc, "[00:01.00]First line")
            .Add(p => p.SuggestedName, "demo.lrc")
            .Add(p => p.Format, SubtitleFormatKind.Srt)
            .Add(p => p.CueCount, 2));

        Assert.Equal("[00:01.00]First line", cut.Find(".lrc").TextContent);
        Assert.Equal("Srt", cut.Find(".badge-format").TextContent.Trim());
        Assert.Equal("2 cue(s)", cut.FindAll(".badge")[1].TextContent.Trim());
        Assert.Contains("Download demo.lrc", cut.Find(".btn-primary").TextContent, StringComparison.Ordinal);

        // No attribute reached the panel as the literal text of the expression.
        Assert.DoesNotContain("SuggestedName", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_result_panel_lists_warnings_only_when_there_are_some()
    {
        var withoutWarnings = Render<ResultPanel>(parameters => parameters
            .Add(p => p.Lrc, "[00:01.00]First line")
            .Add(p => p.SuggestedName, "demo.lrc")
            .Add(p => p.Warnings, Array.Empty<SrtWarning>()));

        Assert.Empty(withoutWarnings.FindAll(".badge-warn"));
        Assert.Empty(withoutWarnings.FindAll("details.warnings"));

        var withWarnings = Render<ResultPanel>(parameters => parameters
            .Add(p => p.Lrc, "[00:01.00]First line")
            .Add(p => p.SuggestedName, "demo.lrc")
            .Add(p => p.Warnings, new[] { new SrtWarning(3, "Missing end time") }));

        Assert.Equal("1 warning(s)", withWarnings.Find(".badge-warn").TextContent.Trim());
        Assert.Contains("Missing end time", withWarnings.Find("details.warnings").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_picker_button_asks_the_page_for_that_sample()
    {
        string? requested = null;
        var cut = Render<FilePickerPanel>(parameters => parameters
            .Add(p => p.OnPickSample, name => requested = name));

        cut.FindAll(".samples button")[1].Click();

        Assert.Equal("demo.vtt", requested);
    }

    [Fact]
    public void The_picker_is_disabled_while_the_page_is_busy()
    {
        var enabled = Render<FilePickerPanel>(parameters => parameters.Add(p => p.OnPickSample, _ => { }));
        var disabled = Render<FilePickerPanel>(parameters => parameters
            .Add(p => p.OnPickSample, _ => { })
            .Add(p => p.Disabled, true));

        Assert.All(enabled.FindAll(".samples button"), button => Assert.False(button.HasAttribute("disabled")));
        Assert.All(disabled.FindAll(".samples button"), button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void Changing_the_encoding_select_writes_the_new_index_back()
    {
        var cut = Render<EncodingPanel>(parameters => parameters.Add(p => p.AnsiChoice, 1));

        cut.Find("select.select").Change("2");

        // An element @bind writes the value back to the property; the paired
        // AnsiChoiceChanged callback is the *component* binding convention, which
        // is how Home.razor raises it.
        Assert.Equal(2, cut.Instance.AnsiChoice);
    }

    [Fact]
    public void Changing_the_encoding_select_reaches_the_parent_through_the_binding()
    {
        var seen = new List<int>();
        var parent = Render<EncodingPanelHost>(parameters => parameters
            .Add(p => p.OnChanged, value => seen.Add(value)));

        parent.Find("select.select").Change("2");

        Assert.Equal([2], seen);
    }

    /// <summary>Binds the panel the way Home.razor does, so the two-way path is covered.</summary>
    private sealed class EncodingPanelHost : ComponentBase
    {
        [Parameter, EditorRequired] public EventCallback<int> OnChanged { get; set; }

        private int _choice;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<EncodingPanel>(0);
            builder.AddComponentParameter(1, nameof(EncodingPanel.AnsiChoice), _choice);
            builder.AddComponentParameter(2, nameof(EncodingPanel.AnsiChoiceChanged), EventCallback.Factory.Create<int>(this, OnChoiceChanged));
            builder.CloseComponent();
        }

        private async Task OnChoiceChanged(int value)
        {
            _choice = value;
            await OnChanged.InvokeAsync(value);
        }
    }

    [Fact]
    public void Changing_the_encoding_select_reconverts_the_loaded_file()
    {
        // A high byte that only one code page can read. Switching the fallback
        // must change the decoded text, which proves the session setter ran
        // through the component binding rather than only moving a label.
        var bytes = Encoding.GetEncoding("windows-1252")
            .GetBytes("1" + "\r\n" + "00:00:01,000 --> 00:00:02,500" + "\r\n" + "Caf" + (char)0xE9 + "\r\n");

        var cut = Render<Home>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, "song.srt"));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".lrc")));
        var before = cut.Find(".lrc").TextContent;

        cut.Find("select.select").Change("2"); // GB18030

        cut.WaitForAssertion(() => Assert.NotEqual(before, cut.Find(".lrc").TextContent));

        // Same bytes, so the cue count cannot move.
        Assert.Equal("1 cue(s)", cut.FindAll(".badge")[1].TextContent.Trim());
        Assert.Equal("Srt", cut.Find(".badge-format").TextContent.Trim());
    }

    [Fact]
    public void An_unlisted_sample_name_is_never_fetched()
    {
        Visit("?sample=../../Program.cs");
        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("select.select")));
        Assert.Empty(cut.FindAll(".lrc"));
        Assert.Empty(cut.FindAll(".status-error"));
        Assert.Empty(_samples.Requests);
    }

    [Fact]
    public void A_dropped_file_is_converted()
    {
        var cut = Render<Home>();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(SampleSrt, "song.srt"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Srt", cut.Find(".badge-format").TextContent.Trim());
            Assert.Equal("Download song.lrc", cut.Find(".btn-primary").TextContent.Replace("\n", " ").Trim());
        });
    }

    [Fact]
    public void A_file_over_the_size_limit_is_refused()
    {
        var cut = Render<Home>();

        // bUnit hands the component the file as a byte array, so the oversized
        // case needs a real one; 32 MB is the page's ceiling.
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary(new byte[32 * 1024 * 1024 + 1], "huge.srt"));

        cut.WaitForAssertion(() => Assert.Contains("larger than 32 MB", cut.Find(".status-error").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".lrc"));
    }

    [Fact]
    public void The_download_button_calls_the_javascript_module()
    {
        Visit("?sample=demo.srt");
        var module = JSInterop.SetupModule("./js/captionconverter.js");
        var cut = Render<Home>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".btn-primary")));

        cut.Find(".btn-primary").Click();

        // The module is imported lazily, then asked to write the file.
        Assert.Equal(["./js/captionconverter.js"], JSInterop.Invocations["import"].Select(i => i.Arguments[0]?.ToString()));

        var download = Assert.Single(module.Invocations["downloadText"]);
        Assert.Equal("demo.lrc", download.Arguments[0]?.ToString());
        Assert.Contains("First line", download.Arguments[1]?.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Serves the bundled samples from memory; records what was asked for.</summary>
    private sealed class SampleHandler(byte[] content) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var relative = request.RequestUri!.PathAndQuery.TrimStart('/');
            Requests.Add(relative);

            if (!relative.StartsWith("samples/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            });
        }
    }

}
