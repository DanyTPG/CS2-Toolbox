using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

class CS2Assistant
{
    // Configuration
    static bool AUTO_ACCEPT_ENABLED = true;
    static bool AUTO_QUEUE_ENABLED = true;
    static bool ANTI_AFK_ENABLED = true;

    static double ACCEPT_SCAN_INTERVAL = 1.0; // seconds
    static double QUEUE_CHECK_INTERVAL = 2.0; // seconds (check for queue button frequency)
    static double QUEUE_DELAY_AFTER_MATCH = 10.0; // seconds

    // Calibrated relative coordinates (0.0 to 1.0) for this screen
    static double[] PLAY_COORDS = { 0.5255, 0.0343 };           // Play button (top center)
    static double[] MATCHMAKING_COORDS = { 0.4036, 0.0796 };    // Matchmaking tab
    static double[] PREMIER_COORDS = { 0.2339, 0.1204 };        // Premier Mode tab
    static double[] GO_COORDS = { 0.8328, 0.9583 };             // Go / Find Match button
    static double[] QUEUE_INDICATOR_COORDS = { 0.9609, 0.0593 }; // Top-right turns green when queuing

    static int AFK_MIN_INTERVAL = 30; // seconds
    static int AFK_MAX_INTERVAL = 90; // seconds
    static byte[] AFK_KEYS = { 0x57, 0x53, 0x41, 0x44 }; // W, S, A, D

    // Win32 API Constants & Structures
    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [DllImport("user32.dll")]
    static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    // State Variables
    static bool running = true;
    static bool active = false;
    static int screenWidth = 1920;
    static int screenHeight = 1080;

