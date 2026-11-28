/* The GitWolf window on the home page: a small, working copy of the app with made-up repositories.
 * Everything you see can be used: switch repositories, branches and tabs, stage and commit, fetch, pull,
 * push, stash, merge the pull request, search, jump with the command palette.
 *
 * It is plain JavaScript with no dependencies. `site/prerender.js` runs `render()` in Node and writes the
 * first screen into index.html, so the window is real HTML for search engines and for visitors without
 * scripts. In a browser this file only attaches the behaviour.
 */
(function (root) {
  "use strict";

  /* ───────────── Sample data ───────────── */

  var AUTHORS = ["Ada", "Sam", "Lin"];
  var EMAIL = ["ada@example.com", "sam@example.com", "lin@example.com"];

  function C(h, p, s, a, m, files, o) {
    o = o || {};
    return { h: h, p: p, s: s, a: a, m: m, body: o.body || "", files: files || [] };
  }
  function F(path, st, diff) { return { p: path, st: st, d: diff }; }

  function makeRepos() {
    var gw = {
      id: "GW", name: "GitWolf", icon: true, ci: "ok", cur: "main",
      heads: { main: "a1f09c2", "fix/rail-badge": "4be07a9", "feature/welcome": "e2d6b40" },
      remote: { main: "d93b6f4" },
      tags: { "v0.2.0": "d93b6f4", "v0.1.2": "b6d2f08", "v0.1.1": "2e8a5b6" },
      commits: [
        C("a1f09c2", ["d93b6f4"], "feat: first-run welcome", 0, 2, [
          F("apple/Sources/GitWolfApp/WelcomeSheet.swift", "A", "@@ -0,0 +1,9 @@\n+import SwiftUI\n+\n+/// Shown once, the first time GitWolf opens.\n+struct WelcomeSheet: View {\n+    var body: some View {\n+        VStack(spacing: 18) {\n+            Text(\"Welcome to GitWolf\")\n+        }\n+    }"),
          F("apple/Sources/GitWolfApp/GitWolfApp.swift", "M", "@@ -226,6 +226,8 @@\n         case .account: AddAccountSheet()\n-        // no first-run flow\n+        case .welcome: WelcomeSheet(finish: dismiss)\n+            .tint(Theme.brand)\n     }")
        ], { body: "Shows a short walkthrough the first time the app opens: where repositories live, how to pick a look, and how to connect an account." }),
        C("4be07a9", ["d93b6f4"], "fix: stop the rail clipping badges", 1, 60, [
          F("apple/Sources/GitWolfApp/Views/RepoRail.swift", "M", "@@ -48,7 +48,7 @@\n             }\n-            .frame(maxWidth: .infinity)\n+            .frame(maxWidth: .infinity)\n+            .padding(.vertical, 6)\n         }")
        ], { body: "The CI mark and the change badge stick out 4 pt past a tile, and the scroll view clipped them." }),
        C("e2d6b40", ["d93b6f4"], "feat: welcome sheet draft", 0, 300, [
          F("apple/Sources/GitWolfApp/WelcomeSheet.swift", "A", "@@ -0,0 +1,4 @@\n+import SwiftUI\n+\n+// Draft: first-run welcome\n+struct WelcomeSheet: View { var body: some View { Text(\"Hello\") } }")
        ]),
        C("d93b6f4", ["c07d9b1"], "feat: Windows updates and CI controls", 2, 420, [
          F("windows/GitWolf.App/UpdateService.cs", "A", "@@ -0,0 +1,6 @@\n+public sealed class UpdateService\n+{\n+    public async Task CheckAsync()\n+    {\n+        var feed = await Feed.LoadAsync(\"appcast-windows.xml\");\n+    }"),
          F("windows/GitWolf.App/CiPanel.xaml", "M", "@@ -12,3 +12,4 @@\n     <StackPanel Spacing=\"8\">\n+        <Button Content=\"Re-run\" />\n     </StackPanel>")
        ], { body: "Windows now checks for updates from the same feed host and lets you re-run or cancel CI from the app." }),
        C("c07d9b1", ["3fa81c4"], "feat: new wolf logo as SVG", 0, 1500, [
          F("assets/gitwolf-mark.svg", "A", "@@ -0,0 +1,3 @@\n+<svg viewBox=\"0 0 64 64\" xmlns=\"http://www.w3.org/2000/svg\">\n+  <path d=\"M8 12l12 8\" fill=\"#fff\"/>\n+</svg>"),
          F("assets/logo.png", "D", "@@ -1,1 +0,0 @@\n-(binary file removed)")
        ]),
        C("3fa81c4", ["b6d2f08"], "fix: pause CI lookups after a refusal", 0, 1700, [
          F("apple/Sources/GitWolfApp/Views/ChecksSection.swift", "M", "@@ -90,6 +90,9 @@\n         let results = try await fetch()\n+        if status == 403 { pause(for: .hour) }\n         return results")
        ]),
        C("b6d2f08", ["2e8a5b6"], "chore: release 0.1.2", 1, 2900, [
          F("CHANGELOG.md", "M", "@@ -1,4 +1,6 @@\n # Changelog\n+\n+## [0.1.2]\n-version 0.1.1\n+version 0.1.2")
        ]),
        C("2e8a5b6", ["f0a11c9"], "feat: stage by line", 2, 5800, [
          F("apple/Sources/GitWolfCore/Patch.swift", "A", "@@ -0,0 +1,3 @@\n+/// Builds a patch for the selected lines only.\n+struct PatchBuilder {\n+}")
        ]),
        C("f0a11c9", [], "chore: first commit", 1, 8600, [
          F("README.md", "A", "@@ -0,0 +1,2 @@\n+# GitWolf\n+A native Git client.")
        ])
      ],
      changes: [
        { p: "site/index.html", st: "M", staged: false, d: "@@ -137,6 +137,9 @@\n     .row.sel { background: var(--select); font-weight: 600; }\n-    .hist { padding: 6px 0; }\n+    .hist { padding: 6px 0; overflow: auto; }\n+    .gw-hash { font: 600 11px ui-monospace, Menlo, monospace; }\n+    .gw-demo { cursor: pointer; }\n     .commit { position: relative; }" },
        { p: "README.md", st: "M", staged: false, d: "@@ -8,3 +8,4 @@\n Install with Homebrew:\n \n     brew install --cask ipuppyyt/gitwolf/gitwolf\n+Or download the .dmg from the latest release." }
      ],
      stashes: [],
      incoming: [C("8c5d2a7", ["d93b6f4"], "fix: debounce the file watcher", 2, 1, [
        F("apple/Sources/GitWolfCore/Watcher.swift", "M", "@@ -22,4 +22,5 @@\n     func start() {\n+        debounce = 0.25\n         stream.start()\n     }")
      ])],
      prs: [{ n: 42, t: "Welcome sheet for the first run", by: 0, branch: "feature/welcome", state: "open", checks: "ok", m: 280,
        body: "Adds a short first-run walkthrough: where repositories live, how to choose a look, and how to connect an account. It shows once and can be skipped." }],
      issues: [
        { n: 21, t: "Support Linux", by: 1, state: "open", m: 4300, body: "A native GTK build that matches the Mac app. Needs the engine tests ported first." },
        { n: 17, t: "Rail badges are clipped on small icons", by: 2, state: "open", m: 2600, body: "The CI mark is cut off at the top right when the rail is scrolled to the first tile." },
        { n: 12, t: "Open a repository from the terminal", by: 0, state: "closed", m: 11000, body: "Fixed in 0.2.0: the gitwolf command opens a folder." }
      ],
      notes: [
        { id: "n1", t: "CI passed on main", s: "GitWolf · 12 min ago", read: false },
        { id: "n2", t: "Ada asked for your review on #42", s: "GitWolf · 1 h ago", read: false }
      ]
    };

    var api = {
      id: "api", name: "api", ci: "run", cur: "main",
      heads: { main: "7ad03e5" }, remote: { main: "7ad03e5" }, tags: { "v1.4.0": "08be2d6" },
      commits: [
        C("7ad03e5", ["e19f7c3"], "fix: return 429 with Retry-After", 1, 12, [
          F("limiter.go", "M", "@@ -31,5 +31,6 @@\n     if over(limit) {\n+        w.Header().Set(\"Retry-After\", \"30\")\n         w.WriteHeader(429)\n     }")
        ]),
        C("e19f7c3", ["08be2d6"], "feat: cursor pagination", 2, 240, [
          F("list.go", "M", "@@ -14,4 +14,4 @@\n-    offset := page * size\n+    after := cursor.After\n     rows := query(after, size)"),
          F("cursor.go", "A", "@@ -0,0 +1,3 @@\n+type Cursor struct {\n+    After string\n+}")
        ]),
        C("08be2d6", [], "chore: bump go to 1.24", 1, 1500, [F("go.mod", "M", "@@ -1,3 +1,3 @@\n module example.com/api\n-go 1.23\n+go 1.24")])
      ],
      changes: [{ p: "limiter_test.go", st: "A", staged: false, d: "@@ -0,0 +1,4 @@\n+func TestRetryAfter(t *testing.T) {\n+    // the header is set when the limit is hit\n+    want := \"30\"\n+}" }],
      stashes: [], incoming: [], prs: [], issues: [], notes: []
    };

    var web = {
      id: "web", name: "web", ci: "ok", cur: "main",
      heads: { main: "d4a60b9" }, remote: { main: "d4a60b9" }, tags: {},
      commits: [
        C("d4a60b9", ["2c71e88"], "feat: dark mode toggle", 0, 60, [
          F("src/theme.css", "A", "@@ -0,0 +1,3 @@\n+:root[data-theme=\"dark\"] {\n+    --bg: #111;\n+}"),
          F("src/Header.tsx", "M", "@@ -6,3 +6,4 @@\n     <nav>\n+        <button onClick={toggle}>Theme</button>\n     </nav>")
        ]),
        C("2c71e88", [], "fix: focus ring on cards", 2, 2900, [F("src/Card.css", "M", "@@ -3,3 +3,3 @@\n .card:focus-visible {\n-    outline: none;\n+    outline: 2px solid var(--brand);\n }")])
      ],
      changes: [], stashes: [], incoming: [], prs: [], issues: [], notes: []
    };

    var ios = {
      id: "ios", name: "ios", ci: "fail", cur: "main",
      heads: { main: "6b93f1d" }, remote: { main: "6b93f1d" }, tags: { "2.0.0": "a8e25c0" },
      commits: [
        C("6b93f1d", ["a8e25c0"], "feat: widgets for today", 1, 180, [F("TodayWidget.swift", "A", "@@ -0,0 +1,3 @@\n+struct TodayWidget: Widget {\n+    var body: some WidgetConfiguration { StaticConfiguration() }\n+}")]),
        C("a8e25c0", [], "fix: keyboard avoids composer", 0, 1500, [F("Composer.swift", "M", "@@ -40,3 +40,3 @@\n     TextField(\"Message\", text: $text)\n-        .ignoresSafeArea()\n+        .ignoresSafeArea(.container)")])
      ],
      changes: [
        { p: "TodayWidget.swift", st: "M", staged: false, d: "@@ -1,3 +1,4 @@\n struct TodayWidget: Widget {\n+    let kind = \"today\"\n     var body: some WidgetConfiguration { StaticConfiguration() }" },
        { p: "Info.plist", st: "M", staged: false, d: "@@ -9,3 +9,3 @@\n     <key>CFBundleShortVersionString</key>\n-    <string>2.0.0</string>\n+    <string>2.0.1</string>" }
      ],
      stashes: [], incoming: [], prs: [], issues: [], notes: []
    };
    return [gw, api, web, ios];
  }

  /* ───────────── State ───────────── */

  var S, KBD = "⌘K";
  function freshState() {
    var repos = makeRepos();
    var s = {
      repos: repos, repo: 0, tab: "history", q: "", split: false,
      sel: {}, file: {}, cfile: {}, open: { branches: true, remotes: true, tags: false, stashes: true, origin: false },
      msg: "", amend: false, pop: null, pq: "", pi: 0, nb: "", toast: "", busy: "", seq: 0,
      issueFilter: "open", issueSel: null, prSel: 42, readAll: false, entered: true
    };
    repos.forEach(function (r) { s.sel[r.id] = r.commits[0].h; });
    return s;
  }
  S = freshState();

  function R() { return S.repos[S.repo]; }
  function byHash(r, h) { for (var i = 0; i < r.commits.length; i++) if (r.commits[i].h === h) return r.commits[i]; return null; }
  function head(r) { return r.heads[r.cur]; }
  function reach(r, h) {
    var seen = {}, stack = [h];
    while (stack.length) {
      var x = stack.pop();
      if (!x || seen[x]) continue;
      seen[x] = 1;
      var c = byHash(r, x);
      if (c) stack.push.apply(stack, c.p);
    }
    return seen;
  }
  function countOnly(r, a, b) { // commits reachable from a, not from b
    var ra = reach(r, a), rb = reach(r, b), n = 0;
    for (var k in ra) if (!rb[k]) n++;
    return n;
  }
  function ahead(r) { var rm = r.remote[r.cur] || r.remote.main; return countOnly(r, head(r), rm); }
  function behind(r) { var rm = r.remote[r.cur]; return rm ? countOnly(r, rm, head(r)) : 0; }
  function stagedOf(r) { return r.changes.filter(function (c) { return c.staged; }); }
  function unstagedOf(r) { return r.changes.filter(function (c) { return !c.staged; }); }

  /* ───────────── Helpers ───────────── */

  function esc(s) { return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }
  function rel(m) {
    if (m < 1) return "now";
    if (m < 60) return Math.round(m) + " min. ago";
    var h = Math.round(m / 60);
    if (m < 1440) return h + " hr. ago";
    var d = Math.round(m / 1440);
    return d === 1 ? "yesterday" : d + " days ago";
  }
  function when(m) { return rel(m); }
  function hue(seed) { var h = 5381; for (var i = 0; i < seed.length; i++) h = ((h * 33) + seed.charCodeAt(i)) | 0; return Math.abs(h) % 360; }
  var LANES = ["#5c85ff", "#bd61f5", "#fa669e", "#ff9e40", "#2ec2b8", "#4dcc73", "#8c80ff", "#f25961"];

  var IC = {
    refresh: "M20 11a8 8 0 0 0-14.5-4.2L4 9M4 4v5h5M4 13a8 8 0 0 0 14.5 4.2L20 15M20 20v-5h-5",
    down: "M12 4v14M6 12l6 6 6-6", up: "M12 20V6M6 12l6-6 6 6",
    tray: "M4 14v5h16v-5M12 4v10M8 10l4 4 4-4",
    bell: "M6 16v-5a6 6 0 0 1 12 0v5l2 2H4zM10 21h4",
    search: "M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14zM16.5 16.5L21 21",
    clock: "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7v5l3 2",
    edit: "M11 5H6a2 2 0 0 0-2 2v11a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2v-5M17 4l3 3-8 8H9v-3z",
    pr: "M6 7v10M18 17V11a4 4 0 0 0-4-4h-2M12 4l-3 3 3 3M6 3a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM6 17a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM18 17a2 2 0 1 0 0 4 2 2 0 0 0 0-4z",
    issue: "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 9.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5z",
    chev: "M9 6l6 6-6 6", check: "M5 12.5l4.5 4.5L19 7.5", plus: "M12 5v14M5 12h14", minus: "M5 12h14", x: "M6 6l12 12M18 6L6 18",
    folder: "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
    cloud: "M7 18a4 4 0 0 1-.6-7.95A6 6 0 0 1 18 9.5a4.25 4.25 0 0 1 0 8.5z",
    branch: "M6 6v12M6 6a2 2 0 1 0 0-.01M6 18a2 2 0 1 0 0-.01M18 8a2 2 0 1 0 0-.01M18 10c0 5-6 4-12 8",
    tag: "M3 12V4h8l9 9-8 8z M8 8h.01",
    copy: "M9 9h10v10H9zM5 15V5h10",
    lines: "M4 7h16M4 12h10M4 17h14", cols: "M4 5h16v14H4zM12 5v14",
    star: "M12 2.5l2.9 6.4 7.1.8-5.3 4.7 1.5 7-6.2-3.6-6.2 3.6 1.5-7L2 9.7l7.1-.8z",
    tray2: "M4 6h16v12H4zM4 13h5l1 2h4l1-2h5"
  };
  function ico(n, size, cls) {
    var fill = (n === "star") ? ' fill="currentColor"' : ' fill="none"';
    var fillFolder = n === "folder" || n === "cloud" ? "" : "";
    return '<svg class="gw-i' + (cls ? " " + cls : "") + '" viewBox="0 0 24 24" width="' + (size || 16) + '" height="' + (size || 16) + '"' + fill + ' stroke="currentColor" stroke-width="' + (n === "star" ? 0 : 1.9) + '" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="' + IC[n] + '"/></svg>' + fillFolder;
  }
  function btn(act, arg, cls, inner, extra) {
    return '<button type="button" class="' + cls + '" data-act="' + act + '"' + (arg != null ? ' data-arg="' + esc(arg) + '"' : "") + ' data-k="' + act + ":" + esc(arg == null ? "" : arg) + '"' + (extra || "") + ">" + inner + "</button>";
  }

  /* File type logos (simple-icons, CC0), drawn white on the language's colour. The shapes live in index.html. */
  var EXT = { swift: ["swift", "#F05138"], cs: ["csharp", "#512BD4"], xaml: ["dotnet", "#512BD4"], go: ["go", "#00ADD8"], mod: ["go", "#00ADD8"], css: ["css", "#1572B6"],
    html: ["html", "#E34F26"], md: ["md", "#083FA1"], svg: ["svg", "#FFB13B"], tsx: ["ts", "#3178C6"], ts: ["ts", "#3178C6"], plist: ["apple", "#555B66"], png: ["img", "#6b7280"] };
  function fileIcon(path) {
    var x = EXT[path.split(".").pop()];
    if (!x) return '<span class="gw-fi" style="--c:#6b7280"></span>';
    return '<span class="gw-fi" style="--c:' + x[1] + '"><svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true"><use href="#ft-' + x[0] + '"/></svg></span>';
  }
  function stBadge(st) {
    var col = st === "A" ? "var(--green)" : st === "D" ? "var(--red)" : "var(--orange)";
    return '<span class="gw-st" style="--c:' + col + '">' + st + "</span>";
  }
  var AV = ["av-ada", "av-sam", "av-lin"];
  function avatar(a, size) {
    return '<svg class="gw-av" width="' + size + '" height="' + size + '" viewBox="0 0 40 40" role="img" aria-label="' + AUTHORS[a] + '"><use href="#' + AV[a] + '"/></svg>';
  }
  function dirOf(p) { var i = p.lastIndexOf("/"); return i < 0 ? "" : p.slice(0, i); }
  function baseOf(p) { return p.slice(p.lastIndexOf("/") + 1); }
  function shortDir(p) { var d = dirOf(p); if (!d) return ""; var parts = d.split("/"); return parts.length > 2 ? "…/" + parts.slice(-2).join("/") : d; }

  /* ───────────── Graph ───────────── */

  function layout(commits) {
    var lanes = [], rows = [], max = 0;
    commits.forEach(function (c) {
      var col = lanes.indexOf(c.h), isNew = false;
      if (col < 0) { isNew = true; col = lanes.indexOf(null); if (col < 0) col = lanes.length; lanes[col] = c.h; }
      var top = [], pass = [], bottom = [];
      lanes.forEach(function (v, i) {
        if (v === c.h) { if (!(isNew && i === col)) top.push({ from: i, to: col }); }
        else if (v) pass.push(i);
      });
      lanes.forEach(function (v, i) { if (v === c.h) lanes[i] = null; });
      c.p.forEach(function (ph, k) {
        var ex = lanes.indexOf(ph);
        if (ex >= 0) { bottom.push({ from: col, to: ex }); return; }
        var t = k === 0 ? col : lanes.indexOf(null);
        if (t < 0) t = lanes.length;
        lanes[t] = ph; bottom.push({ from: col, to: t });
      });
      rows.push({ col: col, top: top, bottom: bottom, pass: pass });
      var w = Math.max(col, lanes.length - 1);
      top.concat(bottom).forEach(function (e) { w = Math.max(w, e.from, e.to); });
      pass.forEach(function (i) { w = Math.max(w, i); });
      if (w > max) max = w;
      while (lanes.length && lanes[lanes.length - 1] == null) lanes.pop();
    });
    return { rows: rows, count: max + 1 };
  }
  function graphSvg(row, count, i) {
    var W = 14, H = 50, mid = 25, w = count * W;
    function x(l) { return l * W + W / 2; }
    function seg(x1, y1, x2, y2, lane) {
      var d = x1 === x2 ? "M" + x1 + " " + y1 + "L" + x2 + " " + y2 : "M" + x1 + " " + y1 + "C" + x1 + " " + (y1 + y2) / 2 + " " + x2 + " " + (y1 + y2) / 2 + " " + x2 + " " + y2;
      return '<path d="' + d + '" stroke="' + LANES[lane % 8] + '" />';
    }
    var s = "";
    row.pass.forEach(function (l) { s += seg(x(l), 0, x(l), H, l); });
    row.top.forEach(function (e) { s += seg(x(e.from), 0, x(e.to), mid, e.from); });
    row.bottom.forEach(function (e) { s += seg(x(e.from), mid, x(e.to), H, e.to); });
    var col = LANES[row.col % 8];
    return '<svg class="gw-graph" width="' + w + '" height="' + H + '" viewBox="0 0 ' + w + " " + H + '" style="--i:' + i + '" aria-hidden="true"><g fill="none" stroke-width="2" stroke-linecap="round">' + s + '</g><circle cx="' + x(row.col) + '" cy="' + mid + '" r="6.5" class="gw-dotbg"/><circle cx="' + x(row.col) + '" cy="' + mid + '" r="4.5" fill="' + col + '"/></svg>';
  }

  /* ───────────── Diff ───────────── */

  function highlight(t) {
    var re = /("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|\/\/.*$|\/\*.*?\*\/)|\b(\d+(?:\.\d+)?)\b/g, out = "", last = 0, m;
    while ((m = re.exec(t))) {
      out += esc(t.slice(last, m.index));
      out += m[1] ? '<span class="' + (m[1].charAt(0) === "/" ? "gw-c" : "gw-s") + '">' + esc(m[1]) + "</span>" : '<span class="gw-n">' + esc(m[2]) + "</span>";
      last = re.lastIndex;
    }
    return out + esc(t.slice(last));
  }
  function parseDiff(text) {
    var rows = [], o = 0, n = 0, add = 0, del = 0;
    text.split("\n").forEach(function (l) {
      if (l.indexOf("@@") === 0) {
        var m = /@@ -(\d+)(?:,\d+)? \+(\d+)/.exec(l);
        o = m ? +m[1] : 1; n = m ? +m[2] : 1;
        rows.push({ t: "h", x: l });
      } else if (l.charAt(0) === "+") { rows.push({ t: "a", n: n++, x: l.slice(1) }); add++; }
      else if (l.charAt(0) === "-") { rows.push({ t: "d", o: o++, x: l.slice(1) }); del++; }
      else rows.push({ t: "c", o: o++, n: n++, x: l.slice(1) });
    });
    return { rows: rows, add: add, del: del };
  }
  function diffBody(text, split) {
    var p = parseDiff(text), h = "";
    if (!split) {
      p.rows.forEach(function (r) {
        if (r.t === "h") { h += '<div class="gw-dl gw-dh"><span class="gw-ln"></span><span class="gw-ln"></span><span class="gw-sg"></span><code>' + esc(r.x) + "</code></div>"; return; }
        h += '<div class="gw-dl gw-d' + r.t + '"><span class="gw-ln">' + (r.o == null ? "" : r.o) + '</span><span class="gw-ln">' + (r.n == null ? "" : r.n) + '</span><span class="gw-sg">' + (r.t === "a" ? "+" : r.t === "d" ? "−" : "") + "</span><code>" + highlight(r.x) + "</code></div>";
      });
      return { html: '<div class="gw-diff">' + h + "</div>", add: p.add, del: p.del };
    }
    var i = 0, rows = p.rows;
    while (i < rows.length) {
      var r = rows[i];
      if (r.t === "h") { h += '<div class="gw-dl gw-dh gw-dsplit"><code>' + esc(r.x) + "</code></div>"; i++; continue; }
      if (r.t === "c") { h += '<div class="gw-dl gw-dsplit"><span class="gw-ln">' + r.o + '</span><code>' + highlight(r.x) + '</code><span class="gw-ln">' + r.n + "</span><code>" + highlight(r.x) + "</code></div>"; i++; continue; }
      var ds = [], as = [];
      while (i < rows.length && rows[i].t === "d") ds.push(rows[i++]);
      while (i < rows.length && rows[i].t === "a") as.push(rows[i++]);
      for (var k = 0; k < Math.max(ds.length, as.length); k++) {
        var d = ds[k], a = as[k];
        h += '<div class="gw-dl gw-dsplit"><span class="gw-ln ' + (d ? "gw-d" : "") + '">' + (d ? d.o : "") + '</span><code class="' + (d ? "gw-dd" : "gw-de") + '">' + (d ? highlight(d.x) : "") + '</code><span class="gw-ln ' + (a ? "gw-a" : "") + '">' + (a ? a.n : "") + '</span><code class="' + (a ? "gw-da" : "gw-de") + '">' + (a ? highlight(a.x) : "") + "</code></div>";
      }
    }
    return { html: '<div class="gw-diff gw-split">' + h + "</div>", add: p.add, del: p.del };
  }
  function diffPane(file, split) {
    if (!file) return '<div class="gw-empty"><b>No file selected</b><span>Select a file to see its changes.</span></div>';
    var d = diffBody(file.d, split), dir = dirOf(file.p);
    return '<div class="gw-dhead">' + fileIcon(file.p) + '<span class="gw-dpath">' + (dir ? '<span class="gw-dim">' + esc(dir) + "/ </span>" : "") + "<b>" + esc(baseOf(file.p)) + "</b></span>" +
      '<span class="gw-seg gw-seg2">' + btn("split", "0", "gw-segb" + (split ? "" : " on"), ico("lines", 15), ' aria-label="Unified diff" title="Unified"') + btn("split", "1", "gw-segb" + (split ? " on" : ""), ico("cols", 15), ' aria-label="Side by side diff" title="Side by side"') + "</span>" +
      '<span class="gw-pill gw-padd">+' + d.add + '</span><span class="gw-pill gw-pdel">−' + d.del + "</span></div>" + '<div class="gw-dscroll">' + d.html + "</div>";
  }

  /* ───────────── Views ───────────── */

  function refsFor(r, c) {
    var out = [];
    Object.keys(r.heads).forEach(function (b) { if (r.heads[b] === c.h) out.push({ t: b === r.cur ? "head" : "branch", n: b }); });
    Object.keys(r.remote).forEach(function (b) { if (r.remote[b] === c.h) out.push({ t: "remote", n: "origin/" + b }); });
    Object.keys(r.tags).forEach(function (t) { if (r.tags[t] === c.h) out.push({ t: "tag", n: t }); });
    out.sort(function (a, b) { return (a.t === "head" ? 0 : 1) - (b.t === "head" ? 0 : 1); });
    return out.slice(0, 3);
  }
  function chip(x) { return '<span class="gw-chip gw-chip-' + x.t + '">' + (x.t === "tag" ? ico("tag", 10) : "") + esc(x.n) + "</span>"; }

  function toolbar(r) {
    var ch = unstagedOf(r).length + stagedOf(r).length;
    var branchIcon = r.cur === "main" ? ico("star", 15, "gw-star") : ico("branch", 15);
    var busy = function (k) { return S.busy === k ? " gw-busy-" + k : ""; };
    var tabs = [["history", "History", "clock"], ["changes", "Changes", "edit"], ["pulls", "Pulls", "pr"], ["issues", "Issues", "issue"]].map(function (t) {
      return btn("tab", t[0], "gw-tab" + (S.tab === t[0] ? " on" : ""), ico(t[2], 15) + "<span>" + t[1] + "</span>" + (t[0] === "changes" && ch ? '<i class="gw-count">' + ch + "</i>" : ""), ' role="tab" aria-selected="' + (S.tab === t[0]) + '"');
    }).join("");
    var unread = r.notes.some(function (n) { return !n.read; });
    return '<div class="gw-bar">' +
      '<div class="gw-barl">' + btn("pop", "branch", "gw-ctl gw-branchbtn", branchIcon + "<b>" + esc(r.cur) + '</b><svg class="gw-i gw-up" viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M8 10l4-4 4 4M8 14l4 4 4-4"/></svg>', ' data-pop="branch" aria-haspopup="menu"') +
      '<div class="gw-ctl gw-tabs" role="tablist">' + tabs + "</div></div>" +
      '<div class="gw-barr"><div class="gw-ctl gw-sync">' +
      btn("fetch", null, "gw-sb" + busy("fetch"), ico("refresh", 16), ' aria-label="Fetch" title="Fetch"') +
      btn("pull", null, "gw-sb" + busy("pull"), ico("down", 16), ' aria-label="Pull" title="Pull"') +
      btn("push", null, "gw-sb" + busy("push"), ico("up", 16), ' aria-label="Push" title="Push"') +
      btn("stash", null, "gw-sb", ico("tray", 16), ' aria-label="Stash changes" title="Stash changes"') +
      "</div>" +
      btn("pop", "notes", "gw-ctl gw-round" + (unread ? " gw-dotted" : ""), ico("bell", 16), ' data-pop="notes" aria-label="Notifications" aria-haspopup="menu"') +
      btn("pop", "jump", "gw-ctl gw-jump", ico("search", 15) + "<span>Jump to…</span><kbd>" + KBD + "</kbd>", ' data-pop="jump" aria-label="Jump to"') +
      "</div></div>";
  }

  function rail() {
    var tiles = S.repos.map(function (r, i) {
      var on = i === S.repo, c = hue(r.name), col = "hsl(" + c + " 47% 58%)";
      var count = r.changes.length;
      var ci = r.ci === "ok" ? '<i class="gw-mk gw-ok">' + ico("check", 9) + "</i>" : r.ci === "run" ? '<i class="gw-mk gw-run"></i>' : r.ci === "fail" ? '<i class="gw-mk gw-fail">' + ico("x", 8) + "</i>" : "";
      var inner = r.icon ? '<svg width="40" height="40" viewBox="0 0 40 40" aria-hidden="true"><use href="#gw-logo"/></svg>' : esc(r.name.length > 3 ? r.name.slice(0, 2) : r.name);
      return '<div class="gw-rt' + (on ? " on" : "") + '">' + btn("repo", String(i), "gw-tile" + (on ? " on" : "") + (r.icon ? " gw-img" : ""), inner + ci + (count ? '<i class="gw-mk gw-num">' + count + "</i>" : ""), ' style="--c:' + (r.icon ? "var(--brand)" : col) + '" aria-label="' + esc(r.name) + (count ? ", " + count + " changes" : "") + '" aria-pressed="' + on + '" title="' + esc(r.name) + " · " + esc(r.cur) + '"') + "</div>";
    }).join("");
    return '<div class="gw-rail">' + tiles + "</div>";
  }

  function section(key, title, count, body) {
    var open = S.open[key];
    return '<div class="gw-sec">' + btn("sec", key, "gw-sh", "<span>" + title + '</span><i class="gw-cnt">' + count + "</i>" + ico("chev", 13, "gw-chv" + (open ? " open" : "")), ' aria-expanded="' + open + '"') + (open ? '<div class="gw-sb2">' + body + "</div>" : "") + "</div>";
  }
  function sideRow(act, arg, icon, label, trailing, cur, extra) {
    return btn(act, arg, "gw-sr" + (cur ? " cur" : ""), '<span class="gw-sri">' + icon + "</span><span class=\"gw-srl\">" + esc(label) + "</span>" + (trailing ? '<span class="gw-srt">' + trailing + "</span>" : ""), extra);
  }
  function sidebar(r) {
    var ch = r.changes.length, a = ahead(r), b = behind(r);
    var head1 = '<div class="gw-side-h"><h2>' + esc(r.name) + '</h2><p><i class="gw-sdot ' + (ch ? "warn" : "ok") + '"></i>' + (ch ? ch + " change" + (ch === 1 ? "" : "s") : "Clean") + (a > 0 ? " <span>↑" + a + "</span>" : "") + (b > 0 ? " <span>↓" + b + "</span>" : "") + "</p>" +
      '<p class="gw-prov">GitHub · ipuppyyt/' + esc(r.name === "GitWolf" ? "GitWolf" : r.name) + " ↗</p></div>";
    var bnames = Object.keys(r.heads);
    var branches = bnames.map(function (n) {
      var cur = n === r.cur, aa = cur ? ahead(r) : 0;
      var ic = n === "main" ? '<span class="gw-star">' + ico("star", 15) + (cur ? '<i class="gw-mk gw-ok gw-mini">' + ico("check", 7) + "</i>" : "") + "</span>" : '<span style="color:' + LANES[bnames.indexOf(n) % 8] + '">' + ico("branch", 15) + "</span>";
      return sideRow("reveal", r.heads[n], ic, n, aa > 0 ? "↑" + aa : "", cur, ' data-branch="' + esc(n) + '" title="Click to show, double-click to check out"');
    }).join("");
    var remotes = '<div>' + btn("sec", "origin", "gw-sr", '<span class="gw-sri gw-rchev">' + ico("chev", 12, S.open.origin ? "open" : "") + '</span><span class="gw-sri" style="color:#bd61f5">' + ico("cloud", 15) + '</span><span class="gw-srl">origin</span>') +
      (S.open.origin ? Object.keys(r.remote).map(function (n) { return sideRow("reveal", r.remote[n], '<span class="gw-ind" style="color:' + LANES[0] + '">' + ico("branch", 14) + "</span>", n, "", false, '').replace("gw-sr\"", "gw-sr gw-sub\""); }).join("") : "") + "</div>";
    var tagNames = Object.keys(r.tags);
    var tags = tagNames.length ? tagNames.map(function (t) { return sideRow("reveal", r.tags[t], '<span style="color:#ff9e40">' + ico("tag", 14) + "</span>", t, "", false); }).join("") : '<p class="gw-note">No tags</p>';
    var stashes = r.stashes.length ? r.stashes.map(function (s, i) { return btn("pop-stash", String(i), "gw-sr", '<span class="gw-sri">' + ico("tray2", 14) + '</span><span class="gw-srl">' + esc(s.t) + '</span><span class="gw-srt gw-link">Pop</span>', ' title="Apply this stash and drop it"'); }).join("") : '<p class="gw-note">No stashes</p>';
    return '<div class="gw-side">' + head1 +
      section("branches", "Branches", bnames.length, branches) + section("remotes", "Remotes", 1, remotes) +
      section("tags", "Tags", tagNames.length, tags) + section("stashes", "Stashes", r.stashes.length, stashes) + "</div>";
  }

  function commitRow(r, c, g, i, selected) {
    var refs = refsFor(r, c);
    return '<button type="button" class="gw-crow' + (selected ? " on" : "") + '" data-act="commit" data-arg="' + c.h + '" data-k="commit:' + c.h + '" aria-pressed="' + selected + '">' +
      (g ? graphSvg(g.rows[i], g.count, i) : "") + avatar(c.a, 24) +
      '<span class="gw-ct"><span class="gw-cl">' + refs.map(chip).join("") + "<b>" + esc(c.s) + '</b></span><small>' + AUTHORS[c.a] + " · " + c.h.slice(0, 7) + "</small></span>" +
      '<span class="gw-cd">' + rel(c.m) + "</span></button>";
  }
  function historyList(r) {
    var q = S.q.trim().toLowerCase(), items = r.commits;
    var g = q ? null : layout(items);
    var rows = [];
    items.forEach(function (c, i) {
      if (q && (c.s + " " + AUTHORS[c.a] + " " + c.h).toLowerCase().indexOf(q) < 0) return;
      rows.push(commitRow(r, c, g, i, S.sel[r.id] === c.h));
    });
    return '<div class="gw-search"><label>' + ico("search", 14) + '<input id="gw-q" data-k="in:q" data-in="q" type="search" placeholder="Search history" value="' + esc(S.q) + '" autocomplete="off" aria-label="Search history"></label></div>' +
      '<div class="gw-list" data-list="history">' + (rows.length ? rows.join("") : '<div class="gw-empty"><b>No matches</b><span>Try a different search.</span></div>') + "</div>";
  }

  function commitDetail(r) {
    var c = byHash(r, S.sel[r.id]);
    if (!c) return '<div class="gw-empty"><b>No commit selected</b><span>Select a commit to see its changes.</span></div>';
    var fp = S.file[r.id + c.h] || (c.files[0] && c.files[0].p), file = c.files.filter(function (f) { return f.p === fp; })[0] || c.files[0];
    var parents = c.p.map(function (p) { return btn("commit", p, "gw-plink", p.slice(0, 7)); }).join(" ");
    var files = c.files.map(function (f) {
      return btn("file", f.p, "gw-frow" + (file && f.p === file.p ? " on" : ""), stBadge(f.st) + fileIcon(f.p) + "<b>" + esc(baseOf(f.p)) + '</b><span class="gw-dim">' + esc(shortDir(f.p)) + "</span>");
    }).join("");
    return '<div class="gw-chead"><h3>' + esc(c.s) + "</h3>" + (c.body ? "<p>" + esc(c.body) + "</p>" : "") +
      '<div class="gw-who">' + avatar(c.a, 30) + "<span><b>" + AUTHORS[c.a] + "</b><small>" + when(c.m) + "</small></span>" +
      btn("copy", c.h, "gw-copy" + (S.copied === c.h ? " done" : ""), (S.copied === c.h ? '<span class="gw-okc">' + ico("check", 14) + "</span>" : ico("copy", 14)) + '<span class="gw-hs"><span class="a">' + c.h.slice(0, 7) + '</span><span class="b">Copied</span></span>', ' aria-label="Copy full hash"') + "</div>" +
      (parents ? '<div class="gw-par"><small>' + (c.p.length > 1 ? "Parents" : "Parent") + "</small>" + parents + "</div>" : "") + "</div>" +
      '<div class="gw-files">' + files + "</div>" + '<div class="gw-dp">' + diffPane(file, S.split) + "</div>";
  }

  function changesList(r) {
    var st = stagedOf(r), un = unstagedOf(r);
    function row(c) {
      return '<div class="gw-chrow' + (S.cfile[r.id] === c.p ? " on" : "") + '">' + btn("cfile", c.p, "gw-chmain", stBadge(c.st) + fileIcon(c.p) + "<b>" + esc(baseOf(c.p)) + '</b><span class="gw-dim">' + ico("folder", 13, "gw-fold") + esc(shortDir(c.p)) + "</span>") +
        btn(c.staged ? "unstage" : "stage", c.p, "gw-stg" + (c.staged ? " on" : ""), ico(c.staged ? "minus" : "plus", 14), ' aria-label="' + (c.staged ? "Unstage " : "Stage ") + esc(baseOf(c.p)) + '"') + "</div>";
    }
    var head = function (t, n, act, label) { return '<div class="gw-lh"><span>' + t + '</span><i class="gw-cnt">' + n + "</i>" + (n ? btn(act, null, "gw-link gw-lhb", label) : "") + "</div>"; };
    var body = "";
    if (st.length) body += head("Staged", st.length, "unstage-all", "Unstage All") + st.map(row).join("");
    body += head("Changes", un.length, "stage-all", "Stage All") + un.map(row).join("");
    if (!st.length && !un.length) body = '<div class="gw-empty"><b>No changes</b><span>Your working tree is clean.</span></div>';
    var can = st.length && (S.msg.trim() || S.amend);
    return '<div class="gw-list gw-chlist">' + body + "</div>" +
      '<div class="gw-box"><textarea id="gw-msg" data-k="in:msg" data-in="msg" rows="3" placeholder="Describe your changes…" aria-label="Commit message">' + esc(S.msg) + "</textarea>" +
      '<div class="gw-boxr"><label class="gw-amend"><input type="checkbox" data-act="amend" data-k="amend"' + (S.amend ? " checked" : "") + "> Amend last commit</label>" +
      btn("commit-now", null, "gw-commit" + (can ? "" : " off"), "Commit", can ? "" : ' aria-disabled="true"') + "</div></div>";
  }
  function changesDetail(r) {
    var all = r.changes, f = all.filter(function (c) { return c.p === S.cfile[r.id]; })[0] || all[0];
    return '<div class="gw-dp gw-full">' + diffPane(f, S.split) + "</div>";
  }

  function pullsView(r) {
    if (!r.prs.length) return { list: '<div class="gw-empty"><b>No pull requests</b><span>Open pull requests for this repository appear here.</span></div>', detail: '<div class="gw-empty"><b>Nothing to review</b><span>Select a pull request to see it.</span></div>' };
    var list = r.prs.map(function (p) {
      return btn("pr", String(p.n), "gw-crow gw-prrow" + (S.prSel === p.n ? " on" : ""), '<span class="gw-prico ' + p.state + '">' + ico("pr", 16) + '</span><span class="gw-ct"><span class="gw-cl"><b>' + esc(p.t) + "</b></span><small>#" + p.n + " · " + AUTHORS[p.by] + " · " + p.state + '</small></span><span class="gw-cd">' + rel(p.m) + "</span>");
    }).join("");
    var p = r.prs.filter(function (x) { return x.n === S.prSel; })[0] || r.prs[0];
    var merged = p.state === "merged";
    var detail = '<div class="gw-chead"><h3>' + esc(p.t) + " <span class=\"gw-dim\">#" + p.n + "</span></h3><p>" + esc(p.body) + "</p>" +
      '<div class="gw-who">' + avatar(p.by, 30) + "<span><b>" + AUTHORS[p.by] + "</b><small>wants to merge <code>" + esc(p.branch) + "</code> into <code>main</code></small></span></div>" +
      '<div class="gw-checks"><span class="gw-mk gw-ok gw-inl">' + ico("check", 9) + "</span><span><b>All checks have passed</b><small>CI · build and test on macOS and Windows</small></span></div>" +
      '<div class="gw-actions">' + (merged ? '<span class="gw-pill gw-padd">Merged</span>' : btn("merge", String(p.n), "gw-primary", "Merge pull request")) + "</div></div>";
    return { list: '<div class="gw-list">' + list + "</div>", detail: detail };
  }
  function issuesView(r) {
    if (!r.issues.length) return { list: '<div class="gw-empty"><b>No issues</b><span>Issues for this repository appear here.</span></div>', detail: '<div class="gw-empty"><b>Nothing here</b></div>' };
    var f = S.issueFilter;
    var seg = '<div class="gw-search"><span class="gw-seg gw-seg3">' + btn("ifilter", "open", "gw-segb" + (f === "open" ? " on" : ""), "Open " + r.issues.filter(function (i) { return i.state === "open"; }).length) + btn("ifilter", "closed", "gw-segb" + (f === "closed" ? " on" : ""), "Closed " + r.issues.filter(function (i) { return i.state === "closed"; }).length) + "</span></div>";
    var items = r.issues.filter(function (i) { return i.state === f; });
    var list = items.length ? items.map(function (i) {
      return btn("issue", String(i.n), "gw-crow gw-prrow" + (S.issueSel === i.n ? " on" : ""), '<span class="gw-prico ' + i.state + '">' + ico("issue", 16) + '</span><span class="gw-ct"><span class="gw-cl"><b>' + esc(i.t) + "</b></span><small>#" + i.n + " · " + AUTHORS[i.by] + '</small></span><span class="gw-cd">' + rel(i.m) + "</span>");
    }).join("") : '<div class="gw-empty"><b>Nothing ' + f + "</b></div>";
    var cur = r.issues.filter(function (i) { return i.n === S.issueSel; })[0] || items[0];
    var detail = cur ? '<div class="gw-chead"><h3>' + esc(cur.t) + ' <span class="gw-dim">#' + cur.n + "</span></h3><p>" + esc(cur.body) + '</p><div class="gw-who">' + avatar(cur.by, 30) + "<span><b>" + AUTHORS[cur.by] + "</b><small>opened " + rel(cur.m).replace(" ago", "") + " ago</small></span></div>" +
      '<div class="gw-actions">' + btn("toggle-issue", String(cur.n), "gw-primary" + (cur.state === "open" ? "" : " quiet"), cur.state === "open" ? "Close issue" : "Reopen issue") + "</div></div>" : '<div class="gw-empty"><b>Nothing here</b></div>';
    return { list: seg + '<div class="gw-list">' + list + "</div>", detail: detail };
  }

  function popover(r) {
    if (!S.pop) return "";
    if (S.pop === "branch") {
      var items = Object.keys(r.heads).map(function (n) { return btn("checkout", n, "gw-mi" + (n === r.cur ? " cur" : ""), '<span class="gw-mic">' + (n === r.cur ? ico("check", 14) : "") + "</span>" + esc(n)); }).join("");
      var sts = r.stashes.length ? '<div class="gw-sep"></div><p class="gw-pl">Stashes</p>' + r.stashes.map(function (s, i) { return btn("pop-stash", String(i), "gw-mi", '<span class="gw-mic">' + ico("tray2", 14) + "</span><span><b>Pop " + esc(s.t) + "</b></span>"); }).join("") : "";
      return '<div class="gw-pop gw-pop-l" role="menu"><p class="gw-pl">Switch branch</p>' + items + sts + '<div class="gw-sep"></div><form class="gw-nbf" data-form="newbranch"><input id="gw-nb" data-k="in:nb" data-in="nb" placeholder="New branch name" value="' + esc(S.nb) + '" autocomplete="off" aria-label="New branch name"><button type="submit" class="gw-primary sm">Create</button></form></div>';
    }
    if (S.pop === "notes") {
      var n = r.notes.length ? r.notes.map(function (x) { return btn("note", x.id, "gw-mi gw-note-i" + (x.read ? " read" : ""), '<span class="gw-mic">' + (x.read ? "" : '<i class="gw-udot"></i>') + '</span><span><b>' + esc(x.t) + "</b><small>" + esc(x.s) + "</small></span>"); }).join("") : '<p class="gw-note pad">You are all caught up.</p>';
      return '<div class="gw-pop gw-pop-r" role="menu"><p class="gw-pl">Notifications</p>' + n + "</div>";
    }
    if (S.pop === "jump") {
      var its = paletteItems(r);
      return '<div class="gw-veil"><div class="gw-pal" role="dialog" aria-label="Jump to"><div class="gw-pin">' + ico("search", 16) + '<input id="gw-pq" data-k="in:pq" data-in="pq" placeholder="Jump to a commit, branch, repository or action" value="' + esc(S.pq) + '" autocomplete="off" aria-label="Jump to"></div><div class="gw-pl2">' +
        (its.length ? its.map(function (it, i) { return btn("palette", String(i), "gw-pi" + (i === S.pi ? " on" : ""), '<span class="gw-pk">' + esc(it.k) + "</span><b>" + esc(it.t) + "</b>"); }).join("") : '<p class="gw-note pad">Nothing matches.</p>') + "</div></div></div>";
    }
    return "";
  }

  function paletteItems(r) {
    var items = [];
    [["history", "History"], ["changes", "Changes"], ["pulls", "Pull requests"], ["issues", "Issues"]].forEach(function (t) { items.push({ k: "Go to", t: t[1], run: function () { S.tab = t[0]; } }); });
    items.push({ k: "Action", t: "Fetch", run: function () { act("fetch"); } }, { k: "Action", t: "Pull", run: function () { act("pull"); } }, { k: "Action", t: "Push", run: function () { act("push"); } }, { k: "Action", t: "Stash changes", run: function () { act("stash"); } });
    S.repos.forEach(function (x, i) { items.push({ k: "Repository", t: x.name, run: function () { S.repo = i; S.tab = "history"; } }); });
    Object.keys(r.heads).forEach(function (b) { items.push({ k: "Checkout", t: b, run: function () { r.cur = b; S.sel[r.id] = r.heads[b]; S.tab = "history"; } }); });
    r.commits.forEach(function (c) { items.push({ k: "Commit", t: c.s + " · " + c.h.slice(0, 7), run: function () { S.sel[r.id] = c.h; S.tab = "history"; S.q = ""; } }); });
    var q = S.pq.trim().toLowerCase();
    if (q) items = items.filter(function (it) { return (it.k + " " + it.t).toLowerCase().indexOf(q) >= 0; });
    return items.slice(0, 8);
  }

  function render(opts) {
    opts = opts || {};
    var r = R(), mid, right, extra = "";
    if (S.tab === "history") { mid = historyList(r); right = commitDetail(r); }
    else if (S.tab === "changes") { mid = changesList(r); right = changesDetail(r); }
    else if (S.tab === "pulls") { var p = pullsView(r); mid = p.list; right = p.detail; }
    else { var q = issuesView(r); mid = q.list; right = q.detail; }
    return toolbar(r) + '<div class="gw-body">' + rail() + sidebar(r) + '<div class="gw-panel gw-mid">' + mid + '</div><div class="gw-panel gw-right">' + right + "</div></div>" + popover(r) +
      (S.toast ? '<div class="gw-toast" role="status">' + esc(S.toast) + "</div>" : "");
  }

  /* ───────────── Actions ───────────── */

  function toast(msg) {
    S.toast = msg; var id = ++S.seq;
    if (typeof setTimeout === "function") setTimeout(function () { if (S.seq === id) { S.toast = ""; paint(); } }, 2400);
  }
  function hex() { S.seq++; var s = ""; var n = (Date.now() + S.seq * 7919) >>> 0; for (var i = 0; i < 7; i++) { s += "0123456789abcdef"[n & 15]; n = (n * 1103515245 + 12345) >>> 0; } return s; }
  function simulate(key, done) {
    S.busy = key; paint();
    setTimeout(function () { S.busy = ""; done(); paint(); }, 650);
  }
  function setCi(r) {
    r.ci = "run";
    setTimeout(function () { r.ci = "ok"; paint(); }, 3200);
  }

  function act(name, arg, el) {
    var r = R();
    switch (name) {
      case "tab": S.tab = arg; S.pop = null; break;
      case "repo": S.repo = +arg; S.pop = null; S.q = ""; break;
      case "commit": S.sel[r.id] = arg; S.tab = "history"; S.q = ""; scrollTo = arg; break;
      case "reveal": S.sel[r.id] = arg; S.tab = "history"; S.q = ""; scrollTo = arg; break;
      case "sec": S.open[arg] = !S.open[arg]; break;
      case "file": S.file[r.id + S.sel[r.id]] = arg; break;
      case "cfile": S.cfile[r.id] = arg; break;
      case "split": S.split = arg === "1"; break;
      case "copy": try { navigator.clipboard.writeText(arg); } catch (e) {} S.copied = arg; setTimeout(function () { S.copied = null; paint(); }, 1500); break;
      case "pop": S.pop = S.pop === arg ? null : arg; S.pq = ""; S.pi = 0; focusNext = arg === "jump" ? "in:pq" : arg === "branch" ? "in:nb" : null; break;
      case "checkout": r.cur = arg; S.sel[r.id] = r.heads[arg]; S.pop = null; toast("Switched to " + arg); break;
      case "stage": r.changes.forEach(function (c) { if (c.p === arg) c.staged = true; }); break;
      case "unstage": r.changes.forEach(function (c) { if (c.p === arg) c.staged = false; }); break;
      case "stage-all": r.changes.forEach(function (c) { c.staged = true; }); break;
      case "unstage-all": r.changes.forEach(function (c) { c.staged = false; }); break;
      case "amend": S.amend = el ? el.checked : !S.amend; if (S.amend && !S.msg) { var hc = byHash(r, head(r)); if (hc) S.msg = hc.s; } break;
      case "commit-now": commit(r); break;
      case "fetch": simulate("fetch", function () {
        if (r.incoming.length) { var inc = r.incoming.shift(); r.commits.unshift(inc); r.remote[r.cur === "main" ? "main" : "main"] = inc.h; toast("Fetched origin: 1 new commit"); }
        else toast("Fetched origin: already up to date");
      }); break;
      case "pull": simulate("pull", function () {
        var rm = r.remote[r.cur];
        if (r.incoming.length && r.cur === "main") { var inc = r.incoming.shift(); r.commits.unshift(inc); r.remote.main = inc.h; rm = inc.h; }
        if (!rm || behind(r) === 0) { toast("Already up to date"); return; }
        if (head(r) === byHash(r, rm).p[0] && countOnly(r, head(r), rm) === 0) { r.heads[r.cur] = rm; toast("Pulled 1 commit (fast-forward)"); }
        else {
          var mc = C(hex(), [head(r), rm], "Merge branch 'main' of origin", 0, 0, []);
          r.commits.unshift(mc); r.heads[r.cur] = mc.h; S.sel[r.id] = mc.h; toast("Pulled and merged origin/" + r.cur);
        }
      }); break;
      case "push": simulate("push", function () {
        var n = ahead(r);
        if (!n) { toast("Nothing to push"); return; }
        r.remote[r.cur] = head(r); toast("Pushed " + n + " commit" + (n === 1 ? "" : "s") + " to origin/" + r.cur); setCi(r);
      }); break;
      case "stash":
        if (!r.changes.length) { toast("Nothing to stash"); break; }
        r.stashes.unshift({ t: "WIP on " + r.cur, files: r.changes.slice() }); r.changes = []; S.open.stashes = true; toast("Stashed " + r.stashes[0].files.length + " file" + (r.stashes[0].files.length === 1 ? "" : "s")); break;
      case "pop-stash": var st = r.stashes.splice(+arg, 1)[0]; st.files.forEach(function (f) { f.staged = false; r.changes.push(f); }); toast("Applied stash"); break;
      case "pr": S.prSel = +arg; break;
      case "merge": var pr = r.prs.filter(function (x) { return x.n === +arg; })[0];
        var mc2 = C(hex(), [head(r), r.heads[pr.branch]], "Merge pull request #" + pr.n + " from " + pr.branch, 0, 0, []);
        pr.state = "merged"; r.commits.unshift(mc2); r.heads[r.cur] = mc2.h; S.sel[r.id] = mc2.h; delete r.heads[pr.branch]; toast("Merged pull request #" + pr.n); break;
      case "ifilter": S.issueFilter = arg; S.issueSel = null; break;
      case "issue": S.issueSel = +arg; break;
      case "toggle-issue": r.issues.forEach(function (i) { if (i.n === +arg) { i.state = i.state === "open" ? "closed" : "open"; toast("Issue #" + i.n + (i.state === "open" ? " reopened" : " closed")); } }); S.issueSel = null; break;
      case "note": r.notes.forEach(function (n) { if (n.id === arg) n.read = true; }); break;
      case "palette": var its = paletteItems(r); if (its[+arg]) { var it = its[+arg]; S.pop = null; it.run(); } break;
    }
  }
  var scrollTo = null, focusNext = null;

  function commit(r) {
    var st = stagedOf(r);
    if (!st.length || !(S.msg.trim() || S.amend)) { toast(st.length ? "Write a message first" : "Stage something to commit"); return; }
    var msg = S.msg.trim().split("\n"), subject = msg[0], body = msg.slice(1).join(" ").trim();
    if (S.amend) {
      var hc = byHash(r, head(r)); hc.s = subject || hc.s; if (body) hc.body = body;
      st.forEach(function (f) { hc.files.push({ p: f.p, st: f.st, d: f.d }); });
      toast("Amended " + hc.h.slice(0, 7));
    } else {
      var c = C(hex(), [head(r)], subject, 0, 0, st.map(function (f) { return { p: f.p, st: f.st, d: f.d }; }), { body: body });
      r.commits.unshift(c); r.heads[r.cur] = c.h; S.sel[r.id] = c.h; toast("Committed " + c.h.slice(0, 7));
    }
    r.changes = r.changes.filter(function (c) { return !c.staged; });
    S.msg = ""; S.amend = false; S.tab = "history"; setCi(r);
  }

  /* ───────────── Mount ───────────── */

  var host = null;
  var lastPop = null, lastToast = "";
  function paint() {
    if (!host) return;
    var a = document.activeElement, key = a && a.getAttribute ? a.getAttribute("data-k") : null, pos = a && a.selectionStart != null ? a.selectionStart : null;
    if (!(a && host.contains(a))) key = null;
    var keepScroll = {};
    [].forEach.call(host.querySelectorAll(".gw-list,.gw-dscroll,.gw-files,.gw-side"), function (n, i) { keepScroll[n.className + i] = n.scrollTop; });
    host.classList.remove("gw-enter");
    // A repaint while the same popup/toast is showing must not replay its entry animation (it would flicker per keystroke).
    host.classList.toggle("gw-still", !!S.pop && S.pop === lastPop);
    host.classList.toggle("gw-still-toast", !!S.toast && S.toast === lastToast);
    lastPop = S.pop; lastToast = S.toast;
    host.innerHTML = render();
    [].forEach.call(host.querySelectorAll(".gw-list,.gw-dscroll,.gw-files,.gw-side"), function (n, i) { if (keepScroll[n.className + i] != null) n.scrollTop = keepScroll[n.className + i]; });
    if (scrollTo) {
      var t = host.querySelector('.gw-crow[data-arg="' + scrollTo + '"]');
      if (t && t.scrollIntoView) { var l = t.parentNode; if (l && l.scrollTop != null) l.scrollTop = Math.max(0, t.offsetTop - l.clientHeight / 2 + 25); }
      scrollTo = null;
    }
    var f = focusNext || key; focusNext = null;
    if (f) { var el = host.querySelector('[data-k="' + f.replace(/"/g, '\\"') + '"]'); if (el) { try { el.focus({ preventScroll: true }); if (pos != null && el.setSelectionRange && el.type !== "checkbox") el.setSelectionRange(pos, pos); } catch (e) {} } }
  }

  function init() {
    host = document.getElementById("gw");
    if (!host || !document.documentElement.classList.contains("gw-live")) { host = null; return; }
    host.removeAttribute("inert"); host.removeAttribute("aria-hidden");
    host.setAttribute("role", "group");
    host.setAttribute("aria-label", "Interactive GitWolf window with sample repositories");
    var over = false;
    host.addEventListener("mouseenter", function () { over = true; });
    host.addEventListener("mouseleave", function () { over = false; });
    host.addEventListener("click", function (e) {
      var t = e.target.closest ? e.target.closest("[data-act]") : null;
      if (!t || !host.contains(t)) {
        if (S.pop && !(e.target.closest && e.target.closest(".gw-pop,.gw-pal"))) { if (S.pop === "jump" && e.target.closest && e.target.closest(".gw-pal")) return; S.pop = null; paint(); }
        return;
      }
      if (S.pop && S.pop !== t.getAttribute("data-pop") && !t.closest(".gw-pop,.gw-pal")) S.pop = null;
      if (t.type === "checkbox") { act(t.dataset.act, t.dataset.arg, t); paint(); return; }
      act(t.dataset.act, t.dataset.arg, t); paint();
    });
    document.addEventListener("click", function (e) {
      if (S.pop && !host.contains(e.target)) { S.pop = null; paint(); }
    }, true); // capture: runs before the window re-renders and detaches e.target
    host.addEventListener("dblclick", function (e) {
      var b = e.target.closest ? e.target.closest("[data-branch]") : null;
      if (b) { var r = R(); r.cur = b.getAttribute("data-branch"); toast("Switched to " + r.cur); paint(); }
    });
    host.addEventListener("input", function (e) {
      var k = e.target.getAttribute && e.target.getAttribute("data-in");
      if (!k || e.isComposing) return;
      S[k] = e.target.value; if (k === "pq") S.pi = 0; paint();
    });
    host.addEventListener("submit", function (e) {
      e.preventDefault();
      if (e.target.getAttribute("data-form") === "newbranch") {
        var n = S.nb.trim().replace(/\s+/g, "-"), r = R();
        if (!n) return;
        if (r.heads[n]) { toast("That branch already exists"); return; }
        r.heads[n] = head(r); r.cur = n; S.nb = ""; S.pop = null; toast("Created and switched to " + n); paint();
      }
    });
    host.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && S.pop) { S.pop = null; paint(); return; }
      if (S.pop === "jump") {
        var n = paletteItems(R()).length;
        if (e.key === "ArrowDown") { e.preventDefault(); S.pi = Math.min(n - 1, S.pi + 1); paint(); }
        else if (e.key === "ArrowUp") { e.preventDefault(); S.pi = Math.max(0, S.pi - 1); paint(); }
        else if (e.key === "Enter") { e.preventDefault(); act("palette", String(S.pi)); paint(); }
        return;
      }
      if ((e.key === "ArrowDown" || e.key === "ArrowUp") && e.target.closest && e.target.closest('[data-list="history"]') && !S.q) {
        e.preventDefault();
        var r2 = R(), i = 0;
        r2.commits.forEach(function (c, k) { if (c.h === S.sel[r2.id]) i = k; });
        var j = Math.max(0, Math.min(r2.commits.length - 1, i + (e.key === "ArrowDown" ? 1 : -1)));
        S.sel[r2.id] = r2.commits[j].h; focusNext = "commit:" + r2.commits[j].h; paint();
      }
    });
    document.addEventListener("keydown", function (e) {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k" && (over || host.contains(document.activeElement))) {
        e.preventDefault(); S.pop = "jump"; S.pq = ""; S.pi = 0; focusNext = "in:pq"; paint();
      }
    });
    setTimeout(function () { host.classList.remove("gw-enter"); }, 2200);
    var mac = /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent || "");
    if (!mac) { KBD = "Ctrl K"; [].forEach.call(host.querySelectorAll(".gw-jump kbd"), function (k) { k.textContent = KBD; }); }
  }

  /* The picture of the history list for phones: the same graph drawing as the working window. */
  function mockHist() {
    S = freshState();
    var keep = ["a1f09c2", "4be07a9", "d93b6f4", "c07d9b1", "3fa81c4", "b6d2f08"], r = S.repos[0];
    var cs = r.commits.filter(function (c) { return keep.indexOf(c.h) >= 0; }).map(function (c, i, a) { return i === a.length - 1 ? { h: c.h, p: [], s: c.s, a: c.a, m: c.m } : c; });
    var g = layout(cs);
    return cs.map(function (c, i) {
      return '<div class="commit' + (i === 0 ? " sel" : "") + '" style="--i:' + i + '"><div class="graph">' + graphSvg(g.rows[i], g.count, i) + "</div>" + avatar(c.a, 24) + "<div><b>" + esc(c.s) + "</b><small>" + AUTHORS[c.a] + " · " + rel(c.m) + "</small></div>" +
        (i === 0 ? '<span class="hash"><span class="a">' + c.h.slice(0, 7) + '</span><span class="b">✓ Copied</span></span>' : "") + "</div>";
    }).join("\n");
  }

  var api = { mockHist: mockHist, render: function () { S = freshState(); return render(); }, init: init };
  if (typeof module === "object" && module.exports) module.exports = api;
  else if (typeof document !== "undefined") { if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init); else init(); }
})(this);
