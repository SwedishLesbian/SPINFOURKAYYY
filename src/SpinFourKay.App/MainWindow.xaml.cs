using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SpinFourKay.App.Infrastructure;
using SpinFourKay.Core.Configuration;
using SpinFourKay.Core.Display;
using SpinFourKay.Core.Layouts;
using SpinFourKay.Core.Magpie;
using SpinFourKay.Core.Orchestration;
using SpinFourKay.Core.Preferences;
using SpinFourKay.Core.Startup;
using SpinFourKay.Core.Updates;
using SpinFourKay.Core.Windows;

namespace SpinFourKay.App;

public partial class MainWindow : Window, IDisposable
{
    private static readonly TimeSpan FullscreenResumeGrace = TimeSpan.FromSeconds(8);
    private static readonly Version CurrentAppVersion = GetCurrentAppVersion();

    private readonly ResolutionPlanner _resolutionPlanner = new();
    private readonly SpinUiResolutionPlanner _spinUiResolutionPlanner = new();
    private readonly WindowPlacementService _windowPlacement = new();
    private readonly FourKayPreparationService _preparationService = new();
    private readonly OverlayCompatibilityService _overlayCompatibility = new();
    private readonly FourKayLaunchService _launchService;
    private readonly FourKayJournalStore _journalStore = new();
    private readonly UiLayoutProfileService _layoutProfileService = new();
    private readonly MagpieScalingWindowInspector _scalingInspector = new();
    private readonly MagpieProcessService _magpieProcess = new();
    private readonly ProcessDiscoveryService _processDiscovery = new();
    private readonly WindowDiscoveryService _windowDiscovery = new();
    private readonly ForegroundWindowService _foregroundWindow = new();
    private readonly DateTimeOffset _supervisionClockOriginUtc =
        DateTimeOffset.UtcNow;
    private readonly long _supervisionTimestampOrigin =
        Stopwatch.GetTimestamp();
    private readonly DispatcherTimer _scalingHealthTimer;
    private readonly DispatcherTimer _overlayCompatibilityTimer;
    private readonly DispatcherTimer _liveScaleDebounceTimer;
    private readonly DispatcherTimer _clarityDebounceTimer;
    private readonly DispatcherTimer _preferencesSaveTimer;
    private readonly UserPreferencesStore _preferencesStore = new(
        PathLocator.PreferencesPath);
    private readonly GitHubReleaseUpdateService _updateService = new(
        PathLocator.UpdateRoot);
    private bool _clarityReapplyPending;
    private int _overlayCompatibilityTickCount;
    private int _displayedOverlayWarningCount;
    private CancellationTokenSource? _operationCancellation;
    private DisplaySnapshot? _display;
    private ResolutionPlan? _currentPlan;
    private SpinUiResolutionPlan[] _spinUiPlans = [];
    private SpinUiDetectionResult _spinUiDetection = new(
        SpinUiDetectionStatus.NotDetected,
        ThemeDirectoryPresent: false,
        MatchingLayouts: [],
        Issue: null);
    private string? _spinUiDetectionDirectory;
    private string? _spinUiFilterNotice;
    private FourKayPreparedState? _activeState;
    private UiLayoutSessionState? _activeLayoutSession;
    private FourKayLaunchResult? _activeLaunch;
    private MagpieScalingCleanupLease? _pendingScalingCleanupLease;
    private Task? _inFlightOperation;
    private Task? _scalingHealthCheckTask;
    private bool _isBusy;
    private bool _isScaledSessionActive;
    private bool _isScalingHealthCheckRunning;
    private bool _scalingCleanupRequired;
    private bool _isCloseCleanupRunning;
    private bool _allowCloseAfterCleanup;
    private bool _isUpdatingPresetCards;
    private bool _startupRunningGameDetectionAttempted;
    private bool _autoPlayAttempted;
    private string? _engineTidySummary;
    private bool _preferencesReady;
    private string? _lastValidLegendsDirectory;
    private string? _spinTextureExecutablePath;
    private int _lastSpinUiPresetIndex = 2;
    private ReleaseUpdateInfo? _availableUpdate;
    private bool _isCheckingForUpdates;
    private bool _isInstallingUpdate;
    private bool _closeRequestedDuringUpdate;
    private CancellationTokenSource? _updateCancellation;
    private FineUiScale? _pendingLiveScale;
    private int _activeRecoveryCount;
    private ScalingSessionSupervisionState _scalingSupervisionState =
        ScalingSessionSupervisionState.Active;

    private bool HasPendingPreparation =>
        _activeState?.Status
            is FourKayJournalStatus.Preparing or FourKayJournalStatus.Prepared;

    private FourKayUiCompatibilityMode SelectedUiCompatibilityMode =>
        SpinUiModeRadioButton.IsChecked == true
            ? FourKayUiCompatibilityMode.SpinUiStrict
            : FourKayUiCompatibilityMode.GenericOrCustom;

    private bool UsesStrictSpinUiMode =>
        SelectedUiCompatibilityMode == FourKayUiCompatibilityMode.SpinUiStrict;

    private bool IsUiSessionConfirmationSatisfied =>
        !UsesStrictSpinUiMode
        || (_spinUiDetection.IsReliable
            && _spinUiDetection.IsDetected
            && SpinUiLayoutReadyCheckBox.IsChecked == true);

