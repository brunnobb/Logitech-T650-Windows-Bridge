using System.Runtime.InteropServices;
using T650Bridge.Touch;

namespace T650Bridge.Gesture;

/// <summary>
/// Precision Gesture Engine for Logitech T650.
/// Translates raw touch frames into smooth Windows cursor movement, tap clicks,
/// two-finger scrolling, three-finger multitasking, and four-finger virtual desktop navigation.
/// </summary>
public class PrecisionGestureEngine : IDisposable
{
    private readonly object _syncLock = new();

    // Touch session tracking
    private bool _sessionActive;
    private DateTime _sessionStartTime = DateTime.MinValue;
    private int _maxFingersInSession;
    private double _totalDX;
    private double _totalDY;
    private bool _swipeTriggered;
    private readonly Dictionary<byte, (ushort X, ushort Y)> _lastContacts = new();

    // Physical button / dragging state
    private bool _isPhysicalDown;

    // Tap to Drag state tracking
    public bool TapToDragEnabled { get; set; } = true;
    public double TapDragTimeoutMs { get; set; } = 300.0;
    private bool _isTapDragActive;
    public bool IsTapDragActive => _isTapDragActive;
    private bool _tapDragMoved;
    private double _tapDragDistance;

    // Disambiguation: pending single-tap timer to prevent double-click on tap-to-drag start
    private System.Threading.Timer? _singleTapTimer;
    private bool _pendingSingleTap;
    private (ushort X, ushort Y) _pendingTapPosition;
    private DateTime _pendingTapTime = DateTime.MinValue;

    // Fractional scroll accumulator
    private double _scrollAccumY;
    private double _scrollAccumX;

    // Cursor Sensitivity Setting (Default: 1.15)
    public double CursorSensitivity { get; set; } = 1.15;

    // 3-Finger Gesture Shortcuts
    public string Gesture3Click { get; set; } = "Win+S";
    public string Gesture3DragUp { get; set; } = "Win+Tab";
    public string Gesture3DragDown { get; set; } = "Win+D";
    public string Gesture3DragLeft { get; set; } = "Alt+Shift+Tab";
    public string Gesture3DragRight { get; set; } = "Alt+Tab";

    // 4-Finger Gesture Shortcuts
    public string Gesture4Click { get; set; } = "Win+A";
    public string Gesture4DragUp { get; set; } = "Win+Tab";
    public string Gesture4DragDown { get; set; } = "Win+A";
    public string Gesture4DragLeft { get; set; } = "Ctrl+Win+Left";
    public string Gesture4DragRight { get; set; } = "Ctrl+Win+Right";

    public void ApplySettings(Config.BridgeSettings settings)
    {
        CursorSensitivity = settings.CursorSensitivity;
        TapToDragEnabled = settings.TapToDragEnabled;
        TapDragTimeoutMs = settings.TapDragTimeoutMs;
        Gesture3Click = settings.Gesture3Click;
        Gesture3DragUp = settings.Gesture3DragUp;
        Gesture3DragDown = settings.Gesture3DragDown;
        Gesture3DragLeft = settings.Gesture3DragLeft;
        Gesture3DragRight = settings.Gesture3DragRight;
        Gesture4Click = settings.Gesture4Click;
        Gesture4DragUp = settings.Gesture4DragUp;
        Gesture4DragDown = settings.Gesture4DragDown;
        Gesture4DragLeft = settings.Gesture4DragLeft;
        Gesture4DragRight = settings.Gesture4DragRight;
    }

