// Post-deploy smoke check for the live GitHub Pages site.
//
// A plain HTTP 200 on index.html proves nothing: while a Pages deploy
// propagates, different CDN edges can serve 200 and 503 at the same
// time. So this drives a real headless Chrome over the DevTools
// protocol and waits until the Blazor WebAssembly app has actually
// rendered its sample buttons, then asserts the converted result.
// It exits non-zero if the app does not render (or a check fails)
// within a bounded timeout, so a propagation failure surfaces in CI
// instead of an hour later.
//
// Usage: node tools/smoke.mjs <site-url>
//   The ?sample=demo.srt deep link is appended when the URL carries
//   no query string of its own.
// Env:
//   SMOKE_TIMEOUT_MS  bounded wait for the app to render (default 150000)
//   CHROME_PATH       Chrome executable to launch (default: per-platform)
import { existsSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawn } from "node:child_process";

const [siteUrl] = process.argv.slice(2);
const timeoutMs = Number(process.env.SMOKE_TIMEOUT_MS || 150000);
const pollMs = 1000;

if (!siteUrl) {
    console.error("usage: node smoke.mjs <site-url>");
    process.exit(1);
}

// GitHub Pages project sites serve from /<repo>/ and the deep link is
// answered from the query string, so the trailing slash matters.
const baseUrl = siteUrl.endsWith("/") ? siteUrl : siteUrl + "/";
const url = baseUrl.includes("?") ? baseUrl : baseUrl + "?sample=demo.srt";

function findChrome() {
    if (process.env.CHROME_PATH) {
        return process.env.CHROME_PATH;
    }

    if (process.platform === "win32") {
        const candidates = [
            "C:/Program Files/Google/Chrome/Application/chrome.exe",
            "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe",
        ];
        const found = candidates.find((path) => existsSync(path));
        if (found) {
            return found;
        }

        throw new Error("no Chrome found; set CHROME_PATH");
    }

    // GitHub-hosted Ubuntu runners ship Google Chrome on the PATH.
    return "google-chrome";
}

const userDir = mkdtempSync(join(tmpdir(), "smoke-chrome-"));
const chrome = spawn(findChrome(), [
    "--headless=new",
    // Port 0 makes Chrome pick a free port and print the DevTools
    // WebSocket URL on stderr, so parallel runs never collide.
    "--remote-debugging-port=0",
    "--user-data-dir=" + userDir,
    "--no-sandbox",
    "--disable-dev-shm-usage",
    "about:blank",
], { stdio: ["ignore", "ignore", "pipe"] });

