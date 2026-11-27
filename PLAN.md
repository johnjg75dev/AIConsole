# AIConsole — Build Plan

## Overview

A **.NET 10 WPF Windows tray application**. A global hotkey (default **Ctrl+`**, remappable) slides a minimalist AI chat panel down from the top of the screen (optional **side slide-out**). Conversations, chat history, endpoints, model selection, and all settings persist in SQLite. Native WPF Markdown rendering, syntax highlighting + line numbers, token-by-token SSE streaming, OpenAI-compatible endpoint management with auto-discovered models, and a live theme engine with built-in palettes.

**Tech:** C# / WPF `net10.0-windows`, CommunityToolkit.Mvvm, Hardcodet.NotifyIcon.Wpf (tray), Markdig (markdown), Microsoft.Data.Sqlite (all persistence), ColorCode.Core (syntax highlighting), Win32 `RegisterHotKey`, DPAPI (API-key encryption), DwmSetWindowAttribute (acrylic backdrop).

## Project Structure

```
AIConsole.slnx
PLAN.md / VERSIONING.md / compile.py / compile.bat / compile.ps1
src/AIConsole/
├── App.xaml(.cs)                  # single-instance mutex, DI wiring, tray bootstrapping
├── Models/                        # POCOs + AppSettings
├── Data/                          # SQLite repositories + schema migrations
├── Services/                      # OpenAI client, hotkey, tray, themes, DPAPI, placement
├── ViewModels/                    # Chat, Message, Attachment, Settings
├── Views/
│   ├── MainWindow.xaml            # borderless transparent slide panel
│   ├── ChatView.xaml              # sidebar, transcript, composer
│   ├── SettingsView.xaml          # responsive tabbed settings
│   └── Controls/                  # ChatBubble, ModelPicker, HotkeyBox, BoundPasswordBox, ResponsiveColumnsPanel
├── Markdown/                      # Markdig → WPF, CodeBlock, SyntaxHighlighter
├── Themes/                        # Tokens + palettes
└── Assets/                        # tray icon
tests/AIConsole.Tests/
installer/Product.wxs              # WiX v4 per-user MSI
.github/workflows/                 # lint + build + tag release
```

## Current Roadmap

### 1. Concurrent conversation streams

- Per-conversation stream sessions: assistant VM, cancellation token, buffer.
- Switching conversations must not cancel or discard a running reply.
- Composer send/stop state reflects the selected conversation only.
- Sending in a second conversation while the first is streaming is allowed.
- Stop affects only the active conversation.
- Completion/cancel/error persist independently, including background conversations.
- Sidebar timestamp updates without replacing the selected `Conversation` instance.
- Extract `IChatClient` so streams can be faked in tests.

### 2. Edit as new + attachments policy

- Right-click / hover action on user bubbles: **Edit as new**.
- Refills the composer with text, attachments (when enabled), and the original model.
- If the edited message is the last message in the conversation and the setting is on, it is deleted when the new message is sent; otherwise the original is kept.
- Setting: **Replace the last message when using Edit as new** (default on).
- Setting: **Send attachments to the model** (default off). When off, attachments are shown but excluded from outgoing history; the attach button is disabled.
- New **Chat** settings tab also exposes **Enter sends**.

### 3. Live streaming render

- Every assistant reply enters streaming mode immediately.
- Every received delta updates `RawMarkdown`.
- Markdown is re-rendered on a coalesced ~60 ms dispatcher timer while streaming, so formatted content (including multiple code blocks) appears as soon as possible.
- Final render occurs the moment streaming ends.
- Non-streaming provider responses are rendered in one pass — no artificial chunking.

### 4. Multiple Markdown/code blocks per bubble

- Renderer already walks all top-level blocks.
- Add regression coverage for two or more fenced code blocks plus surrounding Markdown.
- Live re-render must replace the whole tree, not only the first block.

### 5. Context usage + context end

- Header pill to the right of the model picker:
  - character count plus estimated tokens (`~chars / 4`)
  - counts context messages + draft + pending text attachments + in-flight assistant text.
- Right-click bubble:
  - **End context here** — selected message becomes the last included in outgoing requests.
  - **Use full context** — clears the cursor.
- Excluded messages stay visible but are dimmed and labeled “Out of context”.
- `Conversation.ContextEndMessageId` and `ContextResumeMessageId` persist in SQLite
  (`conversations.context_end_id`, `conversations.context_resume_id`).
- The first exchange after a cutoff starts a new included suffix: the old prefix plus everything
  from that user message onward stays in context, while the skipped tail stays dimmed and excluded.
  Later turns join the suffix automatically.
- Context changes are blocked while that conversation streams.

### 6. Model picker with search + provider tabs

- Replace the model `ComboBox` with a `ModelPicker` control:
  - button shows model ID + endpoint name
  - popup with search box
  - tabs: **All** + one per provider endpoint
  - case-insensitive search over model and endpoint
  - keyboard navigation (Up/Down/Enter/Esc)
  - empty-state message and scrolling
- Picker listens to the models collection so periodic refresh updates tabs/results.

### 7. Responsive settings layout

- `ResponsiveColumnsPanel`: two columns when wide, stacked when narrow, based on actual width.
- Used by **Window & Hotkey** and **Appearance**; the new **Chat** tab is single-column.
- Settings `ScrollViewer` stretches content horizontally.

### 8. Slide-out X/Y offsets

- `PanelOffsetX` / `PanelOffsetY` settings under Layout.
- Left/right edges anchor at the work area top; `0,0` is the anchor corner, `+X` right, `+Y` down.
- Top edge anchors left (or centered for a custom width).
- Offsets apply live and are clamped so the panel stays reachable.
- Placement math lives in a pure `WindowPlacement` helper for tests.

### 9. Multiple endpoint API keys

- Replace the broken `PasswordBox` attached binding with `BoundPasswordBox`.
- Root cause: the attached-property callback only subscribed to `PasswordChanged` when the bound value changed from its default, so newly added endpoints with an empty initial key never propagated typing.
- Regression tests cover two endpoints with distinct keys through save/reload.

## Persistence

| Table | Purpose |
|---|---|
| `settings` | JSON values: hotkey, slide edge, offsets, opacity, theme, chat options |
| `endpoints` | id, name, base_url, api_key_enc (DPAPI) |
| `models` | endpoint_id, model_id, enabled, last_seen |
| `conversations` | id, title, model, context_end_id, context_resume_id, timestamps |
| `messages` | id, conversation_id, role, model_id, content, created_at |
| `message_attachments` | message_id, name, media_type, data |

## Verification

- Unit tests: concurrent streams, edit-as-new replacement, context filtering/advancement, model filtering, placement math, multi-endpoint keys, multi-code-block rendering.
- WPF UI harness: per-conversation send/stop state, live Markdown updates, context pill, model search/tabs, responsive settings, context cutoff dimming, slide offsets.
- `dotnet test AIConsole.slnx`; build through `compile.py` menu option 1.

## Git

Commit at each milestone on `main`; release through `compile.py` and tagged GitHub Actions.
