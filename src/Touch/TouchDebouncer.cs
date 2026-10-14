using System.Diagnostics;
using T650Bridge.Config;

namespace T650Bridge.Touch;

/// <summary>
/// TouchDebouncer prevents contact count skew and finger arrival stagger.
/// When transitioning from 0 fingers to N fingers, fingers land sequentially over 1-4 frames.
/// The debouncer stabilizes the contact count before emitting frames to Windows PTP or the gesture engine,
/// preventing premature 1-finger or 3-finger tap actions (e.g. accidental Start menu activations)
/// before a 3-finger or 4-finger gesture begins.
/// </summary>
public sealed class TouchDebouncer : IDisposable
{
    private readonly object _sync = new();
    private readonly Stopwatch _landingTimer = new();
    private System.Threading.Timer? _flushTimer;

    private TouchFrame? _pendingLandingFrame;
    private int _maxFingersInLanding;
    private ushort _initialX;
    private ushort _initialY;
    private bool _hasInitialCoords;

    private enum State
    {
        Idle,
        LandingDebounce,
        Streaming
    }

    private State _state = State.Idle;
    private int _sessionFrameCount;

    public BridgeSettings Settings { get; set; }
    public Func<bool>? IsDoubleTapPending { get; set; }
    public event Action<TouchFrame, bool>? FrameReady; // FrameReady(frame, isLandingPhase)

    public TouchDebouncer(BridgeSettings settings)
    {
        Settings = settings;
    }

    public void ProcessFrame(TouchFrame frame)
    {
        int activeContactCount = 0;
        for (int i = 0; i < frame.Contacts.Count; i++)
        {
            if (frame.Contacts[i].IsTouching)
            {
                activeContactCount++;
            }
        }

        // If debouncing is disabled, stream directly
        if (!Settings.DebounceEnabled)
        {
            FrameReady?.Invoke(frame, false);
            return;
        }

        lock (_sync)
        {
            // Case 1: All fingers lifted -> Immediate transition to Idle
            if (activeContactCount == 0)
            {
                CancelFlushTimer();
                var pendingToFlush = (_state == State.LandingDebounce) ? _pendingLandingFrame : null;
                _state = State.Idle;
                _sessionFrameCount = 0;
                _hasInitialCoords = false;
                _pendingLandingFrame = null;
                _landingTimer.Reset();

                // If user lifted before debounce window expired (a fast tap!), emit the down frame first
                if (pendingToFlush != null)
                {
                    FrameReady?.Invoke(pendingToFlush, false);
                }

                FrameReady?.Invoke(frame, false);
                return;
            }

            // Case 2: Currently streaming actively (debouncing already passed for this touch session)
            if (_state == State.Streaming)
            {
                _sessionFrameCount++;
                bool isEarlyLanding = _sessionFrameCount <= 3;
                FrameReady?.Invoke(frame, isEarlyLanding);
                return;
            }

            // Case 3: Transition from Idle to LandingDebounce (new finger touchdown)
            if (_state == State.Idle)
            {
                // Fast-path: When waiting for a double-tap / tap-to-drag and exactly 1 finger touches down,
                // stream immediately with zero debounce delay so the double-tap window is not shortened.
                if (activeContactCount == 1 && (IsDoubleTapPending?.Invoke() == true))
                {
                    _state = State.Streaming;
                    _sessionFrameCount = 1;
                    _maxFingersInLanding = 1;
                    FrameReady?.Invoke(frame, false);
                    return;
                }

                _state = State.LandingDebounce;
                _sessionFrameCount = 1;
                _maxFingersInLanding = activeContactCount;
                _pendingLandingFrame = CloneFrame(frame);
                _landingTimer.Restart();

                if (frame.Contacts.Count > 0)
                {
                    _initialX = frame.Contacts[0].X;
                    _initialY = frame.Contacts[0].Y;
                    _hasInitialCoords = true;
                }

                ScheduleFlushTimer(Settings.LandingDebounceMs);
                return;
            }

            // Case 4: Currently in LandingDebounce window
            if (_state == State.LandingDebounce)
            {
                _sessionFrameCount++;

                // Track arrival of additional fingers during the landing window
                if (activeContactCount > _maxFingersInLanding)
                {
                    _maxFingersInLanding = activeContactCount;
                }

                _pendingLandingFrame = CloneFrame(frame);

                // Early exit 1: Check if finger movement exceeded threshold (user started swiping immediately)
                if (_hasInitialCoords && frame.Contacts.Count > 0)
                {
                    int dx = Math.Abs(frame.Contacts[0].X - _initialX);
                    int dy = Math.Abs(frame.Contacts[0].Y - _initialY);
                    if (dx > Settings.MovementDisplacementThreshold || dy > Settings.MovementDisplacementThreshold)
                    {
                        // Significant movement detected -> flush immediately with zero additional latency
                        FlushLandingSessionLocked(isMovementTriggered: true);
                        return;
                    }
                }

                // Early exit 2: Time elapsed exceeded debounce window
                if (_landingTimer.ElapsedMilliseconds >= Settings.LandingDebounceMs)
                {
                    FlushLandingSessionLocked(isMovementTriggered: false);
                    return;
                }
            }
        }
    }

    private void FlushLandingSessionLocked(bool isMovementTriggered)
    {
        CancelFlushTimer();

        if (_pendingLandingFrame == null)
        {
            _state = State.Streaming;
            return;
        }

        var frameToEmit = _pendingLandingFrame;
        _pendingLandingFrame = null;
        _state = State.Streaming;

        // Emit the stabilized touchdown frame
        // When SuppressLandingConfidence is enabled, ONLY suppress confidence if multiple fingers (2+) landed
        // Single-finger taps MUST have confidence = 1 so tap-to-click and double-tap-drag register in Windows PTP
        int contactCount = 0;
        for (int i = 0; i < frameToEmit.Contacts.Count; i++)
        {
            if (frameToEmit.Contacts[i].IsTouching) contactCount++;
        }

        bool isLandingPhase = !isMovementTriggered && Settings.SuppressLandingConfidence && contactCount > 1;
        FrameReady?.Invoke(frameToEmit, isLandingPhase);
    }

    private void ScheduleFlushTimer(int delayMs)
    {
        CancelFlushTimer();
        _flushTimer = new System.Threading.Timer(_ =>
        {
            lock (_sync)
            {
                if (_state == State.LandingDebounce)
                {
                    FlushLandingSessionLocked(isMovementTriggered: false);
                }
            }
        }, null, Math.Max(5, delayMs), System.Threading.Timeout.Infinite);
    }

    private void CancelFlushTimer()
    {
        if (_flushTimer != null)
        {
            try { _flushTimer.Dispose(); } catch { }
            _flushTimer = null;
        }
    }

    private static TouchFrame CloneFrame(TouchFrame src)
    {
        var clone = new TouchFrame
        {
            HardwareTimestamp = src.HardwareTimestamp,
            SystemTimestampMicroseconds = src.SystemTimestampMicroseconds,
            DeclaredFingerCount = src.DeclaredFingerCount,
            PhysicalButtonPressed = src.PhysicalButtonPressed
        };
        clone.Contacts.AddRange(src.Contacts);
        return clone;
    }

    public void Dispose()
    {
        CancelFlushTimer();
    }
}
