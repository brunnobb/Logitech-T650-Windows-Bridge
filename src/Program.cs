using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using T650Bridge.Config;
using T650Bridge.Gesture;
using T650Bridge.Hidpp;
using T650Bridge.Ptp;
using T650Bridge.Touch;
using T650Bridge.UI;

namespace T650Bridge;

class Program
{
    static void Main(string[] args)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.8";

        bool hasUiAccess = HasUiAccess();
        bool isElevated = IsElevated();
        bool canBypassUipi = isElevated || hasUiAccess;

        const string MutexName = @"Global\LogitechT650Bridge_SingleInstanceMutex";
        using var singleMutex = new Mutex(true, MutexName, out bool isOnlyInstance);
        if (!isOnlyInstance)
        {
            Console.WriteLine("\n[Notice] Another instance of Logitech T650 Bridge is already running. Exiting.");
            return;
        }

        var settings = BridgeSettings.Load();

        if (args.Contains("--standalone"))
            settings.IntegrationMode = IntegrationMode.Standalone;
        else if (args.Contains("--ptp"))
            settings.IntegrationMode = IntegrationMode.PtpDriver;
        else if (args.Contains("--auto"))
            settings.IntegrationMode = IntegrationMode.Automatic;

        bool enableGestures = !args.Contains("--no-gesture");
        bool enableNotifications = args.Contains("--notifications") || args.Contains("--balloon") || settings.EnableNotifications;
        bool verboseDiag = args.Contains("--diag") || args.Contains("--test") || (!args.Contains("--silent") && !args.Contains("--tray"));
        bool silentStartup = args.Contains("--silent") || args.Contains("--tray") || args.Contains("--minimized") || settings.StartMinimized;
        bool autoHide = !args.Contains("--console") && !args.Contains("--no-hide") && settings.AutoHideOnConnect;
        bool testOnly = args.Contains("--test-only");

        using var tray = new TrayIconManager(settings, isElevated: canBypassUipi, startHidden: silentStartup || testOnly);
        tray.EnableNotifications = enableNotifications;
        tray.UpdateState(DeviceConnectionState.SearchingForReceiver, isPaused: !enableGestures);

        using var logWriter = new BridgeConsoleRedirectionWriter(
            Console.Out,
            tray.AppendLog,
            tray.UpdateTelemetry);
        Console.SetOut(logWriter);

        Console.WriteLine("===============================================================");
        Console.WriteLine($" Logitech T650 Bridge v{version} ");
        Console.WriteLine("===============================================================");

        Console.WriteLine($"[Version] Bridge Daemon v{version}");
        string securityStatus;
        if (hasUiAccess)
            securityStatus = "Standard User (UIAccess Active - UIPI Bypass Enabled)";
        else if (isElevated)
            securityStatus = "Administrator (UIPI Bypass Active)";
        else
            securityStatus = "Standard User (Medium Integrity)";

        Console.WriteLine($"[Security] Elevation: {securityStatus}");
        if (!canBypassUipi)
        {
            Console.WriteLine("[Security] Notice: Run with UIAccess / Administrator or install Virtual PTP Driver to control Admin windows.");
        }

        Console.WriteLine($"[Config] Gestures Enabled: {enableGestures}");
        Console.WriteLine($"[Config] Desktop Notifications: {(enableNotifications ? "Enabled" : "Disabled (Quiet)")}");
        Console.WriteLine($"[Config] Integration Mode: {settings.IntegrationMode}");
        Console.WriteLine($"[Config] Settings Path: {BridgeSettings.GetSettingsPath()}");

        AttachToInteractiveDesktop();

        bool hasDesktop = GetCursorPos(out var curPos);
        Console.WriteLine($"[Desktop] Interactive winsta0 access: {(hasDesktop ? $"OK (Cursor at {curPos.X},{curPos.Y})" : "Limited")}");

        if (testOnly) return;

        using var receiver = new UnifyingReceiver();
        var assembler = new FrameAssembler();
        using var gestureEngine = new PrecisionGestureEngine();
        gestureEngine.ApplySettings(settings);

        receiver.LogMessage += msg => Console.WriteLine($"[Receiver] {msg}");
        receiver.ReportReceived += report => assembler.ProcessRawReport(report);

        gestureEngine.GestureTriggered += gesture =>
        {
            Console.WriteLine($"\n>>> {gesture} <<<");
        };

        using var ptpDriver = new PtpDriverClient();

