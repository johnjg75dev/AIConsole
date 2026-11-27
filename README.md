# AIConsole

A minimalist **Windows tray** AI chat app. Press **Ctrl+`** (default, remappable) and a slide-in
panel drops down from the top of your screen (or slides in from the left/right). Built with
**C# / WPF on .NET 10**, native **Markdown** rendering, and **SQLite** persistence.

## Features

- **Tray app** — lives in the system tray; global hotkey; start with Windows.
- **Slide-in panel** — top-center slide-down (default) or left/right side slide-out; optional
  **acrylic** backdrop; hides on `Esc` / hotkey, with optional hide on focus loss (off by default); `Enter` to send, `Shift+Enter` for newline.
- **OpenAI-compatible** providers — any remote endpoint; **optional API key** (stored DPAPI-encrypted).
  - **Auto-fetch models** via `GET {base}/v1/models`.
  - Per-model **checkboxes** + **Select All**.
  - **"Always show all models from endpoint"** — background re-fetch on an interval; new models appear automatically.
  - Add endpoints manually or **import** from other tools (see below).
- **Conversations** — sidebar, rename, delete, search-free history; conversations + messages in SQLite.
- **Markdown** — headings, lists, tables, quotes, links, inline + fenced code; **syntax highlighting**
  (ColorCode grammars, theme-aware) + **line numbers**; per-code-block copy.
- **Bubbles** — participant name (model / *You*) + timestamp under each message; **Copy** and
  **Remove from history** icons + same items in the **right-click context menu**.
- **Theming** — live-switchable palettes (Midnight Neon, Cyber Glass, Ember, Nord, Forest, Solar Flare,
  Clean Day), accent overrides, match-Windows dark/light, code-font selection.
- **Streaming** — token-by-token SSE with **Stop**; partial output kept.
- **Attachments** — attach UTF-8 text or PNG, JPEG, GIF, and WebP images using the paperclip.
  Text is included in the message; images use OpenAI-compatible image content and require a vision-capable
  model and provider. Limit: 5 MB per file and 10 MB per message. Attachments are stored in the local
  SQLite database and sent again as part of subsequent conversation history. Do not attach secrets unless
  you intend to send them to the selected provider.
- **Edit history** — use the edit action or right-click a message. Saving changes affects future requests;
  it does not regenerate or delete later replies, and existing attachments remain unchanged.
- **Decision models** — select a compatible model and click **Decision** to submit a state and a typed
  choice, score, or noul question to the selected endpoint's `/v1/systemone` API. Choice options can use
  `name = description`; score levels are ordered. Answers and probabilities are presented in a readable
  summary with an expandable raw response. Results are not added to chat history. This is a System One-compatible integration (such as Jev), not a generic adapter for every
  provider's decision API; the configured endpoint and model must support that schema.

## Import from other installations

Settings → **Import from other tools**:

- **Scan installed tools** — OpenCode (`~/.config/opencode/opencode.json[c]`, `opencode.json[c]`,
  `@ai-sdk/openai-compatible`, `{env:VAR}` keys, `~/.local/share/opencode/auth.json`),
  Continue (`~/.continue/config.json`), Zed (`language_models.openai_compatible`).
- **Detect local servers** — Ollama `:11434`, LM Studio `:1234`, `:8080`, `:5000`, `:8000`.
- **Import file…** — any `.json` / `.jsonc`; auto-detects OpenCode/Continue/Zed or plain
  `{ "baseUrl": ..., "apiKey": ..., "models": [...] }`.

## Build / run

All versioning and release rules are documented in **[VERSIONING.md](VERSIONING.md)**.

Double-click **`compile.bat`** (or run `./compile.ps1`) for the interactive menu covering dev builds,
RC builds, patch/minor/major bumps, publishing, tests, debugging, and releases. **Use the menu as the
normal path — selecting its version option is what properly versions your change.** Python 3 is required.

```powershell
python compile.py                                  # interactive menu
python compile.py build --kind dev                 # auto-incremented build number
python compile.py publish                          # single-file EXE -> dist/AIConsole.exe
python compile.py test                             # unit tests
python compile.py debug                            # build + run
python compile.py release --bump patch --no-github # EXEs + MSIs, no tag
python compile.py release --bump patch             # commit/tag/push; Actions creates the release
```

Version state lives in `version.json` (`dev` builds increment the revision, `rc` produces
`x.y.z-rc.N`, and patch/minor/major produce stable tags). Every compiled binary receives the current
version. Release builds publish `win-x64`, `win-x86`, and `win-arm64` standalones plus per-user MSI
installers into `dist/`.

Raw `dotnet` commands still work, and a plain `dotnet build` automatically bumps the version first
(`Directory.Build.targets` runs `compile.py msbuild-props` before compiling). Override the bump with
`-p:VersionKind=rc|patch|minor|major`, or disable it with `-p:AutoVersion=0`.

```powershell
dotnet build AIConsole.slnx
dotnet build AIConsole.slnx -p:VersionKind=rc
dotnet test  AIConsole.slnx
dotnet publish src/AIConsole/AIConsole.csproj -p:PublishProfile=SingleFile
```

Requires Windows + **.NET 10 SDK**. Data stored in `%APPDATA%\AIConsole\aiconsole.db`.

## Layout

- `src/AIConsole/Views` — XAML (shell, chat, settings, `ChatBubble`, `CodeBlock`, `HotkeyBox`)
- `src/AIConsole/ViewModels` — MVVM (`CommunityToolkit.Mvvm`)
- `src/AIConsole/Services` — `OpenAiClient`, `ModelRefreshService`, `HotkeyService`, `TrayService`,
  `ThemeManager`, `ConfigImporter`, `StartupService`, `Dpapi`
- `src/AIConsole/Data` — SQLite `Db`, repositories
- `src/AIConsole/Markdown` — Markdig → WPF renderer + `SyntaxHighlighter`
- `src/AIConsole/Themes` — `Tokens.xaml` + `Palettes/*`
