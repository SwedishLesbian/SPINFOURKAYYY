using System.IO;
using SpinFourKay.Core.Magpie;

namespace SpinFourKay.App.Infrastructure;

internal static class PathLocator
{
    private const string MagpieBundleVersion = "0.12.1";

    public static string? FindLegendsDirectory()
    {
        foreach (string candidate in EnumerateLegendsCandidates())
        {
            if (IsLegendsDirectory(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public static bool IsLegendsDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }

        return File.Exists(Path.Combine(fullPath, "eqgame.exe"))
            && File.Exists(Path.Combine(fullPath, "eqclient.ini"));
    }

    public static string? FindSpinTextureExecutable()
    {
        foreach (string candidate in EnumerateSpinTextureCandidates())
        {
            if (IsSpinTextureExecutable(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public static bool IsSpinTextureExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            return string.Equals(
                    Path.GetFileName(fullPath),
                    "SpinTexture.exe",
                    StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    public static string FindMagpieDirectory()
    {
        string appVersion = typeof(PathLocator).Assembly.GetName().Version
            ?.ToString(3)
            ?? "unknown";
        string runtimeKey = $"app-{appVersion}-magpie-{MagpieBundleVersion}";
        string runtimeRoot = Path.Combine(StateRoot, "engine-runtime");
        string prepared = Path.Combine(runtimeRoot, runtimeKey);
        if (MagpieRuntimeAssets.IsComplete(prepared))
        {
            return prepared;
        }

        foreach (string candidate in EnumerateMagpieCandidates())
        {
            if (MagpieRuntimeAssets.IsComplete(candidate))
            {
                try
                {
                    return MagpieRuntimeProvisioner.Prepare(
                        candidate,
                        runtimeRoot,
                        runtimeKey);
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
                {
                    throw new DirectoryNotFoundException(
                        "The bundled Magpie engine could not be copied to its stable "
                            + "per-version runtime. Close any old SpinFOURKAYYY or "
                            + "Magpie process, then re-extract the complete release.",
                        exception);
                }
            }
        }

        throw new DirectoryNotFoundException(
            "The bundled Magpie scaling engine or one of its required shaders is "
            + "missing. Re-extract the complete SpinFOURKAYYY release, including "
            + "Engine\\Magpie.");
    }

    public static string StateRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpinFOURKAYYY");

    public static string BackupRoot => Path.Combine(StateRoot, "backups");

    public static string LayoutProfileRoot =>
        Path.Combine(StateRoot, "layout-profiles");

    public static string PreferencesPath =>
        Path.Combine(StateRoot, "preferences.json");

    public static string UpdateRoot => Path.Combine(StateRoot, "updates");

    private static IEnumerable<string> EnumerateLegendsCandidates()
    {
        yield return Environment.CurrentDirectory;

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int depth = 0; directory is not null && depth < 6; depth++)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }

        yield return @"C:\EQLegends";
    }

    private static IEnumerable<string> EnumerateMagpieCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Engine", "Magpie");

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int depth = 0; directory is not null && depth < 7; depth++)
        {
            yield return Path.Combine(
                directory.FullName,
                "vendor",
                "Magpie-v0.12.1-x64");
            directory = directory.Parent;
        }
    }

    private static IEnumerable<string> EnumerateSpinTextureCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "SpinTexture.exe");

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int depth = 0; directory is not null && depth < 6; depth++)
        {
            yield return Path.Combine(directory.FullName, "SpinTexture.exe");
            yield return Path.Combine(
                directory.FullName,
                "SpinTexture",
                "SpinTexture.exe");
            directory = directory.Parent;
        }

        string localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
        yield return Path.Combine(localPrograms, "SpinTexture", "SpinTexture.exe");
        yield return Path.Combine(localPrograms, "spintexture", "SpinTexture.exe");
    }
}
