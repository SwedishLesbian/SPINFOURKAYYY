using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SpinFourKay.Core.Windows;

internal static class NativeMethods
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint GaRoot = 2;
    internal const uint MonitorDefaultToNearest = 2;
    internal const uint MonitorDefaultToPrimary = 1;
    internal const int SwRestore = 9;
    internal const uint WmClose = 0x0010;
    internal const uint WmQuit = 0x0012;
    internal static readonly nint HwndBroadcast = new(0xFFFF);
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventObjectShow = 0x8002;
    internal const uint EventObjectReorder = 0x8004;
    internal const int ObjIdWindow = 0;
    internal const int ChildIdSelf = 0;
    internal const uint WineventOutOfContext = 0x0000;
    internal const uint WsExTopmost = 0x00000008;
    internal const uint WsExTransparent = 0x00000020;
    internal const uint WsExToolWindow = 0x00000080;
    internal const uint WsExLayered = 0x00080000;
    internal const uint MonitorInfoPrimary = 0x00000001;
    internal static readonly nint HwndTopmost = new(-1);
    internal static readonly nint HwndNotTopmost = new(-2);

    internal delegate bool EnumWindowsCallback(nint windowHandle, nint parameter);
    internal delegate void WinEventCallback(
        nint eventHook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime);
    internal delegate bool MonitorEnumCallback(
        nint monitorHandle,
        nint monitorDeviceContext,
        ref NativeRect monitorRectangle,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetPropW(nint windowHandle, string propertyName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowTextLengthW(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowTextW(
        nint windowHandle,
        [Out] char[] text,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassNameW(
        nint windowHandle,
        [Out] char[] className,
        int maximumCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(
        nint windowHandle,
        ref NativeWindowPlacement windowPlacement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPlacement(
        nint windowHandle,
        [In] ref NativeWindowPlacement windowPlacement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint windowHandle, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessageW(string messageName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        nint eventHookModule,
        WinEventCallback callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint eventHook);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessageW(
        uint threadId,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessageW(
        out NativeMessage message,
        nint windowHandle,
        uint messageFilterMinimum,
        uint messageFilterMaximum);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    internal static extern int GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr64(
        nint windowHandle,
        int index,
        nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    internal static extern int SetWindowLong32(
        nint windowHandle,
        int index,
        int newValue);

    [DllImport("user32.dll")]
    internal static extern nint GetMenu(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustWindowRectExForDpi(
        ref NativeRect rectangle,
        uint style,
        [MarshalAs(UnmanagedType.Bool)] bool hasMenu,
        uint extendedStyle,
        uint dpi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint deviceContext,
        nint clippingRectangle,
        MonitorEnumCallback callback,
        nint parameter);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(
        nint monitorHandle,
        ref MonitorInfo monitorInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(
        SafeProcessHandle process,
        uint flags,
        [Out] char[] executableName,
        ref uint size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMessage
    {
        internal nint WindowHandle;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal NativePoint Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeWindowPlacement
    {
        internal uint Length;
        internal uint Flags;
        internal uint ShowCommand;
        internal NativePoint MinimumPosition;
        internal NativePoint MaximumPosition;
        internal NativeRect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        internal int Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;
    }

    internal static nint GetWindowLongPtr(nint windowHandle, int index)
    {
        return nint.Size == sizeof(long)
            ? GetWindowLongPtr64(windowHandle, index)
            : new nint(GetWindowLong32(windowHandle, index));
    }

    internal static nint SetWindowLongPtr(
        nint windowHandle,
        int index,
        nint newValue)
    {
        return nint.Size == sizeof(long)
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : new nint(SetWindowLong32(windowHandle, index, newValue.ToInt32()));
    }
}
