using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpinFourKay.Core.IO;

namespace SpinFourKay.Core.Updates;

public sealed class GitHubReleaseUpdateService : IDisposable
{
    private const long MaximumMetadataBytes = 2L * 1024 * 1024;
    private const long MaximumPackageBytes = 320L * 1024 * 1024;
    private const long MaximumChecksumBytes = 4096;
    private static readonly Uri LatestReleaseApi = new(
        "https://api.github.com/repos/itsspin/SPINFOURKAYYY/releases/latest");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _updateRoot;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public GitHubReleaseUpdateService(
        string updateRoot,
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateRoot);
        _updateRoot = Path.GetFullPath(updateRoot);
        _httpClient = httpClient ?? CreateHttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public async Task<ReleaseUpdateCheck> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        CleanupCompletedUpdateDirectories();
        using HttpRequestMessage request = new(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        byte[] content = await ReadBoundedContentAsync(
            response,
            MaximumMetadataBytes,
            cancellationToken).ConfigureAwait(false);
        GitHubRelease release;
        try
        {
            release = JsonSerializer.Deserialize<GitHubRelease>(content, JsonOptions)
                ?? throw new InvalidDataException(
                    "GitHub returned an empty latest-release response.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "GitHub returned malformed latest-release metadata.",
                exception);
        }

        if (release.Draft || release.Prerelease)
        {
            throw new InvalidDataException(
                "GitHub's latest stable release endpoint returned a draft or prerelease.");
        }

        Version latestVersion = ParseStableTag(release.TagName);
        string versionText = latestVersion.ToString(3);
        string packageName = $"SpinFOURKAYYY-{versionText}-win-x64.zip";
        GitHubAsset package = RequireUniqueAsset(release.Assets, packageName);
        GitHubAsset checksum = RequireUniqueAsset(
            release.Assets,
            packageName + ".sha256");
        if (package.Size is <= 0 or > MaximumPackageBytes)
        {
            throw new InvalidDataException(
                "The GitHub release package size is outside the supported range.");
        }

        string packageHash = ParseAssetDigest(package.Digest);
        Uri releasePage = RequireGitHubUri(release.HtmlUrl, "/itsspin/SPINFOURKAYYY/");
        Uri packageDownload = RequireGitHubUri(
            package.BrowserDownloadUrl,
            "/itsspin/SPINFOURKAYYY/releases/download/");
        Uri checksumDownload = RequireGitHubUri(
            checksum.BrowserDownloadUrl,
            "/itsspin/SPINFOURKAYYY/releases/download/");
        return new ReleaseUpdateCheck(
            NormalizeThreePartVersion(currentVersion),
            new ReleaseUpdateInfo(
                latestVersion,
                release.TagName,
                releasePage,
                packageDownload,
                checksumDownload,
                packageName,
                package.Size,
                packageHash));
    }

    public async Task<PreparedReleaseUpdate> PrepareAsync(
        ReleaseUpdateInfo release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        string versionName = "v" + release.Version.ToString(3);
        string requestDirectory = Path.Combine(_updateRoot, versionName);
        EnsureDirectChild(requestDirectory, _updateRoot);
        if (Directory.Exists(requestDirectory))
        {
            Directory.Delete(requestDirectory, recursive: true);
        }

        Directory.CreateDirectory(requestDirectory);
        string packagePath = Path.Combine(
            requestDirectory,
            release.PackageFileName);
        string checksumPath = packagePath + ".sha256";
        string extractionRoot = Path.Combine(requestDirectory, "staged");
        try
        {
            string actualHash = await DownloadPackageAsync(
                release,
                packagePath,
                progress,
                cancellationToken).ConfigureAwait(false);
            await DownloadSmallFileAsync(
                release.ChecksumDownload,
                checksumPath,
                MaximumChecksumBytes,
                cancellationToken).ConfigureAwait(false);
            string sidecarHash = ParseChecksumSidecar(
                await File.ReadAllTextAsync(
                    checksumPath,
                    cancellationToken).ConfigureAwait(false),
                release.PackageFileName);
            if (!string.Equals(
                    actualHash,
                    sidecarHash,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    actualHash,
                    release.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The downloaded update does not match both GitHub's asset digest "
                        + "and the published SHA-256 sidecar.");
            }

            progress?.Report(92);
            string stagedAppDirectory =
                await ReleasePackageValidator.ExtractAndValidateAsync(
                    packagePath,
                    extractionRoot,
                    release.Version,
                    cancellationToken).ConfigureAwait(false);
            progress?.Report(100);
            return new PreparedReleaseUpdate(
                release,
                stagedAppDirectory,
                requestDirectory,
                actualHash);
        }
        catch
        {
            TryDeleteDirectory(requestDirectory);
            throw;
        }
    }

