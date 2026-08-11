using System.Diagnostics;
using SpinFourKay.Core.Display;

namespace SpinFourKay.Core.Windows;

/// <summary>
/// Defines the bounded stability window used before a newly launched legacy
/// game is handed to fullscreen scaling. Consecutive observations include the
/// first sample, so a one-second interval at 100 ms requires eleven polls.
/// </summary>
public static class WindowInteractionReadinessPolicy
{
    public static TimeSpan DiscoveryPollInterval { get; } =
        TimeSpan.FromMilliseconds(100);

    public static TimeSpan ManagedLaunchStableDuration { get; } =
        TimeSpan.FromSeconds(1);

    public static int ManagedLaunchRequiredStablePolls { get; } =
        CalculateRequiredStablePolls(
            ManagedLaunchStableDuration,
            DiscoveryPollInterval);

    public static int CalculateRequiredStablePolls(
        TimeSpan stableDuration,
        TimeSpan pollInterval)
    {
        if (stableDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stableDuration),
                "The stable duration must be positive.");
        }

        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "The poll interval must be positive.");
        }

        long intervalCount = stableDuration.Ticks / pollInterval.Ticks;
        if (stableDuration.Ticks % pollInterval.Ticks != 0)
        {
            intervalCount = checked(intervalCount + 1);
        }
        int requiredPolls = checked((int)intervalCount + 1);
        if (requiredPolls is < 2 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stableDuration),
                "The requested stability interval must require between 2 and 20 polls.");
        }

        return requiredPolls;
    }

    public static Task<WindowDescriptor> WaitForManagedLaunchAsync(
        IWindowDiscoveryService windowDiscovery,
        Process process,
        PixelSize expectedClientSize,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(windowDiscovery);
        return windowDiscovery.WaitForStableVisibleWindowAsync(
            process,
            expectedClientSize,
            timeout,
            ManagedLaunchRequiredStablePolls,
            cancellationToken);
    }
}
