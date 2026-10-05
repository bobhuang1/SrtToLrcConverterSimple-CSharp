// Browser-only glue for the CaptionConverter demo. Everything the conversion
// needs already lives in the shared C# core; this module only hands a finished
// .lrc back to the user as a file.
export function downloadText(fileName, text) {
    // The core already appends its own trailing newline, so none is added here:
    // the saved bytes match exactly what the preview shows.
    const blob = new Blob([text], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    anchor.rel = "noopener";
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    // Let the browser start the download before revoking the object URL.
    setTimeout(() => URL.revokeObjectURL(url), 0);
}