    static void Log(string message)
    {
        Console.WriteLine(string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message));
    }

    static bool GetCursorState()
    {
        CURSORINFO pci = new CURSORINFO();
        pci.cbSize = Marshal.SizeOf(pci);
        if (GetCursorInfo(out pci))
        {
            return (pci.flags & 0x00000001) != 0; // 0x00000001 is CURSOR_SHOWING
        }
        return true;
    }

    static bool IsCS2Active()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
        if (GetWindowText(hwnd, sb, 256) > 0)
        {
            return sb.ToString().Contains("Counter-Strike 2");
        }
        return false;
    }

    static void ClickRelative(double relX, double relY)
    {
        int absX = (int)(relX * screenWidth);
        int absY = (int)(relY * screenHeight);

        POINT origPos;
        GetCursorPos(out origPos);

        SetCursorPos(absX, absY);
        Thread.Sleep(100);
        mouse_event(0x02, 0, 0, 0, 0); // MOUSEEVENTF_LEFTDOWN = 0x02
        Thread.Sleep(50);
        mouse_event(0x04, 0, 0, 0, 0); // MOUSEEVENTF_LEFTUP = 0x04
        Thread.Sleep(100);

        SetCursorPos(origPos.x, origPos.y);
        Log(string.Format("Clicked at ({0}, {1})", absX, absY));
    }

    private static void ColorToHSV(byte r, byte g, byte b, out double hue, out double saturation, out double value)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));

        hue = 0;
        if (max == min)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = (60 * ((double)(g - b) / (max - min)) + 360) % 360;
        }
        else if (max == g)
        {
            hue = 60 * ((double)(b - r) / (max - min)) + 120;
        }
        else if (max == b)
        {
            hue = 60 * ((double)(r - g) / (max - min)) + 240;
        }

        saturation = (max == 0) ? 0 : 1d - (min / max);
        value = max / 255d;
    }

    static int CountGreenPixels(int left, int top, int width, int height, bool debug = false)
    {
        int greenCount = 0;
        try
        {
            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                }

                BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                unsafe
                {
                    byte* ptr = (byte*)data.Scan0;
                    int bytesPerPixel = 4;
                    int stride = data.Stride;

                    int maxH = 0, maxS = 0, maxV = 0;
                    int minH = 255, minS = 255, minV = 255;
                    double h, s, v;

                    for (int y = 0; y < height; y += 4)
                    {
                        for (int x = 0; x < width; x += 4)
                        {
                            int offset = y * stride + x * bytesPerPixel;
                            byte b = ptr[offset];
                            byte gColor = ptr[offset + 1];
                            byte r = ptr[offset + 2];

                            ColorToHSV(r, gColor, b, out h, out s, out v);

                            if (debug)
                            {
                                if (h > maxH) maxH = (int)h;
                                if (s * 255 > maxS) maxS = (int)(s * 255);
                                if (v * 255 > maxV) maxV = (int)(v * 255);

                                if (h < minH) minH = (int)h;
                                if (s * 255 < minS) minS = (int)(s * 255);
                                if (v * 255 < minV) minV = (int)(v * 255);
                            }

                            // Green Accept button criteria in HSV: Hue [80, 170], Saturation >= 0.35, Value >= 0.35
                            if (h >= 80 && h <= 170 && s >= 0.35 && v >= 0.35)
                            {
                                greenCount++;
                            }
                        }
                    }

                    if (debug)
                    {
                        Log(string.Format("[DEBUG] Go region HSV range: Min=[{0},{1},{2}] Max=[{3},{4},{5}]", (int)(minH / 2), minS, minV, (int)(maxH / 2), maxS, maxV));
                        Log(string.Format("[DEBUG] Matching green pixels: {0} (Threshold: 400)", greenCount * 16));
                    }
                }
                bmp.UnlockBits(data);
            }
        }
        catch (Exception ex)
        {
            Log(string.Format("Pixel scan error: {0}", ex.Message));
        }
        return greenCount * 16;
    }

    static bool ScanForAcceptButton(out int clickX, out int clickY)
    {
        clickX = 0;
        clickY = 0;

        int left = (int)(screenWidth * 0.35);
        int top = (int)(screenHeight * 0.35);
        int width = (int)(screenWidth * 0.30);
        int height = (int)(screenHeight * 0.30);

        // Accept button is usually quite wide, scan the bounding box area
        int matchedPixels = CountGreenPixels(left, top, width, height);

        if (matchedPixels > 500)
        {
            // Calculate center of screen relative area
            clickX = left + (width / 2);
            clickY = top + (height / 2);
            return true;
        }

        return false;
    }

    static Color GetPixelColor(double relX, double relY)
    {
        int x = (int)(relX * screenWidth);
        int y = (int)(relY * screenHeight);
        Color color = Color.Black;
        try
        {
            using (Bitmap bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(x, y, 0, 0, new Size(1, 1), CopyPixelOperation.SourceCopy);
                }
                color = bmp.GetPixel(0, 0);
            }
        }
        catch (Exception ex)
        {
            Log(string.Format("GetPixelColor error: {0}", ex.Message));
        }
        return color;
    }

    // Returns true if the green Go / Find Match button is visible and clickable
    static bool ScanForQueueButton(bool debug = false)
    {
        if (!IsCS2Active() || !GetCursorState())
            return false;

        Color pixel = GetPixelColor(GO_COORDS[0], GO_COORDS[1]);
        double h, s, v;
        ColorToHSV(pixel.R, pixel.G, pixel.B, out h, out s, out v);

        if (debug)
        {
            Log(string.Format("[DEBUG] Go Button Pixel RGB: ({0},{1},{2}) HSV: ({3:F1},{4:F2},{5:F2})", pixel.R, pixel.G, pixel.B, h, s, v));
        }

        // Go button is bright green: Hue ~100, high saturation and brightness
        // Sampled: RGB(86,228,21) HSV(101.2, 0.91, 0.89)
        return (h >= 80 && h <= 140 && s >= 0.50 && v >= 0.50);
    }

    // Returns true if the top-right queue indicator is green (currently searching for a match)
    static bool IsQueuing(bool debug = false)
    {
        Color pixel = GetPixelColor(QUEUE_INDICATOR_COORDS[0], QUEUE_INDICATOR_COORDS[1]);
        double h, s, v;
        ColorToHSV(pixel.R, pixel.G, pixel.B, out h, out s, out v);

        if (debug)
        {
            Log(string.Format("[DEBUG] Queue Indicator Pixel RGB: ({0},{1},{2}) HSV: ({3:F1},{4:F2},{5:F2})", pixel.R, pixel.G, pixel.B, h, s, v));
        }

        // Sampled while queuing: RGB(11,88,12) HSV(120.8, 0.88, 0.35)
        return (h >= 90 && h <= 150 && s >= 0.40 && v >= 0.20);
    }

    // Auto-Accept Loop
    static void AutoAcceptLoop()
    {
        while (running)
        {
            if (active && AUTO_ACCEPT_ENABLED)
            {
                try
                {
                    int clickX, clickY;
                    if (ScanForAcceptButton(out clickX, out clickY))
                    {
                        Log("Accept button found! Clicking...");

                        POINT origPos;
                        GetCursorPos(out origPos);

                        SetCursorPos(clickX, clickY);
                        Thread.Sleep(50);
                        mouse_event(0x02, 0, 0, 0, 0); // MOUSEEVENTF_LEFTDOWN = 0x02
                        Thread.Sleep(50);
                        mouse_event(0x04, 0, 0, 0, 0); // MOUSEEVENTF_LEFTUP = 0x04
                        Thread.Sleep(50);
                        SetCursorPos(origPos.x, origPos.y);

                        Log("Accepted match.");
                        Thread.Sleep(5000);
                    }
                }
                catch (Exception e)
                {
                    Log(string.Format("Error in Auto-Accept: {0}", e.Message));
                }
            }
            Thread.Sleep((int)(ACCEPT_SCAN_INTERVAL * 1000));
        }
    }

    // Anti-AFK Loop
    static void AntiAfkLoop()
    {
        Random rand = new Random();
        while (running)
        {
            if (active && ANTI_AFK_ENABLED)
            {
                if (IsCS2Active() && !GetCursorState())
                {
                    Log("Simulating anti-AFK movement...");
                    try
                    {
                        byte key = AFK_KEYS[rand.Next(AFK_KEYS.Length)];

                        // Press key
                        keybd_event(key, 0, 0, 0); // Key down
                        Thread.Sleep(rand.Next(100, 300));
                        keybd_event(key, 0, 2, 0); // Key up (KEYEVENTF_KEYUP = 2)

                        // Press opposing key to restore position
                        byte opposing = 0;
                        if (key == 0x57) opposing = 0x53; // W -> S
                        else if (key == 0x53) opposing = 0x57; // S -> W
                        else if (key == 0x41) opposing = 0x44; // A -> D
                        else if (key == 0x44) opposing = 0x41; // D -> A

                        if (opposing != 0)
                        {
                            Thread.Sleep(rand.Next(100, 200));
                            keybd_event(opposing, 0, 0, 0);
                            Thread.Sleep(rand.Next(100, 300));
                            keybd_event(opposing, 0, 2, 0);
                        }

                        // Jitter mouse
                        int dx = rand.Next(0, 2) == 0 ? -5 : 5;
                        int dy = rand.Next(0, 2) == 0 ? -5 : 5;
                        mouse_event(0x0001, (uint)dx, (uint)dy, 0, 0); // MOUSEEVENTF_MOVE = 0x0001
                        Thread.Sleep(100);
                        mouse_event(0x0001, (uint)-dx, (uint)-dy, 0, 0);
                    }
                    catch (Exception e)
                    {
                        Log(string.Format("Error in Anti-AFK: {0}", e.Message));
                    }
                }
            }

            int sleepDur = rand.Next(AFK_MIN_INTERVAL, AFK_MAX_INTERVAL);
            for (int i = 0; i < sleepDur; i++)
            {
                if (!running || !active) break;
                Thread.Sleep(1000);
            }
        }
    }

    // Auto-Queue Loop
    static void AutoQueueLoop()
    {
        double lastQueuedTime = 0;
        int cursorVisibleDuration = 0;
        string queueState = "LOBBY";
        double lastNavigationTime = 0;
        double lastScanTime = 0;

        while (running)
        {
            if (active && AUTO_QUEUE_ENABLED)
            {
                try
                {
                    bool csActive = IsCS2Active();
                    bool cursorVisible = GetCursorState();

                    if (csActive && cursorVisible)
                    {
                        cursorVisibleDuration++;
                    }
                    else
                    {
                        if (cursorVisibleDuration > 0)
                        {
                            Log("Cursor hidden or CS2 inactive. Resetting lobby duration.");
                        }
                        cursorVisibleDuration = 0;
                        if (!cursorVisible && queueState != "LOBBY")
                        {
                            Log("In-game detected (cursor hidden). Resetting queue state to LOBBY.");
                            queueState = "LOBBY";
                        }
                    }

                    if (cursorVisibleDuration >= 10)
                    {
                        double currentTime = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

                        if (queueState == "LOBBY")
                        {
                            if (currentTime - lastScanTime >= QUEUE_CHECK_INTERVAL)
                            {
                                lastScanTime = currentTime;

                                // Already searching? Jump to QUEUING state
                                if (IsQueuing(true))
                                {
                                    Log("Queue indicator green. Already searching. Entering QUEUING state.");
                                    queueState = "QUEUING";
                                    lastQueuedTime = currentTime;
                                    cursorVisibleDuration = 0;
                                }
                                // Go button visible? Click it
                                else if (ScanForQueueButton(true))
                                {
                                    Log("Go / Find Match button found! Starting queue...");
                                    ClickRelative(GO_COORDS[0], GO_COORDS[1]);

                                    Log("Queued successfully. Entering QUEUING state.");
                                    queueState = "QUEUING";
                                    lastQueuedTime = currentTime;
                                    cursorVisibleDuration = 0;
                                }
                                // Neither found: navigate Play -> Matchmaking -> Premier
                                else if (currentTime - lastNavigationTime > 15)
                                {
                                    Log("Lobby state active. Navigating to Play menu...");
                                    ClickRelative(PLAY_COORDS[0], PLAY_COORDS[1]);
                                    Thread.Sleep(1200);

                                    Log("Selecting Matchmaking...");
                                    ClickRelative(MATCHMAKING_COORDS[0], MATCHMAKING_COORDS[1]);
                                    Thread.Sleep(800);

                                    Log("Selecting Premier Mode...");
                                    ClickRelative(PREMIER_COORDS[0], PREMIER_COORDS[1]);
                                    Thread.Sleep(1200);

                                    lastNavigationTime = currentTime;
                                }
                                else
                                {
                                    Log("Go button not found yet. Waiting before re-navigating...");
                                }
                            }
                        }
                        else if (queueState == "QUEUING")
                        {
                            if (currentTime - lastQueuedTime > QUEUE_DELAY_AFTER_MATCH)
                            {
                                if (currentTime - lastScanTime >= QUEUE_CHECK_INTERVAL)
                                {
                                    lastScanTime = currentTime;

                                    // Still searching?
                                    if (IsQueuing(false))
                                    {
                                        // Keep waiting
                                    }
                                    // Go button back = queue cancelled
                                    else if (ScanForQueueButton(false))
                                    {
                                        Log("Go button detected while in QUEUING state. Queue cancelled. Resetting to LOBBY.");
                                        queueState = "LOBBY";
                                    }
                                    // Neither: might have left the menu or match started
                                    else
                                    {
                                        Log("Queue indicator and Go button both gone. Resetting to LOBBY.");
                                        queueState = "LOBBY";
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log(string.Format("Error in Auto-Queue: {0}", e.Message));
                }
            }
            else
            {
                cursorVisibleDuration = 0;
                queueState = "LOBBY";
            }

            Thread.Sleep(1000);
        }
    }

    static void PrintCalibrationCoords()
    {
        POINT pos;
        GetCursorPos(out pos);
        double relX = (double)pos.x / screenWidth;
        double relY = (double)pos.y / screenHeight;
        Color color = GetPixelColor(relX, relY);
        double h, s, v;
        ColorToHSV(color.R, color.G, color.B, out h, out s, out v);
        Log(string.Format("Calibration Click at pixel: ({0}, {1}) -> RELATIVE COORDS: ({2:F4}, {3:F4}) | Color: RGB({4},{5},{6}) HSV({7:F1},{8:F2},{9:F2})", pos.x, pos.y, relX, relY, color.R, color.G, color.B, h, s, v));
        Beep(800, 100);
    }

    static void ToggleAssistant()
    {
        active = !active;
        string status = active ? "ACTIVE" : "INACTIVE";
        Log(string.Format("Assistant is now {0}", status));
        if (active)
        {
            Beep(1000, 200);
        }
    }

    static void StopAssistant()
    {
        active = false;
        running = false;
        Log("Exiting CS2 Match Assistant...");
        Beep(600, 300);
        Environment.Exit(0);
    }

    static void Beep(int frequency, int duration)
    {
        try
        {
            Console.Beep(frequency, duration);
        }
        catch { }
    }

    // Get System Screen Resolution Natively
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    static void Main()
    {
        // 0 = SM_CXSCREEN, 1 = SM_CYSCREEN
        screenWidth = GetSystemMetrics(0);
        screenHeight = GetSystemMetrics(1);

        Console.WriteLine("=========================================");
        Console.WriteLine("      CS2 Match Assistant v1.0 (C#)");
        Console.WriteLine("      (VAC Safe - External Only)");
        Console.WriteLine("=========================================");
        Console.WriteLine(string.Format("Screen Resolution: {0}x{1}", screenWidth, screenHeight));
        Console.WriteLine("Calibrate Hotkey : F9");
        Console.WriteLine("Toggle Hotkey    : F10");
        Console.WriteLine("Exit Hotkey      : F11");
        Console.WriteLine(string.Format("Auto-Accept      : {0}", AUTO_ACCEPT_ENABLED ? "Enabled" : "Disabled"));
        Console.WriteLine(string.Format("Auto-Queue       : {0}", AUTO_QUEUE_ENABLED ? "Enabled" : "Disabled"));
        Console.WriteLine(string.Format("Anti-AFK         : {0}", ANTI_AFK_ENABLED ? "Enabled" : "Disabled"));
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine("INSTRUCTIONS:");
        Console.WriteLine("1. Start CS2 in Windowed or Borderless Windowed mode.");
        Console.WriteLine("2. Hover mouse and press F9 to calibrate/find coordinates.");
        Console.WriteLine("3. Press F10 in-game to toggle the assistant on/off.");
        Console.WriteLine("=========================================");

        // Start threads
        new Thread(AutoAcceptLoop) { IsBackground = true }.Start();
        new Thread(AntiAfkLoop) { IsBackground = true }.Start();
        new Thread(AutoQueueLoop) { IsBackground = true }.Start();

        // Hotkey polling loop in main thread
        while (running)
        {
            // F9
            if ((GetAsyncKeyState(0x78) & 0x8000) != 0)
            {
                PrintCalibrationCoords();
                Thread.Sleep(300); // Debounce
            }

            // F10
            if ((GetAsyncKeyState(0x79) & 0x8000) != 0)
            {
                ToggleAssistant();
                Thread.Sleep(300); // Debounce
            }

            // F11
            if ((GetAsyncKeyState(0x7A) & 0x8000) != 0)
            {
                StopAssistant();
            }

            Thread.Sleep(50);
        }
    }
}
