# GitWolf for Windows

C# / WinUI 3 (Windows App SDK) port, following [docs/PORTING.md](../docs/PORTING.md). The macOS
app under `apple/` is the executable spec — port its logic and tests, don't invent new semantics.

## Layout

- `src/GitWolfCore/` — the engine (no UI), ported from `apple/Sources/GitWolfCore/`.
- `src/GitWolf.Store/` — per-repository state: refresh, selection, operations.
- `src/GitWolf.App/` — the WinUI 3 app shell.
- `tests/GitWolfCore.Tests/` — engine tests, ported from `apple/Tests/GitWolfCoreTests/`, run
  against real git in throwaway temp repos (no mocking of git).

## Status

Engine ported so far (see the file map in PORTING.md for the full list and order):

- [x] `GitRunner.swift` -> `GitRunner.cs`
- [x] `Models.swift` -> `Models.cs`
- [x] `Parsers.swift` -> `Parsers.cs`
- [x] `GraphLayout.swift` -> `GraphLayout.cs`
- [x] `PatchBuilder.swift` -> `PatchBuilder.cs`
- [x] `SideBySide.swift` -> `SideBySide.cs`
- [x] `FileIcons.swift` -> `FileIcons.cs` (drawn by `Views/FileIconView.cs` and `Views/SymbolShapes.cs`)
- [x] `SyntaxHighlighter.swift` -> `SyntaxHighlighter.cs`
- [x] `FileWatcher.swift` -> `FileWatcher.cs` (`System.IO.FileSystemWatcher`, backed by
      `ReadDirectoryChangesW` on Windows)
- [x] `GitRepository.swift` -> `GitRepository.cs`
- [x] `Providers.swift` -> `Providers.cs` (askpass is the same POSIX `sh` script as Swift, run by
      Git for Windows' `sh`; deliberately not a `.cmd`, which would let a crafted remote URL
      inject commands). Unlike Swift, it answers only for the account's own server: host, port and
      scheme
- [x] `ProviderClient.swift` -> `ProviderClient.cs` (`HttpClient` in place of `URLSession`,
      `System.Text.Json` in place of `JSONSerialization`)

The full engine file map from PORTING.md is now ported, with the tests from
`GitWolfCoreTests.swift`.

Store layer:

- [x] `RepositoryStore.swift` -> `src/GitWolf.Store/` (UI-framework independent). Use it from the
      UI thread only, and call `RequestRefresh()` when a window is activated.
- [x] `AppSettings.swift` -> `src/GitWolf.App/Services/AppSettings.cs`
- [x] `Accounts.swift` -> `src/GitWolfCore/Accounts.cs`, with secrets in Windows Credential Manager
      (`WindowsCredentialStore.cs`, split across entries when long) and the askpass script written
      with LF endings and no BOM
- [x] `Workspace.swift` (open and recent repositories) -> `src/GitWolf.App/Services/Workspace.cs`

Provider features: OAuth sign-in (`OAuth.cs`, `OAuthLoopback.cs`), pull requests and issues
(`Collab.cs`, `CollabActions.cs`), CI checks (`CiStatus.cs`), notifications (`Notifications.cs`),
fork (`Fork.cs`) and a Markdown subset (`Markdown.cs`).

446 tests pass against real git and stubbed HTTP.

Differences from macOS, all deliberately stricter:

- A bearer token for git over HTTP is sent only to the origin it belongs to.
- An account token is used only for a remote whose every address is the account's scheme, host and
  port. Fetch runs one process per remote when a token is involved, without submodules for that
  remote; other remotes keep your own sign-in.
- A repository that sets its own `http.*` option (other than transfer tuning) or
  `remote.<name>.proxy` never gets the account token; a failed command says why.
- Fetch, pull and push refuse `ext::` remotes, and fetch runs no repository hooks.
- A force push first fetches, lists the commits it would remove, and is tied to the commit that was
  shown, so a later background fetch can't widen what gets overwritten.
