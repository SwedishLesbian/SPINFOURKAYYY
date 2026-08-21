using SpinFourKay.Core.Startup;

namespace SpinFourKay.Core.Windows;

/// <summary>
/// Which unattended launch a desktop shortcut should request.
/// </summary>
public enum DesktopShortcutKind
{
    /// <summary>Starts the normal Legends launcher.</summary>
    NormalPlay = 0,

    /// <summary>Starts SpinTexture's verified enhanced flow.</summary>
    EnhancedPlay = 1,
}

/// <summary>
/// The exact contents of one desktop shortcut, resolved before any file is
/// written so the values can be validated and asserted without touching the
/// user's desktop.
/// </summary>
/// <remarks>
/// The icon deliberately points at the player's own installed
/// <c>eqgame.exe</c> rather than a copied image. SpinFOURKAYYY ships no
/// EverQuest artwork, so referencing the installed client is the only way to
/// show the real Legends icon without redistributing a game asset.
/// </remarks>
public sealed record DesktopShortcutPlan
{
    /// <summary>The shortcut file name, including the .lnk extension.</summary>
    public required string FileName { get; init; }

    /// <summary>The absolute path of SpinFOURKAYYY.exe.</summary>
    public required string TargetPath { get; init; }

    /// <summary>The startup switch passed to SpinFOURKAYYY.exe.</summary>
    public required string Arguments { get; init; }

    /// <summary>The folder the shortcut starts in.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>The hover description Windows shows for the shortcut.</summary>
    public required string Description { get; init; }

    /// <summary>The file supplying the shortcut icon.</summary>
    public required string IconPath { get; init; }

    /// <summary>The icon index inside <see cref="IconPath"/>.</summary>
    public required int IconIndex { get; init; }

    /// <summary>
    /// True when the icon came from the installed Legends client rather than
    /// the SpinFOURKAYYY fallback icon.
    /// </summary>
    public required bool UsesLegendsIcon { get; init; }

    /// <summary>
    /// Builds the shortcut contents for <paramref name="kind"/>.
    /// </summary>
    /// <param name="kind">Which launch the shortcut should request.</param>
    /// <param name="applicationPath">
    /// The absolute path of the running SpinFOURKAYYY.exe. Callers should use
    /// <see cref="Environment.ProcessPath"/>; the published application is a
    /// single-file bundle, so assembly locations are not usable here.
    /// </param>
    /// <param name="legendsDirectory">
    /// The saved EverQuest Legends folder, used only to locate the icon. A
    /// missing or invalid folder falls back to the SpinFOURKAYYY icon.
    /// </param>
    public static DesktopShortcutPlan Create(
        DesktopShortcutKind kind,
        string applicationPath,
        string? legendsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);
        if (kind is not DesktopShortcutKind.NormalPlay
            and not DesktopShortcutKind.EnhancedPlay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                "The requested shortcut kind is not supported.");
        }

        string fullApplicationPath = Path.GetFullPath(applicationPath.Trim());
        if (!File.Exists(fullApplicationPath))
        {
            throw new FileNotFoundException(
                "SpinFOURKAYYY.exe could not be located, so no shortcut was "
                    + "created. Extract the whole release into one folder and "
                    + "run it from there.",
                fullApplicationPath);
        }

        string workingDirectory = Path.GetDirectoryName(fullApplicationPath)
            ?? throw new InvalidDataException(
                "SpinFOURKAYYY.exe has no parent folder, so no shortcut was "
                    + "created.");

        string? legendsIconPath = FindLegendsIcon(legendsDirectory);
        bool isEnhanced = kind == DesktopShortcutKind.EnhancedPlay;
        return new DesktopShortcutPlan
        {
            FileName = isEnhanced
                ? "Enhanced EverQuest (SpinFOURKAYYY).lnk"
                : "EverQuest (SpinFOURKAYYY).lnk",
            TargetPath = fullApplicationPath,
            Arguments = isEnhanced
                ? StartupCommandLine.PlayEnhancedArgument
                : StartupCommandLine.PlayArgument,
            WorkingDirectory = workingDirectory,
            Description = isEnhanced
                ? "Opens SpinFOURKAYYY and starts the enhanced EverQuest "
                    + "Legends texture pack through SpinTexture, using your "
                    + "saved size, quality, display and UI choices."
                : "Opens SpinFOURKAYYY and starts EverQuest Legends through "
                    + "the normal launcher, using your saved size, quality, "
                    + "display and UI choices.",
            IconPath = legendsIconPath ?? fullApplicationPath,
            IconIndex = 0,
            UsesLegendsIcon = legendsIconPath is not null,
        };
    }

    /// <summary>
    /// Resolves where this shortcut belongs inside
    /// <paramref name="desktopDirectory"/>.
    /// </summary>
    public string ResolveDestinationPath(string desktopDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopDirectory);
        return Path.Combine(
            Path.GetFullPath(desktopDirectory.Trim()),
            FileName);
    }

    private static string? FindLegendsIcon(string? legendsDirectory)
    {
        if (string.IsNullOrWhiteSpace(legendsDirectory))
        {
            return null;
        }

        try
        {
            string candidate = Path.Combine(
                Path.GetFullPath(legendsDirectory.Trim()),
                "eqgame.exe");
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            return null;
        }
    }
}
