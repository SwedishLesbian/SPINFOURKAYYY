using SpinFourKay.Core.Display;

namespace SpinFourKay.Core.Magpie;

public sealed record MagpieGraphicsAdapter(
    int Index = -1,
    uint VendorId = 0,
    uint DeviceId = 0);

public enum AntiAliasingMode
{
    Off,
    Fxaa,
    Smaa,
}

public sealed record MagpieProfileRequest
{
    public required string MagpieDirectory { get; init; }

    public required string SourceExecutablePath { get; init; }

    public required string SourceWindowClass { get; init; }

    public string ProfileName { get; init; } = "SpinFOURKAYYY - EverQuest Legends";

    public string? LauncherPath { get; init; }

    public ScalingFilter Filter { get; init; } = ScalingFilter.Nis;

    /// <summary>
    /// Bounded edge-detail strength from 0 to 1. The readable NIS path uses this
    /// inside its single directional scaling pass; the optional FSR path maps it
    /// to one RCAS pass. Multiple sharpening passes are intentionally avoided.
    /// </summary>
    public double RcasSharpness { get; init; } = 0.10;

    /// <summary>
    /// Physical destination-to-source scale for this session. Readable UI uses
    /// a clean anti-ringing reconstruction below 1.20x and its directional NIS
    /// path at 1.20x and above.
    /// </summary>
    public double UiScaleFactor { get; init; } = 1.25;

    /// <summary>
    /// Optional whole-frame post-process edge smoothing for compatibility
    /// filters. The automatic Readable UI and native-pixel paths always protect
    /// UI text by ignoring this post-process.
    /// </summary>
    public AntiAliasingMode AntiAliasing { get; init; } = AntiAliasingMode.Off;

    /// <summary>
    /// Uses an exact 1:1 nearest pass without resizing or sharpening the captured
    /// frame. This is reserved for a source whose client area exactly matches the
    /// physical target display. The legacy name is retained for journal/API
    /// compatibility.
    /// </summary>
    public bool NativeClarityOnly { get; init; }

    public bool AutoScaleFullscreen { get; init; }

    /// <summary>
    /// Magpie's 3D-game mode tears its fullscreen surface down whenever an
    /// overlapping application receives focus. SpinFOURKAYYY keeps this off so
    /// Alt+Tab only changes Z-order and the verified input map remains alive.
    /// </summary>
    public bool ThreeDGameMode { get; init; }

    public bool DisableDirectFlip { get; init; }

    public bool CaptureTitleBar { get; init; }

    public bool AdjustCursorSpeed { get; init; } = true;

    public MagpieGraphicsAdapter GraphicsAdapter { get; init; } = new();

    public double? MaximumFrameRate { get; init; }
}

public sealed record MagpiePortableConfigResult(
    string ConfigPath,
    int ScalingModeIndex,
    int ProfileIndex,
    bool ContentChanged);

public sealed record MagpiePortableConfigTransaction(
    string ConfigPath,
    bool OriginalFileExisted,
    byte[] OriginalContent,
    FileAttributes? OriginalAttributes,
    DateTime? OriginalLastWriteTimeUtc,
    byte[] AppliedContent,
    int ScalingModeIndex,
    int ProfileIndex);

public enum MagpiePortableConfigRollbackDisposition
{
    Restored,
    NewerContentPreserved,
    RetryRequired,
}

public sealed record MagpiePortableConfigRollbackResult(
    MagpiePortableConfigRollbackDisposition Disposition,
    string? Issue)
{
    public bool Restored =>
        Disposition == MagpiePortableConfigRollbackDisposition.Restored;

    public bool Resolved =>
        Disposition != MagpiePortableConfigRollbackDisposition.RetryRequired;
}

public sealed record MagpieProcessStartResult(
    System.Diagnostics.Process Process,
    bool AlreadyRunning);

/// <summary>
/// Where a running Magpie process came from. The engine is provisioned into an
/// app-version-specific runtime folder, so an instance left in the tray by a
/// previous SpinFOURKAYYY version lives at a different path than the current
/// one and must not be mistaken for a separately installed Magpie.
/// </summary>
public enum MagpieInstanceOrigin
{
    /// <summary>Running from the runtime this SpinFOURKAYYY version owns.</summary>
    BundledCurrent = 0,

    /// <summary>
    /// Running from a SpinFOURKAYYY engine runtime belonging to a different app
    /// version. Still this application's own engine, just a superseded copy.
    /// </summary>
    OwnPreviousRuntime = 1,

    /// <summary>A Magpie this application did not provision.</summary>
    Foreign = 2,
}

public sealed record MagpieRunningInstance(
    int ProcessId,
    string? ExecutablePath,
    bool IsBundledInstance)
{
    /// <summary>
    /// Where this instance came from. Defaults from
    /// <see cref="IsBundledInstance"/> so callers that only know whether an
    /// instance is the current one keep their previous meaning.
    /// </summary>
    public MagpieInstanceOrigin Origin { get; init; } = IsBundledInstance
        ? MagpieInstanceOrigin.BundledCurrent
        : MagpieInstanceOrigin.Foreign;

    /// <summary>
    /// True when this is a SpinFOURKAYYY engine from a superseded app version.
    /// These may be shut down as routine housekeeping; they are not a foreign
    /// Magpie the user chose to install and run.
    /// </summary>
    public bool IsSupersededOwnRuntime =>
        Origin == MagpieInstanceOrigin.OwnPreviousRuntime;

    /// <summary>
    /// True only for a Magpie this application did not provision, which is the
    /// one case that needs the user's permission before being closed.
    /// </summary>
    public bool IsForeignInstance => Origin == MagpieInstanceOrigin.Foreign;
}

public sealed class ExternalMagpieInstanceConflictException : InvalidOperationException
{
    public ExternalMagpieInstanceConflictException(
        IReadOnlyList<MagpieRunningInstance> conflictingInstances)
        : base(
            "A separately installed Magpie is already running. It was left alone. "
            + "Close it, or let SpinFOURKAYYY close it for you, so the dedicated "
            + "portable profile can load.")
    {
        ConflictingInstances = conflictingInstances;
    }

    public IReadOnlyList<MagpieRunningInstance> ConflictingInstances { get; }
}

public interface IMagpiePortableConfigService
{
    Task<MagpiePortableConfigTransaction> PrepareTransactionAsync(
        MagpieProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<MagpiePortableConfigResult> ApplyTransactionAsync(
        MagpiePortableConfigTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<MagpiePortableConfigRollbackResult> RollbackTransactionAsync(
        MagpiePortableConfigTransaction transaction,
        bool allowChangedContentAfterEngineExit = false,
        CancellationToken cancellationToken = default);

    Task<MagpiePortableConfigResult> WriteAsync(
        MagpieProfileRequest request,
        CancellationToken cancellationToken = default);
}

public interface IMagpieProcessService
{
    IReadOnlyList<MagpieRunningInstance> InspectRunningInstances(string magpieDirectory);

    System.Diagnostics.Process? TryFindRunning(string magpieDirectory);

    MagpieProcessStartResult StartPortable(
        string magpieDirectory,
        bool startInTray = true);

    Task<bool> ShutdownBundledAsync(
        string magpieDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<bool> ShutdownExactAsync(
        string magpieDirectory,
        System.Diagnostics.Process process,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
