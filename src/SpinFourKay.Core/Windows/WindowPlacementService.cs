using System.ComponentModel;
using System.Runtime.InteropServices;
using SpinFourKay.Core.Display;

namespace SpinFourKay.Core.Windows;

public sealed record MonitorDescriptor(
    nint Handle,
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary);

public sealed record WindowPlacementResult(
    bool IsExactAndOnScreen,
    PixelSize RequestedClientSize,
    PixelSize? ActualClientSize,
    PixelRect? WindowBounds,
    MonitorDescriptor Monitor,
    IReadOnlyList<string> Issues);

public readonly record struct WindowFrameExtents(
    int Left,
    int Top,
    int Right,
    int Bottom);

public sealed record WindowPlacementPlan(
    PixelRect OuterBounds,
    PixelRect ExpectedClientBounds,
    bool UsesWorkArea,
    bool FrameExtendsBeyondMonitor);

/// <summary>
/// Plans an exact client rectangle on a physical monitor. Window chrome may
/// occupy the taskbar area or extend just beyond the monitor when a native-size
/// client is requested; the captured client itself always remains fully inside
/// the selected physical monitor.
/// </summary>
public static class WindowPlacementPlanner
{
    /// <summary>
    /// Returns true when the existing physical client rectangle already has
    /// the requested size and is wholly contained by the selected monitor.
    /// Preserving it avoids changing a running legacy game's cached mouse-hit
    /// geometry merely to make the source window look centered.
    /// </summary>
    public static bool CanPreserveCurrentClientArea(
        PixelRect currentClientBounds,
        PixelSize requestedClientSize,
        MonitorDescriptor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return currentClientBounds.Size == requestedClientSize
            && Contains(monitor.Bounds, currentClientBounds);
    }

    public static WindowPlacementPlan Create(
        PixelSize clientSize,
        MonitorDescriptor monitor,
        WindowFrameExtents frame)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (frame.Left < 0
            || frame.Top < 0
            || frame.Right < 0
            || frame.Bottom < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                "Window-frame extents cannot be negative.");
        }

        if (clientSize.Width > monitor.Bounds.Width
            || clientSize.Height > monitor.Bounds.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clientSize),
                $"The {clientSize} client area is larger than the selected "
                    + $"{monitor.Bounds.Size} physical monitor.");
        }

        int outerWidth = checked(clientSize.Width + frame.Left + frame.Right);
        int outerHeight = checked(clientSize.Height + frame.Top + frame.Bottom);
        PixelRect placementArea;
        bool usesWorkArea;
        int x;
        int y;
        if (outerWidth <= monitor.WorkArea.Width
            && outerHeight <= monitor.WorkArea.Height)
        {
            placementArea = monitor.WorkArea;
            usesWorkArea = true;
            x = placementArea.X + (placementArea.Width - outerWidth) / 2;
            y = placementArea.Y + (placementArea.Height - outerHeight) / 2;
        }
        else if (outerWidth <= monitor.Bounds.Width
            && outerHeight <= monitor.Bounds.Height)
        {
            placementArea = monitor.Bounds;
            usesWorkArea = false;
            x = placementArea.X + (placementArea.Width - outerWidth) / 2;
            y = placementArea.Y + (placementArea.Height - outerHeight) / 2;
        }
        else
        {
            usesWorkArea = false;
            int clientX =
                monitor.Bounds.X + (monitor.Bounds.Width - clientSize.Width) / 2;
            int clientY =
                monitor.Bounds.Y + (monitor.Bounds.Height - clientSize.Height) / 2;
            x = checked(clientX - frame.Left);
            y = checked(clientY - frame.Top);
        }

        PixelRect outerBounds = new(x, y, outerWidth, outerHeight);
        PixelRect clientBounds = new(
            checked(x + frame.Left),
            checked(y + frame.Top),
            clientSize.Width,
            clientSize.Height);
        if (!Contains(monitor.Bounds, clientBounds))
        {
            throw new InvalidOperationException(
                "The planned client area is not fully contained by the selected "
                    + "physical monitor.");
        }

        return new WindowPlacementPlan(
            outerBounds,
            clientBounds,
            usesWorkArea,
            !Contains(monitor.Bounds, outerBounds));
    }

    private static bool Contains(PixelRect outer, PixelRect inner)
    {
        return inner.X >= outer.X
            && inner.Y >= outer.Y
            && inner.X + inner.Width <= outer.X + outer.Width
            && inner.Y + inner.Height <= outer.Y + outer.Height;
    }
}

public readonly record struct WindowPlacementPoint(int X, int Y);

