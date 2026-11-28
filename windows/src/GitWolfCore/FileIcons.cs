using System.Text.RegularExpressions;

namespace GitWolf.Core;

public enum IconGlyph
{
    /// <summary>Short letters on a coloured rounded square, e.g. "TS".</summary>
    Letters,
    /// <summary>A symbol, named as in SF Symbols; each platform draws the nearest icon.</summary>
    Symbol,
    /// <summary>The React atom: three ellipses and a dot.</summary>
    React,
    /// <summary>A folder in the brand colour, with an optional small symbol on it.</summary>
    Folder,
    /// <summary>A brand logo by Simple Icons slug, in one flat colour.</summary>
    Logo,
}

/// <summary>Which icon to show. This is data only; the app draws it (flat colour, no gradients).</summary>
/// <param name="Value">The letters, the symbol name, the logo slug, or the symbol on a folder.</param>
/// <param name="Color">Brand colour as 0xRRGGBB.</param>
/// <param name="DarkText">Use dark text on the tile, for light colours such as JavaScript yellow.</param>
public sealed record FileIcon(string Name, IconGlyph Glyph, string? Value, uint Color, bool DarkText = false);

/// <summary>Icons for files, folders, branches and tags. Ported from FileIcons.swift.</summary>
public static partial class FileIcons
{
    private static readonly FileIcon Generic = Symbol("File", "doc", 0x8E8E93);
    private static readonly FileIcon Env = Symbol("Environment", "key.fill", 0xECD53F);
    private static readonly FileIcon License = Symbol("License", "checkmark.seal.fill", 0xD0B64B);
    private static readonly FileIcon Minified = Symbol("Minified", "arrow.down.right.and.arrow.up.left", 0x8E8E93);
    private static readonly FileIcon React = new("React", IconGlyph.React, null, 0x61DAFB);
    private static readonly FileIcon BranchDefault = Branch("Branch", "arrow.triangle.branch", 0x8E8E93);

    private static readonly HashSet<string> Containers =
    [
        "node_modules", "vendor", "third_party", "pods", ".github", ".gitlab", ".circleci", ".git",
        "dist", "build", "out", "target", ".build", "deriveddata"
    ];

