# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

External CS2 Match Assistant (VAC-safe): auto-accept match invites, auto-queue Premier, anti-AFK. Single-file C# app targeting .NET Framework 4.x via the built-in Windows `csc.exe` — no SDK, no NuGet, no third-party deps.

## Commands

```cmd
compile.bat
```

Compiles `CS2Assistant.cs` → `CS2Assistant.exe` using:
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /unsafe /target:exe /r:System.Drawing.dll`

If the exe is locked (still running):
```cmd
taskkill /IM CS2Assistant.exe /F
compile.bat
```

Run as Administrator (needed for global `GetAsyncKeyState` hotkeys).

No test suite. Verify by running the exe against CS2 in Borderless Windowed mode and watching console logs.

## Language constraints

Compiler is **C# 5 only** (Framework 4.x `csc`). Forbidden:

- String interpolation (`$"..."`) — use `string.Format`
- Inline `out` declarations (`out double h`) — declare vars first
- Null-conditional, expression-bodied members beyond what C# 5 allows, etc.

`/unsafe` is required for `LockBits` pointer pixel scanning.

## Architecture

Everything lives in one class: `CS2Assistant.cs`.

### Threads

| Thread | Role |
|--------|------|
| Main | Hotkey poll (`F9` cal, `F10` toggle, `F11` exit) every 50ms |
| `AutoAcceptLoop` | Center-screen green Accept button via region pixel scan |
| `AutoQueueLoop` | Lobby state machine: navigate menus, click Go, track queue |
| `AntiAfkLoop` | In-match only: random WASD + opposing key + mouse jitter |

Shared state: `running`, `active` (toggled by F10). Feature flags and timing at top of file as static fields.

### State detection (no game memory)

- **In-match vs menu**: `GetCursorInfo` — cursor hidden = in-match; visible = lobby/menu. Anti-AFK only when hidden; auto-queue only when visible ≥10s continuous (avoids ESC-menu false positives).
- **CS2 focused**: `GetForegroundWindow` + title contains `"Counter-Strike 2"`.
- **Accept ready**: GDI+ `CopyFromScreen` on center 30% box; HSV green blob (Hue 80–170).
- **Go button ready**: single pixel at `GO_COORDS` — bright green (Hue ~101).
- **Currently queuing**: single pixel at `QUEUE_INDICATOR_COORDS` (top-right) — dark green while searching.

### Auto-queue state machine

`LOBBY` → if queue indicator green → `QUEUING`; else if Go green → click Go → `QUEUING`; else navigate Play → Matchmaking → Premier (throttled every 15s).  
`QUEUING` → if Go reappears or both indicator and Go gone → back to `LOBBY`. Cursor hide (match start) also resets to `LOBBY`.

### Input

Win32 only: `SetCursorPos` + `mouse_event`, `keybd_event`, `GetAsyncKeyState`. Restore cursor after menu clicks.

### Calibration

F9 prints relative coords + RGB/HSV under cursor. Values are hardcoded as `static double[]` relative fractions (0–1) of primary monitor size from `GetSystemMetrics`. Current values are calibrated for the developer's layout:

| Constant | Purpose |
|----------|---------|
| `PLAY_COORDS` | Top Play button |
| `MATCHMAKING_COORDS` | Matchmaking sub-tab |
| `PREMIER_COORDS` | Premier tab |
| `GO_COORDS` | Bottom-right Go / Find Match |
| `QUEUE_INDICATOR_COORDS` | Top-right search-active green |

After changing coords or logic: recompile with `compile.bat`.

## VAC safety rules

Keep purely external: screen capture + synthetic input only. Do not add process memory read/write, injection, or game API hooks.

## Game requirements

CS2 must be Windowed or Borderless Windowed. Default HUD color assumed for green Accept/Go detection. Run assistant elevated.
