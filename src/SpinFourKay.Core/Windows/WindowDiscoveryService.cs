using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SpinFourKay.Core.Display;

namespace SpinFourKay.Core.Windows;

public sealed record WindowDescriptor(
    nint Handle,
    int ProcessId,
    string Title,
    string ClassName,
    PixelRect WindowBounds,
    PixelRect ClientBounds,
    bool IsMinimized,
    string? ExecutablePath);

public interface IWindowDiscoveryService
{
    IReadOnlyList<WindowDescriptor> FindVisibleWindows(int processId);

    WindowDescriptor? FindBestForProcess(int processId);

    Task<WindowDescriptor> WaitForVisibleWindowAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<WindowDescriptor> WaitForStableVisibleWindowAsync(
        Process process,
        PixelSize expectedClientSize,
        TimeSpan timeout,
        int requiredStablePolls = 3,
        CancellationToken cancellationToken = default);

    Task<WindowDescriptor> WaitForStableVisibleWindowAsync(
        Process process,
        TimeSpan timeout,
        int requiredStablePolls = 3,
        CancellationToken cancellationToken = default);

    bool BringToForeground(WindowDescriptor window);
}

public sealed class WindowDiscoveryService : IWindowDiscoveryService
{
    private static readonly TimeSpan PollInterval =
        WindowInteractionReadinessPolicy.DiscoveryPollInterval;

    public IReadOnlyList<WindowDescriptor> FindVisibleWindows(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);

        List<WindowDescriptor> windows = [];
        string? executablePath = ProcessDiscoveryService.TryGetExecutablePath(processId);
        NativeMethods.EnumWindowsCallback callback = (windowHandle, parameter) =>
        {
            _ = parameter;
            _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out uint candidateProcessId);
            if (candidateProcessId != checked((uint)processId)
                || !NativeMethods.IsWindowVisible(windowHandle)
                || NativeMethods.GetAncestor(windowHandle, NativeMethods.GaRoot) != windowHandle)
            {
                return true;
            }

            if (TryDescribeWindow(
                windowHandle,
                processId,
                executablePath,
                out WindowDescriptor? descriptor)
                && descriptor is not null)
            {
                windows.Add(descriptor);
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

    public WindowDescriptor? FindBestForProcess(int processId)
    {
        return FindVisibleWindows(processId)
            .OrderBy(window => window.IsMinimized)
            .ThenByDescending(
                window => (long)window.ClientBounds.Width * window.ClientBounds.Height)
            .ThenByDescending(window => window.Title.Length)
            .FirstOrDefault();
    }

    public async Task<WindowDescriptor> WaitForVisibleWindowAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasExited(process))
            {
                throw new InvalidOperationException(
                    $"Process {process.Id} exited before its game window became available.");
            }

            WindowDescriptor? window = FindBestForProcess(process.Id);
            if (window is not null
                && !window.IsMinimized
                && window.ClientBounds.Width > 0
                && window.ClientBounds.Height > 0)
            {
                return window;
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            await Task.Delay(
                remaining < PollInterval ? remaining : PollInterval,
                cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Timed out waiting for process {process.Id} to create a visible game window.");
    }

    public async Task<WindowDescriptor> WaitForStableVisibleWindowAsync(
        Process process,
        PixelSize expectedClientSize,
        TimeSpan timeout,
        int requiredStablePolls = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        if (requiredStablePolls is < 2 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredStablePolls),
                "Stable poll count must be between 2 and 20.");
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        nint candidateHandle = nint.Zero;
        string candidateClass = string.Empty;
        int stablePolls = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasExited(process))
            {
                throw new InvalidOperationException(
                    $"Process {process.Id} exited before its game window stabilized.");
            }

            WindowDescriptor? window = FindBestForProcess(process.Id);
            bool expectedSize = window is not null
                && !window.IsMinimized
                && Math.Abs(window.ClientBounds.Width - expectedClientSize.Width) <= 1
                && Math.Abs(window.ClientBounds.Height - expectedClientSize.Height) <= 1
                && window.ClassName.Length > 0;

