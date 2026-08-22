using System.Text.Json;
using System.Text.Json.Serialization;
using SpinFourKay.Core.Display;
using SpinFourKay.Core.IO;
using SpinFourKay.Core.Magpie;
using SpinFourKay.Core.Orchestration;

namespace SpinFourKay.Core.Preferences;

/// <summary>
/// The small, non-secret set of choices that should survive an application
/// restart. Active-session state and one-time safety confirmations deliberately
/// remain in their dedicated journals instead of this convenience file.
/// </summary>
public sealed record UserPreferences
{
    public const int CurrentFormatVersion = 1;

    public static UserPreferences Default { get; } = new();

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public string? LegendsDirectory { get; init; }

    public string? SpinTextureExecutablePath { get; init; }

    public PixelRect? TargetDisplayBounds { get; init; }

    public int UiScaleHundredths { get; init; } = 125;

    public ScalingFilter ScalingFilter { get; init; } = ScalingFilter.Nis;

    public AntiAliasingMode AntiAliasing { get; init; } = AntiAliasingMode.Off;

    public int ClarityPercent { get; init; } = 10;

    public bool MaintainTopmostOverlays { get; init; } = true;

    /// <summary>
    /// When true, a separately installed Magpie found blocking a launch is
    /// closed without asking each time. The preference is the consent: closing
    /// a process the user started is never done on a silent default, so this
    /// stays off unless it is deliberately turned on.
    /// </summary>
    public bool CloseConflictingMagpieAutomatically { get; init; }

    public FourKayUiCompatibilityMode UiCompatibilityMode { get; init; } =
        FourKayUiCompatibilityMode.GenericOrCustom;

    public int SpinUiPresetIndex { get; init; } = 2;
}

public sealed record UserPreferencesLoadResult(
    UserPreferences Preferences,
    bool WasLoaded,
    string? Warning);

/// <summary>
/// Loads and atomically replaces one bounded JSON preferences file. Corrupt or
/// unsupported files never prevent the application from opening; callers get
/// safe defaults plus a diagnostic warning.
/// </summary>
public sealed class UserPreferencesStore : IDisposable
{
    private const long MaximumPreferencesBytes = 64 * 1024;
    private const int MaximumSavedPathLength = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _preferencesPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UserPreferencesStore(string preferencesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferencesPath);
        _preferencesPath = Path.GetFullPath(preferencesPath);
        if (string.IsNullOrWhiteSpace(Path.GetDirectoryName(_preferencesPath)))
        {
            throw new ArgumentException(
                "The preferences file must have a parent directory.",
                nameof(preferencesPath));
        }
    }

    public string PreferencesPath => _preferencesPath;

    public async Task<UserPreferencesLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_preferencesPath))
            {
                return new UserPreferencesLoadResult(
                    UserPreferences.Default,
                    WasLoaded: false,
                    Warning: null);
            }

            try
            {
                FileInfo info = new(_preferencesPath);
                if (info.Length is <= 0 or > MaximumPreferencesBytes)
                {
                    throw new InvalidDataException(
                        "The saved settings file has an invalid size.");
                }

                byte[] content = await File.ReadAllBytesAsync(
                    _preferencesPath,
                    cancellationToken).ConfigureAwait(false);
                UserPreferences preferences =
                    JsonSerializer.Deserialize<UserPreferences>(content, JsonOptions)
                    ?? throw new InvalidDataException(
                        "The saved settings file is empty.");
                return new UserPreferencesLoadResult(
                    ValidateAndNormalize(preferences),
                    WasLoaded: true,
                    Warning: null);
            }
            catch (Exception exception) when (IsRecoverableLoadFailure(exception))
            {
                return new UserPreferencesLoadResult(
                    UserPreferences.Default,
                    WasLoaded: false,
                    "Saved settings could not be read safely, so the recommended "
                        + "defaults were used. " + exception.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        UserPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        UserPreferences normalized = ValidateAndNormalize(preferences);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions);
        if (content.LongLength > MaximumPreferencesBytes)
        {
            throw new InvalidDataException(
                "The saved settings exceed the supported size.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicFile.WriteAllBytesAsync(
                _preferencesPath,
                content,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static UserPreferences ValidateAndNormalize(UserPreferences preferences)
    {
        if (preferences.FormatVersion != UserPreferences.CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"Saved settings version {preferences.FormatVersion} is not supported.");
        }

        string? legendsDirectory = preferences.LegendsDirectory;
        if (string.IsNullOrWhiteSpace(legendsDirectory))
        {
            legendsDirectory = null;
        }
        else
        {
            legendsDirectory = legendsDirectory.Trim();
            if (legendsDirectory.Length > MaximumSavedPathLength)
            {
                throw new InvalidDataException(
                    "The saved EverQuest folder path is too long.");
            }

            legendsDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(legendsDirectory));
        }

        string? spinTextureExecutablePath =
            NormalizeOptionalPath(
                preferences.SpinTextureExecutablePath,
                "saved SpinTexture executable path");

        if (preferences.TargetDisplayBounds is { } displayBounds
            && (displayBounds.Width is <= 0 or > 100_000
                || displayBounds.Height is <= 0 or > 100_000
                || displayBounds.X is < -1_000_000 or > 1_000_000
                || displayBounds.Y is < -1_000_000 or > 1_000_000))
        {
            throw new InvalidDataException(
                "The saved display bounds are outside the supported range.");
        }

        _ = new FineUiScale(preferences.UiScaleHundredths);
        if (!Enum.IsDefined(preferences.ScalingFilter))
        {
            throw new InvalidDataException("The saved scaling filter is not supported.");
        }

        if (!Enum.IsDefined(preferences.AntiAliasing))
        {
            throw new InvalidDataException(
                "The saved anti-aliasing option is not supported.");
        }

        if (preferences.ClarityPercent is < 0 or > 20)
        {
            throw new InvalidDataException(
                "The saved edge-detail strength is outside the supported range.");
        }

        if (preferences.UiCompatibilityMode
            is not FourKayUiCompatibilityMode.GenericOrCustom
                and not FourKayUiCompatibilityMode.SpinUiStrict)
        {
            throw new InvalidDataException(
                "The saved UI compatibility mode is not supported.");
        }

        if (preferences.SpinUiPresetIndex is < 0 or > 2)
        {
            throw new InvalidDataException(
                "The saved SpinUI source choice is not supported.");
        }

        return preferences with
        {
            LegendsDirectory = legendsDirectory,
            SpinTextureExecutablePath = spinTextureExecutablePath,
        };
    }

    private static string? NormalizeOptionalPath(string? path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();
        if (trimmed.Length > MaximumSavedPathLength)
        {
            throw new InvalidDataException($"The {description} is too long.");
        }

        return Path.GetFullPath(trimmed);
    }

    private static bool IsRecoverableLoadFailure(Exception exception) =>
        exception is ArgumentException
            or IOException
            or InvalidDataException
            or JsonException
            or NotSupportedException
            or UnauthorizedAccessException
            or System.Security.SecurityException;
}
