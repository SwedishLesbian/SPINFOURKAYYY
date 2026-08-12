[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.5',

    [switch]$SkipSelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'publish'))
$dotnetArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'dotnet'))
$stageRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "SpinFOURKAYYY-$Version-$Runtime"))
$zipPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "SpinFOURKAYYY-$Version-$Runtime.zip"))
$checksumPath = "$zipPath.sha256"
$solutionPath = Join-Path $projectRoot 'SpinFOURKAYYY.sln'
$appProject = Join-Path $projectRoot 'src\SpinFourKay.App\SpinFourKay.App.csproj'
$selfTestProject = Join-Path $projectRoot 'src\SpinFourKay.SelfTest\SpinFourKay.SelfTest.csproj'
$magpieArchive = Join-Path $projectRoot 'vendor\Magpie-v0.12.1-x64.zip'
$magpieSourceRoot = Join-Path $projectRoot 'vendor\Magpie-source-v0.12.1'
$magpieArchiveUrl = 'https://github.com/Blinue/Magpie/releases/download/v0.12.1/Magpie-v0.12.1-x64.zip'
$magpieSourceUrl = 'https://github.com/Blinue/Magpie.git'
$microsoftUiXamlLicense = Join-Path $projectRoot 'legal\Microsoft.UI.Xaml-2.8.7-LICENSE.txt'
$expectedMagpieArchiveHash = '8BC8BC233438F546B7996B00B21D7376F4F7D3D8A4940E6A8800BABD2225B2DE'
$expectedMagpieCommit = '664e0f4c8a9aca4e6efb0e37f52be6dc62414f7b'
$expectedDotNetRuntimeVersion = '9.0.18'
$expectedDotNetRuntimeLicenseHash = 'D7A68596AB69B06F51CA278A6545148E4269A9381C26D597C13DF5D88E08CF5B'
$expectedDotNetRuntimeNoticesHash = '40686C6447A7D5B5D3693068E4571B5F483D7ED335AEEE773EF662440DE4C5D5'
$expectedWindowsDesktopLicenseHash = 'A89886665765362EB77E0F8E26602C924520041D1711B2EEDC136434FE4D01AB'
$expectedMicrosoftUiXamlLicenseNormalizedHash = 'C9B411CE03FA461B4424A31502F30DC1798DA45266EBAED343FF5FB1629C64DF'
$numericVersion = [regex]::Match($Version, '^\d+\.\d+\.\d+').Value
$windowsVersion = "$numericVersion.0"

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Candidate,

        [Parameter(Mandatory)]
        [string]$Parent
    )

    $candidateFull = [System.IO.Path]::GetFullPath($Candidate)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $candidateFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing an operation outside '$parentFull': '$candidateFull'"
    }
}

