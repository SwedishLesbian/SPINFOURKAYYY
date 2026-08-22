using System.Diagnostics;
using SpinFourKay.Core.Windows;

namespace SpinFourKay.Core.Magpie;

public sealed class MagpieProcessService : IMagpieProcessService
{
    private const string MagpieQuitMessageName = "WM_MAGPIE_QUIT";

    public IReadOnlyList<MagpieRunningInstance> InspectRunningInstances(string magpieDirectory)
    {
        string executablePath = ValidateMagpieDirectory(magpieDirectory);
        string? runtimeRoot = Path.GetDirectoryName(
            Path.GetDirectoryName(executablePath));
        List<MagpieRunningInstance> instances = [];

        foreach (Process process in Process.GetProcessesByName("Magpie"))
        {
            using (process)
            {
                string? runningPath = ProcessDiscoveryService.TryGetExecutablePath(process.Id);
                MagpieInstanceOrigin origin = ClassifyOrigin(
                    runningPath,
                    executablePath,
                    runtimeRoot);
                instances.Add(new MagpieRunningInstance(
                    process.Id,
                    runningPath,
                    origin == MagpieInstanceOrigin.BundledCurrent)
                {
                    Origin = origin,
                });
            }
        }

        return instances;
    }

    public Process? TryFindRunning(string magpieDirectory)
    {
        MagpieRunningInstance? bundled = InspectRunningInstances(magpieDirectory)
            .FirstOrDefault(instance => instance.IsBundledInstance);
        if (bundled is null)
        {
            return null;
        }

        try
        {
            return Process.GetProcessById(bundled.ProcessId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public MagpieProcessStartResult StartPortable(
        string magpieDirectory,
        bool startInTray = true)
    {
        string executablePath = ValidateMagpieDirectory(magpieDirectory);
        string fullDirectory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("Magpie.exe has no parent directory.");
        MagpieRuntimeAssets.EnsureComplete(fullDirectory);
        string configPath = Path.Combine(fullDirectory, "config", "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                "Portable Magpie config was not generated before launch.",
                configPath);
        }

        IReadOnlyList<MagpieRunningInstance> instances = InspectRunningInstances(fullDirectory);
        MagpieRunningInstance[] external = instances
            .Where(instance => instance.IsForeignInstance)
            .ToArray();
        if (external.Length > 0)
        {
            throw new ExternalMagpieInstanceConflictException(external);
        }

        MagpieRunningInstance? bundled = instances.FirstOrDefault(
            instance => instance.IsBundledInstance);
        if (bundled is not null)
        {
            try
            {
                return new MagpieProcessStartResult(
                    Process.GetProcessById(bundled.ProcessId),
                    AlreadyRunning: true);
            }
            catch (ArgumentException)
            {
                // It exited between inspection and acquisition; start our copy below.
            }
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            WorkingDirectory = fullDirectory,
            UseShellExecute = false,
        };
        if (startInTray)
        {
            startInfo.ArgumentList.Add("-t");
        }

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start Magpie.");

        if (process.WaitForExit(milliseconds: 500))
        {
            process.Dispose();
            MagpieRunningInstance[] conflicts = InspectRunningInstances(fullDirectory)
                .Where(instance => instance.IsForeignInstance)
                .ToArray();
            if (conflicts.Length > 0)
            {
                throw new ExternalMagpieInstanceConflictException(conflicts);
            }

            throw new InvalidOperationException(
                "Magpie exited before the portable profile became active.");
        }

        return new MagpieProcessStartResult(process, AlreadyRunning: false);
    }

    /// <summary>
    /// Closes any Magpie left running from a superseded SpinFOURKAYYY engine
    /// runtime and returns the process ids that exited.
    /// </summary>
    /// <remarks>
    /// Upgrading the application changes the engine runtime path, so a Magpie
    /// left in the tray by the previous version would otherwise look like a
    /// separately installed one. Closing it is routine housekeeping rather than
    /// a decision the user needs to make, because the process belongs to this
    /// application.
    /// <para>
    /// Magpie shuts down through a broadcast message, which every instance
    /// receives. That makes this unsafe to run while a foreign Magpie is
    /// present, so this method does nothing at all in that case and leaves the
    /// conflict to be reported and consented to instead.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Closes every running Magpie, including instances this application did
    /// not provision, and returns the process ids that exited.
    /// </summary>
    /// <remarks>
    /// This closes a Magpie the user started themselves, so it must only be
    /// called once they have explicitly agreed to it. Shutdown uses Magpie's own
    /// quit message rather than terminating the process, so it saves its state
    /// and exits the way it would from its own tray menu.
    /// </remarks>
    public async Task<IReadOnlyList<int>> ShutdownAllConsentedAsync(
        string magpieDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(8);
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Timeout must be positive.");
        }

        IReadOnlyList<MagpieRunningInstance> instances =
            InspectRunningInstances(magpieDirectory);
        if (instances.Count == 0)
        {
            return [];
        }

        uint quitMessage = NativeMethods.RegisterWindowMessageW(MagpieQuitMessageName);
        if (quitMessage == 0
            || !NativeMethods.PostMessageW(
                NativeMethods.HwndBroadcast,
                quitMessage,
                nint.Zero,
                nint.Zero))
        {
            return [];
        }

        List<int> closed = [];
        using CancellationTokenSource timeoutCancellation = new(effectiveTimeout);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        foreach (MagpieRunningInstance instance in instances)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(instance.ProcessId);
            }
            catch (ArgumentException)
            {
                closed.Add(instance.ProcessId);
                continue;
            }

            using (process)
            {
                try
                {
                    await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                    closed.Add(instance.ProcessId);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested
                    && timeoutCancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        return closed;
    }

    public async Task<IReadOnlyList<int>> ShutdownSupersededAsync(
        string magpieDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Timeout must be positive.");
        }

        IReadOnlyList<MagpieRunningInstance> instances =
            InspectRunningInstances(magpieDirectory);
        if (instances.Any(instance => instance.IsForeignInstance))
        {
            return [];
        }

        MagpieRunningInstance[] superseded = instances
            .Where(instance => instance.IsSupersededOwnRuntime)
            .ToArray();
        if (superseded.Length == 0)
        {
            return [];
        }

        uint quitMessage = NativeMethods.RegisterWindowMessageW(MagpieQuitMessageName);
        if (quitMessage == 0
            || !NativeMethods.PostMessageW(
                NativeMethods.HwndBroadcast,
                quitMessage,
                nint.Zero,
                nint.Zero))
        {
            return [];
        }

        List<int> closed = [];
        using CancellationTokenSource timeoutCancellation = new(effectiveTimeout);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        foreach (MagpieRunningInstance instance in superseded)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(instance.ProcessId);
            }
            catch (ArgumentException)
            {
                closed.Add(instance.ProcessId);
                continue;
            }

            using (process)
            {
                try
                {
                    await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                    closed.Add(instance.ProcessId);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested
                    && timeoutCancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        return closed;
    }

    public async Task<bool> ShutdownBundledAsync(
        string magpieDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        IReadOnlyList<MagpieRunningInstance> instances =
            InspectRunningInstances(magpieDirectory);
        MagpieRunningInstance[] external = instances
            .Where(instance => instance.IsForeignInstance)
            .ToArray();
        if (external.Length > 0)
        {
            throw new ExternalMagpieInstanceConflictException(external);
        }

        MagpieRunningInstance? bundled = instances.FirstOrDefault(
            instance => instance.IsBundledInstance);
        if (bundled is null)
        {
            return true;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(bundled.ProcessId);
        }
        catch (ArgumentException)
        {
            return true;
        }

        using (process)
        {
            uint quitMessage = NativeMethods.RegisterWindowMessageW(MagpieQuitMessageName);
            if (quitMessage == 0
                || !NativeMethods.PostMessageW(
                    NativeMethods.HwndBroadcast,
                    quitMessage,
                    nint.Zero,
                    nint.Zero))
            {
                return false;
            }

            using CancellationTokenSource timeoutCancellation =
                new(effectiveTimeout);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCancellation.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested
                && timeoutCancellation.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    public async Task<bool> ShutdownExactAsync(
        string magpieDirectory,
        Process process,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        string expectedExecutablePath = ValidateMagpieDirectory(magpieDirectory);
        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        if (process.HasExited)
        {
            return true;
        }

        string? actualExecutablePath =
            ProcessDiscoveryService.TryGetExecutablePath(process.Id);
        if (!string.Equals(
            actualExecutablePath,
            expectedExecutablePath,
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IReadOnlyList<MagpieRunningInstance> instances =
            InspectRunningInstances(magpieDirectory);
        if (instances.Count != 1
            || !instances[0].IsBundledInstance
            || instances[0].ProcessId != process.Id)
        {
            return false;
        }

        uint quitMessage = NativeMethods.RegisterWindowMessageW(MagpieQuitMessageName);
        if (quitMessage == 0
            || !NativeMethods.PostMessageW(
                NativeMethods.HwndBroadcast,
                quitMessage,
                nint.Zero,
                nint.Zero))
        {
            return false;
        }

        using CancellationTokenSource timeoutCancellation =
            new(effectiveTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return process.HasExited
                && InspectRunningInstances(magpieDirectory).Count == 0;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && timeoutCancellation.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Decides where a running Magpie came from, without touching any process.
    /// </summary>
    /// <remarks>
    /// The engine is provisioned per application version, so upgrading changes
    /// the path SpinFOURKAYYY runs Magpie from. An instance left in the tray by
    /// the previous version is still this application's own engine and must not
    /// be reported as a separately installed Magpie. An unreadable path is
    /// treated as foreign, so an instance that cannot be identified is never
    /// closed without asking.
    /// </remarks>
    public static MagpieInstanceOrigin ClassifyOrigin(
        string? runningExecutablePath,
        string currentExecutablePath,
        string? runtimeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentExecutablePath);
        if (string.IsNullOrWhiteSpace(runningExecutablePath))
        {
            return MagpieInstanceOrigin.Foreign;
        }

        if (string.Equals(
                runningExecutablePath,
                currentExecutablePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return MagpieInstanceOrigin.BundledCurrent;
        }

        if (string.IsNullOrWhiteSpace(runtimeRoot))
        {
            return MagpieInstanceOrigin.Foreign;
        }

        string? runningDirectory = Path.GetDirectoryName(runningExecutablePath);
        if (string.IsNullOrWhiteSpace(runningDirectory))
        {
            return MagpieInstanceOrigin.Foreign;
        }

        string? runningParent = Path.GetDirectoryName(runningDirectory);
        return runningParent is not null
            && string.Equals(
                Path.TrimEndingDirectorySeparator(runningParent),
                Path.TrimEndingDirectorySeparator(runtimeRoot),
                StringComparison.OrdinalIgnoreCase)
            && MagpieRuntimeProvisioner.IsRuntimeDirectoryName(
                Path.GetFileName(runningDirectory))
                ? MagpieInstanceOrigin.OwnPreviousRuntime
                : MagpieInstanceOrigin.Foreign;
    }

    private static string ValidateMagpieDirectory(string magpieDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magpieDirectory);
        string fullDirectory = Path.GetFullPath(magpieDirectory);
        string executablePath = Path.Combine(fullDirectory, "Magpie.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Magpie.exe was not found.", executablePath);
        }

        return executablePath;
    }
}
