using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace SpinFourKay.Core.Windows;

/// <summary>
/// The values read back from an existing shortcut file.
/// </summary>
public sealed record WindowsShortcutSnapshot(
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string Description,
    string IconPath,
    int IconIndex);

/// <summary>
/// Writes and reads Windows shell shortcuts (.lnk) through the shell's own
/// <c>IShellLink</c> implementation.
/// </summary>
/// <remarks>
/// Shortcut files are the only artifact SpinFOURKAYYY creates outside its own
/// state folder, so every write is verified by reading the saved file back.
/// A shortcut that cannot be read back is deleted rather than left behind in
/// an unknown state.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsShortcutService
{
    private const int MaximumPathCharacters = 260;
    private const int MaximumTextCharacters = 1024;
    private const int ShowNormal = 1;
    private const int StgmRead = 0x00000000;
    private const uint SlgpRawPath = 0x0004;
    private static readonly Guid ShellLinkClassId =
        new("00021401-0000-0000-C000-000000000046");

    /// <summary>
    /// Writes <paramref name="plan"/> to <paramref name="shortcutPath"/>,
    /// replacing any existing file, then verifies the result by reading it
    /// back.
    /// </summary>
    /// <exception cref="IOException">
    /// The shortcut could not be written, or the saved file did not match the
    /// requested plan.
    /// </exception>
    public static WindowsShortcutSnapshot Save(
        DesktopShortcutPlan plan,
        string shortcutPath)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);

        string fullShortcutPath = Path.GetFullPath(shortcutPath.Trim());
        string? parent = Path.GetDirectoryName(fullShortcutPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                "The folder for the new shortcut does not exist, so nothing "
                    + "was written.");
        }

        IShellLinkW link = CreateShellLink();
        try
        {
            link.SetPath(plan.TargetPath);
            link.SetArguments(plan.Arguments);
            link.SetWorkingDirectory(plan.WorkingDirectory);
            link.SetDescription(plan.Description);
            link.SetIconLocation(plan.IconPath, plan.IconIndex);
            link.SetShowCmd(ShowNormal);
            ((IPersistFile)link).Save(fullShortcutPath, fRemember: true);
        }
        catch (COMException exception)
        {
            throw new IOException(
                "Windows could not write the shortcut, so nothing was changed. "
                    + exception.Message,
                exception);
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(link);
        }

        WindowsShortcutSnapshot saved;
        try
        {
            saved = Read(fullShortcutPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            DeleteUnverifiedShortcut(fullShortcutPath);
            throw new IOException(
                "The new shortcut could not be verified after it was written, "
                    + "so it was removed. Nothing else was changed. "
                    + exception.Message,
                exception);
        }

        if (!string.Equals(
                saved.TargetPath,
                plan.TargetPath,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(saved.Arguments, plan.Arguments, StringComparison.Ordinal))
        {
            DeleteUnverifiedShortcut(fullShortcutPath);
            throw new IOException(
                "The saved shortcut did not match the requested launch, so it "
                    + "was removed. Nothing else was changed.");
        }

        return saved;
    }

    /// <summary>Reads an existing shortcut file.</summary>
    public static WindowsShortcutSnapshot Read(string shortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        string fullShortcutPath = Path.GetFullPath(shortcutPath.Trim());
        if (!File.Exists(fullShortcutPath))
        {
            throw new FileNotFoundException(
                "The shortcut file was not found.",
                fullShortcutPath);
        }

        IShellLinkW link = CreateShellLink();
        try
        {
            ((IPersistFile)link).Load(fullShortcutPath, StgmRead);

            StringBuilder target = new(MaximumPathCharacters);
            link.GetPath(
                target,
                target.Capacity,
                nint.Zero,
                SlgpRawPath);

            StringBuilder arguments = new(MaximumTextCharacters);
            link.GetArguments(arguments, arguments.Capacity);

            StringBuilder workingDirectory = new(MaximumPathCharacters);
            link.GetWorkingDirectory(
                workingDirectory,
                workingDirectory.Capacity);

            StringBuilder description = new(MaximumTextCharacters);
            link.GetDescription(description, description.Capacity);

            StringBuilder iconPath = new(MaximumPathCharacters);
            link.GetIconLocation(iconPath, iconPath.Capacity, out int iconIndex);

            return new WindowsShortcutSnapshot(
                target.ToString(),
                arguments.ToString(),
                workingDirectory.ToString(),
                description.ToString(),
                iconPath.ToString(),
                iconIndex);
        }
        catch (COMException exception)
        {
            throw new IOException(
                "Windows could not read the shortcut file. " + exception.Message,
                exception);
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(link);
        }
    }

    private static IShellLinkW CreateShellLink()
    {
        try
        {
            Type shellLinkType = Type.GetTypeFromCLSID(ShellLinkClassId)
                ?? throw new NotSupportedException(
                    "The Windows shell shortcut component is not registered.");
            return (IShellLinkW)(Activator.CreateInstance(shellLinkType)
                ?? throw new NotSupportedException(
                    "The Windows shell shortcut component could not be created."));
        }
        catch (Exception exception) when (
            exception is COMException
                or InvalidCastException
                or MissingMethodException
                or NotSupportedException
                or TypeLoadException)
        {
            throw new NotSupportedException(
                "Windows did not provide its shortcut service, so no shortcut "
                    + "was created. Nothing else was changed. "
                    + exception.Message,
                exception);
        }
    }

    private static void DeleteUnverifiedShortcut(string shortcutPath)
    {
        try
        {
            File.Delete(shortcutPath);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            // The verification failure is already being reported; a shortcut
            // that also resists deletion must not replace that message.
        }
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int fileCharacters,
            nint findData,
            uint flags);

        void GetIDList(out nint idList);

        void SetIDList(nint idList);

        void GetDescription(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,
            int nameCharacters);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,
            int directoryCharacters);

        void SetWorkingDirectory(
            [MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,
            int argumentCharacters);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out ushort hotkey);

        void SetHotkey(ushort hotkey);

        void GetShowCmd(out int showCommand);

        void SetShowCmd(int showCommand);

        void GetIconLocation(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int iconPathCharacters,
            out int iconIndex);

        void SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string iconPath,
            int iconIndex);

        void SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string relativePath,
            uint reserved);

        void Resolve(nint window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