    // Declared after the icons above: static fields are initialised in order.
    public static IReadOnlyDictionary<string, FileIcon> Names { get; } = new Dictionary<string, FileIcon>
    {
        ["nginx.conf"] = Logo("nginx", "nginx", 0x009639),
        ["vagrantfile"] = Logo("Vagrant", "vagrant", 0x1868F2),
        ["pom.xml"] = Logo("Maven", "apachemaven", 0xC71A36),
        [".npmrc"] = Logo("npm", "npm", 0xCB3837),
        ["pnpm-workspace.yaml"] = Logo("pnpm", "pnpm", 0xF69220),
        [".nvmrc"] = Logo("Node.js", "nodedotjs", 0x5FA04E),
        ["rollup.config.js"] = Logo("Rollup", "rollupdotjs", 0xEC4A3F),
        ["postcss.config.js"] = Logo("PostCSS", "postcss", 0xDD3A0A),
        ["caddyfile"] = Logo("Caddy", "caddy", 0x1F88C0),
        [".vimrc"] = Logo("Vim script", "vim", 0x019733),
        ["redis.conf"] = Logo("Redis", "redis", 0xDC382D),
        ["schema.prisma"] = Logo("Prisma", "prisma", 0x2D3748),
        ["build.gradle"] = Logo("Gradle", "gradle", 0x02303A),
        ["settings.gradle"] = Logo("Gradle", "gradle", 0x02303A),
        ["tailwind.config.js"] = Logo("Tailwind CSS", "tailwindcss", 0x06B6D4),
        ["tailwind.config.ts"] = Logo("Tailwind CSS", "tailwindcss", 0x06B6D4),
        ["vite.config.ts"] = Logo("Vite", "vite", 0x646CFF),
        ["vite.config.js"] = Logo("Vite", "vite", 0x646CFF),
        ["webpack.config.js"] = Logo("webpack", "webpack", 0x8DD6F9, dark: true),
        ["babel.config.js"] = Logo("Babel", "babel", 0xF9DC3E, dark: true),
        [".babelrc"] = Logo("Babel", "babel", 0xF9DC3E, dark: true),
        ["angular.json"] = Logo("Angular", "angular", 0xDD0031),
        ["pubspec.yaml"] = Logo("Dart", "dart", 0x0175C2),
        ["deno.json"] = Logo("Deno", "deno", 0x70FFAF, dark: true),
        ["bun.lockb"] = Symbol("Lockfile", "lock.fill", 0xFBF0DF),
        ["package.json"] = Logo("npm", "npm", 0xCB3837),
        ["package-lock.json"] = Symbol("Lockfile", "lock.fill", 0xCB3837),
        ["yarn.lock"] = Symbol("Lockfile", "lock.fill", 0x2C8EBB),
        ["pnpm-lock.yaml"] = Symbol("Lockfile", "lock.fill", 0xF69220),
        ["cargo.toml"] = Logo("Cargo", "rust", 0xDEA584, dark: true),
        ["cargo.lock"] = Symbol("Lockfile", "lock.fill", 0xDEA584),
        ["go.mod"] = Logo("Go", "go", 0x00ADD8),
        ["go.sum"] = Symbol("Lockfile", "lock.fill", 0x00ADD8),
        ["package.swift"] = Logo("Swift package", "swift", 0xF05138),
        ["package.resolved"] = Symbol("Lockfile", "lock.fill", 0xF05138),
        ["tsconfig.json"] = Logo("TypeScript config", "typescript", 0x3178C6),
        ["jsconfig.json"] = Logo("JavaScript config", "javascript", 0xF7DF1E, dark: true),
        ["gemfile"] = Logo("Ruby", "ruby", 0xCC342D),
        ["gemfile.lock"] = Symbol("Lockfile", "lock.fill", 0xCC342D),
        ["podfile"] = Logo("CocoaPods", "cocoapods", 0xEE3322),
        ["podfile.lock"] = Symbol("Lockfile", "lock.fill", 0xEE3322),
        ["requirements.txt"] = Logo("Python", "python", 0x3776AB),
        ["pyproject.toml"] = Logo("Python", "python", 0x3776AB),
        ["pipfile"] = Logo("Python", "python", 0x3776AB),
        ["dockerfile"] = Logo("Docker", "docker", 0x2496ED),
        ["docker-compose.yml"] = Logo("Docker", "docker", 0x2496ED),
        ["docker-compose.yaml"] = Logo("Docker", "docker", 0x2496ED),
        [".dockerignore"] = Logo("Docker", "docker", 0x2496ED),
        ["makefile"] = Logo("Makefile", "make", 0x6D8086),
        ["cmakelists.txt"] = Logo("CMake", "cmake", 0x064F8C),
        ["readme.md"] = Symbol("Readme", "book.fill", 0x42A5F5),
        [".gitignore"] = Symbol("Git", "arrow.triangle.branch", 0xF05032),
        [".gitattributes"] = Symbol("Git", "arrow.triangle.branch", 0xF05032),
        [".gitmodules"] = Symbol("Git", "arrow.triangle.branch", 0xF05032),
        [".gitkeep"] = Symbol("Git", "arrow.triangle.branch", 0xF05032),
        [".editorconfig"] = Symbol("Editor config", "gearshape.fill", 0x8E8E93),
        [".eslintrc"] = Logo("ESLint", "eslint", 0x4B32C3),
        [".prettierrc"] = Logo("Prettier", "prettier", 0x56B3B4),
        [".ds_store"] = Logo("macOS", "apple", 0x8E8E93),
        ["info.plist"] = Letters("XML", "</>", 0xE37933),
    };

