using System.Diagnostics;
using System.Text.Json;
using SpinFourKay.Core.IO;
using SpinFourKay.Core.Windows;

namespace SpinFourKay.Core.Updates;

public static class ReleaseUpdateInstaller
{
    public const string ApplyArgument = "--apply-update";
    public const string UpdatedFromArgument = "--updated-from";
    private const long MaximumRequestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static bool IsApplyCommand(IReadOnlyList<string> arguments) =>
        arguments.Count > 0
        && string.Equals(
            arguments[0],
            ApplyArgument,
            StringComparison.OrdinalIgnoreCase);

    public static async Task<UpdateApplyResult> ApplyFromRequestAsync(
        string requestPath,
        string updateRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(updateRoot);
        string fullUpdateRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(updateRoot));
        string fullRequestPath = Path.GetFullPath(requestPath);
        string requestDirectory = Path.GetDirectoryName(fullRequestPath)
            ?? throw new InvalidDataException(
                "The update request has no parent directory.");
        if (!string.Equals(
                Path.GetDirectoryName(requestDirectory),
                fullUpdateRoot,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetFileName(fullRequestPath),
                "apply-update.json",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The update request is outside SpinFOURKAYYY's dedicated update folder.");
        }

        FileInfo requestInfo = new(fullRequestPath);
        if (!requestInfo.Exists
            || requestInfo.Length is <= 0 or > MaximumRequestBytes)
        {
            throw new InvalidDataException(
                "The update request is missing or outside the supported size.");
        }

        UpdateApplyRequest request;
        try
        {
            request = JsonSerializer.Deserialize<UpdateApplyRequest>(
                    await File.ReadAllBytesAsync(
                        fullRequestPath,
                        cancellationToken).ConfigureAwait(false),
                    JsonOptions)
                ?? throw new InvalidDataException("The update request is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The update request is malformed.",
                exception);
        }

        ValidateRequest(request, requestDirectory);
        Version expectedVersion = ParseVersion(
            request.ExpectedVersion,
            "expected update version");
        _ = ParseVersion(request.PreviousVersion, "previous version");
        ReleasePackageManifest manifest =
            await ReleasePackageValidator.ValidateStagedAppAsync(
                request.StagedAppDirectory,
                expectedVersion,
                cancellationToken).ConfigureAwait(false);
        await WaitForPreviousProcessAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return await ApplyFilesTransactionalAsync(
            request,
            manifest,
            restartApplication: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<UpdateApplyResult> ApplyFilesTransactionalAsync(
        UpdateApplyRequest request,
        ReleasePackageManifest newManifest,
        bool restartApplication = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(newManifest);
        string targetDirectory = Path.GetFullPath(request.TargetDirectory);
        EnsureSafeTargetDirectory(targetDirectory, request.PreviousExecutablePath);
        // Store rollback beside the request/staging tree, never in the install
        // directory whose files are being replaced.
        string rollbackParent = Directory.GetParent(
                Path.GetFullPath(request.StagedAppDirectory))?.Parent?.FullName
            ?? throw new InvalidDataException(
                "The staged update has no dedicated request directory.");
        string rollbackDirectory = Path.Combine(
            rollbackParent,
            "rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rollbackDirectory);

        Dictionary<string, ReleasePackageFile> newFiles = new(
            StringComparer.OrdinalIgnoreCase);
        foreach (ReleasePackageFile file in newManifest.Files)
        {
            string relative = ReleasePackageValidator.NormalizeOwnedRelativePath(
                file.Path);
            if (!newFiles.TryAdd(relative, file))
            {
                throw new InvalidDataException(
                    $"The new release repeats owned file '{file.Path}'.");
            }
        }

        HashSet<string> oldFiles = await ReadOwnedPathsAsync(
            Path.Combine(targetDirectory, "release-manifest.json"),
            cancellationToken).ConfigureAwait(false);
        HashSet<string> impacted = new(newFiles.Keys, StringComparer.OrdinalIgnoreCase)
        {
            "release-manifest.json",
        };
        impacted.UnionWith(oldFiles.Except(newFiles.Keys));

        List<string> originallyPresent = [];
        try
        {
            foreach (string relative in impacted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string targetPath = ResolveOwnedTarget(targetDirectory, relative);
                if (!File.Exists(targetPath))
                {
                    continue;
                }

                string backupPath = ResolveOwnedTarget(rollbackDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(targetPath, backupPath, overwrite: false);
                originallyPresent.Add(relative);
            }

            foreach ((string relative, ReleasePackageFile file) in newFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string sourcePath = ResolveOwnedTarget(
                    request.StagedAppDirectory,
                    relative);
                string targetPath = ResolveOwnedTarget(targetDirectory, relative);
                ReplaceFromVerifiedSource(sourcePath, targetPath);
                string installedHash = await FileHash.ComputeSha256Async(
                    targetPath,
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(
                        installedHash,
                        file.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Installed update file '{relative}' failed verification.");
                }
            }

            await AtomicFile.WriteAllBytesAsync(
                Path.Combine(targetDirectory, "release-manifest.json"),
                JsonSerializer.SerializeToUtf8Bytes(newManifest, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            foreach (string stale in oldFiles.Except(newFiles.Keys))
            {
                string stalePath = ResolveOwnedTarget(targetDirectory, stale);
                if (File.Exists(stalePath))
                {
                    File.Delete(stalePath);
                }
            }

            PruneEmptyOwnedDirectories(targetDirectory, oldFiles.Except(newFiles.Keys));
            string installedExecutable = Path.Combine(
                targetDirectory,
                "SpinFOURKAYYY.exe");
            if (!File.Exists(installedExecutable))
            {
                throw new FileNotFoundException(
                    "The updated SpinFOURKAYYY executable is missing.",
                    installedExecutable);
            }

            if (restartApplication)
            {
                StartInstalledApplication(
                    installedExecutable,
                    UpdatedFromArgument,
                    request.PreviousVersion);
            }

            return new UpdateApplyResult(
                request.ExpectedVersion,
                installedExecutable,
                rollbackDirectory);
        }
        catch (Exception installFailure)
        {
            Exception? rollbackFailure = null;
            try
            {
                RestoreRollback(
                    targetDirectory,
                    rollbackDirectory,
                    impacted,
                    originallyPresent);
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                rollbackFailure = exception;
            }

            string installedExecutable = Path.Combine(
                targetDirectory,
                "SpinFOURKAYYY.exe");
            if (restartApplication && rollbackFailure is null)
            {
                try
                {
                    StartInstalledApplication(installedExecutable);
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException
                        or System.ComponentModel.Win32Exception)
                {
                    rollbackFailure = exception;
                }
            }

            throw new InvalidOperationException(
                rollbackFailure is null
                    ? "The update could not be installed, so every replaced app "
                        + "file was restored and the previous version was reopened. "
                        + installFailure.Message
                    : "The update failed and the previous app files could not be "
                        + "fully restored or reopened. Recovery files remain at '"
                        + rollbackDirectory + "'. Install failure: "
                        + installFailure.Message + " Recovery failure: "
                        + rollbackFailure.Message,
                rollbackFailure ?? installFailure);
        }
    }

    private static void ValidateRequest(
        UpdateApplyRequest request,
        string requestDirectory)
    {
        if (request.FormatVersion != UpdateApplyRequest.CurrentFormatVersion
            || request.PreviousProcessId <= 0)
        {
            throw new InvalidDataException(
                "The update request version or previous process identity is invalid.");
        }

        string stagedDirectory = Path.GetFullPath(request.StagedAppDirectory);
        string stagedPrefix = Path.Combine(
            Path.GetFullPath(requestDirectory),
            "staged") + Path.DirectorySeparatorChar;
        if (!stagedDirectory.StartsWith(
                stagedPrefix,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetFileName(stagedDirectory),
                $"SpinFOURKAYYY-{request.ExpectedVersion}-win-x64",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The staged application is outside the verified update request.");
        }

        string runningDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(AppContext.BaseDirectory));
        if (!string.Equals(
                runningDirectory,
                Path.TrimEndingDirectorySeparator(stagedDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The update installer is not running from the verified staged release.");
        }

        EnsureSafeTargetDirectory(
            Path.GetFullPath(request.TargetDirectory),
            request.PreviousExecutablePath);
    }

    private static async Task WaitForPreviousProcessAsync(
        UpdateApplyRequest request,
        CancellationToken cancellationToken)
    {
        Process? previous = null;
        try
        {
            previous = Process.GetProcessById(request.PreviousProcessId);
            string? actualPath = ProcessDiscoveryService.TryGetExecutablePath(
                previous.Id);
            if (actualPath is not null
                && !string.Equals(
                    Path.GetFullPath(actualPath),
                    Path.GetFullPath(request.PreviousExecutablePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The process that requested this update has been replaced. "
                        + "No installed files were changed.");
            }

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(90));
            using CancellationTokenSource combined =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeout.Token);
            await previous.WaitForExitAsync(combined.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The old process can exit before the staged updater reaches this
            // check. The exact target/staging/request binding is still verified.
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The previous SpinFOURKAYYY process did not close in time. No "
                    + "installed files were changed.",
                exception);
        }
        finally
        {
            previous?.Dispose();
        }
    }

    private static void EnsureSafeTargetDirectory(
        string targetDirectory,
        string previousExecutablePath)
    {
        string fullTarget = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(targetDirectory));
        string root = Path.GetPathRoot(fullTarget)
            ?? throw new InvalidDataException(
                "The installed application folder has no drive root.");
        if (string.Equals(fullTarget, Path.TrimEndingDirectorySeparator(root),
                StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(Path.Combine(fullTarget, ".git"))
            || File.Exists(Path.Combine(fullTarget, "eqgame.exe"))
            || File.Exists(Path.Combine(fullTarget, "eqclient.ini"))
            || File.Exists(Path.Combine(fullTarget, "LaunchPad.exe")))
        {
            throw new InvalidDataException(
                "Automatic update requires SpinFOURKAYYY to be extracted in its "
                    + "own folder, not a drive root, source checkout, or EverQuest "
                    + "installation. No files were changed.");
        }

        string expectedExecutable = Path.Combine(fullTarget, "SpinFOURKAYYY.exe");
        if (!string.Equals(
                expectedExecutable,
                Path.GetFullPath(previousExecutablePath),
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(expectedExecutable))
        {
            throw new InvalidDataException(
                "The installed SpinFOURKAYYY executable does not match the update request.");
        }
    }

    private static async Task<HashSet<string>> ReadOwnedPathsAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            ReleasePackageManifest? manifest =
                JsonSerializer.Deserialize<ReleasePackageManifest>(
                    await File.ReadAllBytesAsync(
                        manifestPath,
                        cancellationToken).ConfigureAwait(false),
                    JsonOptions);
            if (manifest is null
                || !string.Equals(
                    manifest.Product,
                    "SpinFOURKAYYY",
                    StringComparison.Ordinal))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return manifest.Files
                .Select(file =>
                    ReleasePackageValidator.NormalizeOwnedRelativePath(file.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is JsonException
                or InvalidDataException
                or IOException
                or UnauthorizedAccessException)
        {
            // A missing or damaged old manifest only disables stale-file cleanup;
            // new verified files can still replace their exact destinations.
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string ResolveOwnedTarget(string root, string relative)
    {
        string normalized = ReleasePackageValidator.NormalizeOwnedRelativePath(relative);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        if (!fullPath.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Owned update path '{relative}' escapes its root.");
        }

        return fullPath;
    }

    private static void ReplaceFromVerifiedSource(string source, string target)
    {
        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                "A verified update source file is missing.",
                source);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".update-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void RestoreRollback(
        string targetDirectory,
        string rollbackDirectory,
        IEnumerable<string> impacted,
        IReadOnlyCollection<string> originallyPresent)
    {
        HashSet<string> originals = originallyPresent.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        foreach (string relative in impacted)
        {
            string targetPath = ResolveOwnedTarget(targetDirectory, relative);
            if (originals.Contains(relative))
            {
                string backupPath = ResolveOwnedTarget(rollbackDirectory, relative);
                ReplaceFromVerifiedSource(backupPath, targetPath);
            }
            else if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }
    }

    private static void PruneEmptyOwnedDirectories(
        string targetDirectory,
        IEnumerable<string> staleFiles)
    {
        string fullTarget = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(targetDirectory));
        string[] directories = staleFiles
            .Select(path => Path.GetDirectoryName(
                ResolveOwnedTarget(fullTarget, path)))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path!.Length)
            .ToArray()!;
        foreach (string directory in directories)
        {
            string current = directory;
            while (!string.Equals(
                current,
                fullTarget,
                StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(current)
                    || Directory.EnumerateFileSystemEntries(current).Any())
                {
                    break;
                }

                Directory.Delete(current);
                current = Path.GetDirectoryName(current) ?? fullTarget;
            }
        }
    }

    private static void StartInstalledApplication(
        string executablePath,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            UseShellExecute = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Windows did not reopen SpinFOURKAYYY after the update.");
    }

    private static Version ParseVersion(string value, string description)
    {
        if (!Version.TryParse(value, out Version? parsed)
            || parsed.Major < 0
            || parsed.Minor < 0
            || parsed.Build < 0
            || parsed.Revision >= 0
            || !string.Equals(
                parsed.ToString(3),
                value,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {description} '{value}' is invalid.");
        }

        return parsed;
    }
}