    public static async Task<Process> StartInstallerAsync(
        PreparedReleaseUpdate update,
        string currentExecutablePath,
        int currentProcessId,
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentExecutablePath);
        ArgumentNullException.ThrowIfNull(currentVersion);
        if (currentProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentProcessId),
                "The current process ID must be positive.");
        }

        string fullCurrentExecutable = Path.GetFullPath(currentExecutablePath);
        string targetDirectory = Path.GetDirectoryName(fullCurrentExecutable)
            ?? throw new InvalidOperationException(
                "The installed application has no parent folder.");
        string stagedExecutable = Path.Combine(
            update.StagedAppDirectory,
            "SpinFOURKAYYY.exe");
        if (!File.Exists(stagedExecutable))
        {
            throw new FileNotFoundException(
                "The verified staged updater executable is missing.",
                stagedExecutable);
        }

        UpdateApplyRequest request = new()
        {
            ExpectedVersion = update.Release.Version.ToString(3),
            PreviousVersion = NormalizeThreePartVersion(currentVersion).ToString(3),
            PreviousProcessId = currentProcessId,
            PreviousExecutablePath = fullCurrentExecutable,
            TargetDirectory = targetDirectory,
            StagedAppDirectory = update.StagedAppDirectory,
        };
        string requestPath = Path.Combine(
            update.RequestDirectory,
            "apply-update.json");
        byte[] requestBytes = JsonSerializer.SerializeToUtf8Bytes(
            request,
            IndentedJsonOptions);
        await AtomicFile.WriteAllBytesAsync(
            requestPath,
            requestBytes,
            cancellationToken).ConfigureAwait(false);
        ProcessStartInfo startInfo = new()
        {
            FileName = stagedExecutable,
            WorkingDirectory = update.StagedAppDirectory,
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add(requestPath);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Windows did not start the verified update installer.");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public static Version NormalizeThreePartVersion(Version version) =>
        new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private async Task<string> DownloadPackageAsync(
        ReleaseUpdateInfo release,
        string destinationPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, release.PackageDownload);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength != release.PackageSize)
        {
            throw new InvalidDataException(
                "The GitHub update download size does not match its release metadata.");
        }

        string temporaryPath = destinationPath + ".download";
        long total = 0;
        using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        {
            await using Stream source = await response.Content.ReadAsStreamAsync(
                cancellationToken).ConfigureAwait(false);
            await using FileStream destination = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true);
            byte[] buffer = GC.AllocateUninitializedArray<byte>(128 * 1024);
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > MaximumPackageBytes || total > release.PackageSize)
                {
                    throw new InvalidDataException(
                        "The GitHub update download exceeded its declared size.");
                }

                sha256.AppendData(buffer, 0, read);
                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(Math.Clamp(
                    checked((int)(total * 90 / release.PackageSize)),
                    0,
                    90));
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (total != release.PackageSize)
        {
            throw new InvalidDataException(
                "The GitHub update download ended before its declared size.");
        }

        string hash = Convert.ToHexString(sha256.GetHashAndReset());
        File.Move(temporaryPath, destinationPath);
        return hash;
    }

    private async Task DownloadSmallFileAsync(
        Uri uri,
        string destinationPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        byte[] content = await ReadBoundedContentAsync(
            response,
            maximumBytes,
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(destinationPath, content, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpResponseMessage response,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long contentLength
            && (contentLength < 0 || contentLength > maximumBytes))
        {
            throw new InvalidDataException(
                "The GitHub response is larger than the supported limit.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(
            cancellationToken).ConfigureAwait(false);
        using MemoryStream content = new();
        byte[] buffer = GC.AllocateUninitializedArray<byte>(16 * 1024);
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (content.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    "The GitHub response exceeded the supported limit.");
            }

            content.Write(buffer, 0, read);
        }

        return content.ToArray();
    }

    private static Version ParseStableTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)
            || tagName.Length < 2
            || tagName[0] is not ('v' or 'V')
            || !Version.TryParse(tagName[1..], out Version? parsed)
            || parsed.Major < 0
            || parsed.Minor < 0
            || parsed.Build < 0
            || parsed.Revision >= 0
            || !string.Equals(
                tagName[1..],
                parsed.ToString(3),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"GitHub release tag '{tagName}' is not a stable v<major>.<minor>.<patch> version.");
        }

        return NormalizeThreePartVersion(parsed);
    }

    private static GitHubAsset RequireUniqueAsset(
        IReadOnlyList<GitHubAsset> assets,
        string name)
    {
        GitHubAsset[] matches = assets.Where(asset => string.Equals(
            asset.Name,
            name,
            StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"GitHub release must contain exactly one '{name}' asset.");
        }

        return matches[0];
    }

    private static string ParseAssetDigest(string? digest)
    {
        const string Prefix = "sha256:";
        if (digest is null
            || !digest.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !ReleasePackageValidator.IsSha256(digest[Prefix.Length..]))
        {
            throw new InvalidDataException(
                "The GitHub release package has no valid SHA-256 asset digest.");
        }

        return digest[Prefix.Length..].ToUpperInvariant();
    }

    private static string ParseChecksumSidecar(string content, string packageName)
    {
        string[] lines = content.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
        {
            throw new InvalidDataException(
                "The published update checksum sidecar must contain exactly one line.");
        }

        string[] parts = lines[0].Split(
            [' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !ReleasePackageValidator.IsSha256(parts[0])
            || !string.Equals(
                parts[1].TrimStart('*'),
                packageName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The published update checksum sidecar is malformed or names a "
                    + "different package.");
        }

        return parts[0].ToUpperInvariant();
    }

    private static Uri RequireGitHubUri(string value, string requiredPathPrefix)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(
                requiredPathPrefix,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "GitHub release metadata contains an unexpected download or release URL.");
        }

        return uri;
    }

    private static void EnsureDirectChild(string candidate, string parent)
    {
        string fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        string fullCandidate = Path.GetFullPath(candidate);
        if (!string.Equals(
                Path.GetDirectoryName(fullCandidate),
                fullParent,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The update staging folder is outside its dedicated update root.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The verified failure is more useful than a best-effort cleanup error.
        }
    }

    private void CleanupCompletedUpdateDirectories()
    {
        if (!Directory.Exists(_updateRoot))
        {
            return;
        }

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(_updateRoot))
            {
                TryDeleteDirectory(directory);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort. A staged installer that is still exiting
            // can briefly keep its executable locked; the next check retries.
        }
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SpinFOURKAYYY-Updater/1.0");
        return client;
    }

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("assets")]
        public IReadOnlyList<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed record GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