    public static IReadOnlyDictionary<string, FileIcon> Extensions { get; } = BuildExtensions();
    public static IReadOnlyDictionary<string, FileIcon> Folders { get; } = BuildFolders();
    public static IReadOnlyDictionary<string, FileIcon> BranchNames { get; } = BuildBranchNames();
    public static IReadOnlyDictionary<string, FileIcon> BranchPrefixes { get; } = BuildBranchPrefixes();

    /// <summary>The icon for a path (only the file name matters).</summary>
    public static FileIcon ForPath(string path)
    {
        var lower = path[(path.TrimEnd('/').LastIndexOf('/') + 1)..].TrimEnd('/').ToLowerInvariant();
        if (Names.TryGetValue(lower, out var byName)) return byName;
        if (StartsWith(lower, ".env")) return Env;
        if (StartsWith(lower, "readme")) return Names["readme.md"];
        if (StartsWith(lower, "license") || StartsWith(lower, "licence") || lower == "copying") return License;
        if (StartsWith(lower, "dockerfile")) return Names["dockerfile"];
        // Compound extensions first, then the last one.
        if (EndsWith(lower, ".d.ts")) return Extensions["d.ts"];
        if (EndsWith(lower, ".min.js") || EndsWith(lower, ".min.css")) return Minified;
        // A leading dot names a hidden file; it doesn't start an extension.
        var dot = lower.LastIndexOf('.');
        return dot > 0 && Extensions.TryGetValue(lower[(dot + 1)..], out var icon) ? icon : Generic;
    }

    /// <summary>The icon for a folder, by its own name.</summary>
    public static FileIcon FolderNamed(string name) =>
        Folders.TryGetValue(name.ToLowerInvariant(), out var icon) ? icon : Folder("Folder", null, 0x6FA8DC);

    /// <summary>The icon for the folder that contains <paramref name="path"/>; null at the top level.</summary>
    public static FileIcon? FolderContaining(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var folders = parts[..^1].Select(p => p.ToLowerInvariant()).ToList();
        var last = folders[^1];
        if (Folders.ContainsKey(last)) return FolderNamed(last);
        // Inside a dependency, CI or build folder, everything below it keeps that folder's look.
        return FolderNamed(folders.LastOrDefault(Containers.Contains) ?? last);
    }

    /// <summary>The icon for a branch, chosen from its name.</summary>
    public static FileIcon BranchNamed(string name)
    {
        var lower = name.ToLowerInvariant();
        if (BranchNames.TryGetValue(lower, out var exact)) return exact;
        var end = lower.AsSpan().IndexOfAny('/', '-', '_');
        var first = end < 0 ? lower : lower[..end];
        if (BranchPrefixes.TryGetValue(first, out var byPrefix)) return byPrefix;
        // `123-fix-login` looks like an issue branch.
        return first.Length > 0 && first.All(char.IsDigit) ? BranchPrefixes["issue"] : BranchDefault;
    }

    /// <summary>
    /// The icon for a tag, from its name: releases, pre-releases and deploy markers, and a plain tag
    /// otherwise (outlined when it is lightweight).
    /// </summary>
    public static FileIcon TagNamed(string name, bool annotated = true)
    {
        var lower = name.ToLowerInvariant();
        bool Has(params string[] words) => words.Any(w => lower.Contains(w, StringComparison.Ordinal));
        if (Has("nightly", "daily")) return Symbol("Nightly", "moon.stars.fill", 0x5C6BC0);
        if (Has("-rc", ".rc", "rc.") || EndsWith(lower, "rc") && lower.Any(char.IsDigit))
            return Symbol("Release candidate", "flag.checkered", 0x7E57C2);
        if (Has("alpha", "beta", "preview", "snapshot", "-pre", "canary", "-dev"))
            return Symbol("Pre-release", "testtube.2", 0xAB47BC);
        if (lower is "latest" or "stable" or "current") return Symbol("Latest", "checkmark.seal.fill", 0x43A047);
        if (Has("lts")) return Symbol("Long-term support", "shield.fill", 0x26A69A);
        if (Has("deploy", "prod", "staging", "live")) return Symbol("Deploy", "paperplane.fill", 0x29B6F6);
        if (Has("backup", "archive", "old", "legacy")) return Symbol("Archive", "archivebox.fill", 0x8E8E93);
        if (MajorRelease().IsMatch(lower)) return Symbol("Major release", "star.circle.fill", 0xFFB300);
        return Symbol(Version().IsMatch(lower) ? "Release" : "Tag", annotated ? "tag.fill" : "tag", 0xFF9800);
    }