- Server links open only over https, or http for an account on a plain-HTTP server.
- The OAuth loopback listener ignores a callback with the wrong `state` and keeps waiting.
- Server names and clone URLs from a provider are validated, and a credential is never chosen from a
  guessed provider.
- Markdown is drawn with native controls: only `http` and `https` links work, and images are never
  downloaded.

Browser (OAuth) sign-in is covered by tests with stubbed HTTP but has not been tried against a real
provider.

UI (`src/GitWolf.App/`):

- [x] Open a repository (picker, or `GitWolf.App.exe <folder>`)
- [x] History with graph, commit files and unified diff
- [x] Changes: stage and unstage files, commit, amend
- [x] Fetch, pull, push using git's own sign-in
- [x] macOS layout: repository rail, top bar, sidebar, history list and commit detail panels
- [x] Syntax colours, author avatars, ref chips, unified and side-by-side diff
- [x] Sidebar: branches, remotes, tags, stashes (click to reveal, double-click to check out,
      right-click for checkout, merge, delete, apply/pop/drop)
- [x] Commit search and context menu; new branch, tag and stash
- [x] Multiple repositories, restored between launches
      (`%LOCALAPPDATA%\GitWolf\workspace.json`, paths and view preferences only)
- [x] Jump palette (Ctrl+K), Ctrl+O, Ctrl+Shift+F/L/P (all shortcuts are in `MainWindow.xaml.cs` and
      `MainWindow.Tools.cs`)
- [x] File type, folder and branch icons
- [x] Hunk and line staging; discard a file, hunk or lines after a confirmation
- [x] Home dashboard: open, jump, recent repositories, drop a folder to open
- [x] Merge, rebase, cherry-pick and revert banner; conflict resolver
- [x] Clone and new repository
- [x] Changes filter, breaking change toggle, release notes since a tag, undo of a checkout or whole
      rebase, GitWolf accent, Sign In Again, copied clone address and commit-graph for large repositories
- [x] Pull strategy (merge or rebase), push and delete tags and branches on a remote
- [x] Commit signing, message helpers, long-subject warning, Ctrl+Enter, hook output, ignore rules
- [x] History search by message, author, content, file and date; file history and blame
- [x] Reset, reflog, undo; signature badges
- [x] Compare refs, interactive rebase and rebase onto a branch
- [x] Rename branches, upstream tracking, branch cleanup, annotated tags
- [x] Stash preview and per-hunk stash
- [x] Bisect, patches (export, create, apply), worktrees, submodules, LFS badges, clone options and
      fetching the full history of a shallow clone
- [x] Resizable panels (drag the gap, double-click to reset), keyboard shortcuts, commit message
      spell check, light-theme file icons
- [x] Settings (Ctrl+,): theme, accent colour, background fetch, history and diff options, git path
      (`%LOCALAPPDATA%\GitWolf\settings.json`)
- [x] Provider accounts (Settings > Accounts): token, password or browser sign-in; account
      repositories in the clone dialog
- [x] Pull request and issue tabs: filter, search, read with Markdown, comment, create, label,
      close, reopen and merge, as far as the provider supports each
- [x] Issue references from the commit message menu
- [x] CI checks in commit and pull request details, refreshed while running; re-run and cancel (GitHub
      Actions, GitLab); the checked-out commit's result on the repository tile. Polling, the tile mark and the
      buttons need a connected account
- [x] After a push, an offer to open a pull request for the branch (needs a connected account)
- [x] Notifications, with an unread count in the top bar
- [x] Repository icons: a symbol or a picture and a tile colour (right-click a tile, or drop a picture on it)
- [x] Fork a repository from the jump palette
- [x] Profile pictures for private GitHub repositories, using the github.com account
- [x] Force push with lease, rejected-push handling, push with tags or to a chosen remote,
      fast-forward-only pull (right-click Pull or Push, or the jump palette)
