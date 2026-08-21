using System.IO;
using SpinFourKay.App.Infrastructure;
using SpinFourKay.Core.Startup;
using SpinFourKay.Core.Updates;

namespace SpinFourKay.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName =
        @"Local\SpinFOURKAYYY_91B84F58-9897-4A22-ABAE-B012A6EE89B3";

    private static Mutex? _singleInstanceMutex;
    private static bool _ownsSingleInstanceMutex;

    public static string? UpdatedFromVersion { get; private set; }

    /// <summary>
    /// The startup switches this instance was opened with. Parsed once here so
    /// the launch gate and the running-game detector cannot disagree about what
    /// was requested.
    /// </summary>
    public static StartupCommandLine StartupSwitches { get; private set; } =
        StartupCommandLine.Default;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (ReleaseUpdateInstaller.IsApplyCommand(e.Args))
        {
            RunUpdateInstaller(e.Args);
            return;
        }

        StartupSwitches = StartupCommandLine.Parse(e.Args);
        UpdatedFromVersion = ReadUpdatedFromVersion(e.Args);
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out bool createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            _ = System.Windows.MessageBox.Show(
                "SpinFOURKAYYY is already open. Use the existing window so two "
                    + "sessions cannot prepare the same Legends client at once.",
                "SpinFOURKAYYY is already running",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    private static void RunUpdateInstaller(string[] arguments)
    {
        try
        {
            if (arguments.Length != 2)
            {
                throw new InvalidDataException(
                    "The update installer requires one verified request file.");
            }

            _ = ReleaseUpdateInstaller.ApplyFromRequestAsync(
                    arguments[1],
                    PathLocator.UpdateRoot,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Current.Shutdown(0);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or InvalidDataException
                or InvalidOperationException
                or NotSupportedException
                or UnauthorizedAccessException
                or System.Security.SecurityException
                or TimeoutException)
        {
            _ = System.Windows.MessageBox.Show(
                "SpinFOURKAYYY could not finish the update. "
                    + exception.Message,
                "SpinFOURKAYYY update needs attention",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Current.Shutdown(1);
        }
    }

    private static string? ReadUpdatedFromVersion(string[] arguments)
    {
        for (int index = 0; index + 1 < arguments.Length; index++)
        {
            if (string.Equals(
                    arguments[index],
                    ReleaseUpdateInstaller.UpdatedFromArgument,
                    StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(arguments[index + 1], out Version? version)
                && version.Build >= 0)
            {
                return version.ToString(3);
            }
        }

        return null;
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }
}
