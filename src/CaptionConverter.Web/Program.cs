using System.Text;
using CaptionConverter.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SubtitleConverter = SrtToLrcConverter.SrtToLrcConverter;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Makes Encoding.GetEncoding("gb18030" / "shift_jis" / "windows-1252" / ...) work
// in the browser so SubtitleTextDecoder's ANSI fallback decodes legacy subtitle
// files instead of silently mangling them as UTF-8.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.AddScoped(_ => new SubtitleConverter());

// Used only to fetch the bundled demo samples in wwwroot/samples.
builder.Services.AddScoped(sp => new HttpClient
    { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
