[CmdletBinding()]
param(
    [ValidatePattern('^v\d+\.\d+\.\d+([.-][A-Za-z0-9.]+)?$')][string]$Version,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs/Petal'),
    [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'

function Assert-SupportedWindows {
    if (-not [Environment]::Is64BitOperatingSystem -or
        $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or
        $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') {
        throw 'This release requires x64 Windows.'
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}

function Get-PetalRelease {
    param([string]$Version)

    $repository = 'Aayush9029/petal-windows'
    $endpoint = if ($Version) { "tags/$Version" } else { 'latest' }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/$endpoint" -Headers @{
        Accept = 'application/vnd.github+json'
        'User-Agent' = 'Petal-Installer'
    }
    if ($release.tag_name -notmatch '^v\d+\.\d+\.\d+([.-][A-Za-z0-9.]+)?$') {
        throw 'Unexpected release version.'
    }
    $archiveAsset = @($release.assets | Where-Object name -eq 'Petal-win-x64.zip')
    $checksumAsset = @($release.assets | Where-Object name -eq 'SHA256SUMS.txt')
    if ($archiveAsset.Count -ne 1 -or $checksumAsset.Count -ne 1) {
        throw 'This release is missing its package or checksum.'
    }
    # Accept only the expected assets from this repository's release.
    $baseUrl = "https://github.com/$repository/releases/download/$($release.tag_name)/"
    if ($archiveAsset[0].browser_download_url -cne ($baseUrl + 'Petal-win-x64.zip') -or
        $checksumAsset[0].browser_download_url -cne ($baseUrl + 'SHA256SUMS.txt')) {
        throw 'Unexpected download location.'
    }
    return [PSCustomObject]@{
        Version = $release.tag_name
        ArchiveUrl = $archiveAsset[0].browser_download_url
        ChecksumUrl = $checksumAsset[0].browser_download_url
        Digest = $archiveAsset[0].digest
    }
}

function Save-PetalPackage {
    param($Release, [string]$Stage)

    $zip = Join-Path $stage 'Petal-win-x64.zip'
    Write-Host "Downloading Petal $($Release.Version)..."
    Invoke-WebRequest -UseBasicParsing -Uri $Release.ArchiveUrl -OutFile $zip
    $checksumFile = Join-Path $stage 'SHA256SUMS.txt'
    Invoke-WebRequest -UseBasicParsing -Uri $Release.ChecksumUrl -OutFile $checksumFile
    return $zip
}

function Test-PetalChecksum {
    param([string]$Zip, [string]$ChecksumFile, [string]$Digest)

    $checksums = Get-Content -LiteralPath $checksumFile -Raw
    if ($checksums -notmatch '(?m)^([a-fA-F0-9]{64})\s+\*?Petal-win-x64\.zip\s*$') { throw 'Missing SHA-256 checksum.' }
    $expected = $Matches[1].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw 'Checksum mismatch. Nothing was installed.' }
    if ($Digest -and $Digest -ne "sha256:$actual") { throw 'GitHub asset digest mismatch.' }
    return $actual
}

function Expand-PetalPackage {
    param([string]$Zip, [string]$Stage)

    # Validate all paths before extraction to prevent writes outside the package folder.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $package = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $package.Entries) {
            if ($entry.FullName -match '(^[/\\]|(^|[/\\])\.\.([/\\]|$)|:)' -or
                -not $entry.FullName.StartsWith('Petal-win-x64/')) {
                throw 'Unsafe archive entry.'
            }
        }
    } finally {
        $package.Dispose()
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $stage
    $unpacked = Join-Path $stage 'Petal-win-x64'
    if (-not (Test-Path -LiteralPath (Join-Path $unpacked 'Petal.exe'))) { throw 'The package is incomplete.' }
    return $unpacked
}

function Set-PetalDownloadMetadata {
    param([string]$Unpacked, [string]$DownloadUrl, [string]$Checksum)

    # Preserve Internet-zone provenance for normal Windows reputation checks.
    Get-ChildItem -LiteralPath $unpacked -Recurse -File | Where-Object Extension -in '.exe','.dll','.ps1' | ForEach-Object {
        Set-Content -LiteralPath $_.FullName -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`nHostUrl=$DownloadUrl"
    }
    Set-Content -LiteralPath (Join-Path $unpacked 'release.sha256') -Value $Checksum
}

function Remove-PetalStage {
    param([string]$Stage, [string]$Root)

    # Restrict cleanup to the temporary directory created inside the install root.
    $resolved = [IO.Path]::GetFullPath($stage)
    if ($resolved.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved).StartsWith('.install-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Install-Petal {
    param([string]$Version, [string]$InstallRoot, [switch]$NoLaunch)

    Assert-SupportedWindows
    $release = Get-PetalRelease -Version $Version
    $root = [IO.Path]::GetFullPath($InstallRoot)
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $stage = Join-Path $root ('.install-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        $zip = Save-PetalPackage -Release $release -Stage $stage
        $checksum = Test-PetalChecksum -Zip $zip -ChecksumFile (Join-Path $stage 'SHA256SUMS.txt') -Digest $release.Digest
        $unpacked = Expand-PetalPackage -Zip $zip -Stage $stage
        Set-PetalDownloadMetadata -Unpacked $unpacked -DownloadUrl $release.ArchiveUrl -Checksum $checksum
        $destination = Join-Path $root $release.Version
        if (Test-Path -LiteralPath $destination) {
            throw "This version is already installed at $destination. Run Petal.exe there, or choose another InstallRoot."
        }
        Move-Item -LiteralPath $unpacked -Destination $destination
        $executable = Join-Path $destination 'Petal.exe'
        Write-Host "Installed Petal at $destination"
        if (-not $NoLaunch) { Start-Process -FilePath $executable }
    } finally {
        Remove-PetalStage -Stage $stage -Root $root
    }
}

Install-Petal -Version $Version -InstallRoot $InstallRoot -NoLaunch:$NoLaunch