function connect(wsUrl) {
    return new Promise((resolve, reject) => {
        const ws = new WebSocket(wsUrl);
        let id = 0;
        const pending = new Map();
        ws.addEventListener("open", () => resolve({
            send(method, params, sessionId) {
                const msgId = ++id;
                const message = { id: msgId, method, params };
                if (sessionId) {
                    message.sessionId = sessionId;
                }

                ws.send(JSON.stringify(message));
                return new Promise((res, rej) => pending.set(msgId, { res, rej }));
            },
            close() { ws.close(); },
        }));
        ws.addEventListener("error", () => reject(new Error("DevTools WebSocket failed")));
        ws.addEventListener("message", (ev) => {
            const msg = JSON.parse(ev.data);
            if (msg.id && pending.has(msg.id)) {
                const { res, rej } = pending.get(msg.id);
                pending.delete(msg.id);
                if (msg.error) {
                    rej(new Error(msg.method + " " + JSON.stringify(msg.error)));
                } else {
                    res(msg.result);
                }
            }
        });
    });
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const results = [];
function check(name, passed, detail) {
    results.push(passed);
    console.log((passed ? "PASS" : "FAIL") + "  " + name + (detail ? "  " + detail : ""));
}

async function finish(code) {
    chrome.kill();
    try {
        rmSync(userDir, { recursive: true, force: true });
    } catch {
        // Already gone.
    }

    process.exit(code);
}

try {
    // A missing Chrome binary surfaces as an 'error' event rather than
    // a non-zero exit, so race it into the same wait instead of crashing
    // on an unhandled event.
    const launchError = new Promise((_, reject) => {
        chrome.on("error", (err) => reject(new Error("could not launch Chrome: " + err.message)));
    });
    const listening = await Promise.race([
        launchError,
        new Promise((resolve, reject) => {
            let stderr = "";
            const timer = setTimeout(
                () => reject(new Error("Chrome did not expose a DevTools endpoint")),
                30000,
            );
            chrome.stderr.on("data", (chunk) => {
                stderr += chunk.toString();
                const match = stderr.match(/DevTools listening on (ws:\/\/\S+)/);
                if (match) {
                    clearTimeout(timer);
                    resolve(match[1]);
                }
            });
            chrome.on("exit", (code) => reject(new Error("Chrome exited early with code " + code)));
        }),
    ]);

    const cdp = await connect(listening);
    const { targetId } = await cdp.send("Target.createTarget", { url });
    const { sessionId } = await cdp.send("Target.attachToTarget", { targetId, flatten: true });
    const page = (method, params) => cdp.send(method, params, sessionId);
    await page("Page.enable");
    await page("Runtime.enable");

    async function evaluate(expression) {
        const r = await page("Runtime.evaluate", { expression, returnByValue: true });
        if (r.exceptionDetails) {
            throw new Error((r.exceptionDetails.text || "page exception") + ": " + expression);
        }

        return r.result.value;
    }

    async function waitFor(expression, ms) {
        const deadline = Date.now() + ms;
        for (;;) {
            if (chrome.exitCode !== null) {
                throw new Error("Chrome exited early with code " + chrome.exitCode);
            }

            try {
                if (await evaluate(expression)) {
                    return true;
                }
            } catch {
                // The page is navigating and its context is being torn
                // down; the next poll picks up the new one.
            }

            if (Date.now() >= deadline) {
                return false;
            }

            await sleep(pollMs);
        }
    }

    const badges = "JSON.stringify({"
        + " format: (document.querySelector('.badge-format') || {}).textContent?.trim() || null,"
        + " cues: (document.querySelectorAll('.badge')[1] || {}).textContent?.trim() || null"
        + "})";

    // The sample buttons are disabled while a conversion is in flight,
    // and clicking a disabled button is a silent no-op, so readiness
    // means rendered AND idle.
    const ready = "document.querySelector('select.select option')"
        + " && document.querySelectorAll('.samples button').length === 3"
        + " && !document.querySelector('.samples button').disabled";

    const rendered = await waitFor(ready, timeoutMs);
    check("demo renders its sample buttons", rendered,
        rendered ? "" : "not rendered within " + timeoutMs + "ms");
    if (!rendered) {
        await finish(2);
    }

    const deepLink = JSON.parse(await evaluate(badges));
    check("?sample=demo.srt is detected as Srt", deepLink.format === "Srt", "format=" + deepLink.format);
    check("demo.srt converts to 4 cue(s)", deepLink.cues === "4 cue(s)", "cues=" + deepLink.cues);

    // The bundled samples must convert, not just render: switch to the
    // WebVTT sample and wait for the badges to update.
    await evaluate("document.querySelectorAll('.samples button')[1].click()");
    const vttRendered = await waitFor(
        "(document.querySelector('.badge-format') || {}).textContent?.trim() === 'Vtt'",
        timeoutMs,
    );
    const vtt = JSON.parse(await evaluate(badges));
    check("WebVTT sample converts to Vtt / 2 cue(s)",
        vttRendered && vtt.cues === "2 cue(s)", JSON.stringify(vtt));

    await finish(results.every(Boolean) ? 0 : 1);
} catch (err) {
    console.error("SMOKE FAILED: " + err.message);
    await finish(1);
}
