// Rewrites the published <base href> so the Blazor app can be served from
// https://bobhuang1.github.io/<repo>/ instead of the domain root. A GitHub
// Pages project site is published under its repository name, so every asset
// URL is resolved against that base and would otherwise 404 at the root.
import { readFileSync, writeFileSync } from "node:fs";

const [indexPath, basePath] = process.argv.slice(2);

if (!indexPath || !basePath) {
    console.error("usage: node set-base-href.mjs <index.html> <base-path>");
    process.exit(1);
}

const quote = String.fromCharCode(34);
const normalized = basePath.endsWith("/") ? basePath : basePath + "/";
const html = readFileSync(indexPath, "utf8");

const start = html.indexOf("<base");
const end = start === -1 ? -1 : html.indexOf(">", start);

if (start === -1 || end === -1) {
    console.error("no <base> element found in " + indexPath);
    process.exit(1);
}

const updated = html.slice(0, start) + "<base href=" + quote + normalized + quote
    + " />" + html.slice(end + 1);

writeFileSync(indexPath, updated);
console.log("base href set to " + normalized + " in " + indexPath);