/// <summary>
/// The complete recoverable Win32 placement of a window, plus its observed
/// physical outer and client geometry. Placement flags and show command are
/// retained so a maximized window can be restored as maximized rather than
/// merely resized to similar bounds.
/// </summary>
public sealed record WindowGeometrySnapshot(
    PixelRect WindowBounds,
    PixelSize ClientSize,
    uint PlacementFlags,
    uint ShowCommand,
    WindowPlacementPoint MinimumPosition,
    WindowPlacementPoint MaximumPosition,
    PixelRect NormalWindowBounds);

public sealed record WindowGeometryRestoreResult(
    bool IsExact,
    WindowGeometrySnapshot ExpectedGeometry,
    WindowGeometrySnapshot? ActualGeometry,
    IReadOnlyList<string> Issues);

public interface IWindowPlacementService
{
    IReadOnlyList<MonitorDescriptor> GetMonitors();

    WindowGeometrySnapshot CaptureWindowGeometry(nint windowHandle);

    WindowPlacementResult CenterFixedClientArea(
        nint windowHandle,
        PixelSize clientSize,
        nint targetMonitor = default);

    WindowGeometryRestoreResult RestoreExactWindowGeometry(
        nint windowHandle,
        WindowGeometrySnapshot geometry);
}

/// <summary>
/// Restores, sizes, and centers a source window using physical client pixels.
/// This keeps Magpie's "Closest" monitor selection deterministic.
/// </summary>
public sealed class WindowPlacementService : IWindowPlacementService
{
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        List<MonitorDescriptor> monitors = [];
        NativeMethods.MonitorEnumCallback callback = (
            nint monitorHandle,
            nint monitorDeviceContext,
            ref NativeMethods.NativeRect monitorRectangle,
            nint parameter) =>
        {
            _ = monitorDeviceContext;
            _ = monitorRectangle;
            _ = parameter;
            monitors.Add(ReadMonitor(monitorHandle));
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(
            nint.Zero,
            nint.Zero,
            callback,
            nint.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return monitors
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.Bounds.X)
            .ThenBy(monitor => monitor.Bounds.Y)
            .ToArray();
    }

    public WindowPlacementResult CenterFixedClientArea(
        nint windowHandle,
        PixelSize clientSize,
        nint targetMonitor = default)
    {
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            throw new ArgumentException("The source window handle is invalid.", nameof(windowHandle));
        }

        MonitorDescriptor monitor = ResolveMonitor(targetMonitor);
        List<string> issues = [];

        bool needsRestore =
            NativeMethods.IsZoomed(windowHandle) || NativeMethods.IsIconic(windowHandle);
        PixelRect? currentClientBounds = TryGetClientBounds(windowHandle);
        if (!needsRestore
            && currentClientBounds is { } existingClientBounds
            && WindowPlacementPlanner.CanPreserveCurrentClientArea(
                existingClientBounds,
                clientSize,
                monitor))
        {
            return new WindowPlacementResult(
                IsExactAndOnScreen: true,
                clientSize,
                ActualClientSize: existingClientBounds.Size,
                WindowBounds: TryGetWindowBounds(windowHandle),
                monitor,
                issues);
        }

        if (needsRestore)
        {
            _ = NativeMethods.ShowWindow(windowHandle, NativeMethods.SwRestore);
        }