    [GeneratedRegex(@"^v?[1-9][0-9]*\.0(\.0)?\z")]
    private static partial Regex MajorRelease();

    [GeneratedRegex(@"^v?[0-9]+(\.[0-9]+)+\z")]
    private static partial Regex Version();

    private static bool StartsWith(string text, string prefix) => text.StartsWith(prefix, StringComparison.Ordinal);
    private static bool EndsWith(string text, string suffix) => text.EndsWith(suffix, StringComparison.Ordinal);

    private static FileIcon Letters(string name, string text, uint color, bool dark = false) =>
        new(name, IconGlyph.Letters, text, color, dark);
    /// <summary>A brand logo by Simple Icons slug; the app draws it.</summary>
    private static FileIcon Logo(string name, string slug, uint color, bool dark = false) =>
        new(name, IconGlyph.Logo, slug, color, dark);
    private static FileIcon Symbol(string name, string symbol, uint color) => new(name, IconGlyph.Symbol, symbol, color);
    private static FileIcon Folder(string name, string? inner, uint color) => new(name, IconGlyph.Folder, inner, color);
    private static FileIcon Branch(string name, string symbol, uint color) => Symbol(name, symbol, color);

    private static Dictionary<string, FileIcon> Build(Action<Action<string[], FileIcon>> fill)
    {
        var map = new Dictionary<string, FileIcon>();
        fill((keys, icon) =>
        {
            foreach (var key in keys) map[key] = icon;
        });
        return map;
    }

    private static Dictionary<string, FileIcon> BuildFolders() => Build(Add =>
    {
        Add(["src", "source", "sources"], Folder("Source", "chevron.left.forwardslash.chevron.right", 0x42A5F5));
        Add(["lib", "libs", "library", "libraries"], Folder("Library", "books.vertical.fill", 0x8D6E63));
        Add(["docs", "doc", "documentation"], Folder("Docs", "book.fill", 0x4DB6AC));
        Add(["test", "tests", "__tests__", "spec", "specs"], Folder("Tests", "checkmark", 0x66BB6A));
        Add(["assets", "static", "resources", "res"], Folder("Assets", "photo.fill", 0xAB47BC));
        Add(["images", "img", "icons", "media"], Folder("Images", "photo.fill", 0x9575CD));
        Add(["public", "www", "web"], Folder("Public", "globe", 0x26C6DA));
        Add(["components", "views", "ui"], Folder("Components", "square.grid.2x2.fill", 0x29B6F6));
        Add(["scripts", "script", "tools", "bin"], Folder("Scripts", "terminal.fill", 0x7CB342));
        Add(["config", "configs", "conf", "settings"], Folder("Config", "gearshape.fill", 0x9E9E9E));
        Add([".github", ".gitlab", ".circleci"], Folder("CI", "arrow.triangle.branch", 0xF05032));
        Add([".git"], Folder("Git", "arrow.triangle.branch", 0xF05032));
        Add([".vscode", ".idea", ".xcode"], Folder("Editor", "wrench.and.screwdriver.fill", 0x1E88E5));
        Add(["node_modules", "vendor", "third_party", "pods", "packages"], Folder("Dependencies", "shippingbox.fill", 0x8BC34A));
        Add(["build", "dist", "out", "target", "release", "debug", ".build", "deriveddata"], Folder("Build output", "hammer.fill", 0xFF9800));
        Add(["api", "routes", "controllers", "server"], Folder("API", "network", 0xEF5350));
        Add(["models", "model", "db", "database", "migrations"], Folder("Data", "cylinder.split.1x2.fill", 0x26A69A));
        Add(["styles", "css", "themes"], Folder("Styles", "paintpalette.fill", 0xEC407A));
        Add(["utils", "util", "helpers", "common", "shared", "core"], Folder("Shared", "wrench.fill", 0x78909C));
        Add(["docker", ".docker"], Folder("Docker", "shippingbox.fill", 0x2496ED));
        Add(["fonts"], Folder("Fonts", "textformat", 0x8E8E93));
        Add(["hooks"], Folder("Hooks", "link", 0x7E57C2));
        Add(["types", "typings", "interfaces"], Folder("Types", "t.square.fill", 0x3178C6));
        Add(["localization", "locales", "i18n", "l10n"], Folder("Localization", "character.bubble.fill", 0x5C6BC0));
        Add(["examples", "example", "samples", "demo"], Folder("Examples", "lightbulb.fill", 0xFFCA28));
    });

