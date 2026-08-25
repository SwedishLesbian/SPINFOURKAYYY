using System.Diagnostics;
using SpinFourKay.Core.Configuration;
using SpinFourKay.Core.Display;
using SpinFourKay.Core.IO;
using SpinFourKay.Core.Magpie;
using SpinFourKay.Core.Windows;

namespace SpinFourKay.Core.Orchestration;

public enum FourKayGameStartMode
{
    OfficialLauncher,
    SpinTextureEnhanced,
}

public sealed record FourKayLaunchRequest
{
    public required FourKayPreparedState PreparedState { get; init; }

    public required string LauncherPath { get; init; }

    public required string MagpieDirectory { get; init; }

    public IReadOnlyList<string> LauncherArguments { get; init; } = [];

    public FourKayGameStartMode StartMode { get; init; } =
        FourKayGameStartMode.OfficialLauncher;

    public TimeSpan GameStartTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan WindowTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan ScalingTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public double RcasSharpness { get; init; } = 0.10;

    public AntiAliasingMode AntiAliasing { get; init; } = AntiAliasingMode.Off;

    public double? MaximumFrameRate { get; init; }

    public MagpieGraphicsAdapter GraphicsAdapter { get; init; } = new();

    public bool DisableDirectFlip { get; init; }

    public bool MaintainTopmostOverlays { get; init; }
}

public sealed record FourKayAttachRequest
{
    public required string EqDirectory { get; init; }

    public required string MagpieDirectory { get; init; }

    public required ScalingFilter Filter { get; init; }

    /// <summary>
    /// Optional exact process identity captured by automatic discovery. When
    /// supplied, Attach fails before touching the game window unless the sole
    /// process in the selected Legends directory still has this PID and start
    /// time.
    /// </summary>
    public int? ExpectedProcessId { get; init; }

    public DateTimeOffset? ExpectedProcessStartTimeUtc { get; init; }

    /// <summary>
    /// Optional generic/custom-UI source size to apply to the exact running game
    /// window before fullscreen scaling. This never writes eqclient.ini and is
    /// rejected for strict SpinUI sessions.
    /// </summary>
    public PixelSize? RequestedGenericClientSize { get; init; }

    /// <summary>
    /// Validation-only source size, primarily for strict SpinUI layouts. A
    /// mismatch fails before any window placement or resize.
    /// </summary>
    public PixelSize? ExpectedClientSize { get; init; }

    public nint TargetMonitor { get; init; }

    public TimeSpan WindowTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ScalingTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public double RcasSharpness { get; init; } = 0.10;

    public AntiAliasingMode AntiAliasing { get; init; } = AntiAliasingMode.Off;

    public double? MaximumFrameRate { get; init; }

    public MagpieGraphicsAdapter GraphicsAdapter { get; init; } = new();

    public bool DisableDirectFlip { get; init; }

    public bool MaintainTopmostOverlays { get; init; }

    public FourKayUiCompatibilityMode UiCompatibilityMode { get; init; } =
        FourKayUiCompatibilityMode.GenericOrCustom;
}

public sealed record FourKayLaunchResult(
    Process? LauncherProcess,
    Process GameProcess,
    WindowDescriptor SourceWindow,
    WindowPlacementResult Placement,
    ScalingFilter EffectiveFilter,
    FourKayUiCompatibilityMode UiCompatibilityMode,
    PixelRect ExpectedDestinationRegion,
    string MagpieDirectory,
    MagpiePortableConfigResult MagpieConfig,
    MagpieProcessStartResult MagpieProcess,
    MagpieScalingWindowInspection ScalingInspection,
    bool AttachedToExistingGame,
    IReadOnlyList<string> Warnings)
{
    public AttachWindowRecoveryState? AttachWindowRecovery { get; init; }

    public OverlayCompatibilitySession? OverlayCompatibility { get; init; }
}

public sealed record FourKayLiveScaleRequest
{
    public required FourKayLaunchResult ActiveLaunch { get; init; }

    public required ResolutionPlan RequestedPlan { get; init; }

    public TimeSpan WindowTimeout { get; init; } = TimeSpan.FromSeconds(8);
}

public enum FourKayLiveScaleDisposition
{
    Committed,
    RecoveredPrevious,
}

public sealed record FourKayLiveScaleResult(
    FourKayLiveScaleDisposition Disposition,
    FourKayLaunchResult ActiveLaunch,
    bool OutputIsActive,
    string Message);

public sealed class FourKayLiveScaleAdjustmentException : InvalidOperationException
{
    public FourKayLiveScaleAdjustmentException(
        Exception adjustmentFailure,
        Exception rollbackFailure)
        : base(
            "The live scale could not be applied, and the previous game-window "
                + "geometry could not be reverified. Exact owned scaling cleanup is "
                + "required. Apply failure: "
                + adjustmentFailure.Message
                + " Rollback failure: "
                + rollbackFailure.Message,
            rollbackFailure)
    {
        AdjustmentFailure = adjustmentFailure;
        RollbackFailure = rollbackFailure;
    }

    public Exception AdjustmentFailure { get; }

    public Exception RollbackFailure { get; }
}

public sealed class FourKayAttachResizeException : InvalidOperationException
{
    public FourKayAttachResizeException(
        Exception attachFailure,
        Exception rollbackFailure)
        : base(
            "Attach could not continue, and the exact previous game-window "
                + "geometry could not be reverified. Any fullscreen scaling started "
                + "by this attempt was cleaned up before recovery was attempted. "
                + "Attach failure: "
                + attachFailure.Message
                + " Rollback failure: "
                + rollbackFailure.Message,
            rollbackFailure)
    {
        AttachFailure = attachFailure;
        RollbackFailure = rollbackFailure;
    }

    public Exception AttachFailure { get; }

    public Exception RollbackFailure { get; }
}

public sealed record MagpieFailureCleanupState(
    bool StopScalingReportedSuccess,
    bool InactiveAfterStop,
    bool AttemptOwnedEngine,
    bool ExactShutdownAttempted,
    bool ExactShutdownReportedSuccess,
    bool EngineAbsenceConfirmed,
    bool ConfigRollbackAttempted,
    bool ConfigRollbackReportedSuccess,
    bool ConfigRollbackResolved,
    bool InactiveAfterCleanup,
    IReadOnlyList<string> Issues);

public sealed record AttachWindowRecoveryState(
    ProcessDescriptor ExactProcess,
    string EqGamePath,
    WindowDescriptor OriginalSourceWindow,
    WindowGeometrySnapshot OriginalGeometry,
    TimeSpan WindowTimeout);

public sealed class MagpieScalingCleanupLease : IDisposable
{
    private bool _disposed;

    public MagpieScalingCleanupLease(
        string magpieDirectory,
        nint expectedSourceWindow,
        MagpiePortableConfigTransaction configTransaction,
        Process? exactProcessToken,
        bool ownsEngine,
        bool allowChangedContentRollback)
        : this(
            magpieDirectory,
            expectedSourceWindow,
            configTransaction,
            exactProcessToken,
            ownsEngine,
            allowChangedContentRollback,
            attachWindowRecovery: null)
    {
    }

    internal MagpieScalingCleanupLease(
        string magpieDirectory,
        nint expectedSourceWindow,
        MagpiePortableConfigTransaction configTransaction,
        Process? exactProcessToken,
        bool ownsEngine,
        bool allowChangedContentRollback,
        AttachWindowRecoveryState? attachWindowRecovery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magpieDirectory);
        ArgumentNullException.ThrowIfNull(configTransaction);
        if (ownsEngine && exactProcessToken is null)
        {
            throw new ArgumentException(
                "An owned cleanup lease requires its exact process token.",
                nameof(exactProcessToken));
        }

        if (allowChangedContentRollback && !ownsEngine)
        {
            throw new ArgumentException(
                "Changed config content may only be restored after an exact owned "
                    + "engine exit.",
                nameof(allowChangedContentRollback));
        }

