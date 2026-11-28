#!/usr/bin/env node
// Inlines demo.css and mock.css (so the page has no render-blocking request and previews fully styled) and writes the first screen of the demo window into index.html, so it is real HTML for search engines and for
// visitors without scripts. Run after changing demo.js or demo.css:  node site/prerender.js
const fs = require("fs"), path = require("path");
const demo = require("./demo.js");
const file = path.join(__dirname, "index.html");
const html = fs.readFileSync(file, "utf8");
const block = '<!--demo:start-->\n      <div class="gw gw-enter" id="gw" aria-hidden="true" inert>' + demo.render() + "</div>\n      <!--demo:end-->";
const css = ["demo.css", "mock.css"].map((f) => fs.readFileSync(path.join(__dirname, f), "utf8").trim()).join("\n");
const styled = html.replace(/<!--css:start-->[\s\S]*?<!--css:end-->/, () => "<!--css:start-->\n<style>\n" + css + "\n</style>\n<!--css:end-->");
const out = styled.replace(/<!--demo:start-->[\s\S]*?<!--demo:end-->/, () => block);
if (out === html) console.log("index.html already up to date");
const final = out.replace(/<!--mock:start-->[\s\S]*?<!--mock:end-->/, () => "<!--mock:start-->\n" + demo.mockHist() + "\n<!--mock:end-->");
fs.writeFileSync(file, final);
console.log("Prerendered the demo window (" + block.length + " bytes)");
