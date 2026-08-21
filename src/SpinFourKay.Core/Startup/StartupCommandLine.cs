using SpinFourKay.Core.Updates;

namespace SpinFourKay.Core.Startup;

/// <summary>
/// The unattended game start requested on the command line, if any.
/// </summary>
public enum AutoPlayMode
{
    /// <summary>No unattended start was requested.</summary>
    None = 0,

    /// <summary>Start the normal Legends launcher (<c>--play</c>).</summary>
    OfficialLauncher = 1,

    /// <summary>
    /// Start SpinTexture's verified enhanced flow (<c>--play-enhanced</c>).
    /// </summary>
    SpinTextureEnhanced = 2,
}

/// <summary>
/// The startup switches SpinFOURKAYYY understands, parsed once so the launch
/// gate, the running-game detector, and the self-test suite all agree on the
/// same interpretation of one argument list.
/// </summary>
/// <remarks>
/// Unrecognized arguments are ignored on purpose. The updater re-launches the
/// application with its own switches, and future releases must stay readable by
/// older parsers rather than refusing to open.
/// </remarks>
public sealed record StartupCommandLine
{
    /// <summary>Requests an unattended normal launch.</summary>
    public const string PlayArgument = "--play";

    /// <summary>Requests an unattended SpinTexture enhanced launch.</summary>
    public const string PlayEnhancedArgument = "--play-enhanced";

    /// <summary>Skips automatic running-game detection at startup.</summary>
    public const string ManualArgument = "--manual";

    /// <summary>The result of an empty or absent argument list.</summary>
    public static StartupCommandLine Default { get; } = new();

    /// <summary>The unattended start requested, if any.</summary>
    public AutoPlayMode AutoPlay { get; init; } = AutoPlayMode.None;

    /// <summary>
    /// True when <c>--manual</c> was supplied and automatic running-game
    /// detection should be skipped.
    /// </summary>
    public bool SkipRunningGameDetection { get; init; }

    /// <summary>
    /// A user-facing explanation of why a requested unattended start was
    /// refused, or <see langword="null"/> when nothing was refused. A rejection
    /// always leaves <see cref="AutoPlay"/> at <see cref="AutoPlayMode.None"/>
    /// so an ambiguous request never silently picks one launch path.
    /// </summary>
    public string? AutoPlayRejection { get; init; }

    /// <summary>True when an unattended start should be attempted.</summary>
    public bool RequestsAutoPlay => AutoPlay != AutoPlayMode.None;

    /// <summary>
    /// Interprets one argument list. Pass the arguments only; the executable
    /// path that <see cref="Environment.GetCommandLineArgs"/> places first is
    /// not expected here and would be ignored as an unrecognized argument.
    /// </summary>
    public static StartupCommandLine Parse(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return Default;
        }

        bool play = false;
        bool playEnhanced = false;
        bool manual = false;
        for (int index = 0; index < arguments.Count; index++)
        {
            string? argument = arguments[index];
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            string normalized = argument.Trim();

            // The updater supplies a version immediately after this switch.
            // Skipping the value keeps a malformed pair from being read as a
            // launch request.
            if (Matches(normalized, ReleaseUpdateInstaller.UpdatedFromArgument))
            {
                index++;
                continue;
            }

            if (Matches(normalized, PlayArgument))
            {
                play = true;
            }
            else if (Matches(normalized, PlayEnhancedArgument))
            {
                playEnhanced = true;
            }
            else if (Matches(normalized, ManualArgument))
            {
                manual = true;
            }
        }

        if (play && playEnhanced)
        {
            return new StartupCommandLine
            {
                AutoPlay = AutoPlayMode.None,
                SkipRunningGameDetection = manual,
                AutoPlayRejection =
                    $"{PlayArgument} and {PlayEnhancedArgument} were supplied "
                        + "together, so no game was started. Use exactly one of "
                        + "them, then try the shortcut again.",
            };
        }

        return new StartupCommandLine
        {
            AutoPlay = play
                ? AutoPlayMode.OfficialLauncher
                : playEnhanced
                    ? AutoPlayMode.SpinTextureEnhanced
                    : AutoPlayMode.None,
            SkipRunningGameDetection = manual,
        };
    }

    private static bool Matches(string argument, string switchName) =>
        string.Equals(argument, switchName, StringComparison.OrdinalIgnoreCase);
}
