using System.IO.Compression;
using System.Text.Json;
using SpinFourKay.Core.IO;

namespace SpinFourKay.Core.Updates;

internal static class ReleasePackageValidator
{
    private const long MaximumExtractedBytes = 1024L * 1024 * 1024;
    private const long MaximumManifestBytes = 2L * 1024 * 1024;
    private const int MaximumFileCount = 20_000;
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<string> ExtractAndValidateAsync(
        string packagePath,
        string extractionRoot,
        Version expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        string fullPackagePath = Path.GetFullPath(packagePath);
        string fullExtractionRoot = Path.GetFullPath(extractionRoot);
        string expectedTopLevelName =
            $"SpinFOURKAYYY-{expectedVersion.ToString(3)}-win-x64";
        string expectedAppDirectory = Path.Combine(
            fullExtractionRoot,
            expectedTopLevelName);

        if (Directory.Exists(fullExtractionRoot))
        {
            Directory.Delete(fullExtractionRoot, recursive: true);
        }

        Directory.CreateDirectory(fullExtractionRoot);
        string extractionPrefix = Path.TrimEndingDirectorySeparator(
            fullExtractionRoot) + Path.DirectorySeparatorChar;
        HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);
        long extractedBytes = 0;
        int fileCount = 0;
        using ZipArchive archive = ZipFile.OpenRead(fullPackagePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            if (Path.IsPathRooted(relative)
                || relative.Contains(':', StringComparison.Ordinal)
                || relative.Split(Path.DirectorySeparatorChar)
                    .Any(segment => segment is ".." or "."))
            {
                throw new InvalidDataException(
                    $"The update package contains an unsafe path: '{entry.FullName}'.");
            }

            string targetPath = Path.GetFullPath(
                Path.Combine(fullExtractionRoot, relative));
            if (!targetPath.StartsWith(
                    extractionPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The update package escapes its staging folder: '{entry.FullName}'.");
            }

            string firstSegment = relative.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries)[0];
            if (!string.Equals(
                    firstSegment,
                    expectedTopLevelName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The update package does not have the expected single "
                        + $"'{expectedTopLevelName}' release folder.");
            }

            int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"The update package contains a link or reparse point: '{entry.FullName}'.");
            }

            bool isDirectory = entry.FullName.EndsWith('/')
                || entry.FullName.EndsWith('\\')
                || string.IsNullOrEmpty(entry.Name);
            if (isDirectory)
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            if (!targets.Add(targetPath))
            {
                throw new InvalidDataException(
                    $"The update package contains a duplicate Windows path: '{entry.FullName}'.");
            }

            fileCount++;
            extractedBytes = checked(extractedBytes + entry.Length);
            if (fileCount > MaximumFileCount
                || extractedBytes > MaximumExtractedBytes)
            {
                throw new InvalidDataException(
                    "The update package exceeds the supported extracted size or file count.");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(targetPath)
                    ?? throw new InvalidDataException(
                        "An update file has no parent directory."));
            await using Stream source = entry.Open();
            await using FileStream destination = new(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken)
                .ConfigureAwait(false);
        }

        await ValidateStagedAppAsync(
            expectedAppDirectory,
            expectedVersion,
            cancellationToken).ConfigureAwait(false);
        return expectedAppDirectory;
    }

    public static async Task<ReleasePackageManifest> ValidateStagedAppAsync(
        string appDirectory,
        Version expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        string fullAppDirectory = Path.GetFullPath(appDirectory);
        string manifestPath = Path.Combine(
            fullAppDirectory,
            "release-manifest.json");
        FileInfo manifestInfo = new(manifestPath);
        if (!manifestInfo.Exists
            || manifestInfo.Length is <= 0 or > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                "The update package has no valid release-manifest.json.");
        }

        byte[] manifestBytes = await File.ReadAllBytesAsync(
            manifestPath,
            cancellationToken).ConfigureAwait(false);
        ReleasePackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReleasePackageManifest>(
                    manifestBytes,
                    ManifestJsonOptions)
                ?? throw new InvalidDataException(
                    "The update release manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The update release manifest is malformed.",
                exception);
        }

        string versionText = expectedVersion.ToString(3);
        if (!string.Equals(
                manifest.Product,
                "SpinFOURKAYYY",
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.Version,
                versionText,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.Runtime,
                "win-x64",
                StringComparison.Ordinal)
            || manifest.Files.Count is <= 0 or > MaximumFileCount)
        {
            throw new InvalidDataException(
                "The update manifest product, version, runtime, or file count is invalid.");
        }

        string appPrefix = Path.TrimEndingDirectorySeparator(fullAppDirectory)
            + Path.DirectorySeparatorChar;
        Dictionary<string, ReleasePackageFile> declared =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (ReleasePackageFile file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = NormalizeOwnedRelativePath(file.Path);
            if (file.Size < 0 || !IsSha256(file.Sha256))
            {
                throw new InvalidDataException(
                    $"The update manifest entry '{file.Path}' is invalid.");
            }

            if (!declared.TryAdd(relativePath, file))
            {
                throw new InvalidDataException(
                    $"The update manifest contains duplicate path '{file.Path}'.");
            }

            string fullPath = Path.GetFullPath(
                Path.Combine(fullAppDirectory, relativePath));
            if (!fullPath.StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The update manifest path escapes its folder: '{file.Path}'.");
            }

            FileInfo info = new(fullPath);
            if (!info.Exists || info.Length != file.Size)
            {
                throw new InvalidDataException(
                    $"The staged update file '{file.Path}' is missing or has the wrong size.");
            }

            string actualHash = await FileHash.ComputeSha256Async(
                fullPath,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    actualHash,
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The staged update file '{file.Path}' failed SHA-256 verification.");
            }
        }

        string[] actualFiles = Directory.EnumerateFiles(
                fullAppDirectory,
                "*",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(fullAppDirectory, path))
            .Where(path => !string.Equals(
                path,
                "release-manifest.json",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (actualFiles.Length != declared.Count
            || actualFiles.Any(path => !declared.ContainsKey(path)))
        {
            throw new InvalidDataException(
                "The staged update contains undeclared or missing release files.");
        }

        if (!declared.ContainsKey("SpinFOURKAYYY.exe"))
        {
            throw new InvalidDataException(
                "The staged update does not declare SpinFOURKAYYY.exe.");
        }

        return manifest with
        {
            Files = declared
                .Select(pair => pair.Value with { Path = pair.Key })
                .ToArray(),
        };
    }

    public static string NormalizeOwnedRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Trim().Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)
            || normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split(Path.DirectorySeparatorChar)
                .Any(segment => string.IsNullOrWhiteSpace(segment)
                    || segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"Release manifest path '{path}' is unsafe.");
        }

        return normalized;
    }

    public static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');
}