        void RefreshEngineState()
        {
            switch (settings.IntegrationMode)
            {
                case IntegrationMode.Standalone:
                    if (ptpDriver.IsConnected)
                    {
                        ptpDriver.Disconnect();
                    }
                    Console.WriteLine("[Engine] Integration Mode: Standalone -> High-performance C# software gesture engine active.");
                    tray.UpdateEngineStatus("Standalone (C# Engine)", Color.FromArgb(0, 220, 130));
                    break;

                case IntegrationMode.PtpDriver:
                    if (!ptpDriver.IsConnected)
                    {
                        ptpDriver.Connect();
                    }
                    if (ptpDriver.IsConnected)
                    {
                        Console.WriteLine("[Engine] Integration Mode: PTP Driver -> Connected to Virtual PTP Driver.");
                        tray.UpdateEngineStatus("PTP Driver (Connected)", Color.FromArgb(0, 220, 130));
                    }
                    else
                    {
                        Console.WriteLine("[Engine] Integration Mode: PTP Driver -> Virtual PTP Driver is NOT connected! Software fallback disabled (logs only).");
                        tray.UpdateEngineStatus("PTP Driver (Disconnected - Idle)", Color.FromArgb(255, 120, 120));
                    }
                    break;

                case IntegrationMode.Automatic:
                default:
                    if (!ptpDriver.IsConnected)
                    {
                        ptpDriver.Connect();
                    }
                    if (ptpDriver.IsConnected)
                    {
                        Console.WriteLine("[Tier 2] Connected to Virtual PTP Driver! Injecting directly into Windows Touch Stack.");
                        tray.UpdateEngineStatus("Automatic (PTP Active)", Color.FromArgb(0, 220, 130));
                    }
                    else
                    {
                        Console.WriteLine("[Tier 1] Virtual PTP Driver not detected. Running high-performance software gesture engine.");
                        tray.UpdateEngineStatus("Automatic (Fallback: C# Engine)", Color.FromArgb(240, 180, 40));
                    }
                    break;
            }
        }

        RefreshEngineState();

        using var debouncer = new TouchDebouncer(settings);
        debouncer.IsDoubleTapPending = () => gestureEngine.IsWaitingForDoubleTap;

        tray.SettingsChanged += updatedSettings =>
        {
            settings = updatedSettings;
            debouncer.Settings = settings;
            gestureEngine.ApplySettings(settings);
            autoHide = settings.AutoHideOnConnect;
            enableNotifications = settings.EnableNotifications;
            tray.EnableNotifications = enableNotifications;
            RefreshEngineState();
        };

        long frameIndex = 0;
        bool hadTouchingPrev = false;
        var sw = Stopwatch.StartNew();

        assembler.FrameReady += frame => debouncer.ProcessFrame(frame);

        debouncer.FrameReady += (frame, isLandingPhase) =>
        {
            frameIndex++;
            ushort scanTime = (ushort)((sw.ElapsedTicks * 10_000) / Stopwatch.Frequency);

            // 1. Build standard Microsoft PTP Report (with confidence suppression during initial landing)
            bool suppressConfidence = isLandingPhase && settings.SuppressLandingConfidence;
            var ptpReport = PtpTouchReport.FromTouchFrame(frame, scanTime, suppressConfidence);

            // 2. Inject based on IntegrationMode
            switch (settings.IntegrationMode)
            {
                case IntegrationMode.Standalone:
                    if (enableGestures)
                    {
                        gestureEngine.ProcessFrame(frame);
                    }
                    break;

                case IntegrationMode.PtpDriver:
                    if (ptpDriver.IsConnected)
                    {
                        ptpDriver.InjectReport(ptpReport);
                    }
                    // On PTP failure, leave only logs without fallback
                    break;

                case IntegrationMode.Automatic:
                default:
                    if (ptpDriver.IsConnected)
                    {
                        ptpDriver.InjectReport(ptpReport);
                    }
                    else if (enableGestures)
                    {
                        gestureEngine.ProcessFrame(frame);
                    }
                    break;
            }

            // 3. Diagnostic log window telemetry & frame logging output
            if (!tray.IsConsoleHidden)
            {
                var touching = frame.Contacts.Where(c => c.IsTouching).ToList();
                string contactsInfo = touching.Count == 0 
                    ? "None (Lifted)" 
                    : string.Join(", ", touching.Select(c => $"F{c.Id}:({c.X,4},{c.Y,4})"));

                string clickInfo = frame.PhysicalButtonPressed ? "[CLICK]" : "       ";

                tray.UpdateTelemetry($"[Frame #{frameIndex:D5}] Active: {touching.Count}/5 {clickInfo} | Contacts: {contactsInfo}");

                if (settings.LogTouchFrames)
                {
                    if (touching.Count > 0)
                    {
                        hadTouchingPrev = true;
                        string dragTag = gestureEngine.IsTapDragActive ? " [DRAGGING]" : "";
                        string confTag = suppressConfidence ? " [CONF=0]" : "";
                        Console.WriteLine($"[Frame #{frameIndex:D5}] Active: {touching.Count}/5 {clickInfo} | {contactsInfo}{confTag}{dragTag}");
                    }
                    else if (hadTouchingPrev)
                    {
                        hadTouchingPrev = false;
                        Console.WriteLine($"[Frame #{frameIndex:D5}] Lifted: 0/5 | All contacts released");
                    }
                }
            }
        };

