using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SpinFourKay.Core.Display;
using SpinFourKay.Core.Magpie;

namespace SpinFourKay.Core.Windows;

public sealed record OverlayWindowSnapshot(
    nint Handle,
    int ProcessId,
    string ClassName,
    string Title,
    string? ExecutablePath,
    PixelRect Bounds,
    bool IsVisible,
    bool IsMinimized,
    bool IsRootWindow,
    bool IsTopmost,
    bool IsToolWindow,
    bool IsLayered,
    bool IsInputTransparent);

public sealed record OverlayCompatibilityCaptureRequest
{
    public required nint SourceWindowHandle { get; init; }

    public required int SourceProcessId { get; init; }

    public required PixelRect SourceRegion { get; init; }

    public required PixelRect TargetRegion { get; init; }

    public IReadOnlySet<int> ExcludedProcessIds { get; init; } = new HashSet<int>();
}

public sealed record OverlayCompatibilityUpdate(
    int TrackedWindowCount,
    int MappedWindowCount,
    IReadOnlyList<string> Warnings);

public enum OverlayWindowEventKind
{
    ForegroundChanged,
    Shown,
    ZOrderChanged,
}

public sealed record OverlayWindowEvent(
    OverlayWindowEventKind Kind,
    nint WindowHandle);

public interface IOverlayWindowApi
{
    IReadOnlyList<OverlayWindowSnapshot> EnumerateTopLevelWindows();

    OverlayWindowSnapshot? Inspect(nint windowHandle);

    nint GetForegroundWindowHandle();

    bool TryGetCursorPosition(out WindowPlacementPoint position);

    nint WindowFromPoint(WindowPlacementPoint position);

    bool SetTopmostWithoutActivation(nint windowHandle, bool topmost);

    bool SetInputTransparentWithoutActivation(
        nint windowHandle,
        bool inputTransparent);

    bool MoveWithoutResizing(nint windowHandle, PixelRect bounds);

    IDisposable ObserveWindowOrderChanges(Action<OverlayWindowEvent> callback);
}

public interface IOverlayCompatibilityService
{
    OverlayCompatibilitySession Capture(OverlayCompatibilityCaptureRequest request);

    OverlayCompatibilityUpdate Activate(
        OverlayCompatibilitySession session,
        nint scalingWindowHandle,
        int scalingProcessId,
        PixelRect destinationRegion);

    OverlayCompatibilityUpdate Maintain(
        OverlayCompatibilitySession session,
        bool sourceOrScalingOutputIsForeground,
        bool discoverNewWindows = true);

    OverlayCompatibilityUpdate RefreshInputHandoff(
        OverlayCompatibilitySession session);

    OverlayCompatibilityUpdate Restore(OverlayCompatibilitySession session);
}

public sealed class OverlayCompatibilitySession
{
    private readonly List<OverlayWindowState> _windows = [];
    private readonly List<string> _warnings = [];
    private readonly HashSet<string> _warningKeys = new(StringComparer.Ordinal);
    private int _isActive;

    internal OverlayCompatibilitySession(OverlayCompatibilityCaptureRequest request)
    {
        Request = request;
        ExcludedProcessIds = request.ExcludedProcessIds.ToHashSet();
    }

    internal OverlayCompatibilityCaptureRequest Request { get; }

    internal HashSet<int> ExcludedProcessIds { get; }

    internal List<OverlayWindowState> Windows => _windows;

    internal object SyncRoot { get; } = new();

    public nint ScalingWindowHandle { get; internal set; }

    internal int ScalingProcessId { get; set; }

    internal PixelRect DestinationRegion { get; set; }

    public bool IsActive
    {
        get => Volatile.Read(ref _isActive) != 0;
        internal set => Volatile.Write(ref _isActive, value ? 1 : 0);
    }

    internal bool GameSessionIsForeground { get; set; }

    internal IDisposable? WindowOrderObserver { get; set; }

    public int CapturedWindowCount
    {
        get
        {
            lock (SyncRoot)
            {
                return _windows.Count;
            }
        }
    }

    public int MappedWindowCount
    {
        get
        {
            lock (SyncRoot)
            {
                return _windows.Count(window => window.WasMapped);
            }
        }
    }

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (SyncRoot)
            {
                return _warnings.ToArray();
            }
        }
    }

    public bool TracksWindow(nint windowHandle)
    {
        lock (SyncRoot)
        {
            return windowHandle != nint.Zero
                && _windows.Any(
                    window => window.Identity.Handle == windowHandle);
        }
    }

    internal void AddWarning(string key, string message)
    {
        lock (SyncRoot)
        {
            if (_warningKeys.Add(key))
            {
                _warnings.Add(message);
            }
        }
    }
}

internal sealed class OverlayWindowState
{
    internal OverlayWindowState(
        OverlayWindowSnapshot snapshot,
        bool allowPositionMapping)
    {
        Identity = snapshot;
        OriginalBounds = snapshot.Bounds;
        LastAppliedBounds = snapshot.Bounds;
        AllowPositionMapping = allowPositionMapping;
    }

    internal OverlayWindowSnapshot Identity { get; }

    internal PixelRect OriginalBounds { get; }

    internal PixelRect LastAppliedBounds { get; set; }

    internal bool AllowPositionMapping { get; }

    internal bool WasMapped { get; set; }

    internal bool PreserveExternalPosition { get; set; }

    internal bool IsPromotedByService { get; set; }

    internal bool IsInputTransparentByService { get; set; }

    internal long LastPromotionTimestamp { get; set; }
}

/// <summary>
/// Keeps genuine always-on-top companion windows above Magpie's focused
/// fullscreen output. Overlay pixels are never captured or resampled: only
/// native position, z-order, and a reversible pointer handoff are adjusted.
/// </summary>
public sealed class OverlayCompatibilityService : IOverlayCompatibilityService
{
    private readonly IOverlayWindowApi _windowApi;

    public OverlayCompatibilityService(IOverlayWindowApi? windowApi = null)
    {
        _windowApi = windowApi ?? new NativeOverlayWindowApi();
    }