    private static Dictionary<string, FileIcon> BuildBranchNames() => Build(Add =>
    {
        Add(["main", "master", "trunk"], Branch("Main branch", "star.fill", 0x42A5F5));
        Add(["develop", "dev", "development", "next"], Branch("Development", "hammer.fill", 0xAB47BC));
        Add(["staging", "stage", "preprod"], Branch("Staging", "flag.fill", 0xFF9800));
        Add(["production", "prod", "live"], Branch("Production", "bolt.shield.fill", 0xE53935));
    });

    private static Dictionary<string, FileIcon> BuildBranchPrefixes() => Build(Add =>
    {
        Add(["feature", "feat", "features"], Branch("Feature", "sparkles", 0x26A69A));
        Add(["fix", "bugfix", "bug"], Branch("Bug fix", "ladybug.fill", 0xE53935));
        Add(["hotfix", "urgent"], Branch("Hotfix", "flame.fill", 0xFF7043));
        Add(["release", "releases", "rc", "v"], Branch("Release", "shippingbox.fill", 0x7E57C2));
        Add(["chore", "build", "ci", "ops"], Branch("Chore", "wrench.and.screwdriver.fill", 0x78909C));
        Add(["docs", "doc", "documentation"], Branch("Docs", "book.fill", 0x4DB6AC));
        Add(["test", "tests", "testing"], Branch("Tests", "checkmark.seal.fill", 0x66BB6A));
        Add(["refactor", "cleanup", "tidy"], Branch("Refactor", "arrow.triangle.2.circlepath", 0x5C6BC0));
        Add(["perf", "performance", "speed"], Branch("Performance", "bolt.fill", 0xFFB300));
        Add(["style", "ui", "design", "theme"], Branch("Design", "paintpalette.fill", 0xEC407A));
        Add(["wip", "experiment", "spike", "poc", "tmp", "temp", "try"], Branch("Experiment", "testtube.2", 0x9C27B0));
        Add(["revert"], Branch("Revert", "arrow.uturn.backward.circle.fill", 0xEF5350));
        Add(["dependabot", "renovate", "deps", "bump"], Branch("Dependencies", "arrow.down.circle.fill", 0x8BC34A));
        Add(["pr", "pull", "review", "mr"], Branch("Pull request", "arrow.triangle.pull", 0x29B6F6));
        Add(["issue", "issues", "ticket"], Branch("Issue", "smallcircle.filled.circle", 0x66BB6A));
    });