    private void ExecuteGesture(string gestureName, string? shortcut, string logPrefix)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            GestureTriggered?.Invoke($"{logPrefix}: [Disabled / Empty]");
            return;
        }

        if (ShortcutExecutor.ExecuteShortcut(shortcut))
        {
            GestureTriggered?.Invoke($"{logPrefix} ({shortcut})");
        }
        else
        {
            GestureTriggered?.Invoke($"{logPrefix} [Unrecognized: {shortcut}]");
        }
    }

    // Tuning constants
    private const double SwipeTravelThreshold = 140.0; // ~6mm movement required to trigger 3/4 finger swipe
    private const double TapMaxTravel = 60.0;          // Max distance allowed to qualify as a tap
    private const double TapMaxDurationMs = 300.0;     // Max contact time in ms to qualify as a tap

    public event Action<string>? GestureTriggered;

    public void ProcessFrame(TouchFrame frame)
    {
        EnsureInteractiveDesktop();

        var contacts = frame.Contacts.Where(c => c.IsTouching).ToList();
        int count = contacts.Count;

        // 1. Physical Hardware Click (bottom mechanical switches)
        if (frame.PhysicalButtonPressed && !_isPhysicalDown)
        {
            FirePendingSingleTapNow();
            _isPhysicalDown = true;
            SendMouseButton(leftDown: true);
            GestureTriggered?.Invoke("Physical Click: DOWN");
        }
        else if (!frame.PhysicalButtonPressed && _isPhysicalDown)
        {
            _isPhysicalDown = false;
            SendMouseButton(leftDown: false);
            GestureTriggered?.Invoke("Physical Click: UP");
        }

        // 2. Touch Session Management
        if (count > 0)
        {
            if (!_sessionActive)
            {
                // New session starting
                _sessionActive = true;
                _sessionStartTime = DateTime.UtcNow;
                _maxFingersInSession = count;
                _totalDX = 0;
                _totalDY = 0;
                _swipeTriggered = false;
                _scrollAccumY = 0;
                _scrollAccumX = 0;
                _lastContacts.Clear();

                // Check if a single-tap was pending disambiguation
                bool isDoubleTap = false;
                lock (_syncLock)
                {
                    if (_pendingSingleTap)
                    {
                        if (TapToDragEnabled && count == 1)
                        {
                            double msSinceFirstTap = (DateTime.UtcNow - _pendingTapTime).TotalMilliseconds;
                            if (msSinceFirstTap <= TapDragTimeoutMs)
                            {
                                var firstContact = contacts[0];
                                double distFromFirstTap = Math.Sqrt(
                                    Math.Pow(firstContact.X - _pendingTapPosition.X, 2) +
                                    Math.Pow(firstContact.Y - _pendingTapPosition.Y, 2)
                                );

                                if (distFromFirstTap <= TapMaxTravel * 2.5)
                                {
                                    isDoubleTap = true;
                                    CancelPendingSingleTap();
                                }
                            }
                        }

                        if (!isDoubleTap)
                        {
                            // Not a double-tap: flush pending single click immediately so it isn't lost
                            FirePendingSingleTapNow();
                        }
                    }
                }

                if (isDoubleTap)
                {
                    _isTapDragActive = true;
                    _tapDragMoved = false;
                    _tapDragDistance = 0;
                    SendMouseButton(leftDown: true);
                    GestureTriggered?.Invoke("Tap to Drag: START (Left Button DOWN)");
                }
            }
            else
            {
                // Fingers may land slightly out of sync; ramp up max finger count
                if (count > _maxFingersInSession)
                {
                    _maxFingersInSession = count;
                }

                if (_isTapDragActive && count > 1)
                {
                    _isTapDragActive = false;
                    SendMouseButton(leftDown: false);
                    GestureTriggered?.Invoke("Tap to Drag: CANCELLED (Multi-finger)");
                }
            }

            // Calculate movement delta across currently tracked contacts
            double frameDx = 0;
            double frameDy = 0;
            int trackedCount = 0;

            foreach (var c in contacts)
            {
                if (_lastContacts.TryGetValue(c.Id, out var prev))
                {
                    frameDx += (c.X - prev.X);
                    frameDy += (c.Y - prev.Y);
                    trackedCount++;
                }
            }

            if (trackedCount > 0)
            {
                frameDx /= trackedCount;
                frameDy /= trackedCount;
                _totalDX += frameDx;
                _totalDY += frameDy;

                if (_isTapDragActive)
                {
                    _tapDragDistance += Math.Sqrt(frameDx * frameDx + frameDy * frameDy);
                    if (_tapDragDistance > 20.0)
                    {
                        _tapDragMoved = true;
                    }
                }
            }

            // Dispatch gesture actions based on max finger count
            switch (_maxFingersInSession)
            {
                case 1:
                    // 1-Finger Cursor Movement
                    if (trackedCount > 0 && (Math.Abs(frameDx) > 0.1 || Math.Abs(frameDy) > 0.1))
                    {
                        double dist = Math.Sqrt(frameDx * frameDx + frameDy * frameDy);
                        double accel = 1.0;
                        if (dist > 8) accel = 1.2;
                        if (dist > 20) accel = 1.5;
                        if (dist > 35) accel = 2.0;

                        int moveX = (int)Math.Round(frameDx * CursorSensitivity * accel);
                        int moveY = (int)Math.Round(frameDy * CursorSensitivity * accel);

                        if (moveX != 0 || moveY != 0)
                        {
                            SendMouseMove(moveX, moveY);
                        }
                    }
                    break;

                case 2:
                    // 2-Finger Smooth Scrolling
                    if (trackedCount > 0)
                    {
                        // Inverted Y: moving fingers down increases Y (frameDy > 0), so content moves down (scroll UP)
                        // In Windows, negative WHEEL_DELTA scrolls DOWN.
                        // Standard trackpad natural scroll: fingers moving UP (frameDy < 0) scrolls page DOWN.
                        _scrollAccumY += (-frameDy * 6.0);
                        _scrollAccumX += (frameDx * 6.0);

                        if (Math.Abs(_scrollAccumY) >= 30)
                        {
                            int ticks = (int)_scrollAccumY;
                            _scrollAccumY -= ticks;
                            SendMouseScroll(ticks);
                        }

                        if (Math.Abs(_scrollAccumX) >= 30)
                        {
                            int ticks = (int)_scrollAccumX;
                            _scrollAccumX -= ticks;
                            SendMouseHorizontalScroll(ticks);
                        }
                    }
                    break;

                case 3:
                    // 3-Finger Multi-tasking Swipes
                    if (!_swipeTriggered)
                    {
                        double travel = Math.Sqrt(_totalDX * _totalDX + _totalDY * _totalDY);
                        if (travel >= SwipeTravelThreshold)
                        {
                            _swipeTriggered = true;
                            if (Math.Abs(_totalDY) > Math.Abs(_totalDX))
                            {
                                if (_totalDY < 0)
                                {
                                    ExecuteGesture("3 Drag Up", Gesture3DragUp, "Gesture: 3-Finger Swipe UP");
                                }
                                else
                                {
                                    ExecuteGesture("3 Drag Down", Gesture3DragDown, "Gesture: 3-Finger Swipe DOWN");
                                }
                            }
                            else
                            {
                                if (_totalDX > 0)
                                {
                                    ExecuteGesture("3 Right", Gesture3DragRight, "Gesture: 3-Finger Swipe RIGHT");
                                }
                                else
                                {
                                    ExecuteGesture("3 Left", Gesture3DragLeft, "Gesture: 3-Finger Swipe LEFT");
                                }
                            }
                        }
                    }
                    break;

                case 4:
                case 5:
                    // 4/5-Finger Virtual Desktop Swipes
                    if (!_swipeTriggered)
                    {
                        double travel = Math.Sqrt(_totalDX * _totalDX + _totalDY * _totalDY);
                        if (travel >= SwipeTravelThreshold)
                        {
                            _swipeTriggered = true;
                            if (Math.Abs(_totalDX) > Math.Abs(_totalDY))
                            {
                                if (_totalDX > 0)
                                {
                                    ExecuteGesture("4 Right", Gesture4DragRight, "Gesture: 4-Finger Swipe RIGHT");
                                }
                                else
                                {
                                    ExecuteGesture("4 Left", Gesture4DragLeft, "Gesture: 4-Finger Swipe LEFT");
                                }
                            }
                            else
                            {
                                if (_totalDY < 0)
                                {
                                    ExecuteGesture("4 Drag Up", Gesture4DragUp, "Gesture: 4-Finger Swipe UP");
                                }
                                else
                                {
                                    ExecuteGesture("4 Drag Down", Gesture4DragDown, "Gesture: 4-Finger Swipe DOWN");
                                }
                            }
                        }
                    }
                    break;
            }

            // Record contact positions for next frame
            _lastContacts.Clear();
            foreach (var c in contacts)
            {
                _lastContacts[c.Id] = (c.X, c.Y);
            }
        }
        else // count == 0: All fingers lifted
        {
            if (_sessionActive)
            {
                var duration = (DateTime.UtcNow - _sessionStartTime).TotalMilliseconds;
                double totalTravel = Math.Sqrt(_totalDX * _totalDX + _totalDY * _totalDY);

                // Tap to Drag Release
                if (_isTapDragActive)
                {
                    _isTapDragActive = false;
                    SendMouseButton(leftDown: false);

                    if (_tapDragMoved)
                    {
                        GestureTriggered?.Invoke("Tap to Drag: RELEASED (Left Button UP)");
                    }
                    else
                    {
                        // Double-tap in place without moving: User intended a Double-Click!
                        if (duration < TapMaxDurationMs)
                        {
                            SendLeftClick();
                            GestureTriggered?.Invoke("Double Tap: Double Click in place");
                        }
                        else
                        {
                            GestureTriggered?.Invoke("Tap to Drag: RELEASED (Left Button UP)");
                        }
                    }
                }
                // Tap detection: short duration, minimal travel, no physical button held, no swipe fired
                else if (!_swipeTriggered && !_isPhysicalDown && duration < TapMaxDurationMs && totalTravel < TapMaxTravel)
                {
                    if (_maxFingersInSession == 1)
                    {
                        var pt = _lastContacts.Count > 0 ? _lastContacts.Values.First() : (ushort.MinValue, ushort.MinValue);

                        if (TapToDragEnabled)
                        {
                            // Disambiguation window: wait to see if a second tap touches down
                            ArmPendingSingleTap(pt);
                        }
                        else
                        {
                            GestureTriggered?.Invoke("Tap: 1-Finger (Left Click)");
                            SendLeftClick();
                        }
                    }
                    else if (_maxFingersInSession == 2)
                    {
                        FirePendingSingleTapNow();
                        GestureTriggered?.Invoke("Tap: 2-Finger (Right Click)");
                        SendRightClick();
                    }
                    else if (_maxFingersInSession == 3)
                    {
                        FirePendingSingleTapNow();
                        ExecuteGesture("3 Click", Gesture3Click, "Tap: 3-Finger");
                    }
                    else if (_maxFingersInSession >= 4)
                    {
                        FirePendingSingleTapNow();
                        ExecuteGesture("4 Click", Gesture4Click, "Tap: 4-Finger");
                    }
                }
                else
                {
                    FirePendingSingleTapNow();
                }

                _sessionActive = false;
                _lastContacts.Clear();
            }
        }
    }

    private void ArmPendingSingleTap((ushort X, ushort Y) pt)
    {
        lock (_syncLock)
        {
            _singleTapTimer?.Dispose();
            _pendingSingleTap = true;
            _pendingTapPosition = pt;
            _pendingTapTime = DateTime.UtcNow;

            int timeout = (int)Math.Clamp(TapDragTimeoutMs, 80.0, 500.0);
            _singleTapTimer = new System.Threading.Timer(OnSingleTapTimeout, null, timeout, Timeout.Infinite);
        }
    }

    private void OnSingleTapTimeout(object? state)
    {
        bool fire = false;
        lock (_syncLock)
        {
            if (_pendingSingleTap)
            {
                _pendingSingleTap = false;
                _singleTapTimer?.Dispose();
                _singleTapTimer = null;
                fire = true;
            }
        }

        if (fire)
        {
            EnsureInteractiveDesktop();
            GestureTriggered?.Invoke("Tap: 1-Finger (Left Click)");
            SendLeftClick();
        }
    }

    private void CancelPendingSingleTap()
    {
        lock (_syncLock)
        {
            _pendingSingleTap = false;
            _singleTapTimer?.Dispose();
            _singleTapTimer = null;
        }
    }

    private void FirePendingSingleTapNow()
    {
        bool fire = false;
        lock (_syncLock)
        {
            if (_pendingSingleTap)
            {
                _pendingSingleTap = false;
                _singleTapTimer?.Dispose();
                _singleTapTimer = null;
                fire = true;
            }
        }

        if (fire)
        {
            EnsureInteractiveDesktop();
            GestureTriggered?.Invoke("Tap: 1-Finger (Left Click)");
            SendLeftClick();
        }
    }

    public void Reset()
    {
        lock (_syncLock)
        {
            CancelPendingSingleTap();
            if (_isTapDragActive)
            {
                _isTapDragActive = false;
                SendMouseButton(leftDown: false);
            }
            if (_isPhysicalDown)
            {
                _isPhysicalDown = false;
                SendMouseButton(leftDown: false);
            }
            _sessionActive = false;
            _lastContacts.Clear();
        }
    }

    public void Dispose()
    {
        Reset();
        GC.SuppressFinalize(this);
    }

    #region Win32 Input API (mouse_event & keybd_event)
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x01000;

    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const byte VK_TAB = 0x09;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_LEFT = 0x25;
    private const byte VK_RIGHT = 0x27;
    private const byte VK_A = 0x41;
    private const byte VK_D = 0x44;
    private const byte VK_S = 0x53;
    private const byte VK_LWIN = 0x5B;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static void SendMouseMove(int dx, int dy)
    {
        mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, UIntPtr.Zero);
    }

    private static void SendMouseButton(bool leftDown)
    {
        mouse_event(leftDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static void SendLeftClick()
    {
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static void SendRightClick()
    {
        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static void SendMouseScroll(int verticalTicks)
    {
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)verticalTicks), UIntPtr.Zero);
    }

    private static void SendMouseHorizontalScroll(int horizontalTicks)
    {
        mouse_event(MOUSEEVENTF_HWHEEL, 0, 0, unchecked((uint)horizontalTicks), UIntPtr.Zero);
    }

    private static void SendKeyCombo(byte modKey, byte actionKey)
    {
        keybd_event(modKey, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(actionKey, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(actionKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(modKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SendKeyComboWithShift(byte modKey, byte actionKey)
    {
        keybd_event(modKey, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(actionKey, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(actionKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(modKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SendDesktopSwitch(bool next)
    {
        byte dir = next ? VK_RIGHT : VK_LEFT;
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(dir, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(dir, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    [ThreadStatic]
    private static bool _threadDesktopAttached;

    private static void EnsureInteractiveDesktop()
    {
        if (_threadDesktopAttached) return;
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
        finally
        {
            _threadDesktopAttached = true;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);
    #endregion
}
