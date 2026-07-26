using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class CS2Assistant
{
    // Configuration
    static public bool AUTO_ACCEPT_ENABLED = true;
    static public bool AUTO_QUEUE_ENABLED = true;
    static public bool ANTI_AFK_ENABLED = true;

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

    // GSI Configuration
    static int GSI_PORT = 41234;
    static double GSI_HEARTBEAT_TIMEOUT = 60.0; // seconds

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

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const int HOTKEY_TOGGLE = 1;
    const int HOTKEY_EXIT = 2;
    const uint MOD_NONE = 0x0000;

    // State Variables
    static public bool running = true;
    static public bool active = false;
    static int screenWidth = 1920;
    static int screenHeight = 1080;
    static AssistantForm form;

    // Game State Integration (GSI) fields
    static public string gsMapPhase = "blank";
    static public string gsRoundPhase = "end";
    static public bool gsiConnected = false;
    static DateTime gsiLastUpdate = DateTime.MinValue;

    static void Log(string message)
    {
        string formatted = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message);
        if (form != null && !form.IsDisposed)
        {
            form.BeginInvoke(new Action(() => form.AppendLog(formatted)));
        }
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

    static void ProcessGsiData(string json)
    {
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(json);

            if (data.ContainsKey("map"))
            {
                Dictionary<string, object> map = data["map"] as Dictionary<string, object>;
                gsMapPhase = (map != null && map.ContainsKey("phase")) ? map["phase"].ToString() : "blank";
            }
            else
            {
                gsMapPhase = "blank";
            }

            if (data.ContainsKey("round"))
            {
                Dictionary<string, object> round = data["round"] as Dictionary<string, object>;
                gsRoundPhase = (round != null && round.ContainsKey("phase")) ? round["phase"].ToString() : "end";
            }
            else
            {
                gsRoundPhase = "end";
            }

            gsiLastUpdate = DateTime.UtcNow;
            gsiConnected = true;
        }
        catch (Exception ex)
        {
            Log(string.Format("GSI parse error: {0}", ex.Message));
        }
    }

    static void GsiListenerLoop()
    {
        HttpListener listener = new HttpListener();
        try
        {
            listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", GSI_PORT));
            listener.Start();
            Log(string.Format("GSI listener started on port {0}", GSI_PORT));
        }
        catch (Exception ex)
        {
            Log(string.Format("GSI listener failed to start: {0}. Running without GSI.", ex.Message));
            gsiConnected = false;
            return;
        }

        bool loggedWaiting = false;

        while (running)
        {
            try
            {
                IAsyncResult result = listener.BeginGetContext(null, null);

                int waited = 0;
                while (!result.IsCompleted && running)
                {
                    Thread.Sleep(500);
                    waited += 500;
                    if (waited > 30000 && !loggedWaiting)
                    {
                        loggedWaiting = true;
                        Log("No GSI data. Place gamestate_integration_cs2assistant.cfg in CS2 cfg folder.");
                    }
                }

                if (!result.IsCompleted || !running)
                    break;

                HttpListenerContext context = listener.EndGetContext(result);
                HttpListenerRequest request = context.Request;

                if (request.HttpMethod == "POST")
                {
                    System.IO.Stream body = request.InputStream;
                    System.IO.StreamReader reader = new System.IO.StreamReader(body, System.Text.Encoding.UTF8);
                    string json = reader.ReadToEnd();
                    reader.Close();
                    body.Close();

                    ProcessGsiData(json);

                    if (!loggedWaiting)
                    {
                        Log("GSI connected. Map phase: " + gsMapPhase);
                        loggedWaiting = true;
                    }
                }

                HttpListenerResponse response = context.Response;
                response.StatusCode = 200;
                response.Close();
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log(string.Format("GSI listener error: {0}", ex.Message));
            }
        }

        try { listener.Stop(); listener.Close(); }
        catch { }

        Log("GSI listener stopped.");
    }

    static void GsiHeartbeatCheckLoop()
    {
        while (running)
        {
            Thread.Sleep(10000);

            if (gsiConnected)
            {
                double elapsed = (DateTime.UtcNow - gsiLastUpdate).TotalSeconds;
                if (elapsed > GSI_HEARTBEAT_TIMEOUT)
                {
                    Log("GSI heartbeat timeout. Falling back to pixel detection.");
                    gsiConnected = false;
                    gsMapPhase = "blank";
                    gsRoundPhase = "end";
                }
            }
        }
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

    // Returns true if the center pixel indicates match acceptance (red ban/pick phase)
    // Prevents re-queueing when GSI still shows "blank" after accepting
    static bool IsMatchAccepted()
    {
        if (!IsCS2Active())
            return false;

        Color pixel = GetPixelColor(0.5005, 0.5565);
        double h, s, v;
        ColorToHSV(pixel.R, pixel.G, pixel.B, out h, out s, out v);

        // Red color in HSV: Hue ~0 (or ~360), moderate-high saturation
        // Sampled: RGB(168,77,76) HSV(0.7, 0.55, 0.66)
        return (h <= 10 || h >= 350) && s >= 0.40 && v >= 0.50;
    }

    // Auto-Accept Loop
    static void AutoAcceptLoop()
    {
        while (running)
        {
            if (active && AUTO_ACCEPT_ENABLED)
            {
                // GSI gating: only accept when in menu state
                if (gsiConnected && gsMapPhase != "blank")
                {
                    Thread.Sleep((int)(ACCEPT_SCAN_INTERVAL * 1000));
                    continue;
                }

                try
                {
                    // Scan center screen for green pixels (Accept button)
                    int left = (int)(screenWidth * 0.35);
                    int top = (int)(screenHeight * 0.35);
                    int width = (int)(screenWidth * 0.30);
                    int height = (int)(screenHeight * 0.30);
                    int matchedPixels = CountGreenPixels(left, top, width, height);

                    if (matchedPixels > 500)
                    {
                        // Accept button visible - click at calibrated coords
                        int clickX = (int)(screenWidth * 0.4984);
                        int clickY = (int)(screenHeight * 0.4167);

                        Log(string.Format("Accept button found ({0} green px). Clicking ({1}, {2})", matchedPixels, clickX, clickY));

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
                bool inMatch;
                if (gsiConnected)
                {
                    inMatch = (gsMapPhase == "live" || gsMapPhase == "warmup");
                }
                else
                {
                    inMatch = IsCS2Active() && !GetCursorState();
                }
                if (inMatch)
                {
                    Log("Simulating anti-AFK movement...");
                    try
                    {
                        byte key = AFK_KEYS[rand.Next(4)]; // LockArray index check

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
                    }
                    catch (Exception e)
                    {
                        Log(string.Format("Error in Anti-AFK: {0}", e.Message));
                    }
                }
            }

            int sleepMs = rand.Next(AFK_MIN_INTERVAL * 1000, AFK_MAX_INTERVAL * 1000);
            for (int i = 0; i < sleepMs; i += 1000)
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
                        bool gsiInMatch = gsiConnected && (gsMapPhase == "live" || gsMapPhase == "warmup" || gsMapPhase == "intermission");
                        if ((gsiInMatch || !cursorVisible) && queueState != "LOBBY")
                        {
                            string reason = gsiConnected ? "GSI map.phase=" + gsMapPhase : "cursor hidden";
                            Log(string.Format("In-game detected ({0}). Resetting queue state to LOBBY.", reason));
                            queueState = "LOBBY";
                        }
                    }

                    // GSI fast-path: skip cursor wait when we know we're in menus
                    bool confirmedLobby = gsiConnected && (gsMapPhase == "blank" || gsMapPhase == "game_over");
                    // Pixel check: red ban/pick pixel indicates match was accepted
                    bool isMatchAccepted = IsMatchAccepted();

                    // Don't treat as lobby if match is accepted (ban/pick phase)
                    if (isMatchAccepted)
                    {
                        confirmedLobby = false;
                    }

                    if ((confirmedLobby || isMatchAccepted) && cursorVisibleDuration < 10)
                    {
                        cursorVisibleDuration = 10;
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
                                // Match already accepted (red ban/pick pixel)? Don't re-queue
                                else if (isMatchAccepted)
                                {
                                    Log("Match accepted detected (red ban/pick pixel). Not queuing.");
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
                            // GSI fast-detect: match started
                            if (gsiConnected && (gsMapPhase == "live" || gsMapPhase == "warmup"))
                            {
                                Log("GSI confirms match started. Resetting to LOBBY.");
                                queueState = "LOBBY";
                                cursorVisibleDuration = 0;
                            }
                            else if (currentTime - lastQueuedTime > QUEUE_DELAY_AFTER_MATCH)
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
                        else
                        {
                            lastNavigationTime = currentTime;
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

    static public void PrintCalibrationCoords()
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

    static public void ToggleAssistant()
    {
        active = !active;
        string status = active ? "ACTIVE" : "INACTIVE";
        Log(string.Format("Assistant is now {0}", status));
        if (form != null && !form.IsDisposed)
        {
            form.BeginInvoke(new Action(() => form.UpdateState()));
        }
        if (active)
        {
            Beep(1000, 200);
        }
    }

    static public void StopAssistant()
    {
        active = false;
        running = false;
        Log("Exiting CS2 Match Assistant...");
        if (form != null && !form.IsDisposed)
        {
            form.BeginInvoke(new Action(() => Application.Exit()));
        }
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

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [STAThread]
    static void Main()
    {
        // Hide console window immediately to prevent flash
        IntPtr consoleWnd = GetConsoleWindow();
        if (consoleWnd != IntPtr.Zero)
            ShowWindow(consoleWnd, 0);

        // 0 = SM_CXSCREEN, 1 = SM_CYSCREEN
        screenWidth = GetSystemMetrics(0);
        screenHeight = GetSystemMetrics(1);

        Application.EnableVisualStyles();

        // Start background threads
        new Thread(AutoAcceptLoop) { IsBackground = true }.Start();
        new Thread(AntiAfkLoop) { IsBackground = true }.Start();
        new Thread(AutoQueueLoop) { IsBackground = true }.Start();
        new Thread(GsiListenerLoop) { IsBackground = true }.Start();
        new Thread(GsiHeartbeatCheckLoop) { IsBackground = true }.Start();

        // Auto-install GSI cfg to CS2 cfg folder
        try
        {
            string sourcePath = System.AppDomain.CurrentDomain.BaseDirectory + "gamestate_integration_cs2assistant.cfg";
            string cs2ConfigDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\Steam\steamapps\common\Counter-Strike 2\game\csgo\cfg\";
            string targetConfig = cs2ConfigDir + "gamestate_integration_cs2assistant.cfg";

            if (!System.IO.File.Exists(targetConfig) && System.IO.File.Exists(sourcePath))
            {
                System.IO.Directory.CreateDirectory(cs2ConfigDir);
                System.IO.File.Copy(sourcePath, targetConfig);
                Log("Auto-installed GSI config to CS2 cfg folder.");
            }
        }
        catch (Exception copyEx)
        {
            Log(string.Format("Failed to auto-install GSI config: {0}", copyEx.Message));
        }

        // Launch GUI
        form = new AssistantForm();
        Application.Run(form);
    }
}

class AssistantForm : Form
{
    private TextBox logBox;
    private Label statusLabel;
    private Button btnToggle;
    private CheckBox chkAccept;
    private CheckBox chkQueue;
    private CheckBox chkAfk;
    private Label lblUptime;
    private System.Windows.Forms.Timer timer;
    private DateTime startTime;
    private Label lblGsiStatus;

    public AssistantForm()
    {
        startTime = DateTime.Now;
        InitializeComponents();
        UpdateState();

        timer = new System.Windows.Forms.Timer();
        timer.Interval = 1000;
        timer.Tick += Timer_Tick;
        timer.Start();
    }

    private void InitializeComponents()
    {
        this.Text = "CS2 Match Assistant v1.0";
        this.Size = new System.Drawing.Size(360, 460);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
        this.ForeColor = System.Drawing.Color.White;
        this.KeyPreview = true;
        this.KeyDown += OnKeyDown;

        // Register global hotkeys
        RegisterHotKey(this.Handle, HOTKEY_TOGGLE, MOD_NONE, (uint)Keys.F10);
        RegisterHotKey(this.Handle, HOTKEY_EXIT, MOD_NONE, (uint)Keys.F11);

        // Status label
        statusLabel = new Label();
        statusLabel.Text = "INACTIVE";
        statusLabel.Font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold);
        statusLabel.ForeColor = System.Drawing.Color.Red;
        statusLabel.Location = new System.Drawing.Point(12, 10);
        statusLabel.AutoSize = true;
        this.Controls.Add(statusLabel);

        // Feature toggles
        chkAccept = new CheckBox();
        chkAccept.Text = "Auto-Accept";
        chkAccept.Checked = true;
        chkAccept.Font = new System.Drawing.Font("Segoe UI", 10);
        chkAccept.ForeColor = System.Drawing.Color.White;
        chkAccept.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
        chkAccept.Location = new System.Drawing.Point(15, 50);
        chkAccept.Size = new System.Drawing.Size(180, 25);
        chkAccept.CheckedChanged += ChkAccept_CheckedChanged;
        this.Controls.Add(chkAccept);

        chkQueue = new CheckBox();
        chkQueue.Text = "Auto-Queue (Premier)";
        chkQueue.Checked = true;
        chkQueue.Font = new System.Drawing.Font("Segoe UI", 10);
        chkQueue.ForeColor = System.Drawing.Color.White;
        chkQueue.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
        chkQueue.Location = new System.Drawing.Point(15, 80);
        chkQueue.Size = new System.Drawing.Size(180, 25);
        chkQueue.CheckedChanged += ChkQueue_CheckedChanged;
        this.Controls.Add(chkQueue);

        chkAfk = new CheckBox();
        chkAfk.Text = "Anti-AFK";
        chkAfk.Checked = true;
        chkAfk.Font = new System.Drawing.Font("Segoe UI", 10);
        chkAfk.ForeColor = System.Drawing.Color.White;
        chkAfk.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
        chkAfk.Location = new System.Drawing.Point(15, 110);
        chkAfk.Size = new System.Drawing.Size(180, 25);
        chkAfk.CheckedChanged += ChkAfk_CheckedChanged;
        this.Controls.Add(chkAfk);

        // Buttons
        btnToggle = new Button();
        btnToggle.Text = "Start";
        btnToggle.Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold);
        btnToggle.BackColor = System.Drawing.Color.FromArgb(0, 120, 0);
        btnToggle.ForeColor = System.Drawing.Color.White;
        btnToggle.FlatStyle = FlatStyle.Flat;
        btnToggle.Location = new System.Drawing.Point(15, 150);
        btnToggle.Size = new System.Drawing.Size(150, 35);
        btnToggle.Click += BtnToggle_Click;
        this.Controls.Add(btnToggle);

        // Log area
        Label lblLog = new Label();
        lblLog.Text = "Log:";
        lblLog.Font = new System.Drawing.Font("Segoe UI", 9);
        lblLog.ForeColor = System.Drawing.Color.Gray;
        lblLog.Location = new System.Drawing.Point(12, 200);
        lblLog.AutoSize = true;
        this.Controls.Add(lblLog);

        logBox = new TextBox();
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.BackColor = System.Drawing.Color.FromArgb(20, 20, 20);
        logBox.ForeColor = System.Drawing.Color.LightGreen;
        logBox.Font = new System.Drawing.Font("Consolas", 9);
        logBox.Location = new System.Drawing.Point(12, 220);
        logBox.Size = new System.Drawing.Size(320, 140);
        this.Controls.Add(logBox);

        // Uptime
        lblUptime = new Label();
        lblUptime.Text = "Uptime: 00:00:00";
        lblUptime.Font = new System.Drawing.Font("Segoe UI", 9);
        lblUptime.ForeColor = System.Drawing.Color.Gray;
        lblUptime.Location = new System.Drawing.Point(12, 390);
        lblUptime.AutoSize = true;
        this.Controls.Add(lblUptime);

        // Hotkey hint
        Label lblHotkeys = new Label();
        lblHotkeys.Text = "Hotkeys: F10=Toggle  F11=Exit";
        lblHotkeys.Font = new System.Drawing.Font("Segoe UI", 8);
        lblHotkeys.ForeColor = System.Drawing.Color.Gray;
        lblHotkeys.Location = new System.Drawing.Point(12, 410);
        lblHotkeys.AutoSize = true;
        this.Controls.Add(lblHotkeys);

        // GSI status
        lblGsiStatus = new Label();
        lblGsiStatus.Text = "GSI: Disconnected";
        lblGsiStatus.Font = new System.Drawing.Font("Segoe UI", 8);
        lblGsiStatus.ForeColor = System.Drawing.Color.Gray;
        lblGsiStatus.Location = new System.Drawing.Point(12, 370);
        lblGsiStatus.AutoSize = true;
        this.Controls.Add(lblGsiStatus);
    }

    public void AppendLog(string message)
    {
        if (logBox != null && !logBox.IsDisposed)
        {
            logBox.AppendText(message + Environment.NewLine);
        }
    }

    public void UpdateState()
    {
        if (statusLabel == null || statusLabel.IsDisposed) return;
        if (CS2Assistant.active)
        {
            statusLabel.Text = "ACTIVE";
            statusLabel.ForeColor = System.Drawing.Color.LimeGreen;
            btnToggle.Text = "Stop";
            btnToggle.BackColor = System.Drawing.Color.FromArgb(180, 0, 0);
        }
        else
        {
            statusLabel.Text = "INACTIVE";
            statusLabel.ForeColor = System.Drawing.Color.Red;
            btnToggle.Text = "Start";
            btnToggle.BackColor = System.Drawing.Color.FromArgb(0, 120, 0);
        }
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        TimeSpan elapsed = DateTime.Now - startTime;
        lblUptime.Text = string.Format("Uptime: {0:00}:{1:00}:{2:00}",
            (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);

        if (CS2Assistant.gsiConnected)
        {
            lblGsiStatus.Text = "GSI: Connected (" + CS2Assistant.gsMapPhase + ")";
            lblGsiStatus.ForeColor = System.Drawing.Color.LimeGreen;
        }
        else
        {
            lblGsiStatus.Text = "GSI: Disconnected";
            lblGsiStatus.ForeColor = System.Drawing.Color.Gray;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F9)
        {
            CS2Assistant.PrintCalibrationCoords();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F10)
        {
            CS2Assistant.ToggleAssistant();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F11)
        {
            CS2Assistant.StopAssistant();
            e.Handled = true;
        }
    }

    private void ChkAccept_CheckedChanged(object sender, EventArgs e)
    {
        CS2Assistant.AUTO_ACCEPT_ENABLED = chkAccept.Checked;
    }

    private void ChkQueue_CheckedChanged(object sender, EventArgs e)
    {
        CS2Assistant.AUTO_QUEUE_ENABLED = chkQueue.Checked;
    }

    private void ChkAfk_CheckedChanged(object sender, EventArgs e)
    {
        CS2Assistant.ANTI_AFK_ENABLED = chkAfk.Checked;
    }

    private void BtnToggle_Click(object sender, EventArgs e)
    {
        CS2Assistant.ToggleAssistant();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CS2Assistant.running = false;
        CS2Assistant.active = false;
        timer.Stop();
        base.OnFormClosing(e);
    }
}
