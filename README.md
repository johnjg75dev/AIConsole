# AIConsole

A minimalist **Windows tray** AI chat app. Press **Ctrl+`** (default, remappable) and a slide-in
panel drops down from the top of your screen (or slides in from the left/right). Built with
**C# / WPF on .NET 10**, native **Markdown** rendering, and **SQLite** persistence.

## Features

- **Tray app** — lives in the system tray; global hotkey; start with Windows.
- **Slide-in panel** — top-center slide-down (default) or left/right side slide-out; optional
  **acrylic** backdrop; hides on `Esc` / focus loss / hotkey; `Enter` to send, `Shift+Enter` for newline.
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

## Import from other installations

Settings → **Import from other tools**:

- **Scan installed tools** — OpenCode (`~/.config/opencode/opencode.json[c]`, `opencode.json[c]`,
  `@ai-sdk/openai-compatible`, `{env:VAR}` keys, `~/.local/share/opencode/auth.json`),
  Continue (`~/.continue/config.json`), Zed (`language_models.openai_compatible`).
- **Detect local servers** — Ollama `:11434`, LM Studio `:1234`, `:8080`, `:5000`, `:8000`.
- **Import file…** — any `.json` / `.jsonc`; auto-detects OpenCode/Continue/Zed or plain
  `{ "baseUrl": ..., "apiKey": ..., "models": [...] }`.

## Build / run

```powershell
dotnet build src/AIConsole/AIConsole.csproj
dotnet run   --project src/AIConsole
# self-contained single-file:
dotnet publish src/AIConsole/AIConsole.csproj -p:PublishProfile=SingleFile
# -> src/AIConsole/artifacts/SingleFile/AIConsole.exe
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