            if (expectedSize)
            {
                if (window!.Handle == candidateHandle
                    && string.Equals(
                        window.ClassName,
                        candidateClass,
                        StringComparison.Ordinal))
                {
                    stablePolls++;
                }
                else
                {
                    candidateHandle = window.Handle;
                    candidateClass = window.ClassName;
                    stablePolls = 1;
                }

                if (stablePolls >= requiredStablePolls)
                {
                    return window;
                }
            }
            else
            {
                candidateHandle = nint.Zero;
                candidateClass = string.Empty;
                stablePolls = 0;
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            await Task.Delay(
                remaining < PollInterval ? remaining : PollInterval,
                cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Timed out waiting for process {process.Id} to keep a "
            + $"{expectedClientSize} client window stable.");
    }

    public async Task<WindowDescriptor> WaitForStableVisibleWindowAsync(
        Process process,
        TimeSpan timeout,
        int requiredStablePolls = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        if (requiredStablePolls is < 2 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredStablePolls),
                "Stable poll count must be between 2 and 20.");
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        (nint Handle, string ClassName, int Width, int Height) candidate = default;
        int stablePolls = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasExited(process))
            {
                throw new InvalidOperationException(
                    $"Process {process.Id} exited before its game window stabilized.");
            }

            WindowDescriptor? window = FindBestForProcess(process.Id);
            if (window is not null
                && !window.IsMinimized
                && window.ClientBounds.Width > 0
                && window.ClientBounds.Height > 0
                && window.ClassName.Length > 0)
            {
                var signature = (
                    window.Handle,
                    window.ClassName,
                    window.ClientBounds.Width,
                    window.ClientBounds.Height);
                if (signature == candidate)
                {
                    stablePolls++;
                }
                else
                {
                    candidate = signature;
                    stablePolls = 1;
                }

                if (stablePolls >= requiredStablePolls)
                {
                    return window;
                }
            }
            else
            {
                candidate = default;
                stablePolls = 0;
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            await Task.Delay(
                remaining < PollInterval ? remaining : PollInterval,
                cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Timed out waiting for process {process.Id} to keep a stable visible window.");
    }

    public bool BringToForeground(WindowDescriptor window)
    {
        if (window.Handle == nint.Zero)
        {
            return false;
        }

        if (window.IsMinimized)
        {
            _ = NativeMethods.ShowWindowAsync(window.Handle, NativeMethods.SwRestore);
        }

        return NativeMethods.SetForegroundWindow(window.Handle);
    }

    private static bool TryDescribeWindow(
        nint windowHandle,
        int processId,
        string? executablePath,
        out WindowDescriptor? descriptor)
    {
        if (!NativeMethods.GetWindowRect(windowHandle, out NativeMethods.NativeRect windowRect)
            || !NativeMethods.GetClientRect(windowHandle, out NativeMethods.NativeRect clientRect))
        {
            descriptor = null;
            return false;
        }

        NativeMethods.NativePoint clientOrigin = new();
        if (!NativeMethods.ClientToScreen(windowHandle, ref clientOrigin))
        {
            descriptor = null;
            return false;
        }

        int clientWidth = Math.Max(0, clientRect.Right - clientRect.Left);
        int clientHeight = Math.Max(0, clientRect.Bottom - clientRect.Top);
        descriptor = new WindowDescriptor(
            windowHandle,
            processId,
            ReadWindowTitle(windowHandle),
            ReadClassName(windowHandle),
            ToPixelRect(windowRect),
            new PixelRect(clientOrigin.X, clientOrigin.Y, clientWidth, clientHeight),
            NativeMethods.IsIconic(windowHandle),
            executablePath);
        return true;
    }

    private static string ReadWindowTitle(nint windowHandle)
    {
        int length = NativeMethods.GetWindowTextLengthW(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        char[] title = GC.AllocateUninitializedArray<char>(length + 1);
        int copied = NativeMethods.GetWindowTextW(windowHandle, title, title.Length);
        return copied > 0 ? new string(title, 0, copied) : string.Empty;
    }

    private static string ReadClassName(nint windowHandle)
    {
        const int InitialCapacity = 256;
        char[] className = GC.AllocateUninitializedArray<char>(InitialCapacity);
        int length = NativeMethods.GetClassNameW(windowHandle, className, className.Length);
        if (length == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                throw new Win32Exception(error);
            }
        }

        return length > 0 ? new string(className, 0, length) : string.Empty;
    }

    private static PixelRect ToPixelRect(NativeMethods.NativeRect rectangle)
    {
        return new PixelRect(
            rectangle.Left,
            rectangle.Top,
            Math.Max(0, rectangle.Right - rectangle.Left),
            Math.Max(0, rectangle.Bottom - rectangle.Top));
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