        nint styleValue = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlStyle);
        nint extendedStyleValue = NativeMethods.GetWindowLongPtr(
            windowHandle,
            NativeMethods.GwlExStyle);
        uint style = unchecked((uint)styleValue.ToInt64());
        uint extendedStyle = unchecked((uint)extendedStyleValue.ToInt64());
        uint dpi = NativeMethods.GetDpiForWindow(windowHandle);
        if (dpi == 0)
        {
            dpi = 96;
        }

        NativeMethods.NativeRect adjusted = new()
        {
            Right = clientSize.Width,
            Bottom = clientSize.Height,
        };
        if (!NativeMethods.AdjustWindowRectExForDpi(
            ref adjusted,
            style,
            NativeMethods.GetMenu(windowHandle) != nint.Zero,
            extendedStyle,
            dpi))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        WindowFrameExtents frame = new(
            checked(-adjusted.Left),
            checked(-adjusted.Top),
            checked(adjusted.Right - clientSize.Width),
            checked(adjusted.Bottom - clientSize.Height));
        WindowPlacementPlan plan;
        try
        {
            plan = WindowPlacementPlanner.Create(clientSize, monitor, frame);
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
        {
            issues.Add(exception.Message);
            return new WindowPlacementResult(
                IsExactAndOnScreen: false,
                clientSize,
                ActualClientSize: TryGetClientSize(windowHandle),
                WindowBounds: TryGetWindowBounds(windowHandle),
                monitor,
                issues);
        }

        if (!NativeMethods.SetWindowPos(
            windowHandle,
            nint.Zero,
            plan.OuterBounds.X,
            plan.OuterBounds.Y,
            plan.OuterBounds.Width,
            plan.OuterBounds.Height,
            NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate
                | NativeMethods.SwpFrameChanged))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        PixelSize? actualClientSize = TryGetClientSize(windowHandle);
        PixelRect? actualClientBounds = TryGetClientBounds(windowHandle);
        PixelRect? windowBounds = TryGetWindowBounds(windowHandle);
        if (actualClientSize != clientSize)
        {
            issues.Add(
                $"The game accepted a {actualClientSize?.ToString() ?? "missing"} client area "
                + $"instead of the requested {clientSize}.");
        }

        if (NativeMethods.IsZoomed(windowHandle))
        {
            issues.Add("The source game window is still maximized.");
        }

        if (actualClientBounds is null
            || !Contains(monitor.Bounds, actualClientBounds.Value))
        {
            issues.Add(
                "The source game client area is not fully inside the selected "
                    + "physical monitor.");
        }

        return new WindowPlacementResult(
            IsExactAndOnScreen: issues.Count == 0,
            clientSize,
            actualClientSize,
            windowBounds,
            monitor,
            issues);
    }

    public WindowGeometrySnapshot CaptureWindowGeometry(nint windowHandle)
    {
        ValidateWindowHandle(windowHandle);

        NativeMethods.NativeWindowPlacement placement = new()
        {
            Length = checked((uint)Marshal.SizeOf<NativeMethods.NativeWindowPlacement>()),
        };
        if (!NativeMethods.GetWindowPlacement(windowHandle, ref placement))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        PixelRect windowBounds = TryGetWindowBounds(windowHandle)
            ?? throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows did not expose the source window's outer bounds.");
        PixelSize clientSize = TryGetClientSize(windowHandle)
            ?? throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows did not expose the source window's client size.");

        return ToSnapshot(placement, windowBounds, clientSize);
    }

    public WindowGeometryRestoreResult RestoreExactWindowGeometry(
        nint windowHandle,
        WindowGeometrySnapshot geometry)
    {
        ValidateWindowHandle(windowHandle);
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.WindowBounds.Width <= 0 || geometry.WindowBounds.Height <= 0
            || geometry.NormalWindowBounds.Width <= 0
            || geometry.NormalWindowBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(geometry),
                "The previous outer and normal window bounds must contain positive pixels.");
        }

        if (geometry.ClientSize.Width <= 0 || geometry.ClientSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(geometry),
                "The previous client size must contain positive pixels.");
        }

        NativeMethods.NativeWindowPlacement placement = ToNativePlacement(geometry);
        if (!NativeMethods.SetWindowPlacement(windowHandle, ref placement))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        List<string> issues = [];
        WindowGeometrySnapshot? actualGeometry = null;
        try
        {
            actualGeometry = CaptureWindowGeometry(windowHandle);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or Win32Exception
                or InvalidOperationException)
        {
            issues.Add(
                "Windows did not expose the window placement after restoration: "
                    + exception.Message);
        }

        if (actualGeometry is not null)
        {
            AddGeometryIssues(geometry, actualGeometry, issues);
        }

        return new WindowGeometryRestoreResult(
            actualGeometry is not null && issues.Count == 0,
            geometry,
            actualGeometry,
            issues);
    }

    private static void ValidateWindowHandle(nint windowHandle)
    {
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            throw new ArgumentException(
                "The source window handle is invalid.",
                nameof(windowHandle));
        }
    }

    private static WindowGeometrySnapshot ToSnapshot(
        NativeMethods.NativeWindowPlacement placement,
        PixelRect windowBounds,
        PixelSize clientSize)
    {
        return new WindowGeometrySnapshot(
            windowBounds,
            clientSize,
            placement.Flags,
            placement.ShowCommand,
            new WindowPlacementPoint(
                placement.MinimumPosition.X,
                placement.MinimumPosition.Y),
            new WindowPlacementPoint(
                placement.MaximumPosition.X,
                placement.MaximumPosition.Y),
            ToPixelRect(placement.NormalPosition));
    }

    private static NativeMethods.NativeWindowPlacement ToNativePlacement(
        WindowGeometrySnapshot geometry)
    {
        return new NativeMethods.NativeWindowPlacement
        {
            Length = checked((uint)Marshal.SizeOf<NativeMethods.NativeWindowPlacement>()),
            Flags = geometry.PlacementFlags,
            ShowCommand = geometry.ShowCommand,
            MinimumPosition = new NativeMethods.NativePoint
            {
                X = geometry.MinimumPosition.X,
                Y = geometry.MinimumPosition.Y,
            },
            MaximumPosition = new NativeMethods.NativePoint
            {
                X = geometry.MaximumPosition.X,
                Y = geometry.MaximumPosition.Y,
            },
            NormalPosition = ToNativeRect(geometry.NormalWindowBounds),
        };
    }

    private static NativeMethods.NativeRect ToNativeRect(PixelRect rectangle)
    {
        return new NativeMethods.NativeRect
        {
            Left = rectangle.X,
            Top = rectangle.Y,
            Right = checked(rectangle.X + rectangle.Width),
            Bottom = checked(rectangle.Y + rectangle.Height),
        };
    }

    private static void AddGeometryIssues(
        WindowGeometrySnapshot expected,
        WindowGeometrySnapshot actual,
        List<string> issues)
    {
        if (actual.ClientSize != expected.ClientSize)
        {
            issues.Add(
                $"The restored client area is {actual.ClientSize}, not the previous "
                    + $"{expected.ClientSize}.");
        }

        if (actual.WindowBounds != expected.WindowBounds)
        {
            issues.Add(
                $"The restored outer window bounds are {actual.WindowBounds}, not the "
                    + $"previous {expected.WindowBounds}.");
        }

        if (actual.ShowCommand != expected.ShowCommand)
        {
            issues.Add(
                $"The restored show command is {actual.ShowCommand}, not the previous "
                    + $"{expected.ShowCommand}.");
        }

        if (actual.PlacementFlags != expected.PlacementFlags)
        {
            issues.Add(
                $"The restored placement flags are {actual.PlacementFlags}, not the "
                    + $"previous {expected.PlacementFlags}.");
        }

        if (actual.MinimumPosition != expected.MinimumPosition
            || actual.MaximumPosition != expected.MaximumPosition
            || actual.NormalWindowBounds != expected.NormalWindowBounds)
        {
            issues.Add(
                "The restored minimized, maximized, or normal placement data does "
                    + "not exactly match the previous window placement.");
        }
    }

    private MonitorDescriptor ResolveMonitor(nint targetMonitor)
    {
        if (targetMonitor != nint.Zero)
        {
            MonitorDescriptor? requested = GetMonitors()
                .FirstOrDefault(monitor => monitor.Handle == targetMonitor);
            return requested
                ?? throw new ArgumentException(
                    "The selected monitor is no longer connected.",
                    nameof(targetMonitor));
        }

        NativeMethods.NativePoint origin = new();
        nint primary = NativeMethods.MonitorFromPoint(
            origin,
            NativeMethods.MonitorDefaultToPrimary);
        if (primary == nint.Zero)
        {
            throw new InvalidOperationException("Windows did not identify a primary monitor.");
        }

        return ReadMonitor(primary);
    }

    private static MonitorDescriptor ReadMonitor(nint monitorHandle)
    {
        NativeMethods.MonitorInfo info = new()
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (!NativeMethods.GetMonitorInfoW(monitorHandle, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new MonitorDescriptor(
            monitorHandle,
            ToPixelRect(info.Monitor),
            ToPixelRect(info.WorkArea),
            (info.Flags & NativeMethods.MonitorInfoPrimary) != 0);
    }

    private static PixelSize? TryGetClientSize(nint windowHandle)
    {
        if (!NativeMethods.GetClientRect(
                windowHandle,
                out NativeMethods.NativeRect rectangle))
        {
            return null;
        }

        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;
        return width > 0 && height > 0
            ? new PixelSize(width, height)
            : null;
    }

    private static PixelRect? TryGetClientBounds(nint windowHandle)
    {
        if (!NativeMethods.GetClientRect(
                windowHandle,
                out NativeMethods.NativeRect rectangle))
        {
            return null;
        }

        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        NativeMethods.NativePoint origin = new()
        {
            X = rectangle.Left,
            Y = rectangle.Top,
        };
        if (!NativeMethods.ClientToScreen(windowHandle, ref origin))
        {
            return null;
        }

        return new PixelRect(origin.X, origin.Y, width, height);
    }

    private static PixelRect? TryGetWindowBounds(nint windowHandle)
    {
        return NativeMethods.GetWindowRect(windowHandle, out NativeMethods.NativeRect rectangle)
            ? ToPixelRect(rectangle)
            : null;
    }

    private static PixelRect ToPixelRect(NativeMethods.NativeRect rectangle)
    {
        return new PixelRect(
            rectangle.Left,
            rectangle.Top,
            Math.Max(0, rectangle.Right - rectangle.Left),
            Math.Max(0, rectangle.Bottom - rectangle.Top));
    }

    private static bool Contains(PixelRect outer, PixelRect inner)
    {
        return inner.X >= outer.X
            && inner.Y >= outer.Y
            && inner.X + inner.Width <= outer.X + outer.Width
            && inner.Y + inner.Height <= outer.Y + outer.Height;
    }
}