    public MainWindow()
    {
        _launchService = new FourKayLaunchService(
            overlayCompatibility: _overlayCompatibility);
        InitializeComponent();

        _scalingHealthTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5),
        };
        _scalingHealthTimer.Tick += ScalingHealthTimer_Tick;
        _overlayCompatibilityTimer = new DispatcherTimer(
            DispatcherPriority.Background)
        {
            // Magpie moves the physical cursor between its source and scaled
            // surfaces. A short handoff cadence keeps overlay controls clickable
            // without leaving an input-opaque shadow over EverQuest.
            Interval = TimeSpan.FromMilliseconds(20),
        };
        _overlayCompatibilityTimer.Tick += OverlayCompatibilityTimer_Tick;
        _liveScaleDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        _liveScaleDebounceTimer.Tick += LiveScaleDebounceTimer_Tick;
        _clarityDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(800),
        };
        _clarityDebounceTimer.Tick += ClarityDebounceTimer_Tick;
        _preferencesSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _preferencesSaveTimer.Tick += PreferencesSaveTimer_Tick;
        Loaded += MainWindow_Loaded;
        LocationChanged += MainWindow_LocationChanged;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        UserPreferencesLoadResult saved =
            await _preferencesStore.LoadAsync(CancellationToken.None)
                .ConfigureAwait(true);
        if (saved.Warning is not null)
        {
            Debug.WriteLine("SpinFOURKAYYY settings fallback: " + saved.Warning);
        }

        UserPreferences preferences = saved.Preferences;
        _spinTextureExecutablePath =
            PathLocator.IsSpinTextureExecutable(
                preferences.SpinTextureExecutablePath)
                ? Path.GetFullPath(preferences.SpinTextureExecutablePath!)
                : PathLocator.FindSpinTextureExecutable();
        string? legendsDirectory =
            PathLocator.IsLegendsDirectory(preferences.LegendsDirectory)
                ? Path.GetFullPath(preferences.LegendsDirectory!)
                : PathLocator.FindLegendsDirectory();
        if (legendsDirectory is not null)
        {
            LegendsPathTextBox.Text = legendsDirectory;
            _lastValidLegendsDirectory = legendsDirectory;
        }

        PopulateDisplays(preferences.TargetDisplayBounds);
        ApplySavedPreferences(preferences);
        await TidySupersededEngineRuntimesAsync().ConfigureAwait(true);
        _preferencesReady = true;
        RefreshDisplayAndPlan();
        await LoadRecoveryStateAsync().ConfigureAwait(true);
        await LoadScaleAwareLayoutStateAsync().ConfigureAwait(true);
        RefreshActionAvailability();
        _scalingHealthTimer.Start();
        _overlayCompatibilityTimer.Start();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        DetectRunningGameAtStartup();
        QueuePreferencesSave();
        if (App.UpdatedFromVersion is { } previousVersion)
        {
            string currentVersion = CurrentAppVersion.ToString(3);
            SetStatus(
                StatusTone.Ready,
                $"UPDATED TO {currentVersion}",
                $"SpinFOURKAYYY updated from {previousVersion}. Your saved folder, "
                    + "display, scaling, quality, UI, overlay, and SpinTexture "
                    + "choices were kept.");
            _ = MessageBox.Show(
                this,
                $"SpinFOURKAYYY updated successfully to {currentVersion}.\n\n"
                    + "Your saved settings were kept and are ready to use.",
                "SpinFOURKAYYY is up to date",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // A shortcut launch must not be answered with an install prompt the
        // player did not open. The update banner and Install update button
        // still appear; only the modal question is skipped.
        await CheckForUpdatesAsync(
            showCurrentVersionMessage: false,
            offerInstall: App.UpdatedFromVersion is null
                && !App.StartupSwitches.RequestsAutoPlay).ConfigureAwait(true);
        if (_engineTidySummary is { } tidySummary
            && !App.StartupSwitches.RequestsAutoPlay)
        {
            SetStatus(StatusTone.Info, "SCALING ENGINE TIDIED", tidySummary);
        }

        await TryStartRequestedAutoPlayAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Closes and removes scaling engines left behind by previous versions of
    /// SpinFOURKAYYY.
    /// </summary>
    /// <remarks>
    /// The engine is provisioned into an app-version-specific folder, so
    /// updating SpinFOURKAYYY moves it. A Magpie left in the tray by the old
    /// version would otherwise be reported as a separately installed Magpie the
    /// user had to hunt down and close, which is confusing precisely because
    /// they never installed one. Closing it is this application's own business.
    /// </remarks>
    private async Task TidySupersededEngineRuntimesAsync()
    {
        try
        {
            string magpieDirectory = PathLocator.FindMagpieDirectory();
            string? runtimeRoot = Path.GetDirectoryName(magpieDirectory);
            string currentRuntimeKey = Path.GetFileName(magpieDirectory);
            if (string.IsNullOrWhiteSpace(runtimeRoot)
                || string.IsNullOrWhiteSpace(currentRuntimeKey))
            {
                return;
            }

            IReadOnlyList<int> closed = await _magpieProcess
                .ShutdownSupersededAsync(
                    magpieDirectory,
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None)
                .ConfigureAwait(true);

            // A runtime whose engine is still running must be left completely
            // alone; a partial delete would strip a live engine's shaders.
            HashSet<string> directoriesInUse = _magpieProcess
                .InspectRunningInstances(magpieDirectory)
                .Select(instance => instance.ExecutablePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetDirectoryName(path!))
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(directory =>
                    Path.TrimEndingDirectorySeparator(directory!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<string> removed =
                MagpieRuntimeProvisioner.PruneSupersededRuntimes(
                    runtimeRoot,
                    currentRuntimeKey,
                    candidate => directoriesInUse.Contains(
                        Path.TrimEndingDirectorySeparator(candidate)));

            if (closed.Count == 0 && removed.Count == 0)
            {
                return;
            }

            List<string> parts = [];
            if (closed.Count > 0)
            {
                parts.Add(
                    $"Closed {closed.Count} scaling "
                        + (closed.Count == 1 ? "engine" : "engines")
                        + " left running by a previous SpinFOURKAYYY version");
            }

            if (removed.Count > 0)
            {
                parts.Add(
                    $"removed {removed.Count} superseded engine "
                        + (removed.Count == 1 ? "folder" : "folders"));
            }

            _engineTidySummary = string.Join(" and ", parts)
                + ". Your settings and EverQuest files were not touched.";
        }
        catch (Exception exception) when (
            IsExpectedUserFacingFailure(exception)
            || exception is DirectoryNotFoundException)
        {
            // Housekeeping never blocks startup. A missing or busy engine is
            // reported by the normal readiness checks instead.
            Debug.WriteLine("SpinFOURKAYYY engine tidy skipped: " + exception.Message);
        }
    }

    /// <summary>
    /// Starts the game without further input when SpinFOURKAYYY was opened by a
    /// <c>--play</c> or <c>--play-enhanced</c> shortcut. The launch runs the
    /// same code path as the on-screen buttons; only the trigger differs, so an
    /// unattended start can never take a route the manual one does not.
    /// </summary>
    private async Task TryStartRequestedAutoPlayAsync()
    {
        if (_autoPlayAttempted)
        {
            return;
        }

        _autoPlayAttempted = true;
        StartupCommandLine startup = App.StartupSwitches;
        if (startup.AutoPlayRejection is { } rejection)
        {
            AnnounceRefusedAutoPlay(
                "SHORTCUT NOT UNDERSTOOD",
                "SpinFOURKAYYY did not start the game",
                rejection);
            return;
        }

        if (!startup.RequestsAutoPlay)
        {
            return;
        }

        // The launch button is the single authority on whether a launch may
        // proceed. An unattended start asks it rather than repeating its rules.
        RefreshActionAvailability();
        if (!PrepareLaunchButton.IsEnabled)
        {
            AdvancedExpander.IsExpanded = true;
            AnnounceRefusedAutoPlay(
                "AUTOMATIC START SKIPPED",
                "SpinFOURKAYYY did not start the game",
                DescribeAutoPlayBlock());
            return;
        }

        if (startup.AutoPlay == AutoPlayMode.SpinTextureEnhanced)
        {
            // A desktop shortcut must never answer a double-click with a file
            // picker, so an unknown SpinTexture location refuses instead of
            // prompting the way the on-screen button does.
            string? spinTexturePath =
                PathLocator.IsSpinTextureExecutable(_spinTextureExecutablePath)
                    ? Path.GetFullPath(_spinTextureExecutablePath!)
                    : PathLocator.FindSpinTextureExecutable();
            if (spinTexturePath is null)
            {
                AnnounceRefusedAutoPlay(
                    "SPINTEXTURE NOT SET UP YET",
                    "SpinFOURKAYYY needs SpinTexture first",
                    "The enhanced shortcut needs SpinTexture. Use Play Enhanced "
                        + "EQ here once and choose SpinTexture.exe from its fully "
                        + "extracted folder; the shortcut works from then on. "
                        + "Nothing was started.");
                return;
            }

            _spinTextureExecutablePath = spinTexturePath;
            QueuePreferencesSave();
            await RunOperationAsync(
                "PREPARING ENHANCED EVERQUEST",
                "Starting from your desktop shortcut using your saved size, "
                    + "quality, display and UI choices\u2026",
                token => LaunchThenAutoScaleCoreAsync(
                    FourKayGameStartMode.SpinTextureEnhanced,
                    spinTexturePath,
                    token)).ConfigureAwait(true);
            return;
        }

        await RunOperationAsync(
            "PREPARING AND STARTING EVERQUEST",
            "Starting from your desktop shortcut using your saved size, quality, "
                + "display and UI choices\u2026",
            token => LaunchThenAutoScaleCoreAsync(
                FourKayGameStartMode.OfficialLauncher,
                launchTargetPath: null,
                token)).ConfigureAwait(true);
    }

    /// <summary>
    /// Reports a refused unattended start. A shortcut is usually opened by
    /// someone who has already looked away, so the refusal is raised in front
    /// of them rather than left as a line in a window they are not watching.
    /// </summary>
    private void AnnounceRefusedAutoPlay(
        string heading,
        string title,
        string message)
    {
        SetStatus(StatusTone.Warning, heading, message);
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        _ = Activate();
        _ = MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// Supplies the wording for a refused unattended start. This never decides
    /// whether a launch may happen; it only explains a decision already made by
    /// <see cref="RefreshActionAvailability"/>.
    /// </summary>
    private string DescribeAutoPlayBlock()
    {
        if (_isBusy || _isCloseCleanupRunning)
        {
            return "SpinFOURKAYYY is still finishing another action, so nothing "
                + "was started. Use Start EverQuest for me once it finishes.";
        }

        if (_isScaledSessionActive || _scalingCleanupRequired)
        {
            return "A fullscreen scaling session is still active or still needs "
                + "cleanup, so nothing was started. Finish it here first.";
        }

        if (!PathLocator.IsLegendsDirectory(LegendsPathTextBox.Text))
        {
            return "The saved EverQuest Legends folder could not be verified, so "
                + "nothing was started. Choose the folder containing eqgame.exe "
                + "and eqclient.ini, then use the shortcut again.";
        }

        try
        {
            _ = PathLocator.FindMagpieDirectory();
        }
        catch (DirectoryNotFoundException)
        {
            return "The Engine\\Magpie folder is incomplete, so nothing was "
                + "started. Re-extract the whole release into one folder.";
        }

        if (UsesStrictSpinUiMode && !IsUiSessionConfirmationSatisfied)
        {
            return "SpinUI mode still needs its one-time layout confirmation, so "
                + "nothing was started. Confirm it here, then use the shortcut "
                + "again.";
        }

        if (_display is null || _currentPlan is null)
        {
            return "A display and size could not be prepared from your saved "
                + "settings, so nothing was started. Choose them here, then use "
                + "the shortcut again.";
        }

        return "Your saved settings could not be used for an unattended start, "
            + "so nothing was started. Check the highlighted options here, then "
            + "use Start EverQuest for me.";
    }

    private void DetectRunningGameAtStartup()
    {
        if (_startupRunningGameDetectionAttempted)
        {
            return;
        }

        _startupRunningGameDetectionAttempted = true;
        if (App.StartupSwitches.SkipRunningGameDetection)
        {
            SetStatus(
                StatusTone.Info,
                "MANUAL MODE",
                "Automatic running-game detection was skipped because --manual "
                    + "was supplied. The normal controls remain available.");
            return;
        }

        StartupDiscovery discovery;
        try
        {
            discovery = FindRunningLegendsCandidates();
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            AdvancedExpander.IsExpanded = true;
            SetStatus(
                StatusTone.Warning,
                "AUTOMATIC DETECTION NEEDS ATTENTION",
                "Windows could not safely inventory every running eqgame.exe "
                    + "process. Nothing was resized or attached. The manual controls "
                    + "remain available. "
                    + exception.Message);
            return;
        }

        IReadOnlyList<StartupGameCandidate> candidates = discovery.Candidates;
        if (discovery.UninspectableProcessCount > 0)
        {
            AdvancedExpander.IsExpanded = true;
            SetStatus(
                StatusTone.Warning,
                "RUNNING CLIENT COULD NOT BE VERIFIED",
                "Windows reported an eqgame.exe process whose exact path or start "
                    + "time could not be inspected. Automatic scaling stopped "
                    + "without changing any game window. Close extra clients or use "
                    + "the manual controls after verifying the selected directory.");
            return;
        }

        if (candidates.Count == 0)
        {
            if (_activeRecoveryCount == 0 && !HasPendingPreparation)
            {
                SetStatus(
                    StatusTone.Ready,
                    "READY · CHOOSE SETTINGS, THEN LAUNCH",
                    "No running Legends client was found. Choose the UI percentage, "
                        + "text detail and compatibility options you want, then use Start EverQuest "
                        + "for me. SpinFOURKAYYY prepares the exact window before the "
                        + "normal launcher opens.");
            }

            return;
        }

        if (candidates.Count > 1)
        {
            AdvancedExpander.IsExpanded = true;
            SetStatus(
                StatusTone.Warning,
                "MULTIPLE LEGENDS CLIENTS FOUND",
                $"Automatic scaling requires one unambiguous client; "
                    + $"{candidates.Count} valid eqgame.exe processes are running. "
                    + "Close the extra client, then restart SpinFOURKAYYY. No game "
                    + "window or UI file was changed.");
            return;
        }

        StartupGameCandidate candidate = candidates[0];
        LegendsPathTextBox.Text = candidate.EqDirectory;
        RefreshSpinUiDetection(force: true);
        RefreshDisplayAndPlan();
        SetStatus(
            StatusTone.Warning,
            "RUNNING GAME LEFT UNTOUCHED",
            "EverQuest is already running. SpinFOURKAYYY will not resize, move, or "
                + "attach to a live game window. Exit EverQuest normally, choose your "
                + "settings here, then use Start EverQuest for me so the source "
                + "window and fullscreen mapping are prepared before launch.");
        RefreshActionAvailability();
    }

    private async Task AutoAttachRunningGameCoreAsync(
        StartupGameCandidate candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartupDiscovery currentDiscovery = FindRunningLegendsCandidates();
        IReadOnlyList<StartupGameCandidate> currentCandidates =
            currentDiscovery.Candidates;
        if (currentDiscovery.UninspectableProcessCount > 0
            || currentCandidates.Count != 1
            || currentCandidates[0].Process.ProcessId
                != candidate.Process.ProcessId
            || currentCandidates[0].Process.StartTimeUtc
                != candidate.Process.StartTimeUtc
            || !string.Equals(
                currentCandidates[0].Process.ExecutablePath,
                candidate.Process.ExecutablePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The running Legends process set changed during automatic detection. "
                    + "Nothing was attached; leave one client running and try again.");
        }

        _isUpdatingPresetCards = true;
        try
        {
            LegendsPathTextBox.Text = candidate.EqDirectory;
            CurrentUiModeRadioButton.IsChecked = true;
            SpinUiModeRadioButton.IsChecked = false;
            SpinUiLayoutReadyCheckBox.IsChecked = false;
            QualityComboBox.SelectedIndex = 0;
        }
        finally
        {
            _isUpdatingPresetCards = false;
        }

        using Process process = Process.GetProcessById(candidate.Process.ProcessId);
        IReadOnlyList<WindowDescriptor> initialWindows =
            _windowDiscovery.FindVisibleWindows(candidate.Process.ProcessId);
        if (initialWindows.Any(window => window.IsMinimized))
        {
            throw new InvalidOperationException(
                "The verified Legends window is minimized. Restore it in windowed "
                    + "mode, then exit it and launch through SpinFOURKAYYY. Nothing "
                    + "was resized.");
        }

        WindowDescriptor sourceWindow =
            await _windowDiscovery.WaitForStableVisibleWindowAsync(
                process,
                TimeSpan.FromSeconds(45),
                requiredStablePolls: 5,
                cancellationToken).ConfigureAwait(true);
        WindowDescriptor[] eligibleWindows =
            FindEligibleAutomaticWindows(
                candidate,
                _windowDiscovery.FindVisibleWindows(
                    candidate.Process.ProcessId));
        if (eligibleWindows.Length != 1
            || eligibleWindows[0].Handle != sourceWindow.Handle)
        {
            throw new InvalidOperationException(
                "The running Legends process does not have exactly one stable, "
                    + "unambiguous game window. No automatic resize or scaling was "
                    + "started.");
        }

        WindowRuntimeSnapshot sourceSnapshot =
            _foregroundWindow.InspectWindow(sourceWindow.Handle);
        if (!sourceSnapshot.IsValid
            || sourceSnapshot.ProcessId != candidate.Process.ProcessId
            || !string.Equals(
                sourceSnapshot.ClassName,
                sourceWindow.ClassName,
                StringComparison.Ordinal)
            || sourceSnapshot.ClientBounds is not { } inspectedClientBounds
            || !RectsMatch(
                inspectedClientBounds,
                sourceWindow.ClientBounds,
                tolerance: 1)
            || sourceSnapshot.MonitorHandle == nint.Zero)
        {
            throw new InvalidOperationException(
                "The running Legends window could not be bound to an exact display. "
                    + "No automatic resize or scaling was started.");
        }

        MonitorDescriptor targetMonitor = AutomaticAttachPolicy.ResolveTargetMonitor(
            sourceWindow.ClientBounds,
            _windowPlacement.GetMonitors());
        if (sourceSnapshot.MonitorHandle != targetMonitor.Handle)
        {
            throw new InvalidOperationException(
                "The Legends window spans displays ambiguously. Move it fully onto "
                    + "the display you want to use, then click Scale the running "
                    + "game. Nothing was resized.");
        }

        if (RectsMatch(
                sourceWindow.WindowBounds,
                targetMonitor.WorkArea,
                tolerance: 8)
            || RectsMatch(
                sourceWindow.ClientBounds,
                targetMonitor.Bounds,
                tolerance: 8))
        {
            throw new InvalidOperationException(
                "Legends already appears maximized or fullscreen. Put it in normal "
                    + "windowed mode, then exit it and launch through SpinFOURKAYYY; "
                    + "the program creates and validates the borderless fullscreen "
                    + "result itself.");
        }

        PopulateDisplays();
        DisplayChoice? targetDisplay = TargetDisplayComboBox.Items
            .OfType<DisplayChoice>()
            .SingleOrDefault(
                choice => choice.Monitor.Handle == targetMonitor.Handle
                    && choice.Monitor.Bounds == targetMonitor.Bounds);
        if (targetDisplay is null)
        {
            throw new InvalidOperationException(
                "The display containing the running Legends window is no longer "
                    + "available. No automatic resize or scaling was started.");
        }

        TargetDisplayComboBox.SelectedItem = targetDisplay;
        FineUiScale recommended = _activeLayoutSession is { } layoutSession
            && string.Equals(
                layoutSession.EqDirectory,
                candidate.EqDirectory,
                StringComparison.OrdinalIgnoreCase)
            && layoutSession.NativeResolution == targetDisplay.Monitor.Bounds.Size
                ? FineUiScale.FromSlider(
                    Math.Min(
                        (double)layoutSession.NativeResolution.Width
                            / layoutSession.ScaledResolution.Width,
                        (double)layoutSession.NativeResolution.Height
                            / layoutSession.ScaledResolution.Height))
                : new FineUiScale(FineUiScale.MinimumHundredths);
        _isUpdatingPresetCards = true;
        try
        {
            FineScaleSlider.Value = recommended.Factor;
            SyncGenericPresetSelection(recommended.Factor);
        }
        finally
        {
            _isUpdatingPresetCards = false;
        }

        RefreshSpinUiDetection(force: true);
        RefreshDisplayAndPlan();
        SetStatus(
            StatusTone.Working,
            "AUTO-ATTACHING RUNNING LEGENDS",
            _activeLayoutSession is null
                ? "A game that was started outside SpinFOURKAYYY is attached at "
                    + "Native pixels so its unprepared character layout cannot "
                    + "overlap. Choose a larger percentage before using Start "
                    + "EverQuest for me next time."
                : $"The prepared {FormatUiPercent(recommended.Factor)} character "
                    + $"layout matches {FormatPixels(targetDisplay.Monitor.Bounds.Size)}. "
                    + "Attaching without changing character settings.");
        await AttachCoreAsync(cancellationToken, candidate.Process)
            .ConfigureAwait(true);
    }

    private StartupDiscovery FindRunningLegendsCandidates()
    {
        List<StartupGameCandidate> candidates = [];
        IReadOnlyList<ProcessDescriptor> describedProcesses =
            _processDiscovery.FindByExecutableName("eqgame.exe");
        int uninspectableProcessCount =
            describedProcesses.Count(process => process.StartTimeUtc is null);
        HashSet<int> describedProcessIds =
            describedProcesses.Select(process => process.ProcessId).ToHashSet();
        try
        {
            foreach (Process process in Process.GetProcessesByName("eqgame"))
            {
                using (process)
                {
                    if (!describedProcessIds.Contains(process.Id))
                    {
                        uninspectableProcessCount++;
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or Win32Exception
                or NotSupportedException)
        {
            uninspectableProcessCount++;
        }

        foreach (ProcessDescriptor process in describedProcesses)
        {
            if (process.StartTimeUtc is null)
            {
                continue;
            }

            string? directory = Path.GetDirectoryName(process.ExecutablePath);
            if (directory is null
                || !IsVerifiedLegendsAutoAttachDirectory(directory))
            {
                continue;
            }

            candidates.Add(
                new StartupGameCandidate(
                    process,
                    Path.GetFullPath(directory)));
        }

        return new StartupDiscovery(
            candidates,
            uninspectableProcessCount);
    }

    private static bool IsVerifiedLegendsAutoAttachDirectory(string directory)
    {
        if (!PathLocator.IsLegendsDirectory(directory)
            || !File.Exists(Path.Combine(directory, "LaunchPad.exe")))
        {
            return false;
        }

        string launchPadIni = Path.Combine(directory, "LaunchPad.ini");
        if (!File.Exists(launchPadIni))
        {
            return false;
        }

        try
        {
            IniDocument document = IniDocument.Parse(
                File.ReadAllText(launchPadIni));
            return string.Equals(
                    document.GetValue("Settings", "appName")?.Trim(),
                    "EverQuest Legends",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    document.GetValue("Settings", "id")?.Trim(),
                    "eqns",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }

    private static WindowDescriptor[] FindEligibleAutomaticWindows(
        StartupGameCandidate candidate,
        IReadOnlyList<WindowDescriptor> windows)
    {
        return windows
            .Where(
                window =>
                    !window.IsMinimized
                    && window.Handle != nint.Zero
                    && window.ProcessId == candidate.Process.ProcessId
                    && window.ClientBounds.Width > 0
                    && window.ClientBounds.Height > 0
                    && !string.IsNullOrWhiteSpace(window.ClassName)
                    && !string.IsNullOrWhiteSpace(window.ExecutablePath)
                    && string.Equals(
                        Path.GetFullPath(window.ExecutablePath),
                        Path.GetFullPath(candidate.Process.ExecutablePath),
                        StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (!IsLoaded || _isBusy || _isCloseCleanupRunning)
        {
            return;
        }

        // The plan targets the monitor chosen in the combo box, not the one
        // under this window. Only a DPI change (crossing monitors) affects the
        // captured snapshot, so plain moves must not recompute plans or
        // overwrite the current status message.
        int dpiPercentage = (int)Math.Round(
            VisualTreeHelper.GetDpi(this).DpiScaleX * 100);
        if (_display is { } display && display.DpiPercentage == dpiPercentage)
        {
            return;
        }

        RefreshDisplayAndPlan();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        OpenFolderDialog dialog = new()
        {
            Title = "Choose the EverQuest Legends folder",
            Multiselect = false,
        };

        string currentPath = LegendsPathTextBox.Text.Trim();
        if (Directory.Exists(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        if (dialog.ShowDialog(this) == true)
        {
            LegendsPathTextBox.Text = dialog.FolderName;
        }
    }

    private void LegendsPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (!IsInitialized)
        {
            return;
        }

        if (PathLocator.IsLegendsDirectory(LegendsPathTextBox.Text))
        {
            _lastValidLegendsDirectory = Path.GetFullPath(
                LegendsPathTextBox.Text.Trim());
        }

        RefreshSpinUiDetection();
        if (IsLoaded)
        {
            RefreshDisplayAndPlan();
        }
        else
        {
            RefreshActionAvailability();
        }

        QueuePreferencesSave();
    }

    private void Preset_Checked(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (IsLoaded && !_isUpdatingPresetCards)
        {
            int selectedCardIndex = SelectedPresetCardIndex();
            if (UsesStrictSpinUiMode && selectedCardIndex >= 0)
            {
                _lastSpinUiPresetIndex = selectedCardIndex;
            }

            if (!UsesStrictSpinUiMode)
            {
                _isUpdatingPresetCards = true;
                try
                {
                    FineScaleSlider.Value = SelectedPresetCardScale();
                }
                finally
                {
                    _isUpdatingPresetCards = false;
                }
            }

            string? filterNotice = NormalizeFilterForPreset();
            RefreshDisplayAndPlan();
            if (filterNotice is not null)
            {
                SetStatus(StatusTone.Info, "FILTER ADJUSTED", filterNotice);
            }

            QueuePreferencesSave();
        }
    }

    private string? ResolveSpinTextureExecutablePath()
    {
        if (PathLocator.IsSpinTextureExecutable(_spinTextureExecutablePath))
        {
            return Path.GetFullPath(_spinTextureExecutablePath!);
        }

        string? detected = PathLocator.FindSpinTextureExecutable();
        if (detected is not null)
        {
            return detected;
        }

        OpenFileDialog dialog = new()
        {
            Title = "Choose SpinTexture.exe",
            Filter = "SpinTexture application (SpinTexture.exe)|SpinTexture.exe",
            FileName = "SpinTexture.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return null;
        }

        if (!PathLocator.IsSpinTextureExecutable(dialog.FileName))
        {
            ShowError(
                "SpinTexture was not selected",
                "Choose the real SpinTexture.exe from its fully extracted folder. "
                    + "SpinFOURKAYYY will only use SpinTexture's verified Enhanced "
                    + "EQ launch flow.");
            return null;
        }

        return Path.GetFullPath(dialog.FileName);
    }

    private void FineScaleSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        _ = sender;
        if (!IsLoaded
            || _isUpdatingPresetCards
            || UsesStrictSpinUiMode)
        {
            return;
        }

        _isUpdatingPresetCards = true;
        try
        {
            SyncGenericPresetSelection(e.NewValue);
        }
        finally
        {
            _isUpdatingPresetCards = false;
        }

        string? filterNotice = NormalizeFilterForPreset();
        RefreshDisplayAndPlan();
        if (filterNotice is not null)
        {
            SetStatus(StatusTone.Info, "FILTER ADJUSTED", filterNotice);
        }

        QueuePreferencesSave();
    }

    /// <summary>
    /// While a generic/custom-UI session is active, slider movement applies to
    /// the running game after a short debounce — no restart, no INI write.
    /// </summary>
    private void QueuePendingLiveScale()
    {
        if (!_isScaledSessionActive
            || _scalingCleanupRequired
            || _isCloseCleanupRunning
            || _activeLaunch is not { } launch
            || launch.UiCompatibilityMode
                != FourKayUiCompatibilityMode.GenericOrCustom
            || launch.EffectiveFilter == ScalingFilter.NearestNeighbor)
        {
            return;
        }

        _pendingLiveScale = FineUiScale.FromSlider(FineScaleSlider.Value);
        _liveScaleDebounceTimer.Stop();
        _liveScaleDebounceTimer.Start();
    }

    private void FineScaleStep_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (!IsLoaded || !FineScaleSlider.IsEnabled)
        {
            return;
        }

        int direction = ReferenceEquals(sender, FineScaleMinusButton) ? -1 : 1;
        FineUiScale current = FineUiScale.FromSlider(FineScaleSlider.Value);
        FineScaleSlider.Value =
            GenericUiScalePolicy.Step(current, direction).Factor;
    }

    private async void LiveScaleDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _liveScaleDebounceTimer.Stop();
        if (_pendingLiveScale is not { } requestedScale
            || !_isScaledSessionActive
            || _scalingCleanupRequired
            || _isCloseCleanupRunning
            || _activeLaunch is not { } expectedLaunch)
        {
            _pendingLiveScale = null;
            return;
        }

        if (_isBusy)
        {
            _liveScaleDebounceTimer.Start();
            return;
        }

        _pendingLiveScale = null;
        await RunOperationAsync(
            "NEW LAUNCH REQUIRED",
            $"A {FormatUiPercent(requestedScale.Factor)} source needs a fresh "
                + "managed EverQuest launch…",
            token => AdjustLiveScaleCoreAsync(
                requestedScale,
                expectedLaunch,
                token))
            .ConfigureAwait(true);
    }

    private void ClaritySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        _ = sender;
        _ = e;
        if (!IsInitialized)
        {
            return;
        }

        ClarityValueText.Text =
            $"{Math.Round(ClaritySlider.Value):0}%";
        if (!IsLoaded || _isUpdatingPresetCards)
        {
            return;
        }

        // Clarity is a launch setting. The control is disabled while a managed
        // session is active so the running game window is never re-attached.
        QueuePreferencesSave();
    }

    private async void ClarityDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _clarityDebounceTimer.Stop();
        if (!_clarityReapplyPending
            || !_isScaledSessionActive
            || _scalingCleanupRequired
            || _isCloseCleanupRunning
            || _activeLaunch is null)
        {
            _clarityReapplyPending = false;
            return;
        }

        if (_isBusy)
        {
            _clarityDebounceTimer.Start();
            return;
        }

        _clarityReapplyPending = false;
        await RunOperationAsync(
            "APPLYING TEXT EDGE DETAIL",
            $"Restarting the fullscreen output at "
                + $"{Math.Round(ClaritySlider.Value):0}% edge detail…",
            ReapplyClarityCoreAsync).ConfigureAwait(true);
    }

    /// <summary>
    /// The engine reads its sharpening strength at startup, so a live clarity
    /// change stops the exact owned output and immediately re-attaches at the
    /// same size with the new strength. No game restart, no INI write.
    /// </summary>
    private async Task ReapplyClarityCoreAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Text detail and compatibility anti-aliasing are applied only during a managed launch. "
                + "Stop scaling, exit EverQuest, choose the new settings, then use "
                + "Start EverQuest for me.");
    }

    private void TargetDisplayComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (IsLoaded)
        {
            RefreshDisplayAndPlan();
            QueuePreferencesSave();
        }
    }

    private void PreferenceSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        QueuePreferencesSave();
    }

    private void PreferenceToggle_Changed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        QueuePreferencesSave();
    }

    private void QualityComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (IsLoaded && !_isUpdatingPresetCards)
        {
            if (SelectedFilter() == ScalingFilter.Nis)
            {
                // Readable UI deliberately owns reconstruction and sharpening in
                // one pass. Never carry a softening AA override into that preset.
                AntiAliasingComboBox.SelectedIndex = 0;
            }

            string? filterNotice = NormalizeFilterForPreset();
            RefreshDisplayAndPlan();
            if (filterNotice is not null)
            {
                SetStatus(StatusTone.Info, "FILTER ADJUSTED", filterNotice);
            }

            QueuePreferencesSave();
        }
    }

    private void UiSessionMode_Checked(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!IsLoaded || _isUpdatingPresetCards)
        {
            return;
        }

        SpinUiLayoutReadyCheckBox.IsChecked = false;
        string? filterNotice = null;
        if (!UsesStrictSpinUiMode)
        {
            filterNotice = NormalizeFilterForPreset();
        }

        RefreshDisplayAndPlan();
        if (filterNotice is not null)
        {
            SetStatus(StatusTone.Info, "FILTER ADJUSTED", filterNotice);
        }

        if (_activeState is not null)
        {
            SetRecoveryStatus();
        }

        QueuePreferencesSave();
    }

    private void SpinUiLayoutReady_Checked(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (IsLoaded
            && !_isBusy
            && !_isScaledSessionActive
            && !_scalingCleanupRequired
            && _activeState is null)
        {
            SetSpinUiReadinessStatus();
        }

        RefreshActionAvailability();
    }

    private void ResetSessionUiConfirmations()
    {
        bool wasUpdating = _isUpdatingPresetCards;
        _isUpdatingPresetCards = true;
        try
        {
            SpinUiLayoutReadyCheckBox.IsChecked = false;
        }
        finally
        {
            _isUpdatingPresetCards = wasUpdating;
        }
    }

    private void RefreshSpinUiDetection(bool force = false)
    {
        string candidate = LegendsPathTextBox.Text.Trim();
        if (!PathLocator.IsLegendsDirectory(candidate))
        {
            bool wasDetected = _spinUiDetection.Status
                != SpinUiDetectionStatus.NotDetected;
            _spinUiDetectionDirectory = null;
            _spinUiDetection = new SpinUiDetectionResult(
                SpinUiDetectionStatus.NotDetected,
                ThemeDirectoryPresent: false,
                MatchingLayouts: [],
                Issue: null);
            _spinUiPlans = [];
            if (wasDetected)
            {
                SpinUiLayoutReadyCheckBox.IsChecked = false;
            }

            return;
        }

        string fullDirectory = Path.GetFullPath(candidate);
        if (!force
            && string.Equals(
                _spinUiDetectionDirectory,
                fullDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SpinUiDetectionResult previous = _spinUiDetection;
        SpinUiDetectionResult detected;
        try
        {
            detected = SpinUiDetector.Detect(fullDirectory);
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            detected = new SpinUiDetectionResult(
                SpinUiDetectionStatus.Uncertain,
                ThemeDirectoryPresent: true,
                MatchingLayouts: [],
                Issue: "SpinUI detection could not finish reliably: " + exception.Message);
        }

        _spinUiDetectionDirectory = fullDirectory;
        _spinUiDetection = detected;
        bool materiallyChanged =
            previous.Status != detected.Status
            || !previous.MatchingLayouts.SequenceEqual(detected.MatchingLayouts);
        if (materiallyChanged)
        {
            bool wasUpdating = _isUpdatingPresetCards;
            _isUpdatingPresetCards = true;
            try
            {
                SpinUiLayoutReadyCheckBox.IsChecked = false;
            }
            finally
            {
                _isUpdatingPresetCards = wasUpdating;
            }
        }
    }

    private async void PrepareAndLaunch_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        await RunOperationAsync(
            "PREPARING AND STARTING EVERQUEST",
            "Saving the selected source resolution, then opening the normal "
                + "Legends launcher…",
            token => LaunchThenAutoScaleCoreAsync(
                FourKayGameStartMode.OfficialLauncher,
                launchTargetPath: null,
                token)).ConfigureAwait(true);
    }

    private async void PlayEnhanced_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        string? spinTexturePath = ResolveSpinTextureExecutablePath();
        if (spinTexturePath is null)
        {
            return;
        }

        _spinTextureExecutablePath = spinTexturePath;
        QueuePreferencesSave();
        await RunOperationAsync(
            "PREPARING ENHANCED EVERQUEST",
            "Preparing the selected UI size, then asking SpinTexture to verify "
                + "and play the installed enhanced pack…",
            token => LaunchThenAutoScaleCoreAsync(
                FourKayGameStartMode.SpinTextureEnhanced,
                spinTexturePath,
                token)).ConfigureAwait(true);
    }

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (_isScaledSessionActive || _scalingCleanupRequired)
        {
            _liveScaleDebounceTimer.Stop();
            _pendingLiveScale = null;
            await RunOperationAsync(
                "STOPPING",
                "Stopping the fullscreen output safely…",
                StopScalingCoreAsync).ConfigureAwait(true);
            return;
        }

        ShowError(
            "Launch through SpinFOURKAYYY",
            "Live attachment is disabled because resizing an already-running "
                + "EverQuest window can leave it maximized, overlap personal UI "
                + "layouts, or break Magpie's mouse mapping. Exit EverQuest, choose "
                + "your settings, then use Start EverQuest for me.");
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        int recoveryCount = Math.Max(
            _activeRecoveryCount,
            HasPendingPreparation ? 1 : 0);
        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Restore {recoveryCount} saved SpinFOURKAYYY player-profile "
                + (recoveryCount == 1 ? "snapshot" : "snapshots")
                + " now?\n\n"
                + "EverQuest Legends, LaunchPad, and Options Editor must be closed. "
                + "Each verified snapshot is restored newest-first. Any layout, "
                + "hotbar, macro, spell-set, keybind, userdata, or client-settings "
                + "copy changed during the temporary session is preserved in a "
                + "recovery backup before the pre-session profile is restored.",
            "Restore saved SpinFOURKAYYY player profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOperationAsync(
            "RESTORING PLAYER PROFILE",
            "Restoring every verified player-profile snapshot newest-first…",
            RestoreAllCoreAsync).ConfigureAwait(true);
    }

    private void OpenBackup_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        string path = _activeState is null
            ? PathLocator.BackupRoot
            : Path.GetDirectoryName(_activeState.BackupManifestPath)
                ?? PathLocator.BackupRoot;
        try
        {
            OpenFolder(path);
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            ShowError("Could not open the backup folder", exception.Message);
        }
    }

    private void OpenEngine_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        try
        {
            OpenFolder(PathLocator.FindMagpieDirectory());
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            ShowError("Scaling engine not found", exception.Message);
        }
    }

    private void MakeShortcut_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Button button || button.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 4;
        menu.IsOpen = true;
    }

    private void MakeNormalShortcut_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CreateDesktopShortcut(DesktopShortcutKind.NormalPlay);
    }

    private void MakeEnhancedShortcut_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CreateDesktopShortcut(DesktopShortcutKind.EnhancedPlay);
    }

    /// <summary>
    /// Writes one desktop shortcut that reopens SpinFOURKAYYY with the matching
    /// play switch. Replacing an existing shortcut is confirmed first, because
    /// a file on the user's desktop may have been customized by hand.
    /// </summary>
    private void CreateDesktopShortcut(DesktopShortcutKind kind)
    {
        try
        {
            // The release is published as a single file, so the running
            // executable path is the only usable shortcut target.
            string applicationPath = Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "The running SpinFOURKAYYY executable could not be located, "
                        + "so no shortcut was created.");
            string? legendsDirectory =
                PathLocator.IsLegendsDirectory(LegendsPathTextBox.Text)
                    ? Path.GetFullPath(LegendsPathTextBox.Text)
                    : _lastValidLegendsDirectory;
            DesktopShortcutPlan plan = DesktopShortcutPlan.Create(
                kind,
                applicationPath,
                legendsDirectory);

            string desktopDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(desktopDirectory)
                || !Directory.Exists(desktopDirectory))
            {
                ShowError(
                    "Desktop folder not found",
                    "Windows did not report a usable desktop folder, so no "
                        + "shortcut was created. Nothing else was changed.");
                return;
            }

            string destination = plan.ResolveDestinationPath(desktopDirectory);
            if (File.Exists(destination)
                && MessageBox.Show(
                    this,
                    $"Replace the existing \"{plan.FileName}\" on your desktop?"
                        + "\n\nThe replacement starts EverQuest with the settings "
                        + "saved in SpinFOURKAYYY. Any changes you made to the "
                        + "existing shortcut yourself would be lost.",
                    "Replace desktop shortcut",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                SetStatus(
                    StatusTone.Info,
                    "SHORTCUT LEFT ALONE",
                    $"The existing \"{plan.FileName}\" on your desktop was not "
                        + "changed.");
                return;
            }

            _ = WindowsShortcutService.Save(plan, destination);
            SetStatus(
                StatusTone.Ready,
                "DESKTOP SHORTCUT READY",
                $"\"{plan.FileName}\" is on your desktop. Opening it starts "
                    + (kind == DesktopShortcutKind.EnhancedPlay
                        ? "the enhanced texture pack through SpinTexture "
                        : "EverQuest through the normal launcher ")
                    + "using the settings saved here. "
                    + (plan.UsesLegendsIcon
                        ? "It uses the EverQuest Legends icon from your "
                            + "installed client."
                        : "It uses the SpinFOURKAYYY icon because no EverQuest "
                            + "Legends folder is selected yet."));
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            ShowError("Could not create the shortcut", exception.Message);
        }
    }

    private void OpenSpinUi_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        try
        {
            OpenSpinUiRepository();
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            ShowError(
                "Could not open SpinUI",
                "The SpinUI repository could not be opened in your default browser. "
                    + "Visit https://github.com/itsspin/spinips manually.\n\n"
                    + exception.Message);
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _ = sender;

        if (_allowCloseAfterCleanup)
        {
            return;
        }

        e.Cancel = true;
        if (_isCloseCleanupRunning)
        {
            return;
        }

        if (_isCheckingForUpdates || _isInstallingUpdate)
        {
            _closeRequestedDuringUpdate = true;
            _updateCancellation?.Cancel();
            SetStatus(
                StatusTone.Working,
                "FINISHING UPDATE ACTIVITY",
                "Cancelling the current download or update check safely, then closing.");
            return;
        }

        _preferencesSaveTimer.Stop();
        await SavePreferencesSafelyAsync().ConfigureAwait(true);
        _isCloseCleanupRunning = true;
        _scalingHealthTimer.Stop();
        _overlayCompatibilityTimer.Stop();
        _liveScaleDebounceTimer.Stop();
        _pendingLiveScale = null;
        _clarityDebounceTimer.Stop();
        _clarityReapplyPending = false;
        _operationCancellation?.Cancel();
        SetStatus(
            StatusTone.Working,
            "CLOSING SAFELY",
            "Waiting for the current action, then verifying that fullscreen scaling "
                + "and the dedicated tray engine are both stopped.");
        OperationProgressBar.IsIndeterminate = true;
        RefreshActionAvailability();

        try
        {
            Task? operation = _inFlightOperation;
            if (operation is not null)
            {
                await operation.ConfigureAwait(true);
            }

            Task? healthCheck = _scalingHealthCheckTask;
            if (healthCheck is not null)
            {
                await healthCheck.ConfigureAwait(true);
            }

            ScalingCleanupResult cleanup = await StopAndConfirmOwnedScalingAsync(
                TimeSpan.FromSeconds(12),
                CancellationToken.None).ConfigureAwait(true);
            if (!cleanup.Success)
            {
                throw new InvalidOperationException(cleanup.Message);
            }

            if (_activeLayoutSession is { } layoutState
                && IsLegendsGameRunning(layoutState.EqDirectory))
            {
                throw new InvalidOperationException(
                    "EverQuest is still running with an automatic personal UI layout. "
                        + "Exit the game first so SpinFOURKAYYY can save any layout "
                        + "changes for this percentage and restore the native layout.");
            }

            string? layoutMessage =
                await FinalizeLayoutProfileIfGameClosedAsync(
                    CancellationToken.None).ConfigureAwait(true);

            CompleteScalingOwnership(
                layoutMessage
                    ?? "Fullscreen and the dedicated scaling engine stopped cleanly.");
            _allowCloseAfterCleanup = true;
            Close();
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            _isCloseCleanupRunning = false;
            OperationProgressBar.IsIndeterminate = false;
            OperationProgressBar.Value = _isScaledSessionActive ? 100 : 0;
            _scalingHealthTimer.Start();
            _overlayCompatibilityTimer.Start();
            SetStatus(
                StatusTone.Error,
                "CLOSE BLOCKED — FINISHING SAFELY",
                exception.Message
                    + " The controller is staying open so it can finish safely.");
            RefreshActionAvailability();
            ShowError(
                "SpinFOURKAYYY stayed open for safety",
                exception.Message
                    + "\n\nExit the game, then close SpinFOURKAYYY again. If only "
                    + "fullscreen cleanup is pending, Alt+Shift+A can stop it first.");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        Dispose();
    }

    public void Dispose()
    {
        _scalingHealthTimer.Stop();
        _scalingHealthTimer.Tick -= ScalingHealthTimer_Tick;
        _overlayCompatibilityTimer.Stop();
        _overlayCompatibilityTimer.Tick -= OverlayCompatibilityTimer_Tick;
        _liveScaleDebounceTimer.Stop();
        _liveScaleDebounceTimer.Tick -= LiveScaleDebounceTimer_Tick;
        _clarityDebounceTimer.Stop();
        _clarityDebounceTimer.Tick -= ClarityDebounceTimer_Tick;
        _preferencesSaveTimer.Stop();
        _preferencesSaveTimer.Tick -= PreferencesSaveTimer_Tick;
        _preferencesStore.Dispose();
        _updateService.Dispose();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        RestoreOverlayCompatibility(_activeLaunch);
        DisposeLaunchHandles(_activeLaunch);
        _activeLaunch = null;
        _pendingScalingCleanupLease?.Dispose();
        _pendingScalingCleanupLease = null;
        GC.SuppressFinalize(this);
    }

    private void OverlayCompatibilityTimer_Tick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_activeLaunch is not { OverlayCompatibility: { IsActive: true } overlays }
            || (!_isScaledSessionActive && !_scalingCleanupRequired))
        {
            return;
        }

        int tick = ++_overlayCompatibilityTickCount;
        bool regularMaintenance = tick % 12 == 0;
        bool discoverNewWindows = tick % 100 == 0;
        OverlayCompatibilityUpdate update;
        if (regularMaintenance || discoverNewWindows)
        {
            nint foregroundWindow = _foregroundWindow.GetForegroundWindowHandle();
            bool exactGameSessionIsForeground =
                foregroundWindow == _activeLaunch.SourceWindow.Handle
                || foregroundWindow == overlays.ScalingWindowHandle;
            update = _overlayCompatibility.Maintain(
                overlays,
                exactGameSessionIsForeground,
                discoverNewWindows);
        }
        else
        {
            update = _overlayCompatibility.RefreshInputHandoff(overlays);
        }
        if (update.Warnings.Count > _displayedOverlayWarningCount)
        {
            string[] newWarnings = update.Warnings
                .Skip(_displayedOverlayWarningCount)
                .ToArray();
            _displayedOverlayWarningCount = update.Warnings.Count;
            SetStatus(
                StatusTone.Warning,
                "OVERLAY COMPATIBILITY NOTE",
                string.Join(" ", newWarnings)
                    + " Fullscreen scaling and its verified mouse map remain active.");
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await CheckForUpdatesAsync(
            showCurrentVersionMessage: true,
            offerInstall: true).ConfigureAwait(true);
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await InstallAvailableUpdateAsync().ConfigureAwait(true);
    }

    private async Task CheckForUpdatesAsync(
        bool showCurrentVersionMessage,
        bool offerInstall)
    {
        if (_isCheckingForUpdates || _isCloseCleanupRunning)
        {
            return;
        }

        _isCheckingForUpdates = true;
        CheckUpdatesButton.Content = "Checking…";
        CheckUpdatesButton.IsEnabled = false;
        bool installRequested = false;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        _updateCancellation = timeout;
        try
        {
            ReleaseUpdateCheck check = await _updateService.CheckAsync(
                CurrentAppVersion,
                timeout.Token).ConfigureAwait(true);
            if (!check.IsUpdateAvailable)
            {
                _availableUpdate = null;
                UpdateAvailableBorder.Visibility = Visibility.Collapsed;
                if (showCurrentVersionMessage)
                {
                    _ = MessageBox.Show(
                        this,
                        $"SpinFOURKAYYY {CurrentAppVersion.ToString(3)} is the "
                            + "latest completed GitHub Release.",
                        "SpinFOURKAYYY is up to date",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else
            {
                _availableUpdate = check.LatestRelease;
                UpdateAvailableTitleText.Text =
                    $"SpinFOURKAYYY {check.LatestRelease.Version.ToString(3)} is available";
                UpdateAvailableDetailText.Text =
                    $"You have {check.CurrentVersion.ToString(3)}. Install the verified "
                        + "GitHub Release, keep every saved setting, and reopen "
                        + "automatically.";
                UpdateAvailableBorder.Visibility = Visibility.Visible;
                RefreshActionAvailability();
                if (offerInstall && CanInstallAvailableUpdate())
                {
                    MessageBoxResult choice = MessageBox.Show(
                        this,
                        $"SpinFOURKAYYY {check.LatestRelease.Version.ToString(3)} is "
                            + "available.\n\nInstall it now? The download is verified "
                            + "against GitHub's SHA-256 digest and release checksum. "
                            + "SpinFOURKAYYY will close, keep your settings, install, "
                            + "and reopen automatically.",
                        "SpinFOURKAYYY update available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information,
                        MessageBoxResult.Yes);
                    installRequested = choice == MessageBoxResult.Yes;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (showCurrentVersionMessage && !_closeRequestedDuringUpdate)
            {
                ShowError(
                    "Update check timed out",
                    "GitHub did not respond in time. Your current version is "
                        + "unchanged; try Check updates again later.");
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or IOException
                or InvalidDataException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            if (showCurrentVersionMessage)
            {
                ShowError(
                    "Could not check for updates",
                    "Your current installation was not changed. " + exception.Message);
            }
            else
            {
                Debug.WriteLine(
                    "SpinFOURKAYYY automatic update check skipped: "
                        + exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_updateCancellation, timeout))
            {
                _updateCancellation = null;
            }

            _isCheckingForUpdates = false;
            CheckUpdatesButton.Content = "Check updates";
            RefreshActionAvailability();
        }

        if (_closeRequestedDuringUpdate)
        {
            _ = Dispatcher.BeginInvoke(Close);
            return;
        }

        if (installRequested)
        {
            await InstallAvailableUpdateAsync().ConfigureAwait(true);
        }
    }

    private async Task InstallAvailableUpdateAsync()
    {
        if (_availableUpdate is not { } release)
        {
            return;
        }

        if (!CanInstallAvailableUpdate())
        {
            _ = MessageBox.Show(
                this,
                "Finish the current action and exit EverQuest before installing. "
                    + "The update remains available in the green banner.",
                "Finish the current session first",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _isBusy = true;
        _isInstallingUpdate = true;
        bool installerStarted = false;
        OperationProgressBar.IsIndeterminate = false;
        OperationProgressBar.Value = 0;
        SetStatus(
            StatusTone.Working,
            $"DOWNLOADING {release.Version.ToString(3)}",
            "Downloading the completed GitHub Release, then verifying its asset "
                + "digest, checksum, package paths, manifest, and every app file.");
        RefreshActionAvailability();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(10));
        _updateCancellation = timeout;
        try
        {
            await SavePreferencesSafelyAsync().ConfigureAwait(true);
            Progress<int> progress = new(value =>
            {
                OperationProgressBar.Value = value;
                if (value >= 92)
                {
                    SetStatus(
                        StatusTone.Working,
                        "VERIFYING UPDATE",
                        "The package checksum passed. Verifying every staged app "
                            + "file before SpinFOURKAYYY closes.");
                }
            });
            PreparedReleaseUpdate prepared = await _updateService.PrepareAsync(
                release,
                progress,
                timeout.Token).ConfigureAwait(true);
            string currentExecutable = Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "Windows did not expose the current SpinFOURKAYYY executable path.");
            using Process installer =
                await GitHubReleaseUpdateService.StartInstallerAsync(
                    prepared,
                    currentExecutable,
                    Environment.ProcessId,
                    CurrentAppVersion,
                    CancellationToken.None).ConfigureAwait(true);
            installerStarted = true;
            _allowCloseAfterCleanup = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            if (!_closeRequestedDuringUpdate)
            {
                ShowError(
                    "Update cancelled",
                    "The update download did not finish. Your current installation and "
                        + "saved settings were not changed.");
            }
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            ShowError(
                "Could not install the update",
                "Your current installation and saved settings were not changed. "
                    + exception.Message);
        }
        finally
        {
            _updateCancellation = null;
            _isInstallingUpdate = false;
            if (!installerStarted)
            {
                _isBusy = false;
                OperationProgressBar.IsIndeterminate = false;
                OperationProgressBar.Value = _isScaledSessionActive ? 100 : 0;
                RefreshActionAvailability();
                if (_closeRequestedDuringUpdate)
                {
                    _ = Dispatcher.BeginInvoke(Close);
                }
            }
        }
    }

    private bool CanInstallAvailableUpdate() =>
        _availableUpdate is not null
        && !_isBusy
        && !_isCloseCleanupRunning
        && !_isScaledSessionActive
        && !_scalingCleanupRequired
        && _activeLaunch is null
        && _activeLayoutSession is null
        && !HasPendingPreparation;

    private static Version GetCurrentAppVersion()
    {
        Assembly assembly = typeof(MainWindow).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        string numericVersion = (informationalVersion ?? string.Empty)
            .Split('+', 2)[0]
            .Split('-', 2)[0];
        if (Version.TryParse(numericVersion, out Version? parsed)
            && parsed.Build >= 0)
        {
            return GitHubReleaseUpdateService.NormalizeThreePartVersion(parsed);
        }

        return GitHubReleaseUpdateService.NormalizeThreePartVersion(
            assembly.GetName().Version ?? new Version(1, 0, 5));
    }

    private async void ScalingHealthTimer_Tick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if ((!_isScaledSessionActive
                && !_scalingCleanupRequired
                && _activeLayoutSession is null)
            || _isBusy
            || _isCloseCleanupRunning
            || _isScalingHealthCheckRunning)
        {
            return;
        }

        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task trackedHealthCheck = completion.Task;
        _scalingHealthCheckTask = trackedHealthCheck;
        _isScalingHealthCheckRunning = true;
        try
        {
            if (_activeLaunch is not { } activeLaunch)
            {
                if (_scalingCleanupRequired)
                {
                    await RetryPendingScalingCleanupAsync().ConfigureAwait(true);
                }

                string? layoutMessage =
                    await FinalizeLayoutProfileIfGameClosedAsync(
                        CancellationToken.None).ConfigureAwait(true);
                if (layoutMessage is not null)
                {
                    SetStatus(
                        StatusTone.Ready,
                        "PERSONAL UI LAYOUT SAVED",
                        layoutMessage);
                }

                return;
            }

            MagpieScalingWindowInspection inspection =
                _scalingInspector.Inspect(activeLaunch.SourceWindow.Handle);
            // Player configuration is never watched or enforced: whatever the
            // game saves to eqclient.ini during play is the player's business.
            const bool nativeUiScaleSafe = true;
            const string? nativeUiScaleIssue = null;
            if (!ReferenceEquals(_activeLaunch, activeLaunch))
            {
                return;
            }

            WindowRuntimeSnapshot sourceSnapshot =
                _foregroundWindow.InspectWindow(activeLaunch.SourceWindow.Handle);
            bool infrastructureHealthy =
                IsProcessAlive(activeLaunch.MagpieProcess.Process);
            bool sourceHealthy =
                IsProcessAlive(activeLaunch.GameProcess)
                && SourceWindowMatchesLaunch(activeLaunch, sourceSnapshot);
            nint foregroundWindow =
                _foregroundWindow.GetForegroundWindowHandle();
            bool sourceIsForeground =
                foregroundWindow == activeLaunch.SourceWindow.Handle
                || (inspection.IsActive
                    && inspection.ScalingProcessId
                        == activeLaunch.MagpieProcess.Process.Id
                    && foregroundWindow == inspection.ScalingWindowHandle);
            ScalingOutputValidation outputValidation =
                ClassifyScalingOutput(activeLaunch, sourceSnapshot, inspection);

            ScalingSessionSupervisionState previousState =
                _scalingSupervisionState;
            ScalingSessionSupervisionDecision decision =
                ScalingSessionSupervisionPolicy.Evaluate(
                    previousState,
                    new ScalingSessionObservation(
                        GetMonotonicSupervisionTime(),
                        infrastructureHealthy,
                        nativeUiScaleSafe,
                        sourceHealthy,
                        sourceIsForeground,
                        outputValidation),
                    FullscreenResumeGrace);
            _scalingSupervisionState = decision.State;
            ScalingSupervisionStopReason stopReason = decision.StopReason;

            if (!_scalingCleanupRequired
                && decision.Action == ScalingSupervisionAction.Continue)
            {
                ApplyScalingSupervisionStatus(previousState, decision.State);
                return;
            }

            _scalingCleanupRequired = true;
            SetScalingCleanupStatus(
                stopReason,
                inspection,
                nativeUiScaleIssue);
            ScalingCleanupResult cleanup = await StopAndConfirmOwnedScalingAsync(
                TimeSpan.FromSeconds(6),
                CancellationToken.None).ConfigureAwait(true);
            if (!cleanup.Success)
            {
                SetStatus(
                    StatusTone.Error,
                    "SCALING CLEANUP REQUIRED",
                    cleanup.Message
                        + " Monitoring remains active; use Alt+Shift+A or exit the game "
                        + "if the next automatic retry cannot stop it.");
                return;
            }

            string? completedLayoutMessage =
                await FinalizeLayoutProfileIfGameClosedAsync(
                    CancellationToken.None).ConfigureAwait(true);
            CompleteScalingOwnership(
                completedLayoutMessage
                    ?? ScalingCleanupCompletionMessage(stopReason));
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            _scalingCleanupRequired = true;
            SetStatus(
                StatusTone.Error,
                "INPUT CHECK NEEDS ATTENTION",
                "The live mouse-map cleanup could not finish: "
                    + exception.Message
                    + " Monitoring and session ownership remain active.");
        }
        finally
        {
            _isScalingHealthCheckRunning = false;
            RefreshActionAvailability();
            if (ReferenceEquals(_scalingHealthCheckTask, trackedHealthCheck))
            {
                _scalingHealthCheckTask = null;
            }

            completion.TrySetResult();
        }
    }

    private async Task RetryPendingScalingCleanupAsync()
    {
        _scalingCleanupRequired = true;
        SetStatus(
            StatusTone.Working,
            "FINISHING SCALING CLEANUP",
            "The controller is retrying exact owned-engine and portable-config cleanup.");
        ScalingCleanupResult cleanup = await StopAndConfirmOwnedScalingAsync(
            TimeSpan.FromSeconds(6),
            CancellationToken.None).ConfigureAwait(true);
        if (!cleanup.Success)
        {
            SetStatus(
                StatusTone.Error,
                "SCALING CLEANUP REQUIRED",
                cleanup.Message + " Monitoring remains active for another exact retry.");
            return;
        }

        CompleteScalingOwnership(
            "The pending fullscreen engine and portable configuration are confirmed clean.");
    }

    private static bool IsProcessAlive(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or Win32Exception
                or NotSupportedException)
        {
            return false;
        }
    }

    private DateTimeOffset GetMonotonicSupervisionTime()
    {
        return _supervisionClockOriginUtc
            + Stopwatch.GetElapsedTime(_supervisionTimestampOrigin);
    }

    private static bool SourceWindowMatchesLaunch(
        FourKayLaunchResult launch,
        WindowRuntimeSnapshot snapshot)
    {
        return snapshot.IsValid
            && snapshot.ProcessId == launch.GameProcess.Id
            && snapshot.ProcessId == launch.SourceWindow.ProcessId
            && string.Equals(
                snapshot.ClassName,
                launch.SourceWindow.ClassName,
                StringComparison.Ordinal)
            && snapshot.ClientBounds is { } clientBounds
            && RectSizeMatches(
                clientBounds,
                launch.Placement.RequestedClientSize,
                tolerance: 2)
            && snapshot.MonitorHandle == launch.Placement.Monitor.Handle;
    }

    private static ScalingOutputValidation ClassifyScalingOutput(
        FourKayLaunchResult launch,
        WindowRuntimeSnapshot sourceSnapshot,
        MagpieScalingWindowInspection inspection)
    {
        if (!inspection.IsActive)
        {
            return ScalingOutputValidation.Inactive;
        }

        if (inspection.ScalingProcessId != launch.MagpieProcess.Process.Id
            || inspection.SourceWindowHandle != launch.SourceWindow.Handle
            || inspection.IsWindowedScaling)
        {
            return ScalingOutputValidation.Unsafe;
        }

        PixelRect expectedMonitor = launch.Placement.Monitor.Bounds;
        if (inspection.MonitorBounds is { } monitor
            && !RectsMatch(monitor, expectedMonitor, tolerance: 1))
        {
            return ScalingOutputValidation.Unsafe;
        }

        PixelSize expectedSource = launch.Placement.RequestedClientSize;
        if (inspection.SourceRegion is { } sourceRegion
            && !RectSizeMatches(sourceRegion, expectedSource, tolerance: 2))
        {
            return ScalingOutputValidation.Unsafe;
        }

        if (inspection.SourceRegion is { } mappedSource
            && sourceSnapshot.ClientBounds is { } currentClient
            && !RectsMatch(mappedSource, currentClient, tolerance: 2))
        {
            return ScalingOutputValidation.Unsafe;
        }

        if (inspection.DestinationRegion is { } destination
            && !RectsMatch(
                destination,
                launch.ExpectedDestinationRegion,
                tolerance: 2))
        {
            return ScalingOutputValidation.Unsafe;
        }

        if (inspection.CoordinateValidation is { IsSafe: false })
        {
            return ScalingOutputValidation.Unsafe;
        }

        bool exactGeometryAvailable =
            inspection.MonitorBounds is not null
            && inspection.SourceRegion is not null
            && inspection.DestinationRegion is not null
            && inspection.CoordinateValidation is not null;
        return inspection.IsSafeForInput && exactGeometryAvailable
            ? ScalingOutputValidation.ExactSafe
            : ScalingOutputValidation.Pending;
    }

    private void ApplyScalingSupervisionStatus(
        ScalingSessionSupervisionState previous,
        ScalingSessionSupervisionState current)
    {
        if (previous == current)
        {
            return;
        }

        switch (current.Mode)
        {
            case ScalingSessionMode.Suspended:
                InputSafetyText.Foreground = (Brush)FindResource("WarningBrush");
                InputSafetyText.Text =
                    "○ Alt-Tabbed; mouse map will be reverified on return";
                SetStatus(
                    StatusTone.Warning,
                    "ALT-TABBED · AUTO-RETURN ARMED",
                    "The normal Legends window and exact dedicated scaling engine "
                        + "remain owned and healthy. Switch back to EverQuest; "
                        + "borderless fullscreen will restore automatically.");
                break;

            case ScalingSessionMode.Resuming:
                InputSafetyText.Foreground = (Brush)FindResource("WarningBrush");
                InputSafetyText.Text =
                    "○ Restoring fullscreen and reverifying the mouse map";
                SetStatus(
                    StatusTone.Working,
                    "RESTORING BORDERLESS FULLSCREEN",
                    $"The exact executable/class profile is waiting for Magpie's "
                        + $"engine-owned return of the original Legends window. "
                        + $"Safety verification allows up to "
                        + $"{FullscreenResumeGrace.TotalSeconds:0} seconds.");
                break;

            case ScalingSessionMode.Active:
                InputSafetyText.Foreground = (Brush)FindResource("SuccessBrush");
                InputSafetyText.Text =
                    "✓ Mouse map reverified to within one source pixel";
                SetStatus(
                    StatusTone.Ready,
                    "FULLSCREEN RESTORED · INPUT VERIFIED",
                    "Borderless fullscreen returned automatically with the exact "
                        + "owned engine, source window, display, resolution, native "
                        + "1x UI setting, and physical mouse map reverified.");
                break;

            default:
                throw new InvalidOperationException(
                    "The scaling supervision state is not supported.");
        }
    }

    private void SetScalingCleanupStatus(
        ScalingSupervisionStopReason reason,
        MagpieScalingWindowInspection inspection,
        string? nativeUiScaleIssue)
    {
        (string heading, string message) = reason switch
        {
            ScalingSupervisionStopReason.NativeUiScaleFailure => (
                "NATIVE UI SCALE CHANGED — STOPPING",
                (nativeUiScaleIssue
                    ?? "The saved native UI scale is no longer verified.")
                    + " Fullscreen scaling is stopping so native and whole-frame "
                    + "scales cannot remain stacked."),
            ScalingSupervisionStopReason.SourceFailure => (
                "GAME WINDOW CHANGED — STOPPING",
                "The original Legends process, window identity, class, physical "
                    + "client size, or target display changed. Exact owned cleanup "
                    + "is running before another fullscreen attachment is allowed."),
            ScalingSupervisionStopReason.InfrastructureFailure => (
                "SCALING ENGINE ENDED — CLEANING UP",
                "The exact dedicated Magpie process is no longer healthy. The "
                    + "controller is confirming that no replacement process or "
                    + "fullscreen output remains before releasing ownership."),
            ScalingSupervisionStopReason.ResumeGraceExpired => (
                "FULLSCREEN RETURN TIMED OUT",
                "The original Legends window regained focus, but its verified "
                    + "borderless output did not return in time. Exact owned cleanup "
                    + "is running instead of leaving an uncertain input transform."),
            ScalingSupervisionStopReason.UnsafeOutput => (
                "INPUT MAP CHANGED — STOPPING",
                string.Join(" ", inspection.Issues)
                    + " The resumed output did not match the exact owned process, "
                    + "source, display, geometry, or physical mouse map."),
            _ => (
                "STOPPING FULLSCREEN SAFELY",
                "The controller is completing the requested exact owned-session cleanup."),
        };
        SetStatus(StatusTone.Error, heading, message);
    }

    private static string ScalingCleanupCompletionMessage(
        ScalingSupervisionStopReason reason)
    {
        return reason switch
        {
            ScalingSupervisionStopReason.NativeUiScaleFailure =>
                "Fullscreen was stopped automatically after the saved native UI "
                    + "scale changed or became unverifiable.",
            ScalingSupervisionStopReason.SourceFailure =>
                "Fullscreen was stopped after the original game-window identity or "
                    + "geometry changed.",
            ScalingSupervisionStopReason.ResumeGraceExpired =>
                "Fullscreen could not be restored safely after Alt+Tab, so the exact "
                    + "dedicated engine was closed.",
            ScalingSupervisionStopReason.UnsafeOutput =>
                "Fullscreen was stopped automatically before an unsafe resumed input "
                    + "mapping could be accepted.",
            ScalingSupervisionStopReason.InfrastructureFailure =>
                "The ended scaling engine has no replacement process or fullscreen "
                    + "output, so session ownership was released safely.",
            _ =>
                "Fullscreen and the dedicated scaling engine are confirmed stopped.",
        };
    }

    private static bool RectSizeMatches(
        PixelRect rectangle,
        PixelSize size,
        int tolerance)
    {
        return Math.Abs(rectangle.Width - size.Width) <= tolerance
            && Math.Abs(rectangle.Height - size.Height) <= tolerance;
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

    private void RefreshDisplayAndPlan()
    {
        if (!IsLoaded)
        {
            return;
        }

        try
        {
            DisplayChoice selectedDisplay = TargetDisplayComboBox.SelectedItem as DisplayChoice
                ?? throw new InvalidOperationException("Choose a target display.");
            _display = DisplaySnapshot.Capture(
                this,
                selectedDisplay.Monitor,
                selectedDisplay.Name);
            DesktopScaleText.Text = "UNCHANGED";

            PixelSize target = _display.Resolution;
            PixelSize? previousSource = _currentPlan?.SourceResolution;
            if (UsesStrictSpinUiMode
                && _spinUiDetection.Status == SpinUiDetectionStatus.Uncertain)
            {
                ConfigureUncertainSpinUiCards();
            }
            else if (UsesStrictSpinUiMode)
            {
                ConfigureSpinUiPresetCards(target);
            }
            else
            {
                ConfigureGenericPresetCards(target);
            }

            if (UsesStrictSpinUiMode
                && previousSource is { } oldSource
                && _currentPlan is { } selectedPlan
                && oldSource != selectedPlan.SourceResolution)
            {
                SpinUiLayoutReadyCheckBox.IsChecked = false;
            }

            if (_currentPlan is { } currentPlan)
            {
                PipelineSourceText.Text = FormatPixels(currentPlan.SourceResolution);
                PipelineTargetText.Text = FormatPixels(currentPlan.TargetResolution);
                PipelineScaleText.Text =
                    $"{currentPlan.ActualUiScale:0.##}× {FilterShortName(currentPlan.Filter)}";
            }
            else
            {
                PipelineSourceText.Text = "NOT AVAILABLE";
                PipelineTargetText.Text = FormatPixels(target);
                PipelineScaleText.Text = "BLOCKED";
            }

            if (UsesStrictSpinUiMode
                && _spinUiDetection.Status == SpinUiDetectionStatus.Uncertain)
            {
                SetStatus(
                    StatusTone.Error,
                    "SPINUI CHECK FAILED — SCALING BLOCKED",
                    _spinUiDetection.Issue
                        ?? "SpinUI compatibility could not be confirmed reliably.");
            }
            else if (UsesStrictSpinUiMode && !_spinUiDetection.IsDetected)
            {
                SetStatus(
                    StatusTone.Error,
                    "SPINUI NOT DETECTED",
                    "SpinUI mode was selected, but no saved SpinUI Reloaded character "
                        + "layout was detected. Select Current/default/custom UI, or "
                        + "install and select SpinUI before using strict mode.");
            }
            else if (UsesStrictSpinUiMode && _currentPlan is null)
            {
                SetStatus(
                    StatusTone.Error,
                    "NO VALIDATED SPINUI PLAN",
                    $"No known SpinUI source layout safely matches {FormatPixels(target)}. "
                        + "No generic source size will be substituted.");
            }
            else if (UsesStrictSpinUiMode
                && !_isBusy
                && !_isScaledSessionActive
                && !_scalingCleanupRequired
                && !_isCloseCleanupRunning
                && _activeState is null)
            {
                SetSpinUiReadinessStatus();
            }
            else if (!_spinUiDetection.IsDetected
                && (target.Width < 3_840 || target.Height < 2_160))
            {
                SetStatus(
                    StatusTone.Info,
                    "DISPLAY ADAPTED",
                    $"This monitor is {target.Width}×{target.Height}; presets were adapted "
                        + "to its physical resolution.");
            }
            else if (!_isBusy
                && !_isScaledSessionActive
                && !_scalingCleanupRequired
                && !_isCloseCleanupRunning
                && _activeState is null)
            {
                SetStatus(
                    StatusTone.Ready,
                    "READY",
                    "Choose a UI size, then start EverQuest; personal layouts are fitted automatically.");
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            _currentPlan = null;
            SetStatus(StatusTone.Error, "DISPLAY ERROR", exception.Message);
        }

        RefreshActionAvailability();
    }

    private void ConfigureGenericPresetCards(PixelSize target)
    {
        _isUpdatingPresetCards = true;
        try
        {
            PresetCardsGrid.Visibility = Visibility.Visible;
            PresetCardsGrid.Columns = 2;
            NoCompatiblePresetText.Visibility = Visibility.Collapsed;
            SpinUiNoticeBorder.Visibility = Visibility.Collapsed;
            ReadabilityEyebrowText.Text = "UI SIZE";
            ReadabilityHeadingText.Text = "Choose native pixels or a larger UI";
            ReadabilityDescriptionText.Text =
                "Native pixels keeps the original image unchanged at 100%. "
                + "Above 100%, UI, visible nameplates, artwork, hitboxes, and clicks "
                + "scale together through a text-first directional scaler. Each "
                + "player's own layout is fitted automatically before EverQuest opens.";

            ComfortPresetRadio.Visibility = Visibility.Visible;
            BalancedPresetRadio.Visibility = Visibility.Visible;
            GentlePresetRadio.Visibility = Visibility.Visible;
            NativeClarityPresetRadio.Visibility = Visibility.Visible;
            FineScaleBorder.Visibility = Visibility.Visible;
            SyncGenericPresetSelection(FineScaleSlider.Value);

            ComfortBadgeText.Text = "MAXIMUM READABILITY";
            ComfortTitleText.Text = "Comfort";
            ComfortDescriptionText.Text = "200% UI · largest text";
            BalancedBadgeText.Text = "LARGER";
            BalancedTitleText.Text = "Balanced";
            BalancedDescriptionText.Text = "150% UI · larger and readable";
            GentleBadgeText.Text = "RECOMMENDED";
            GentleTitleText.Text = "Gentle";
            GentleDescriptionText.Text = "125% UI · most world detail";
            NativeClarityBadgeText.Text = "NO UI RESIZE";
            NativeClarityTitleText.Text = "Native pixels";
            NativeClarityDescriptionText.Text =
                "100% UI · original detail, no sharpening";
            FineScaleTitleText.Text = "Fine UI sizing";
            FineScaleHintText.Text = _isScaledSessionActive
                || _activeLayoutSession is not null
                ? "Exit EverQuest before choosing another size."
                : "Choose the size before launch; 101%-133% keeps the most world detail.";

            ComfortResolutionText.Text = FormatPlan(
                _resolutionPlanner.CreateCustomPlan(target, 2.0, ScalingFilter.Nis));
            BalancedResolutionText.Text = FormatPlan(
                _resolutionPlanner.CreateCustomPlan(target, 1.5, ScalingFilter.Nis));
            GentleResolutionText.Text = FormatPlan(
                _resolutionPlanner.CreateCustomPlan(target, 1.25, ScalingFilter.Nis));
            NativeClarityResolutionText.Text =
                $"{FormatPixels(target)} native · original pixels";
            _spinUiPlans = [];
            _currentPlan = CreateSelectedPlan(target);
            bool safelyLimited =
                SelectedUiScale() - _currentPlan.ActualUiScale > 0.005;
            FineScaleUsageText.Text = _isScaledSessionActive
                ? "Active — this session is using its fitted personal layout."
                : safelyLimited
                    ? $"Safely limited to {FormatUiPercent(_currentPlan.ActualUiScale)} "
                        + "so EverQuest's login controls stay usable."
                    : _currentPlan.ActualUiScale > 1.005
                        ? "Automatic — personal layouts are fitted before launch."
                        : "Native pixels — no layout conversion is needed.";
            FineScaleValueText.Text =
                FormatUiPercent(_currentPlan.ActualUiScale);
            FineScaleSourceText.Text =
                $"{FormatPixels(_currentPlan.SourceResolution)} source";
            PipelineSourceLabel.Text = _isScaledSessionActive
                ? "Legends renders now"
                : "Legends will render";

        }
        finally
        {
            _isUpdatingPresetCards = false;
        }
    }

    private void ConfigureSpinUiPresetCards(PixelSize target)
    {
        SpinUiResolutionPlan[] basePlans =
            _spinUiResolutionPlanner
                .GetRecommendedPlans(target, ScalingFilter.Nis)
                .ToArray();
        int currentCardIndex = SelectedPresetCardIndex();
        int selectedIndex = -1;
        if (_activeState is
            {
                UiCompatibilityMode: FourKayUiCompatibilityMode.SpinUiStrict,
                Status:
                    FourKayJournalStatus.Preparing
                    or FourKayJournalStatus.Prepared,
            }
            && UsesStrictSpinUiMode)
        {
            PixelSize preparedSource = _activeState.ResolutionPlan.SourceResolution;
            selectedIndex = FindSpinUiPlanIndex(basePlans, preparedSource);
        }

        if (selectedIndex < 0
            && currentCardIndex >= 0
            && currentCardIndex < basePlans.Length)
        {
            selectedIndex = currentCardIndex;
        }

        if (selectedIndex < 0 && _currentPlan is { } currentPlan)
        {
            selectedIndex = FindSpinUiPlanIndex(
                basePlans,
                currentPlan.SourceResolution);
        }

        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        ScalingFilter requestedFilter = SelectedFilter();
        if (requestedFilter == ScalingFilter.NearestNeighbor
            && selectedIndex < basePlans.Length)
        {
            try
            {
                _ = _spinUiResolutionPlanner.CreateExactSourcePlan(
                    target,
                    basePlans[selectedIndex].SourceResolution,
                    requestedFilter);
                _spinUiFilterNotice = null;
            }
            catch (ArgumentException)
            {
                requestedFilter = ScalingFilter.Nis;
                _spinUiFilterNotice =
                    "Exact pixels is unavailable for this fractional SpinUI source; "
                    + "Readable UI was selected automatically.";
                _isUpdatingPresetCards = true;
                try
                {
                    QualityComboBox.SelectedIndex = 0;
                    AntiAliasingComboBox.SelectedIndex = 0;
                }
                finally
                {
                    _isUpdatingPresetCards = false;
                }
            }
        }
        else
        {
            _spinUiFilterNotice = null;
        }

        SpinUiResolutionPlan[] plans = basePlans
            .Select(
                basePlan =>
                {
                    try
                    {
                        return _spinUiResolutionPlanner.CreateExactSourcePlan(
                            target,
                            basePlan.SourceResolution,
                            requestedFilter);
                    }
                    catch (ArgumentException) when (
                        requestedFilter == ScalingFilter.NearestNeighbor)
                    {
                        return _spinUiResolutionPlanner.CreateExactSourcePlan(
                            target,
                            basePlan.SourceResolution,
                            ScalingFilter.Nis);
                    }
                })
            .ToArray();

        _isUpdatingPresetCards = true;
        try
        {
            _spinUiPlans = plans;
            ReadabilityEyebrowText.Text = "03 / VALIDATED SPINUI SOURCE";
            ReadabilityHeadingText.Text = "Choose the matching SpinUI layout size";
            ReadabilityDescriptionText.Text =
                "Only source resolutions with known SpinUI layout profiles are offered. "
                + "This is SpinUI's safe fractional path: native UI scaling remains "
                + "at 1x and generic calculated sizes stay disabled.";
            FineScaleBorder.Visibility = Visibility.Collapsed;
            PipelineSourceLabel.Text = "Legends renders";
            SpinUiNoticeBorder.Visibility = Visibility.Visible;
            SpinUiNoticeBorder.BorderBrush = (Brush)FindResource("CyanBrush");
            SpinUiNoticeTitleText.Foreground = (Brush)FindResource("CyanBrush");
            SpinUiNoticeTitleText.Text = "SpinUI Reloaded detected";
            SpinUiDetectionDetailText.Text =
                $"{_spinUiDetection.MatchingLayoutFiles.Count} root layout profile"
                + (_spinUiDetection.MatchingLayoutFiles.Count == 1 ? "" : "s")
                + " selects spinui_reloaded. This confirms the skin, not which "
                + "character layout is currently loaded.";
            SpinUiLayoutReadyCheckBox.Visibility = Visibility.Visible;
            NativeClarityPresetRadio.Visibility = Visibility.Collapsed;

            RadioButton[] radios =
            [
                ComfortPresetRadio,
                BalancedPresetRadio,
                GentlePresetRadio,
            ];
            TextBlock[] badges =
            [
                ComfortBadgeText,
                BalancedBadgeText,
                GentleBadgeText,
            ];
            TextBlock[] titles =
            [
                ComfortTitleText,
                BalancedTitleText,
                GentleTitleText,
            ];
            TextBlock[] descriptions =
            [
                ComfortDescriptionText,
                BalancedDescriptionText,
                GentleDescriptionText,
            ];
            TextBlock[] resolutions =
            [
                ComfortResolutionText,
                BalancedResolutionText,
                GentleResolutionText,
            ];

            for (int index = 0; index < radios.Length; index++)
            {
                bool visible = index < plans.Length;
                radios[index].Visibility =
                    visible ? Visibility.Visible : Visibility.Collapsed;
                radios[index].IsChecked = visible && index == selectedIndex;
                if (!visible)
                {
                    continue;
                }

                SpinUiResolutionPlan plan = plans[index];
                titles[index].Text = SpinUiPlanTitle(plan);
                badges[index].Text = index == 0
                    ? "RECOMMENDED"
                    : titles[index].Text == "Comfort"
                        ? "MAXIMUM READABILITY"
                        : "VALIDATED";
                descriptions[index].Text =
                    $"{plan.ActualUiScale:0.##}× larger UI · "
                    + $"{FilterShortName(plan.Filter)} · validated SpinUI layout"
                    + DescribeSpinUiFitMargins(plan);
                resolutions[index].Text = FormatPlan(plan.ResolutionPlan);
            }

            PresetCardsGrid.Columns = Math.Max(1, Math.Min(3, plans.Length));
            PresetCardsGrid.Visibility =
                plans.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoCompatiblePresetText.Visibility =
                plans.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            NoCompatiblePresetText.Text =
                $"No validated SpinUI source resolution safely matches "
                + $"{FormatPixels(target)}. SpinUI scaling is blocked.";

            _currentPlan = plans.Length > 0
                ? CreateSelectedPlan(target)
                : null;
            if (_currentPlan is { } selectedPlan)
            {
                string source = FormatPixels(selectedPlan.SourceResolution);
                SpinUiLayoutInstructionText.Text =
                    $"Before scaling, use the SpinUI installer to apply its {source} "
                    + "layout profile to the character you will play. SpinFOURKAYYY "
                    + "does not modify or auto-select character UI layouts. Restore "
                    + "will not undo that installer choice; afterward, reapply the "
                    + "pre-session/native SpinUI profile that matches the restored "
                    + "client dimensions with "
                    + "EverQuest closed.";
                SpinUiLayoutReadyCheckBox.Content =
                    $"I used the SpinUI installer to apply the {source} layout profile "
                    + "for the character I will play.";
            }
            else
            {
                SpinUiLayoutInstructionText.Text =
                    "No matching SpinUI installer layout is available for this display.";
                SpinUiLayoutReadyCheckBox.Content =
                    "I applied the matching SpinUI layout profile.";
                SpinUiLayoutReadyCheckBox.IsChecked = false;
            }
        }
        finally
        {
            _isUpdatingPresetCards = false;
        }
    }

    private void ConfigureUncertainSpinUiCards()
    {
        _isUpdatingPresetCards = true;
        try
        {
            _spinUiPlans = [];
            _currentPlan = null;
            PresetCardsGrid.Visibility = Visibility.Collapsed;
            FineScaleBorder.Visibility = Visibility.Collapsed;
            PipelineSourceLabel.Text = "Legends renders";
            NoCompatiblePresetText.Visibility = Visibility.Visible;
            NoCompatiblePresetText.Text =
                "SpinUI assets may be active, but layout detection was not reliable. "
                + "No SpinUI scaling will be started.";
            ReadabilityEyebrowText.Text = "03 / SPINUI CHECK REQUIRED";
            ReadabilityHeadingText.Text = "SpinUI scaling is blocked";
            ReadabilityDescriptionText.Text =
                "Close tools that are locking the UI profile, then reselect the Legends "
                + "folder so compatibility can be checked again.";
            SpinUiNoticeBorder.Visibility = Visibility.Visible;
            SpinUiNoticeBorder.BorderBrush = (Brush)FindResource("DangerBrush");
            SpinUiNoticeTitleText.Foreground = (Brush)FindResource("DangerBrush");
            SpinUiNoticeTitleText.Text = "SpinUI detection needs attention";
            SpinUiDetectionDetailText.Text = _spinUiDetection.Issue
                ?? "The active UI skin could not be determined reliably.";
            SpinUiLayoutInstructionText.Text =
                "SpinUI mode is fail-closed; no character UI INI is ever changed.";
            SpinUiLayoutReadyCheckBox.IsChecked = false;
            SpinUiLayoutReadyCheckBox.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _isUpdatingPresetCards = false;
        }
    }

    private int SelectedPresetCardIndex()
    {
        if (BalancedPresetRadio.IsChecked == true)
        {
            return 1;
        }

        if (GentlePresetRadio.IsChecked == true)
        {
            return 2;
        }

        return ComfortPresetRadio.IsChecked == true ? 0 : -1;
    }

    private double SelectedPresetCardScale()
    {
        if (NativeClarityPresetRadio.IsChecked == true)
        {
            return 1.0;
        }

        if (BalancedPresetRadio.IsChecked == true)
        {
            return 1.5;
        }

        if (GentlePresetRadio.IsChecked == true)
        {
            return 1.25;
        }

        return 2.0;
    }

    private void SyncGenericPresetSelection(double scale)
    {
        ComfortPresetRadio.IsChecked = Math.Abs(scale - 2.0) < 0.001;
        BalancedPresetRadio.IsChecked = Math.Abs(scale - 1.5) < 0.001;
        GentlePresetRadio.IsChecked = Math.Abs(scale - 1.25) < 0.001;
        NativeClarityPresetRadio.IsChecked = Math.Abs(scale - 1.0) < 0.001;
    }

    private static int FindSpinUiPlanIndex(
        SpinUiResolutionPlan[] plans,
        PixelSize source)
    {
        for (int index = 0; index < plans.Length; index++)
        {
            if (plans[index].SourceResolution == source)
            {
                return index;
            }
        }

        return -1;
    }

    private static string SpinUiPlanTitle(SpinUiResolutionPlan plan)
    {
        PixelSize source = plan.SourceResolution;
        if (source == new PixelSize(1920, 1080)
            && Math.Abs(plan.ActualUiScale - 2.0) < 0.05)
        {
            return "Comfort";
        }

        if (source == new PixelSize(2560, 1440)
            && Math.Abs(plan.ActualUiScale - 1.5) < 0.05)
        {
            return "Balanced";
        }

        if (source == new PixelSize(2560, 1080))
        {
            return "Ultrawide";
        }

        return $"{plan.ActualUiScale:0.##}× SpinUI";
    }

    private static string DescribeSpinUiFitMargins(SpinUiResolutionPlan plan)
    {
        if (plan.HasPillarboxing)
        {
            return $" · {plan.LeftMarginPixels}/{plan.RightMarginPixels}px side margins";
        }

        if (plan.HasLetterboxing)
        {
            return $" · {plan.TopMarginPixels}/{plan.BottomMarginPixels}px top/bottom margins";
        }

        return string.Empty;
    }

    private void SetSpinUiReadinessStatus()
    {
        if (!UsesStrictSpinUiMode)
        {
            return;
        }

        if (!_spinUiDetection.IsReliable)
        {
            SetStatus(
                StatusTone.Error,
                "SPINUI CHECK FAILED — SCALING BLOCKED",
                _spinUiDetection.Issue
                    ?? "SpinUI compatibility could not be confirmed reliably.");
            return;
        }

        if (!_spinUiDetection.IsDetected)
        {
            SetStatus(
                StatusTone.Error,
                "SPINUI NOT DETECTED",
                "Strict SpinUI mode requires a detected SpinUI Reloaded character "
                    + "layout. Current/default/custom UI mode remains available.");
            return;
        }

        if (_currentPlan is null)
        {
            return;
        }

        bool confirmed = SpinUiLayoutReadyCheckBox.IsChecked == true;
        SetStatus(
            confirmed ? StatusTone.Ready : StatusTone.Warning,
            confirmed ? "SPINUI PLAN READY" : "SPINUI LAYOUT CONFIRMATION NEEDED",
            confirmed
                ? $"Validated {FormatPixels(_currentPlan.SourceResolution)} SpinUI "
                    + "source selected. Scaling is unlocked."
                    + (_spinUiFilterNotice is null
                        ? string.Empty
                        : " " + _spinUiFilterNotice)
                : $"Apply the {FormatPixels(_currentPlan.SourceResolution)} profile "
                    + "with the SpinUI installer, then confirm the highlighted note."
                    + (_spinUiFilterNotice is null
                        ? string.Empty
                        : " " + _spinUiFilterNotice));
    }

    private void PopulateDisplays(PixelRect? preferredBounds = null)
    {
        IReadOnlyList<MonitorDescriptor> monitors = _windowPlacement.GetMonitors();
        DisplayChoice[] choices = monitors
            .Select(
                (monitor, index) =>
                    new DisplayChoice(
                        monitor,
                        monitor.IsPrimary
                            ? $"Display {index + 1} · {FormatPixels(monitor.Bounds.Size)} · primary"
                            : $"Display {index + 1} · {FormatPixels(monitor.Bounds.Size)}"))
            .ToArray();
        TargetDisplayComboBox.ItemsSource = choices;
        int preferredIndex = preferredBounds is { } bounds
            ? Array.FindIndex(
                choices,
                choice => choice.Monitor.Bounds == bounds)
            : -1;
        int primaryIndex = Array.FindIndex(choices, choice => choice.Monitor.IsPrimary);
        TargetDisplayComboBox.SelectedIndex = preferredIndex >= 0
            ? preferredIndex
            : primaryIndex >= 0
                ? primaryIndex
                : 0;
    }

    private void ApplySavedPreferences(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        bool wasUpdating = _isUpdatingPresetCards;
        _isUpdatingPresetCards = true;
        try
        {
            FineScaleSlider.Value = new FineUiScale(
                preferences.UiScaleHundredths).Factor;
            QualityComboBox.SelectedIndex = preferences.ScalingFilter switch
            {
                ScalingFilter.Fsr => 1,
                ScalingFilter.Lanczos => 2,
                ScalingFilter.NearestNeighbor => 3,
                _ => 0,
            };
            AntiAliasingComboBox.SelectedIndex = preferences.AntiAliasing switch
            {
                AntiAliasingMode.Smaa => 1,
                AntiAliasingMode.Fxaa => 2,
                _ => 0,
            };
            ClaritySlider.Value = preferences.ClarityPercent;
            OverlayCompatibilityCheckBox.IsChecked =
                preferences.MaintainTopmostOverlays;
            CloseConflictingMagpieCheckBox.IsChecked =
                preferences.CloseConflictingMagpieAutomatically;
            bool strict = preferences.UiCompatibilityMode
                == FourKayUiCompatibilityMode.SpinUiStrict;
            _lastSpinUiPresetIndex = preferences.SpinUiPresetIndex;
            CurrentUiModeRadioButton.IsChecked = !strict;
            SpinUiModeRadioButton.IsChecked = strict;
            SpinUiLayoutReadyCheckBox.IsChecked = false;
            if (strict)
            {
                ComfortPresetRadio.IsChecked = preferences.SpinUiPresetIndex == 0;
                BalancedPresetRadio.IsChecked = preferences.SpinUiPresetIndex == 1;
                GentlePresetRadio.IsChecked = preferences.SpinUiPresetIndex == 2;
                NativeClarityPresetRadio.IsChecked = false;
            }
            else
            {
                SyncGenericPresetSelection(FineScaleSlider.Value);
            }
        }
        finally
        {
            _isUpdatingPresetCards = wasUpdating;
        }

        RefreshSpinUiDetection(force: true);
        _ = NormalizeFilterForPreset();
    }

    private UserPreferences CapturePreferences()
    {
        PixelRect? targetBounds =
            (TargetDisplayComboBox.SelectedItem as DisplayChoice)?.Monitor.Bounds;
        return new UserPreferences
        {
            LegendsDirectory = _lastValidLegendsDirectory,
            SpinTextureExecutablePath = _spinTextureExecutablePath,
            TargetDisplayBounds = targetBounds,
            UiScaleHundredths =
                FineUiScale.FromSlider(FineScaleSlider.Value).Hundredths,
            ScalingFilter = SelectedFilter(),
            AntiAliasing = SelectedAntiAliasing(),
            ClarityPercent = checked((int)Math.Round(
                ClaritySlider.Value,
                MidpointRounding.AwayFromZero)),
            MaintainTopmostOverlays =
                OverlayCompatibilityCheckBox.IsChecked == true,
            CloseConflictingMagpieAutomatically =
                CloseConflictingMagpieCheckBox.IsChecked == true,
            UiCompatibilityMode = SelectedUiCompatibilityMode,
            SpinUiPresetIndex = _lastSpinUiPresetIndex,
        };
    }

    private void QueuePreferencesSave()
    {
        if (!_preferencesReady || _isCloseCleanupRunning)
        {
            return;
        }

        _preferencesSaveTimer.Stop();
        _preferencesSaveTimer.Start();
    }

    private async void PreferencesSaveTimer_Tick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _preferencesSaveTimer.Stop();
        await SavePreferencesSafelyAsync().ConfigureAwait(true);
    }

    private async Task SavePreferencesSafelyAsync()
    {
        if (!_preferencesReady)
        {
            return;
        }

        try
        {
            await _preferencesStore.SaveAsync(
                CapturePreferences(),
                CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            Debug.WriteLine(
                "SpinFOURKAYYY could not save user preferences: "
                    + exception.Message);
        }
    }

    private ResolutionPlan CreateSelectedPlan(PixelSize target)
    {
        if (UsesStrictSpinUiMode)
        {
            int selectedIndex = SelectedPresetCardIndex();
            if (selectedIndex < 0 || selectedIndex >= _spinUiPlans.Length)
            {
                throw new InvalidOperationException(
                    "Choose a validated SpinUI source resolution.");
            }

            SpinUiResolutionPlan selected = _spinUiPlans[selectedIndex];
            if (selected.TargetResolution != target)
            {
                throw new InvalidOperationException(
                    "The SpinUI source choices do not match the selected display.");
            }

            return selected.ResolutionPlan;
        }

        double desiredUiScale = SelectedUiScale();
        ScalingFilter filter = SelectedFilter();
        return _resolutionPlanner.CreateCustomPlan(target, desiredUiScale, filter);
    }

    private double SelectedUiScale()
    {
        return FineUiScale.FromSlider(FineScaleSlider.Value).Factor;
    }

    private ScalingFilter SelectedFilter()
    {
        string? tag = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        return tag switch
        {
            "Nis" => ScalingFilter.Nis,
            "Fsr" => ScalingFilter.Fsr,
            "Lanczos" => ScalingFilter.Lanczos,
            "Integer" => ScalingFilter.NearestNeighbor,
            _ => ScalingFilter.Nis,
        };
    }

    private AntiAliasingMode SelectedAntiAliasing()
    {
        string? tag = (AntiAliasingComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        return tag switch
        {
            "Fxaa" => AntiAliasingMode.Fxaa,
            "Smaa" => AntiAliasingMode.Smaa,
            _ => AntiAliasingMode.Off,
        };
    }

    /// <summary>
    /// Maps the capped edge-detail control directly to the selected scaler. At
    /// mild enlargement Readable UI ignores it and uses clean Lanczos
    /// reconstruction; from 1.20x, NIS applies it inside its one directional
    /// pass. Optional FSR uses one RCAS pass.
    /// </summary>
    private double SelectedClaritySharpness() =>
        Math.Clamp(
            Math.Round(ClaritySlider.Value) / 100.0,
            0.0,
            1.0);

    private string? NormalizeFilterForPreset()
    {
        if (!IsLoaded || UsesStrictSpinUiMode)
        {
            return null;
        }

        FineUiScale scale = FineUiScale.FromSlider(FineScaleSlider.Value);
        ScalingFilter requested = SelectedFilter();
        ScalingFilter normalized =
            GenericUiScalePolicy.NormalizeFilter(scale, requested);
        if (normalized == requested)
        {
            return null;
        }

        bool previousUpdating = _isUpdatingPresetCards;
        _isUpdatingPresetCards = true;
        try
        {
            QualityComboBox.SelectedIndex = normalized switch
            {
                ScalingFilter.Fsr => 1,
                ScalingFilter.Lanczos => 2,
                ScalingFilter.NearestNeighbor => 3,
                _ => 0,
            };
            if (normalized == ScalingFilter.Nis)
            {
                AntiAliasingComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _isUpdatingPresetCards = previousUpdating;
        }

        return scale.Hundredths == FineUiScale.MinimumHundredths
            ? "Native pixels preserves the game's original 1:1 image without "
                + "sharpening or whole-frame anti-aliasing."
            : "Exact-pixel scaling is limited to the true 2× Comfort preset. "
                + "Readable UI was selected for this fractional scale.";
    }

    private void RefreshActionAvailability()
    {
        if (!IsInitialized)
        {
            return;
        }

        bool clientValid = PathLocator.IsLegendsDirectory(LegendsPathTextBox.Text);
        bool planReady = _display is not null && _currentPlan is not null;
        bool uiModeReady =
            !UsesStrictSpinUiMode
            || (_spinUiDetection.IsReliable && _spinUiDetection.IsDetected);
        bool uiSessionConfirmed = IsUiSessionConfirmationSatisfied;
        bool controlsAvailable = !_isBusy && !_isCloseCleanupRunning;
        bool configurationAvailable =
            controlsAvailable
            && !_isScaledSessionActive
            && !_scalingCleanupRequired
            && _activeLayoutSession is null;
        bool engineReady;
        try
        {
            _ = PathLocator.FindMagpieDirectory();
            engineReady = true;
        }
        catch (DirectoryNotFoundException)
        {
            engineReady = false;
        }

        bool scaleReady =
            clientValid
            && engineReady
            && uiModeReady
            && uiSessionConfirmed
            && planReady;
        PrepareLaunchButton.IsEnabled =
            controlsAvailable
            && !_isScaledSessionActive
            && !_scalingCleanupRequired
            && _activeLayoutSession is null
            && scaleReady;
        PlayEnhancedButton.IsEnabled = PrepareLaunchButton.IsEnabled;
        AttachButton.IsEnabled =
            controlsAvailable
            && (_isScaledSessionActive
                || _scalingCleanupRequired);
        RestoreButton.IsEnabled =
            controlsAvailable
            && !_isScaledSessionActive
            && !_scalingCleanupRequired
            && _activeRecoveryCount > 0;
        CheckUpdatesButton.IsEnabled =
            controlsAvailable && !_isCheckingForUpdates;
        InstallUpdateButton.IsEnabled = CanInstallAvailableUpdate();

        PrepareLaunchButton.Content = _isScaledSessionActive
            ? "Live scaling is already active"
            : _scalingCleanupRequired
                ? "Finish scaling cleanup first"
                : "Start EverQuest for me";
        PrepareLaunchButton.ToolTip =
            "Prepares a normal, exact-size EverQuest source window and starts the "
                + "normal Legends launcher. Above 100%, SpinFOURKAYYY also fits each "
                + "personal UI layout before the game opens, saves that scale-specific "
                + "layout, and restores native geometry after the game exits.";
        PlayEnhancedButton.Content = _isScaledSessionActive
            ? "Enhanced play is already active"
            : _scalingCleanupRequired
                ? "Finish scaling cleanup first"
                : "Play Enhanced EQ";
        PlayEnhancedButton.ToolTip =
            "Uses SpinTexture's verified Play Enhanced flow to start the installed "
                + "texture pack without opening LaunchPad, then applies the same "
                + "safe layout preparation and fullscreen scaling. Use the normal "
                + "Start button whenever EverQuest needs an update.";
        AttachButton.Content = _isScaledSessionActive
            ? "Stop fullscreen scaling"
            : _scalingCleanupRequired
                ? "Retry scaling cleanup"
                : "Launch required";
        AttachButton.ToolTip =
            _isScaledSessionActive || _scalingCleanupRequired
                ? null
                : "Running games are never resized or attached. Choose every setting "
                    + "first, then use Start EverQuest for me.";
        RestoreButton.Content = _activeRecoveryCount > 1
            ? $"Restore {_activeRecoveryCount} profiles"
            : "Restore profile";
        LegendsPathTextBox.IsReadOnly = !configurationAvailable;
        BrowseButton.IsEnabled = configurationAvailable;
        TargetDisplayComboBox.IsEnabled = configurationAvailable;
        CurrentUiModeRadioButton.IsEnabled = configurationAvailable;
        SpinUiModeRadioButton.IsEnabled = configurationAvailable;
        ComfortPresetRadio.IsEnabled = configurationAvailable;
        BalancedPresetRadio.IsEnabled = configurationAvailable;
        GentlePresetRadio.IsEnabled = configurationAvailable;
        NativeClarityPresetRadio.IsEnabled = configurationAvailable;
        bool liveSliderAvailable =
            configurationAvailable
            && !UsesStrictSpinUiMode
            && !_isScaledSessionActive
            && !_scalingCleanupRequired;
        FineScaleSlider.IsEnabled = liveSliderAvailable;
        FineScaleMinusButton.IsEnabled = FineScaleSlider.IsEnabled;
        FineScalePlusButton.IsEnabled = FineScaleSlider.IsEnabled;
        FineScaleSlider.ToolTip = _isScaledSessionActive
            || _activeLayoutSession is not null
            ? "Exit EverQuest before choosing another percentage; its personal "
                + "layout profile is prepared when the next session launches."
            : "Selects the real render size in exact one-percent steps. Above 100%, "
                + "the personal layout is fitted automatically before launch.";
        QualityComboBox.IsEnabled = configurationAvailable;
        ScalingFilter selectedQuality = SelectedFilter();
        bool isUpscaling = _currentPlan is { ActualUiScale: > 1.005 };
        AntiAliasingComboBox.IsEnabled =
            configurationAvailable
            && isUpscaling
            && selectedQuality != ScalingFilter.Nis;
        ClaritySlider.IsEnabled =
            configurationAvailable
            && isUpscaling
            && (selectedQuality == ScalingFilter.Fsr
                || (selectedQuality == ScalingFilter.Nis
                    && _currentPlan!.ActualUiScale
                            + MagpiePortableConfigService
                                .ReadableScaleComparisonTolerance
                        >= MagpiePortableConfigService.ReadableNisMinimumScale));
        OverlayCompatibilityCheckBox.IsEnabled = configurationAvailable;
        SpinUiLayoutReadyCheckBox.IsEnabled =
            controlsAvailable
            && UsesStrictSpinUiMode
            && _spinUiDetection.IsReliable
            && _spinUiDetection.IsDetected
            && _currentPlan is not null
            && !_isScaledSessionActive
            && !_scalingCleanupRequired;
        RefreshUiModePresentation();
        RefreshRecoveryGatePresentation();

        if (!clientValid && IsLoaded && configurationAvailable)
        {
            AdvancedExpander.IsExpanded = true;
            SetStatus(
                StatusTone.Warning,
                "CLIENT NEEDED",
                "Choose the folder containing eqgame.exe and eqclient.ini.");
        }
        else if (!engineReady && IsLoaded && configurationAvailable)
        {
            SetStatus(
                StatusTone.Error,
                "ENGINE MISSING",
                "The Engine\\Magpie folder is incomplete. Re-extract the full release.");
        }
        else if (_activeState is not null
            && !_isBusy
            && !_isScaledSessionActive
            && !_scalingCleanupRequired
            && !_isCloseCleanupRunning
            && uiModeReady
            && uiSessionConfirmed
            && planReady)
        {
            SetRecoveryStatus();
        }
    }

    private void RefreshUiModePresentation()
    {
        SpinUiDetectionBadgeText.Text = _spinUiDetection.Status switch
        {
            SpinUiDetectionStatus.Detected =>
                $"SPINUI FOUND IN {_spinUiDetection.MatchingLayoutFiles.Count} SAVED "
                    + (_spinUiDetection.MatchingLayoutFiles.Count == 1
                        ? "LAYOUT"
                        : "LAYOUTS"),
            SpinUiDetectionStatus.Uncertain => "SPINUI SCAN INCOMPLETE",
            _ when _spinUiDetection.ThemeDirectoryPresent =>
                "SPINUI FILES INSTALLED · NOT SELECTED",
            _ => "NO SPINUI SAVED LAYOUT DETECTED",
        };

        UiModeHelpText.Text = UsesStrictSpinUiMode
            ? _spinUiDetection.Status switch
            {
                SpinUiDetectionStatus.Detected =>
                    "Strict SpinUI mode uses only validated source sizes and requires "
                        + "you to confirm the matching layout. SpinFOURKAYYY never "
                        + "installs or selects that profile.",
                SpinUiDetectionStatus.Uncertain =>
                    "Strict SpinUI mode is blocked until its saved layouts can be "
                        + "checked reliably. Current/default/custom UI mode remains "
                        + "available.",
                _ =>
                    "No SpinUI character layout was detected. Select Current/default/"
                        + "custom UI unless the character you will play already uses "
                        + "SpinUI Reloaded.",
            }
            : _spinUiDetection.IsDetected
                ? "Current/default/custom UI is selected. SpinUI was found in another "
                    + "saved layout, but it is not assumed active. This app will not "
                    + "select or rewrite a UI profile, layout file, or keybind entry."
                : "Current/default/custom UI is selected. Fine scaling enlarges the "
                    + "complete game frame. Automatic launch fits each personal UI "
                    + "layout to the chosen size without installing or selecting a "
                    + "skin, or touching macros, hotbuttons, or keybinds.";

        UiModeConflictBorder.Visibility = Visibility.Collapsed;
        UiModeConflictText.Text = string.Empty;
    }

    private void RefreshRecoveryGatePresentation()
    {
        LauncherPanel.Visibility = Visibility.Visible;
        bool showRecoveryNotice =
            _activeRecoveryCount > 0
            && !_isBusy
            && !_isScaledSessionActive
            && !_scalingCleanupRequired;
        RecoveryGateBorder.Visibility = showRecoveryNotice
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!showRecoveryNotice)
        {
            return;
        }

        int count = Math.Max(1, _activeRecoveryCount);
        RecoveryCountText.Text =
            $"{count} SAVED PLAYER-PROFILE "
            + (count == 1 ? "SNAPSHOT FOUND" : "SNAPSHOTS FOUND");
        RecoveryDetailText.Text =
            "An older SpinFOURKAYYY version saved this pre-session backup before "
                + "editing eqclient.ini. That legacy backup remains separate from "
                + "automatic scale-specific UI layouts and is never restored on its "
                + "own. Use Restore now only if you want to roll back to the old "
                + "backup (the game must be closed; current files are preserved in "
                + "a recovery copy first).";
    }

    /// <summary>
    /// Asks whether a separately installed Magpie may be closed, and closes it
    /// when the user agrees.
    /// </summary>
    /// <remarks>
    /// This is the one case where SpinFOURKAYYY ends a process the user started
    /// themselves, so it is never done without an explicit answer. Magpie is
    /// asked to quit through its own message rather than terminated, so it exits
    /// the same way it would from its tray icon.
    /// </remarks>
    private async Task<bool> TryCloseForeignMagpieAsync(
        ExternalMagpieInstanceConflictException conflict)
    {
        string running = string.Join(
            Environment.NewLine,
            conflict.ConflictingInstances.Select(
                instance => "    "
                    + (instance.ExecutablePath
                        ?? $"Magpie process {instance.ProcessId}")));
        if (CloseConflictingMagpieCheckBox.IsChecked == true)
        {
            return await CloseForeignMagpieAsync().ConfigureAwait(true);
        }

        MessageBoxResult choice = MessageBox.Show(
            this,
            "A separately installed Magpie is running. SpinFOURKAYYY needs its "
                + "own copy so the exact scaling profile for your chosen size can "
                + "load, and Magpie allows only one copy at a time."
                + Environment.NewLine
                + Environment.NewLine
                + running
                + Environment.NewLine
                + Environment.NewLine
                + "Close it and continue? Magpie is asked to quit normally, the "
                + "same as choosing Exit from its tray icon. Anything it is "
                + "currently scaling will stop. Nothing else is changed.",
            "Close the other Magpie?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
        {
            return false;
        }

        return await CloseForeignMagpieAsync().ConfigureAwait(true);
    }

    private async Task<bool> CloseForeignMagpieAsync()
    {
        SetStatus(
            StatusTone.Working,
            "CLOSING THE OTHER MAGPIE",
            "Asking Magpie to quit normally, then continuing\u2026");
        IReadOnlyList<int> closed = await _magpieProcess
            .ShutdownAllConsentedAsync(
                PathLocator.FindMagpieDirectory(),
                TimeSpan.FromSeconds(8),
                CancellationToken.None)
            .ConfigureAwait(true);
        if (closed.Count == 0)
        {
            ShowError(
                "Magpie did not close",
                "Magpie did not respond to a normal quit request, so nothing was "
                    + "changed. Close it from its tray icon, then try again.");
            return false;
        }

        return true;
    }

    private void CloseConflictingMagpie_Changed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        QueuePreferencesSave();
    }

    private async Task RunOperationAsync(
        string heading,
        string message,
        Func<CancellationToken, Task> operation)
    {
        if (_isBusy || _isCloseCleanupRunning)
        {
            return;
        }

        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task trackedOperation = completion.Task;
        _inFlightOperation = trackedOperation;
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _isBusy = true;
        SetStatus(StatusTone.Working, heading, message);
        OperationProgressBar.IsIndeterminate = true;
        RefreshActionAvailability();

        try
        {
            Task? healthCheck = _scalingHealthCheckTask;
            if (healthCheck is not null)
            {
                await healthCheck.ConfigureAwait(true);
            }

            // A foreign Magpie blocks the dedicated profile from loading. Offer
            // to close it rather than making the user hunt for a tray icon, then
            // retry exactly once so a declined or failed shutdown still reports
            // through the normal error path.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await operation(_operationCancellation.Token).ConfigureAwait(true);
                    break;
                }
                catch (ExternalMagpieInstanceConflictException conflict)
                    when (attempt == 0)
                {
                    if (!await TryCloseForeignMagpieAsync(conflict).ConfigureAwait(true))
                    {
                        throw;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(
                StatusTone.Warning,
                "CANCELLED",
                "The operation was cancelled. Any prepared personal layout was "
                    + "rolled back or remains journaled for safe recovery.");
        }
        catch (MagpieScalingCleanupException exception)
        {
            _pendingScalingCleanupLease?.Dispose();
            _pendingScalingCleanupLease = exception.CleanupLease;
            _scalingCleanupRequired = true;
            SetStatus(
                StatusTone.Error,
                "SCALING CLEANUP REQUIRED",
                exception.Message
                    + " The controller is retaining cleanup ownership and will retry.");
            ShowError("SpinFOURKAYYY cleanup needs attention", exception.Message);
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            SetStatus(StatusTone.Error, "NEEDS ATTENTION", exception.Message);
            ShowError("SpinFOURKAYYY could not finish", exception.Message);
        }
        finally
        {
            _isBusy = false;
            OperationProgressBar.IsIndeterminate = false;
            OperationProgressBar.Value = _isScaledSessionActive ? 100 : 0;
            RefreshActionAvailability();
            if (ReferenceEquals(_inFlightOperation, trackedOperation))
            {
                _inFlightOperation = null;
            }

            completion.TrySetResult();
        }
    }

    /// <summary>
    /// Starts either the normal LaunchPad or SpinTexture's verified enhanced
    /// wrapper and, when required, prepares a reversible scale-specific copy of
    /// every discovered personal UI layout before the game window appears.
    /// </summary>
    private async Task LaunchThenAutoScaleCoreAsync(
        FourKayGameStartMode startMode,
        string? launchTargetPath,
        CancellationToken cancellationToken)
    {
        string eqDirectory = RequireValidLegendsDirectory();
        string launcherPath;
        IReadOnlyList<string> launcherArguments;
        if (startMode == FourKayGameStartMode.OfficialLauncher)
        {
            launcherPath = Path.Combine(eqDirectory, "LaunchPad.exe");
            if (!File.Exists(launcherPath))
            {
                throw new FileNotFoundException(
                    "LaunchPad.exe was not found. Nothing was started. Restore the "
                        + "launcher or choose the correct EverQuest Legends folder.",
                    launcherPath);
            }

            launcherArguments = [];
        }
        else if (startMode == FourKayGameStartMode.SpinTextureEnhanced)
        {
            if (!PathLocator.IsSpinTextureExecutable(launchTargetPath))
            {
                _spinTextureExecutablePath = null;
                QueuePreferencesSave();
                throw new FileNotFoundException(
                    "SpinTexture.exe could not be verified. Choose Play Enhanced EQ "
                        + "again and select the SpinTexture application from its "
                        + "fully extracted folder.",
                    launchTargetPath);
            }

            launcherPath = Path.GetFullPath(launchTargetPath!);
            launcherArguments = ["--play-enhanced", eqDirectory];
        }
        else
        {
            throw new ArgumentOutOfRangeException(
                nameof(startMode),
                "The requested game start mode is not supported.");
        }

        string magpieDirectory = PathLocator.FindMagpieDirectory();
        RefreshSpinUiDetection(force: true);
        RefreshDisplayAndPlan();
        ResolutionPlan plan = _currentPlan
            ?? throw new InvalidOperationException("Choose a valid display preset.");
        DisplaySnapshot display = _display
            ?? throw new InvalidOperationException("Choose a target display.");
        EnsureSpinUiSessionIsReady("Launch");

        string eqGamePath = Path.Combine(eqDirectory, "eqgame.exe");
        IReadOnlyList<ProcessDescriptor> existing =
            _processDiscovery.FindByExecutablePath(eqGamePath);
        if (existing.Count > 0)
        {
            throw new InvalidOperationException(
                "EverQuest Legends is already running. It was left completely "
                    + "untouched. Exit every Legends client, choose the percentage, "
                    + "text detail and compatibility options you want, then use Start EverQuest "
                    + "for me again.");
        }

        if (IsLegendsGameRunning(eqDirectory))
        {
            throw new InvalidOperationException(
                "An eqgame process is running but its exact Legends path could not "
                    + "be verified. Nothing was prepared and no game window was "
                    + "touched. Exit every EverQuest client and try again.");
        }

        string? recoveredLayoutMessage =
            await RecoverPendingLayoutSessionBeforeLaunchAsync(
                eqDirectory,
                cancellationToken).ConfigureAwait(true);
        if (recoveredLayoutMessage is not null)
        {
            SetStatus(
                StatusTone.Working,
                "EARLIER UI LAYOUT RECOVERED",
                recoveredLayoutMessage + " Preparing this launch nowâ€¦");
        }

        await SavePreferencesSafelyAsync().ConfigureAwait(true);

        UiLayoutSessionState? preparedThisAttempt = null;
        try
        {
            if (RequiresScaleAwareLayout(plan))
            {
                UiLayoutPrepareResult layout =
                    await _layoutProfileService.PrepareAsync(
                        new UiLayoutPrepareRequest
                        {
                            EqDirectory = eqDirectory,
                            StateRoot = PathLocator.LayoutProfileRoot,
                            NativeResolution = plan.TargetResolution,
                            ScaledResolution = plan.SourceResolution,
                        },
                        cancellationToken).ConfigureAwait(true);
                preparedThisAttempt = layout.State;
                _activeLayoutSession = layout.State;
                SetStatus(
                    StatusTone.Working,
                    "PERSONAL UI LAYOUT READY",
                    $"Prepared {layout.LayoutCount} personal UI "
                        + (layout.LayoutCount == 1 ? "layout" : "layouts")
                        + $" for {FormatPixels(plan.SourceResolution)}. Macros, "
                        + "hotbuttons, socials, spell sets, keybinds, and userdata "
                        + "were not opened or changed.");
            }

            FourKayPreparedState prepared =
                await _preparationService.PrepareAsync(
                    new FourKayPreparationRequest
                    {
                        EqDirectory = eqDirectory,
                        StateDirectory = PathLocator.StateRoot,
                        ResolutionPlan = plan,
                        TargetMonitor = display.MonitorHandle,
                        ChatFontSizeIncrease = 0,
                        UiCompatibilityMode = SelectedUiCompatibilityMode,
                        KeepPreparedConfiguration = true,
                    },
                    cancellationToken).ConfigureAwait(true);

            SetStatus(
                StatusTone.Working,
                "WAITING FOR LEGENDS",
                startMode == FourKayGameStartMode.SpinTextureEnhanced
                    ? "SpinTexture is verifying the installed enhanced pack and "
                        + "starting EverQuest without LaunchPad. Sign in if prompted; "
                        + $"fullscreen starts after the verified {FormatPixels(plan.SourceResolution)} "
                        + "game window is stable and its physical mouse map passes."
                    : "The normal launcher is opening with a verified, normal-window "
                        + $"{FormatPixels(plan.SourceResolution)} source. Finish patching "
                        + "and sign in; fullscreen starts only after that managed window "
                        + "is stable and its physical mouse map can be verified.");

            FourKayLaunchResult result = await _launchService.LaunchAndScaleAsync(
                new FourKayLaunchRequest
                {
                    PreparedState = prepared,
                    LauncherPath = launcherPath,
                    LauncherArguments = launcherArguments,
                    StartMode = startMode,
                    MagpieDirectory = magpieDirectory,
                    GameStartTimeout = startMode
                        == FourKayGameStartMode.SpinTextureEnhanced
                            ? TimeSpan.FromMinutes(3)
                            : TimeSpan.FromMinutes(15),
                    WindowTimeout = TimeSpan.FromMinutes(3),
                    ScalingTimeout = TimeSpan.FromSeconds(20),
                    RcasSharpness = SelectedClaritySharpness(),
                    AntiAliasing = SelectedAntiAliasing(),
                    MaintainTopmostOverlays =
                        OverlayCompatibilityCheckBox.IsChecked == true,
                },
                cancellationToken).ConfigureAwait(true);

            ReplaceActiveLaunch(result);
            PixelSize source = result.Placement.RequestedClientSize;
            PixelSize target = result.ScalingInspection.MonitorBounds?.Size
                ?? plan.TargetResolution;
            double activeScale = Math.Min(
                (double)target.Width / source.Width,
                (double)target.Height / source.Height);
            PipelineSourceLabel.Text = "Managed launch renders";
            PipelineSourceText.Text = FormatPixels(source);
            PipelineTargetText.Text = FormatPixels(target);
            PipelineScaleText.Text = $"{activeScale:0.##}x PREPARED";
            int overlayCount =
                result.OverlayCompatibility?.CapturedWindowCount ?? 0;
            string overlayMessage = result.OverlayCompatibility is null
                ? string.Empty
                : overlayCount > 0
                    ? $" {overlayCount} existing companion overlay "
                        + (overlayCount == 1
                            ? "window remains native-sized and is kept"
                            : "windows remain native-sized and are kept")
                        + " above the game while you play."
                    : " Companion-overlay monitoring is active for always-on-top "
                        + "HUDs opened during play.";
            SetScaledSessionState(
                active: true,
                $"The managed {FormatPixels(source)} EverQuest window now fills "
                    + $"{FormatPixels(target)} at {activeScale:0.##}x. The source "
                    + "window is normal (not maximized), and the physical mouse map "
                    + "passed validation."
                    + overlayMessage,
                result.Warnings);
        }
        catch
        {
            if (preparedThisAttempt is not null
                && !IsLegendsGameRunning(eqDirectory))
            {
                await RollbackPreparedLayoutAsync(
                    preparedThisAttempt,
                    CancellationToken.None).ConfigureAwait(true);
            }

            throw;
        }
    }

    private void EnsureSpinUiSessionIsReady(string actionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        string stoppedMessage = $"{actionName} was not started.";
        if (UsesStrictSpinUiMode
            && _spinUiDetection.Status == SpinUiDetectionStatus.Uncertain)
        {
            throw new InvalidOperationException(
                _spinUiDetection.Issue
                    ?? "SpinUI compatibility could not be checked reliably. "
                        + stoppedMessage);
        }

        if (!UsesStrictSpinUiMode)
        {
            return;
        }

        if (!_spinUiDetection.IsDetected)
        {
            throw new InvalidOperationException(
                "SpinUI strict mode was selected, but no SpinUI Reloaded character "
                    + "layout was detected. Select Current/default/custom UI instead, "
                    + "or install and select SpinUI first. "
                    + stoppedMessage);
        }

        ResolutionPlan plan = _currentPlan
            ?? throw new InvalidOperationException(
                "No validated SpinUI source resolution matches the selected display. "
                    + stoppedMessage);
        bool offeredByCore = _spinUiPlans.Any(
            option => option.SourceResolution == plan.SourceResolution
                && option.TargetResolution == plan.TargetResolution
                && option.Filter == plan.Filter);
        if (!offeredByCore)
        {
            throw new InvalidOperationException(
                "The selected source is not in the validated SpinUI plan list. "
                    + stoppedMessage);
        }

        if (SpinUiLayoutReadyCheckBox.IsChecked != true)
        {
            throw new InvalidOperationException(
                $"Use the SpinUI installer to apply the "
                    + $"{FormatPixels(plan.SourceResolution)} character layout, then "
                    + "confirm the highlighted SpinUI note. "
                    + stoppedMessage);
        }
    }

    private static Task AttachCoreAsync(
        CancellationToken cancellationToken,
        ProcessDescriptor? expectedAutomaticProcess = null)
    {
        _ = expectedAutomaticProcess;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(
            new InvalidOperationException(
                "Live attachment is disabled. SpinFOURKAYYY never resizes, moves, "
                    + "or scales an already-running EverQuest window. Exit the game, "
                    + "choose every setting, then use Start EverQuest for me."));
    }

    private static Task<FourKayLiveScaleResult> RejectLiveScaleChangeAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<FourKayLiveScaleResult>(
            new InvalidOperationException(
                "Live scale changes are disabled. Exit EverQuest, choose the new "
                    + "percentage and quality options, then start a new "
                    + "managed session through SpinFOURKAYYY."));
    }

    private async Task AdjustLiveScaleCoreAsync(
        FineUiScale requestedScale,
        FourKayLaunchResult expectedLaunch,
        CancellationToken cancellationToken)
    {
        if (!_isScaledSessionActive
            || _scalingCleanupRequired
            || !ReferenceEquals(_activeLaunch, expectedLaunch))
        {
            return;
        }

        FourKayLaunchResult launch = expectedLaunch;
        if (launch.UiCompatibilityMode
                != FourKayUiCompatibilityMode.GenericOrCustom
            || launch.EffectiveFilter == ScalingFilter.NearestNeighbor)
        {
            throw new InvalidOperationException(
                "Live 1% adjustment requires a generic/custom UI session using "
                    + "Readable UI, Smooth FSR, or Lanczos.");
        }

        nint foreground = _foregroundWindow.GetForegroundWindowHandle();
        if (foreground == launch.SourceWindow.Handle)
        {
            throw new InvalidOperationException(
                "Alt-Tab to SpinFOURKAYYY before adjusting the live scale.");
        }

        ResolutionPlan requestedPlan = _resolutionPlanner.CreateCustomPlan(
            launch.Placement.Monitor.Bounds.Size,
            requestedScale.Factor,
            launch.EffectiveFilter);
        if (requestedPlan.SourceResolution
            == launch.Placement.RequestedClientSize)
        {
            RefreshDisplayAndPlan();
            SetStatus(
                StatusTone.Ready,
                "LIVE SCALE ALREADY ACTIVE",
                $"{FormatUiPercent(requestedScale.Factor)} resolves to the current "
                    + $"{FormatPixels(requestedPlan.SourceResolution)} render surface, "
                    + "so the game window and mouse map were left untouched.");
            return;
        }

        FourKayLiveScaleResult result;
        try
        {
            result = await RejectLiveScaleChangeAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        catch (FourKayLiveScaleAdjustmentException exception)
        {
            _scalingCleanupRequired = true;
            ScalingCleanupResult cleanup = await StopAndConfirmOwnedScalingAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken.None).ConfigureAwait(true);
            if (cleanup.Success)
            {
                CompleteScalingOwnership(
                    "Live scaling could not restore its previous verified geometry, "
                        + "so the exact owned fullscreen engine was stopped safely.");
            }

            throw new InvalidOperationException(
                exception.Message
                    + (cleanup.Success
                        ? " The exact owned session is stopped; the game remains in "
                            + "its ordinary window."
                        : " Exact cleanup also needs attention: " + cleanup.Message),
                exception);
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            if (ReferenceEquals(_activeLaunch, launch))
            {
                SyncFineScaleToActiveLaunch(launch);
                RefreshDisplayAndPlan();
            }

            throw;
        }

        if (!ReferenceEquals(_activeLaunch, launch))
        {
            throw new InvalidOperationException(
                "The active scaling session changed while live adjustment was running.");
        }

        _activeLaunch = result.ActiveLaunch;
        _scalingSupervisionState = result.OutputIsActive
            ? ScalingSessionSupervisionState.Active
            : ScalingSessionSupervisionState.Suspended;
        if (result.Disposition == FourKayLiveScaleDisposition.RecoveredPrevious)
        {
            SyncFineScaleToActiveLaunch(result.ActiveLaunch);
        }

        RefreshDisplayAndPlan();
        InputSafetyText.Foreground = (Brush)FindResource(
            result.OutputIsActive ? "SuccessBrush" : "WarningBrush");
        InputSafetyText.Text = result.OutputIsActive
            ? "✓ Live size and physical mouse map verified"
            : "○ Live size verified; mouse map will be reverified on return";
        if (result.Disposition == FourKayLiveScaleDisposition.Committed)
        {
            SetStatus(
                StatusTone.Ready,
                result.OutputIsActive
                    ? "LIVE SCALE ACTIVE · INPUT VERIFIED"
                    : "LIVE SCALE READY · RETURN TO EVERQUEST",
                $"{FormatUiPercent(requestedScale.Factor)} uses a "
                    + $"{FormatPixels(requestedPlan.SourceResolution)} render surface. "
                    + (result.OutputIsActive
                        ? "Borderless fullscreen and every click are already verified. "
                        : "Switch back to EverQuest to restore borderless fullscreen "
                            + "and reverify every click. ")
                    + "Larger UI means slightly less 3D detail.");
        }
        else
        {
            SetStatus(
                StatusTone.Warning,
                result.OutputIsActive
                    ? "PREVIOUS LIVE SCALE RESTORED · INPUT VERIFIED"
                    : "PREVIOUS LIVE SCALE RESTORED",
                result.Message
                    + (result.OutputIsActive
                        ? " The restored fullscreen mouse map is verified."
                        : " Return to EverQuest to reverify fullscreen."));
        }
    }

    private static double CalculateActiveUiScale(FourKayLaunchResult launch)
    {
        PixelSize source = launch.Placement.RequestedClientSize;
        PixelRect destination = launch.ExpectedDestinationRegion;
        return Math.Min(
            (double)destination.Width / source.Width,
            (double)destination.Height / source.Height);
    }

    private void SyncFineScaleToActiveLaunch(FourKayLaunchResult launch)
    {
        double scale = CalculateActiveUiScale(launch);
        _isUpdatingPresetCards = true;
        try
        {
            FineScaleSlider.Value = Math.Clamp(
                Math.Round(scale * 100.0, MidpointRounding.AwayFromZero) / 100.0,
                FineScaleSlider.Minimum,
                FineScaleSlider.Maximum);
        }
        finally
        {
            _isUpdatingPresetCards = false;
        }
    }

    private async Task StopScalingCoreAsync(CancellationToken cancellationToken)
    {
        _scalingCleanupRequired = true;
        ScalingCleanupResult cleanup = await StopAndConfirmOwnedScalingAsync(
            TimeSpan.FromSeconds(12),
            cancellationToken).ConfigureAwait(true);
        if (!cleanup.Success)
        {
            throw new InvalidOperationException(cleanup.Message);
        }

        CompleteScalingOwnership(
            "The game is still running in its normal window; fullscreen and the "
                + "dedicated tray engine are confirmed stopped.");
    }

    private async Task<ScalingCleanupResult> StopAndConfirmOwnedScalingAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Cleanup timeout must be positive.");
        }

        if (_pendingScalingCleanupLease is { } pendingLease)
        {
            MagpieScalingCleanupResolution resolution =
                await _launchService.ResolveCleanupLeaseAsync(
                    pendingLease,
                    timeout,
                    cancellationToken).ConfigureAwait(true);
            if (!resolution.Resolved)
            {
                return new ScalingCleanupResult(
                    Success: false,
                    resolution.Message);
            }

            pendingLease.Dispose();
            _pendingScalingCleanupLease = null;
            return new ScalingCleanupResult(
                Success: true,
                resolution.Message);
        }

        if (_activeLaunch is not { } activeLaunch)
        {
            return new ScalingCleanupResult(
                Success: true,
                "This controller has no exact Magpie process token to clean up. "
                    + "Any running Magpie process or fullscreen output was left untouched.");
        }

        MagpieScalingCleanupResolution ownedResolution =
            await _launchService.StopExactOwnedSessionAsync(
                activeLaunch.SourceWindow.Handle,
                activeLaunch.MagpieDirectory,
                activeLaunch.MagpieProcess.Process,
                timeout,
                activeLaunch.AttachWindowRecovery,
                cancellationToken).ConfigureAwait(true);
        if (ownedResolution.Resolved)
        {
            MagpieScalingWindowInspection remainingOutput =
                _scalingInspector.Inspect(activeLaunch.SourceWindow.Handle);
            if (remainingOutput.IsActive)
            {
                return new ScalingCleanupResult(
                    Success: false,
                    "The exact engine owned by this session is stopped, but a "
                        + "replacement or foreign Magpie fullscreen output is still "
                        + "active. It was left untouched. Close that other Magpie "
                        + "instance manually; SpinFOURKAYYY will retain the original "
                        + "session token and verify again.");
            }
        }

        return new ScalingCleanupResult(
            ownedResolution.Resolved,
            ownedResolution.Message);
    }

    private void CompleteScalingOwnership(string message)
    {
        string[] overlayWarnings =
            RestoreOverlayCompatibility(_activeLaunch);
        if (overlayWarnings.Length > 0)
        {
            message += " Overlay note: " + string.Join(" ", overlayWarnings);
        }

        DisposeLaunchHandles(_activeLaunch);
        _activeLaunch = null;
        _pendingScalingCleanupLease?.Dispose();
        _pendingScalingCleanupLease = null;
        _scalingCleanupRequired = false;
        _liveScaleDebounceTimer.Stop();
        _pendingLiveScale = null;
        _clarityDebounceTimer.Stop();
        _clarityReapplyPending = false;
        _isScaledSessionActive = false;
        _overlayCompatibilityTickCount = 0;
        _displayedOverlayWarningCount = 0;
        ResetSessionUiConfirmations();
        RefreshDisplayAndPlan();
        SetScaledSessionState(active: false, message: message);
        if (_activeState is not null)
        {
            SetRecoveryStatus();
        }
    }

    private async Task RestoreAllCoreAsync(CancellationToken cancellationToken)
    {
        int restoredCount = await RestoreAllActiveJournalsAsync(
            cancellationToken).ConfigureAwait(true);
        SetStatus(
            StatusTone.Ready,
            "PLAYER PROFILE RESTORED",
            $"{restoredCount} saved player-profile "
                + (restoredCount == 1 ? "snapshot was" : "snapshots were")
                + " restored newest-first and checksum-verified. Changes made during "
                + "the temporary session were preserved in recovery backups before "
                + "the pre-session layout, hotbars, macros, keybinds, and settings "
                + "were restored. The launcher is ready.");
    }

    private async Task<int> RestoreAllActiveJournalsAsync(
        CancellationToken cancellationToken)
    {
        int restoredCount = 0;
        try
        {
            while (true)
            {
                IReadOnlyList<FourKayPreparedState> activeStates =
                    await _journalStore.ListActiveAsync(
                        PathLocator.StateRoot,
                        cancellationToken).ConfigureAwait(true);
                _activeRecoveryCount = activeStates.Count;
                _activeState = activeStates.Count > 0 ? activeStates[0] : null;
                if (_activeState is null)
                {
                    break;
                }

                FourKayPreparedState state = _activeState;
                SetStatus(
                    StatusTone.Working,
                    "RESTORING SAVED PLAYER PROFILE",
                    $"Restoring profile snapshot {restoredCount + 1} of at least "
                        + $"{restoredCount + activeStates.Count}, newest-first. "
                        + "The complete verified player-state set is being restored, "
                        + "including character layouts, hotbars, socials, spell sets, "
                        + "userdata, keybinds, and eqclient.ini.");
                FourKayRestoreResult result =
                    await _preparationService.RestoreAsync(
                        state,
                        cancellationToken).ConfigureAwait(true);
                if (result.Disposition == FourKayRestoreDisposition.GameStillRunning)
                {
                    throw new InvalidOperationException(
                        "EverQuest Legends is still running. Exit the game normally, "
                            + "wait for it to close, then choose Restore profile again. "
                            + $"{restoredCount} earlier "
                            + (restoredCount == 1
                                ? "profile snapshot was"
                                : "profile snapshots were")
                            + " already restored safely.");
                }

                restoredCount++;
            }
        }
        catch
        {
            try
            {
                await ReloadActiveRecoveryStateAsync(
                    rebindLegendsPath: false,
                    CancellationToken.None).ConfigureAwait(true);
                RefreshDisplayAndPlan();
            }
            catch
            {
                // Preserve the original restore failure. The journals remain on disk
                // and will be rediscovered at the next application start.
            }

            throw;
        }

        await ReloadActiveRecoveryStateAsync(
            rebindLegendsPath: false,
            CancellationToken.None).ConfigureAwait(true);
        RefreshDisplayAndPlan();
        return restoredCount;
    }

    private async Task LoadRecoveryStateAsync()
    {
        try
        {
            await ReloadActiveRecoveryStateAsync(
                rebindLegendsPath:
                    !PathLocator.IsLegendsDirectory(LegendsPathTextBox.Text),
                CancellationToken.None).ConfigureAwait(true);
            if (_activeState is null)
            {
                RefreshActionAvailability();
                return;
            }

            RefreshDisplayAndPlan();
            RefreshActionAvailability();
            SetRecoveryStatus();
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            SetStatus(
                StatusTone.Error,
                "RECOVERY CHECK FAILED",
                "A recovery journal exists but could not be read: " + exception.Message);
        }
    }

    private async Task ReloadActiveRecoveryStateAsync(
        bool rebindLegendsPath,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FourKayPreparedState> activeStates =
            await _journalStore.ListActiveAsync(
                PathLocator.StateRoot,
                cancellationToken).ConfigureAwait(true);
        _activeRecoveryCount = activeStates.Count;
        _activeState = activeStates.Count > 0 ? activeStates[0] : null;
        if (rebindLegendsPath && _activeState is not null)
        {
            LegendsPathTextBox.Text = _activeState.EqDirectory;
            RefreshSpinUiDetection(force: true);
        }
    }

    private async Task LoadScaleAwareLayoutStateAsync()
    {
        if (!PathLocator.IsLegendsDirectory(LegendsPathTextBox.Text))
        {
            return;
        }

        string eqDirectory = Path.GetFullPath(LegendsPathTextBox.Text.Trim());
        try
        {
            UiLayoutSessionState? state =
                await _layoutProfileService.LoadActiveAsync(
                    eqDirectory,
                    PathLocator.LayoutProfileRoot,
                    CancellationToken.None).ConfigureAwait(true);
            if (state is null)
            {
                return;
            }

            _activeLayoutSession = state;
            if (IsLegendsGameRunning(eqDirectory))
            {
                SelectLayoutSessionScale(state);
                SetStatus(
                    StatusTone.Info,
                    "SCALE-AWARE LAYOUT SESSION RESUMED",
                    $"The running game is using its automatic "
                        + $"{FormatPixels(state.ScaledResolution)} layout profile. "
                        + "Keep SpinFOURKAYYY open; personal UI layouts will be "
                        + "captured and returned to native size after Legends exits.");
                return;
            }

            string message = await FinalizeLayoutProfileIfGameClosedAsync(
                CancellationToken.None).ConfigureAwait(true)
                ?? "The pending scale-aware layout session was already resolved.";
            SetStatus(
                StatusTone.Ready,
                "SCALE-AWARE LAYOUT RECOVERED",
                message);
        }
        catch (Exception exception) when (IsExpectedUserFacingFailure(exception))
        {
            SetStatus(
                StatusTone.Error,
                "LAYOUT RECOVERY NEEDS ATTENTION",
                "A scale-aware UI layout journal could not be recovered safely: "
                    + exception.Message);
        }
    }

    private void SelectLayoutSessionScale(UiLayoutSessionState state)
    {
        double factor = Math.Min(
            (double)state.NativeResolution.Width / state.ScaledResolution.Width,
            (double)state.NativeResolution.Height / state.ScaledResolution.Height);
        _isUpdatingPresetCards = true;
        try
        {
            CurrentUiModeRadioButton.IsChecked = true;
            SpinUiModeRadioButton.IsChecked = false;
            FineScaleSlider.Value = Math.Clamp(
                Math.Round(factor * 100.0, MidpointRounding.AwayFromZero) / 100.0,
                FineScaleSlider.Minimum,
                FineScaleSlider.Maximum);
            SyncGenericPresetSelection(FineScaleSlider.Value);
        }
        finally
        {
            _isUpdatingPresetCards = false;
        }

        RefreshDisplayAndPlan();
    }

    private bool IsLegendsGameRunning(string eqDirectory)
    {
        try
        {
            string eqGamePath = Path.Combine(eqDirectory, "eqgame.exe");
            if (_processDiscovery.FindByExecutablePath(eqGamePath).Count > 0)
            {
                return true;
            }

            // Exact-path discovery can be denied for an elevated client. Treat any
            // remaining eqgame process as active so native layouts are never
            // restored beneath a client that might still be writing them.
            Process[] candidates = Process.GetProcessesByName("eqgame");
            try
            {
                return candidates.Length > 0;
            }
            finally
            {
                foreach (Process candidate in candidates)
                {
                    candidate.Dispose();
                }
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
        {
            return true;
        }
    }

    private bool RequiresScaleAwareLayout(ResolutionPlan plan) =>
        SelectedUiCompatibilityMode == FourKayUiCompatibilityMode.GenericOrCustom
        && plan.ActualUiScale > 1.005;

    private bool ActiveLayoutMatches(
        string eqDirectory,
        ResolutionPlan plan)
    {
        return _activeLayoutSession is { } state
            && string.Equals(
                state.EqDirectory,
                Path.GetFullPath(eqDirectory),
                StringComparison.OrdinalIgnoreCase)
            && state.NativeResolution == plan.TargetResolution
            && state.ScaledResolution == plan.SourceResolution;
    }

    private async Task<string?> FinalizeLayoutProfileIfGameClosedAsync(
        CancellationToken cancellationToken)
    {
        if (_activeLayoutSession is not { } state
            || IsLegendsGameRunning(state.EqDirectory))
        {
            return null;
        }

        if (state.Status == UiLayoutSessionStatus.Preparing)
        {
            UiLayoutSessionState rolledBack =
                await _layoutProfileService.RollbackAsync(
                    state,
                    cancellationToken).ConfigureAwait(true);
            _activeLayoutSession = null;
            return $"The incomplete {FormatPixels(rolledBack.ScaledResolution)} "
                + "layout preparation was rolled back to the verified native files.";
        }

        UiLayoutCompleteResult result = await _layoutProfileService.CompleteAsync(
            state,
            cancellationToken).ConfigureAwait(true);
        _activeLayoutSession = null;
        int completedLayoutCount = result.CapturedProfileCount > 0
            ? result.CapturedProfileCount
            : result.State.Entries.Count;
        return $"Saved or recovered {completedLayoutCount} scale-specific personal UI "
            + (completedLayoutCount == 1 ? "layout" : "layouts")
            + $" for {FormatPixels(result.State.ScaledResolution)} and returned "
            + "the live files to their native geometry. Macros, hotbuttons, socials, "
            + "spell sets, keybinds, and userdata were never touched.";
    }

    private async Task RollbackPreparedLayoutAsync(
        UiLayoutSessionState state,
        CancellationToken cancellationToken)
    {
        await _layoutProfileService.RollbackAsync(state, cancellationToken)
            .ConfigureAwait(true);
        if (ReferenceEquals(_activeLayoutSession, state)
            || _activeLayoutSession?.SessionId == state.SessionId)
        {
            _activeLayoutSession = null;
        }
    }

    private void SetRecoveryStatus()
    {
        if (_activeState is null)
        {
            return;
        }

        int recoveryCount = Math.Max(1, _activeRecoveryCount);
        SetStatus(
            StatusTone.Info,
            "OLDER PROFILE BACKUP AVAILABLE",
            $"{recoveryCount} saved player-profile "
                + (recoveryCount == 1 ? "snapshot" : "snapshots")
                + " from an older SpinFOURKAYYY version "
                + (recoveryCount == 1 ? "was" : "were")
                + " found. Nothing is restored automatically — your current "
                + "in-game settings and layouts stay untouched. Use Restore now "
                + "only if you want to roll back to that older backup.");
    }

    private string RequireValidLegendsDirectory()
    {
        string path = LegendsPathTextBox.Text.Trim();
        if (!PathLocator.IsLegendsDirectory(path))
        {
            throw new DirectoryNotFoundException(
                "Choose the EverQuest Legends folder containing eqgame.exe and eqclient.ini.");
        }

        return Path.GetFullPath(path);
    }

    private void ReplaceActiveLaunch(FourKayLaunchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _ = RestoreOverlayCompatibility(_activeLaunch);
        DisposeLaunchHandles(_activeLaunch);
        _activeLaunch = result;
        _displayedOverlayWarningCount =
            result.OverlayCompatibility?.Warnings.Count ?? 0;
    }

    private async Task<string?> RecoverPendingLayoutSessionBeforeLaunchAsync(
        string eqDirectory,
        CancellationToken cancellationToken)
    {
        UiLayoutSessionState? pending =
            await _layoutProfileService.LoadActiveAsync(
                eqDirectory,
                PathLocator.LayoutProfileRoot,
                cancellationToken).ConfigureAwait(true);
        if (pending is null)
        {
            return null;
        }

        _activeLayoutSession = pending;
        if (IsLegendsGameRunning(eqDirectory))
        {
            throw new InvalidOperationException(
                "An earlier scale-aware UI layout session was found, but "
                    + "EverQuest started running before it could be recovered. "
                    + "Exit every EverQuest client and use Start EverQuest for me "
                    + "again; SpinFOURKAYYY will recover it automatically.");
        }

        string? message = await FinalizeLayoutProfileIfGameClosedAsync(
            cancellationToken).ConfigureAwait(true);
        if (message is null)
        {
            throw new InvalidOperationException(
                "An earlier scale-aware UI layout session still belongs to a "
                    + "running EverQuest client. Exit the game, then try again so "
                    + "SpinFOURKAYYY can recover it safely.");
        }

        return message;
    }

    private string[] RestoreOverlayCompatibility(
        FourKayLaunchResult? launch)
    {
        if (launch?.OverlayCompatibility is not { } overlays)
        {
            return [];
        }

        int previousWarningCount = overlays.Warnings.Count;
        OverlayCompatibilityUpdate update =
            _overlayCompatibility.Restore(overlays);
        return update.Warnings.Skip(previousWarningCount).ToArray();
    }

    private static void DisposeLaunchHandles(FourKayLaunchResult? launch)
    {
        if (launch is null)
        {
            return;
        }

        launch.LauncherProcess?.Dispose();
        launch.GameProcess.Dispose();
        launch.MagpieProcess.Process.Dispose();
    }

    private void SetScaledSessionState(
        bool active,
        string message,
        IReadOnlyList<string>? warnings = null)
    {
        _isScaledSessionActive = active;
        _scalingCleanupRequired = false;
        _scalingSupervisionState = ScalingSessionSupervisionState.Active;
        AttachButton.Content = active
            ? "Stop fullscreen scaling"
            : "Launch required";
        InputSafetyText.Foreground = active
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("WarningBrush");
        InputSafetyText.Text = active
            ? "✓ Mouse map verified to within one source pixel"
            : "○ Mouse map verified after fullscreen starts";
        if (IsLoaded)
        {
            RefreshDisplayAndPlan();
        }

        string[] launchWarnings = warnings?
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? [];
        bool fsrFallback = launchWarnings.Any(
            warning => warning.Contains(
                "Smooth FSR",
                StringComparison.OrdinalIgnoreCase));
        string statusMessage = launchWarnings.Length == 0
            ? message
            : message + " Notes: " + string.Join(" ", launchWarnings);
        SetStatus(
            active && launchWarnings.Length > 0
                ? StatusTone.Warning
                : active
                    ? StatusTone.Ready
                    : StatusTone.Info,
            active
                ? fsrFallback
                    ? "FULLSCREEN VERIFIED · SMOOTH FSR ACTIVE"
                    : launchWarnings.Length > 0
                        ? "FULLSCREEN VERIFIED · NOTES"
                        : "FULLSCREEN VERIFIED"
                : "SCALING STOPPED",
            statusMessage);
    }

    private void SetStatus(StatusTone tone, string heading, string message)
    {
        Brush brush = tone switch
        {
            StatusTone.Ready => (Brush)FindResource("SuccessBrush"),
            StatusTone.Info => (Brush)FindResource("CyanBrush"),
            StatusTone.Warning => (Brush)FindResource("WarningBrush"),
            StatusTone.Error => (Brush)FindResource("DangerBrush"),
            StatusTone.Working => (Brush)FindResource("AccentBrush"),
            _ => (Brush)FindResource("MutedTextBrush"),
        };

        StatusDot.Fill = brush;
        StatusHeadingText.Foreground = brush;
        StatusHeadingText.Text = heading;
        StatusMessageText.Text = message;
        if (IsLoaded)
        {
            AutomationPeer? statusPeer =
                UIElementAutomationPeer.FromElement(StatusMessageText)
                ?? UIElementAutomationPeer.CreatePeerForElement(StatusMessageText);
            statusPeer?.RaiseAutomationEvent(
                AutomationEvents.LiveRegionChanged);
        }
    }

    private static string FormatPlan(ResolutionPlan plan) =>
        $"{FormatPixels(plan.SourceResolution)}  →  {FormatPixels(plan.TargetResolution)}";

    private static string FormatUiPercent(double scale) =>
        $"{Math.Round(scale * 100.0, MidpointRounding.AwayFromZero):0}%";

    private static string FormatPixels(PixelSize size) =>
        $"{size.Width:N0} × {size.Height:N0}";

    private static string FilterShortName(ScalingFilter filter) =>
        filter switch
        {
            ScalingFilter.Nis => "Readable UI",
            ScalingFilter.Fsr => "FSR",
            ScalingFilter.Lanczos => "Lanczos",
            ScalingFilter.NearestNeighbor => "Exact",
            _ => filter.ToString(),
        };

    private static bool IsExpectedUserFacingFailure(Exception exception) =>
        exception is ArgumentException
            or HttpRequestException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or JsonException
            or NotSupportedException
            or TimeoutException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or Win32Exception;

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { path },
            UseShellExecute = true,
        });
    }

    private static void OpenSpinUiRepository()
    {
        using Process? browser = Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/itsspin/spinips",
            UseShellExecute = true,
        });

        if (browser is null)
        {
            throw new InvalidOperationException(
                "Windows did not start the default browser.");
        }
    }

    private void ShowError(string title, string message)
    {
        _ = MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private enum StatusTone
    {
        Ready,
        Info,
        Warning,
        Error,
        Working,
    }

    private sealed record StartupDiscovery(
        IReadOnlyList<StartupGameCandidate> Candidates,
        int UninspectableProcessCount);

    private sealed record StartupGameCandidate(
        ProcessDescriptor Process,
        string EqDirectory);

    private sealed record DisplayChoice(MonitorDescriptor Monitor, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ScalingCleanupResult(bool Success, string Message);
}
