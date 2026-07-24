import time
import random
import sys
import os
import threading

# Verify operating system
if sys.platform != "win32":
    print("Warning: CS2 runs on Windows/Linux. PyDirectInput and Keyboard hooks require Windows.")

try:
    import cv2
    import numpy as np
    from PIL import ImageGrab
    import pyautogui
    import pydirectinput
    import keyboard
    import ctypes
    from ctypes import wintypes
except ImportError:
    print("Error: Missing required packages. Please install them using:")
    print("pip install -r requirements.txt")
    sys.exit(1)

# Windows API Structures for Cursor and Window Detection
class POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]

class CURSORINFO(ctypes.Structure):
    _fields_ = [
        ("cbSize", ctypes.c_ulong),
        ("flags", ctypes.c_ulong),
        ("hCursor", ctypes.c_void_p),
        ("ptScreenPos", POINT)
    ]

def get_cursor_state():
    """Returns True if the Windows cursor is visible/showing, False if hidden (in-game)"""
    try:
        cursor_info = CURSORINFO()
        cursor_info.cbSize = ctypes.sizeof(CURSORINFO)
        if ctypes.windll.user32.GetCursorInfo(ctypes.byref(cursor_info)):
            # flags: 0x00000000 (hidden) or 0x00000001 (showing)
            return (cursor_info.flags & 0x00000001) != 0
    except Exception:
        pass
    return True  # Fallback to visible if call fails

def is_cs2_active():
    """Returns True if Counter-Strike 2 is the currently active foreground window"""
    try:
        hwnd = ctypes.windll.user32.GetForegroundWindow()
        length = ctypes.windll.user32.GetWindowTextLengthW(hwnd)
        if length > 0:
            buf = ctypes.create_unicode_buffer(length + 1)
            ctypes.windll.user32.GetWindowTextW(hwnd, buf, length + 1)
            return "Counter-Strike 2" in buf.value
    except Exception:
        pass
    return False

# Configuration
CONFIG = {
    # Auto-Accept Settings
    "AUTO_ACCEPT_ENABLED": True,
    "ACCEPT_SCAN_INTERVAL": 1.0,  # seconds
    # Green color range for Accept button in HSV (CS2 default green button is bright green/cyan HUD)
    # Lower and upper bounds for Accept button green in HSV format
    "GREEN_LOWER": np.array([35, 100, 100]),
    "GREEN_UPPER": np.array([85, 255, 255]),

    # Auto-Queue Settings (Premier Mode)
    "AUTO_QUEUE_ENABLED": True,
    "QUEUE_CHECK_INTERVAL": 2.0,  # seconds
    "QUEUE_DELAY_AFTER_MATCH": 5.0,  # delay before re-queueing (seconds)
    # Default relative coordinates (0.0 to 1.0) for standard 16:9 resolutions
    "PLAY_COORDS": (0.5224, 0.0324),      # Play button at the top menu
    "PREMIER_COORDS": (0.2339, 0.1231),   # Premier Mode card on Play screen

    # Anti-AFK Settings
    "ANTI_AFK_ENABLED": True,
    "AFK_MIN_INTERVAL": 30,  # minimum seconds between movements
    "AFK_MAX_INTERVAL": 90,  # maximum seconds between movements
    "AFK_KEYS": ["w", "s", "a", "d"],  # Keys to simulate for movement

    # Hotkeys
    "CALIBRATE_HOTKEY": "f9",  # Key to print current mouse relative coordinates
    "TOGGLE_HOTKEY": "f10",  # Key to toggle the assistant on/off
    "EXIT_HOTKEY": "f11",    # Key to close the script
}

# State Variables
running = True
active = False  # Is the assistant currently active and processing
screen_width, screen_height = pyautogui.size()

def log(message):
    print(f"[{time.strftime('%H:%M:%S')}] {message}")

def click_relative(rel_x, rel_y):
    """Click on screen coordinate calculated relative to screen size (0.0 to 1.0)"""
    abs_x = int(rel_x * screen_width)
    abs_y = int(rel_y * screen_height)

    # Save original position
    orig_x, orig_y = pyautogui.position()

    # Move and click using pydirectinput (better compatibility with games)
    pydirectinput.moveTo(abs_x, abs_y)
    time.sleep(0.1)
    pydirectinput.click()
    time.sleep(0.1)

    # Restore original position to avoid disrupting user
    pydirectinput.moveTo(orig_x, orig_y)
    log(f"Clicked at ({abs_x}, {abs_y})")

