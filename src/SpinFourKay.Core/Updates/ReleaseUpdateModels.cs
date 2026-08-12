namespace SpinFourKay.Core.Updates;

public sealed record ReleaseUpdateInfo(
    Version Version,
    string TagName,
    Uri ReleasePage,
    Uri PackageDownload,
    Uri ChecksumDownload,
    string PackageFileName,
    long PackageSize,
    string PackageSha256);

public sealed record ReleaseUpdateCheck(
    Version CurrentVersion,
    ReleaseUpdateInfo LatestRelease)
{
    public bool IsUpdateAvailable => LatestRelease.Version > CurrentVersion;
}

public sealed record PreparedReleaseUpdate(
    ReleaseUpdateInfo Release,
    string StagedAppDirectory,
    string RequestDirectory,
    string PackageSha256);

public sealed record ReleasePackageFile
{
    public required string Path { get; init; }

    public long Size { get; init; }

    public required string Sha256 { get; init; }
}

public sealed record ReleasePackageManifest
{
    public required string Product { get; init; }

    public required string Version { get; init; }

    public required string Runtime { get; init; }

    public IReadOnlyList<ReleasePackageFile> Files { get; init; } = [];
}

public sealed record UpdateApplyRequest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required string ExpectedVersion { get; init; }

    public required string PreviousVersion { get; init; }

    public int PreviousProcessId { get; init; }

    public required string PreviousExecutablePath { get; init; }

    public required string TargetDirectory { get; init; }

    public required string StagedAppDirectory { get; init; }
}

public sealed record UpdateApplyResult(
    string InstalledVersion,
    string InstalledExecutablePath,
    string RollbackDirectory);