    private static Dictionary<string, FileIcon> BuildExtensions() => Build(Add =>
    {
        Add(["tsx", "jsx"], React);
        Add(["ts", "mts", "cts"], Logo("TypeScript", "typescript", 0x3178C6));
        Add(["d.ts"], Logo("TypeScript declarations", "typescript", 0x3178C6));
        Add(["js", "mjs", "cjs"], Logo("JavaScript", "javascript", 0xF7DF1E, dark: true));
        Add(["swift"], Logo("Swift", "swift", 0xF05138));
        Add(["py", "pyw", "pyi"], Logo("Python", "python", 0x3776AB));
        Add(["go"], Logo("Go", "go", 0x00ADD8));
        Add(["rs"], Logo("Rust", "rust", 0xDEA584, dark: true));
        Add(["java"], Logo("Java", "openjdk", 0xE76F00));
        Add(["kt", "kts"], Logo("Kotlin", "kotlin", 0x7F52FF));
        Add(["c", "h"], Logo("C", "c", 0x5C6BC0));
        Add(["cpp", "cc", "cxx", "hpp", "hh", "hxx"], Logo("C++", "cplusplus", 0x00599C));
        Add(["m", "mm"], Letters("Objective-C", "OC", 0x438EFF));
        Add(["rb", "gemspec"], Logo("Ruby", "ruby", 0xCC342D));
        Add(["php"], Logo("PHP", "php", 0x777BB4));
        Add(["dart"], Logo("Dart", "dart", 0x0175C2));
        Add(["lua"], Logo("Lua", "lua", 0x000080));
        Add(["scala"], Logo("Scala", "scala", 0xDC322F));
        Add(["ex", "exs"], Logo("Elixir", "elixir", 0x6E4A7E));
        Add(["hs"], Logo("Haskell", "haskell", 0x5D4F85));
        Add(["zig"], Logo("Zig", "zig", 0xF7A41D, dark: true));
        Add(["vue"], Logo("Vue", "vuedotjs", 0x42B883));
        Add(["svelte"], Logo("Svelte", "svelte", 0xFF3E00));
        Add(["html", "htm"], Logo("HTML", "html5", 0xE44D26));
        Add(["css"], Logo("CSS", "css", 0x1572B6));
        Add(["pl", "pm"], Logo("Perl", "perl", 0x39457E));
        Add(["clj", "cljs", "cljc", "edn"], Logo("Clojure", "clojure", 0x5881D8));
        Add(["erl", "hrl"], Logo("Erlang", "erlang", 0xA90533));
        Add(["jl"], Logo("Julia", "julia", 0x9558B2));
        Add(["nim", "nims"], Logo("Nim", "nim", 0xFFE953, dark: true));
        Add(["cr"], Logo("Crystal", "crystal", 0x2B2B2B));
        Add(["fs", "fsx", "fsi"], Logo("F#", "fsharp", 0x378BBA));
        Add(["sol"], Logo("Solidity", "solidity", 0x5B5B5B));
        Add(["wasm", "wat"], Logo("WebAssembly", "webassembly", 0x654FF0));
        Add(["tex", "sty", "cls"], Logo("LaTeX", "latex", 0x008080));
        Add(["coffee"], Logo("CoffeeScript", "coffeescript", 0x6F4E37));
        Add(["elm"], Logo("Elm", "elm", 0x1293D8));
        Add(["ml", "mli"], Logo("OCaml", "ocaml", 0xEC6813));
        Add(["rkt"], Logo("Racket", "racket", 0x9F1D20));
        Add(["f90", "f95", "f03", "for"], Logo("Fortran", "fortran", 0x734F96));
        Add(["d"], Logo("D", "d", 0xB03931));
        Add(["astro"], Logo("Astro", "astro", 0xBC52EE));
        Add(["gd"], Logo("GDScript", "godotengine", 0x478CBF));
        Add(["cs", "csx"], Logo("C#", "csharp", 0x68217A));
        Add(["razor", "cshtml"], Logo(".NET", "dotnet", 0x512BD4));
        Add(["scss", "sass"], Logo("Sass", "sass", 0xCD6799));
        Add(["less"], Logo("Less", "less", 0x1D365D));
        Add(["json", "jsonc", "json5"], Logo("JSON", "json", 0xCBA02A, dark: true));
        Add(["yml", "yaml"], Logo("YAML", "yaml", 0xCB171E));
        Add(["toml"], Logo("TOML", "toml", 0x9C4221));
        Add(["xml", "plist", "svg"], Letters("XML", "</>", 0xE37933));
        Add(["md", "markdown", "mdx"], Logo("Markdown", "markdown", 0x519ABA));
        Add(["txt", "text", "log"], Symbol("Text", "doc.text", 0x8E8E93));
        Add(["sh", "bash", "zsh", "fish"], Logo("Shell", "gnubash", 0x4EAA25));
        Add(["ps1", "bat", "cmd"], Symbol("Script", "terminal.fill", 0x3B78C4));
        Add(["sql", "sqlite", "db"], Symbol("Database", "cylinder.split.1x2.fill", 0x0288D1));
        Add(["csv", "tsv", "xls", "xlsx"], Symbol("Table", "tablecells.fill", 0x1D8348));
        Add(["png", "jpg", "jpeg", "gif", "webp", "heic", "bmp", "tiff", "ico", "icns", "avif"], Symbol("Image", "photo.fill", 0x9B59B6));
        Add(["mp4", "mov", "avi", "mkv", "webm"], Symbol("Video", "film.fill", 0xE91E63));
        Add(["mp3", "wav", "flac", "aac", "ogg", "m4a"], Symbol("Audio", "music.note", 0xFF7043));
        Add(["zip", "tar", "gz", "tgz", "bz2", "xz", "7z", "rar", "dmg", "pkg"], Symbol("Archive", "doc.zipper", 0xB8860B));
        Add(["pdf"], Symbol("PDF", "doc.richtext.fill", 0xE53935));
        Add(["doc", "docx", "pages", "rtf"], Symbol("Document", "doc.text.fill", 0x2B7CD3));
        Add(["ppt", "pptx", "key"], Symbol("Presentation", "rectangle.on.rectangle.angled.fill", 0xD35230));
        Add(["ttf", "otf", "woff", "woff2"], Symbol("Font", "textformat", 0x8E8E93));
        Add(["lock"], Symbol("Lockfile", "lock.fill", 0x8E8E93));
        Add(["gitignore", "gitattributes", "gitmodules", "gitkeep"], Symbol("Git", "arrow.triangle.branch", 0xF05032));
        Add(["xcodeproj", "xcworkspace", "pbxproj", "xcconfig", "entitlements"], Symbol("Xcode", "hammer.fill", 0x1C7CD6));
        Add(["storyboard", "xib"], Symbol("Interface Builder", "rectangle.3.group.fill", 0x1C7CD6));
        Add(["gradle"], Logo("Gradle", "gradle", 0x02303A));
        Add(["tf", "tfvars"], Logo("Terraform", "terraform", 0x7B42BC));
        Add(["proto"], Letters("Protobuf", "PB", 0x4285F4));
        Add(["graphql", "gql"], Logo("GraphQL", "graphql", 0xE10098));
        Add(["patch", "diff"], Symbol("Patch", "plusminus", 0x4CAF50));
        Add(["ipynb"], Logo("Notebook", "jupyter", 0xF37726));
        Add(["r"], Logo("R", "r", 0x276DC3));
        Add(["conf", "ini", "cfg", "properties"], Symbol("Settings", "gearshape.fill", 0x8E8E93));
        Add(["groovy", "gvy"], Logo("Groovy", "apachegroovy", 0x4298B8));
        Add(["ps1", "psm1", "psd1"], Logo("PowerShell", "powershell", 0x5391FE));
        Add(["nix"], Logo("Nix", "nixos", 0x5277C3));
        Add(["mlx", "mat"], Letters("MATLAB", "ML", 0xE16737));
        Add(["scm", "ss", "sld"], Letters("Scheme", "SCM", 0x1E4A8C));
        Add(["cob", "cbl", "cpy"], Letters("COBOL", "CBL", 0x005CA5));
        Add(["gleam"], Letters("Gleam", "GL", 0xFFAFF3, dark: true));
        Add(["v", "vh", "sv", "svh"], Letters("Verilog", "V", 0x4F6D8F));
        Add(["vhd", "vhdl"], Letters("VHDL", "VH", 0x543978));
        Add(["adb", "ads", "ada"], Letters("Ada", "ADA", 0x02F88C, dark: true));
        Add(["pas", "dpr", "dfm", "lpr", "pp"], Logo("Delphi", "delphi", 0xEE1F35));
        Add(["pro", "prolog"], Letters("Prolog", "PRO", 0xE61B23));
        Add(["tcl"], Letters("Tcl", "TCL", 0xE4CC98, dark: true));
        Add(["st"], Letters("Smalltalk", "ST", 0x596706));
        Add(["raku", "rakumod", "p6"], Letters("Raku", "RK", 0x0000FB));
        Add(["lisp", "lsp", "cl"], Letters("Lisp", "LISP", 0x3FB68B));
        Add(["el"], Logo("Emacs Lisp", "gnuemacs", 0x7F5AB6));
        Add(["vim"], Logo("Vim script", "vim", 0x019733));
        Add(["ino"], Logo("Arduino", "arduino", 0x00979D));
        Add(["cu", "cuh"], Logo("CUDA", "nvidia", 0x76B900));
        Add(["bicep"], Letters("Bicep", "BI", 0x519ABA));
        Add(["asm", "nasm"], Letters("Assembly", "ASM", 0x6E4C13));
        Add(["hx", "hxml"], Logo("Haxe", "haxe", 0xEA8220));
        Add(["res", "resi"], Logo("ReScript", "rescript", 0xE6484F));
        Add(["re", "rei"], Letters("Reason", "RE", 0xDD4B39));
        Add(["vala", "vapi"], Logo("Vala", "vala", 0x7239B3));
        Add(["scad"], Logo("OpenSCAD", "openscad", 0xF9D72C, dark: true));
        Add(["luau"], Logo("Luau", "lua", 0x000080));
        Add(["cairo"], Letters("Cairo", "CA", 0xFF7A59));
        Add(["move"], Letters("Move", "MV", 0x4A90D9));
        Add(["vy"], Letters("Vyper", "VY", 0x2E7D32));
        Add(["twig"], Letters("Twig", "TW", 0xBACF29, dark: true));
        Add(["liquid"], Letters("Liquid", "LQ", 0x67B8DE));
        Add(["hbs", "handlebars"], Logo("Handlebars", "handlebarsdotjs", 0xF0772B));
        Add(["mustache"], Letters("Mustache", "{{", 0x724B3B));
        Add(["pug", "jade"], Logo("Pug", "pug", 0xA86454));
        Add(["ejs"], Logo("EJS", "ejs", 0xB4CA65, dark: true));
        Add(["j2", "jinja", "jinja2"], Logo("Jinja", "jinja", 0xB41717));
        Add(["styl"], Logo("Stylus", "stylus", 0xFF6347));
        Add(["pcss", "postcss"], Logo("PostCSS", "postcss", 0xDD3A0A));
        Add(["hcl"], Logo("Terraform", "terraform", 0x7B42BC));
        Add(["prisma"], Logo("Prisma", "prisma", 0x2D3748));
        Add(["epp"], Logo("Puppet", "puppet", 0xFFAE1A, dark: true));
        Add(["bzl", "bazel"], Logo("Bazel", "bazel", 0x43A047));
        Add(["xcodeproj", "xcworkspace", "pbxproj", "xcconfig"], Logo("Xcode", "xcode", 0x147EFB));
        Add(["sln", "csproj", "vbproj", "fsproj"], Logo("Visual Studio", "visualstudio", 0x5C2D91));
        Add(["xaml"], Logo("XAML", "xaml", 0x0C54C2));
        Add(["scala.sbt"], Logo("Apache Spark", "apachespark", 0xE25A1C));
        Add(["sh", "bash", "zsh", "fish", "ksh"], Logo("Shell", "gnubash", 0x4EAA25));
    });
}