    public OverlayCompatibilitySession Capture(
        OverlayCompatibilityCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceWindowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "The source window handle is invalid.",
                nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            request.SourceProcessId);

        OverlayPlacementPlanner.ValidateRegion(request.SourceRegion, nameof(request));
        OverlayPlacementPlanner.ValidateRegion(request.TargetRegion, nameof(request));

        OverlayCompatibilitySession session = new(request);
        try
        {
            foreach (OverlayWindowSnapshot snapshot in
                _windowApi.EnumerateTopLevelWindows())
            {
                TryCaptureWindow(session, snapshot, allowPositionMapping: true);
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
        {
            session.AddWarning(
                "capture",
                "Always-on-top companion windows could not be inventoried safely; "
                    + "their own placement was left unchanged. "
                    + exception.Message);
        }

        return session;
    }

    public OverlayCompatibilityUpdate Activate(
        OverlayCompatibilitySession session,
        nint scalingWindowHandle,
        int scalingProcessId,
        PixelRect destinationRegion)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (session.SyncRoot)
        {
            return ActivateLocked(
                session,
                scalingWindowHandle,
                scalingProcessId,
                destinationRegion);
        }
    }

    private OverlayCompatibilityUpdate ActivateLocked(
        OverlayCompatibilitySession session,
        nint scalingWindowHandle,
        int scalingProcessId,
        PixelRect destinationRegion)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (scalingWindowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "The scaling window handle is invalid.",
                nameof(scalingWindowHandle));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scalingProcessId);
        OverlayPlacementPlanner.ValidateRegion(
            destinationRegion,
            nameof(destinationRegion));

        session.ScalingWindowHandle = scalingWindowHandle;
        session.ScalingProcessId = scalingProcessId;
        session.DestinationRegion = destinationRegion;
        session.ExcludedProcessIds.Add(scalingProcessId);
        session.IsActive = true;
        session.GameSessionIsForeground = IsExactGameSessionForeground(
            session,
            _windowApi.GetForegroundWindowHandle());

        foreach (OverlayWindowState window in session.Windows)
        {
            OverlayWindowSnapshot? current = _windowApi.Inspect(window.Identity.Handle);
            if (!SameWindow(window.Identity, current))
            {
                continue;
            }

            if (window.AllowPositionMapping
                && OverlayPlacementPlanner.ShouldMapPosition(
                    session.Request.SourceRegion,
                    current!.Bounds))
            {
                PixelRect mapped = OverlayPlacementPlanner.MapNativeSize(
                    session.Request.SourceRegion,
                    destinationRegion,
                    current.Bounds);
                if (mapped != current.Bounds)
                {
                    if (_windowApi.MoveWithoutResizing(current.Handle, mapped))
                    {
                        window.LastAppliedBounds = mapped;
                        window.WasMapped = true;
                    }
                    else
                    {
                        session.AddWarning(
                            $"move:{current.Handle}",
                            "A companion overlay kept its original position because "
                                + "Windows rejected a no-resize placement request.");
                    }
                }
            }

            if (session.GameSessionIsForeground)
            {
                Promote(session, window);
            }
            else
            {
                RestoreInputTransparency(session, window);
                Demote(session, window);
            }
        }

        try
        {
            session.WindowOrderObserver = _windowApi.ObserveWindowOrderChanges(
                windowEvent => HandleWindowEvent(session, windowEvent));
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
        {
            session.AddWarning(
                "z-order-observer",
                "Immediate companion-overlay recovery is unavailable, so the "
                    + "regular compatibility check will be used instead. "
                    + exception.Message);
        }

        return RefreshInputHandoffLocked(session);
    }

    public OverlayCompatibilityUpdate Maintain(
        OverlayCompatibilitySession session,
        bool sourceOrScalingOutputIsForeground,
        bool discoverNewWindows = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (session.SyncRoot)
        {
            return MaintainLocked(
                session,
                sourceOrScalingOutputIsForeground,
                discoverNewWindows);
        }
    }

    private OverlayCompatibilityUpdate MaintainLocked(
        OverlayCompatibilitySession session,
        bool sourceOrScalingOutputIsForeground,
        bool discoverNewWindows = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        nint foregroundWindowHandle = _windowApi.GetForegroundWindowHandle();
        bool exactGameSessionIsForeground = sourceOrScalingOutputIsForeground
            && IsExactGameSessionForeground(
                session,
                foregroundWindowHandle);
        bool trackedOverlayIsForeground = IsTrackedOverlayWindow(
            session,
            foregroundWindowHandle);
        session.GameSessionIsForeground = exactGameSessionIsForeground;
        if (!session.IsActive)
        {
            return CreateUpdate(session);
        }

        if (trackedOverlayIsForeground)
        {
            // Interactive companion overlays can take focus. Keep the protected
            // z-order while restoring their normal mouse input; otherwise an
            // unlocked Electron overlay falls behind Magpie as soon as it is
            // clicked.
            foreach (OverlayWindowState window in session.Windows)
            {
                RestoreInputTransparency(session, window);
                OverlayWindowSnapshot? current = _windowApi.Inspect(
                    window.Identity.Handle);
                if (SameWindow(window.Identity, current) && !current!.IsTopmost)
                {
                    Promote(session, window);
                }
            }

            return CreateUpdate(session);
        }

        if (!exactGameSessionIsForeground)
        {
            foreach (OverlayWindowState window in session.Windows)
            {
                RestoreInputTransparency(session, window);
                Demote(session, window);
            }

            return CreateUpdate(session);
        }

        try
        {
            if (discoverNewWindows)
            {
                foreach (OverlayWindowSnapshot snapshot in
                    _windowApi.EnumerateTopLevelWindows())
                {
                    TryCaptureWindow(
                        session,
                        snapshot,
                        allowPositionMapping: false);
                }
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
        {
            session.AddWarning(
                "runtime-capture",
                "New companion overlays could not be inventoried during this "
                    + "session. Existing protected overlays remain active. "
                    + exception.Message);
        }

        foreach (OverlayWindowState window in session.Windows)
        {
            OverlayWindowSnapshot? current = _windowApi.Inspect(window.Identity.Handle);
            if (!SameWindow(window.Identity, current))
            {
                continue;
            }

            if (window.WasMapped
                && !window.PreserveExternalPosition
                && current!.Bounds != window.LastAppliedBounds)
            {
                // The companion application or the player moved/resized the window
                // after our one-time mapping. Adopt that newer placement and never
                // overwrite it during cleanup.
                window.LastAppliedBounds = current.Bounds;
                window.PreserveExternalPosition = true;
            }

            Promote(session, window);
        }

        return RefreshInputHandoffLocked(session);
    }

    public OverlayCompatibilityUpdate RefreshInputHandoff(
        OverlayCompatibilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (session.SyncRoot)
        {
            return RefreshInputHandoffLocked(session);
        }
    }

    private OverlayCompatibilityUpdate RefreshInputHandoffLocked(
        OverlayCompatibilitySession session)
    {
        if (!session.IsActive)
        {
            return CreateUpdate(session);
        }

        bool exactGameSessionIsForeground = IsExactGameSessionForeground(
            session,
            _windowApi.GetForegroundWindowHandle());
        session.GameSessionIsForeground = exactGameSessionIsForeground;
        if (!exactGameSessionIsForeground)
        {
            foreach (OverlayWindowState window in session.Windows)
            {
                RestoreInputTransparency(session, window);
            }

            return CreateUpdate(session);
        }

        if (!_windowApi.TryGetCursorPosition(out WindowPlacementPoint rawPointer))
        {
            foreach (OverlayWindowState window in session.Windows)
            {
                RestoreInputTransparency(session, window);
            }

            session.AddWarning(
                "cursor-position",
                "Windows did not expose the current pointer position, so companion "
                    + "overlays were left clickable for safety.");
            return CreateUpdate(session);
        }

        nint windowAtRawPointer = OverlayPlacementPlanner.Contains(
            session.Request.SourceRegion,
            rawPointer)
            ? _windowApi.WindowFromPoint(rawPointer)
            : nint.Zero;
        WindowPlacementPoint effectivePointer =
            OverlayPlacementPlanner.ResolveEffectivePointer(
                rawPointer,
                session.Request.SourceRegion,
                session.DestinationRegion,
                session.ScalingWindowHandle,
                windowAtRawPointer);
        foreach (OverlayWindowState window in session.Windows)
        {
            UpdateInputHandoff(session, window, effectivePointer);
        }

        return CreateUpdate(session);
    }

    public OverlayCompatibilityUpdate Restore(OverlayCompatibilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        IDisposable? observer;
        lock (session.SyncRoot)
        {
            if (!session.IsActive)
            {
                return CreateUpdate(session);
            }

            session.GameSessionIsForeground = false;
            observer = session.WindowOrderObserver;
            session.WindowOrderObserver = null;
        }

        observer?.Dispose();
        lock (session.SyncRoot)
        {
            return RestoreLocked(session);
        }
    }

    private OverlayCompatibilityUpdate RestoreLocked(
        OverlayCompatibilitySession session)
    {
        foreach (OverlayWindowState window in session.Windows)
        {
            RestoreInputTransparency(session, window);
        }

        foreach (OverlayWindowState window in session.Windows)
        {
            if (!window.WasMapped || window.PreserveExternalPosition)
            {
                continue;
            }

            OverlayWindowSnapshot? current = _windowApi.Inspect(window.Identity.Handle);
            if (!SameWindow(window.Identity, current)
                || current!.Bounds != window.LastAppliedBounds)
            {
                continue;
            }

            if (!_windowApi.MoveWithoutResizing(
                window.Identity.Handle,
                window.OriginalBounds))
            {
                session.AddWarning(
                    $"restore:{window.Identity.Handle}",
                    "A companion overlay could not be returned to its exact "
                        + "pre-scaling position. Its current placement was preserved.");
            }
        }

        foreach (OverlayWindowState window in session.Windows)
        {
            OverlayWindowSnapshot? current = _windowApi.Inspect(window.Identity.Handle);
            if (SameWindow(window.Identity, current)
                && !_windowApi.SetTopmostWithoutActivation(
                    window.Identity.Handle,
                    window.Identity.IsTopmost))
            {
                session.AddWarning(
                    $"restore-z:{window.Identity.Handle}",
                    "A companion overlay's original topmost state could not be "
                        + "restored. Its own window policy remains in control.");
            }

            window.IsPromotedByService = false;
        }

        session.IsActive = false;
        return CreateUpdate(session);
    }

    private static bool TryCaptureWindow(
        OverlayCompatibilitySession session,
        OverlayWindowSnapshot snapshot,
        bool allowPositionMapping)
    {
        OverlayWindowState? existing = session.Windows.FirstOrDefault(
            window => window.Identity.Handle == snapshot.Handle);
        bool alreadyTracked = existing is not null
            && SameWindow(existing.Identity, snapshot);
        if (existing is not null && !alreadyTracked)
        {
            // HWND values are recyclable. Forget stale identity without touching
            // the replacement window, then evaluate the new snapshot normally.
            session.Windows.Remove(existing);
        }

        if (alreadyTracked
            && OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(snapshot)
            && (!snapshot.IsVisible || snapshot.IsMinimized))
        {
            session.AddWarning(
                "eq-legends-companion-hidden",
                "EQ Legends Companion has hidden or minimized one of its "
                    + "overlays. SpinFOURKAYYY will not force-open another "
                    + "app's hidden window. If that overlay should be visible, "
                    + "restore it and turn off EQ Legends Companion's 'Hide "
                    + "when unfocused' option.");
        }

        bool allowTemporaryNonTopmost =
            OverlayPlacementPlanner.IsRecognizedCompanion(snapshot);
        if (alreadyTracked
            || !OverlayPlacementPlanner.IsEligible(
                snapshot,
                session.Request.TargetRegion,
                session.ExcludedProcessIds,
                session.ScalingWindowHandle,
                allowTemporaryNonTopmost))
        {
            return false;
        }

        session.Windows.Add(new OverlayWindowState(snapshot, allowPositionMapping));
        return true;
    }

    private void Promote(
        OverlayCompatibilitySession session,
        OverlayWindowState window)
    {
        OverlayWindowSnapshot? current = _windowApi.Inspect(window.Identity.Handle);
        if (!SameWindow(window.Identity, current))
        {
            window.IsPromotedByService = false;
            window.IsInputTransparentByService = false;
            return;
        }

        if (!_windowApi.SetTopmostWithoutActivation(
            window.Identity.Handle,
            topmost: true))
        {
            session.AddWarning(
                $"topmost:{window.Identity.Handle}",
                "Windows did not allow one companion overlay to stay above the "
                    + "scaled game. That overlay retained its own normal behavior.");
            return;
        }

        window.IsPromotedByService = true;
        window.LastPromotionTimestamp = Stopwatch.GetTimestamp();
    }

    private void HandleWindowEvent(
        OverlayCompatibilitySession session,
        OverlayWindowEvent windowEvent)
    {
        if (windowEvent.WindowHandle == nint.Zero)
        {
            return;
        }

        lock (session.SyncRoot)
        {
            if (!session.IsActive)
            {
                return;
            }

            OverlayWindowState? changedWindow = session.Windows.FirstOrDefault(
                window => window.Identity.Handle == windowEvent.WindowHandle);
            try
            {
                if (changedWindow is not null
                    && !SameWindow(
                        changedWindow.Identity,
                        _windowApi.Inspect(windowEvent.WindowHandle)))
                {
                    session.Windows.Remove(changedWindow);
                    changedWindow = null;
                }

                if (changedWindow is null)
                {
                    OverlayWindowSnapshot? snapshot =
                        _windowApi.Inspect(windowEvent.WindowHandle);
                    if (snapshot is not null
                        && TryCaptureWindow(
                            session,
                            snapshot,
                            allowPositionMapping: false))
                    {
                        changedWindow = session.Windows.First(
                            window =>
                                window.Identity.Handle == windowEvent.WindowHandle);
                    }
                }
            }
            catch (Exception exception)
            {
                session.AddWarning(
                    "event-capture",
                    "A newly opened companion overlay could not be inspected "
                        + "immediately. The regular compatibility check will "
                        + "try again. "
                        + exception.Message);
            }

            if (windowEvent.Kind == OverlayWindowEventKind.ForegroundChanged)
            {
                bool gameSessionIsForeground = IsExactGameSessionForeground(
                    session,
                    windowEvent.WindowHandle);
                bool trackedOverlayIsForeground = changedWindow is not null
                    && SameWindow(
                        changedWindow.Identity,
                        _windowApi.Inspect(changedWindow.Identity.Handle));
                session.GameSessionIsForeground = gameSessionIsForeground;
                foreach (OverlayWindowState window in session.Windows)
                {
                    if (gameSessionIsForeground)
                    {
                        Promote(session, window);
                    }
                    else if (trackedOverlayIsForeground)
                    {
                        RestoreInputTransparency(session, window);
                        OverlayWindowSnapshot? overlayCurrent = _windowApi.Inspect(
                            window.Identity.Handle);
                        if (SameWindow(window.Identity, overlayCurrent)
                            && !overlayCurrent!.IsTopmost)
                        {
                            Promote(session, window);
                        }
                    }
                    else
                    {
                        RestoreInputTransparency(session, window);
                        Demote(session, window);
                    }
                }

                if (gameSessionIsForeground)
                {
                    _ = RefreshInputHandoffLocked(session);
                }

                return;
            }

            if (!session.GameSessionIsForeground)
            {
                return;
            }

            if (windowEvent.WindowHandle == session.ScalingWindowHandle)
            {
                foreach (OverlayWindowState window in session.Windows)
                {
                    Promote(session, window);
                }

                _ = RefreshInputHandoffLocked(session);

                return;
            }

            if (changedWindow is null)
            {
                return;
            }

            OverlayWindowSnapshot? current = _windowApi.Inspect(
                changedWindow.Identity.Handle);
            if (!SameWindow(changedWindow.Identity, current))
            {
                return;
            }

            bool recentlyPromoted = current!.IsTopmost
                && Stopwatch.GetElapsedTime(
                    changedWindow.LastPromotionTimestamp) <
                    TimeSpan.FromMilliseconds(100);
            if (!recentlyPromoted)
            {
                Promote(session, changedWindow);
            }

            _ = RefreshInputHandoffLocked(session);
        }
    }

    private static bool IsExactGameSessionForeground(
        OverlayCompatibilitySession session,
        nint windowHandle) =>
        windowHandle != nint.Zero
        && (windowHandle == session.Request.SourceWindowHandle
            || windowHandle == session.ScalingWindowHandle);

    private bool IsTrackedOverlayWindow(
        OverlayCompatibilitySession session,
        nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return false;
        }

        OverlayWindowState? tracked = session.Windows.FirstOrDefault(
            window => window.Identity.Handle == windowHandle);
        return tracked is not null
            && SameWindow(tracked.Identity, _windowApi.Inspect(windowHandle));
    }

    private void UpdateInputHandoff(
        OverlayCompatibilitySession session,
        OverlayWindowState window,
        WindowPlacementPoint effectivePointer)
    {
        if (OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(window.Identity))
        {
            // Companion dynamically owns WS_EX_TRANSPARENT through Electron's
            // lock/click-through control. Never race or overwrite that choice.
            return;
        }

        if (window.Identity.IsInputTransparent)
        {
            return;
        }

        OverlayWindowSnapshot? current = _windowApi.Inspect(window.Identity.Handle);
        if (!SameWindow(window.Identity, current))
        {
            window.IsInputTransparentByService = false;
            return;
        }

        bool shadowsSource = current!.IsVisible
            && !current.IsMinimized
            && OverlayPlacementPlanner.Intersects(
                current.Bounds,
                session.Request.SourceRegion);
        bool pointerIsOverVisibleOverlay = OverlayPlacementPlanner.Contains(
            current.Bounds,
            effectivePointer);
        if (!shadowsSource || pointerIsOverVisibleOverlay)
        {
            RestoreInputTransparency(session, window);
            return;
        }

        if (current.IsInputTransparent)
        {
            // Respect click-through behavior applied by the companion itself. Only
            // cleanup a style change that this exact session successfully made.
            return;
        }

        bool accepted = _windowApi.SetInputTransparentWithoutActivation(
            window.Identity.Handle,
            inputTransparent: true);
        OverlayWindowSnapshot? updated = _windowApi.Inspect(window.Identity.Handle);
        bool applied = SameWindow(window.Identity, updated)
            && updated!.IsInputTransparent;
        window.IsInputTransparentByService = applied;
        if (!accepted || !applied)
        {
            session.AddWarning(
                $"input-pass-through:{window.Identity.Handle}",
                "Windows could not make one hidden companion-overlay shadow "
                    + "temporarily click-through. The visible overlay stayed "
                    + "clickable, but it may cover part of the scaled game.");
        }
    }

    private void RestoreInputTransparency(
        OverlayCompatibilitySession session,
        OverlayWindowState window)
    {
        if (!window.IsInputTransparentByService)
        {
            return;
        }

        OverlayWindowSnapshot? current = _windowApi.Inspect(window.Identity.Handle);
        if (!SameWindow(window.Identity, current))
        {
            window.IsInputTransparentByService = false;
            return;
        }

        if (!current!.IsInputTransparent)
        {
            window.IsInputTransparentByService = false;
            return;
        }

        bool accepted = _windowApi.SetInputTransparentWithoutActivation(
            window.Identity.Handle,
            inputTransparent: false);
        OverlayWindowSnapshot? updated = _windowApi.Inspect(window.Identity.Handle);
        bool restored = SameWindow(window.Identity, updated)
            && !updated!.IsInputTransparent;
        if (restored)
        {
            window.IsInputTransparentByService = false;
        }

        if (!accepted || !restored)
        {
            session.AddWarning(
                $"restore-input:{window.Identity.Handle}",
                "A companion overlay could not have its original mouse-input "
                    + "behavior restored. Stop scaling or restart that overlay "
                    + "before interacting with it.");
        }
    }

    private void Demote(
        OverlayCompatibilitySession session,
        OverlayWindowState window)
    {
        if (!window.IsPromotedByService)
        {
            return;
        }

        OverlayWindowSnapshot? current = _windowApi.Inspect(window.Identity.Handle);
        if (!SameWindow(window.Identity, current))
        {
            window.IsPromotedByService = false;
            return;
        }

        if (!_windowApi.SetTopmostWithoutActivation(
            window.Identity.Handle,
            topmost: false))
        {
            session.AddWarning(
                $"background-z:{window.Identity.Handle}",
                "A companion overlay could not be lowered while another app was "
                    + "foreground. Its own window policy remains in control.");
            return;
        }

        window.IsPromotedByService = false;
    }

    private static bool SameWindow(
        OverlayWindowSnapshot expected,
        OverlayWindowSnapshot? current)
    {
        if (current is null
            || current.Handle != expected.Handle
            || current.ProcessId != expected.ProcessId
            || !string.Equals(
                current.ClassName,
                expected.ClassName,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (expected.ExecutablePath is null || current.ExecutablePath is null)
        {
            return true;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(current.ExecutablePath),
                Path.GetFullPath(expected.ExecutablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private static OverlayCompatibilityUpdate CreateUpdate(
        OverlayCompatibilitySession session) =>
        new(
            session.CapturedWindowCount,
            session.MappedWindowCount,
            session.Warnings);
}

public static class OverlayPlacementPlanner
{
    private const double MaximumOverlayAreaFraction = 0.45;

    private static readonly HashSet<string> EqLegendsCompanionExecutables = new(
        [
            "EQ Legends Companion.exe",
            "everquest-companion.exe",
            "eq-tools.exe",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> EqLegendsCompanionOverlayTitles = new(
        [
            "Buff Timer Overlay",
            "Celebration Overlay",
            "Debuff Timer Overlay",
            "Event Log Overlay",
            "Fight Healing Overlay",
            "Fight Overlay",
            "Respawn Timer Overlay",
            "XP Overlay",
            "Zone Healing Overlay",
            "Zone Overlay",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ExcludedClasses = new(
        [
            "#32768",
            "IME",
            "MSCTFIME UI",
            "NotifyIconOverflowWindow",
            "Progman",
            "Shell_SecondaryTrayWnd",
            "Shell_TrayWnd",
            "tooltips_class32",
            "WorkerW",
            "Windows.UI.Core.CoreWindow",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ExcludedProcesses = new(
        [
            "ApplicationFrameHost.exe",
            "dwm.exe",
            "explorer.exe",
            "LockApp.exe",
            "SearchHost.exe",
            "SecurityHealthSystray.exe",
            "ShellExperienceHost.exe",
            "StartMenuExperienceHost.exe",
            "SystemSettings.exe",
            "Taskmgr.exe",
            "TextInputHost.exe",
            "Widgets.exe",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsEligible(
        OverlayWindowSnapshot snapshot,
        PixelRect targetRegion,
        IReadOnlySet<int> excludedProcessIds,
        nint scalingWindowHandle = default,
        bool allowTemporaryNonTopmost = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(excludedProcessIds);
        ValidateRegion(targetRegion, nameof(targetRegion));

        if (snapshot.Handle == nint.Zero
            || snapshot.Handle == scalingWindowHandle
            || snapshot.ProcessId <= 0
            || excludedProcessIds.Contains(snapshot.ProcessId)
            || !snapshot.IsVisible
            || snapshot.IsMinimized
            || !snapshot.IsRootWindow
            || (!snapshot.IsTopmost && !allowTemporaryNonTopmost)
            || snapshot.Bounds.Width <= 0
            || snapshot.Bounds.Height <= 0
            || snapshot.Bounds.Width > targetRegion.Width
            || snapshot.Bounds.Height > targetRegion.Height
            || ExcludedClasses.Contains(snapshot.ClassName)
            || !Intersects(snapshot.Bounds, targetRegion))
        {
            return false;
        }

        // EQ Legends Companion's dashboard shares its executable and process ID
        // with the HUDs. Only its Electron toolbar windows are overlays; never
        // promote the normal dashboard even if another utility makes it topmost.
        if (IsEqLegendsCompanionProcess(snapshot) && !snapshot.IsToolWindow)
        {
            return false;
        }

        long overlayArea = (long)snapshot.Bounds.Width * snapshot.Bounds.Height;
        long targetArea = (long)targetRegion.Width * targetRegion.Height;
        if (overlayArea > targetArea * MaximumOverlayAreaFraction)
        {
            return false;
        }

        string? processName = TryGetExecutableName(snapshot.ExecutablePath);
        if (processName is not null && ExcludedProcesses.Contains(processName))
        {
            return false;
        }

        string windowsDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        return snapshot.ExecutablePath is null
            || windowsDirectory.Length == 0
            || !IsUnderDirectory(snapshot.ExecutablePath, windowsDirectory);
    }

    public static bool IsRecognizedCompanion(OverlayWindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (IsEqLegendsCompanionOverlay(snapshot))
        {
            return true;
        }

        string identity = string.Join(
            ' ',
            snapshot.Title,
            snapshot.ClassName,
            snapshot.ExecutablePath is null
                ? string.Empty
                : Path.GetFileNameWithoutExtension(snapshot.ExecutablePath));
        string[] companionTerms =
        [
            "combat tracker",
            "companion hud",
            "dps meter",
            "eqbuddy",
            "eqlogparser",
            "loremaster",
        ];
        return companionTerms.Any(
            term => identity.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Identifies only EQ Legends Companion's floating Electron toolbar windows.
    /// Its full dashboard uses the same process and executable but is not a tool
    /// window, so a process-name-only match would incorrectly promote the app UI.
    /// </summary>
    public static bool IsEqLegendsCompanionOverlay(
        OverlayWindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.IsToolWindow
            && IsEqLegendsCompanionProcess(snapshot)
            && EqLegendsCompanionOverlayTitles.Contains(snapshot.Title);
    }

    private static bool IsEqLegendsCompanionProcess(
        OverlayWindowSnapshot snapshot)
    {
        string? executableName = TryGetExecutableName(snapshot.ExecutablePath);
        return executableName is not null
            && EqLegendsCompanionExecutables.Contains(executableName);
    }

    private static string? TryGetExecutableName(string? executablePath)
    {
        if (executablePath is null)
        {
            return null;
        }

        try
        {
            return Path.GetFileName(executablePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return null;
        }
    }

    public static bool ShouldMapPosition(PixelRect sourceRegion, PixelRect overlayBounds)
    {
        ValidateRegion(sourceRegion, nameof(sourceRegion));
        ValidateRegion(overlayBounds, nameof(overlayBounds));

        long intersectionArea = IntersectionArea(sourceRegion, overlayBounds);
        long overlayArea = (long)overlayBounds.Width * overlayBounds.Height;
        long centerXTwice = (long)overlayBounds.X * 2 + overlayBounds.Width;
        long centerYTwice = (long)overlayBounds.Y * 2 + overlayBounds.Height;
        bool centerInside =
            centerXTwice >= (long)sourceRegion.X * 2
            && centerXTwice <= ((long)sourceRegion.X + sourceRegion.Width) * 2
            && centerYTwice >= (long)sourceRegion.Y * 2
            && centerYTwice <= ((long)sourceRegion.Y + sourceRegion.Height) * 2;

        return centerInside || intersectionArea * 4 >= overlayArea;
    }

    public static PixelRect MapNativeSize(
        PixelRect sourceRegion,
        PixelRect destinationRegion,
        PixelRect overlayBounds)
    {
        ValidateRegion(sourceRegion, nameof(sourceRegion));
        ValidateRegion(destinationRegion, nameof(destinationRegion));
        ValidateRegion(overlayBounds, nameof(overlayBounds));

        int x = MapAxis(
            sourceRegion.X,
            sourceRegion.Width,
            destinationRegion.X,
            destinationRegion.Width,
            overlayBounds.X,
            overlayBounds.Width);
        int y = MapAxis(
            sourceRegion.Y,
            sourceRegion.Height,
            destinationRegion.Y,
            destinationRegion.Height,
            overlayBounds.Y,
            overlayBounds.Height);
        return new PixelRect(x, y, overlayBounds.Width, overlayBounds.Height);
    }

    public static void ValidateRegion(PixelRect region, string parameterName)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The physical pixel region must have a positive size.");
        }
    }

    private static int MapAxis(
        int sourceOrigin,
        int sourceLength,
        int destinationOrigin,
        int destinationLength,
        int overlayOrigin,
        int overlayLength)
    {
        double scale = (double)destinationLength / sourceLength;
        double overlayCenter = overlayOrigin + overlayLength / 2.0;
        double relativeCenter = (overlayCenter - sourceOrigin) / sourceLength;
        double mapped;
        if (relativeCenter <= 1.0 / 3.0)
        {
            mapped = destinationOrigin
                + (overlayOrigin - sourceOrigin) * scale;
        }
        else if (relativeCenter >= 2.0 / 3.0)
        {
            double sourceFarEdge = (double)sourceOrigin + sourceLength;
            double destinationFarEdge =
                (double)destinationOrigin + destinationLength;
            double farGap = sourceFarEdge - (overlayOrigin + overlayLength);
            mapped = destinationFarEdge - farGap * scale - overlayLength;
        }
        else
        {
            double sourceCenter = sourceOrigin + sourceLength / 2.0;
            double destinationCenter =
                destinationOrigin + destinationLength / 2.0;
            mapped = destinationCenter
                + (overlayCenter - sourceCenter) * scale
                - overlayLength / 2.0;
        }

        int rounded = checked((int)Math.Round(
            mapped,
            MidpointRounding.AwayFromZero));
        int maximum = checked(
            destinationOrigin + Math.Max(0, destinationLength - overlayLength));
        return Math.Clamp(rounded, destinationOrigin, maximum);
    }

    private static long IntersectionArea(PixelRect left, PixelRect right)
    {
        long intersectionWidth = Math.Max(
            0L,
            Math.Min(
                (long)left.X + left.Width,
                (long)right.X + right.Width)
                - Math.Max(left.X, right.X));
        long intersectionHeight = Math.Max(
            0L,
            Math.Min(
                (long)left.Y + left.Height,
                (long)right.Y + right.Height)
                - Math.Max(left.Y, right.Y));
        return intersectionWidth * intersectionHeight;
    }

    public static bool Intersects(PixelRect left, PixelRect right) =>
        IntersectionArea(left, right) > 0;

    public static bool Contains(
        PixelRect bounds,
        WindowPlacementPoint point) =>
        point.X >= bounds.X
        && point.X < (long)bounds.X + bounds.Width
        && point.Y >= bounds.Y
        && point.Y < (long)bounds.Y + bounds.Height;

    public static WindowPlacementPoint ResolveEffectivePointer(
        WindowPlacementPoint rawPointer,
        PixelRect sourceRegion,
        PixelRect destinationRegion,
        nint scalingWindowHandle,
        nint windowAtRawPointer)
    {
        ValidateRegion(sourceRegion, nameof(sourceRegion));
        ValidateRegion(destinationRegion, nameof(destinationRegion));
        if (!Contains(sourceRegion, rawPointer)
            || windowAtRawPointer == scalingWindowHandle)
        {
            return rawPointer;
        }

        PixelPoint mapped = CoordinateMappingValidator.SourceToDestination(
            new PixelPoint(rawPointer.X, rawPointer.Y),
            sourceRegion,
            destinationRegion);
        return new WindowPlacementPoint(
            checked((int)mapped.X),
            checked((int)mapped.Y));
    }

    private static bool IsUnderDirectory(string filePath, string directory)
    {
        try
        {
            string fullPath = Path.GetFullPath(filePath);
            string fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(
                fullDirectory,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return true;
        }
    }
}

internal sealed class NativeOverlayWindowApi : IOverlayWindowApi
{
    public IReadOnlyList<OverlayWindowSnapshot> EnumerateTopLevelWindows()
    {
        List<OverlayWindowSnapshot> windows = [];
        NativeMethods.EnumWindowsCallback callback = (windowHandle, parameter) =>
        {
            _ = parameter;
            OverlayWindowSnapshot? snapshot = Inspect(windowHandle);
            if (snapshot is not null)
            {
                windows.Add(snapshot);
            }

            return true;
        };

        if (!NativeMethods.EnumWindows(callback, nint.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                throw new Win32Exception(error);
            }
        }

        return windows;
    }

    public OverlayWindowSnapshot? Inspect(nint windowHandle)
    {
        if (windowHandle == nint.Zero
            || !NativeMethods.IsWindow(windowHandle)
            || !NativeMethods.GetWindowRect(
                windowHandle,
                out NativeMethods.NativeRect nativeBounds))
        {
            return null;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(
            windowHandle,
            out uint processIdValue);
        if (threadId == 0
            || processIdValue == 0
            || processIdValue > int.MaxValue)
        {
            return null;
        }

        int processId = checked((int)processIdValue);
        uint extendedStyle = unchecked(
            (uint)NativeMethods.GetWindowLongPtr(
                windowHandle,
                NativeMethods.GwlExStyle).ToInt64());
        return new OverlayWindowSnapshot(
            windowHandle,
            processId,
            ReadClassName(windowHandle),
            ReadWindowTitle(windowHandle),
            ProcessDiscoveryService.TryGetExecutablePath(processId),
            new PixelRect(
                nativeBounds.Left,
                nativeBounds.Top,
                Math.Max(0, nativeBounds.Right - nativeBounds.Left),
                Math.Max(0, nativeBounds.Bottom - nativeBounds.Top)),
            NativeMethods.IsWindowVisible(windowHandle),
            NativeMethods.IsIconic(windowHandle),
            NativeMethods.GetAncestor(windowHandle, NativeMethods.GaRoot)
                == windowHandle,
            (extendedStyle & NativeMethods.WsExTopmost) != 0,
            (extendedStyle & NativeMethods.WsExToolWindow) != 0,
            (extendedStyle & NativeMethods.WsExLayered) != 0,
            (extendedStyle & NativeMethods.WsExTransparent) != 0);
    }

    public nint GetForegroundWindowHandle() => NativeMethods.GetForegroundWindow();

    public bool TryGetCursorPosition(out WindowPlacementPoint position)
    {
        if (NativeMethods.GetCursorPos(out NativeMethods.NativePoint nativePoint))
        {
            position = new WindowPlacementPoint(nativePoint.X, nativePoint.Y);
            return true;
        }

        position = default;
        return false;
    }

    public nint WindowFromPoint(WindowPlacementPoint position) =>
        NativeMethods.WindowFromPoint(
            new NativeMethods.NativePoint
            {
                X = position.X,
                Y = position.Y,
            });

    public bool SetTopmostWithoutActivation(nint windowHandle, bool topmost) =>
        NativeMethods.SetWindowPos(
            windowHandle,
            topmost
                ? NativeMethods.HwndTopmost
                : NativeMethods.HwndNotTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove
                | NativeMethods.SwpNoSize
                | NativeMethods.SwpNoActivate
                | NativeMethods.SwpNoOwnerZOrder);

    public bool SetInputTransparentWithoutActivation(
        nint windowHandle,
        bool inputTransparent)
    {
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        nint currentValue = NativeMethods.GetWindowLongPtr(
            windowHandle,
            NativeMethods.GwlExStyle);
        if (currentValue == nint.Zero && Marshal.GetLastPInvokeError() != 0)
        {
            return false;
        }

        uint currentStyle = unchecked((uint)currentValue.ToInt64());
        uint updatedStyle = inputTransparent
            ? currentStyle | NativeMethods.WsExTransparent
            : currentStyle & ~NativeMethods.WsExTransparent;
        if (updatedStyle == currentStyle)
        {
            return true;
        }

        nint updatedValue = nint.Size == sizeof(long)
            ? new nint((long)updatedStyle)
            : new nint(unchecked((int)updatedStyle));
        Marshal.SetLastPInvokeError(0);
        nint previousValue = NativeMethods.SetWindowLongPtr(
            windowHandle,
            NativeMethods.GwlExStyle,
            updatedValue);
        if (previousValue == nint.Zero && Marshal.GetLastPInvokeError() != 0)
        {
            return false;
        }

        // SetWindowLongPtr changes only the requested style bit and never activates,
        // moves, resizes, or changes the z-order of the companion window.
        return true;
    }

    public bool MoveWithoutResizing(nint windowHandle, PixelRect bounds) =>
        NativeMethods.SetWindowPos(
            windowHandle,
            nint.Zero,
            bounds.X,
            bounds.Y,
            0,
            0,
            NativeMethods.SwpNoSize
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate
                | NativeMethods.SwpNoOwnerZOrder);

    public IDisposable ObserveWindowOrderChanges(
        Action<OverlayWindowEvent> callback) =>
        new NativeOverlayWindowOrderObserver(callback);

    private static string ReadClassName(nint windowHandle)
    {
        char[] buffer = new char[256];
        int length = NativeMethods.GetClassNameW(
            windowHandle,
            buffer,
            buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static string ReadWindowTitle(nint windowHandle)
    {
        int length = NativeMethods.GetWindowTextLengthW(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[length + 1];
        int copied = NativeMethods.GetWindowTextW(
            windowHandle,
            buffer,
            buffer.Length);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
    }
}

internal sealed class NativeOverlayWindowOrderObserver : IDisposable
{
    private readonly Action<OverlayWindowEvent> _callback;
    private readonly NativeMethods.WinEventCallback _nativeCallback;
    private readonly ManualResetEventSlim _started = new(initialState: false);
    private readonly Thread _observerThread;
    private Exception? _startupFailure;
    private uint _observerThreadId;
    private nint _foregroundHook;
    private nint _showHook;
    private nint _zOrderHook;
    private int _disposed;

    internal NativeOverlayWindowOrderObserver(
        Action<OverlayWindowEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        _nativeCallback = OnWindowEvent;
        _observerThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "SpinFOURKAYYY overlay z-order observer",
        };
        _observerThread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(5)))
        {
            Dispose();
            throw new InvalidOperationException(
                "The companion-overlay z-order observer did not initialize in time.");
        }

        if (_startupFailure is not null)
        {
            Dispose();
            throw new InvalidOperationException(
                "Windows could not start companion-overlay z-order monitoring.",
                _startupFailure);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        uint observerThreadId = _observerThreadId;
        if (observerThreadId != 0)
        {
            _ = NativeMethods.PostThreadMessageW(
                observerThreadId,
                NativeMethods.WmQuit,
                nint.Zero,
                nint.Zero);
        }

        bool observerStopped = false;
        if (Environment.CurrentManagedThreadId != _observerThread.ManagedThreadId)
        {
            observerStopped = _observerThread.Join(TimeSpan.FromSeconds(2));
        }

        if (observerStopped)
        {
            _started.Dispose();
        }
    }

    private void RunMessageLoop()
    {
        try
        {
            _observerThreadId = NativeMethods.GetCurrentThreadId();
            _foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EventSystemForeground,
                NativeMethods.EventSystemForeground,
                nint.Zero,
                _nativeCallback,
                0,
                0,
                NativeMethods.WineventOutOfContext);
            _showHook = NativeMethods.SetWinEventHook(
                NativeMethods.EventObjectShow,
                NativeMethods.EventObjectShow,
                nint.Zero,
                _nativeCallback,
                0,
                0,
                NativeMethods.WineventOutOfContext);
            _zOrderHook = NativeMethods.SetWinEventHook(
                NativeMethods.EventObjectReorder,
                NativeMethods.EventObjectReorder,
                nint.Zero,
                _nativeCallback,
                0,
                0,
                NativeMethods.WineventOutOfContext);
            if (_foregroundHook == nint.Zero
                || _showHook == nint.Zero
                || _zOrderHook == nint.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows rejected a native window-event hook.");
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            _started.Set();
            while (NativeMethods.GetMessageW(
                out NativeMethods.NativeMessage message,
                nint.Zero,
                0,
                0) > 0)
            {
                _ = message;
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
        {
            _startupFailure = exception;
            _started.Set();
        }
        finally
        {
            if (_foregroundHook != nint.Zero)
            {
                _ = NativeMethods.UnhookWinEvent(_foregroundHook);
                _foregroundHook = nint.Zero;
            }

            if (_zOrderHook != nint.Zero)
            {
                _ = NativeMethods.UnhookWinEvent(_zOrderHook);
                _zOrderHook = nint.Zero;
            }

            if (_showHook != nint.Zero)
            {
                _ = NativeMethods.UnhookWinEvent(_showHook);
                _showHook = nint.Zero;
            }

            _observerThreadId = 0;
            _started.Set();
        }
    }

    private void OnWindowEvent(
        nint eventHook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime)
    {
        _ = eventHook;
        _ = eventThreadId;
        _ = eventTime;
        if (Volatile.Read(ref _disposed) != 0 || windowHandle == nint.Zero)
        {
            return;
        }

        if ((eventType == NativeMethods.EventObjectShow
                || eventType == NativeMethods.EventObjectReorder)
            && (objectId != NativeMethods.ObjIdWindow
                || childId != NativeMethods.ChildIdSelf))
        {
            return;
        }

        OverlayWindowEventKind? kind = eventType switch
        {
            NativeMethods.EventSystemForeground =>
                OverlayWindowEventKind.ForegroundChanged,
            NativeMethods.EventObjectShow =>
                OverlayWindowEventKind.Shown,
            NativeMethods.EventObjectReorder =>
                OverlayWindowEventKind.ZOrderChanged,
            _ => null,
        };
        if (kind is not null)
        {
            try
            {
                _callback(new OverlayWindowEvent(kind.Value, windowHandle));
            }
            catch (Exception exception)
            {
                _ = exception;
                // Native accessibility callbacks must never unwind through user32.
            }
        }
    }
}