        MagpieDirectory = Path.GetFullPath(magpieDirectory);
        ExpectedSourceWindow = expectedSourceWindow;
        ConfigTransaction = configTransaction;
        ExactProcessToken = exactProcessToken;
        OwnsEngine = ownsEngine;
        AllowChangedContentRollback = allowChangedContentRollback;
        AttachWindowRecovery = attachWindowRecovery;
    }

    public string MagpieDirectory { get; }

    public nint ExpectedSourceWindow { get; }

    public MagpiePortableConfigTransaction ConfigTransaction { get; }

    public Process? ExactProcessToken { get; }

    public bool OwnsEngine { get; }

    public bool AllowChangedContentRollback { get; }

    internal AttachWindowRecoveryState? AttachWindowRecovery { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ExactProcessToken?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed record MagpieScalingCleanupResolution(
    bool Resolved,
    string Message,
    MagpiePortableConfigRollbackDisposition? RollbackDisposition = null);

public sealed class MagpieScalingCleanupException : InvalidOperationException
{
    public MagpieScalingCleanupException(
        Exception startupFailure,
        MagpieFailureCleanupState cleanupState,
        MagpieScalingCleanupLease cleanupLease)
        : base(
            "Fullscreen scaling failed and its automatic cleanup could not be "
            + "fully confirmed. "
            + string.Join(" ", cleanupState.Issues),
            startupFailure)
    {
        StartupFailure = startupFailure;
        CleanupState = cleanupState;
        CleanupLease = cleanupLease;
    }

    public Exception StartupFailure { get; }

    public MagpieFailureCleanupState CleanupState { get; }

    public MagpieScalingCleanupLease CleanupLease { get; }

    public MagpiePortableConfigTransaction PendingConfigTransaction =>
        CleanupLease.ConfigTransaction;
}

public sealed class MultipleGameInstancesException : InvalidOperationException
{
    public MultipleGameInstancesException(string executablePath, IReadOnlyList<int> processIds)
        : base(
            $"Multiple exact '{Path.GetFileName(executablePath)}' processes are running. "
            + "Exit all but one before attaching.")
    {
        ExecutablePath = executablePath;
        ProcessIds = processIds;
    }

    public string ExecutablePath { get; }

    public IReadOnlyList<int> ProcessIds { get; }
}

public sealed class UnexpectedClientResolutionException : InvalidOperationException
{
    public UnexpectedClientResolutionException(PixelSize expected, PixelSize actual)
        : base(
            $"The running game client is {actual}, but the confirmed SpinUI layout "
            + $"expects {expected}. Restart with the selected source resolution "
            + "before attaching.")
    {
        Expected = expected;
        Actual = actual;
    }

    public PixelSize Expected { get; }

    public PixelSize Actual { get; }
}

public interface IFourKayLaunchService
{
    Task<FourKayLaunchResult> LaunchAndScaleAsync(
        FourKayLaunchRequest request,
        CancellationToken cancellationToken = default);

    Task<FourKayLaunchResult> AttachExistingAsync(
        FourKayAttachRequest request,
        CancellationToken cancellationToken = default);

    Task<FourKayLiveScaleResult> AdjustLiveScaleAsync(
        FourKayLiveScaleRequest request,
        CancellationToken cancellationToken = default);

    Task<MagpieScalingCleanupResolution> ResolveCleanupLeaseAsync(
        MagpieScalingCleanupLease lease,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<MagpieScalingCleanupResolution> StopExactOwnedSessionAsync(
        nint expectedSourceWindow,
        string magpieDirectory,
        Process exactProcessToken,
        TimeSpan timeout,
        AttachWindowRecoveryState? attachWindowRecovery = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts the official launcher or attaches to one exact existing client, places
/// the source on the chosen monitor, and refuses an unsafe mouse mapping.
/// </summary>
public sealed class FourKayLaunchService : IFourKayLaunchService
{
    private readonly IProcessDiscoveryService _processDiscovery;
    private readonly IWindowDiscoveryService _windowDiscovery;
    private readonly IWindowPlacementService _windowPlacement;
    private readonly IMagpiePortableConfigService _magpieConfig;
    private readonly IMagpieProcessService _magpieProcess;
    private readonly IMagpieScalingWindowInspector _scalingInspector;
    private readonly IEqClientConfigurationService _eqClientConfiguration;
    private readonly IForegroundWindowService _foregroundWindow;
    private readonly IOverlayCompatibilityService _overlayCompatibility;

    public FourKayLaunchService(
        IProcessDiscoveryService? processDiscovery = null,
        IWindowDiscoveryService? windowDiscovery = null,
        IWindowPlacementService? windowPlacement = null,
        IMagpiePortableConfigService? magpieConfig = null,
        IMagpieProcessService? magpieProcess = null,
        IMagpieScalingWindowInspector? scalingInspector = null,
        IEqClientConfigurationService? eqClientConfiguration = null,
        IForegroundWindowService? foregroundWindow = null,
        IOverlayCompatibilityService? overlayCompatibility = null)
    {
        _processDiscovery = processDiscovery ?? new ProcessDiscoveryService();
        _windowDiscovery = windowDiscovery ?? new WindowDiscoveryService();
        _windowPlacement = windowPlacement ?? new WindowPlacementService();
        _magpieConfig = magpieConfig ?? new MagpiePortableConfigService();
        _magpieProcess = magpieProcess ?? new MagpieProcessService();
        _scalingInspector = scalingInspector ?? new MagpieScalingWindowInspector();
        _eqClientConfiguration =
            eqClientConfiguration ?? new EqClientConfigurationService();
        _foregroundWindow =
            foregroundWindow ?? new ForegroundWindowService();
        _overlayCompatibility =
            overlayCompatibility ?? new OverlayCompatibilityService();
    }

    private static readonly TimeSpan MagpieProfileReloadShutdownTimeout =
        TimeSpan.FromSeconds(6);

    public async Task<FourKayLaunchResult> LaunchAndScaleAsync(
        FourKayLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateLaunchRequest(request);
        FourKayPreparedState state = request.PreparedState;
        await EnsureMagpieReadyAsync(request.MagpieDirectory, cancellationToken)
            .ConfigureAwait(false);
        await ValidatePreparedClientConfigurationAsync(
            state,
            request,
            cancellationToken)
            .ConfigureAwait(false);
        EnsureTargetMonitorMatchesPlan(state);

        IReadOnlyList<ProcessDescriptor> existing =
            _processDiscovery.FindByExecutablePath(state.EqGamePath);
        if (existing.Count > 0)
        {
            throw new GameAlreadyRunningException(
                state.EqGamePath,
                existing.Select(process => process.ProcessId).ToArray());
        }

        Process? launcher = null;
        Process? gameProcess = null;
        bool processWrappersTransferred = false;
        try
        {
            launcher = StartLauncher(request);
            ProcessDescriptor gameDescriptor =
                await WaitForManagedGameStartAsync(
                    request,
                    launcher,
                    cancellationToken).ConfigureAwait(false);
            gameProcess = GetLiveProcess(gameDescriptor.ProcessId);
            WindowDescriptor sourceWindow =
                await _windowDiscovery.WaitForStableVisibleWindowAsync(
                    gameProcess,
                    state.ResolutionPlan.SourceResolution,
                    request.WindowTimeout,
                    requiredStablePolls: 3,
                    cancellationToken).ConfigureAwait(false);

            // Sign-in can take minutes; re-verify the display topology before
            // the prepared window is moved onto the target monitor.
            EnsureTargetMonitorMatchesPlan(state);
            nint targetMonitor = new(state.TargetMonitorHandle);
            WindowPlacementResult placement = _windowPlacement.CenterFixedClientArea(
                sourceWindow.Handle,
                state.ResolutionPlan.SourceResolution,
                targetMonitor);
            EnsureSafePlacement(placement);
            // EverQuest can keep stale mouse-hit geometry briefly after its
            // top-level window settles. Require a full, bounded second of the
            // exact post-placement HWND and client size before Magpie starts.
            sourceWindow = await WindowInteractionReadinessPolicy
                .WaitForManagedLaunchAsync(
                    _windowDiscovery,
                    gameProcess,
                    state.ResolutionPlan.SourceResolution,
                    request.WindowTimeout,
                    cancellationToken).ConfigureAwait(false);
            await ValidatePreparedClientConfigurationAsync(
                state,
                request,
                cancellationToken).ConfigureAwait(false);

            FourKayLaunchResult result = await ConfigureAndStartScalingAsync(
                launcher,
                gameProcess,
                sourceWindow,
                placement,
                request.MagpieDirectory,
                state.ResolutionPlan.Filter,
                GetMagpieLauncherPath(request),
                request.RcasSharpness,
                request.AntiAliasing,
                request.MaximumFrameRate,
                request.GraphicsAdapter,
                request.DisableDirectFlip,
                request.MaintainTopmostOverlays,
                state.UiCompatibilityMode,
                request.ScalingTimeout,
                attachedToExistingGame: false,
                warnings: state.Warnings,
                finalConfigurationGuard: token =>
                    ValidatePreparedClientConfigurationAsync(
                        state,
                        request,
                        token),
                attachWindowRecovery: null,
                cancellationToken).ConfigureAwait(false);
            processWrappersTransferred = true;
            return result;
        }
        finally
        {
            if (!processWrappersTransferred)
            {
                gameProcess?.Dispose();
                launcher?.Dispose();
            }
        }
    }

    public async Task<FourKayLaunchResult> AttachExistingAsync(
        FourKayAttachRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateAttachRequest(request);
        await EnsureMagpieReadyAsync(request.MagpieDirectory, cancellationToken)
            .ConfigureAwait(false);

        string eqDirectory = Path.GetFullPath(request.EqDirectory);
        string eqGamePath = RequireFile(Path.Combine(eqDirectory, "eqgame.exe"));
        // Player configuration is strictly read-only for Attach. A non-native
        // saved UI scale becomes an advisory warning instead of a hard stop, and
        // eqclient.ini is never required, locked, or rewritten.
        string? nativeUiScaleAdvisory = await ReadNativeUiScaleAdvisoryAsync(
            Path.Combine(eqDirectory, "eqclient.ini"),
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProcessDescriptor> matches =
            _processDiscovery.FindByExecutablePath(eqGamePath);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                "EverQuest Legends is not running. Start the game normally, then "
                    + "scale the running window.");
        }

        if (matches.Count > 1)
        {
            throw new MultipleGameInstancesException(
                eqGamePath,
                matches.Select(process => process.ProcessId).ToArray());
        }

        ProcessDescriptor exactProcess = matches[0];
        if (request.ExpectedProcessId is int expectedProcessId
            && (exactProcess.ProcessId != expectedProcessId
                || exactProcess.StartTimeUtc
                    != request.ExpectedProcessStartTimeUtc))
        {
            throw new InvalidOperationException(
                "The exact EverQuest Legends process changed after automatic "
                    + "discovery. No game window was resized and no fullscreen "
                    + "scaling was started.");
        }

        Process? gameProcess = null;
        bool processWrapperTransferred = false;
        try
        {
            gameProcess = GetLiveProcess(exactProcess.ProcessId);
            WindowDescriptor sourceWindow =
                await _windowDiscovery.WaitForStableVisibleWindowAsync(
                    gameProcess,
                    request.WindowTimeout,
                    requiredStablePolls: 3,
                    cancellationToken).ConfigureAwait(false);
            WindowDescriptor originalSourceWindow = sourceWindow;
            PixelSize currentClientSize = new(
                sourceWindow.ClientBounds.Width,
                sourceWindow.ClientBounds.Height);
            if (request.ExpectedClientSize is { } expectedClientSize
                && currentClientSize != expectedClientSize)
            {
                throw new UnexpectedClientResolutionException(
                    expectedClientSize,
                    currentClientSize);
            }

            PixelSize requestedClientSize =
                request.RequestedGenericClientSize ?? currentClientSize;
            bool clientSizeChanged = requestedClientSize != currentClientSize;
            WindowGeometrySnapshot originalGeometry =
                _windowPlacement.CaptureWindowGeometry(sourceWindow.Handle);
            EnsureAttachGeometrySnapshotMatchesSource(
                originalGeometry,
                sourceWindow,
                currentClientSize);
            AttachWindowRecoveryState attachWindowRecovery = new(
                exactProcess,
                eqGamePath,
                sourceWindow,
                originalGeometry,
                request.WindowTimeout);
            bool placementAttempted = false;
            Exception? attachFailure = null;
            try
            {
                EnsureExactAttachProcess(exactProcess, eqGamePath);
                placementAttempted = true;
                WindowPlacementResult placement =
                    _windowPlacement.CenterFixedClientArea(
                        sourceWindow.Handle,
                        requestedClientSize,
                        request.TargetMonitor);
                EnsureSafePlacement(placement);
                sourceWindow =
                    await _windowDiscovery.WaitForStableVisibleWindowAsync(
                        gameProcess,
                        requestedClientSize,
                        request.WindowTimeout,
                        requiredStablePolls: 3,
                        cancellationToken).ConfigureAwait(false);
                EnsureExactAttachSourceIdentity(
                    originalSourceWindow,
                    sourceWindow,
                    exactProcess,
                    eqGamePath,
                    requestedClientSize);
                EnsureExactAttachProcess(exactProcess, eqGamePath);
                ScalingFilter effectiveFilter = request.Filter;
                List<string> attachWarnings =
                    clientSizeChanged
                        ?
                        [
                            "Attached without changing eqclient.ini. The exact running "
                                + $"client was resized from {currentClientSize} to "
                                + $"{requestedClientSize} for this generic/custom UI "
                                + "session; its saved resolution remains unchanged.",
                        ]
                        :
                        [
                            "Attached without changing eqclient.ini. The game is using "
                                + "its current window resolution.",
                        ];
                if (nativeUiScaleAdvisory is not null)
                {
                    attachWarnings.Add(nativeUiScaleAdvisory);
                }

                if (effectiveFilter == ScalingFilter.NearestNeighbor
                    && !IsExactIntegerScale(
                        requestedClientSize,
                        placement.Monitor.Bounds.Size))
                {
                    effectiveFilter = ScalingFilter.Nis;
                    attachWarnings.Add(
                        "Pixel Crisp requires an exact integer scale. The running "
                            + "window does not match one, so Attach safely used the "
                            + "Readable UI scaler for this session.");
                }

                FourKayLaunchResult result =
                    await ConfigureAndStartScalingAsync(
                        launcher: null,
                        gameProcess,
                        sourceWindow,
                        placement,
                        request.MagpieDirectory,
                        effectiveFilter,
                        launcherPath: null,
                        request.RcasSharpness,
                        request.AntiAliasing,
                        request.MaximumFrameRate,
                        request.GraphicsAdapter,
                        request.DisableDirectFlip,
                        request.MaintainTopmostOverlays,
                        request.UiCompatibilityMode,
                        request.ScalingTimeout,
                        attachedToExistingGame: true,
                        warnings: attachWarnings,
                        finalConfigurationGuard: _ => Task.CompletedTask,
                        attachWindowRecovery: attachWindowRecovery,
                        cancellationToken).ConfigureAwait(false);
                processWrapperTransferred = true;
                return result;
            }
            catch (Exception exception) when (
                placementAttempted
                && exception is not MagpieScalingCleanupException)
            {
                attachFailure = exception;
            }

            try
            {
                await RestoreExactAttachGeometryAsync(
                    gameProcess,
                    attachWindowRecovery).ConfigureAwait(false);
            }
            catch (Exception rollbackFailure)
            {
                throw new FourKayAttachResizeException(
                    attachFailure!,
                    rollbackFailure);
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(attachFailure!)
                .Throw();
            throw new UnreachableException();
        }
        finally
        {
            if (!processWrapperTransferred)
            {
                gameProcess?.Dispose();
            }
        }
    }

    public async Task<FourKayLiveScaleResult> AdjustLiveScaleAsync(
        FourKayLiveScaleRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateLiveScaleRequest(request);
        FourKayLaunchResult launch = request.ActiveLaunch;
        ResolutionPlan requestedPlan = request.RequestedPlan;
        PixelSize previousSize = launch.Placement.RequestedClientSize;
        PixelSize requestedSize = requestedPlan.SourceResolution;
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOriginalLiveMonitorAvailable(launch);
        _ = RequireExactLiveSourceWindow(
            launch,
            previousSize);
        EnsureExactLiveProcesses(launch);
        MagpieScalingWindowInspection currentOutput =
            _scalingInspector.Inspect(launch.SourceWindow.Handle);
        if (currentOutput.IsActive)
        {
            EnsureExactLiveOutput(
                launch,
                currentOutput,
                previousSize,
                launch.ExpectedDestinationRegion);
        }
        EnsureLiveSourceIsBackground(
            launch,
            currentOutput,
            "before pausing fullscreen output");
        if (requestedSize == previousSize)
        {
            LiveOutputPostcondition unchangedOutput =
                await ValidateLiveOutputPostconditionAsync(
                    launch,
                    previousSize,
                    launch.ExpectedDestinationRegion,
                    request.WindowTimeout,
                    cancellationToken).ConfigureAwait(false);
            return new FourKayLiveScaleResult(
                FourKayLiveScaleDisposition.Committed,
                launch with
                {
                    ScalingInspection = unchangedOutput.Inspection,
                },
                unchangedOutput.OutputIsActive,
                "The requested live scale already matches the active game window.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Exception? adjustmentFailure = null;
        try
        {
            if (currentOutput.IsActive)
            {
                bool stopped = await _scalingInspector.StopScalingAsync(
                    launch.SourceWindow.Handle,
                    launch.MagpieProcess.Process.Id,
                    TimeSpan.FromSeconds(4),
                    CancellationToken.None).ConfigureAwait(false);
                if (!stopped)
                {
                    throw new InvalidOperationException(
                        "The exact owned fullscreen output did not confirm its pause "
                            + "before the live resize.");
                }
            }

            MagpieScalingWindowInspection pausedOutput =
                _scalingInspector.Inspect(launch.SourceWindow.Handle);
            EnsureLiveOutputIsInactive(
                pausedOutput,
                "after pausing fullscreen output");
            EnsureLiveSourceIsBackground(
                launch,
                pausedOutput,
                "after pausing fullscreen output");
            EnsureOriginalLiveMonitorAvailable(launch);
            MagpieScalingWindowInspection placementOutput =
                _scalingInspector.Inspect(launch.SourceWindow.Handle);
            EnsureLiveOutputIsInactive(
                placementOutput,
                "immediately before resizing the game window");
            EnsureLiveSourceIsBackground(
                launch,
                placementOutput,
                "immediately before resizing the game window");

            WindowPlacementResult placement =
                _windowPlacement.CenterFixedClientArea(
                    launch.SourceWindow.Handle,
                    requestedSize,
                    launch.Placement.Monitor.Handle);
            EnsureSafePlacement(placement);
            WindowDescriptor resizedSource =
                await _windowDiscovery.WaitForStableVisibleWindowAsync(
                    launch.GameProcess,
                    requestedSize,
                    request.WindowTimeout,
                    requiredStablePolls: 3,
                    CancellationToken.None).ConfigureAwait(false);
            EnsureExactLiveSourceIdentity(
                launch,
                resizedSource,
                requestedSize,
                placement.Monitor);
            EnsureExactLiveProcesses(launch);

            PixelRect expectedDestination = OffsetDestinationToMonitor(
                requestedPlan.DestinationContent,
                placement.Monitor.Bounds);
            FourKayLaunchResult updated = launch with
            {
                SourceWindow = resizedSource,
                Placement = placement,
                ExpectedDestinationRegion = expectedDestination,
            };
            LiveOutputPostcondition output =
                await ValidateLiveOutputPostconditionAsync(
                    updated,
                    requestedSize,
                    expectedDestination,
                    request.WindowTimeout,
                    CancellationToken.None).ConfigureAwait(false);
            updated = updated with
            {
                ScalingInspection = output.Inspection,
            };
            return new FourKayLiveScaleResult(
                FourKayLiveScaleDisposition.Committed,
                updated,
                output.OutputIsActive,
                $"The exact Legends client is now {requestedSize}. Return to the "
                    + "game to restore and verify borderless fullscreen.");
        }
        catch (Exception exception) when (IsNonFatalLiveScaleFailure(exception))
        {
            adjustmentFailure = exception;
        }

        try
        {
            LiveScaleRecovery recovered =
                await RestorePreviousLiveScaleAsync(
                    launch,
                    request.WindowTimeout).ConfigureAwait(false);
            return new FourKayLiveScaleResult(
                FourKayLiveScaleDisposition.RecoveredPrevious,
                recovered.ActiveLaunch,
                recovered.OutputIsActive,
                "The requested live scale was not accepted, so the previous verified "
                    + $"window size was restored. Reason: {adjustmentFailure!.Message}");
        }
        catch (Exception rollbackFailure) when (
            IsNonFatalLiveScaleFailure(rollbackFailure))
        {
            throw new FourKayLiveScaleAdjustmentException(
                adjustmentFailure!,
                rollbackFailure);
        }
    }

    public async Task<MagpieScalingCleanupResolution> ResolveCleanupLeaseAsync(
        MagpieScalingCleanupLease lease,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Cleanup timeout must be positive.");
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        string lastIssue = lease.OwnsEngine
            ? "The exact scaling engine started by this attempt has not yet "
                + "confirmed that it exited."
            : "A scaling engine this attempt did not start is still active. It "
                + "was left untouched while SpinFOURKAYYY waits for it to exit.";

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MagpieScalingWindowInspection inspection =
                _scalingInspector.Inspect(lease.ExpectedSourceWindow);
            IReadOnlyList<MagpieRunningInstance> instances =
                _magpieProcess.InspectRunningInstances(lease.MagpieDirectory);

            if (!lease.OwnsEngine)
            {
                bool observedProcessExited =
                    lease.ExactProcessToken is null
                    || HasProcessExited(lease.ExactProcessToken);
                if (!observedProcessExited || instances.Count > 0 || inspection.IsActive)
                {
                    lastIssue =
                        "A Magpie process or fullscreen output this attempt did not "
                        + "start is still active. SpinFOURKAYYY left it untouched; "
                        + "close that Magpie instance to finish safe config cleanup.";
                    await DelayCleanupRetryAsync(deadline, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                MagpieScalingCleanupResolution? resolved =
                    await TryResolveLeaseConfigAsync(
                        lease,
                        allowChangedContentRollback: false,
                        cancellationToken).ConfigureAwait(false);
                if (resolved is not null)
                {
                    return await CompleteResolvedCleanupLeaseAsync(
                        lease,
                        resolved).ConfigureAwait(false);
                }

                lastIssue =
                    "The newer portable config was kept, but its safe state could "
                    + "not yet be confirmed.";
                await DelayCleanupRetryAsync(deadline, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            Process exactProcess = lease.ExactProcessToken
                ?? throw new InvalidOperationException(
                    "The owned cleanup lease lost its exact process token.");
            bool exactExitConfirmed = HasProcessExited(exactProcess);
            if (!exactExitConfirmed
                && inspection.IsActive
                && inspection.ScalingProcessId == exactProcess.Id
                && (inspection.SourceWindowHandle == nint.Zero
                    || inspection.SourceWindowHandle == lease.ExpectedSourceWindow))
            {
                _ = await _scalingInspector.StopScalingAsync(
                    lease.ExpectedSourceWindow,
                    expectedScalingProcessId: exactProcess.Id,
                    timeout: TimeSpan.FromSeconds(4),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!exactExitConfirmed)
            {
                TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    TimeSpan shutdownBudget = remaining < TimeSpan.FromSeconds(4)
                        ? remaining
                        : TimeSpan.FromSeconds(4);
                    exactExitConfirmed = await _magpieProcess.ShutdownExactAsync(
                        lease.MagpieDirectory,
                        exactProcess,
                        shutdownBudget,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            IReadOnlyList<MagpieRunningInstance> remainingInstances =
                _magpieProcess.InspectRunningInstances(lease.MagpieDirectory);
            MagpieScalingWindowInspection finalInspection =
                _scalingInspector.Inspect(lease.ExpectedSourceWindow);
            if (exactExitConfirmed
                && remainingInstances.Count == 0
                && !finalInspection.IsActive)
            {
                MagpieScalingCleanupResolution? resolved =
                    await TryResolveLeaseConfigAsync(
                        lease,
                        lease.AllowChangedContentRollback,
                        cancellationToken).ConfigureAwait(false);
                if (resolved is not null)
                {
                    return await CompleteResolvedCleanupLeaseAsync(
                        lease,
                        resolved).ConfigureAwait(false);
                }

                lastIssue =
                    "The exact owned engine exited, but restoring its pre-write "
                    + "config snapshot still needs another retry.";
            }
            else
            {
                lastIssue =
                    "The exact owned engine, all Magpie processes, and fullscreen "
                    + "output have not all confirmed inactive.";
            }

            await DelayCleanupRetryAsync(deadline, cancellationToken)
                .ConfigureAwait(false);
        }

        return new MagpieScalingCleanupResolution(
            Resolved: false,
            lastIssue);
    }

    public async Task<MagpieScalingCleanupResolution> StopExactOwnedSessionAsync(
        nint expectedSourceWindow,
        string magpieDirectory,
        Process exactProcessToken,
        TimeSpan timeout,
        AttachWindowRecoveryState? attachWindowRecovery = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magpieDirectory);
        ArgumentNullException.ThrowIfNull(exactProcessToken);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Cleanup timeout must be positive.");
        }

        string fullDirectory = Path.GetFullPath(magpieDirectory);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        string lastIssue =
            "The exact scaling engine started by this session has not yet "
            + "confirmed that it exited.";

        Task<MagpieScalingCleanupResolution> CompleteStopAsync(string message) =>
            CompleteAttachWindowRecoveryAsync(
                attachWindowRecovery,
                new MagpieScalingCleanupResolution(
                    Resolved: true,
                    message));

        (bool IsClear, string Message) InspectPostExitState()
        {
            IReadOnlyList<MagpieRunningInstance> remainingInstances =
                _magpieProcess.InspectRunningInstances(fullDirectory);
            MagpieScalingWindowInspection remainingOutput =
                _scalingInspector.Inspect(expectedSourceWindow);
            if (remainingInstances.Count == 0 && !remainingOutput.IsActive)
            {
                return (
                    true,
                    "No replacement Magpie process or fullscreen output remains.");
            }

            List<string> conflicts = [];
            if (remainingInstances.Count > 0)
            {
                conflicts.Add(
                    $"{remainingInstances.Count} replacement or foreign Magpie "
                        + "process instance(s) remain");
            }

            if (remainingOutput.IsActive)
            {
                conflicts.Add(
                    "a replacement or foreign fullscreen output remains active");
            }

            return (
                false,
                "The exact engine owned by this session exited, but "
                    + string.Join(" and ", conflicts)
                    + ". They were left untouched. Close the other Magpie instance "
                    + "manually before cleanup can be confirmed.");
        }

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasProcessExited(exactProcessToken))
            {
                (bool isClear, string message) = InspectPostExitState();
                if (isClear)
                {
                    return await CompleteStopAsync(
                        "The exact engine owned by this session has exited, and no "
                            + "replacement process or output remains.")
                        .ConfigureAwait(false);
                }

                lastIssue = message;
                await DelayCleanupRetryAsync(deadline, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            MagpieScalingWindowInspection inspection =
                _scalingInspector.Inspect(expectedSourceWindow);
            TimeSpan stopRemaining = deadline - DateTimeOffset.UtcNow;
            if (stopRemaining > TimeSpan.Zero
                && !HasProcessExited(exactProcessToken)
                && inspection.IsActive
                && inspection.ScalingProcessId == exactProcessToken.Id
                && (inspection.SourceWindowHandle == nint.Zero
                    || inspection.SourceWindowHandle == expectedSourceWindow))
            {
                TimeSpan stopBudget = stopRemaining < TimeSpan.FromSeconds(4)
                    ? stopRemaining
                    : TimeSpan.FromSeconds(4);
                _ = await _scalingInspector.StopScalingAsync(
                    expectedSourceWindow,
                    expectedScalingProcessId: exactProcessToken.Id,
                    timeout: stopBudget,
                    cancellationToken).ConfigureAwait(false);
            }

            if (HasProcessExited(exactProcessToken))
            {
                (bool isClear, string message) = InspectPostExitState();
                if (isClear)
                {
                    return await CompleteStopAsync(
                        "The exact engine owned by this session exited after its "
                            + "fullscreen output stopped, with no replacement left.")
                        .ConfigureAwait(false);
                }

                lastIssue = message;
                await DelayCleanupRetryAsync(deadline, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            TimeSpan shutdownBudget = remaining < TimeSpan.FromSeconds(4)
                ? remaining
                : TimeSpan.FromSeconds(4);
            bool exactShutdown = await _magpieProcess.ShutdownExactAsync(
                fullDirectory,
                exactProcessToken,
                shutdownBudget,
                cancellationToken).ConfigureAwait(false);
            if (exactShutdown || HasProcessExited(exactProcessToken))
            {
                (bool isClear, string message) = InspectPostExitState();
                if (isClear)
                {
                    return await CompleteStopAsync(
                        "Fullscreen and the exact scaling engine owned by this session "
                            + "are stopped, with no replacement process or output left.")
                        .ConfigureAwait(false);
                }

                lastIssue = message;
                await DelayCleanupRetryAsync(deadline, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            lastIssue =
                "The exact owned process could not be shut down without claiming "
                + "another Magpie instance; no path-based shutdown was attempted.";
            await DelayCleanupRetryAsync(deadline, cancellationToken)
                .ConfigureAwait(false);
        }

        return new MagpieScalingCleanupResolution(
            Resolved: false,
            lastIssue);
    }

    private async Task<FourKayLaunchResult> ConfigureAndStartScalingAsync(
        Process? launcher,
        Process gameProcess,
        WindowDescriptor sourceWindow,
        WindowPlacementResult placement,
        string magpieDirectory,
        ScalingFilter filter,
        string? launcherPath,
        double rcasSharpness,
        AntiAliasingMode antiAliasing,
        double? maximumFrameRate,
        MagpieGraphicsAdapter graphicsAdapter,
        bool disableDirectFlip,
        bool maintainTopmostOverlays,
        FourKayUiCompatibilityMode uiCompatibilityMode,
        TimeSpan scalingTimeout,
        bool attachedToExistingGame,
        IReadOnlyList<string> warnings,
        Func<CancellationToken, Task> finalConfigurationGuard,
        AttachWindowRecoveryState? attachWindowRecovery,
        CancellationToken cancellationToken)
    {
        string exactExecutablePath = ProcessDiscoveryService.TryGetExecutablePath(gameProcess.Id)
            ?? throw new InvalidOperationException(
                "Windows did not expose the exact running game executable path.");

        MagpieProfileRequest profileRequest =
            new()
            {
                MagpieDirectory = magpieDirectory,
                SourceExecutablePath = exactExecutablePath,
                SourceWindowClass = sourceWindow.ClassName,
                LauncherPath = launcherPath,
                Filter = filter,
                RcasSharpness = rcasSharpness,
                UiScaleFactor = Math.Min(
                    (double)placement.Monitor.Bounds.Width
                        / placement.RequestedClientSize.Width,
                    (double)placement.Monitor.Bounds.Height
                        / placement.RequestedClientSize.Height),
                AntiAliasing = antiAliasing,
                NativeClarityOnly =
                    placement.RequestedClientSize
                        == placement.Monitor.Bounds.Size,
                // The dedicated profile binds automatic startup to the exact
                // eqgame.exe path and window class. This is source-specific;
                // the stock foreground hotkey/toggle is never used.
                AutoScaleFullscreen = true,
                // Normal mode keeps the same fullscreen surface and coordinate
                // map alive across Alt+Tab; 3D-game mode intentionally destroys
                // it when another window overlaps the game.
                ThreeDGameMode = false,
                DisableDirectFlip = disableDirectFlip,
                CaptureTitleBar = false,
                AdjustCursorSpeed = true,
                GraphicsAdapter = graphicsAdapter,
                MaximumFrameRate = maximumFrameRate,
            };
        // The earlier readiness check can precede launcher sign-in by many minutes.
        // Stop and confirm any old dedicated process before snapshotting a file
        // that Magpie itself can save during shutdown.
        await EnsureMagpieReadyAsync(magpieDirectory, cancellationToken)
            .ConfigureAwait(false);
        MagpiePortableConfigTransaction transaction =
            await _magpieConfig.PrepareTransactionAsync(
                profileRequest,
                cancellationToken).ConfigureAwait(false);
        EnsureNoMagpieInstances(magpieDirectory);

        MagpieProcessStartResult? magpie = null;
        OverlayCompatibilitySession? overlaySession = null;
        try
        {
            await finalConfigurationGuard(cancellationToken).ConfigureAwait(false);
            MagpiePortableConfigResult config =
                await _magpieConfig.ApplyTransactionAsync(
                    transaction,
                    cancellationToken).ConfigureAwait(false);

            List<string> runtimeWarnings = warnings.ToList();
            const string focusGuidance =
                "Windows blocked automatic focus. Click the EverQuest window once, "
                + "then retry fullscreen scaling.";
            bool focusSucceeded = _windowDiscovery.BringToForeground(sourceWindow);
            if (!focusSucceeded)
            {
                runtimeWarnings.Add(focusGuidance);
            }

            if (maintainTopmostOverlays)
            {
                if (focusSucceeded)
                {
                    // Companion overlays commonly update their topmost state on a
                    // short UI timer after EQ becomes foreground. Give that state a
                    // bounded moment to settle before Magpie becomes foreground.
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(300),
                        cancellationToken).ConfigureAwait(false);
                }

                HashSet<int> excludedProcessIds =
                [
                    Environment.ProcessId,
                    gameProcess.Id,
                ];
                if (launcher is not null)
                {
                    excludedProcessIds.Add(launcher.Id);
                }

                overlaySession = _overlayCompatibility.Capture(
                    new OverlayCompatibilityCaptureRequest
                    {
                        SourceWindowHandle = sourceWindow.Handle,
                        SourceProcessId = gameProcess.Id,
                        SourceRegion = sourceWindow.ClientBounds,
                        TargetRegion = placement.Monitor.Bounds,
                        ExcludedProcessIds = excludedProcessIds,
                    });
                runtimeWarnings.AddRange(overlaySession.Warnings);
            }

            magpie = _magpieProcess.StartPortable(
                magpieDirectory,
                startInTray: true);
            if (magpie.AlreadyRunning)
            {
                throw new DedicatedMagpieConfigReloadRequiredException();
            }

            MagpieScalingWindowInspection inspection =
                await _scalingInspector.WaitForSafeAttachmentAsync(
                    sourceWindow.Handle,
                    scalingTimeout,
                    cancellationToken).ConfigureAwait(false);

            List<string> attachmentIssues = [];
            // When no scaling window exists, its owner and geometry are naturally
            // unavailable. Reporting those as three additional mismatches hid the
            // useful root failure, such as a missing shader. Validate these exact
            // details only after Magpie has created an active output.
            if (inspection.IsActive
                && inspection.ScalingProcessId != magpie.Process.Id)
            {
                attachmentIssues.Add(
                    "Magpie's fullscreen window is not owned by the exact dedicated "
                        + "process started for this session.");
            }

            if (inspection.IsActive
                && (inspection.MonitorBounds is not { } actualMonitor
                    || actualMonitor != placement.Monitor.Bounds))
            {
                attachmentIssues.Add(
                    "Magpie filled a different monitor than the selected target.");
            }

            if (inspection.IsActive
                && (inspection.SourceRegion is not { } actualSource
                    || Math.Abs(
                        actualSource.Width - placement.RequestedClientSize.Width) > 2
                    || Math.Abs(
                        actualSource.Height - placement.RequestedClientSize.Height) > 2))
            {
                attachmentIssues.Add(
                    "Magpie's physical source region does not match the requested "
                        + "Legends client resolution.");
            }

            if (attachmentIssues.Count > 0)
            {
                inspection = inspection with
                {
                    IsSafeForInput = false,
                    Issues = inspection.Issues
                        .Concat(attachmentIssues)
                        .ToArray(),
                };
            }

            if (!inspection.IsSafeForInput)
            {
                if (!inspection.IsActive)
                {
                    string logPath = Path.Combine(
                        Path.GetFullPath(magpieDirectory),
                        "logs",
                        "magpie.log");
                    string engineState = HasProcessExited(magpie.Process)
                        ? "The exact dedicated Magpie process exited before it "
                            + "created a scaling surface."
                        : "The exact dedicated Magpie process remained open but "
                            + "did not create a scaling surface.";
                    inspection = inspection with
                    {
                        Issues = inspection.Issues
                            .Append($"{engineState} Engine log: {logPath}")
                            .ToArray(),
                    };
                }

                if (!focusSucceeded)
                {
                    inspection = inspection with
                    {
                        Issues = inspection.Issues.Append(focusGuidance).ToArray(),
                    };
                }

                throw new UnsafeScalingAttachmentException(inspection);
            }

            await finalConfigurationGuard(cancellationToken).ConfigureAwait(false);

            PixelRect destinationRegion = inspection.DestinationRegion
                ?? throw new InvalidOperationException(
                    "The verified scaling output has no destination region.");
            if (overlaySession is not null)
            {
                OverlayCompatibilityUpdate overlayUpdate =
                    _overlayCompatibility.Activate(
                        overlaySession,
                        inspection.ScalingWindowHandle,
                        magpie.Process.Id,
                        destinationRegion);
                runtimeWarnings.AddRange(overlayUpdate.Warnings);
                runtimeWarnings = runtimeWarnings
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }

            return new FourKayLaunchResult(
                launcher,
                gameProcess,
                sourceWindow,
                placement,
                filter,
                uiCompatibilityMode,
                destinationRegion,
                Path.GetFullPath(magpieDirectory),
                config,
                magpie,
                inspection,
                attachedToExistingGame,
                runtimeWarnings)
            {
                AttachWindowRecovery = attachWindowRecovery,
                OverlayCompatibility = overlaySession,
            };
        }
        catch (Exception startupFailure)
        {
            MagpieFailureCleanupState cleanup = await CleanupFailedScalingAsync(
                sourceWindow.Handle,
                magpieDirectory,
                magpie,
                transaction).ConfigureAwait(false);
            if (overlaySession is not null)
            {
                _ = _overlayCompatibility.Restore(overlaySession);
            }
            if (!cleanup.EngineAbsenceConfirmed
                || !cleanup.ConfigRollbackResolved
                || !cleanup.InactiveAfterCleanup)
            {
                MagpieScalingCleanupLease lease = new(
                    magpieDirectory,
                    sourceWindow.Handle,
                    transaction,
                    magpie?.Process,
                    cleanup.AttemptOwnedEngine,
                    allowChangedContentRollback: cleanup.AttemptOwnedEngine,
                    attachWindowRecovery: attachWindowRecovery);
                throw new MagpieScalingCleanupException(
                    startupFailure,
                    cleanup,
                    lease);
            }

            magpie?.Process.Dispose();
            throw;
        }
    }

    private async Task<MagpieScalingCleanupResolution?> TryResolveLeaseConfigAsync(
        MagpieScalingCleanupLease lease,
        bool allowChangedContentRollback,
        CancellationToken cancellationToken)
    {
        MagpiePortableConfigRollbackResult rollback =
            await _magpieConfig.RollbackTransactionAsync(
                lease.ConfigTransaction,
                allowChangedContentRollback,
                cancellationToken).ConfigureAwait(false);
        if (!rollback.Resolved)
        {
            return null;
        }

        string message = rollback.Disposition switch
        {
            MagpiePortableConfigRollbackDisposition.Restored =>
                rollback.Issue
                ?? "The exact scaling engine is inactive and its pre-write portable "
                    + "config snapshot was restored.",
            MagpiePortableConfigRollbackDisposition.NewerContentPreserved =>
                rollback.Issue
                ?? "A newer portable config was preserved without being overwritten.",
            _ => throw new InvalidOperationException(
                "An unresolved rollback disposition was marked resolved."),
        };
        return new MagpieScalingCleanupResolution(
            Resolved: true,
            message,
            rollback.Disposition);
    }

    private async Task<MagpieScalingCleanupResolution>
        CompleteResolvedCleanupLeaseAsync(
            MagpieScalingCleanupLease lease,
            MagpieScalingCleanupResolution configResolution)
    {
        return await CompleteAttachWindowRecoveryAsync(
            lease.AttachWindowRecovery,
            configResolution).ConfigureAwait(false);
    }

    private async Task<MagpieScalingCleanupResolution>
        CompleteAttachWindowRecoveryAsync(
            AttachWindowRecoveryState? recovery,
            MagpieScalingCleanupResolution completedResolution)
    {
        if (recovery is null)
        {
            return completedResolution;
        }

        try
        {
            if (!IsOriginalAttachRecoveryTargetPresent(recovery))
            {
                return CompleteCleanupWithoutOriginalAttachWindow(
                    completedResolution);
            }

            using Process gameProcess = GetLiveProcess(
                recovery.ExactProcess.ProcessId);
            await RestoreExactAttachGeometryAsync(gameProcess, recovery)
                .ConfigureAwait(false);
            return completedResolution with
            {
                Message =
                    completedResolution.Message
                    + " The exact pre-Attach game-window geometry and show state "
                    + "were also restored.",
            };
        }
        catch (Exception exception) when (
            IsNonFatalLiveScaleFailure(exception))
        {
            // The original process can exit after the pre-restore identity check.
            // Reinspect before retaining the lease so PID reuse can never redirect
            // recovery toward a replacement Legends client.
            try
            {
                if (!IsOriginalAttachRecoveryTargetPresent(recovery))
                {
                    return CompleteCleanupWithoutOriginalAttachWindow(
                        completedResolution);
                }
            }
            catch (Exception identityException) when (
                IsNonFatalLiveScaleFailure(identityException))
            {
                // Preserve the original recovery failure below. A failed read-only
                // identity recheck is not evidence that the target exited.
            }

            return new MagpieScalingCleanupResolution(
                Resolved: false,
                completedResolution.Message
                    + " Scaling-engine, output, and config cleanup are confirmed, "
                    + "but the exact pre-Attach game-window geometry or show state "
                    + "could not yet be restored: "
                    + exception.Message,
                completedResolution.RollbackDisposition);
        }
    }

    private bool IsOriginalAttachRecoveryTargetPresent(
        AttachWindowRecoveryState recovery)
    {
        IReadOnlyList<ProcessDescriptor> matches =
            _processDiscovery.FindByExecutablePath(recovery.EqGamePath);
        bool originalProcessPresent = matches.Any(
            match => IsSameAttachProcessIdentity(
                recovery.ExactProcess,
                match,
                recovery.EqGamePath));
        if (!originalProcessPresent)
        {
            return false;
        }

        WindowDescriptor original = recovery.OriginalSourceWindow;
        return _windowDiscovery.FindVisibleWindows(
                recovery.ExactProcess.ProcessId)
            .Any(
                window =>
                    window.Handle == original.Handle
                    && window.ProcessId == recovery.ExactProcess.ProcessId
                    && string.Equals(
                        window.ClassName,
                        original.ClassName,
                        StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(window.ExecutablePath)
                    && string.Equals(
                        Path.GetFullPath(window.ExecutablePath),
                        Path.GetFullPath(recovery.EqGamePath),
                        StringComparison.OrdinalIgnoreCase));
    }

    private static MagpieScalingCleanupResolution
        CompleteCleanupWithoutOriginalAttachWindow(
            MagpieScalingCleanupResolution configResolution)
    {
        return configResolution with
        {
            Message =
                configResolution.Message
                + " The original EverQuest Legends process has exited or was "
                + "replaced, so no live original window remains to restore. No "
                + "replacement process or window was touched.",
        };
    }

    private static bool HasProcessExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task DelayCleanupRetryAsync(
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(
            remaining < TimeSpan.FromMilliseconds(100)
                ? remaining
                : TimeSpan.FromMilliseconds(100),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<MagpieFailureCleanupState> CleanupFailedScalingAsync(
        nint sourceWindow,
        string magpieDirectory,
        MagpieProcessStartResult? magpie,
        MagpiePortableConfigTransaction transaction)
    {
        List<string> issues = [];
        bool attemptOwnedEngine = magpie is { AlreadyRunning: false };
        bool stopReportedSuccess = false;
        if (attemptOwnedEngine)
        {
            try
            {
                stopReportedSuccess = await _scalingInspector.StopScalingAsync(
                    sourceWindow,
                    expectedScalingProcessId: magpie!.Process.Id,
                    timeout: TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
                if (!stopReportedSuccess)
                {
                    issues.Add(
                        "Magpie did not confirm that the scaling window stopped normally.");
                }
            }
            catch (Exception exception)
            {
                issues.Add(
                    "Stopping the scaling window raised an error: "
                    + exception.Message);
            }
        }

        bool inactiveAfterStop = ConfirmScalingInactive(sourceWindow, issues);
        if (attemptOwnedEngine && stopReportedSuccess && !inactiveAfterStop)
        {
            issues.Add(
                "Magpie reported a normal stop, but fullscreen scaling remained "
                + "active or could not be confirmed inactive.");
        }
        else if (!attemptOwnedEngine && !inactiveAfterStop)
        {
            issues.Add(
                "A scaling output is active, but this attempt did not start its "
                    + "engine. It was left untouched.");
        }

        bool exactShutdownAttempted = attemptOwnedEngine;
        bool exactShutdownReportedSuccess = false;
        if (attemptOwnedEngine)
        {
            try
            {
                exactShutdownReportedSuccess = await _magpieProcess.ShutdownExactAsync(
                    magpieDirectory,
                    magpie!.Process,
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None).ConfigureAwait(false);
                if (!exactShutdownReportedSuccess)
                {
                    issues.Add(
                        "The exact bundled Magpie process started by this attempt did "
                            + "not confirm a graceful shutdown.");
                }
            }
            catch (Exception exception)
            {
                issues.Add(
                    "Gracefully shutting down the owned bundled Magpie process "
                    + "raised an error: "
                    + exception.Message);
            }
        }
        else if (magpie is { AlreadyRunning: true })
        {
            issues.Add(
                "A bundled Magpie process appeared between the pre-write check and "
                    + "startup. It was not started by this attempt, so config rollback "
                    + "is pending until that process exits.");
        }

        bool engineAbsenceConfirmed = false;
        try
        {
            IReadOnlyList<MagpieRunningInstance> remaining =
                _magpieProcess.InspectRunningInstances(magpieDirectory);
            bool observedUnownedProcessExited =
                magpie is not { AlreadyRunning: true }
                || HasProcessExited(magpie.Process);
            engineAbsenceConfirmed =
                remaining.Count == 0
                && (!attemptOwnedEngine || exactShutdownReportedSuccess)
                && observedUnownedProcessExited;
            if (!engineAbsenceConfirmed)
            {
                issues.Add(
                    "A Magpie process is still running, so restoring its pre-write "
                        + "config snapshot was withheld.");
            }
        }
        catch (Exception exception)
        {
            issues.Add(
                "Confirming that the scaling engine exited raised an error: "
                    + exception.Message);
        }

        bool inactiveAfterCleanup = ConfirmScalingInactive(sourceWindow, issues);
        if (!inactiveAfterCleanup)
        {
            issues.Add(
                "Fullscreen scaling is still active or its inactive state could "
                + "not be confirmed. Close the bundled Magpie from its tray icon.");
        }

        bool rollbackAttempted = engineAbsenceConfirmed && inactiveAfterCleanup;
        bool rollbackReportedSuccess = false;
        bool rollbackResolved = false;
        if (rollbackAttempted)
        {
            MagpiePortableConfigRollbackResult rollback =
                await _magpieConfig.RollbackTransactionAsync(
                    transaction,
                    allowChangedContentAfterEngineExit: attemptOwnedEngine,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            rollbackReportedSuccess = rollback.Restored;
            rollbackResolved = rollback.Resolved;
            if (rollback.Issue is not null)
            {
                issues.Add(rollback.Issue);
            }
            else if (!rollback.Restored)
            {
                issues.Add("The pre-write Magpie config snapshot could not be restored.");
            }
        }

        return new MagpieFailureCleanupState(
            stopReportedSuccess,
            inactiveAfterStop,
            attemptOwnedEngine,
            exactShutdownAttempted,
            exactShutdownReportedSuccess,
            engineAbsenceConfirmed,
            rollbackAttempted,
            rollbackReportedSuccess,
            rollbackResolved,
            inactiveAfterCleanup,
            issues);
    }

    private bool ConfirmScalingInactive(nint sourceWindow, List<string> issues)
    {
        try
        {
            return !_scalingInspector.Inspect(sourceWindow).IsActive;
        }
        catch (Exception exception)
        {
            issues.Add(
                "Confirming that fullscreen scaling is inactive raised an error: "
                + exception.Message);
            return false;
        }
    }

    /// <summary>
    /// Ensures no scaling engine is holding a stale profile before this
    /// operation writes a new one.
    /// </summary>
    /// <remarks>
    /// Magpie reads its portable profile only when it starts, so a size or
    /// quality change cannot reach an instance that is already running. Once a
    /// foreign Magpie is ruled out, everything left belongs to this application:
    /// its own dedicated engine, in its own folder, running a profile this
    /// application wrote. Restarting it is this application's own business, so
    /// it is stopped here rather than asking the player to close a process they
    /// never started. The engine is asked to quit through its own message, and
    /// its absence is confirmed before the caller writes the new profile.
    /// </remarks>
    private async Task EnsureMagpieReadyAsync(
        string magpieDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MagpieRunningInstance> instances =
            _magpieProcess.InspectRunningInstances(magpieDirectory);
        MagpieRunningInstance[] external = instances
            .Where(instance => instance.IsForeignInstance)
            .ToArray();
        if (external.Length > 0)
        {
            throw new ExternalMagpieInstanceConflictException(external);
        }

        if (instances.Count == 0)
        {
            return;
        }

        if (!await _magpieProcess.ShutdownBundledAsync(
                magpieDirectory,
                MagpieProfileReloadShutdownTimeout,
                cancellationToken).ConfigureAwait(false))
        {
            throw new DedicatedMagpieConfigReloadRequiredException();
        }

        // Confirm the engine is gone rather than trusting that it was asked to
        // leave. Magpie can write its own profile while shutting down, so the
        // caller must not snapshot that file until the process is really absent.
        if (_magpieProcess.InspectRunningInstances(magpieDirectory).Count > 0)
        {
            throw new DedicatedMagpieConfigReloadRequiredException();
        }
    }

    private void EnsureNoMagpieInstances(string magpieDirectory)
    {
        IReadOnlyList<MagpieRunningInstance> instances =
            _magpieProcess.InspectRunningInstances(magpieDirectory);
        MagpieRunningInstance[] external = instances
            .Where(instance => instance.IsForeignInstance)
            .ToArray();
        if (external.Length > 0)
        {
            throw new ExternalMagpieInstanceConflictException(external);
        }

        if (instances.Count > 0)
        {
            throw new DedicatedMagpieConfigReloadRequiredException();
        }
    }

    private static Process StartLauncher(FourKayLaunchRequest request)
    {
        string launcherPath = RequireFile(request.LauncherPath);
        ProcessStartInfo startInfo = new()
        {
            FileName = launcherPath,
            WorkingDirectory = Path.GetDirectoryName(launcherPath),
            UseShellExecute = true,
        };
        foreach (string argument in request.LauncherArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Windows did not start the selected EverQuest launch application.");
    }

    private async Task<ProcessDescriptor> WaitForManagedGameStartAsync(
        FourKayLaunchRequest request,
        Process launchProcess,
        CancellationToken cancellationToken)
    {
        if (request.StartMode == FourKayGameStartMode.OfficialLauncher)
        {
            return await _processDiscovery.WaitForExecutableAsync(
                request.PreparedState.EqGamePath,
                request.GameStartTimeout,
                cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + request.GameStartTimeout;
        int exitedWrapperGracePolls = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ProcessDescriptor> matches =
                _processDiscovery.FindByExecutablePath(
                    request.PreparedState.EqGamePath);
            if (matches.Count > 0)
            {
                return matches[0];
            }

            if (launchProcess.HasExited)
            {
                exitedWrapperGracePolls++;
                if (exitedWrapperGracePolls >= 5)
                {
                    throw new InvalidOperationException(
                        "SpinTexture did not start Enhanced EverQuest. Review any "
                            + "SpinTexture message, reopen SpinTexture to verify that "
                            + "the enhanced pack is active, then try Play Enhanced EQ "
                            + "again. Your prepared UI layout will be recovered safely.");
                }
            }
            else
            {
                exitedWrapperGracePolls = 0;
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            TimeSpan delay = remaining < TimeSpan.FromMilliseconds(150)
                ? remaining
                : TimeSpan.FromMilliseconds(150);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Timed out waiting for Enhanced EverQuest to start through SpinTexture. "
                + "Review SpinTexture's pack status and try again.");
    }

    private static Process GetLiveProcess(int processId)
    {
        try
        {
            Process process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                process.Dispose();
                throw new InvalidOperationException(
                    $"The game process {processId} exited during startup.");
            }

            return process;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"The game process {processId} exited during startup.",
                exception);
        }
    }

    private sealed record LiveScaleRecovery(
        FourKayLaunchResult ActiveLaunch,
        bool OutputIsActive);

    private sealed record LiveOutputPostcondition(
        bool OutputIsActive,
        MagpieScalingWindowInspection Inspection);

    private async Task RestoreExactAttachGeometryAsync(
        Process gameProcess,
        AttachWindowRecoveryState recovery)
    {
        EnsureExactAttachProcess(recovery.ExactProcess, recovery.EqGamePath);
        WindowGeometryRestoreResult restored =
            _windowPlacement.RestoreExactWindowGeometry(
                recovery.OriginalSourceWindow.Handle,
                recovery.OriginalGeometry);
        if (!restored.IsExact)
        {
            throw new InvalidOperationException(
                "The previous game-window geometry was not restored exactly: "
                    + string.Join(" ", restored.Issues));
        }

        WindowDescriptor restoredSource =
            await _windowDiscovery.WaitForStableVisibleWindowAsync(
                gameProcess,
                recovery.OriginalGeometry.ClientSize,
                recovery.WindowTimeout,
                requiredStablePolls: 3,
                CancellationToken.None).ConfigureAwait(false);
        EnsureExactAttachSourceIdentity(
            recovery.OriginalSourceWindow,
            restoredSource,
            recovery.ExactProcess,
            recovery.EqGamePath,
            recovery.OriginalGeometry.ClientSize);
        if (restoredSource.WindowBounds != recovery.OriginalGeometry.WindowBounds)
        {
            throw new InvalidOperationException(
                "The game client size recovered, but its exact previous outer "
                    + "window position or dimensions did not.");
        }

        WindowGeometrySnapshot stableGeometry =
            _windowPlacement.CaptureWindowGeometry(
                recovery.OriginalSourceWindow.Handle);
        if (stableGeometry != recovery.OriginalGeometry)
        {
            throw new InvalidOperationException(
                "The game client recovered its visible bounds, but its exact "
                    + "pre-Attach show state or Win32 placement did not remain stable.");
        }

        EnsureExactAttachProcess(recovery.ExactProcess, recovery.EqGamePath);
    }

    private static void EnsureAttachGeometrySnapshotMatchesSource(
        WindowGeometrySnapshot geometry,
        WindowDescriptor source,
        PixelSize clientSize)
    {
        if (geometry.WindowBounds != source.WindowBounds
            || geometry.ClientSize != clientSize)
        {
            throw new InvalidOperationException(
                "The exact source window geometry changed while Attach was "
                    + "capturing its recovery snapshot. No placement was attempted.");
        }
    }

    private void EnsureExactAttachProcess(
        ProcessDescriptor exactProcess,
        string eqGamePath)
    {
        IReadOnlyList<ProcessDescriptor> matches =
            _processDiscovery.FindByExecutablePath(eqGamePath);
        if (matches.Count != 1
            || !IsSameAttachProcessIdentity(
                exactProcess,
                matches[0],
                eqGamePath))
        {
            throw new InvalidOperationException(
                "The exact EverQuest Legends process changed while Attach was "
                    + "validating the requested generic window size.");
        }
    }

    private static bool IsSameAttachProcessIdentity(
        ProcessDescriptor expected,
        ProcessDescriptor observed,
        string eqGamePath)
    {
        return observed.ProcessId == expected.ProcessId
            && string.Equals(
                Path.GetFullPath(expected.ExecutablePath),
                Path.GetFullPath(eqGamePath),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                Path.GetFullPath(observed.ExecutablePath),
                Path.GetFullPath(eqGamePath),
                StringComparison.OrdinalIgnoreCase)
            && (expected.StartTimeUtc is null
                || observed.StartTimeUtc == expected.StartTimeUtc);
    }

    private static void EnsureExactAttachSourceIdentity(
        WindowDescriptor original,
        WindowDescriptor observed,
        ProcessDescriptor exactProcess,
        string eqGamePath,
        PixelSize expectedClientSize)
    {
        if (observed.Handle != original.Handle
            || observed.ProcessId != original.ProcessId
            || observed.ProcessId != exactProcess.ProcessId
            || !string.Equals(
                observed.ClassName,
                original.ClassName,
                StringComparison.Ordinal)
            || observed.IsMinimized
            || string.IsNullOrWhiteSpace(observed.ExecutablePath)
            || !string.Equals(
                Path.GetFullPath(observed.ExecutablePath),
                Path.GetFullPath(eqGamePath),
                StringComparison.OrdinalIgnoreCase)
            || Math.Abs(observed.ClientBounds.Width - expectedClientSize.Width) > 1
            || Math.Abs(observed.ClientBounds.Height - expectedClientSize.Height) > 1)
        {
            throw new InvalidOperationException(
                "The resized game window no longer matches the exact process, HWND, "
                    + "class, executable, or requested physical client size bound "
                    + "to this Attach attempt.");
        }
    }

    private async Task<LiveScaleRecovery> RestorePreviousLiveScaleAsync(
        FourKayLaunchResult launch,
        TimeSpan windowTimeout)
    {
        EnsureExactLiveProcesses(launch);
        EnsureOriginalLiveMonitorAvailable(launch);
        _ = RequireExactLiveSourceWindow(launch, expectedSize: null);
        MagpieScalingWindowInspection output =
            _scalingInspector.Inspect(launch.SourceWindow.Handle);
        EnsureLiveSourceIsBackground(
            launch,
            output,
            "before restoring the previous game-window size");
        if (output.IsActive)
        {
            if (output.ScalingProcessId != launch.MagpieProcess.Process.Id
                || output.SourceWindowHandle != launch.SourceWindow.Handle)
            {
                throw new InvalidOperationException(
                    "A replacement or foreign fullscreen output appeared during live "
                        + "scale rollback. It was left untouched.");
            }

            bool stopped = await _scalingInspector.StopScalingAsync(
                launch.SourceWindow.Handle,
                launch.MagpieProcess.Process.Id,
                TimeSpan.FromSeconds(4),
                CancellationToken.None).ConfigureAwait(false);
            if (!stopped)
            {
                throw new InvalidOperationException(
                    "The exact owned fullscreen output could not be paused for rollback.");
            }
        }

        MagpieScalingWindowInspection pausedOutput =
            _scalingInspector.Inspect(launch.SourceWindow.Handle);
        EnsureLiveOutputIsInactive(
            pausedOutput,
            "after pausing fullscreen output for rollback");
        EnsureLiveSourceIsBackground(
            launch,
            pausedOutput,
            "after pausing fullscreen output for rollback");
        EnsureOriginalLiveMonitorAvailable(launch);
        MagpieScalingWindowInspection placementOutput =
            _scalingInspector.Inspect(launch.SourceWindow.Handle);
        EnsureLiveOutputIsInactive(
            placementOutput,
            "immediately before restoring the previous game-window size");
        EnsureLiveSourceIsBackground(
            launch,
            placementOutput,
            "immediately before restoring the previous game-window size");

        PixelSize previousSize = launch.Placement.RequestedClientSize;
        WindowPlacementResult restoredPlacement =
            _windowPlacement.CenterFixedClientArea(
                launch.SourceWindow.Handle,
                previousSize,
                launch.Placement.Monitor.Handle);
        EnsureSafePlacement(restoredPlacement);
        WindowDescriptor restoredSource =
            await _windowDiscovery.WaitForStableVisibleWindowAsync(
                launch.GameProcess,
                previousSize,
                windowTimeout,
                requiredStablePolls: 3,
                CancellationToken.None).ConfigureAwait(false);
        EnsureExactLiveSourceIdentity(
            launch,
            restoredSource,
            previousSize,
            restoredPlacement.Monitor);
        EnsureExactLiveProcesses(launch);

        FourKayLaunchResult recovered = launch with
        {
            SourceWindow = restoredSource,
            Placement = restoredPlacement,
        };
        LiveOutputPostcondition postcondition =
            await ValidateLiveOutputPostconditionAsync(
                recovered,
                previousSize,
                recovered.ExpectedDestinationRegion,
                windowTimeout,
                CancellationToken.None).ConfigureAwait(false);
        recovered = recovered with
        {
            ScalingInspection = postcondition.Inspection,
        };
        return new LiveScaleRecovery(
            recovered,
            postcondition.OutputIsActive);
    }

    private WindowDescriptor RequireExactLiveSourceWindow(
        FourKayLaunchResult launch,
        PixelSize? expectedSize)
    {
        if (!IsProcessAlive(launch.GameProcess))
        {
            throw new InvalidOperationException(
                "The exact EverQuest Legends process is no longer running.");
        }

        WindowDescriptor source = _windowDiscovery
            .FindVisibleWindows(launch.GameProcess.Id)
            .SingleOrDefault(
                window => window.Handle == launch.SourceWindow.Handle)
            ?? throw new InvalidOperationException(
                "The original EverQuest Legends window is no longer available.");
        EnsureExactLiveSourceIdentity(
            launch,
            source,
            expectedSize,
            launch.Placement.Monitor);
        return source;
    }

    private static void EnsureExactLiveSourceIdentity(
        FourKayLaunchResult launch,
        WindowDescriptor source,
        PixelSize? expectedSize,
        MonitorDescriptor monitor)
    {
        if (source.Handle != launch.SourceWindow.Handle
            || source.ProcessId != launch.GameProcess.Id
            || source.ProcessId != launch.SourceWindow.ProcessId
            || !string.Equals(
                source.ClassName,
                launch.SourceWindow.ClassName,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(source.ExecutablePath)
            || string.IsNullOrWhiteSpace(launch.SourceWindow.ExecutablePath)
            || !string.Equals(
                Path.GetFullPath(source.ExecutablePath),
                Path.GetFullPath(launch.SourceWindow.ExecutablePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The live game window no longer matches the exact process, HWND, "
                    + "class, or executable bound to this session.");
        }

        if (expectedSize is { } size
            && (Math.Abs(source.ClientBounds.Width - size.Width) > 1
                || Math.Abs(source.ClientBounds.Height - size.Height) > 1))
        {
            throw new InvalidOperationException(
                $"The live game client is {source.ClientBounds.Width}x"
                    + $"{source.ClientBounds.Height}, not the expected {size}.");
        }

        // The placement planner intentionally lets the window frame extend past
        // the work area (and, at native size, the monitor); only the client
        // area's containment is part of the session contract.
        if (monitor.Handle != launch.Placement.Monitor.Handle
            || monitor.Bounds != launch.Placement.Monitor.Bounds
            || !Contains(monitor.Bounds, source.ClientBounds))
        {
            throw new InvalidOperationException(
                "The exact game client area is no longer fully contained on the "
                    + "session's original target monitor, or that monitor's "
                    + "physical bounds changed.");
        }
    }

    private static void EnsureExactLiveProcesses(FourKayLaunchResult launch)
    {
        if (!IsProcessAlive(launch.GameProcess)
            || !IsProcessAlive(launch.MagpieProcess.Process))
        {
            throw new InvalidOperationException(
                "The exact game or dedicated scaling-engine process has exited.");
        }

        string? gamePath =
            ProcessDiscoveryService.TryGetExecutablePath(launch.GameProcess.Id);
        string? magpiePath =
            ProcessDiscoveryService.TryGetExecutablePath(
                launch.MagpieProcess.Process.Id);
        string expectedMagpiePath = Path.GetFullPath(
            Path.Combine(launch.MagpieDirectory, "Magpie.exe"));
        if (string.IsNullOrWhiteSpace(gamePath)
            || string.IsNullOrWhiteSpace(launch.SourceWindow.ExecutablePath)
            || !string.Equals(
                Path.GetFullPath(gamePath),
                Path.GetFullPath(launch.SourceWindow.ExecutablePath),
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(magpiePath)
            || !string.Equals(
                Path.GetFullPath(magpiePath),
                expectedMagpiePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The exact game or dedicated Magpie executable identity changed.");
        }
    }

    private static void EnsureExactLiveOutput(
        FourKayLaunchResult launch,
        MagpieScalingWindowInspection inspection,
        PixelSize expectedSource,
        PixelRect expectedDestination)
    {
        bool exact =
            inspection.IsActive
            && inspection.IsSafeForInput
            && !inspection.IsWindowedScaling
            && inspection.ScalingProcessId == launch.MagpieProcess.Process.Id
            && inspection.SourceWindowHandle == launch.SourceWindow.Handle
            && inspection.MonitorBounds is { } monitor
            && RectsMatch(monitor, launch.Placement.Monitor.Bounds, tolerance: 1)
            && inspection.SourceRegion is { } source
            && Math.Abs(source.Width - expectedSource.Width) <= 2
            && Math.Abs(source.Height - expectedSource.Height) <= 2
            && inspection.DestinationRegion is { } destination
            && RectsMatch(destination, expectedDestination, tolerance: 2);
        if (!exact)
        {
            throw new InvalidOperationException(
                "The current fullscreen output does not match the exact owned "
                    + "process, source, monitor, geometry, and safe mouse map. "
                    + string.Join(" ", inspection.Issues));
        }
    }

    private async Task<LiveOutputPostcondition>
        ValidateLiveOutputPostconditionAsync(
            FourKayLaunchResult launch,
            PixelSize expectedSource,
            PixelRect expectedDestination,
            TimeSpan timeout,
            CancellationToken cancellationToken)
    {
        EnsureExactLiveProcesses(launch);
        EnsureOriginalLiveMonitorAvailable(launch);
        _ = RequireExactLiveSourceWindow(launch, expectedSource);

        MagpieScalingWindowInspection inspection =
            _scalingInspector.Inspect(launch.SourceWindow.Handle);
        if (inspection.IsActive)
        {
            if (inspection.ScalingProcessId
                    != launch.MagpieProcess.Process.Id
                || inspection.SourceWindowHandle
                    != launch.SourceWindow.Handle)
            {
                throw new InvalidOperationException(
                    "A replacement or foreign fullscreen output appeared while "
                        + "live scaling was verifying its final mouse map. It was "
                        + "left untouched.");
            }

            if (!inspection.IsSafeForInput)
            {
                TimeSpan attachmentTimeout =
                    timeout < TimeSpan.FromSeconds(4)
                        ? timeout
                        : TimeSpan.FromSeconds(4);
                inspection =
                    await _scalingInspector.WaitForSafeAttachmentAsync(
                        launch.SourceWindow.Handle,
                        attachmentTimeout,
                        cancellationToken).ConfigureAwait(false);
            }
        }

        EnsureExactLiveProcesses(launch);
        EnsureOriginalLiveMonitorAvailable(launch);
        _ = RequireExactLiveSourceWindow(launch, expectedSource);
        if (inspection.IsActive)
        {
            EnsureExactLiveOutput(
                launch,
                inspection,
                expectedSource,
                expectedDestination);
            return new LiveOutputPostcondition(
                OutputIsActive: true,
                Inspection: inspection);
        }

        EnsureLiveSourceIsBackground(
            launch,
            inspection,
            "while accepting an inactive fullscreen output");
        return new LiveOutputPostcondition(
            OutputIsActive: false,
            Inspection: inspection);
    }

    private void EnsureLiveSourceIsBackground(
        FourKayLaunchResult launch,
        MagpieScalingWindowInspection inspection,
        string boundary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        nint foreground = _foregroundWindow.GetForegroundWindowHandle();
        if (foreground == nint.Zero)
        {
            throw new InvalidOperationException(
                $"Windows did not expose a foreground window {boundary}. Live "
                    + "scaling stopped rather than assuming the game was safely "
                    + "in the background.");
        }

        WindowRuntimeSnapshot foregroundSnapshot =
            _foregroundWindow.InspectWindow(foreground);
        bool exactScalingWindowIsForeground =
            inspection.IsActive
            && inspection.ScalingProcessId
                == launch.MagpieProcess.Process.Id
            && foreground == inspection.ScalingWindowHandle;
        bool gameProcessWindowIsForeground =
            foregroundSnapshot.IsValid
            && foregroundSnapshot.ProcessId == launch.GameProcess.Id;
        if (foreground == launch.SourceWindow.Handle
            || exactScalingWindowIsForeground
            || gameProcessWindowIsForeground)
        {
            throw new InvalidOperationException(
                $"EverQuest Legends returned to the foreground {boundary}. Live "
                    + "scaling stopped before changing a mouse map under active "
                    + "game input.");
        }

        if (!foregroundSnapshot.IsValid)
        {
            throw new InvalidOperationException(
                $"The foreground-window identity changed {boundary}. Live scaling "
                    + "stopped rather than assuming the game remained safely in "
                    + "the background.");
        }
    }

    private static void EnsureLiveOutputIsInactive(
        MagpieScalingWindowInspection inspection,
        string boundary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        if (inspection.IsActive)
        {
            throw new InvalidOperationException(
                $"A fullscreen scaling output is still active {boundary}. No "
                    + "game-window resize was accepted. "
                    + string.Join(" ", inspection.Issues));
        }
    }

    private void EnsureOriginalLiveMonitorAvailable(
        FourKayLaunchResult launch)
    {
        MonitorDescriptor current = _windowPlacement.GetMonitors()
            .SingleOrDefault(
                monitor => monitor.Handle
                    == launch.Placement.Monitor.Handle)
            ?? throw new InvalidOperationException(
                "The live session's original target monitor is no longer connected.");
        if (current.Bounds != launch.Placement.Monitor.Bounds)
        {
            throw new InvalidOperationException(
                "The original target monitor's physical bounds changed during the "
                    + "live session. Stop fullscreen scaling and start a new session "
                    + "for the current display mode.");
        }
    }

    private static PixelRect OffsetDestinationToMonitor(
        PixelRect relativeDestination,
        PixelRect monitorBounds)
    {
        return new PixelRect(
            monitorBounds.X + relativeDestination.X,
            monitorBounds.Y + relativeDestination.Y,
            relativeDestination.Width,
            relativeDestination.Height);
    }

    private static bool IsProcessAlive(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            return false;
        }
    }

    private static bool Contains(PixelRect outer, PixelRect inner)
    {
        return inner.X >= outer.X
            && inner.Y >= outer.Y
            && (long)inner.X + inner.Width <= (long)outer.X + outer.Width
            && (long)inner.Y + inner.Height <= (long)outer.Y + outer.Height;
    }

    private static bool RectsMatch(
        PixelRect left,
        PixelRect right,
        int tolerance)
    {
        return Math.Abs(left.X - right.X) <= tolerance
            && Math.Abs(left.Y - right.Y) <= tolerance
            && Math.Abs(left.Width - right.Width) <= tolerance
            && Math.Abs(left.Height - right.Height) <= tolerance;
    }

    private static bool IsNonFatalLiveScaleFailure(Exception exception)
    {
        return exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;
    }

    private static void EnsureSafePlacement(WindowPlacementResult placement)
    {
        if (!placement.IsExactAndOnScreen)
        {
            throw new InvalidOperationException(
                "The game window could not be placed safely on the target monitor: "
                + string.Join(" ", placement.Issues));
        }
    }

    private static bool IsExactIntegerScale(PixelSize source, PixelSize target)
    {
        if (target.Width % source.Width != 0 || target.Height % source.Height != 0)
        {
            return false;
        }

        int horizontalScale = target.Width / source.Width;
        int verticalScale = target.Height / source.Height;
        return horizontalScale == verticalScale && horizontalScale >= 1;
    }

    private async Task ValidatePreparedClientConfigurationAsync(
        FourKayPreparedState state,
        FourKayLaunchRequest request,
        CancellationToken cancellationToken)
    {
        string eqDirectory = Path.GetFullPath(state.EqDirectory);
        ValidateLaunchTargetBinding(request, eqDirectory);
        string expectedIniPath = Path.GetFullPath(
            Path.Combine(eqDirectory, "eqclient.ini"));
        string expectedGamePath = Path.GetFullPath(
            Path.Combine(eqDirectory, "eqgame.exe"));
        string journalIniPath = Path.GetFullPath(state.EqClientIniPath);
        if (!string.Equals(
            expectedIniPath,
            journalIniPath,
            StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                expectedGamePath,
                Path.GetFullPath(state.EqGamePath),
                StringComparison.OrdinalIgnoreCase)
            || state.DpiCompatibility is null
            || !string.Equals(
                expectedGamePath,
                Path.GetFullPath(state.DpiCompatibility.ExecutablePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The prepared journal, launch target, DPI state, and game executable do "
                    + "not all belong to the same selected EverQuest Legends "
                    + "directory. Launch was stopped.");
        }

        if (string.IsNullOrWhiteSpace(state.AppliedIniSha256))
        {
            throw new InvalidDataException(
                "The prepared journal has no applied eqclient.ini checksum. Launch "
                    + "was stopped; restore the saved settings first.");
        }

        string stableHash = await CaptureVerifiedNativeUiScaleSnapshotAsync(
            expectedIniPath,
            "Launch",
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                stableHash,
                state.AppliedIniSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "eqclient.ini changed after this session was prepared. Launch was "
                    + "stopped so native scaling or other unverified settings cannot "
                    + "stack with fullscreen scaling. Restore first, then prepare again.");
        }
    }

    /// <summary>
    /// Read-only advisory check used by Attach and launch-then-attach. It never
    /// blocks scaling and never writes: a missing, unreadable, or non-native
    /// eqclient.ini simply produces a warning string (or none).
    /// </summary>
    private async Task<string?> ReadNativeUiScaleAdvisoryAsync(
        string eqClientIniPath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(eqClientIniPath))
            {
                return null;
            }

            EqClientVideoSettings settings = await _eqClientConfiguration.ReadAsync(
                eqClientIniPath,
                cancellationToken).ConfigureAwait(false);
            if (settings.NativeUiScaleStatus == NativeUiScaleStatus.Valid
                && settings.NativeUiScaleIndex is int nativeUiScaleIndex
                && nativeUiScaleIndex != 0)
            {
                return
                    "The saved Legends native UI scale is above 1x, so the in-game "
                        + "UI scaling and this fullscreen scaling are stacked. "
                        + "eqclient.ini was read only and was not modified.";
            }

            return null;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or NotSupportedException
                or System.Security.SecurityException)
        {
            return
                "eqclient.ini could not be read for the advisory native UI-scale "
                    + "check. It was not modified. " + exception.Message;
        }
    }

    private async Task<string> CaptureVerifiedNativeUiScaleSnapshotAsync(
        string eqClientIniPath,
        string actionName,
        CancellationToken cancellationToken)
    {
        string beforeHash = await FileHash.ComputeSha256Async(
            eqClientIniPath,
            cancellationToken).ConfigureAwait(false);
        EqClientVideoSettings settings = await _eqClientConfiguration.ReadAsync(
            eqClientIniPath,
            cancellationToken).ConfigureAwait(false);
        string afterHash = await FileHash.ComputeSha256Async(
            eqClientIniPath,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
            beforeHash,
            afterHash,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"eqclient.ini changed while {actionName} was verifying native UI "
                    + "scale. No fullscreen scaling was started.");
        }

        if (settings.NativeUiScaleStatus != NativeUiScaleStatus.Valid
            || settings.NativeUiScaleIndex != 0)
        {
            string observed = settings.NativeUiScaleStatus switch
            {
                NativeUiScaleStatus.Missing => "missing",
                NativeUiScaleStatus.Invalid => "malformed or outside 0-4",
                _ => $"index {settings.NativeUiScaleIndex}",
            };
            throw new InvalidDataException(
                $"{actionName} requires a verified Legends native UI scale of 1x "
                    + $"(UIScale=0), but the saved value is {observed}. "
                    + "No fullscreen scaling was started.");
        }

        return afterHash;
    }

    private void EnsureTargetMonitorMatchesPlan(FourKayPreparedState state)
    {
        nint expectedHandle = new(state.TargetMonitorHandle);
        MonitorDescriptor? monitor = _windowPlacement.GetMonitors()
            .FirstOrDefault(candidate => candidate.Handle == expectedHandle);
        if (monitor is null
            || monitor.Bounds != state.TargetMonitorBounds
            || monitor.Bounds.Size != state.ResolutionPlan.TargetResolution)
        {
            throw new InvalidOperationException(
                "The prepared target monitor is no longer available with its "
                    + "original physical bounds, so the borderless output could "
                    + "appear on the wrong display or at the wrong size. Restore "
                    + "the profile, reselect the display, and prepare again.");
        }
    }

    private static void ValidateLaunchRequest(FourKayLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PreparedState);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LauncherPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MagpieDirectory);
        if (!Enum.IsDefined(request.StartMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The requested game start mode is not supported.");
        }

        if (request.LauncherArguments is null)
        {
            throw new ArgumentNullException(
                nameof(request),
                "Launch arguments cannot be null.");
        }

        ValidateLaunchTargetBinding(
            request,
            Path.GetFullPath(request.PreparedState.EqDirectory));
        if (request.PreparedState.Status is not (
                FourKayJournalStatus.Prepared
                or FourKayJournalStatus.Committed))
        {
            throw new InvalidOperationException(
                "The launch configuration is not prepared and verified.");
        }

        if (!Enum.IsDefined(request.PreparedState.UiCompatibilityMode)
            || request.PreparedState.UiCompatibilityMode
                == FourKayUiCompatibilityMode.UnspecifiedLegacy)
        {
            throw new InvalidOperationException(
                "This prepared journal does not contain a trusted UI compatibility "
                    + "mode. It is restore-only and cannot be launched.");
        }

        ValidateTimeouts(
            request.GameStartTimeout,
            request.WindowTimeout,
            request.ScalingTimeout);
    }

    private static void ValidateLaunchTargetBinding(
        FourKayLaunchRequest request,
        string eqDirectory)
    {
        string fullEqDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(eqDirectory));
        string launchTargetPath = Path.GetFullPath(request.LauncherPath);
        switch (request.StartMode)
        {
            case FourKayGameStartMode.OfficialLauncher:
                string expectedLauncherPath = Path.GetFullPath(
                    Path.Combine(fullEqDirectory, "LaunchPad.exe"));
                if (!string.Equals(
                        launchTargetPath,
                        expectedLauncherPath,
                        StringComparison.OrdinalIgnoreCase)
                    || request.LauncherArguments.Count != 0)
                {
                    throw new InvalidDataException(
                        "Normal launch requires the exact LaunchPad.exe in the "
                            + "selected EverQuest Legends folder and no custom "
                            + "arguments. Launch was stopped.");
                }

                break;

            case FourKayGameStartMode.SpinTextureEnhanced:
                bool exactExecutableName = string.Equals(
                    Path.GetFileName(launchTargetPath),
                    "SpinTexture.exe",
                    StringComparison.OrdinalIgnoreCase);
                bool exactArguments = request.LauncherArguments.Count == 2
                    && string.Equals(
                        request.LauncherArguments[0],
                        "--play-enhanced",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(request.LauncherArguments[1]);
                string? requestedEqDirectory = exactArguments
                    ? Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(request.LauncherArguments[1]))
                    : null;
                if (!exactExecutableName
                    || !exactArguments
                    || !string.Equals(
                        requestedEqDirectory,
                        fullEqDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Enhanced launch requires the exact SpinTexture.exe wrapper "
                            + "with only '--play-enhanced' and the selected EverQuest "
                            + "Legends folder. Tickets, credentials, and arbitrary "
                            + "direct-launch arguments are not accepted.");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "The requested game start mode is not supported.");
        }
    }

    private static string? GetMagpieLauncherPath(FourKayLaunchRequest request) =>
        request.StartMode == FourKayGameStartMode.OfficialLauncher
            ? request.LauncherPath
            : null;

    private static void ValidateAttachRequest(FourKayAttachRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EqDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MagpieDirectory);
        if (!Enum.IsDefined(request.UiCompatibilityMode)
            || request.UiCompatibilityMode
                == FourKayUiCompatibilityMode.UnspecifiedLegacy)
        {
            throw new InvalidOperationException(
                "Attach requires an explicit trusted UI compatibility mode.");
        }

        if (request.ExpectedProcessId.HasValue
            != request.ExpectedProcessStartTimeUtc.HasValue)
        {
            throw new ArgumentException(
                "Automatic Attach must supply both the expected process ID and "
                    + "its UTC start time.",
                nameof(request));
        }

        if (request.ExpectedProcessId is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The expected process ID must be positive.");
        }

        if (request.ExpectedClientSize is { } expectedClientSize
            && (expectedClientSize.Width <= 0 || expectedClientSize.Height <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The expected client size must contain positive physical pixels.");
        }

        if (request.RequestedGenericClientSize is { } requestedClientSize
            && (requestedClientSize.Width <= 0 || requestedClientSize.Height <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The requested generic client size must contain positive physical "
                    + "pixels.");
        }

        if (request.RequestedGenericClientSize is not null
            && request.UiCompatibilityMode
                != FourKayUiCompatibilityMode.GenericOrCustom)
        {
            throw new InvalidOperationException(
                "Only an explicitly selected default or generic/custom UI session "
                    + "may resize an already-running client. Strict SpinUI Attach "
                    + "uses ExpectedClientSize for validation only.");
        }

        ValidateTimeouts(
            TimeSpan.FromSeconds(1),
            request.WindowTimeout,
            request.ScalingTimeout);
    }

    private static void ValidateLiveScaleRequest(FourKayLiveScaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ActiveLaunch);
        ArgumentNullException.ThrowIfNull(request.RequestedPlan);
        if (request.WindowTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The live-scale window timeout must be positive.");
        }

        FourKayLaunchResult launch = request.ActiveLaunch;
        ResolutionPlan plan = request.RequestedPlan;
        if (launch.UiCompatibilityMode
            != FourKayUiCompatibilityMode.GenericOrCustom)
        {
            throw new InvalidOperationException(
                "Live 1% scaling is available only for a session explicitly bound "
                    + "to the default or another generic/custom UI. SpinUI sessions "
                    + "remain locked to validated layout resolutions.");
        }

        if (launch.EffectiveFilter == ScalingFilter.NearestNeighbor
            || plan.Filter == ScalingFilter.NearestNeighbor)
        {
            throw new InvalidOperationException(
                "Live fractional adjustment is unavailable with Exact pixels. Use "
                    + "Readable UI, Smooth FSR, or Lanczos for a generic/custom "
                    + "UI session.");
        }

        if (plan.PresetKind != ResolutionPresetKind.Custom
            || plan.Filter != launch.EffectiveFilter
            || plan.TargetResolution != launch.Placement.Monitor.Bounds.Size)
        {
            throw new InvalidOperationException(
                "The requested live plan does not match the active filter, target "
                    + "monitor, or generic custom-plan contract.");
        }

        PixelSize source = plan.SourceResolution;
        PixelSize target = plan.TargetResolution;
        if (source.Width > target.Width
            || source.Height > target.Height
            || source.Width < Math.Min(
                ResolutionPlanner.MinimumInteractiveWidth,
                target.Width)
            || source.Height < Math.Min(
                ResolutionPlanner.MinimumInteractiveHeight,
                target.Height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The live source resolution is outside the supported render bounds.");
        }

        if (string.IsNullOrWhiteSpace(launch.SourceWindow.ExecutablePath))
        {
            throw new InvalidOperationException(
                "The active session has no exact game executable path binding.");
        }
    }

    private static void ValidateTimeouts(
        TimeSpan gameStartTimeout,
        TimeSpan windowTimeout,
        TimeSpan scalingTimeout)
    {
        if (gameStartTimeout <= TimeSpan.Zero
            || windowTimeout <= TimeSpan.Zero
            || scalingTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gameStartTimeout),
                "Launch timeouts must be positive.");
        }
    }

    private static string RequireFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("A required executable is missing.", fullPath);
        }

        return fullPath;
    }
}