function Reset-Directory {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Assert-ChildPath -Candidate $Path -Parent $projectRoot
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Get-StreamSha256 {
    param(
        [Parameter(Mandatory)]
        [System.IO.Stream]$Stream
    )

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($Stream)
        return ([BitConverter]::ToString($hashBytes)).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Expand-VerifiedZip {
    param(
        [Parameter(Mandatory)]
        [string]$ArchivePath,

        [Parameter(Mandatory)]
        [string]$DestinationPath
    )

    Assert-ChildPath -Candidate $DestinationPath -Parent $stageRoot
    if (Test-Path -LiteralPath $DestinationPath) {
        throw "Refusing to extract over an existing directory: $DestinationPath"
    }

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    $destinationPrefix = [System.IO.Path]::GetFullPath($DestinationPath).TrimEnd('\') + '\'
    $seenTargets = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $verifiedFiles = New-Object System.Collections.Generic.List[object]

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrWhiteSpace($entry.FullName)) {
                continue
            }

            $relativePath = $entry.FullName.Replace('/', '\')
            if ([System.IO.Path]::IsPathRooted($relativePath) -or $relativePath.Contains(':')) {
                throw "The verified archive contains an unsafe rooted entry: '$($entry.FullName)'"
            }

            $targetPath = [System.IO.Path]::GetFullPath((Join-Path $DestinationPath $relativePath))
            if (-not $targetPath.StartsWith($destinationPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "The verified archive contains an entry outside its destination: '$($entry.FullName)'"
            }

            if (-not $seenTargets.Add($targetPath)) {
                throw "The verified archive contains a duplicate Windows path: '$($entry.FullName)'"
            }

            if ([string]::IsNullOrEmpty($entry.Name)) {
                New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
                continue
            }

            $targetDirectory = [System.IO.Path]::GetDirectoryName($targetPath)
            New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetPath, $false)

            $entryStream = $entry.Open()
            try {
                $entryHash = Get-StreamSha256 -Stream $entryStream
            }
            finally {
                $entryStream.Dispose()
            }

            $extractedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash
            if ($entryHash -ne $extractedHash) {
                throw "Extracted Magpie file failed verification: '$($entry.FullName)'"
            }

            $verifiedFiles.Add([PSCustomObject]@{
                Path = $entry.FullName.Replace('\', '/')
                Size = $entry.Length
                Sha256 = $entryHash
            })
        }
    }
    finally {
        $archive.Dispose()
    }

    return $verifiedFiles.ToArray()
}

function Find-NuGetGlobalPackagesRoot {
    $candidateRoots = New-Object System.Collections.Generic.List[string]

    $assetsPath = Join-Path $dotnetArtifactsRoot 'obj\SpinFourKay.App\project.assets.json'
    if (Test-Path -LiteralPath $assetsPath -PathType Leaf) {
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
        foreach ($property in $assets.packageFolders.PSObject.Properties) {
            $candidateRoots.Add([System.IO.Path]::GetFullPath($property.Name))
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        $candidateRoots.Add([System.IO.Path]::GetFullPath($env:NUGET_PACKAGES))
    }

    $output = & $script:dotnetPath nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not locate the NuGet global-packages directory.'
    }

    foreach ($line in $output) {
        if ([string]$line -match '^\s*global-packages:\s*(.+?)\s*$') {
            $candidateRoots.Add([System.IO.Path]::GetFullPath($Matches[1]))
        }
    }

    foreach ($candidateRoot in $candidateRoots | Select-Object -Unique) {
        $runtimeLicense = Join-Path $candidateRoot `
            "microsoft.netcore.app.runtime.win-x64\$expectedDotNetRuntimeVersion\LICENSE.TXT"
        $desktopLicense = Join-Path $candidateRoot `
            "microsoft.windowsdesktop.app.runtime.win-x64\$expectedDotNetRuntimeVersion\LICENSE"
        if ((Test-Path -LiteralPath $runtimeLicense -PathType Leaf) -and
            (Test-Path -LiteralPath $desktopLicense -PathType Leaf)) {
            return $candidateRoot
        }
    }

    throw "The restored .NET $expectedDotNetRuntimeVersion runtime package licenses could not be located."
}

function Assert-FileHash {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$ExpectedHash,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }

    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
    if ($actualHash -ne $ExpectedHash) {
        throw "$Description failed SHA-256 verification. Expected $ExpectedHash; found $actualHash."
    }
}

function Assert-NormalizedTextHash {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$ExpectedHash,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }

    $text = [System.IO.File]::ReadAllText($Path)
    $normalizedLines = $text.Replace("`r`n", "`n").Replace("`r", "`n").Split("`n") |
        ForEach-Object { $_.TrimEnd() }
    $normalizedText = ($normalizedLines -join "`n").TrimEnd("`n")
    $normalizedBytes = [System.Text.Encoding]::UTF8.GetBytes($normalizedText)
    $stream = New-Object System.IO.MemoryStream(, $normalizedBytes)
    try {
        $actualHash = Get-StreamSha256 -Stream $stream
    }
    finally {
        $stream.Dispose()
    }

    if ($actualHash -ne $ExpectedHash) {
        throw "$Description failed normalized SHA-256 verification. Expected $ExpectedHash; found $actualHash."
    }
}

