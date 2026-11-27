# AIConsole — Build Plan

## Overview

A **.NET 10 WPF Windows tray application**. A global hotkey (default **Ctrl+`**, remappable) slides a minimalist AI chat panel down from the top of the screen (optional **side slide-out**). Conversations, chat history, endpoints, model selection, **and all settings persist in SQLite**. Native WPF Markdown rendering, **syntax highlighting + line numbers** in code blocks, token-by-token **SSE streaming**, OpenAI-compatible endpoint management with auto-discovered models, and a **live theme engine** with creative built-in palettes.

**Tech:** C# / WPF `net10.0-windows`, CommunityToolkit.Mvvm, Hardcodet.NotifyIcon.Wpf (tray), Markdig (markdown), Microsoft.Data.Sqlite (all persistence), ColorCode.Core (syntax-highlighting grammars; thin WPF sink maps token styles to theme brushes; built-in regex fallback), Win32 `RegisterHotKey`, DPAPI (API-key encryption), DwmSetWindowAttribute (Acrylic/Mica backdrop, graceful fallback).

## Project Structure

```
AIConsole.sln
PLAN.md
src/AIConsole/                     # net10.0-windows WPF app
├── App.xaml(.cs)                  # single-instance mutex, DI wiring, tray bootstrapping
├── Models/                        # POCOs: Conversation, ChatMessage, Endpoint, ModelInfo, AppSettings
├── Data/                          # SQLite (Microsoft.Data.Sqlite, plain ADO)
│   ├── Db.cs                      # connection mgmt + schema/migrations
│   ├── SettingsRepo.cs            # settings table (JSON values), DPAPI-encrypted API keys
│   ├── EndpointRepo.cs            # endpoints + discovered models
│   └── ChatRepo.cs                # conversations + messages
├── Services/
│   ├── OpenAiClient.cs            # GET {base}/v1/models (auto-fetch), POST chat/completions SSE → IAsyncEnumerable
│   ├── ModelRefreshService.cs     # PeriodicTimer background refresh of "always show all" endpoints
│   ├── HotkeyService.cs           # RegisterHotKey + HwndSource hook (default MOD_CONTROL + VK_OEM_3)
│   ├── TrayService.cs             # NotifyIcon: toggle / new chat / settings / quit
│   ├── ThemeManager.cs            # swap merged dictionaries live, persist choice/accent, follow-Windows mode
│   ├── StartupService.cs          # HKCU Run-key start-with-Windows
│   └── Dpapi.cs                   # ProtectedData for API keys
├── ViewModels/                    # CommunityToolkit.Mvvm
│   ├── ShellViewModel.cs          # slide state, view switching (Chat ↔ Settings)
│   ├── ChatViewModel.cs           # conversations, send/stop streaming, model picker
│   ├── MessageViewModel.cs        # markdown blocks, name, timestamp, copy/delete
│   └── SettingsViewModel.cs       # endpoint editor, model checkboxes, select-all, appearance
├── Views/
│   ├── MainWindow.xaml            # borderless transparent popup; slide animation; acrylic
│   ├── ChatView.xaml              # bubbles, sidebar, input
│   ├── SettingsView.xaml          # endpoints & models + appearance
│   └── Controls/
│       ├── ChatBubble.xaml(.cs)   # bubble + name/timestamp + hover icons + context menu
│       ├── HotkeyBox.xaml(.cs)    # key-capture control
│       └── ThemeCard.xaml         # theme picker swatch card
├── Markdown/
│   ├── MarkdownRenderer.cs        # Markdig AST → WPF blocks/inlines (headings, lists, quotes, tables, links, code)
│   ├── CodeBlock.xaml(.cs)        # card: header (language + copy) + line-number gutter + highlighted code
│   └── SyntaxHighlighter.cs       # tokenizer → theme brushes; ColorCode.Core grammars + regex fallback
├── Themes/
│   ├── Tokens.xaml                # shared styles: radii, spacing, fonts, glyphs, control templates
│   └── Palettes/                  # one ResourceDictionary per theme (see below)
└── Assets/                        # tray icon .ico, glyphs
```

## Themes

Token-based resource dictionaries swapped at runtime via `ThemeManager`; everything binds `DynamicResource`, applies live, persisted in SQLite. Each palette defines backgrounds/surfaces/borders, text + dim text, user-bubble gradient start/end, assistant surface, accent primary/secondary, success/error/warning, and a full **syntax palette** (keyword, string, comment, number, type, function, operator, tag, property, punctuation) — dark palettes get dark code surfaces, light palettes light ones.

**Built-in palettes:** Midnight Neon (default dark: deep navy + violet/cyan), Cyber Glass (acrylic + magenta/cyan neon), Ember (warm charcoal + sunset orange/rose), Nord, Forest, Solar Flare (light warm), Clean Day (light).

**Creative dressing:** acrylic/Mica backdrop via `DwmSetWindowAttribute` (Win11, per-theme, fallback), gradient user bubbles, accent-tinted glow, 12–14px rounded corners, micro-animations (slide + fade + scale, hover lift, streaming caret shimmer, pulsing send button), custom thin scrollbars, pill-shaped selected conversation, Segoe Fluent Icons. **Accent override:** 8-swatch accent picker re-tints accents/gradients/glow; "match Windows dark/light" auto option. Theme picker with live swatch cards. Code font selector. Theme infrastructure is step ② so all UI is theme-aware from day one.

## Code Blocks (syntax highlighting + line numbers)

```
┌──────────────────────────────────────┐
│ csharp                          ⧉    │  ← header: language label + copy button
├──┬───────────────────────────────────┤
│ 1│ using Markdig;                    │  ← gutter (dim, right-aligned, 1px divider)
│ 2│ var doc = Markdown.Parse(md);     │     identical fixed line heights on both sides
│ 3│ Render(doc);                      │
└──┴───────────────────────────────────┘
```

Rounded card on code-surface; monospace `Cascadia Code → Consolas → Courier New`; horizontal scroll for long lines keeps gutter aligned; language from fenced info string; copy button copies raw code; **"Show line numbers" setting**; highlighting follows active theme. Syntax line numbers also apply to inline streaming-safe re-render (plain text while streaming, Markdown on completion).

## Persistence (SQLite → `%APPDATA%\AIConsole\aiconsole.db`)

| Table | Purpose |
|---|---|
| `settings` | key/value (JSON) — hotkey, slide edge, refresh interval, start-with-Windows, theme, accent, last model, UI state |
| `endpoints` | id, name, base_url, api_key_enc (DPAPI) |
| `models` | endpoint_id, model_id, enabled, last_seen — composite key; picker uses `endpointId::modelId` to avoid name collisions |
| `conversations` | id, title, model, timestamps |
| `messages` | id, conversation_id, role, model_id, content, created_at |

## Key Behaviors

- **Popup:** borderless transparent window, top-center of active monitor; `TranslateTransform` + easing ~160ms in / ~120ms out; **Top (default) / Left / Right** slide edge setting; hide on `Esc`, hotkey re-press, or focus loss; tray left-click toggles; thin drag handle; model picker; settings gear.
- **Hotkey:** Win32 `RegisterHotKey`, captured in Settings, persisted; conflict → notify, keep previous.
- **Chat:** Enter sends, Shift+Enter newline; model dropdown grouped by endpoint; collapsible conversation sidebar (rename/delete/search); streaming renders plain per token then re-renders Markdown; Stop button; inline error banners; conversation auto-titles.
- **Bubbles:** user right / assistant left; **name (model or "You") + timestamp under bubble**; hover **Copy** + **Remove-from-history** icons; identical commands in right-click **context menu**.
- **Markdown:** Markdig advanced extensions (tables, lists, code, links-open-externally).
- **Settings → Endpoints:** add name + Base URL + optional API key → on save auto-fetch `GET {base}/v1/models`; per-model checkboxes + tri-state **Select All**; per-endpoint **"Always show all models"** toggle; background `PeriodicTimer` refresh (default 6h, configurable) keeps picker current; manual model add.
- **Extras:** start-with-Windows; single instance; API keys DPAPI-encrypted; self-contained single-file publish profile.

## Implementation Order

1. Scaffold solution + DB schema + repositories
2. **Theme engine** (tokens, ThemeManager, starter palettes)
3. Tray icon + hotkey + slide shell (acrylic)
4. Chat UI: sidebar, bubbles, input, **streaming** OpenAI client
5. Markdown + **CodeBlock**: syntax highlighting + line numbers + copy
6. Settings UI: endpoints, auto-fetch, model checkboxes/select-all, **auto-refresh**, **appearance**
7. Remaining palettes, **polish, start-with-Windows, single-instance, publish**, end-to-end test

## Git

Periodic commits at each milestone on `main`.