        using var cts = new CancellationTokenSource();

        tray.ToggleStartStop += () =>
        {
            enableGestures = !enableGestures;
            string stateMsg = enableGestures ? "Active (Gestures Running)" : "Paused (Standard Mouse Only)";
            Console.WriteLine($"\n[Tray] Gestures toggled -> {stateMsg}");
            tray.UpdateState(receiver.State, isPaused: !enableGestures);
        };

        tray.RequestRewake += () =>
        {
            Console.WriteLine("\n[Tray] Re-sending raw multi-touch unlock packet to T650...");
            receiver.UnlockRawMode();
            if (settings.IntegrationMode != IntegrationMode.Standalone && !ptpDriver.IsConnected)
            {
                RefreshEngineState();
            }
            tray.ShowBalloon("Logitech T650", "Re-sent raw touch mode unlock command to pad.");
        };

        tray.RequestExit += () =>
        {
            Console.WriteLine("\n[Tray] Exit requested from taskbar menu.");
            cts.Cancel();
        };

        bool hasEverStreamed = false;

        receiver.StateChanged += state =>
        {
            tray.UpdateState(state, isPaused: !enableGestures);

            switch (state)
            {
                case DeviceConnectionState.SearchingForReceiver:
                    Console.WriteLine("\n[Receiver] Waiting for Logitech Unifying receiver to be plugged in...");
                    tray.ShowBalloon("Logitech T650", "Waiting for Logitech Unifying receiver USB dongle.", System.Windows.Forms.ToolTipIcon.Warning);
                    break;

                case DeviceConnectionState.ReceiverConnectedWaitingForPad:
                    Console.WriteLine("\n[Receiver] Unifying receiver ready! Waiting for T650 Touchpad to power on...");
                    tray.ShowBalloon("Logitech T650", "Unifying receiver connected. Turn on your T650 touchpad.", System.Windows.Forms.ToolTipIcon.Info);
                    break;

                case DeviceConnectionState.TouchpadActive:
                    Console.WriteLine("\n[Active] T650 Multi-touch stream is live! Try touching, scrolling, or swiping on the pad.\n");

                    if (!hasEverStreamed)
                    {
                        hasEverStreamed = true;
                        if (autoHide && !silentStartup && !tray.IsConsoleHidden)
                        {
                            Console.WriteLine("[Tray] Pad connected and active! Hiding log window to system tray...");
                            Thread.Sleep(600);
                            tray.HideConsole(notifyUser: true);
                        }
                        else
                        {
                            tray.ShowBalloon("Logitech T650 Active", "Multi-touch gestures ready. Right-click taskbar icon to manage.", System.Windows.Forms.ToolTipIcon.Info);
                        }
                    }
                    break;
            }
        };

        receiver.StartSupervisor();

        Console.WriteLine("[Tray] Taskbar notification icon active. Double-click or right-click icon to open/hide log window.");
        Console.WriteLine("[Keys] Press 'R' anytime to re-wake/unlock | Press 'H' or Esc to hide to tray | Press 'Q' to exit.\n");

        cts.Token.WaitHandle.WaitOne();

        Console.WriteLine("\nShutting down bridge daemon...");
    }

    private static void AttachToInteractiveDesktop()
    {
        try
        {
            const uint WINSTA_ALL_ACCESS = 0x37F;
            const uint DESKTOP_ALL_ACCESS = 0x1FF;

            IntPtr hWinSta = OpenWindowStation("winsta0", false, WINSTA_ALL_ACCESS);
            if (hWinSta != IntPtr.Zero)
            {
                SetProcessWindowStation(hWinSta);
                IntPtr hDesktop = OpenDesktop("Default", 0, false, DESKTOP_ALL_ACCESS);
                if (hDesktop != IntPtr.Zero)
                {
                    SetThreadDesktop(hDesktop);
                }
            }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetProcessWindowStation();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out uint TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenUIAccess = 26;

    private static bool HasUiAccess()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            if (OpenProcessToken(proc.Handle, TOKEN_QUERY, out IntPtr hToken))
            {
                try
                {
                    if (GetTokenInformation(hToken, TokenUIAccess, out uint uiAccess, sizeof(uint), out _))
                    {
                        return uiAccess != 0;
                    }
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
        }
        catch { }
        return false;
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