- [x] Manage remotes: add, edit, rename, remove, fetch and push one remote
- [x] Diff: changed words, ignore whitespace, whole file with folding, image previews

- [x] Open in File Explorer, a terminal or an editor, and custom actions (Settings > Tools), run with Git's
      `sh` so that commands work as on macOS and branch names are never read as commands
- [x] In-app updates: a daily check (Settings > General) and *Check for Updates…* in the jump palette, with
      release notes, then install and relaunch, skip the version or be reminded later. The feed and releases are
      verified with the project's Ed25519 key (see [docs/RELEASING.md](../docs/RELEASING.md)). Development builds never
      contact the update server
- [x] Home dashboard pins and groups; drag one branch onto another to merge or rebase
- [x] Repository statistics, About with contributors, and pull requests across repositories (jump palette)
- [x] First-run welcome
- [x] `gitwolf` command and `gitwolf://open?path=` links, both off until turned on in Settings, with one running
      copy. A link opens only a folder on a local drive, after a confirmation
- [x] Tray icon, taskbar badge, jump list and a notification when the checked-out commit's CI finishes
- [x] Brand logos for file types
- [x] Spoken names for screen readers on rows, lists and icon buttons

Not yet tried by hand: statistics, About, the command and link registration, the tray icon, taskbar badge,
jump list, CI notifications, the later welcome steps, the pull request list, the link confirmation, pins and
groups, branch dragging, and use with a screen reader (some names were read through UI Automation only).

### Trusting a repository

A folder's `.git/config` (or a submodule's) can make git run programs, for example through
`core.fsmonitor`, filters, `diff.external`, `gpg.program`, `core.hooksPath` or
`remote.<name>.uploadpack`, and so can scripts in `.git/hooks`. GitWolf reads these first, and if it
finds a hook, such a setting, or an HTTP or proxy option, runs no other git command there until you
agree to what is listed. It asks again when the list or a hook script changes.
Answers are kept in `%LOCALAPPDATA%\GitWolf\trusted.json`. `git lfs install --local` is one
ordinary reason to be asked.

Known limits:

- In a trusted repository, pull and push hooks can read the account token.
- Only the hook files git runs are checked for changes, not other scripts they call, and not hooks in a
  network folder or in a submodule's own `core.hooksPath`.
- A trusted repository's settings run with your rights, including during pull and push.

## Building

Requires the .NET 10 SDK and, for `GitWolf.App`, Visual Studio 2022 with the **Windows App SDK**
workload installed (the WinUI 3 packaging build tasks are not available through the .NET SDK /
`dotnet workload` alone).

```powershell
dotnet build src\GitWolfCore\GitWolfCore.csproj   # engine only, builds anywhere
dotnet test tests\GitWolfCore.Tests\GitWolfCore.Tests.csproj
# The app needs Visual Studio's MSBuild (`dotnet build` lacks the WinUI packaging tasks):
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" src\GitWolf.App\GitWolf.App.csproj -restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

## Notes

- Shell out to the system `git` (`GitRunner`); never reimplement git or use libgit2.
- `GitRunner.ResolveDefaultGitPath` looks on `PATH` first, then the Git for Windows default
  install location, per PORTING.md's platform-specific replacements table.
- Byte-level splitting (`ByteSplitting.SplitBytes`) is used instead of string line-splitting so
  CRLF files and non-UTF8-safe filenames survive, matching the Swift implementation.
- Windows commonly defaults `core.autocrlf` to `true` at the global/system git config level,
  which rewrites line endings on checkout and `git apply`. The engine itself doesn't set this
  (GitRepository should not override a user's repo config), but it's a real source of surprise
  diffs/patches on this platform that macOS doesn't hit by default — worth a settings/onboarding
  note once GitRepository and Settings are ported. The patch-builder tests pin
  `core.autocrlf=false` per test repo so they see exactly the bytes they wrote.
