# CS2 Match Assistant (Native C#)

A high-performance, standalone, Valve Anti-Cheat (VAC) safe external tool for Counter-Strike 2. Automates match acceptance, lobby auto-queueing, and anti-AFK movements.

This version is written in native C# targeting .NET Framework 4.X (built-in on Windows 10/11). It compiles to a tiny standalone executable (~30KB) with zero external dependencies and near 0% CPU usage.

## Features

1. **Auto Accept**: Periodically scans the center area of the screen using fast GDI+ pixel capture to locate the green "Accept Match" button and clicks it.
2. **Auto Queue (Premier)**: Detects if you are in the lobby/menu for a stable period (>10s) and automatically navigates: clicks "Play" (top menu), clicks "Premier" (first tab), and scans for the green "Go" button to start matchmaking.
3. **Anti-AFK**: Triggers only when CS2 is active and your cursor is hidden (actively in-game). Simulates slight random movement keystrokes (`W`, `A`, `S`, `D` with opposing corrections) and minor mouse jitters.
4. **VAC Safe**: Purely external. No reading or writing process memory, no DLL injection, and no API hooks. Operates strictly via standard Win32 screen capture and input simulation APIs.

---

## Compilation

You can compile the executable yourself using the native Windows C# compiler (`csc.exe`). No installation is required.

1. Double-click or run `compile.bat` in your terminal:
   ```cmd
   compile.bat
   ```
2. This compiles `CS2Assistant.cs` and creates `CS2Assistant.exe` in the current folder.

---

## Usage

1. Launch `CS2Assistant.exe` (run as Administrator to allow global key state detection).
2. Start CS2.
3. **Game Settings Requirements**:
   - Run CS2 in **Windowed** or **Borderless Windowed** mode (so the tool can capture screenshots).
   - Use default/standard HUD color theme.
4. **Calibration (Highly Recommended)**:
   - Hover your mouse over the **PLAY** button in CS2 and press **F9**. Note the relative coordinates printed in the console.
   - Go to the Play menu, hover over the **PREMIER** game mode card, and press **F9**. Note the coordinates.
   - Open `CS2Assistant.cs`, locate the `PLAY_COORDS` and `PREMIER_COORDS` values in the configuration block at the top, update them with your coordinates, and recompile.
5. Press **F10** in-game to toggle the assistant on/off (you will hear a beep).
6. Press **F11** to exit the assistant.