function Find-DotNet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $bundledCandidate = [System.IO.Path]::GetFullPath(
        (Join-Path $projectRoot '..\SpinTextureProject\.dotnet\dotnet.exe')
    )
    if (Test-Path -LiteralPath $bundledCandidate) {
        return $bundledCandidate
    }

    throw '.NET SDK 9.x was not found. Install it from https://dotnet.microsoft.com/download/dotnet/9.0.'
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $script:dotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

function Find-Git {
    $command = Get-Command git -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @(
        'C:\Program Files\Git\cmd\git.exe',
        'C:\Program Files\Git\bin\git.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw 'Git is required to verify and archive the corresponding Magpie source.'
}

function Initialize-PinnedMagpieInputs {
    $vendorRoot = Join-Path $projectRoot 'vendor'
    if (-not (Test-Path -LiteralPath $vendorRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $vendorRoot -Force | Out-Null
    }

    if (-not (Test-Path -LiteralPath $magpieArchive -PathType Leaf)) {
        $downloadPath = "$magpieArchive.download"
        if (Test-Path -LiteralPath $downloadPath) {
            Remove-Item -LiteralPath $downloadPath -Force
        }

        Write-Host 'Downloading pinned Magpie v0.12.1 x64 release archive...'
        Invoke-WebRequest `
            -UseBasicParsing `
            -Uri $magpieArchiveUrl `
            -OutFile $downloadPath
        $downloadHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $downloadPath).Hash
        if ($downloadHash -ne $expectedMagpieArchiveHash) {
            Remove-Item -LiteralPath $downloadPath -Force
            throw "The downloaded Magpie archive failed SHA-256 verification. Expected $expectedMagpieArchiveHash; found $downloadHash."
        }

        Move-Item -LiteralPath $downloadPath -Destination $magpieArchive
    }

    if (-not (Test-Path -LiteralPath $magpieSourceRoot -PathType Container)) {
        $sourceDownloadPath = "$magpieSourceRoot.download"
        if (Test-Path -LiteralPath $sourceDownloadPath) {
            Assert-ChildPath -Candidate $sourceDownloadPath -Parent $projectRoot
            Remove-Item -LiteralPath $sourceDownloadPath -Recurse -Force
        }

        Write-Host 'Cloning pinned Magpie v0.12.1 corresponding source...'
        & $script:gitPath clone --no-checkout $magpieSourceUrl $sourceDownloadPath
        if ($LASTEXITCODE -ne 0) {
            throw "Could not clone Magpie corresponding source (exit code $LASTEXITCODE)."
        }

        $magpieSafeDirectoryArgument = "safe.directory=$sourceDownloadPath"
        & $script:gitPath -c $magpieSafeDirectoryArgument `
            -C $sourceDownloadPath checkout --detach $expectedMagpieCommit
        if ($LASTEXITCODE -ne 0) {
            throw "Could not check out pinned Magpie commit $expectedMagpieCommit."
        }

        $downloadedCommit = & $script:gitPath -c $magpieSafeDirectoryArgument `
            -C $sourceDownloadPath rev-parse HEAD
        $downloadedCommit = [string]($downloadedCommit | Select-Object -First 1)
        $downloadedCommit = $downloadedCommit.Trim()
        if ($LASTEXITCODE -ne 0 -or $downloadedCommit -ne $expectedMagpieCommit) {
            throw "The downloaded Magpie source did not resolve to pinned commit $expectedMagpieCommit."
        }

        Move-Item -LiteralPath $sourceDownloadPath -Destination $magpieSourceRoot
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $magpieSourceRoot '.git'))) {
        throw "The Magpie source directory exists but is not a Git checkout: $magpieSourceRoot"
    }
}

Write-Host 'SpinFOURKAYYY release build' -ForegroundColor Cyan
Write-Host "  Project:       $projectRoot"
Write-Host "  Configuration: $Configuration"
Write-Host "  Runtime:       $Runtime"
Write-Host "  Version:       $Version"

$dotnetPath = Find-DotNet
$gitPath = Find-Git

$sdkVersion = (& $dotnetPath --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith('9.', [System.StringComparison]::Ordinal)) {
    throw "SpinFOURKAYYY requires .NET SDK 9.x; found '$sdkVersion'."
}

Initialize-PinnedMagpieInputs

foreach ($requiredPath in @(
    $solutionPath,
    $appProject,
    $magpieArchive,
    $magpieSourceRoot,
    (Join-Path $magpieSourceRoot 'LICENSE'),
    $microsoftUiXamlLicense
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required build input is missing: $requiredPath"
    }
}

$actualMagpieArchiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $magpieArchive).Hash
if ($actualMagpieArchiveHash -ne $expectedMagpieArchiveHash) {
    throw "The Magpie release archive failed SHA-256 verification. Expected $expectedMagpieArchiveHash; found $actualMagpieArchiveHash."
}
Assert-NormalizedTextHash `
    -Path $microsoftUiXamlLicense `
    -ExpectedHash $expectedMicrosoftUiXamlLicenseNormalizedHash `
    -Description 'Microsoft.UI.Xaml 2.8.7 redistribution license'

$magpieSafeDirectoryArgument = "safe.directory=$magpieSourceRoot"
$commitOutput = & $gitPath -c $magpieSafeDirectoryArgument `
    -C $magpieSourceRoot rev-parse HEAD
$commitExitCode = $LASTEXITCODE
$actualMagpieCommit = [string]($commitOutput | Select-Object -First 1)
$actualMagpieCommit = $actualMagpieCommit.Trim()
if ($commitExitCode -ne 0 -or $actualMagpieCommit -ne $expectedMagpieCommit) {
    throw "The Magpie source checkout is not the pinned v0.12.1 commit. Expected $expectedMagpieCommit; found $actualMagpieCommit."
}

Reset-Directory -Path $publishRoot
Reset-Directory -Path $dotnetArtifactsRoot
if (Test-Path -LiteralPath $stageRoot) {
    Assert-ChildPath -Candidate $stageRoot -Parent $artifactRoot
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Assert-ChildPath -Candidate $zipPath -Parent $artifactRoot
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $checksumPath) {
    Assert-ChildPath -Candidate $checksumPath -Parent $artifactRoot
    Remove-Item -LiteralPath $checksumPath -Force
}

Invoke-DotNet -Arguments @(
    'restore', $solutionPath,
    '--artifacts-path', $dotnetArtifactsRoot,
    '--nologo'
)
Invoke-DotNet -Arguments @(
    'restore', $appProject,
    '--runtime', $Runtime,
    '--artifacts-path', $dotnetArtifactsRoot,
    "-p:RuntimeFrameworkVersion=$expectedDotNetRuntimeVersion",
    '-p:PublishReadyToRun=true',
    '--nologo'
)
Invoke-DotNet -Arguments @(
    'build', $solutionPath,
    '--configuration', $Configuration,
    '--artifacts-path', $dotnetArtifactsRoot,
    '--no-restore',
    "-p:RuntimeFrameworkVersion=$expectedDotNetRuntimeVersion",
    '--nologo'
)

if (-not $SkipSelfTest) {
    if (-not (Test-Path -LiteralPath $selfTestProject)) {
        throw "Self-test project is missing: $selfTestProject"
    }

    Invoke-DotNet -Arguments @(
        'run',
        '--project', $selfTestProject,
        '--configuration', $Configuration,
        '--artifacts-path', $dotnetArtifactsRoot,
        '--no-build',
        '--nologo'
    )
}

Invoke-DotNet -Arguments @(
    'publish',
    $appProject,
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--artifacts-path', $dotnetArtifactsRoot,
    '--self-contained', 'true',
    '--no-restore',
    '--nologo',
    "-p:RuntimeFrameworkVersion=$expectedDotNetRuntimeVersion",
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:PublishReadyToRun=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    "-p:Version=$Version",
    "-p:AssemblyVersion=$windowsVersion",
    "-p:FileVersion=$windowsVersion",
    "-p:InformationalVersion=$Version",
    '--output', $publishRoot
)

New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
Get-ChildItem -LiteralPath $publishRoot -Force |
    Copy-Item -Destination $stageRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination $stageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $stageRoot -Force

$engineRoot = Join-Path $stageRoot 'Engine'
$magpieStageRoot = Join-Path $engineRoot 'Magpie'
$thirdPartyLicensesRoot = Join-Path $stageRoot 'ThirdPartyLicenses'
$thirdPartySourceRoot = Join-Path $stageRoot 'ThirdPartySource'
New-Item -ItemType Directory -Path $engineRoot, $thirdPartyLicensesRoot, $thirdPartySourceRoot -Force | Out-Null
$magpieVerifiedFiles = Expand-VerifiedZip -ArchivePath $magpieArchive -DestinationPath $magpieStageRoot
if (-not (Test-Path -LiteralPath (Join-Path $magpieStageRoot 'Magpie.exe') -PathType Leaf)) {
    throw 'The verified Magpie archive did not contain Magpie.exe at its expected path.'
}
if (-not (Test-Path -LiteralPath (Join-Path $magpieStageRoot 'Microsoft.UI.Xaml.dll') -PathType Leaf)) {
    throw 'The verified Magpie archive did not contain Microsoft.UI.Xaml.dll at its expected path.'
}

foreach ($requiredMagpieAsset in @(
    'resources.pri',
    'effects\FSR\FSR_EASU.hlsl',
    'effects\FSR\FSR_RCAS.hlsl',
    'effects\NIS\NIS.hlsl',
    'effects\NIS\NIS_Scaler.hlsli',
    'effects\NIS\Coef_Scale.dds',
    'effects\NIS\Coef_USM.dds',
    'effects\Lanczos.hlsl',
    'effects\Nearest.hlsl',
    'effects\FXAA\FXAA.hlsli',
    'effects\FXAA\FXAA_High.hlsl',
    'effects\SMAA\SMAA.hlsli',
    'effects\SMAA\SMAA_High.hlsl',
    'effects\SMAA\AreaTex.dds',
    'effects\SMAA\SearchTex.dds'
)) {
    $requiredMagpiePath = Join-Path $magpieStageRoot $requiredMagpieAsset
    if (-not (Test-Path -LiteralPath $requiredMagpiePath -PathType Leaf)) {
        throw "The verified Magpie archive is missing required runtime asset '$requiredMagpieAsset'."
    }
}

Copy-Item -LiteralPath (Join-Path $magpieSourceRoot 'LICENSE') `
    -Destination (Join-Path $thirdPartyLicensesRoot 'Magpie-GPL-3.0.txt') -Force
Copy-Item -LiteralPath $microsoftUiXamlLicense `
    -Destination (Join-Path $thirdPartyLicensesRoot 'Microsoft.UI.Xaml-2.8.7-LICENSE.txt') -Force

$nugetPackagesRoot = Find-NuGetGlobalPackagesRoot
$dotNetRuntimePackageRoot = Join-Path $nugetPackagesRoot `
    "microsoft.netcore.app.runtime.win-x64\$expectedDotNetRuntimeVersion"
$windowsDesktopPackageRoot = Join-Path $nugetPackagesRoot `
    "microsoft.windowsdesktop.app.runtime.win-x64\$expectedDotNetRuntimeVersion"
$dotNetRuntimeLicense = Join-Path $dotNetRuntimePackageRoot 'LICENSE.TXT'
$dotNetRuntimeNotices = Join-Path $dotNetRuntimePackageRoot 'THIRD-PARTY-NOTICES.TXT'
$windowsDesktopLicense = Join-Path $windowsDesktopPackageRoot 'LICENSE'

Assert-FileHash -Path $dotNetRuntimeLicense `
    -ExpectedHash $expectedDotNetRuntimeLicenseHash `
    -Description ".NET Runtime $expectedDotNetRuntimeVersion license"
Assert-FileHash -Path $dotNetRuntimeNotices `
    -ExpectedHash $expectedDotNetRuntimeNoticesHash `
    -Description ".NET Runtime $expectedDotNetRuntimeVersion third-party notices"
Assert-FileHash -Path $windowsDesktopLicense `
    -ExpectedHash $expectedWindowsDesktopLicenseHash `
    -Description ".NET WindowsDesktop Runtime $expectedDotNetRuntimeVersion license"

Copy-Item -LiteralPath $dotNetRuntimeLicense `
    -Destination (Join-Path $thirdPartyLicensesRoot `
        "DotNet-Runtime-$expectedDotNetRuntimeVersion-MIT.txt") -Force
Copy-Item -LiteralPath $dotNetRuntimeNotices `
    -Destination (Join-Path $thirdPartyLicensesRoot `
        "DotNet-Runtime-$expectedDotNetRuntimeVersion-THIRD-PARTY-NOTICES.txt") -Force
Copy-Item -LiteralPath $windowsDesktopLicense `
    -Destination (Join-Path $thirdPartyLicensesRoot `
        "DotNet-WindowsDesktop-$expectedDotNetRuntimeVersion-MIT.txt") -Force

$sourceArchive = Join-Path $thirdPartySourceRoot 'Magpie-v0.12.1-source.zip'
& $gitPath -c $magpieSafeDirectoryArgument `
    -C $magpieSourceRoot archive --format=zip "--output=$sourceArchive" HEAD
if ($LASTEXITCODE -ne 0) {
    throw "Could not create the corresponding Magpie source archive (exit code $LASTEXITCODE)."
}

$magpieBinaryManifest = [ordered]@{
    Component = 'Magpie'
    Version = '0.12.1'
    OfficialReleaseArchive = 'Magpie-v0.12.1-x64.zip'
    OfficialReleaseArchiveSha256 = $actualMagpieArchiveHash
    Extraction = 'Every staged file was extracted directly from the verified official archive and SHA-256 compared with its archive entry.'
    Files = @($magpieVerifiedFiles | Sort-Object Path)
}
Write-Utf8NoBom `
    -Path (Join-Path $thirdPartySourceRoot 'Magpie-v0.12.1-binary-manifest.json') `
    -Content ($magpieBinaryManifest | ConvertTo-Json -Depth 5)

$releaseFiles = Get-ChildItem -LiteralPath $stageRoot -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        [PSCustomObject]@{
            Path = $_.FullName.Substring($stageRoot.Length + 1).Replace('\', '/')
            Size = $_.Length
            Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
        }
    }

$releaseManifest = [ordered]@{
    Product = 'SpinFOURKAYYY'
    Version = $Version
    Runtime = $Runtime
    BuiltUtc = [DateTime]::UtcNow.ToString('o')
    DotNetSdk = $sdkVersion
    DotNetRuntime = $expectedDotNetRuntimeVersion
    MagpieVersion = '0.12.1'
    MagpieReleaseArchiveSha256 = $actualMagpieArchiveHash
    MagpieSourceCommit = $actualMagpieCommit
    Files = @($releaseFiles)
}
$releaseManifest |
    ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $stageRoot 'release-manifest.json') -Encoding UTF8

Compress-Archive -LiteralPath $stageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
$checksumLine = "$($zipHash.ToLowerInvariant()) *$([System.IO.Path]::GetFileName($zipPath))"
Write-Utf8NoBom -Path $checksumPath -Content ($checksumLine + [Environment]::NewLine)

Write-Host ''
Write-Host 'Release built and verified.' -ForegroundColor Green
Write-Host "  Folder: $stageRoot"
Write-Host "  ZIP:    $zipPath"
Write-Host "  Checksum sidecar: $checksumPath"
Write-Host "  SHA256: $zipHash"