def scan_for_accept_button():
    """Scans the center area of the screen for the green Accept button"""
    # Accept button is in the center. We crop to the middle 30% of the screen.
    left = int(screen_width * 0.35)
    top = int(screen_height * 0.35)
    width = int(screen_width * 0.30)
    height = int(screen_height * 0.30)

    # Take screenshot of the center bounding box
    screenshot = ImageGrab.grab(bbox=(left, top, left + width, top + height))
    frame = cv2.cvtColor(np.array(screenshot), cv2.COLOR_RGB2BGR)
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)

    # Filter green pixels
    mask = cv2.inRange(hsv, CONFIG["GREEN_LOWER"], CONFIG["GREEN_UPPER"])

    # Find contours
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    for contour in contours:
        area = cv2.contourArea(contour)
        if area > 500:  # Avoid tiny green spots
            # Find the bounding box of the contour
            x, y, w, h = cv2.boundingRect(contour)

            # Verify aspect ratio of Accept button (usually wide)
            aspect_ratio = float(w) / h
            if 2.0 < aspect_ratio < 6.0:
                # Calculate center coordinate relative to the full screen
                click_x = left + x + (w // 2)
                click_y = top + y + (h // 2)
                return click_x, click_y

    return None

def auto_accept_loop():
    """Independent loop to check and click the Accept button"""
    while running:
        if active and CONFIG["AUTO_ACCEPT_ENABLED"]:
            try:
                coords = scan_for_accept_button()
                if coords:
                    click_x, click_y = coords
                    log("Accept button found! Clicking...")

                    # Store original mouse pos
                    orig_x, orig_y = pyautogui.position()

                    # Perform click
                    pydirectinput.moveTo(click_x, click_y)
                    time.sleep(0.05)
                    pydirectinput.click()
                    time.sleep(0.05)
                    pydirectinput.moveTo(orig_x, orig_y)

                    log("Accepted match.")
                    time.sleep(5)  # Sleep longer after click to avoid spamming
            except Exception as e:
                log(f"Error in Auto-Accept: {e}")
        time.sleep(CONFIG["ACCEPT_SCAN_INTERVAL"])

def anti_afk_loop():
    """Simulates random key presses and slight mouse movement to prevent being kicked"""
    while running:
        if active and CONFIG["ANTI_AFK_ENABLED"]:
            # Only trigger AFK protection when CS2 is the active window AND the mouse cursor is hidden (in-match)
            if is_cs2_active() and not get_cursor_state():
                log("Simulating anti-AFK movement...")
                try:
                    # Choose random key
                    key = random.choice(CONFIG["AFK_KEYS"])

                    # Press key briefly
                    pydirectinput.keyDown(key)
                    time.sleep(random.uniform(0.1, 0.3))
                    pydirectinput.keyUp(key)

                    # Perform an opposing key press to restore position
                    opposing = {"w": "s", "s": "w", "a": "d", "d": "a"}.get(key)
                    if opposing:
                        time.sleep(random.uniform(0.1, 0.2))
                        pydirectinput.keyDown(opposing)
                        time.sleep(random.uniform(0.1, 0.3))
                        pydirectinput.keyUp(opposing)

                    # Slightly jitter the mouse
                    dx = random.choice([-5, 5])
                    dy = random.choice([-5, 5])
                    pydirectinput.moveRel(dx, dy)
                    time.sleep(0.1)
                    pydirectinput.moveRel(-dx, -dy)

                except Exception as e:
                    log(f"Error in Anti-AFK: {e}")

        # Sleep duration between checks. We do random delay here.
        sleep_dur = random.randint(CONFIG["AFK_MIN_INTERVAL"], CONFIG["AFK_MAX_INTERVAL"])
        for _ in range(sleep_dur):
            if not running or not active:
                break
            time.sleep(1)

def scan_for_queue_button(debug=False):
    """Scans the bottom-right area of the screen for the green Go/Find Match button"""
    # Only scan if CS2 is active and cursor is visible (in lobby/menu)
    if not is_cs2_active() or not get_cursor_state():
        return None

    # Go/Find Match button is in the bottom right quadrant.
    left = int(screen_width * 0.80)
    top = int(screen_height * 0.88)
    width = int(screen_width * 0.18)
    height = int(screen_height * 0.10)

    # Take screenshot of the bottom-right bounding box
    screenshot = ImageGrab.grab(bbox=(left, top, left + width, top + height))
    frame = cv2.cvtColor(np.array(screenshot), cv2.COLOR_RGB2BGR)
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)

    # Filter green pixels
    mask = cv2.inRange(hsv, CONFIG["GREEN_LOWER"], CONFIG["GREEN_UPPER"])

    # Find contours
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    # Logging stats for debugging color values
    if debug:
        # Get dominant/max HSV values in the region
        h_channel, s_channel, v_channel = cv2.split(hsv)
        max_h, max_s, max_v = np.max(h_channel), np.max(s_channel), np.max(v_channel)
        min_h, min_s, min_v = np.min(h_channel), np.min(s_channel), np.min(v_channel)
        matching_pixels = cv2.countNonZero(mask)
        log(f"[DEBUG] Go region HSV range: Min=[{min_h},{min_s},{min_v}] Max=[{max_h},{max_s},{max_v}]")
        log(f"[DEBUG] Matching green pixels: {matching_pixels} (Threshold: 400)")

    for contour in contours:
        area = cv2.contourArea(contour)
        if debug:
            x, y, w, h = cv2.boundingRect(contour)
            aspect_ratio = float(w) / h
            log(f"[DEBUG] Found green contour: Area={area:.1f}, Aspect Ratio={aspect_ratio:.2f}, Bounding Box=({x},{y},{w},{h})")

        if area > 400:  # Avoid small green spots
            # Find the bounding box of the contour
            x, y, w, h = cv2.boundingRect(contour)

            # Aspect ratio of the button (wide button at bottom right)
            aspect_ratio = float(w) / h
            if 1.5 < aspect_ratio < 6.0:
                click_x = left + x + (w // 2)
                click_y = top + y + (h // 2)
                return click_x, click_y

    return None

def auto_queue_loop():
    """Loop to handle automatically queuing up again (Premier mode)"""
    last_queued_time = 0
    cursor_visible_duration = 0
    queue_state = "LOBBY"  # LOBBY, QUEUING
    last_navigation_time = 0

    while running:
        if active and CONFIG["AUTO_QUEUE_ENABLED"]:
            try:
                # Check if CS2 is foreground and cursor is visible
                cs_active = is_cs2_active()
                cursor_visible = get_cursor_state()

                if cs_active and cursor_visible:
                    cursor_visible_duration += 1
                else:
                    if cursor_visible_duration > 0:
                        log("Cursor hidden or CS2 inactive. Resetting lobby duration.")
                    cursor_visible_duration = 0
                    # If cursor is hidden, we might be in-game. Reset queue state.
                    if not cursor_visible and queue_state != "LOBBY":
                        log("In-game detected (cursor hidden). Resetting queue state to LOBBY.")
                        queue_state = "LOBBY"

                # Only attempt queueing if cursor has been visible continuously for at least 10 seconds
                if cursor_visible_duration >= 10:
                    current_time = time.time()

                    if queue_state == "LOBBY":
                        # Prevent navigating too frequently (cooldown of 15s between nav attempts)
                        if current_time - last_navigation_time > 15:
                            log("Lobby state active. Navigating to Play menu...")
                            click_relative(CONFIG["PLAY_COORDS"][0], CONFIG["PLAY_COORDS"][1])
                            time.sleep(1.2)

                            log("Selecting Premier Mode...")
                            click_relative(CONFIG["PREMIER_COORDS"][0], CONFIG["PREMIER_COORDS"][1])
                            time.sleep(1.2)

                            last_navigation_time = current_time

                        # Scan for the Go button (enable debug logging to see why it fails)
                        coords = scan_for_queue_button(debug=True)
                        if coords:
                            click_x, click_y = coords
                            log("Go / Find Match button found! Starting queue...")

                            # Save original mouse pos
                            orig_x, orig_y = pyautogui.position()

                            # Click the button
                            pydirectinput.moveTo(click_x, click_y)
                            time.sleep(0.1)
                            pydirectinput.click()
                            time.sleep(0.1)
                            pydirectinput.moveTo(orig_x, orig_y)

                            log("Queued successfully. Entering QUEUING state.")
                            queue_state = "QUEUING"
                            last_queued_time = current_time
                            cursor_visible_duration = 0

                    elif queue_state == "QUEUING":
                        # In QUEUING state, we monitor if the "Go" button is visible again.
                        # If the green Go button is visible again, it means the queue was cancelled.
                        # Wait at least 10 seconds after starting queue before checking to avoid instant triggers.
                        if current_time - last_queued_time > 10:
                            # Use debug=False to avoid logs when normally queuing
                            coords = scan_for_queue_button(debug=False)
                            if coords:
                                log("Go button detected while in QUEUING state. Queue must have been cancelled. Resetting to LOBBY.")
                                queue_state = "LOBBY"

            except Exception as e:
                log(f"Error in Auto-Queue: {e}")
        else:
            cursor_visible_duration = 0
            queue_state = "LOBBY"

        time.sleep(1.0)  # Run check loop every second

def print_calibration_coords():
    """Prints the relative coordinates of the current mouse cursor position"""
    abs_x, abs_y = pyautogui.position()
    rel_x = abs_x / screen_width
    rel_y = abs_y / screen_height
    log(f"Calibration Click at pixel: ({abs_x}, {abs_y}) -> RELATIVE COORDS: ({rel_x:.4f}, {rel_y:.4f})")
    try:
        import winsound
        winsound.Beep(800, 100)
    except ImportError:
        pass

def toggle_assistant():
    global active
    active = not active
    status = "ACTIVE" if active else "INACTIVE"
    log(f"Assistant is now {status}")
    if active:
        # Play a simple beep sound to notify user on Windows
        try:
            import winsound
            winsound.Beep(1000, 200)
        except ImportError:
            pass

def stop_assistant():
    global running, active
    active = False
    running = False
    log("Exiting CS2 Match Assistant...")
    try:
        import winsound
        winsound.Beep(600, 300)
    except ImportError:
        pass
    os._exit(0)

def main():
    global active
    print("=========================================")
    print("      CS2 Match Assistant v1.0")
    print("      (VAC Safe - External Only)")
    print("=========================================")
    print(f"Screen Resolution: {screen_width}x{screen_height}")
    print(f"Calibrate Hotkey : {CONFIG['CALIBRATE_HOTKEY'].upper()}")
    print(f"Toggle Hotkey    : {CONFIG['TOGGLE_HOTKEY'].upper()}")
    print(f"Exit Hotkey      : {CONFIG['EXIT_HOTKEY'].upper()}")
    print(f"Auto-Accept      : {'Enabled' if CONFIG['AUTO_ACCEPT_ENABLED'] else 'Disabled'}")
    print(f"Anti-AFK         : {'Enabled' if CONFIG['ANTI_AFK_ENABLED'] else 'Disabled'}")
    print("-----------------------------------------")
    print("INSTRUCTIONS:")
    print("1. Start CS2 in Windowed or Borderless Windowed mode.")
    print("2. Make sure the HUD color is default / not heavily customized.")
    print("3. Hover mouse and press F9 to calibrate/find coordinates.")
    print("4. Press F10 in-game to toggle the assistant on/off.")
    print("=========================================")

    # Register global hotkeys
    keyboard.add_hotkey(CONFIG["CALIBRATE_HOTKEY"], print_calibration_coords)
    keyboard.add_hotkey(CONFIG["TOGGLE_HOTKEY"], toggle_assistant)
    keyboard.add_hotkey(CONFIG["EXIT_HOTKEY"], stop_assistant)

    # Start threads
    threading.Thread(target=auto_accept_loop, daemon=True).start()
    threading.Thread(target=anti_afk_loop, daemon=True).start()
    threading.Thread(target=auto_queue_loop, daemon=True).start()

    # Keep main thread alive
    while running:
        time.sleep(0.5)

if __name__ == "__main__":
    main()
