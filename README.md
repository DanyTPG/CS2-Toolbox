# CS2-Toolbox

VAC-safe external assistant for Counter-Strike 2. Auto-accept matches, auto-queue Premier, anti-AFK — single C# file, zero dependencies.

## Features

- **Auto Accept** — Detects the green Accept button via pixel scan and clicks it automatically
- **Auto Queue** — Navigates menus and starts Premier matchmaking when idle in lobby
- **Anti-AFK** — Random WASD jitter while in-match (cursor hidden = in-game)
- **Game State Integration** — Optional HTTP listener for CS2's GSI, replacing cursor-based detection with authoritative game state

## Requirements

- Windows 10/11 (.NET Framework 4.x built-in)
- CS2 in **Windowed** or **Borderless Windowed** mode
- Run as Administrator

## Build

No SDK needed — uses the Windows-bundled C# compiler:

```
compile.bat
```

Produces `CS2Assistant.exe` (~25 KB).

## Usage

1. Run `CS2Assistant.exe` as Admin
2. Toggle features in the GUI or via hotkeys:
   - **F9** — Print cursor position + color (for calibration)
   - **F10** — Start/stop the assistant
   - **F11** — Quit
3. Start CS2 — the assistant runs in the background

## Calibration

The tool uses hardcoded screen coordinates relative to your monitor. If menu positions differ from the defaults:

1. Hover over a UI element (e.g. Play button) in CS2
2. Press **F9** to log its relative coordinates
3. Update the corresponding `*_COORDS` constant in `CS2Assistant.cs`
4. Recompile with `compile.bat`

## Game State Integration (Optional)

GSI provides more reliable in-match detection than cursor visibility checks.

1. Copy `gamestate_integration_cs2assistant.cfg` to `<CS2>/game/csgo/cfg/`
2. Restart CS2
3. The assistant auto-detects GSI and falls back to pixel detection if unavailable

## How It Works

Purely external — screen capture (GDI+) and synthetic input (Win32 API). No memory reading, no injection, no hooks.

## License

MIT
