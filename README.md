# CS2 Match Assistant

A lightweight, Valve Anti-Cheat (VAC) safe external tool for Counter-Strike 2 to automate match acceptance, auto queueing, and anti-AFK movements.

## Features

1. **Auto Accept**: Actively scans the center area of the screen for the default green Accept Match button and automatically clicks it using DirectX virtual input simulation.
2. **Auto Queue**: Scans the bottom-right region of the screen for the green "Go" / "Find Match" button when inside the lobby or main menu. If found, it automatically clicks it to start matchmaking (e.g. after a match finishes or queue is canceled), with a customizable delay.
3. **Anti-AFK**: Periodically performs minor movement simulations (moving character slightly and minor mouse jitter) with randomized delays to avoid triggering in-game AFK penalties while playing.
3. **VAC Safe**: The tool works entirely externally by capturing screenshots (using standard Windows desktop replication API) and sending keyboard/mouse inputs via standard Win32 input injection APIs. It does not read process memory, write to process memory, hook game processes, or inject DLLs.

---

## How It Works

1. **Auto-Accept**: Crops a 30% area in the center of your screen and performs OpenCV color filtering to find a green block matching the matchmaking accept dialog.
2. **Auto-Queue**: Detects if CS2 is active and the mouse cursor has been visible for at least 10 seconds (verifying you are in the lobby/menu and not in a match or briefly pausing). It then navigates: clicks "Play" (top menu), clicks "Premier" (first tab), and scans the bottom-right corner for the green "Go" button to start queueing.
3. **Anti-AFK**: Only activates when CS2 is active and your cursor is hidden (meaning you are actively in-match controlling your crosshair). It presses a random key (`W`, `A`, `S`, `D`), followed by its opposing key to restore position, and adds a small mouse jitter.

---

## Installation & Setup

### Prerequisites

- **Python 3.10+** (Python 3.12 is tested and working)
- **Windows OS** (Due to DirectX-level input emulation dependencies)
- **Counter-Strike 2 settings**:
  - Run the game in **Windowed** or **Borderless Windowed** mode. (Full-screen mode may prevent the Python script from taking screenshots or sending inputs correctly).
  - Use the default/standard HUD color theme.

### Installation

1. Open a terminal/command prompt in the application directory.
2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```

---

## Usage

1. Run the script using Python:
   ```bash
   python cs2_assistant.py
   ```
2. Launch CS2.
3. **Calibrate Coordinates (Highly Recommended)**:
   - Hover your mouse over the **PLAY** button in CS2 and press **F9**. Note the relative coordinates printed in the console.
   - Go to the Play menu, hover over the **PREMIER** game mode card, and press **F9**. Note the coordinates.
   - Update the `PLAY_COORDS` and `PREMIER_COORDS` values in the `CONFIG` section at the top of `cs2_assistant.py` with these exact printed coordinates.
4. Press **F10** in-game to toggle the assistant on/off. You will hear a beep when it is activated.
5. To safely exit the script, press **F11**.

### Configuration

You can customize the script configurations by editing the `CONFIG` dictionary at the top of the `cs2_assistant.py` file:

- **`AUTO_ACCEPT_ENABLED`**: Set to `True` (default) or `False` to toggle matchmaking auto-accept.
- **`ANTI_AFK_ENABLED`**: Set to `True` (default) or `False` to toggle AFK protection.
- **`AFK_MIN_INTERVAL` / `AFK_MAX_INTERVAL`**: Set the range of delay (in seconds) between each random anti-AFK keystroke simulation.
- **`TOGGLE_HOTKEY`**: The key used to start/stop the script (default: `F10`).
- **`EXIT_HOTKEY`**: The key used to close the script execution (default: `F11`).

---

## Warning / Disclaimer

This tool is designed to be purely external and does not interact with CS2's executable memory directly, which makes it safe from traditional VAC detection signature scans. However:
- Do not use anti-AFK in competitive matches if it disrupts the game for other players.
- Self-check the input behaviors in custom/offline lobbies first.
- Use at your own risk. The developer holds no responsibility for any in-game actions taken against your account.
